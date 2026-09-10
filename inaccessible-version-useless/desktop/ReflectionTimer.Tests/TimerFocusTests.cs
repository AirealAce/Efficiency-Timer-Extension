using System.Reflection;
using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private sealed class ShortcutClock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }
    private static void PressPeriod(TimerApplication app)
    {
        Is(CompactFocusShortcut(app).Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactFocusId));
        Application.DoEvents();
    }
    private static void AssertDurationSelected(DurationControl duration, string part)
    {
        var input = CompactPart(duration, part);
        Is(input.Visible && input.Enabled && input.ContainsFocus);
        var edit = input.Controls.OfType<TextBox>().Single(); Equal(edit.Text, edit.SelectedText);
    }
    private static void TestTimerFocusShortcut()
    {
        TestMainWindowToggleShortcut();
        Test("period uses Ctrl Alt NoRepeat registration and releases exactly once", () => {
            var api = new FakeHotKey(); var used = 0;
            var shortcut = new GlobalShortcut(() => used++, api, GlobalShortcut.CompactFocusKey, GlobalShortcut.CompactFocusId);
            var call = api.Registrations.Single(); Equal(0xBEu, call.Key); Equal(0x4003u, call.Modifiers); Equal(0x5257, call.Id);
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.ReflectionFocusId));
            Is(!shortcut.Dispatch(0, call.Id)); Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, call.Id)); Equal(1, used);
            shortcut.Dispose(); shortcut.Dispose(); Equal((call.Window, call.Id), api.Unregistrations.Single());
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, call.Id));
        });
        Test("period double-press uses monotonic 800 ms pairs, expiry and explicit reset", () => {
            var clock = new ShortcutClock(); var presses = new ConsecutiveShortcutPresses(clock);
            Is(!presses.Press()); clock.Milliseconds += 800; Is(presses.Press());
            Is(!presses.Press()); clock.Milliseconds += 801; Is(!presses.Press());
            clock.Milliseconds += 100; Is(presses.Press()); Is(!presses.Press());
            presses.Reset(); Is(!presses.Press()); clock.Milliseconds -= 100; Is(!presses.Press());
            clock.Milliseconds += 100; Is(presses.Press());
        });
        foreach (var example in new[] { (3730, "Hours"), (150, "Minutes"), (30, "Seconds"), (0, "Hours") })
            Test("period selects compact then full Timer view and preserves shared draft: " + example.Item2 + "/" + example.Item1, () => {
                var clock = new ShortcutClock();
                WithEndEarlyApp((app, _) => {
                    var (main, tabs, full, _) = TimerShortcutControls(app); tabs.SelectedIndex = 3; main.Hide();
                    var parts = new[] { "Hours", "Minutes", "Seconds" };
                    var values = new[] { example.Item1 / 3600, example.Item1 / 60 % 60, example.Item1 % 60 };
                    for (var i = 0; i < 3; i++) CompactPart(full, parts[i]).Text = values[i].ToString();
                    var before = app.Engine.Snapshot.Timer;
                    PressPeriod(app);
                    var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                    AssertDurationSelected(mini.Duration, example.Item2); Is(!main.Visible); Equal(3, tabs.SelectedIndex);
                    clock.Milliseconds += 200; PressPeriod(app);
                    Is(main.Visible); Equal(0, tabs.SelectedIndex); AssertDurationSelected(full, example.Item2);
                    Is(mini.Visible && app.Engine.Snapshot.ShowFloatingTimer); Equal(example.Item1, full.Seconds);
                    Equal(before, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count); Equal(0, app.Engine.Snapshot.Outbox.Count);
                    // A third press begins another pair, not another full-view request.
                    clock.Milliseconds += 200; PressPeriod(app); AssertDurationSelected(mini.Duration, example.Item2);
                    CompactPart(mini.Duration, "Seconds").Text = "59"; Equal("59", CompactPart(full, "Seconds").Text);
                }, shortcutClock: clock);
            });
        Test("a late period stays compact, including from time-only, and the next quick press restores minimized full view", () => {
            var clock = new ShortcutClock();
            WithEndEarlyApp((app, _) => {
                var (main, tabs, full, _) = TimerShortcutControls(app); full.LoadSeconds(150);
                PressPeriod(app); var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                Is(mini.ShrinkToTimeOnly());
                clock.Milliseconds += 801; PressPeriod(app); AssertDurationSelected(mini.Duration, "Minutes"); Is(!main.Visible);
                main.WindowState = FormWindowState.Minimized; clock.Milliseconds += 100; PressPeriod(app);
                Is(main.Visible && main.WindowState != FormWindowState.Minimized); Equal(0, tabs.SelectedIndex); AssertDurationSelected(full, "Minutes");
            }, shortcutClock: clock);
        });
        foreach (var chord in new[] { ("focusShortcut", GlobalShortcut.HotKeyId), ("endEarlyShortcut", GlobalShortcut.EndEarlyId),
            ("compactShortcut", GlobalShortcut.CompactId), ("reflectionFocusShortcut", GlobalShortcut.ReflectionFocusId) })
            Test("another registered shortcut interrupts the period pair: " + chord.Item1, () => {
                var clock = new ShortcutClock();
                WithEndEarlyApp((app, _) => {
                    var (main, _, _, _) = TimerShortcutControls(app); PressPeriod(app);
                    var other = (GlobalShortcut)typeof(TimerApplication).GetField(chord.Item1, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
                    Is(other.Dispatch(GlobalShortcut.HotKeyMessage, chord.Item2)); Application.DoEvents(); main.Hide();
                    clock.Milliseconds += 100; PressPeriod(app);
                    var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                    AssertDurationSelected(mini.Duration, "Minutes"); Is(!main.Visible);
                }, shortcutClock: clock);
            });
        foreach (var running in new[] { false, true }) Test("period focus preserves paused or running session and its auto-start cutoff: " + running, () => {
            var clock = new ShortcutClock();
            WithEndEarlyApp((app, _) => {
                var (main, _, full, _) = TimerShortcutControls(app);
                app.Engine.Start(150, true, 0, app.Engine.Now + 3600000); if (!running) app.Engine.Pause();
                var before = app.Engine.Snapshot.Timer;
                PressPeriod(app); var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                AssertDurationSelected(mini.Duration, "Minutes");
                clock.Milliseconds += 100; PressPeriod(app); AssertDurationSelected(full, "Minutes");
                Equal(running, full.ReadOnly); Equal(before, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count);
                if (running) {
                    var minutes = CompactPart(full, "Minutes"); var original = minutes.Text;
                    minutes.UpButton(); minutes.DownButton(); Equal(original, minutes.Text);
                    Is(DispatchCommandKey(full, Keys.Enter)); Equal(before, app.Engine.Snapshot.Timer);
                    main.Render(app.Engine.Snapshot); AssertDurationSelected(full, "Minutes");
                    app.Engine.Start(300, true, 0); Application.DoEvents(); Is(!full.Enabled);
                    app.Engine.Pause(); Application.DoEvents(); Is(full.Enabled && !full.ReadOnly);
                }
            }, shortcutClock: clock);
        });
        Test("period does not reopen a postponed reflection or change its saved draft", () => {
            var prompt = new ReflectionPrompt(Guid.NewGuid(), DateTimeOffset.Now.ToUnixTimeMilliseconds(), 30, 0, false, "Synthetic postponed draft");
            var clock = new ShortcutClock();
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app); var popup = Application.OpenForms.OfType<ReflectionWindow>().Single();
                Descendants(popup).OfType<Button>().Single(x => x.Text == "Later").PerformClick(); Application.DoEvents();
                PressPeriod(app); clock.Milliseconds += 100; PressPeriod(app);
                Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count()); Equal(prompt, app.Engine.Snapshot.Prompts.Single());
                Equal(0, app.Engine.Snapshot.Outbox.Count);
            }, new AppState { ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 }, Prompts = [prompt] }, clock);
        });
        Test("period cannot redirect full-view typing behind a disabled modal owner", () => {
            var clock = new ShortcutClock();
            WithEndEarlyApp((app, _) => {
                var (main, tabs, _, _) = TimerShortcutControls(app); tabs.SelectedIndex = 3;
                PressPeriod(app); main.Enabled = false; clock.Milliseconds += 100; PressPeriod(app);
                Equal(3, tabs.SelectedIndex); Is(!main.Enabled); Equal(0, app.Engine.Snapshot.Prompts.Count);
                main.Enabled = true;
            }, shortcutClock: clock);
        });
        Test("period conflict is reported and disposal releases only successfully registered chords", () => {
            var api = new FakeHotKey { BlockedKey = GlobalShortcut.CompactFocusKey };
            WithEndEarlyApp((app, _) => {
                app.SetFloatingTimer(false);
                app.EnableGlobalShortcut(api); app.EnableGlobalShortcut(api);
                Is(!CompactFocusShortcut(app).Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactFocusId));
                var main = (MainWindow)typeof(TimerApplication).GetField("main", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
                Is(Descendants(main).OfType<Label>().Any(x => x.Text.StartsWith("Ctrl+Alt+. (period) could not be registered")));
                Is(!app.Engine.Snapshot.ShowFloatingTimer);
            });
            Equal(5, api.Registrations.Count); Equal(4, api.Unregistrations.Count);
            Is(!api.Unregistrations.Any(x => x.Id == GlobalShortcut.CompactFocusId));
        });
    }

    private static void PressMainShortcut(TimerApplication app)
    {
        var shortcut = (GlobalShortcut)typeof(TimerApplication).GetField("focusShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.HotKeyId));
        Application.DoEvents();
    }

    private static void TestMainWindowToggleShortcut()
    {
        Test("main foreground check excludes hidden, minimized, disabled, uncreated and other handles", () => {
            using var form = new Form();
            Is(!WindowActivation.IsForeground(form, 0)); Is(!form.IsHandleCreated);
            form.Show(); var handle = form.Handle;
            Is(WindowActivation.IsForeground(form, handle)); Is(!WindowActivation.IsForeground(form, handle + 1));
            Is(!WindowActivation.IsForeground(form, 0));
            form.Enabled = false; Is(!WindowActivation.IsForeground(form, handle)); form.Enabled = true;
            form.WindowState = FormWindowState.Minimized; Is(!WindowActivation.IsForeground(form, handle));
            form.WindowState = FormWindowState.Normal; form.Hide(); Is(!WindowActivation.IsForeground(form, handle));
            form.Dispose(); Is(!WindowActivation.IsForeground(form, handle));
        });
        foreach (var tab in Enumerable.Range(0, 5))
            Test("T uses main X close path and preserves tab and drafts: " + tab, () => {
                WithEndEarlyApp((app, directory) => {
                    var (main, tabs, duration, _) = TimerShortcutControls(app);
                    duration.LoadSeconds(150);
                    var appointment = Descendants(tabs.TabPages[0]).OfType<SessionStartInput>().Single(x => x.AccessibleName == "Start timer at");
                    appointment.Text = "unfinished appointment draft";
                    tabs.SelectedIndex = tab; app.Open(); Application.DoEvents();
                    Is(WindowActivation.IsForeground(main, Form.ActiveForm?.Handle ?? 0));
                    var before = JsonSerializer.Serialize(app.Engine.Snapshot);
                    var saved = File.ReadAllBytes(Path.Combine(directory, "state.dat"));
                    var closeReasons = new List<CloseReason>();
                    main.FormClosing += (_, e) => { Is(e.Cancel); closeReasons.Add(e.CloseReason); };
                    PressMainShortcut(app);
                    Is(!main.Visible && !main.IsDisposed); Equal(CloseReason.UserClosing, closeReasons.Single());
                    Is(app.Log.Recent().Any(x => x.Event == "app.hidden"));
                    PressMainShortcut(app);
                    Is(WindowActivation.IsForeground(main, Form.ActiveForm?.Handle ?? 0)); Equal(tab, tabs.SelectedIndex);
                    Equal(150, duration.Seconds); Equal("unfinished appointment draft", appointment.Text);
                    if (tab == 0) AssertDurationSelected(duration, "Minutes");
                    Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot));
                    Is(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "state.dat"))));
                    // Tray/double-click Open must remain idempotent, not toggle.
                    app.Open(); Is(main.Visible); Equal(1, closeReasons.Count);
                });
            });
        foreach (var paused in new[] { false, true })
            Test("T hiding preserves active session, compact view and auto-start: paused=" + paused, () => {
                WithEndEarlyApp((app, _) => {
                    var (main, _, _, _) = TimerShortcutControls(app);
                    app.SetFloatingTimer(true);
                    app.Engine.Start(7200, true, 0, app.Engine.Now + 3600000);
                    if (paused) app.Engine.Pause();
                    app.Open(); Application.DoEvents(); Is(WindowActivation.IsForeground(main, Form.ActiveForm?.Handle ?? 0));
                    var before = JsonSerializer.Serialize(app.Engine.Snapshot);
                    PressMainShortcut(app);
                    Is(!main.Visible && !main.IsDisposed);
                    Is(Application.OpenForms.OfType<FloatingTimerWindow>().Single().Visible);
                    Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot));
                    PressMainShortcut(app); Is(WindowActivation.IsForeground(main, Form.ActiveForm?.Handle ?? 0));
                });
            });
        Test("T restores minimized/maximized main and brings it forward from another window", () => {
            nint foreground = 0;
            WithEndEarlyApp((app, _) => {
                var (main, _, _, _) = TimerShortcutControls(app);
                app.Open(); main.WindowState = FormWindowState.Maximized; Application.DoEvents();
                main.WindowState = FormWindowState.Minimized; Application.DoEvents();
                Is(!WindowActivation.IsForeground(main, Form.ActiveForm?.Handle ?? 0));
                PressMainShortcut(app); Is(main.Visible); Equal(FormWindowState.Maximized, main.WindowState);
                using var other = new Form { Text = "Isolated foreground test" };
                WindowActivation.Focus(other); Application.DoEvents(); foreground = other.Handle;
                PressMainShortcut(app); Is(main.Visible); Equal(FormWindowState.Maximized, main.WindowState);
                foreground = main.Handle;
                PressMainShortcut(app); Is(!main.Visible);
                foreground = other.Handle;
                PressMainShortcut(app); Equal(FormWindowState.Maximized, main.WindowState);
            }, foregroundWindow: () => foreground);
        });
        Test("T from compact and reflection focuses main without closing either window", () => {
            nint foreground = 0;
            WithEndEarlyApp((app, _) => {
                var (main, _, _, _) = TimerShortcutControls(app); app.Open();
                app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                foreground = mini.Handle;
                PressMainShortcut(app); Is(main.Visible); Is(mini.Visible);
                app.Engine.Start(7200, false, 0); app.ShowCheckIn(); Application.DoEvents();
                var popup = Application.OpenForms.OfType<ReflectionWindow>().Single();
                ReflectionResponse(popup).Text = "Synthetic unfinished check-in";
                WindowActivation.Focus(popup); Application.DoEvents(); foreground = popup.Handle;
                var before = JsonSerializer.Serialize(app.Engine.Snapshot);
                PressMainShortcut(app); Is(main.Visible); Is(popup.Visible && !popup.IsDisposed);
                Equal("Synthetic unfinished check-in", ReflectionResponse(popup).Text);
                Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot));
            }, foregroundWindow: () => foreground);
        });
        Test("T retains an owned modal instead of closing or typing behind its main owner", () => {
            nint foreground = 0;
            WithEndEarlyApp((app, _) => {
                var (main, tabs, _, _) = TimerShortcutControls(app); tabs.SelectedIndex = 3; app.Open();
                using var modal = new Form { Text = "Isolated modal test" };
                var editor = new TextBox { Text = "unsaved modal draft" }; modal.Controls.Add(editor);
                Exception? failure = null;
                modal.Shown += (_, _) => modal.BeginInvoke((Action)(() => {
                    try {
                        WindowActivation.Focus(modal); editor.Focus(); Application.DoEvents();
                        foreground = modal.Handle;
                        if (WindowActivation.CanReceiveFocus(main) || !editor.ContainsFocus) throw new Exception("Modal setup must disable its owner and focus its editor.");
                        PressMainShortcut(app);
                        Is(main.Visible && !WindowActivation.CanReceiveFocus(main) && !main.IsDisposed);
                        if (!modal.Visible || !editor.ContainsFocus) throw new Exception("T must retain the visible modal and its focused editor.");
                        Equal("unsaved modal draft", editor.Text); Equal(3, tabs.SelectedIndex);
                    } catch (Exception error) { failure = error; }
                    finally { modal.Close(); }
                }));
                modal.ShowDialog(main);
                if (failure is not null) throw failure;
                Is(main.Visible && main.Enabled);
            }, foregroundWindow: () => foreground);
        });
    }
}
