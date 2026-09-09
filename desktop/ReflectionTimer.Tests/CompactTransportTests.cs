using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static TransportButton CompactAction(FloatingTimerWindow mini, string name) =>
        Descendants(mini).OfType<TransportButton>().Single(x => x.Name == name);
    private static void TestCompactTransport()
    {
        Test("compact transport starts, pauses, resumes, and resets through shared timer operations", () => {
            WithEndEarlyApp((app, _) => {
                var (main, _, full, _) = TimerShortcutControls(app); main.Hide(); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var back = CompactAction(mini, "CompactReset"); var toggle = CompactAction(mini, "CompactStartPause"); var forward = CompactAction(mini, "CompactEndEarly");
                Is(!back.Enabled && toggle.Enabled && !forward.Enabled); Equal(TransportIcon.Play, toggle.Icon);
                Equal("Start timer", toggle.AccessibleName); Equal("", toggle.Text);
                CompactPart(mini.Duration, "Hours").Text = "0"; CompactPart(mini.Duration, "Minutes").Text = "1"; CompactPart(mini.Duration, "Seconds").Text = "100";
                Is(back.Enabled && toggle.Enabled); toggle.PerformClick(); Application.DoEvents();
                Is(app.Engine.Snapshot.Timer.IsRunning); Equal(160, app.Engine.Snapshot.Timer.DurationSeconds); Equal(160, full.Seconds);
                Is(!main.Visible); Equal(TransportIcon.Pause, toggle.Icon); Equal("Pause timer", toggle.AccessibleName);
                Is(back.Enabled && forward.Enabled); Is(!mini.Duration.Visible);
                app.FocusCompactTimer(); Application.DoEvents(); toggle.PerformClick(); Application.DoEvents();
                Is(TimerEngine.IsPaused(app.Engine.Snapshot.Timer)); Equal("Resume timer", toggle.AccessibleName); Equal(TransportIcon.Play, toggle.Icon);
                Is(back.Enabled && toggle.Enabled && !forward.Enabled);
                var remainder = app.Engine.Snapshot.Timer.PausedRemainingMilliseconds;
                toggle.PerformClick(); Application.DoEvents(); Is(app.Engine.Snapshot.Timer.IsRunning);
                Is(app.Engine.Snapshot.Timer.EndTime - app.Engine.Now <= remainder); Equal(0, app.Engine.Snapshot.Prompts.Count);
                app.FocusCompactTimer(); Application.DoEvents(); back.PerformClick(); Application.DoEvents();
                var reset = app.Engine.Snapshot.Timer; Is(!reset.IsRunning && !TimerEngine.IsPaused(reset)); Equal(160, reset.RemainingSeconds);
                Is(!back.Enabled && toggle.Enabled && !forward.Enabled); Equal(0, app.Engine.Snapshot.Prompts.Count);
            });
        });
        foreach (var mode in new[] { "paused", "completed", "edited" }) Test("compact Back matches full Reset for " + mode, () => {
            WithEndEarlyApp((app, _) => {
                var (_, _, full, _) = TimerShortcutControls(app);
                app.Engine.Start(300, true, 0, app.Engine.Now + 3600000);
                if (mode == "completed") { app.Engine.SetPreferences(false, 0); app.Engine.EndEarly(); }
                else app.Engine.Pause();
                app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                if (mode == "edited") CompactPart(full, "Seconds").Text = "100";
                var before = app.Engine.Snapshot; var seconds = full.Seconds;
                CompactAction(mini, "CompactReset").PerformClick(); Application.DoEvents();
                var after = app.Engine.Snapshot; Is(!after.Timer.IsRunning && !TimerEngine.IsPaused(after.Timer));
                Equal(seconds, after.Timer.DurationSeconds); Equal(seconds, after.Timer.RemainingSeconds); Equal(seconds, mini.Duration.Seconds);
                Equal(before.Timer.AutoRestart, after.Timer.AutoRestart); Equal(before.Timer.AutoRestartUntil, after.Timer.AutoRestartUntil);
                Is(before.Prompts.SequenceEqual(after.Prompts)); Is(!full.Dirty && !mini.Duration.Dirty);
                Is(!CompactAction(mini, "CompactReset").Enabled);
            });
        });
        foreach (var repeat in new[] { false, true }) Test("compact Forward ends early with reflection and preserves auto-start: " + repeat, () => {
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app); var until = repeat ? app.Engine.Now + 3600000 : (long?)null;
                app.Engine.Start(300, repeat, 0, until); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var forward = CompactAction(mini, "CompactEndEarly"); Is(forward.Enabled);
                forward.PerformClick(); Application.DoEvents();
                var state = app.Engine.Snapshot; Equal(repeat, state.Timer.IsRunning); Equal(repeat, state.Timer.AutoRestart); Equal(until, state.Timer.AutoRestartUntil);
                Equal(1, state.Prompts.Count); Is(state.Prompts[0].EndedEarly); Equal(300, state.Prompts[0].DurationSeconds);
                Equal(1, Application.OpenForms.OfType<ReflectionWindow>().Count());
                Equal(repeat, forward.Enabled);
                if (!repeat) { forward.PerformClick(); Application.DoEvents(); Equal(1, app.Engine.Snapshot.Prompts.Count); Is(!app.Engine.Snapshot.Timer.IsRunning); }
            });
        });
        Test("unusable compact transport actions are disabled and cannot mutate a timer", () => {
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var before = app.Engine.Snapshot.Timer;
                Is(Descendants(mini).OfType<TransportButton>().All(x => !x.Enabled));
                foreach (var button in Descendants(mini).OfType<TransportButton>()) button.PerformClick();
                Equal(before, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count);
            }, new AppState { ExtensionDisabledConfirmed = false, Timer = new() { Volume = 0 } });
        });
        foreach (var invalid in new[] { "0", "invalid", "999999999999999999999" }) Test("invalid compact duration disables Play and Back: " + invalid, () => {
            WithEndEarlyApp((app, _) => {
                TimerShortcutControls(app); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single(); mini.Duration.LoadSeconds(0);
                CompactPart(mini.Duration, "Seconds").Text = invalid;
                Is(Descendants(mini).OfType<TransportButton>().All(x => !x.Enabled));
                CompactPart(mini.Duration, "Seconds").Text = "15";
                Is(CompactAction(mini, "CompactStartPause").Enabled && CompactAction(mini, "CompactReset").Enabled);
                Is(!CompactAction(mini, "CompactEndEarly").Enabled);
            });
        });
        Test("failed compact Reset leaves the saved timer and drafts recoverable", () => {
            WithEndEarlyApp((app, directory) => {
                TimerShortcutControls(app); app.Engine.Start(300, false, 0); app.Engine.Pause(); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single(); var before = app.Engine.Snapshot.Timer;
                using (var locked = new FileStream(Path.Combine(directory, "state.dat"), FileMode.Open, FileAccess.Read, FileShare.None)) {
                    CompactAction(mini, "CompactReset").PerformClick(); Application.DoEvents(); Equal(before, app.Engine.Snapshot.Timer);
                }
                CompactAction(mini, "CompactReset").PerformClick(); Application.DoEvents(); Is(!TimerEngine.IsPaused(app.Engine.Snapshot.Timer));
            });
        });
        Test("transport icons use themed button backgrounds, accessible names and visible disabled states", () => {
            foreach (var theme in Enum.GetValues<AppColorTheme>()) foreach (var icon in Enum.GetValues<TransportIcon>()) {
                var palette = AppTheme.PaletteFor(theme);
                using var button = new TransportButton(icon, "QA", icon.ToString(), (_, _) => { }) { BackColor = palette.PrimaryButton, ForeColor = palette.AccentText };
                using var enabled = new Bitmap(38, 38); using var disabled = new Bitmap(38, 38);
                button.DrawToBitmap(enabled, new(0, 0, 38, 38)); button.Enabled = false; button.DrawToBitmap(disabled, new(0, 0, 38, 38));
                Equal("", button.Text); Is(button.Image is null); Equal(AccessibleRole.PushButton, button.AccessibleRole);
                Equal(palette.PrimaryButton.ToArgb(), enabled.GetPixel(4, 4).ToArgb());
                Is(Enumerable.Range(10, 18).Any(x => Enumerable.Range(10, 18).Any(y => enabled.GetPixel(x, y).ToArgb() != disabled.GetPixel(x, y).ToArgb())));
            }
        });
    }
}
