using System.Reflection;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static GlobalShortcut CompactFocusShortcut(TimerApplication app) =>
        (GlobalShortcut)typeof(TimerApplication).GetField("compactFocusShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static void AssertCompactHoursSelected(FloatingTimerWindow mini)
    {
        var hours = CompactPart(mini.Duration, "Hours");
        Is(hours.Visible && hours.Enabled && hours.ContainsFocus);
        var edit = hours.Controls.OfType<TextBox>().Single();
        Equal(edit.Text, edit.SelectedText);
    }

    private static void TestCompactFocusShortcut()
    {
        foreach (var chord in new[] { ("T", "focusShortcut", GlobalShortcut.HotKeyId),
            ("period", "compactFocusShortcut", GlobalShortcut.CompactFocusId), ("slash", "compactShortcut", GlobalShortcut.CompactId) })
            Test("existing " + chord.Item1 + " shortcut selects the first positive part, or Hours for zero", () => {
                foreach (var example in new[] { (3730, "Hours"), (150, "Minutes"), (30, "Seconds"), (0, "Hours") }) {
                    var clock = new ShortcutClock();
                    WithEndEarlyApp((app, _) => {
                        var (main, tabs, full, _) = TimerShortcutControls(app); tabs.SelectedIndex = 0; main.Hide();
                        var shortcut = (GlobalShortcut)typeof(TimerApplication).GetField(chord.Item2, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
                        var before = app.Engine.Snapshot.Timer;
                        Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, chord.Item3)); Application.DoEvents();
                        // Set a typed draft so a focus request cannot replace it with saved defaults.
                        var parts = new[] { "Hours", "Minutes", "Seconds" };
                        var values = new[] { example.Item1 / 3600, example.Item1 / 60 % 60, example.Item1 % 60 };
                        for (var i = 0; i < 3; i++) CompactPart(full, parts[i]).Text = values[i].ToString();
                        if (chord.Item1 == "slash") { app.SetFloatingTimer(false); Application.DoEvents(); }
                        clock.Milliseconds += 801; // Separate presses, not period's double-press gesture.
                        Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, chord.Item3)); Application.DoEvents();
                        var target = chord.Item1 == "T" ? full : Application.OpenForms.OfType<FloatingTimerWindow>().Single().Duration;
                        var input = CompactPart(target, example.Item2);
                        if (!input.ContainsFocus) throw new Exception($"Expected {example.Item2} for {example.Item1}; enabled={target.Enabled}, visible={target.Visible}, values={string.Join(':', parts.Select(p => CompactPart(target, p).Text))}, focused={string.Join(',', parts.Where(p => CompactPart(target, p).ContainsFocus))}.");
                        var edit = input.Controls.OfType<TextBox>().Single(); Equal(edit.Text, edit.SelectedText);
                        Equal(example.Item1, full.Seconds); Equal(before, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count);
                    }, shortcutClock: clock);
                }
            });
        Test("focus selection reads raw overflow and invalid fields without normalizing them", () => {
            WithEndEarlyApp((app, _) => {
                var (main, tabs, full, _) = TimerShortcutControls(app); tabs.SelectedIndex = 0; app.Open(); Application.DoEvents();
                CompactPart(full, "Hours").Text = "invalid";
                CompactPart(full, "Minutes").Text = "100";
                CompactPart(full, "Seconds").Text = "30";
                full.FocusFirstPositivePart();
                Is(CompactPart(full, "Minutes").ContainsFocus);
                Equal("invalid", CompactPart(full, "Hours").Text); Equal("100", CompactPart(full, "Minutes").Text);
            });
        });
        Test("period shortcut registers Ctrl+Alt+NoRepeat independently and releases once", () => {
            var api = new FakeHotKey(); var count = 0;
            var shortcut = new GlobalShortcut(() => count++, api, GlobalShortcut.CompactFocusKey, GlobalShortcut.CompactFocusId);
            var call = api.Registrations.Single(); Equal(0xBEu, call.Key); Equal(0x4003u, call.Modifiers); Equal(0x5257, call.Id);
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactId));
            Is(!shortcut.Dispatch(0, GlobalShortcut.CompactFocusId));
            Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactFocusId)); Equal(1, count);
            shortcut.Dispose(); shortcut.Dispose(); Equal((call.Window, call.Id), api.Unregistrations.Single());
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactFocusId));
        });
        foreach (var paused in new[] { false, true }) Test("period focuses Hours without toggling, starting, or changing a draft; paused=" + paused, () => {
            var clock = new ShortcutClock();
            WithEndEarlyApp((app, directory) => {
                var (main, _, full, _) = TimerShortcutControls(app); main.Hide();
                if (paused) { app.Engine.Start(7200, true, 0); app.Engine.Pause(); }
                CompactPart(full, "Hours").Text = "12";
                var before = app.Engine.Snapshot.Timer;
                var shortcut = CompactFocusShortcut(app);
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactFocusId)); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                AssertCompactHoursSelected(mini); Is(!mini.Duration.ReadOnly); Is(!main.Visible);
                Equal("12", CompactPart(mini.Duration, "Hours").Text);
                Equal(before, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count);
                var saved = File.ReadAllBytes(Path.Combine(directory, "state.dat"));
                clock.Milliseconds += 801;
                shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactFocusId); Application.DoEvents();
                AssertCompactHoursSelected(mini); Is(mini.Visible && app.Engine.Snapshot.ShowFloatingTimer);
                Is(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "state.dat"))));
                // The focused editor still shares edits and its normal Enter-to-start path.
                CompactPart(mini.Duration, "Hours").Text = "2"; Equal("2", CompactPart(full, "Hours").Text);
                Is(DispatchCommandKey(mini.Duration, Keys.Enter)); Is(app.Engine.Snapshot.Timer.IsRunning);
                Equal(full.Seconds, app.Engine.Snapshot.Timer.DurationSeconds);
            }, shortcutClock: clock);
        });
        Test("period reveals a running timer without changing its deadline, preferences, prompts or locked duration", () => {
            WithEndEarlyApp((app, _) => {
                var (main, _, _, _) = TimerShortcutControls(app); main.Hide();
                var cutoff = app.Engine.Now + 3600000;
                app.Engine.Start(9000, true, 0, cutoff); app.SetFloatingTimer(true); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single(); var tinySize = mini.Size;
                var before = app.Engine.Snapshot.Timer;
                CompactFocusShortcut(app).Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactFocusId); Application.DoEvents();
                AssertCompactHoursSelected(mini); Is(mini.Duration.ReadOnly); Is(!main.Visible); Is(mini.Height > tinySize.Height);
                var hours = CompactPart(mini.Duration, "Hours"); var original = hours.Text;
                hours.UpButton(); hours.DownButton(); Equal(original, hours.Text); Is(!mini.Duration.Dirty);
                Is(DispatchCommandKey(mini.Duration, Keys.Enter));
                mini.Render(app.Engine.Snapshot, app.Engine.Now + 1000); AssertCompactHoursSelected(mini);
                Equal(before, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count);
                Is(DispatchCommandKey(mini, Keys.Escape)); Application.DoEvents();
                Is(!mini.Duration.Visible); Equal(tinySize, mini.Size); Equal(before, app.Engine.Snapshot.Timer);
                app.FocusCompactTimer(); Application.DoEvents(); AssertCompactHoursSelected(mini);
                app.ToggleTimerPause(); Application.DoEvents(); Is(!mini.Duration.ReadOnly && mini.Duration.Enabled);
                app.ToggleTimerPause(); Application.DoEvents(); Is(!mini.Duration.Visible);
            });
        });
        Test("switching windows and hiding compact dismiss the temporary running controls", () => {
            WithEndEarlyApp((app, _) => {
                var (main, _, _, _) = TimerShortcutControls(app);
                app.Engine.Start(9000, false, 0); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single(); AssertCompactHoursSelected(mini);
                var before = app.Engine.Snapshot.Timer;
                app.Open(); Application.DoEvents(); Is(!mini.Duration.Visible && mini.Visible);
                Equal(before, app.Engine.Snapshot.Timer);
                main.Hide(); app.FocusCompactTimer(); Application.DoEvents(); AssertCompactHoursSelected(mini);
                app.ToggleCompactTimer(); Application.DoEvents(); Is(mini.Visible && !mini.Duration.Visible);
                app.ToggleCompactTimer(); Application.DoEvents(); Is(!mini.Visible);
                app.ToggleCompactTimer(); Application.DoEvents(); AssertCompactHoursSelected(mini);
                Equal(before, app.Engine.Snapshot.Timer);
            });
        });
        Test("a new running session restores countdown-only after temporary focus", () => {
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app);
                app.Engine.Start(9000, true, 0); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single(); AssertCompactHoursSelected(mini);
                // A scheduled handoff or auto-start replaces the current deadline.
                app.Engine.Start(300, true, 0); Application.DoEvents();
                Is(!mini.Duration.Visible); Is(app.Engine.Snapshot.Timer.IsRunning);
            });
        });
        Test("period unavailable is visible in settings and app disposal releases the other chords", () => {
            var api = new FakeHotKey { BlockedKey = GlobalShortcut.CompactFocusKey };
            WithEndEarlyApp((app, _) => {
                app.EnableGlobalShortcut(api);
                var main = (MainWindow)typeof(TimerApplication).GetField("main", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
                Is(Descendants(main).OfType<Label>().Any(x => x.Text.StartsWith("Ctrl+Alt+. (period) could not be registered")));
                Is(!CompactFocusShortcut(app).Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.CompactFocusId));
            });
            Equal(5, api.Registrations.Count); Equal(4, api.Unregistrations.Count);
        });
    }
}
