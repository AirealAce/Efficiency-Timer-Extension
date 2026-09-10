using System.Reflection;
using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestEndEarly()
    {
        foreach (var selectedTab in Enumerable.Range(0, 5)) Test("timer shortcut starts the current duration from hidden tab " + selectedTab, () => {
            WithEndEarlyApp((app, _) => {
                var (main, tabs, duration, shortcut) = TimerShortcutControls(app);
                var timerPage = tabs.TabPages[0];
                var scheduledDuration = Descendants(tabs.TabPages[1]).OfType<DurationControl>().Single();
                scheduledDuration.LoadSeconds(999);
                app.Engine.SaveSchedule(null, DateTimeOffset.Now.AddHours(3), 60, false, 0);
                var schedules = JsonSerializer.Serialize(app.Engine.Snapshot.Schedules);
                var parts = Descendants(duration).OfType<DurationPartInput>().ToArray();
                parts.Single(x => x.AccessibleName == "Hours").Text = "1";
                parts.Single(x => x.AccessibleName == "Minutes").Text = "20";
                parts.Single(x => x.AccessibleName == "Seconds").Text = "100";
                var repeat = Descendants(timerPage).OfType<AutoRestartOptions>().Single();
                repeat.LoadOptions(true, DateTimeOffset.Now.AddHours(2).ToUnixTimeMilliseconds(), true);
                var cutoff = repeat.AutoRestartUntil;
                var low = Descendants(timerPage).OfType<LowTimeControl>().Single();
                low.LoadOptions(new() { Enabled = true, ThresholdSeconds = 19, Track = LibrarySound.None }, 60, true);
                Descendants(timerPage).OfType<VolumeControl>().Single().Value = 37;
                var appointment = Descendants(timerPage).OfType<SessionStartInput>().Single(x => x.AccessibleName == "Start timer at");
                appointment.Text = "unfinished appointment draft";
                tabs.SelectedIndex = selectedTab;
                Is(!main.Visible);
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                var state = app.Engine.Snapshot;
                Is(state.Timer.IsRunning); Equal(4900, state.Timer.DurationSeconds); Equal(37, state.Timer.Volume);
                Is(state.Timer.AutoRestart); Equal(cutoff, state.Timer.AutoRestartUntil); Equal(low.Selection, state.Timer.LowTime);
                Equal(4900, duration.Seconds); Is(!duration.Dirty); Equal(selectedTab, tabs.SelectedIndex); Is(!main.Visible);
                Equal(0, state.Prompts.Count); Equal(0, state.Outbox.Count);
                Equal(schedules, JsonSerializer.Serialize(state.Schedules)); Equal("unfinished appointment draft", appointment.Text);
                Equal(999, scheduledDuration.Seconds);
            });
        });
        Test("timer shortcut commits the actively edited field without requiring focus loss", () => {
            WithEndEarlyApp((app, _) => {
                var (main, tabs, duration, shortcut) = TimerShortcutControls(app);
                main.Show(); tabs.SelectedIndex = 0; duration.LoadSeconds(60);
                var seconds = Descendants(duration).OfType<DurationPartInput>().Single(x => x.AccessibleName == "Seconds");
                seconds.Focus(); seconds.Text = "100"; Is(seconds.ContainsFocus);
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                Is(app.Engine.Snapshot.Timer.IsRunning); Equal(160, app.Engine.Snapshot.Timer.DurationSeconds);
                Equal(160, duration.Seconds); Equal(0, app.Engine.Snapshot.Prompts.Count);
            });
        });
        Test("timer shortcut starts a finished session again at its configured duration", () => {
            WithEndEarlyApp((app, _) => {
                var (main, _, _, shortcut) = TimerShortcutControls(app);
                app.Engine.Start(75, false, 0); app.EndTimerEarly();
                var prompt = app.Engine.Snapshot.Prompts.Single(); app.Engine.SaveDraft(prompt.Id, "keep this reflection");
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                var state = app.Engine.Snapshot; Is(state.Timer.IsRunning); Equal(75, state.Timer.DurationSeconds);
                Equal(1, state.Prompts.Count); Equal("keep this reflection", state.Prompts.Single().Draft);
                Is(!main.Visible);
            });
        });
        foreach (var edited in new[] { false, true }) Test("timer shortcut handles paused duration edits " + edited, () => {
            WithEndEarlyApp((app, _) => {
                var (_, _, duration, shortcut) = TimerShortcutControls(app);
                if (edited) Descendants(duration).OfType<DurationPartInput>().Single(x => x.AccessibleName == "Seconds").Text = "45";
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                var state = app.Engine.Snapshot;
                Is(state.Timer.IsRunning); Equal(edited ? 165 : 120, state.Timer.DurationSeconds);
                Equal(edited ? 165 : 70, state.Timer.RemainingSeconds); Equal(0, state.Prompts.Count);
                Equal(0, state.Timer.Volume);
            }, new AppState { ExtensionDisabledConfirmed = true, Timer = new() { DurationSeconds = 120, RemainingSeconds = 70, Volume = 0 } });
        });
        foreach (var invalid in new[] { "0", "-1", "100000000000000000000000000000000000" })
            Test("timer shortcut rejects invalid duration without a phantom session: " + invalid, () => {
                WithEndEarlyApp((app, _) => {
                    var (main, _, duration, shortcut) = TimerShortcutControls(app); duration.LoadSeconds(0);
                    Descendants(duration).OfType<DurationPartInput>().Single(x => x.AccessibleName == "Seconds").Text = invalid;
                    var before = JsonSerializer.Serialize(app.Engine.Snapshot);
                    Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                    Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot)); Is(main.Visible);
                    Is(Descendants(main).OfType<Label>().Any(x => x.Text.StartsWith("Could not start the timer:")));
                });
            });
        Test("timer shortcut validates the auto-start cutoff and preserves its draft", () => {
            WithEndEarlyApp((app, _) => {
                var (main, tabs, _, shortcut) = TimerShortcutControls(app);
                var repeat = Descendants(tabs.TabPages[0]).OfType<AutoRestartOptions>().Single();
                repeat.LoadOptions(true, DateTimeOffset.Now.AddHours(-1).ToUnixTimeMilliseconds(), true);
                var before = JsonSerializer.Serialize(app.Engine.Snapshot); var cutoff = repeat.AutoRestartUntil;
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot)); Equal(cutoff, repeat.AutoRestartUntil); Is(main.Visible);
            });
        });
        Test("timer shortcut requires the extension switch-over confirmation", () => {
            WithEndEarlyApp((app, _) => {
                var (main, _, _, shortcut) = TimerShortcutControls(app);
                var before = JsonSerializer.Serialize(app.Engine.Snapshot);
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot)); Is(main.Visible);
            }, new AppState { Timer = new() { Volume = 0 } });
        });
        Test("timer shortcut start storage failure preserves timer, duration and reflections", () => {
            WithEndEarlyApp((app, directory) => {
                var (main, _, duration, shortcut) = TimerShortcutControls(app);
                duration.LoadSeconds(81);
                var before = JsonSerializer.Serialize(app.Engine.Snapshot);
                using var blocked = new FileStream(Path.Combine(directory, "state.dat.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot)); Equal(81, duration.Seconds); Is(main.Visible);
            });
        });
        Test("end early completes once at zero and preserves duration, preferences and future schedules", () => {
            var f = new Fixture(); f.Add(300, 90, true, 75);
            var settings = new LowTimeOptions { Enabled = true, ThresholdSeconds = 20, Track = LibrarySound.TrainerBattle };
            f.Engine.Start(120, false, 35, lowTime: settings); f.Move(15);
            var before = f.Engine.Snapshot; var writes = f.Store.Writes;
            Is(f.Engine.EndEarly()); Equal(writes + 1, f.Store.Writes);
            var state = f.Engine.Snapshot; Is(!state.Timer.IsRunning); Equal(0, state.Timer.RemainingSeconds); Is(state.Timer.EndTime is null);
            Equal(before.Timer with { IsRunning = false, RemainingSeconds = 0, PausedRemainingMilliseconds = 0, EndTime = null }, state.Timer);
            Equal(before.Schedules.Single(), state.Schedules.Single());
            var prompt = state.Prompts.Single(); Equal(120, prompt.DurationSeconds); Equal(35, prompt.Volume); Is(!prompt.IsTest);
            Equal(f.Time.ToUnixTimeMilliseconds(), prompt.CompletedAt); Equal(0, state.Outbox.Count);
            Is(!f.Engine.EndEarly()); f.Engine.Advance(); Equal(1, f.Engine.Snapshot.Prompts.Count);
            Equal(state.Timer, f.Restart().Snapshot.Timer);
        });
        Test("end early auto-restarts a full session while retaining existing reflection drafts", () => {
            var f = new Fixture(); var existing = f.Engine.TestPrompt(); f.Engine.SaveDraft(existing, "keep my draft");
            var cutoff = f.Time.AddHours(1).ToUnixTimeMilliseconds();
            var options = new LowTimeOptions { ThresholdSeconds = 50, Track = LibrarySound.None };
            f.Engine.Start(60, true, 42, cutoff, options); f.Move(11); f.Engine.Advance(); Is(f.Engine.Snapshot.Timer.LowTimePlayed);
            var before = f.Engine.Snapshot.Timer; Is(f.Engine.EndEarly()); var state = f.Engine.Snapshot;
            Is(state.Timer.IsRunning); Equal(60, TimerEngine.Remaining(state.Timer, f.Engine.Now));
            Equal(f.Engine.Now + 60000, state.Timer.EndTime); Is(state.Timer.AutoRestart); Equal(cutoff, state.Timer.AutoRestartUntil);
            Equal(before.Volume, state.Timer.Volume); Equal(options, state.Timer.LowTime); Is(!state.Timer.LowTimePlayed);
            Equal(2, state.Prompts.Count); Equal("keep my draft", state.Prompts.Single(x => x.Id == existing).Draft);
            f.Engine.Advance(); Equal(2, f.Engine.Snapshot.Prompts.Count); Equal(60, f.Engine.Snapshot.Timer.DurationSeconds);
        });
        foreach (var seconds in new[] { 20, 21 }) Test("end early respects cutoff at or after boundary " + seconds, () => {
            var f = new Fixture(); f.Engine.Start(60, true, 30, f.Time.AddSeconds(20).ToUnixTimeMilliseconds()); f.Move(seconds);
            Is(f.Engine.EndEarly()); var state = f.Engine.Snapshot;
            Is(!state.Timer.IsRunning); Is(!state.Timer.AutoRestart); Is(state.Timer.AutoRestartUntil is null); Equal(1, state.Prompts.Count);
        });
        Test("end early does not create sessions while idle, paused, or already finished", () => {
            var f = new Fixture(); var before = JsonSerializer.Serialize(f.Engine.Snapshot);
            Is(!f.Engine.EndEarly()); Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot)); Equal(0, f.Store.Writes);
            f.Engine.Start(60, true, 25); f.Move(5); f.Engine.Pause(); before = JsonSerializer.Serialize(f.Engine.Snapshot);
            var writes = f.Store.Writes; Is(!f.Engine.EndEarly()); Equal(writes, f.Store.Writes); Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot));
        });
        Test("end early after a deadline creates only its normal reflection and starts the due appointment atomically", () => {
            var f = new Fixture(); f.Add(20, 90); f.Engine.Start(10, false, 25); var deadline = f.Engine.Snapshot.Timer.EndTime;
            f.Move(21); Is(f.Engine.EndEarly()); Equal(deadline, f.Engine.Snapshot.Prompts.Single().CompletedAt);
            Equal(0, f.Engine.Snapshot.Schedules.Count); f.Engine.Advance();
            Equal(90, f.Engine.Snapshot.Timer.DurationSeconds); Equal(1, f.Engine.Snapshot.Prompts.Count); Equal(0, f.Engine.Snapshot.Schedules.Count);
        });
        Test("failed end-early save leaves timer, reflections and emitted events unchanged", () => {
            var f = new Fixture(); f.Engine.Start(600, true, 40); f.Move(100);
            var before = JsonSerializer.Serialize(f.Engine.Snapshot); var events = 0; f.Engine.Changed += () => events++;
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.EndEarly()); Equal(0, events);
            Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot));
            f.Store.Fail = false; Is(f.Engine.EndEarly()); Equal(1, events); Equal(1, f.Restart().Snapshot.Prompts.Count);
        });
        Test("backtick shortcut is independently registered, routed, repeat-suppressed and released", () => {
            var api = new FakeHotKey(); var focusCount = 0; var endCount = 0;
            using var focus = new GlobalShortcut(() => focusCount++, api);
            var end = new GlobalShortcut(() => endCount++, api, GlobalShortcut.EndEarlyKey, GlobalShortcut.EndEarlyId);
            Equal(2, api.Registrations.Count); var call = api.Registrations[1];
            Equal(0xC0u, call.Key); Equal(0x4003u, call.Modifiers); Equal(0x5255, call.Id);
            Is(!focus.Dispatch(GlobalShortcut.HotKeyMessage, call.Id)); Is(!end.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.HotKeyId));
            Is(end.Dispatch(GlobalShortcut.HotKeyMessage, call.Id)); Equal(1, endCount); Equal(0, focusCount);
            end.Dispose(); end.Dispose(); Equal((call.Window, call.Id), api.Unregistrations.Single());
            Is(!end.Dispatch(GlobalShortcut.HotKeyMessage, call.Id));
        });
        foreach (var blocked in new[] { GlobalShortcut.Key, GlobalShortcut.EndEarlyKey, GlobalShortcut.CompactKey, GlobalShortcut.CompactFocusKey, GlobalShortcut.ReflectionFocusKey })
            Test("one hotkey conflict leaves the other shortcut registered " + blocked, () => {
                WithEndEarlyApp((app, _) => {
                    var api = new FakeHotKey { BlockedKey = blocked }; app.EnableGlobalShortcut(api); app.EnableGlobalShortcut(api);
                    Equal(5, api.Registrations.Count);
                    var registered = app.Log.Recent().Where(x => x.Event == "shortcut.registered").ToArray(); Equal(4, registered.Length);
                    var unavailable = app.Log.Recent().Where(x => x.Event == "shortcut.unavailable").ToArray(); Equal(1, unavailable.Length);
                    Equal(blocked == GlobalShortcut.Key ? 1L : null, registered[0].Value);
                    app.Quit(); Equal(4, api.Unregistrations.Count);
                });
            });
        Test("end-early hotkey opens the normal popup from the tray and preserves draft and auto-restart", () => {
            WithEndEarlyApp((app, _) => {
                app.EnableGlobalShortcut(new FakeHotKey());
                var shortcut = (GlobalShortcut)typeof(TimerApplication).GetField("endEarlyShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
                app.Engine.Start(600, true, 0);
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                var popup = Application.OpenForms.OfType<ReflectionWindow>().Single(); Is(popup.Visible);
                Equal("Reflection Timer — ended early", popup.Text); Is(app.Engine.Snapshot.Timer.IsRunning);
                Equal(600, app.Engine.Snapshot.Timer.DurationSeconds); Equal(1, app.Engine.Snapshot.Prompts.Count);
                var text = Descendants(popup).OfType<TextBox>().Single(x => x.MaxLength == 5000); text.Text = "unsaved reflection draft";
                var bounds = popup.Bounds;
                Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.EndEarlyId));
                Equal(2, app.Engine.Snapshot.Prompts.Count); Equal("unsaved reflection draft", text.Text); Equal(bounds, popup.Bounds);
                Equal(1, Application.OpenForms.OfType<ReflectionWindow>().Count()); Equal(0, app.Engine.Snapshot.Outbox.Count);
                Equal(2, app.Log.Recent().Count(x => x.Event == "timer.endedEarly"));
            });
        });
        Test("end-early app failure opens an error without a phantom reflection", () => {
            WithEndEarlyApp((app, directory) => {
                app.Engine.Start(600, true, 0); var before = JsonSerializer.Serialize(app.Engine.Snapshot);
                using var blocked = new FileStream(Path.Combine(directory, "state.dat.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                app.EndTimerEarly(); Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot));
                Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count());
                var main = Application.OpenForms.OfType<MainWindow>().Single(); Is(main.Visible);
                Is(Descendants(main).OfType<Label>().Any(x => x.Text.StartsWith("Could not end the timer early")));
            });
        });
    }

    private static (MainWindow Main, TabControl Tabs, DurationControl Duration, GlobalShortcut Shortcut) TimerShortcutControls(TimerApplication app)
    {
        app.EnableGlobalShortcut(new FakeHotKey());
        var main = (MainWindow)typeof(TimerApplication).GetField("main", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var tabs = Descendants(main).OfType<TabControl>().Single();
        var duration = Descendants(tabs.TabPages[0]).OfType<DurationControl>().Single();
        var shortcut = (GlobalShortcut)typeof(TimerApplication).GetField("endEarlyShortcut", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        return (main, tabs, duration, shortcut);
    }

    private static void WithEndEarlyApp(Action<TimerApplication, string> test, AppState? initial = null, TimeProvider? shortcutClock = null,
        Func<nint>? foregroundWindow = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ReflectionTimer-QA-end-early-" + Guid.NewGuid().ToString("N"));
        var store = new EncryptedStore(directory); store.Save(initial ?? new AppState { ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 } });
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
        // Background test runners cannot reliably acquire Windows foreground
        // rights. Inject the active test form; production uses GetForegroundWindow.
        using var app = new TimerApplication(store, directory, show, updateStartup: _ => { }, shortcutTimeProvider: shortcutClock,
            shortcutForegroundWindow: foregroundWindow ?? (() => Form.ActiveForm?.Handle ?? 0));
        test(app, directory);
    }
}
