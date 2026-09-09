using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestTimerScheduling()
    {
        Test("low-time audio defaults on and saved opt-outs survive reload", () => {
            Is(new TimerState().LowTime.Enabled);
            Is(new ScheduledSession(Guid.NewGuid(), 0, 60, false, 0).LowTime.Enabled);
            Is(JsonSerializer.Deserialize<LowTimeOptions>("{}", DataJson.Options)!.Enabled);
            var disabled = JsonSerializer.Deserialize<LowTimeOptions>("{\"Enabled\":false}", DataJson.Options)!;
            Is(!DataJson.Clone(disabled).Enabled);
            using var control = new LowTimeControl();
            Is(control.Selection.Enabled);
            control.LoadOptions(disabled, 60); Is(!control.Selection.Enabled);
        });
        Test("Timer page queues a one-time session with its current duration and audio", () => {
            WithTimerWindow((app, main, store) => {
                var tab = Descendants(main).OfType<TabControl>().Single().TabPages[0];
                var whenInput = Descendants(tab).OfType<SessionStartInput>().Single(x => x.AccessibleName == "Start timer at");
                var when = DateTimeOffset.Now.AddMinutes(10);
                whenInput.Value = when.LocalDateTime; when = new(whenInput.Value);
                var duration = Descendants(tab).OfType<DurationControl>().Single(); duration.LoadSeconds(125);
                var low = Descendants(tab).OfType<LowTimeControl>().Single();
                Is(low.Selection.Enabled);
                Equal("Low on time audio", Descendants(low).OfType<CheckBox>().First().Text);
                low.LoadOptions(new() { Enabled = true, ThresholdSeconds = 45, Track = LibrarySound.None }, 60, true);
                var before = app.Engine.Snapshot.Timer;
                Descendants(tab).OfType<Button>().Single(x => x.Text == "Schedule session").PerformClick();
                Equal(before, app.Engine.Snapshot.Timer); // Scheduling must not start or reset the live timer.
                var entry = store.Load().Schedules.Single();
                Equal(when.ToUnixTimeMilliseconds(), entry.StartTime); Equal(125, entry.DurationSeconds);
                Is(!entry.AutoRestart); Is(entry.AutoRestartUntil is null); Equal(23, entry.Volume);
                Equal(low.Selection, entry.LowTime);
                var repeat = Descendants(tab).OfType<AutoRestartOptions>().Single();
                Is(whenInput.PointToScreen(Point.Empty).Y < repeat.PointToScreen(Point.Empty).Y);
                var time = when;
                var engine = new TimerEngine(store, () => time); engine.Advance();
                Is(engine.Snapshot.Timer.IsRunning); Equal(0, engine.Snapshot.Schedules.Count);
                Equal(entry.LowTime, engine.Snapshot.Timer.LowTime);
                time = time.AddSeconds(125); engine.Advance();
                Is(!engine.Snapshot.Timer.IsRunning); Equal(1, engine.Snapshot.Prompts.Count);
            });
        });
        Test("Timer page rejects invalid starts and cutoffs without changing timer or queue", () => {
            WithTimerWindow((app, main, store) => {
                var tab = Descendants(main).OfType<TabControl>().Single().TabPages[0];
                var input = Descendants(tab).OfType<SessionStartInput>().Single(x => x.AccessibleName == "Start timer at");
                var save = Descendants(tab).OfType<Button>().Single(x => x.Text == "Schedule session");
                var before = app.Engine.Snapshot.Timer;
                input.Text = "not a time"; save.PerformClick(); Equal(0, store.Load().Schedules.Count);
                Equal("not a time", input.Text);
                input.Value = DateTime.Now.AddMinutes(-2); save.PerformClick(); Equal(0, store.Load().Schedules.Count);
                input.Value = DateTime.Now.AddMinutes(10);
                var repeat = Descendants(tab).OfType<AutoRestartOptions>().Single();
                repeat.LoadOptions(true, new DateTimeOffset(input.Value.AddMinutes(-1)).ToUnixTimeMilliseconds(), true);
                save.PerformClick(); Equal(0, store.Load().Schedules.Count); Equal(before, app.Engine.Snapshot.Timer);
                var until = new DateTimeOffset(input.Value.AddHours(1)).ToUnixTimeMilliseconds();
                repeat.LoadOptions(true, until, true); save.PerformClick();
                var saved = store.Load().Schedules.Single(); Is(saved.AutoRestart); Equal(until, saved.AutoRestartUntil);
                save.PerformClick(); Equal(1, store.Load().Schedules.Count); // A duplicate click cannot double-book.
            });
        });
    }

    private static void WithTimerWindow(Action<TimerApplication, MainWindow, EncryptedStore> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-quick-schedule-" + Guid.NewGuid().ToString("N"));
        var store = new EncryptedStore(directory);
        store.Save(new AppState { ExtensionDisabledConfirmed = true, Timer = new() { Volume = 23 } });
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
        using var app = new TimerApplication(store, directory, show, updateStartup: _ => { });
        using var main = new MainWindow(app, _ => { }); main.Render(app.Engine.Snapshot); main.Show();
        Descendants(main).OfType<TabControl>().Single().SelectedIndex = 0;
        test(app, main, store);
    }
}
