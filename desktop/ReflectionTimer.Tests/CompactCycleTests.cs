using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestCompactCycle()
    {
        foreach (var mode in new[] { "idle", "paused", "running" }) Test("slash cycles controls time-only hidden controls without changing the " + mode + " timer", () => {
            WithEndEarlyApp((app, directory) => {
                var (main, _, full, _) = TimerShortcutControls(app); main.Hide();
                if (mode == "idle") app.Engine.Reset(150);
                else { app.Engine.Start(150, true, 0, app.Engine.Now + 3600000); if (mode == "paused") app.Engine.Pause(); }
                Application.DoEvents(); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single(); var expanded = mini.Size;
                Is(mini.Duration.Visible); var before = app.Engine.Snapshot.Timer;
                var saved = File.ReadAllBytes(Path.Combine(directory, "state.dat"));
                app.ToggleCompactTimer(); Application.DoEvents();
                Is(mini.Visible && !mini.Duration.Visible); Equal(FormBorderStyle.None, mini.FormBorderStyle);
                Is(mini.Width < expanded.Width && mini.Height < expanded.Height / 2);
                Is(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "state.dat"))));
                Is(app.Engine.Snapshot.ShowFloatingTimer); Equal(before, app.Engine.Snapshot.Timer);
                for (var i = 0; i < 3; i++) mini.Render(app.Engine.Snapshot, app.Engine.Now);
                Is(!mini.Duration.Visible);
                app.ToggleCompactTimer(); Application.DoEvents(); Is(!mini.Visible && !app.Engine.Snapshot.ShowFloatingTimer);
                app.ToggleCompactTimer(); Application.DoEvents();
                Is(mini.Visible && mini.Duration.Visible); Equal(expanded, mini.Size); Is(!main.Visible);
                var minutes = CompactPart(mini.Duration, "Minutes"); Is(minutes.ContainsFocus);
                var edit = minutes.Controls.OfType<TextBox>().Single(); Equal(edit.Text, edit.SelectedText);
                Equal(mode == "running", mini.Duration.ReadOnly);
                Equal(before, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count); Equal(150, full.Seconds);
            });
        });
        Test("manual time-only displays large idle drafts without clipping and period restores the editor", () => {
            WithEndEarlyApp((app, _) => {
                var (main, _, full, _) = TimerShortcutControls(app); main.Hide(); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                CompactPart(full, "Hours").Text = "8760"; CompactPart(full, "Minutes").Text = "0"; CompactPart(full, "Seconds").Text = "0";
                var before = app.Engine.Snapshot.Timer;
                app.ToggleCompactTimer(); Application.DoEvents();
                var label = Descendants(mini).OfType<Label>().Single(x => x.Visible);
                Equal(MainWindow.Clock(TimerEngine.MaxDuration), label.Text);
                using var graphics = label.CreateGraphics();
                var size = TextRenderer.MeasureText(graphics, label.Text, label.Font, Size.Empty, TextFormatFlags.NoPadding);
                Is(label.Width >= size.Width && label.Height >= size.Height);
                app.FocusCompactTimer(); Application.DoEvents();
                Is(mini.Duration.Visible); Is(CompactPart(mini.Duration, "Hours").ContainsFocus);
                Equal(TimerEngine.MaxDuration, full.Seconds); Equal(before, app.Engine.Snapshot.Timer);
            });
        });
        Test("shrinking retains placement anchors and never persists a resize as a drag", () => {
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                foreach (var placement in Enum.GetValues<FloatingTimerPlacement>().Where(x => x != FloatingTimerPlacement.Custom)) {
                    app.FocusCompactTimer(); app.Engine.SetFloatingTimerPlacement(placement); Application.DoEvents();
                    app.ToggleCompactTimer(); Application.DoEvents();
                    Equal(placement, app.Engine.Snapshot.FloatingPlacement);
                    Equal(FloatingTimerWindow.PresetPosition(Screen.FromControl(mini).WorkingArea, mini.Size, placement), mini.Location);
                    app.ToggleCompactTimer(); app.ToggleCompactTimer(); Application.DoEvents();
                    Equal(placement, app.Engine.Snapshot.FloatingPlacement);
                    Equal(FloatingTimerWindow.PresetPosition(Screen.FromControl(mini).WorkingArea, mini.Size, placement), mini.Location);
                }
            });
        });
        Test("a manual shrink survives preference refreshes but pause restores controls", () => {
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                app.ToggleCompactTimer(); app.Engine.SetAppVolume(27); Application.DoEvents(); Is(!mini.Duration.Visible);
                app.Engine.Start(300, false, 0); Application.DoEvents(); Is(!mini.Duration.Visible);
                app.Engine.Pause(); Application.DoEvents(); Is(mini.Duration.Visible);
                app.ToggleCompactTimer(); Application.DoEvents(); Is(!mini.Duration.Visible);
                app.FocusCompactTimer(); Application.DoEvents(); Is(mini.Duration.Visible && mini.Duration.Enabled);
            });
        });
    }
}
