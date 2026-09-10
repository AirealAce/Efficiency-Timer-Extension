using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestReliabilityBoundaries()
    {
        foreach (var endByShortcut in new[] { false, true }) Test("waiting FIFO survives a newly due session at completion; shortcut=" + endByShortcut, () => {
            var f = new Fixture(); f.Engine.SetScheduleOverlap(ScheduleOverlapPolicy.Wait);
            f.Engine.Start(60, true, 0); f.Add(10, 20); f.Add(60, 30); f.Add(500, 40);
            f.Move(10); f.Engine.Advance(); f.Move(50);
            var writes = f.Store.Writes;
            if (endByShortcut) f.Engine.EndEarly(); else f.Engine.Advance();
            Equal(writes + 1, f.Store.Writes); Equal(20, f.Engine.Snapshot.Timer.DurationSeconds);
            Equal(1, f.Engine.Snapshot.Prompts.Count); Is(!f.Engine.Snapshot.Prompts.Single().EndedEarly);
            Is(f.Engine.Snapshot.Schedules.Any(x => x.DurationSeconds == 30 && x.WaitingForCurrentSession));
            f.Engine.Advance(); Equal(1, f.Engine.Snapshot.Prompts.Count);
            f.Move(20); f.Engine.Advance(); Equal(30, f.Engine.Snapshot.Timer.DurationSeconds);
            Equal(40, f.Engine.Snapshot.Schedules.Single().DurationSeconds);
        });
        foreach (var policy in Enum.GetValues<ScheduleOverlapPolicy>())
            foreach (var delay in new[] { 20, 60, 120 }) Test($"shortcut hands off a due session once; policy={policy}, elapsed={delay}", () => {
                var f = new Fixture(); f.Engine.SetScheduleOverlap(policy);
                f.Engine.Start(60, true, 0); f.Add(20, 120); f.Add(500, 40); f.Move(delay);
                var writes = f.Store.Writes; f.Engine.EndEarly();
                Equal(writes + 1, f.Store.Writes); Equal(120, f.Engine.Snapshot.Timer.DurationSeconds);
                Equal(40, f.Engine.Snapshot.Schedules.Single().DurationSeconds);
                f.Engine.Advance(); var restored = f.Restart(); restored.Advance();
                var prompt = restored.Snapshot.Prompts.Single(); Equal(Math.Min(60, delay), prompt.ActualDurationSeconds!.Value);
                Equal(delay < 60, prompt.EndedEarly); Equal(60, prompt.DurationSeconds);
                Equal(120, restored.Snapshot.Timer.DurationSeconds);
            });
        Test("reset keeps waiting FIFO when a new appointment is due", () => {
            var f = new Fixture(); f.Engine.SetScheduleOverlap(ScheduleOverlapPolicy.Wait);
            f.Engine.Start(600, false, 0); f.Add(10, 20); f.Add(20, 30);
            f.Move(10); f.Engine.Advance(); f.Move(10); f.Engine.Reset(); f.Engine.Advance();
            Equal(20, f.Engine.Snapshot.Timer.DurationSeconds);
            Is(f.Engine.Snapshot.Schedules.Single().WaitingForCurrentSession); Equal(0, f.Engine.Snapshot.Prompts.Count);
        });
        Test("failed shortcut handoff is atomic and retry creates only one reflection", () => {
            var f = new Fixture(); f.Engine.Start(60, true, 0); f.Add(20, 120); f.Move(20);
            var before = JsonSerializer.Serialize(f.Engine.Snapshot); f.Store.Fail = true;
            Throws<IOException>(() => f.Engine.EndEarly()); Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot));
            f.Store.Fail = false; f.Engine.EndEarly(); f.Engine.Advance();
            Equal(120, f.Engine.Snapshot.Timer.DurationSeconds); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        Test("shortcut catch-up keeps latest newly missed session and its expired cutoff", () => {
            var f = new Fixture(); f.Engine.Start(60, true, 0); f.Add(10, 90);
            f.Engine.SaveSchedule(null, f.Time.AddSeconds(20), 120, true, 31, f.Time.AddSeconds(30).ToUnixTimeMilliseconds());
            f.Move(120); f.Engine.EndEarly(); f.Engine.Advance();
            Equal(120, f.Engine.Snapshot.Timer.DurationSeconds); Equal(31, f.Engine.Snapshot.Timer.Volume);
            Is(!f.Engine.Snapshot.Timer.AutoRestart); Is(f.Engine.Snapshot.Timer.AutoRestartUntil is null);
            Equal(0, f.Engine.Snapshot.Schedules.Count); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        foreach (var entryPoint in new[] { "shortcut", "button", "enter", "floating" }) Test("subsecond pause resumes without rearming warning via " + entryPoint, () => {
            WithEndEarlyApp((app, _) => {
                var (main, tabs, duration, shortcut) = TimerShortcutControls(app);
                main.Show(); tabs.SelectedIndex = 0; Application.DoEvents();
                var before = app.Engine.Now;
                if (entryPoint == "shortcut") shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId);
                else if (entryPoint == "button") Descendants(tabs.TabPages[0]).OfType<Button>().Single(x => x.Text is "Start" or "Resume").PerformClick();
                else if (entryPoint == "enter") {
                    var input = Descendants(duration).OfType<DurationPartInput>().First(); input.Focus();
                    Is(DispatchCommandKey(input, Keys.Enter));
                }
                else { app.SetFloatingTimer(true); Application.DoEvents();
                    CompactAction(Application.OpenForms.OfType<FloatingTimerWindow>().Single(), "CompactStartPause").PerformClick(); }
                var after = app.Engine.Now; var timer = app.Engine.Snapshot.Timer;
                Is(timer.IsRunning); Is(timer.LowTimePlayed); Equal(60, timer.DurationSeconds);
                Is(timer.EndTime >= before + 59500 && timer.EndTime <= after + 59500);
                Equal(0, app.Engine.Snapshot.Prompts.Count);
            }, FractionallyPausedState());
        });
        Test("both timer views label a subsecond pause as paused and resumable", () => {
            WithEndEarlyApp((app, _) => {
                var (main, tabs, _, _) = TimerShortcutControls(app); main.RenderClock();
                Is(Descendants(tabs.TabPages[0]).OfType<Button>().Any(x => x.Text == "Resume"));
                Is(Descendants(tabs.TabPages[0]).OfType<Label>().Any(x => x.Text == "Paused"));
                app.SetFloatingTimer(true); Application.DoEvents(); var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                Is(Descendants(mini).OfType<Button>().Any(x => x.AccessibleName == "Resume timer"));
                Is(Descendants(mini).OfType<DurationControl>().Single().Enabled);
            }, FractionallyPausedState());
        });
        Test("editing a fractionally paused duration still starts a new session", () => {
            WithEndEarlyApp((app, _) => {
                var (_, _, duration, shortcut) = TimerShortcutControls(app);
                Descendants(duration).OfType<DurationPartInput>().Single(x => x.AccessibleName == "Seconds").Text = "15";
                shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId);
                Equal(75, app.Engine.Snapshot.Timer.DurationSeconds); Is(!app.Engine.Snapshot.Timer.LowTimePlayed);
            }, FractionallyPausedState());
        });
        Test("pause classification supports legacy state and explicit zero-progress pauses", () => {
            Is(!TimerEngine.IsPaused(new()));
            Is(TimerEngine.IsPaused(new() { DurationSeconds = 60, RemainingSeconds = 59 }));
            Is(TimerEngine.IsPaused(new() { DurationSeconds = 60, RemainingSeconds = 60, PausedRemainingMilliseconds = 60000 }));
            Is(!TimerEngine.IsPaused(new() { IsRunning = true, DurationSeconds = 60, RemainingSeconds = 59, PausedRemainingMilliseconds = 59500 }));
            Is(!TimerEngine.IsPaused(new() { DurationSeconds = 60, RemainingSeconds = 0, PausedRemainingMilliseconds = 0 }));
        });
    }
    private static AppState FractionallyPausedState() => new() { ExtensionDisabledConfirmed = true,
        Timer = new() { DurationSeconds = 60, RemainingSeconds = 60, PausedRemainingMilliseconds = 59500,
            Volume = 0, LowTimePlayed = true, LowTime = new() { Enabled = false } } };
}
