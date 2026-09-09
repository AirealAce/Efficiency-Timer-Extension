using System.Reflection;
using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestCompactUpdate()
    {
        TestCompactFocusShortcut();
        TestCompactTransport();
        Test("compact footer has a centered checkbox on the left and right-aligned App then Back Play Forward", () => {
            WithEndEarlyApp((app, _) => {
                app.SetFloatingTimer(true); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var check = Descendants(mini).OfType<CheckBox>().Single();
                var open = Descendants(mini).OfType<Button>().Single(x => x.Text == "App");
                var back = CompactAction(mini, "CompactReset");
                var start = CompactAction(mini, "CompactStartPause");
                var forward = CompactAction(mini, "CompactEndEarly");
                foreach (var factor in new[] { 1f, 1.5f }) {
                    if (factor != 1) mini.Scale(new SizeF(factor, factor));
                    mini.PerformLayout(); Application.DoEvents();
                    Equal(0, check.Left); Is(check.Right <= open.Left); Is(open.Right < back.Left); Is(back.Right < start.Left); Is(start.Right < forward.Left);
                    Equal(forward.Parent!.ClientSize.Width, forward.Right);
                    Equal(open.Top, start.Top); Equal(open.Height, start.Height);
                    Equal(back.Size, start.Size); Equal(start.Size, forward.Size); Equal(back.Top, start.Top); Equal(start.Top, forward.Top);
                    Is(Math.Abs((check.Top * 2 + check.Height) - (start.Top * 2 + start.Height)) <= 2);
                    var bounds = mini.RectangleToClient(start.RectangleToScreen(start.ClientRectangle));
                    Is(bounds.Bottom <= mini.ClientSize.Height - mini.Padding.Bottom);
                }
            });
        });
        foreach (var mode in new[] { "idle", "running", "paused" }) Test("compact auto-start synchronizes and preserves the " + mode + " session and draft", () => {
            WithEndEarlyApp((app, directory) => {
                var (main, tabs, duration, _) = TimerShortcutControls(app);
                if (mode != "idle") app.Engine.Start(300, false, 0);
                if (mode == "paused") app.Engine.Pause();
                app.SetFloatingTimer(true); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var check = Descendants(mini).OfType<CheckBox>().Single();
                var options = Descendants(tabs.TabPages[0]).OfType<AutoRestartOptions>().Single();
                var mainCheck = Descendants(options).OfType<CheckBox>().Single(x => x.Text == "Auto-start next session");
                if (mode != "running") CompactPart(mini.Duration, "Seconds").Text = "100";
                var before = app.Engine.Snapshot.Timer;
                check.Checked = true; Application.DoEvents();
                Equal(before with { AutoRestart = true }, app.Engine.Snapshot.Timer);
                Is(options.AutoRestart && mainCheck.Checked && check.Checked);
                Is(new EncryptedStore(directory).Load().Timer.AutoRestart);
                if (mode != "running") { Equal("100", CompactPart(duration, "Seconds").Text); Is(duration.Dirty); }
                mainCheck.Checked = false; Application.DoEvents();
                Is(!check.Checked && !app.Engine.Snapshot.Timer.AutoRestart);
                var until = app.Engine.Now / 60000 * 60000 + 3600000;
                app.Engine.SetPreferences(true, 0, until); Application.DoEvents();
                Is(check.Checked); Equal(until, options.AutoRestartUntil);
                check.Checked = false; Application.DoEvents();
                Is(!options.AutoRestart); Equal(null, options.AutoRestartUntil);
                Equal(null, app.Engine.Snapshot.Timer.AutoRestartUntil);
                check.Checked = true; Application.DoEvents();
                Is(options.AutoRestart); Equal(null, options.AutoRestartUntil);
                Equal(0, app.Engine.Snapshot.Prompts.Count);
            });
        });
        Test("failed compact auto-start save restores the checkbox and keeps the saved session", () => {
            WithEndEarlyApp((app, directory) => {
                var (_, tabs, _, _) = TimerShortcutControls(app);
                app.SetFloatingTimer(true); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var check = Descendants(mini).OfType<CheckBox>().Single();
                var before = JsonSerializer.Serialize(app.Engine.Snapshot);
                using (var locked = new FileStream(Path.Combine(directory, "state.dat"), FileMode.Open, FileAccess.Read, FileShare.None)) {
                    check.Checked = true; Application.DoEvents();
                    Is(!check.Checked); Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot));
                    Is(!Descendants(tabs.TabPages[0]).OfType<AutoRestartOptions>().Single().AutoRestart);
                }
                check.Checked = true; Application.DoEvents(); Is(app.Engine.Snapshot.Timer.AutoRestart);
            });
        });
        Test("volume sliders sit below labels in all three pages", () => {
            WithEndEarlyApp((app, _) => {
                var (main, tabs, _, _) = TimerShortcutControls(app); main.Show();
                foreach (var index in new[] { 0, 1, 3 }) {
                    tabs.SelectedIndex = index; Application.DoEvents();
                    foreach (var volume in Descendants(tabs.TabPages[index]).OfType<VolumeControl>()) {
                        var label = Descendants(volume).OfType<Label>().Single();
                        var track = Descendants(volume).OfType<TrackBar>().Single();
                        Is(track.Top >= label.Bottom); Equal(label.Left, track.Left);
                        Is(volume.ClientRectangle.Contains(volume.RectangleToClient(track.RectangleToScreen(track.ClientRectangle))));
                    }
                }
            });
        });
        Test("compact and main duration drafts mirror both directions without normalization or feedback", () => {
            WithEndEarlyApp((app, _) => {
                var (main, tabs, full, _) = TimerShortcutControls(app);
                app.SetFloatingTimer(true); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var small = mini.Duration;
                full.LoadSeconds(0); CompactPart(small, "Hours").Text = "1";
                Equal("1", CompactPart(full, "Hours").Text); Is(full.Dirty && small.Dirty);
                CompactPart(full, "Minutes").Text = "20"; CompactPart(small, "Seconds").Text = "100";
                Equal("100", CompactPart(full, "Seconds").Text); Equal(4900, full.Seconds); Equal(full.Seconds, small.Seconds);
                small.Normalize(); Equal("21", CompactPart(full, "Minutes").Text); Equal("40", CompactPart(full, "Seconds").Text);
                CompactPart(small, "Seconds").Text = "invalid";
                Equal("invalid", CompactPart(full, "Seconds").Text); Is(!full.TryGetSeconds(out var invalidSeconds, out var invalidError));
                app.Engine.SetAppVolume(27); Application.DoEvents();
                Equal("invalid", CompactPart(small, "Seconds").Text); Equal("invalid", CompactPart(full, "Seconds").Text);
                Is(Descendants(mini).OfType<Label>().Any(x => x.Text == "—"));
                full.LoadSeconds(9000); Equal(9000, small.Seconds); Is(!small.Dirty);
                // Hiding either view never breaks the binding or resets a draft.
                main.Hide(); app.SetFloatingTimer(false); CompactPart(full, "Seconds").Text = "22";
                app.SetFloatingTimer(true); Equal(full.Seconds, small.Seconds);
            });
        });
        foreach (var part in new[] { "Hours", "Minutes", "Seconds" }) Test("Enter in compact " + part + " starts shared duration once", () => {
            WithEndEarlyApp((app, _) => {
                var (_, _, full, _) = TimerShortcutControls(app);
                app.SetFloatingTimer(true); Application.DoEvents();
                full.LoadSeconds(0);
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                CompactPart(mini.Duration, "Seconds").Text = "100";
                var input = CompactPart(mini.Duration, part); input.Focus();
                Is(DispatchCommandKey(input, Keys.Enter)); Application.DoEvents();
                Is(app.Engine.Snapshot.Timer.IsRunning); Equal(100, app.Engine.Snapshot.Timer.DurationSeconds);
                Equal(100, full.Seconds); Equal(100, mini.Duration.Seconds);
                Is(!full.Enabled && !mini.Duration.Enabled);
                Is(DispatchCommandKey(input, Keys.Enter)); Is(app.Engine.Snapshot.Timer.IsRunning);
                Equal(0, app.Engine.Snapshot.Prompts.Count);
            });
        });
        Test("compact hotkey shrinks before hiding and selects Hours with the main window hidden", () => {
            WithEndEarlyApp((app, directory) => {
                var (main, _, duration, _) = TimerShortcutControls(app); main.Hide();
                var shortcut = (GlobalShortcut)typeof(TimerApplication).GetField("compactShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
                var before = app.Engine.Snapshot.Timer;
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactId)); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                Is(mini.Visible); Is(CompactPart(mini.Duration, "Hours").ContainsFocus);
                var edit = CompactPart(mini.Duration, "Hours").Controls.OfType<TextBox>().Single();
                Equal(edit.Text, edit.SelectedText); Is(!main.Visible);
                Equal(before, app.Engine.Snapshot.Timer);
                Is(new EncryptedStore(directory).Load().ShowFloatingTimer);
                shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactId); Application.DoEvents();
                Is(mini.Visible && !mini.Duration.Visible); Is(new EncryptedStore(directory).Load().ShowFloatingTimer);
                Equal(before, app.Engine.Snapshot.Timer);
                shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactId); Application.DoEvents();
                Is(!mini.Visible); Is(!new EncryptedStore(directory).Load().ShowFloatingTimer); Equal(9000, duration.Seconds);
            }, new AppState { ExtensionDisabledConfirmed = true, Timer = new TimerState { DurationSeconds = 9000, RemainingSeconds = 9000, Volume = 0 } });
        });
        Test("slash shortcut registration is independent and released", () => {
            var api = new FakeHotKey(); var count = 0;
            var shortcut = new GlobalShortcut(() => count++, api, GlobalShortcut.CompactKey, GlobalShortcut.CompactId);
            var call = api.Registrations.Single(); Equal(0xBFu, call.Key); Equal(0x4003u, call.Modifiers); Equal(0x5256, call.Id);
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
            Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactId)); Equal(1, count);
            shortcut.Dispose(); Equal((call.Window, call.Id), api.Unregistrations.Single());
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactId));
        });
        Test("all placement presets fit work areas, including negative and small monitor bounds", () => {
            var area = new Rectangle(-1000, 100, 1000, 800); var size = new Size(360, 240);
            var expected = new Dictionary<FloatingTimerPlacement, Point> {
                [FloatingTimerPlacement.Center] = new(-680, 380), [FloatingTimerPlacement.TopLeft] = new(-984,116),
                [FloatingTimerPlacement.TopRight] = new(-376,116), [FloatingTimerPlacement.BottomLeft] = new(-984,644),
                [FloatingTimerPlacement.BottomRight] = new(-376,644), [FloatingTimerPlacement.TopCenter] = new(-680,116),
                [FloatingTimerPlacement.BottomCenter] = new(-680,644)
            };
            foreach (var (placement, point) in expected) {
                Equal(point, FloatingTimerWindow.PresetPosition(area, size, placement));
                Equal(new Point(10,20), FloatingTimerWindow.PresetPosition(new(10,20,50,50), size, placement));
            }
        });
        Test("saved placement validates atomically, drag switches to custom and coordinates survive restart", () => {
            var f = new Fixture(); f.Engine.SetFloatingTimerPosition(-1800, 200);
            f.Engine.SetFloatingTimerPlacement(FloatingTimerPlacement.TopCenter);
            var state = f.Restart().Snapshot; Equal(FloatingTimerPlacement.TopCenter, state.FloatingPlacement); Equal(-1800, state.FloatingTimerLeft);
            var before = JsonSerializer.Serialize(state);
            Throws<ArgumentException>(() => f.Engine.SetFloatingTimerPlacement((FloatingTimerPlacement)99));
            Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot));
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.SetFloatingTimerPlacement(FloatingTimerPlacement.Center));
            Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot)); f.Store.Fail = false;
            f.Engine.SetFloatingTimerPosition(120,250); state = f.Restart().Snapshot;
            Equal(FloatingTimerPlacement.Custom, state.FloatingPlacement); Equal(120, state.FloatingTimerLeft); Equal(250, state.FloatingTimerTop);
        });
        Test("compact position saves on hiding and restores from encrypted storage", () => {
            AppState? saved = null; Point location = default;
            WithEndEarlyApp((app, directory) => {
                app.SetFloatingTimer(true); Application.DoEvents(); var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var area = Screen.FromControl(mini).WorkingArea; location = new(area.Left + 40, area.Top + 70);
                mini.Location = location; app.SetFloatingTimer(false); Application.DoEvents();
                saved = new EncryptedStore(directory).Load(); Equal(location.X, saved.FloatingTimerLeft); Equal(location.Y, saved.FloatingTimerTop);
                saved.ShowFloatingTimer = true;
            });
            WithEndEarlyApp((app, _) => { Application.DoEvents(); Equal(location, Application.OpenForms.OfType<FloatingTimerWindow>().Single().Location); }, saved);
        });
        Test("compact settings selector moves an open timer and drag preference follows", () => {
            WithEndEarlyApp((app, directory) => {
                var (main, tabs, _, _) = TimerShortcutControls(app); main.Show(); tabs.SelectedIndex = 3;
                app.SetFloatingTimer(true); Application.DoEvents(); var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var select = Descendants(main).OfType<ComboBox>().Single(x => x.AccessibleName == "Compact timer position"); Equal(8, select.Items.Count);
                Equal(4, select.SelectedIndex);
                Equal(FloatingTimerWindow.PresetPosition(Screen.FromControl(mini).WorkingArea, mini.Size, FloatingTimerPlacement.BottomLeft), mini.Location);
                foreach (var index in new[] { 2, 3, 4, 5, 1, 6, 7 }) {
                    select.SelectedIndex = index; Application.DoEvents();
                    Equal((FloatingTimerPlacement)index, new EncryptedStore(directory).Load().FloatingPlacement);
                    Equal(FloatingTimerWindow.PresetPosition(Screen.FromControl(mini).WorkingArea, mini.Size, (FloatingTimerPlacement)index), mini.Location);
                }
                mini.Location = new(100,100); mini.SavePosition(); Application.DoEvents(); Equal(0, select.SelectedIndex);
            });
        });
        Test("compact autosizing contains fields and action buttons with larger fonts/scaling", () => {
            WithEndEarlyApp((app, _) => {
                app.SetFloatingTimer(true); Application.DoEvents(); var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                foreach (var factor in new[] { 1f, 1.5f }) {
                    if (factor != 1) mini.Scale(new SizeF(factor, factor));
                    mini.PerformLayout(); Application.DoEvents();
                    foreach (var child in Descendants(mini).Where(x => x is Button or InputFrame or CheckBox)) {
                        var rect = mini.RectangleToClient(child.RectangleToScreen(child.ClientRectangle));
                        Is(rect.Top >= 0 && rect.Bottom <= mini.ClientSize.Height - mini.Padding.Bottom);
                        Is(rect.Left >= 0 && rect.Right <= mini.ClientSize.Width);
                    }
                }
                Is(!Descendants(mini).OfType<Label>().Any(x => x.Text.Contains("Session finished")));
            });
        });
        Test("completion display holds zero for one second without changing engine or reflection", () => {
            var f = new Fixture(); var view = new CountdownPresentation();
            f.Engine.Start(30, false, 0); Equal(30, view.Seconds(f.Engine.Snapshot.Timer, f.Engine.Now));
            f.Move(30); f.Engine.Advance(); var state = f.Engine.Snapshot; var before = JsonSerializer.Serialize(state);
            var now = f.Engine.Now; Equal(0, view.Seconds(state.Timer, now)); Equal(0, view.Seconds(state.Timer, now + 999));
            Equal(30, view.Seconds(state.Timer, now + 1000)); Equal(30, view.Seconds(state.Timer, now + 10000));
            Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot)); Equal(1, state.Prompts.Count);
            Equal(30, new CountdownPresentation().Seconds(state.Timer, now + 1000));
        });
        Test("ready display preserves pause remainder and never delays auto restart or scheduled handoff", () => {
            var f = new Fixture(); var view = new CountdownPresentation(); f.Engine.Start(30, true, 0);
            view.Seconds(f.Engine.Snapshot.Timer, f.Engine.Now); f.Move(10); f.Engine.Pause(); Equal(20, view.Seconds(f.Engine.Snapshot.Timer, f.Engine.Now));
            f.Engine.Resume(); view.Seconds(f.Engine.Snapshot.Timer, f.Engine.Now); f.Move(20); f.Engine.Advance(); Equal(30, view.Seconds(f.Engine.Snapshot.Timer, f.Engine.Now));
            f.Add(5,60); f.Move(5); f.Engine.Advance(); Equal(60, view.Seconds(f.Engine.Snapshot.Timer, f.Engine.Now));
        });
    }
    private static DurationPartInput CompactPart(DurationControl control, string name) =>
        Descendants(control).OfType<DurationPartInput>().Single(x => x.AccessibleName == name);
}
