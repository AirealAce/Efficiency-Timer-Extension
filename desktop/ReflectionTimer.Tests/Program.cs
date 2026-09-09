using System.Net;
using System.Text;
using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static int passed, failed;
    private static readonly ConnectionSettings Connection = new() { SheetUrl = "https://docs.google.com/spreadsheets/d/synthetic-spreadsheet-id-for-tests/edit", WebAppUrl = "https://script.google.com/macros/s/test-deployment/exec", ApiToken = "unit-test-token-not-a-real-secret" };

    [STAThread]
    private static int Main(string[] args)
    {
        var smokeTheme = args switch {
            ["--theme-smoke", var selected] => Enum.Parse<AppColorTheme>(selected),
            ["--transport-preview", _, var selected] => Enum.Parse<AppColorTheme>(selected),
            _ => AppColorTheme.Dark
        };
        AppTheme.Initialize(() => smokeTheme);
        if (args is ["--transport-preview", var previewPath, _]) {
            WithEndEarlyApp((app, _) => {
                app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                using var preview = new Bitmap(mini.Width, mini.Height);
                mini.DrawToBitmap(preview, new Rectangle(Point.Empty, mini.Size));
                preview.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
            }); return 0;
        }
        if (args is ["--audio-smoke"]) {
            // Explicit hardware check only: normal tests never emit audio.
            return Task.Run(async () => {
                using var player = new AlertSoundPlayer();
                foreach (var kind in new[] { SoundEvent.Success, SoundEvent.Failure }) {
                    var result = await player.PlayAsync(SoundLibrary.Resolve(kind, new()), 5, SoundBehavior.Disruptive, kind);
                    Console.WriteLine($"{kind}: {result}"); if (result != AlertSoundResult.Played) return 1;
                }
                return 0;
            }).GetAwaiter().GetResult();
        }
        if (args.Length >= 2 && args[0] == "--seed-ui") {
            var path = args[1];
            var theme = args.Length == 3 ? Enum.Parse<AppColorTheme>(args[2]) : AppColorTheme.Dark;
            if (!Enum.IsDefined(theme)) throw new ArgumentException("Choose a valid QA theme.");
            // An isolated, unconnected fixture; never changes the production data or Chrome.
            if (!Path.GetFileName(path).StartsWith("ReflectionTimer-QA-", StringComparison.Ordinal)) throw new ArgumentException("Use a dedicated ReflectionTimer-QA-* directory.");
            if (Directory.Exists(path)) throw new ArgumentException("The QA directory must be new.");
            new EncryptedStore(path).Save(new AppState { Theme = theme, ExtensionDisabledConfirmed = true, Timer = new TimerState { Volume = 0, DurationSeconds = 10, RemainingSeconds = 10 } });
            return 0;
        }
        if (args is ["--seed-tiny-ui", var tinyPath, var tinyTheme]) {
            if (!Path.GetFileName(tinyPath).StartsWith("ReflectionTimer-QA-", StringComparison.Ordinal) || Directory.Exists(tinyPath)) throw new ArgumentException("Use a new isolated QA directory.");
            var theme = Enum.Parse<AppColorTheme>(tinyTheme);
            if (!Enum.IsDefined(theme)) throw new ArgumentException("Choose a valid QA theme.");
            new EncryptedStore(tinyPath).Save(new AppState {
                Theme = theme, ExtensionDisabledConfirmed = true, ShowFloatingTimer = true, FloatingPlacement = FloatingTimerPlacement.Center,
                Timer = new TimerState { DurationSeconds = 300, RemainingSeconds = 300, Volume = 0 }
            }); return 0;
        }
        if (args is ["--seed-update-ui", var qaPath, var qaTheme]) {
            if (!Path.GetFileName(qaPath).StartsWith("ReflectionTimer-QA-", StringComparison.Ordinal) || Directory.Exists(qaPath)) throw new ArgumentException("Use a new isolated QA directory.");
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            new EncryptedStore(qaPath).Save(new AppState {
                Theme = Enum.Parse<AppColorTheme>(qaTheme), ExtensionDisabledConfirmed = true, ShowFloatingTimer = true, ScheduleOverlap = ScheduleOverlapPolicy.Ask,
                Timer = new TimerState { DurationSeconds = 1800, RemainingSeconds = 1320, PausedRemainingMilliseconds = 1320000, Volume = 0 },
                Prompts = [new ReflectionPrompt(Guid.NewGuid(), now, 1800, 0, true) { ActualDurationSeconds = 480, EndedEarly = true }],
                Schedules = [new ScheduledSession(Guid.NewGuid(), now - 1000, 1200, false, 0) { AwaitingDecision = true }]
            }); return 0;
        }
        if (args is ["--session-details"]) {
            TestSessionDetails(); TestEndEarly(); TestFloatingTimer();
            Console.WriteLine($"\n{passed} passed; {failed} failed.");
            return failed == 0 ? 0 : 1;
        }
        if (args is ["--reliability-boundaries"]) {
            TestReliabilityBoundaries(); Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--compact"]) {
            TestTinyCountdown(); TestCompactUpdate(); TestFloatingTimer(); TestEndEarly(); TestReliabilityBoundaries(); TestVolumeSettings();
            Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--tiny"]) {
            TestTinyCountdown(); TestCompactUpdate(); TestFloatingTimer();
            Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--compact-focus"]) {
            TestCompactFocusShortcut();
            Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--hotkeys"]) {
            TestHotkeyRegistrationMatrix(); TestGlobalShortcut(); TestCompactFocusShortcut();
            TestTimerFocusShortcut(); TestCompactCycle(); TestReflectionFocus(); TestCheckIns();
            Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--reflection-focus"]) {
            TestReflectionFocus();
            Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--check-ins"]) {
            TestCheckIns(); TestCheckInHttp().GetAwaiter().GetResult();
            Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--theme-switch"]) {
            TestThemeSwitches(); Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--onboarding"]) {
            TestOnboarding(); Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--setup-preview", var setupPreviewDirectory]) {
            RenderSetupPreview(setupPreviewDirectory); return 0;
        }
        if (args is ["--theme-switch-preview", var themePreviewDirectory]) {
            RenderThemeSwitchPreview(themePreviewDirectory); return 0;
        }
        if (args is ["--timer-focus"]) {
            TestTimerFocusShortcut();
            Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--compact-transport"]) {
            TestCompactTransport();
            Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--probe-timer-focus-shortcut"]) {
            // Explicit availability check only; the normal suite never reserves a chord.
            using var probe = new GlobalShortcut(() => { }, key: GlobalShortcut.CompactFocusKey, id: GlobalShortcut.CompactFocusId);
            Console.WriteLine(probe.IsRegistered ? "Ctrl+Alt+. is available; probe released on exit." : "Ctrl+Alt+. could not be registered; no existing binding was changed.");
            return probe.IsRegistered ? 0 : 1;
        }
        if (args is ["--sync-safety"]) {
            TestSafeDelivery(); RunHttpTests().GetAwaiter().GetResult();
            Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--schedule-overlap"]) {
            TestScheduleOverlap(); Console.WriteLine($"\n{passed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
        }
        if (args is ["--theme-smoke", _]) {
            TestTheme(); TestStorage(); TestInputLayout();
            Console.WriteLine($"\n{passed} passed; {failed} failed. Theme: {AppTheme.Preference}");
            return failed == 0 ? 0 : 1;
        }
        Test("default and detached snapshots", () => { var f = new Fixture(); Equal(1500, f.Engine.Snapshot.Timer.DurationSeconds); f.Engine.Snapshot.Prompts.Add(new(Guid.NewGuid(), 0, 30, 0, true)); Equal(0, f.Engine.Snapshot.Prompts.Count); });
        TestOnboarding();
        TestInputLayout();
        TestSessionDetails();
        TestCheckIns();
        TestFloatingTimer();
        TestCompactUpdate();
        TestReflectionFocus();
        TestTimerFocusShortcut();
        TestTinyCountdown();
        TestSafeDelivery();
        TestScheduleOverlap();
        TestReliabilityBoundaries();
        Test("absolute deadline and ceiling", () => { var f = new Fixture(); f.Engine.Start(60, false, 500); f.Move(10.2); Equal(50, TimerEngine.Remaining(f.Engine.Snapshot.Timer, f.Engine.Now)); Equal(100, f.Engine.Snapshot.Timer.Volume); });
        Test("pause/resume retains remainder", () => { var f = new Fixture(); f.Engine.Start(60, false, 50); f.Move(20); f.Engine.Pause(); f.Move(100); Equal(40, f.Engine.Snapshot.Timer.RemainingSeconds); f.Engine.Resume(); f.Move(10); Equal(30, TimerEngine.Remaining(f.Engine.Snapshot.Timer, f.Engine.Now)); });
        Test("reset keeps independent future schedules", () => { var f = new Fixture(); f.Add(10, 30); f.Engine.Start(60, true, 10); f.Engine.Reset(100); Equal(100, f.Engine.Snapshot.Timer.RemainingSeconds); Equal(1, f.Engine.Snapshot.Schedules.Count); Is(!f.Engine.Snapshot.Timer.IsRunning); });
        foreach (var invalid in new[] { -1, 0, TimerEngine.MaxDuration + 1 }) Test("invalid duration " + invalid, () => { var f = new Fixture(); Throws<ArgumentException>(() => f.Engine.Start(invalid, false, 50)); Equal(0, f.Store.Writes); });
        Test("deadline completion happens once", () => { var f = new Fixture(); f.Engine.Start(10, false, 0); f.Move(10); f.Engine.Advance(); f.Engine.Advance(); Equal(1, f.Engine.Snapshot.Prompts.Count); Equal(0, f.Engine.Snapshot.Timer.RemainingSeconds); Is(!f.Engine.Snapshot.Timer.IsRunning); Is(!f.Engine.Snapshot.Prompts[0].IsTest); });
        Test("pause at deadline preserves reflection before UI tick", () => { var f = new Fixture(); f.Engine.Start(10, true, 0); f.Move(10); f.Engine.Pause(); f.Engine.Advance(); f.Engine.Pause(); Equal(1, f.Engine.Snapshot.Prompts.Count); Is(!f.Engine.Snapshot.Timer.IsRunning); Equal(0, f.Engine.Snapshot.Timer.RemainingSeconds); });
        Test("sleep catch-up does not flood repeated sessions", () => { var f = new Fixture(); f.Engine.Start(10, true, 17); f.Move(86400); f.Engine.Advance(); Equal(1, f.Engine.Snapshot.Prompts.Count); Equal(10, TimerEngine.Remaining(f.Engine.Snapshot.Timer, f.Engine.Now)); Equal(17, f.Engine.Snapshot.Timer.Volume); });
        Test("restart recovers original running deadline", () => { var f = new Fixture(); f.Engine.Start(10, false, 0); f.Move(20); var restored = f.Restart(); restored.Advance(); Equal(1, restored.Snapshot.Prompts.Count); Is(!restored.Snapshot.Timer.IsRunning); });
        Test("failed commit changes neither timer nor visible state", () => { var f = new Fixture(); f.Store.Fail = true; Throws<IOException>(() => f.Engine.Start(30, true, 70)); Equal(new TimerState(), f.Engine.Snapshot.Timer); });
        Test("idle advance does not write once per second", () => { var f = new Fixture(); for (var i = 0; i < 100; i++) f.Engine.Advance(); Equal(0, f.Store.Writes); });
        Test("schedule copies all independent options", () => { var f = new Fixture(); f.Add(5, 120, true, 37); f.Move(5); f.Engine.Advance(); var t = f.Engine.Snapshot.Timer; Equal(120, t.DurationSeconds); Is(t.AutoRestart); Equal(37, t.Volume); Equal(0, f.Engine.Snapshot.Schedules.Count); });
        Test("only latest missed schedule starts and future remains", () => { var f = new Fixture(); f.Add(5, 30); f.Add(10, 60); f.Add(20, 90); f.Move(15); f.Engine.Advance(); Equal(60, f.Engine.Snapshot.Timer.DurationSeconds); Equal(1, f.Engine.Snapshot.Schedules.Count); Equal(0, f.Engine.Snapshot.Prompts.Count); });
        Test("scheduled takeover preserves already-completed reflection", () => { var f = new Fixture(); f.Engine.Start(5, false, 0); f.Add(10, 60); f.Move(15); f.Engine.Advance(); Equal(1, f.Engine.Snapshot.Prompts.Count); Equal(5, f.Engine.Snapshot.Prompts[0].DurationSeconds); Equal(60, f.Engine.Snapshot.Timer.DurationSeconds); });
        Test("late scheduled takeover preserves the elapsed original session", () => { var f = new Fixture(); f.Engine.Start(100, false, 0); f.Add(10, 60); f.Move(200); f.Engine.Advance(); Equal(1, f.Engine.Snapshot.Prompts.Count); Equal(100, f.Engine.Snapshot.Prompts.Single().ActualDurationSeconds!.Value); Equal(60, f.Engine.Snapshot.Timer.DurationSeconds); });
        Test("schedule editing and removal use stable IDs", () => { var f = new Fixture(); var id = f.Add(5, 10); f.Engine.SaveSchedule(id, f.Time.AddSeconds(20), 30, true, 80); Equal(id, f.Engine.Snapshot.Schedules.Single().Id); Equal(30, f.Engine.Snapshot.Schedules[0].DurationSeconds); f.Engine.RemoveSchedule(id); Equal(0, f.Engine.Snapshot.Schedules.Count); });
        Test("schedule validation is atomic", () => { var f = new Fixture(); f.Add(5, 10); Throws<ArgumentException>(() => f.Add(5, 20)); Throws<ArgumentException>(() => f.Add(-1, 20)); Throws<ArgumentException>(() => f.Engine.SaveSchedule(Guid.NewGuid(), f.Time.AddSeconds(10), 20, false, 0)); Equal(1, f.Engine.Snapshot.Schedules.Count); });
        Test("50 schedule cap", () => { var f = new Fixture(); for (var i = 1; i <= 50; i++) f.Add(i, 10); Throws<ArgumentException>(() => f.Add(51, 10)); Equal(50, f.Engine.Snapshot.Schedules.Count); });
        Test("import skips past/duplicates and allocates new IDs", () => { var f = new Fixture(); f.Add(10, 10); var id = Guid.NewGuid(); f.Engine.ImportSchedules([new(id, f.Engine.Now - 1, 20, false, 0), new(id, f.Engine.Now + 10000, 20, false, 0), new(id, f.Engine.Now + 20000, 20, true, 500)]); Equal(2, f.Engine.Snapshot.Schedules.Count); Is(f.Engine.Snapshot.Schedules[1].Id != id); Equal(100, f.Engine.Snapshot.Schedules[1].Volume); });
        Test("over-limit import rolls back entire batch", () => { var f = new Fixture(); var rows = Enumerable.Range(1, 51).Select(i => new ScheduledSession(Guid.NewGuid(), f.Engine.Now + i * 1000, 10, false, 0)); Throws<ArgumentException>(() => f.Engine.ImportSchedules(rows)); Equal(0, f.Engine.Snapshot.Schedules.Count); });
        Test("draft persists across restart", () => { var f = new Fixture(); var id = f.Engine.TestPrompt(); f.Engine.SaveDraft(id, "private draft"); Equal("private draft", f.Restart().Snapshot.Prompts.Single().Draft); });
        Test("unchanged draft avoids disk writes; long draft capped", () => { var f = new Fixture(); var id = f.Engine.TestPrompt(); f.Engine.SaveDraft(id, "abc"); var writes = f.Store.Writes; f.Engine.SaveDraft(id, "abc"); Equal(writes, f.Store.Writes); f.Engine.SaveDraft(id, new string('a', 6000)); Equal(5000, f.Engine.Snapshot.Prompts[0].Draft.Length); });
        Test("queue commits before removing prompt, even on disk failure", () => { var f = new Fixture(); var id = f.Engine.TestPrompt(); f.Engine.SaveDraft(id, "draft"); f.Store.Fail = true; Throws<IOException>(() => f.Engine.QueueReflection(id, "reflection")); Equal(1, f.Engine.Snapshot.Prompts.Count); Equal(0, f.Engine.Snapshot.Outbox.Count); });
        Test("save retains original timestamp, offset, destination and test flag", () => { var f = new Fixture(); f.Engine.SaveSettings(Connection, true, false, true); var id = f.Engine.TestPrompt(); f.Move(3600); var timestamp = f.Time; f.Engine.QueueReflection(id, "  example  "); var item = f.Engine.Snapshot.Outbox.Single(); Equal(timestamp, item.SubmittedAt); Equal(TimeSpan.FromHours(-4), item.SubmittedAt.Offset); Equal(id, item.Id); Is(item.IsTest); Equal("example", item.Message); f.Engine.SaveSettings(Connection with { SheetMode = "fixed", SheetName = "changed" }, true, false, true); Equal("date", f.Engine.Snapshot.Outbox.Single().SheetMode); Equal(0, f.Engine.Snapshot.Prompts.Count); });
        Test("double save cannot queue twice", () => { var f = new Fixture(); var id = f.Queue(); Throws<ArgumentException>(() => f.Engine.QueueReflection(id, "twice")); Equal(1, f.Engine.Snapshot.Outbox.Count); });
        foreach (var text in new[] { " ", new string('x', 5001) }) Test("reject invalid reflection length " + text.Length, () => { var f = new Fixture(); var id = f.Engine.TestPrompt(); Throws<ArgumentException>(() => f.Engine.QueueReflection(id, text)); Equal(1, f.Engine.Snapshot.Prompts.Count); });
        Test("skip removes only selected prompt", () => { var f = new Fixture(); var id = f.Engine.TestPrompt(); f.Engine.TestPrompt(); f.Engine.SkipPrompt(id); Equal(1, f.Engine.Snapshot.Prompts.Count); });
        Test("upload state transitions and success", () => { var f = new Fixture(); var id = f.Queue(); var item = f.Engine.BeginUpload()!; Equal(DeliveryStatus.Sending, item.Status); Equal(1, item.Attempts); Is(f.Engine.BeginUpload() is null); f.Engine.FinishUpload(id, true, tab: "test"); Equal(DeliveryStatus.Sent, f.Engine.Snapshot.Outbox[0].Status); Equal("test", f.Engine.Snapshot.Outbox[0].SavedTab); });
        Test("ambiguous send waits for explicit retry", () => { var f = new Fixture(); var id = f.Queue(); f.Engine.BeginUpload(); f.Engine.FinishUpload(id, false, "timeout"); Is(f.Engine.BeginUpload() is null); f.Engine.RetryUpload(id); Equal(2, f.Engine.BeginUpload()!.Attempts); });
        Test("interrupted upload is not automatically retried", () => { var f = new Fixture(); f.Queue(); f.Engine.BeginUpload(); var restored = f.Restart(); Equal(DeliveryStatus.NeedsReview, restored.Snapshot.Outbox[0].Status); Equal("interrupted", restored.Snapshot.Outbox[0].ErrorKind); Is(restored.BeginUpload() is null); });
        Test("sending item cannot be retried", () => { var f = new Fixture(); var id = f.Queue(); f.Engine.BeginUpload(); Throws<ArgumentException>(() => f.Engine.RetryUpload(id)); });
        Test("confirmed arrival cannot resend", () => { var f = new Fixture(); var id = f.Queue(); f.Engine.BeginUpload(); f.Engine.FinishUpload(id, false, "network"); f.Engine.MarkAlreadySent(id); f.Engine.RetryUpload(id); Is(f.Engine.BeginUpload() is null); });
        Test("untrusted error content is redacted", () => { var f = new Fixture(); var id = f.Queue(); f.Engine.FinishUpload(id, false, "secret reflection contents"); Equal("unknown", f.Engine.Snapshot.Outbox[0].ErrorKind); });
        Test("sent history capped, unsent never pruned", () => { var f = new Fixture(); f.Store.Data = new AppState { Outbox = Enumerable.Range(0, 205).Select(i => new OutboxItem { Status = DeliveryStatus.Sent, SubmittedAt = f.Time.AddSeconds(i) }).Append(new OutboxItem { Status = DeliveryStatus.NeedsReview }).ToList() }; var engine = f.Restart(); var id = engine.TestPrompt(); engine.QueueReflection(id, "new"); Equal(202, engine.Snapshot.Outbox.Count); Equal(200, engine.Snapshot.Outbox.Count(x => x.Status == DeliveryStatus.Sent)); });
        Test("unknown data version not overwritten", () => { var f = new Fixture(); f.Store.Data = new AppState { FormatVersion = 99 }; Throws<InvalidDataException>(() => f.Restart()); Equal(0, f.Store.Writes); });
        foreach (var url in new[] { "https://script.google.com/home/projects/abc/edit", "http://script.google.com/macros/s/a/exec", "https://evil.test/macros/s/a/exec", "https://user@script.google.com/macros/s/a/exec", "https://script.google.com:444/macros/s/a/exec" }) Test("reject endpoint " + url, () => Is(SheetsClient.Validate(Connection with { WebAppUrl = url }) is not null));
        Test("valid connection and validation failures", () => { Is(SheetsClient.Validate(Connection) is null); Is(SheetsClient.Validate(Connection with { ApiToken = "short" }) is not null); Is(SheetsClient.Validate(Connection with { SheetUrl = "https://evil.test/sheet" }) is not null); Is(SheetsClient.Validate(Connection with { SheetMode = "oops" }) is not null); Is(SheetsClient.Validate(Connection with { SheetMode = "fixed", SheetName = "" }) is not null); });
        TestAutoRestartCutoff();
        TestDisplayPlacement();
        TestThemePreferences();
        TestThemeSwitches();
        TestGlobalShortcut();
        TestHotkeyRegistrationMatrix();
        Task.Run(TestAlertSounds).GetAwaiter().GetResult();
        TestLowTime();
        TestFadeOutSettings();
        Task.Run(TestAudioPolicies).GetAwaiter().GetResult();
        RunHttpTests().GetAwaiter().GetResult();
        TestStorage();
        TestTheme();
        Console.WriteLine($"\n{passed} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void TestGlobalShortcut()
    {
        TestEndEarly();
        Test("global shortcut registers exactly Ctrl Alt T with repeat suppression", () => {
            var api = new FakeHotKey(); using var shortcut = new GlobalShortcut(() => { }, api);
            Is(shortcut.IsRegistered); Equal(1, api.Registrations.Count);
            var call = api.Registrations.Single(); Is(call.Window != 0); Equal(0x5254, call.Id);
            Equal(0x4003u, call.Modifiers); Equal(0x54u, call.Key);
        });
        Test("global shortcut routes only its own message and id", () => {
            var count = 0; using var shortcut = new GlobalShortcut(() => count++, new FakeHotKey());
            Is(!shortcut.Dispatch(0x0100, GlobalShortcut.HotKeyId));
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.HotKeyId + 1));
            Equal(0, count); Is(shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.HotKeyId)); Equal(1, count);
        });
        Test("shortcut conflict is nonfatal and cannot trigger the callback", () => {
            var api = new FakeHotKey { Available = false }; var count = 0;
            using var shortcut = new GlobalShortcut(() => count++, api); Is(!shortcut.IsRegistered);
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.HotKeyId)); Equal(0, count);
            shortcut.Dispose(); Equal(0, api.Unregistrations.Count);
        });
        Test("disposing releases the shortcut once and ignores queued messages", () => {
            var api = new FakeHotKey(); var count = 0; var shortcut = new GlobalShortcut(() => count++, api);
            var handle = shortcut.Handle; shortcut.Dispose(); shortcut.Dispose();
            Equal((handle, GlobalShortcut.HotKeyId), api.Unregistrations.Single());
            Is(!shortcut.IsRegistered); Equal(nint.Zero, shortcut.Handle);
            Is(!shortcut.Dispatch(GlobalShortcut.HotKeyMessage, GlobalShortcut.HotKeyId)); Equal(0, count);
        });
    }

    private static void TestThemePreferences()
    {
        Test("theme migration and unknown values fall back to dark", () => {
            Equal(AppColorTheme.Dark, new AppState().Theme);
            Equal(AppColorTheme.Dark, JsonSerializer.Deserialize<AppState>("{}", DataJson.Options)!.Theme);
            Equal(AppColorTheme.Dark, AppTheme.Normalize((AppColorTheme)99));
            Equal(AppTheme.PaletteFor(AppColorTheme.Dark), AppTheme.PaletteFor((AppColorTheme)99));
        });
        foreach (var theme in Enum.GetValues<AppColorTheme>()) Test("theme persists without changing timer or drafts: " + theme, () => {
            var f = new Fixture(); f.Engine.Start(30, true, 37); f.Add(100, 60);
            var id = f.Engine.TestPrompt(); f.Engine.SaveDraft(id, "theme test draft");
            f.Engine.SetPopupPosition(ReflectionPopupPosition.BottomLeft);
            var before = f.Engine.Snapshot; f.Engine.SetTheme(theme);
            f.Engine.SaveSettings(Connection, true, false, true); f.Engine.SetAlertSound("");
            var saved = f.Restart().Snapshot; Equal(theme, saved.Theme);
            Equal(before.Timer, saved.Timer); Equal(before.Schedules.Single(), saved.Schedules.Single());
            Equal(before.Prompts.Single(), saved.Prompts.Single()); Equal(before.PopupPosition, saved.PopupPosition);
        });
        Test("invalid theme and failed save are atomic", () => {
            var f = new Fixture(); var changes = 0; f.Engine.Changed += () => changes++;
            Throws<ArgumentException>(() => f.Engine.SetTheme((AppColorTheme)99)); Equal(0, f.Store.Writes);
            f.Engine.SetTheme(AppColorTheme.Glamour); changes = 0; f.Store.Fail = true;
            Throws<IOException>(() => f.Engine.SetTheme(AppColorTheme.Light)); Equal(0, changes);
            Equal(AppColorTheme.Glamour, f.Engine.Snapshot.Theme);
        });
        foreach (var theme in Enum.GetValues<AppColorTheme>()) Test("readable theme palette: " + theme, () => {
            var p = AppTheme.PaletteFor(theme);
            foreach (var background in new[] { p.Background, p.Field, p.Raised })
                foreach (var foreground in new[] { p.Text, p.Muted, p.Warning, p.Error, p.Accent })
                    Is(Contrast(background, foreground) >= 4.5);
            Is(Contrast(p.PrimaryButton, p.AccentText) >= 4.5);
            Is(Contrast(p.Selection, p.SelectionText) >= 4.5);
            if (theme == AppColorTheme.HighContrast) {
                Is(Contrast(p.Background, p.Text) >= 7); Is(Contrast(p.Field, p.Border) >= 7);
            }
        });
        Test("Windows contrast colors override every theme without decorations", () => {
            foreach (var theme in Enum.GetValues<AppColorTheme>()) {
                var p = AppTheme.PaletteFor(theme, true); Is(p.IsSystemContrast); Is(!p.IsGlamour);
                Equal(SystemColors.Control, p.Background); Equal(SystemColors.Window, p.Field);
                Equal(SystemColors.ControlText, p.Text); Equal(SystemColors.Highlight, p.PrimaryButton);
            }
        });
    }

    private static void TestDisplayPlacement()
    {
        Test("state without placement preferences defaults to opposite bottom corners", () => {
            foreach (var state in new[] { new AppState(), JsonSerializer.Deserialize<AppState>("{\"FormatVersion\":1}", DataJson.Options)! }) {
                Equal(ReflectionPopupPosition.BottomRight, state.PopupPosition);
                Equal(FloatingTimerPlacement.BottomLeft, state.FloatingPlacement);
            }
        });
        Test("display preference survives restart and unrelated settings edits", () => {
            var f = new Fixture(); var id = f.Engine.TestPrompt(); f.Engine.SaveDraft(id, "retained draft");
            f.Engine.Start(60, true, 17); f.Add(100, 30);
            var before = f.Engine.Snapshot; var events = new List<Activity>(); f.Engine.ActivityRecorded += events.Add;
            f.Engine.SetPopupPosition(ReflectionPopupPosition.BottomRight);
            f.Engine.SaveSettings(Connection, true, false, true); f.Engine.SetAlertSound("");
            var restored = f.Restart().Snapshot; Equal(ReflectionPopupPosition.BottomRight, restored.PopupPosition);
            Equal(before.Timer, restored.Timer); Equal(before.Schedules.Single(), restored.Schedules.Single());
            Equal(before.Prompts.Single(), restored.Prompts.Single());
            var change = events.Single(x => x.Event == "display.changed"); Equal<long?>(4, change.Value);
        });
        Test("invalid display preference does not commit", () => {
            var f = new Fixture();
            foreach (var invalid in new[] { -1, 5, int.MaxValue })
                Throws<ArgumentException>(() => f.Engine.SetPopupPosition((ReflectionPopupPosition)invalid));
            Equal(0, f.Store.Writes); Equal(ReflectionPopupPosition.BottomRight, f.Engine.Snapshot.PopupPosition);
        });
        Test("failed display save retains previous preference and emits no change", () => {
            var f = new Fixture(); f.Engine.SetPopupPosition(ReflectionPopupPosition.TopLeft);
            var changes = 0; f.Engine.Changed += () => changes++; f.Store.Fail = true;
            Throws<IOException>(() => f.Engine.SetPopupPosition(ReflectionPopupPosition.TopRight));
            Equal(ReflectionPopupPosition.TopLeft, f.Engine.Snapshot.PopupPosition); Equal(0, changes);
        });
        var positions = new[] { new Point(680, 320), new Point(16, 56), new Point(1344, 56), new Point(16, 584), new Point(1344, 584) };
        foreach (var position in Enum.GetValues<ReflectionPopupPosition>()) Test("working-area placement " + position, () => {
            var area = new Rectangle(0, 40, 1920, 1000); var size = new Size(560, 440);
            var result = ReflectionPlacement.Calculate(area, size, position, 16);
            Equal(positions[(int)position], result); Is(area.Contains(new Rectangle(result, size)));
            var negative = area with { X = -1920, Y = -1040 };
            Equal(new Point(result.X - 1920, result.Y - 1080), ReflectionPlacement.Calculate(negative, size, position, 16));
        });
        Test("placement honors scaled size and margin", () => {
            Equal(new Point(1696, 732), ReflectionPlacement.Calculate(new(0, 0, 2560, 1416), new(840, 660), ReflectionPopupPosition.BottomRight, 24));
        });
        Test("placement reduces margins when there is little room", () => {
            foreach (var position in Enum.GetValues<ReflectionPopupPosition>()) {
                var area = new Rectangle(-100, 30, 570, 446); var size = new Size(560, 440);
                var point = ReflectionPlacement.Calculate(area, size, position, 16);
                Equal(new Point(-95, 33), point); Is(area.Contains(new Rectangle(point, size)));
            }
        });
        Test("oversized reflections keep their title bar on screen", () => {
            foreach (var position in Enum.GetValues<ReflectionPopupPosition>())
                Equal(new Point(-800, 40), ReflectionPlacement.Calculate(new(-800, 40, 400, 300), new(560, 440), position, 16));
        });
        Test("unknown saved placement falls back to center", () => {
            Equal(new Point(680, 320), ReflectionPlacement.Calculate(new(0, 40, 1920, 1000), new(560, 440), (ReflectionPopupPosition)99, 16));
        });
    }

    private static void TestAutoRestartCutoff()
    {
        Test("cutoff enables repeat and survives a restart", () => {
            var f = new Fixture(); var until = f.Engine.Now + 60000;
            f.Engine.Start(20, false, 35, until);
            Is(f.Engine.Snapshot.Timer.AutoRestart); Equal<long?>(until, f.Restart().Snapshot.Timer.AutoRestartUntil);
            f.Engine.SetPreferences(false, 40, until); Is(f.Engine.Snapshot.Timer.AutoRestart);
        });
        Test("mid-session cutoff disables repeat without changing countdown", () => {
            var f = new Fixture(); f.Engine.Start(100, true, 0, f.Engine.Now + 20000);
            var deadline = f.Engine.Snapshot.Timer.EndTime; var events = new List<Activity>(); f.Engine.ActivityRecorded += events.Add;
            f.Move(20); f.Engine.Advance(); var t = f.Engine.Snapshot.Timer;
            Is(t.IsRunning); Is(!t.AutoRestart); Is(t.AutoRestartUntil is null); Equal(deadline, t.EndTime);
            Equal(80, TimerEngine.Remaining(t, f.Engine.Now)); Equal(0, f.Engine.Snapshot.Prompts.Count);
            Equal("timer.autoRestartDisabled", events.Single().Event);
            var writes = f.Store.Writes; f.Engine.Advance(); Equal(writes, f.Store.Writes);
            f.Move(80); f.Engine.Advance(); Is(!f.Engine.Snapshot.Timer.IsRunning); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        Test("completion exactly at cutoff does not start another session", () => {
            var f = new Fixture(); f.Engine.Start(10, true, 0, f.Engine.Now + 10000);
            f.Move(10); f.Engine.Advance(); f.Engine.Advance(); Is(!f.Engine.Snapshot.Timer.IsRunning);
            Is(!f.Engine.Snapshot.Timer.AutoRestart); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        Test("repeating retains absolute cutoff across cycles", () => {
            var f = new Fixture(); var until = f.Engine.Now + 25000; f.Engine.Start(10, true, 17, until);
            f.Move(10); f.Engine.Advance(); Equal<long?>(until, f.Engine.Snapshot.Timer.AutoRestartUntil);
            f.Move(10); f.Engine.Advance(); Equal<long?>(until, f.Engine.Snapshot.Timer.AutoRestartUntil);
            f.Move(5); f.Engine.Advance(); Is(f.Engine.Snapshot.Timer.IsRunning); Is(!f.Engine.Snapshot.Timer.AutoRestart);
            f.Move(5); f.Engine.Advance(); Equal(3, f.Engine.Snapshot.Prompts.Count); Is(!f.Engine.Snapshot.Timer.IsRunning);
        });
        Test("paused and idle cutoffs expire without starting a session", () => {
            foreach (var paused in new[] { false, true }) {
                var f = new Fixture(); var until = f.Engine.Now + 10000;
                if (paused) { f.Engine.Start(60, true, 0, until); f.Move(2); f.Engine.Pause(); }
                else f.Engine.SetPreferences(false, 0, until);
                var remainder = f.Engine.Snapshot.Timer.RemainingSeconds;
                f.Move(10); f.Engine.Advance(); Is(!f.Engine.Snapshot.Timer.AutoRestart); Is(!f.Engine.Snapshot.Timer.IsRunning);
                Equal(remainder, f.Engine.Snapshot.Timer.RemainingSeconds); Equal(0, f.Engine.Snapshot.Prompts.Count);
            }
        });
        Test("pause resume and reset cannot revive an expired cutoff before tick", () => {
            foreach (var action in new[] { "pause", "resume", "reset" }) {
                var f = new Fixture(); f.Engine.Start(60, true, 0, f.Engine.Now + 10000);
                if (action == "resume") f.Engine.Pause();
                f.Move(10);
                if (action == "pause") f.Engine.Pause(); else if (action == "resume") f.Engine.Resume(); else f.Engine.Reset();
                Is(!f.Engine.Snapshot.Timer.AutoRestart); Is(f.Engine.Snapshot.Timer.AutoRestartUntil is null);
            }
        });
        Test("pause and resume before cutoff retain same absolute deadline", () => {
            var f = new Fixture(); var until = f.Engine.Now + 120000;
            f.Engine.Start(60, true, 0, until); f.Move(10); f.Engine.Pause(); f.Move(10); f.Engine.Resume();
            Equal<long?>(until, f.Engine.Snapshot.Timer.AutoRestartUntil); Equal(50, TimerEngine.Remaining(f.Engine.Snapshot.Timer, f.Engine.Now));
        });
        Test("sleep and process restart after cutoff do not repeat", () => {
            var f = new Fixture(); f.Engine.Start(10, true, 0, f.Engine.Now + 60000);
            f.Move(86400); var engine = f.Restart(); engine.Advance();
            Is(!engine.Snapshot.Timer.AutoRestart); Is(!engine.Snapshot.Timer.IsRunning); Equal(1, engine.Snapshot.Prompts.Count);
        });
        Test("schedule saves edits restores and applies its own cutoff", () => {
            var f = new Fixture(); var until = f.Engine.Now + 120000;
            var id = f.Engine.SaveSchedule(null, f.Time.AddSeconds(10), 30, false, 27, until);
            Is(f.Engine.Snapshot.Schedules[0].AutoRestart);
            f.Engine.SaveSchedule(id, f.Time.AddSeconds(20), 40, false, 37, until + 1000);
            var saved = f.Restart().Snapshot.Schedules.Single(); Equal(id, saved.Id); Equal<long?>(until + 1000, saved.AutoRestartUntil);
            f.Move(20); f.Engine.Advance(); var t = f.Engine.Snapshot.Timer;
            Equal<long?>(saved.AutoRestartUntil, t.AutoRestartUntil); Is(t.AutoRestart); Equal(40, t.DurationSeconds); Equal(37, t.Volume);
        });
        Test("late scheduled start runs once when its cutoff already passed", () => {
            var f = new Fixture(); f.Engine.SaveSchedule(null, f.Time.AddSeconds(10), 30, true, 0, f.Engine.Now + 20000);
            f.Move(25); f.Engine.Advance(); Is(f.Engine.Snapshot.Timer.IsRunning); Is(!f.Engine.Snapshot.Timer.AutoRestart);
            Is(f.Engine.Snapshot.Timer.AutoRestartUntil is null); Equal(30, TimerEngine.Remaining(f.Engine.Snapshot.Timer, f.Engine.Now));
            f.Move(30); f.Engine.Advance(); Is(!f.Engine.Snapshot.Timer.IsRunning); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        Test("live cutoff leaves independent future scheduled repeat intact", () => {
            var f = new Fixture(); f.Engine.Start(10, true, 0, f.Engine.Now + 10000);
            var until = f.Engine.Now + 50000; f.Engine.SaveSchedule(null, f.Time.AddSeconds(20), 30, true, 0, until);
            f.Move(10); f.Engine.Advance(); Equal(1, f.Engine.Snapshot.Schedules.Count);
            f.Move(10); f.Engine.Advance(); Is(f.Engine.Snapshot.Timer.AutoRestart); Equal<long?>(until, f.Engine.Snapshot.Timer.AutoRestartUntil);
        });
        Test("scheduled takeover at old cutoff applies new appointment options", () => {
            var f = new Fixture(); f.Engine.Start(10, true, 0, f.Engine.Now + 10000);
            f.Add(10, 30, true); f.Move(10); f.Engine.Advance();
            Is(f.Engine.Snapshot.Timer.AutoRestart); Is(f.Engine.Snapshot.Timer.AutoRestartUntil is null);
            Equal(30, f.Engine.Snapshot.Timer.DurationSeconds); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        Test("removing cutoff keeps repeat while disabling repeat clears cutoff", () => {
            var f = new Fixture(); f.Engine.Start(10, true, 0, f.Engine.Now + 60000);
            f.Engine.SetPreferences(true, 0); Is(f.Engine.Snapshot.Timer.AutoRestart); Is(f.Engine.Snapshot.Timer.AutoRestartUntil is null);
            f.Engine.SetPreferences(false, 0); Is(!f.Engine.Snapshot.Timer.AutoRestart);
        });
        Test("invalid past and out of range cutoffs reject atomically", () => {
            var f = new Fixture();
            foreach (var until in new[] { f.Engine.Now - 1, f.Engine.Now, long.MaxValue }) {
                Throws<ArgumentException>(() => f.Engine.Start(10, true, 0, until));
                Throws<ArgumentException>(() => f.Engine.SetPreferences(true, 0, until));
                Throws<ArgumentException>(() => f.Engine.SaveSchedule(null, f.Time.AddSeconds(10), 10, true, 0, until));
            }
            Throws<ArgumentException>(() => f.Engine.SaveSchedule(null, f.Time.AddSeconds(10), 10, true, 0, f.Engine.Now + 10000));
            Equal(0, f.Store.Writes);
        });
        Test("cutoff import normalizes repeat and invalid batch is atomic", () => {
            var f = new Fixture(); var start = f.Engine.Now + 10000; var until = start + 60000;
            f.Engine.ImportSchedules([new(Guid.NewGuid(), start, 30, false, 0, until)]);
            Is(f.Engine.Snapshot.Schedules[0].AutoRestart); Equal<long?>(until, f.Engine.Snapshot.Schedules[0].AutoRestartUntil);
            Throws<ArgumentException>(() => f.Engine.ImportSchedules([new(Guid.NewGuid(), start + 1000, 30, true, 0), new(Guid.NewGuid(), start + 2000, 30, true, 0, start)]));
            Equal(1, f.Engine.Snapshot.Schedules.Count);
        });
        Test("failed cutoff save stays atomic and next tick retries", () => {
            var f = new Fixture(); f.Engine.Start(10, true, 0, f.Engine.Now + 10000); f.Move(10);
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.Advance());
            Is(f.Engine.Snapshot.Timer.AutoRestart); Equal(0, f.Engine.Snapshot.Prompts.Count);
            f.Store.Fail = false; f.Engine.Advance(); Is(!f.Engine.Snapshot.Timer.AutoRestart); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        Test("old JSON defaults to unlimited repeat without data migration", () => {
            var old = JsonSerializer.Deserialize<AppState>("""{"Timer":{"AutoRestart":true},"Schedules":[{"Id":"00000000-0000-0000-0000-000000000001","StartTime":2000000000000,"DurationSeconds":10,"AutoRestart":true,"Volume":10}]}""", DataJson.Options)!;
            Is(old.Timer.AutoRestart); Is(old.Timer.AutoRestartUntil is null); Is(old.Schedules[0].AutoRestartUntil is null); Equal(1, old.FormatVersion);
        });
        Test("cutoff checkbox enables repeat in one event and can be cleared independently", () => {
            using var control = new AutoRestartOptions(); var boxes = Descendants(control).OfType<CheckBox>().ToArray();
            var input = Descendants(control).OfType<SessionStartInput>().Single(); var changed = 0; control.UserChanged += () => changed++;
            boxes[1].Checked = true; Is(control.AutoRestart); Is(control.AutoRestartUntil > DateTimeOffset.Now.ToUnixTimeMilliseconds()); Is(input.Enabled); Equal(1, changed);
            boxes[1].Checked = false; Is(control.AutoRestart); Is(control.AutoRestartUntil is null); Is(!input.Enabled); Equal(2, changed);
            boxes[1].Checked = true; boxes[0].Checked = false;
            Is(!boxes[1].Checked); Is(!control.AutoRestart); Is(control.AutoRestartUntil is null); Equal(4, changed);
        });
        Test("schedule cutoff default follows future start and model load is quiet", () => {
            var start = DateTime.Now.AddDays(3); using var control = new AutoRestartOptions(() => start);
            var changed = 0; control.UserChanged += () => changed++;
            Descendants(control).OfType<CheckBox>().Last().Checked = true;
            Is(control.AutoRestartUntil > new DateTimeOffset(start).ToUnixTimeMilliseconds()); Equal(1, changed);
            var until = control.AutoRestartUntil; control.LoadOptions(true, until, true); Equal(1, changed); Equal(until, control.AutoRestartUntil);
            var input = Descendants(control).OfType<SessionStartInput>().Single(); input.Text = "draft";
            control.LoadOptions(true, until); Equal("draft", input.Text); Throws<ArgumentException>(() => _ = control.AutoRestartUntil);
            control.LoadOptions(false, null); Is(!control.AutoRestart); Is(control.AutoRestartUntil is null); Equal(1, changed);
        });
    }

    private static async Task TestAlertSounds()
    {
        Test("old state defaults to the extension sound", () => {
            Equal("", JsonSerializer.Deserialize<AppState>("{}", DataJson.Options)!.AlertSoundPath);
            Equal("", new Fixture().Engine.Snapshot.AlertSoundPath);
        });
        Test("custom sound persists and does not change other settings", () => {
            var f = new Fixture(); var path = Path.Combine(Path.GetTempPath(), "my sound.MP3");
            f.Engine.Start(60, true, 37); var timer = f.Engine.Snapshot.Timer;
            f.Engine.SetAlertSound(path); Equal(path, f.Restart().Snapshot.AlertSoundPath); Equal(timer, f.Engine.Snapshot.Timer);
            f.Engine.SaveSettings(Connection, true, false, true); Equal(path, f.Engine.Snapshot.AlertSoundPath);
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.SetAlertSound("")); Equal(path, f.Engine.Snapshot.AlertSoundPath);
            f.Store.Fail = false; f.Engine.SetAlertSound(""); Equal("", f.Restart().Snapshot.AlertSoundPath);
        });
        Test("sound settings reject non-local or non-MP3 selections atomically", () => {
            var f = new Fixture();
            foreach (var path in new[] { "relative.mp3", "https://example.com/audio.mp3", Path.Combine(Path.GetTempPath(), "sound.wav") })
                Throws<ArgumentException>(() => f.Engine.SetAlertSound(path));
            Equal(0, f.Store.Writes);
        });
        Test("bundled sound is the original extension MP3 and decodes", () => {
            var path = AlertSoundPlayer.BundledPath;
            Equal("01714F0BF6EC9F13DFBE0CBEAF02C3F499A39681BC8DE28C6C2E4AD9CD3FFBFA", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
            Equal(path, Mp3AudioBackend.ValidateCustomFile(path));
            using var reader = new NAudio.Wave.AudioFileReader(path); var buffer = new byte[4096];
            Is(reader.TotalTime > TimeSpan.Zero); var audible = false;
            while (reader.Read(buffer, 0, buffer.Length) is var count && count > 0) audible |= buffer.Take(count).Any(b => b != 0);
            Is(audible); // The original clip starts with silence; inspect the whole file.
        });
        await TestAsync("empty selection plays bundled MP3 at clamped volume", async () => {
            var backend = new FakeAudio(); using var player = new AlertSoundPlayer(backend, "bundled.mp3");
            Equal(AlertSoundResult.Played, await player.PlayAsync("", 500)); Equal(("bundled.mp3", 100), backend.Calls.Single());
        });
        await TestAsync("custom MP3 uses the same session volume", async () => {
            var backend = new FakeAudio(); using var player = new AlertSoundPlayer(backend, "bundled.mp3");
            Equal(AlertSoundResult.Played, await player.PlayAsync("chosen.mp3", 37)); Equal(("chosen.mp3", 37), backend.Calls.Single());
        });
        await TestAsync("muted sound never opens an audio file or device", async () => {
            var backend = new FakeAudio(); using var player = new AlertSoundPlayer(backend);
            Equal(AlertSoundResult.Muted, await player.PlayAsync("missing.mp3", 0));
            Equal(AlertSoundResult.Muted, await player.PlayAsync("", -100)); Equal(0, backend.Calls.Count);
        });
        await TestAsync("unavailable custom sound falls back once at the same volume", async () => {
            var backend = new FakeAudio { Play = (path, _) => path == "missing.mp3" ? Task.FromException(new IOException("private path")) : Task.CompletedTask };
            using var player = new AlertSoundPlayer(backend, "bundled.mp3");
            Equal(AlertSoundResult.DefaultFallback, await player.PlayAsync("missing.mp3", 42));
            Equal(2, backend.Calls.Count); Equal(("bundled.mp3", 42), backend.Calls[1]);
        });
        await TestAsync("unavailable output reports failure without throwing or looping", async () => {
            var backend = new FakeAudio { Play = (_, _) => Task.FromException(new IOException("private driver data")) };
            using var player = new AlertSoundPlayer(backend, "bundled.mp3");
            Equal(AlertSoundResult.Failed, await player.PlayAsync("chosen.mp3", 50)); Equal(2, backend.Calls.Count);
        });
        await TestAsync("new sound cancels previous playback without overlap or fallback", async () => {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var active = 0; var peak = 0;
            var backend = new FakeAudio { Play = async (path, token) => {
                peak = Math.Max(peak, Interlocked.Increment(ref active));
                try { if (path == "first.mp3") { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } }
                finally { Interlocked.Decrement(ref active); }
            } };
            using var player = new AlertSoundPlayer(backend);
            var first = player.PlayAsync("first.mp3", 30); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = player.PlayAsync("second.mp3", 40);
            Equal(AlertSoundResult.Cancelled, await first.WaitAsync(TimeSpan.FromSeconds(5)));
            Equal(AlertSoundResult.Played, await second.WaitAsync(TimeSpan.FromSeconds(5))); Equal(1, peak); Equal(2, backend.Calls.Count);
        });
        await TestAsync("stop and disposal cancel audio without affecting timer state", async () => {
            foreach (var dispose in new[] { false, true }) {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var backend = new FakeAudio { Play = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } };
                using var player = new AlertSoundPlayer(backend); var playing = player.PlayAsync("chosen.mp3", 20);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (dispose) player.Dispose(); else player.Stop();
                Equal(AlertSoundResult.Cancelled, await playing.WaitAsync(TimeSpan.FromSeconds(5))); Equal(1, backend.Calls.Count);
                if (dispose) Equal(AlertSoundResult.Cancelled, await player.PlayAsync("", 20));
            }
        });
    }

    private static async Task RunHttpTests()
    {
        await TestDeliveryBoundaries();
        await TestCheckInHttp();
        await TestAsync("protected upload includes actual allotted reason and protocol", async () => {
            var item = new OutboxItem { Id = Guid.NewGuid(), Message = "synthetic reflection", DurationSeconds = 2104,
                ActualDurationSeconds = 480, EndedEarly = true, EarlyEndReason = "synthetic reason", RetryProtected = true,
                ReceiverUrl = Connection.WebAppUrl, SheetUrl = Connection.SheetUrl, SheetMode = "fixed", SheetName = "test", IsTest = true,
                SubmittedAt = DateTimeOffset.Parse("2026-09-07T12:00:00-04:00") };
            var handler = new FakeHttp(_ => Json("{\"success\":true,\"deliveryProtocol\":\"request-id-v1\",\"duplicate\":true,\"sheet\":\"test\"}"));
            using var client = new SheetsClient(handler); var reply = await client.Upload(Connection, item);
            Is(reply.Success); Is(reply.SupportsSafeRetry);
            using var body = JsonDocument.Parse(handler.Requests.Single().Body); var root = body.RootElement;
            Equal(2104, root.GetProperty("durationSeconds").GetInt32()); Equal(480, root.GetProperty("actualDurationSeconds").GetInt32());
            Is(root.GetProperty("endedEarly").GetBoolean()); Equal("synthetic reason", root.GetProperty("earlyEndReason").GetString());
            Equal(SheetsClient.DeliveryProtocol, root.GetProperty("deliveryProtocol").GetString());
        });
        await TestAsync("safe retry capability must be explicitly advertised", async () => {
            foreach (var protocol in new[] { "request-id-v1", "future-protocol", "" }) {
                using var client = new SheetsClient(new FakeHttp(_ => Json(JsonSerializer.Serialize(new { success = true, target = "Synthetic / test", deliveryProtocol = protocol }))));
                Equal(protocol == SheetsClient.DeliveryProtocol, (await client.Ping(Connection)).SupportsSafeRetry);
            }
        });
        await TestAsync("only transient HTTP failures are retryable", async () => {
            foreach (var status in new[] { 429, 500, 503, 403 }) {
                using var client = new SheetsClient(new FakeHttp(_ => new HttpResponseMessage((HttpStatusCode)status) {
                    Content = new StringContent("{\"success\":false}") }));
                var reply = await client.Ping(Connection); Is(!reply.Success); Equal(status != 403, reply.Retryable);
            }
        });
        await TestAsync("partial write and ID conflict require review without exposing raw errors", async () => {
            foreach (var code in new[] { "write_uncertain", "id_conflict" }) {
                using var client = new SheetsClient(new FakeHttp(_ => Json(JsonSerializer.Serialize(new { success = false, code, error = "private-secret" }))));
                var reply = await client.Ping(Connection); Equal(code, reply.ErrorKind); Is(!reply.Retryable); Is(!reply.DisplayMessage.Contains("private-secret"));
            }
        });
        await TestAsync("ping contract uses POST and no reflection", async () => { var handler = new FakeHttp(_ => Json("{\"success\":true,\"target\":\"Book / test\"}")); using var client = new SheetsClient(handler); var result = await client.Ping(Connection); Is(result.Success); Equal("Book / test", result.Target); using var body = JsonDocument.Parse(handler.Requests[0].Body); Equal("ping", body.RootElement.GetProperty("action").GetString()); Equal("", body.RootElement.GetProperty("message").GetString()); });
        await TestAsync("upload preserves test route and save-time zone", async () => { var f = new Fixture(); f.Engine.SaveSettings(Connection, true, false, true); f.Queue(); var item = f.Engine.Snapshot.Outbox.Single(); var handler = new FakeHttp(_ => Json("{\"success\":true,\"sheet\":\"test\"}")); using var client = new SheetsClient(handler); var result = await client.Upload(Connection, item); Is(result.Success); Equal("test", result.Tab); using var body = JsonDocument.Parse(handler.Requests[0].Body); var root = body.RootElement; Equal("appendReflection", root.GetProperty("action").GetString()); Is(root.GetProperty("isTest").GetBoolean()); Equal(240, root.GetProperty("timezoneOffsetMinutes").GetInt32()); Equal(item.SubmittedAt.UtcDateTime.ToString("O"), root.GetProperty("submittedAt").GetString()); Equal(item.Id.ToString(), root.GetProperty("requestId").GetString()); Equal("text/plain", handler.Requests[0].ContentType); });
        await TestAsync("Apps Script 302 switches to body-free GET", async () => { var handler = new FakeHttp(i => i == 0 ? Redirect("https://script.googleusercontent.com/macros/echo?test=1") : Json("{\"success\":true,\"target\":\"Synthetic / test\"}")); using var client = new SheetsClient(handler); Is((await client.Ping(Connection)).Success); Equal(HttpMethod.Get, handler.Requests[1].Method); Equal("", handler.Requests[1].Body); });
        foreach (var url in new[] { "https://evil.test/", "http://script.googleusercontent.com/a", "https://script.googleusercontent.com:444/a", "https://user@script.google.com/a" }) await TestAsync("credentials never follow redirect " + url, async () => { var handler = new FakeHttp(_ => Redirect(url, HttpStatusCode.TemporaryRedirect)); using var client = new SheetsClient(handler); Equal("invalid_response", (await client.Ping(Connection)).ErrorKind); Equal(1, handler.Requests.Count); });
        await TestAsync("redirect loop is bounded", async () => { var handler = new FakeHttp(_ => Redirect("https://script.google.com/macros/s/loop/exec")); using var client = new SheetsClient(handler); Equal("invalid_response", (await client.Ping(Connection)).ErrorKind); Equal(6, handler.Requests.Count); });
        foreach (var invalid in new[] { "[]", "null", "<html>sign in</html>", "\"text\"" }) await TestAsync("invalid server shape " + invalid, async () => { using var client = new SheetsClient(new FakeHttp(_ => Json(invalid))); Equal("invalid_response", (await client.Ping(Connection)).ErrorKind); });
        await TestAsync("server rejection never exposes raw errors", async () => { using var client = new SheetsClient(new FakeHttp(_ => Json("{\"success\":false,\"error\":\"private-secret\"}"))); var reply = await client.Ping(Connection); Equal("rejected", reply.ErrorKind); Is(!reply.DisplayMessage.Contains("private-secret")); });
        await TestAsync("network failure and timeout use safe categories", async () => { using var network = new SheetsClient(new FakeHttp(_ => throw new HttpRequestException("private secret"))); Equal("network", (await network.Ping(Connection)).ErrorKind); using var timeout = new SheetsClient(new FakeHttp(_ => throw new TaskCanceledException())); Equal("timeout", (await timeout.Ping(Connection)).ErrorKind); });
        await TestAsync("invalid setup never makes HTTP request", async () => { var handler = new FakeHttp(_ => throw new Exception("Must not call")); using var client = new SheetsClient(handler); Equal("settings_required", (await client.Ping(new())).ErrorKind); Equal(0, handler.Requests.Count); });
    }

    private static void TestStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReflectionTimer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            Test("focusing restores hidden minimized and maximized windows without state changes", () => {
                var directory = Path.Combine(root, "focus");
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var app = new TimerApplication(new EncryptedStore(directory), directory, show);
                app.Open(); var main = Application.OpenForms.OfType<MainWindow>().Single();
                var tabs = Descendants(main).OfType<TabControl>().Single(); tabs.SelectedIndex = 3;
                var before = JsonSerializer.Serialize(app.Engine.Snapshot);
                main.Hide(); app.Open(); Is(main.Visible); Equal(3, tabs.SelectedIndex);
                main.WindowState = FormWindowState.Minimized; app.Open(); Is(main.WindowState != FormWindowState.Minimized);
                main.WindowState = FormWindowState.Maximized; app.Open(); Equal(FormWindowState.Maximized, main.WindowState);
                main.WindowState = FormWindowState.Minimized; app.Open(); Equal(FormWindowState.Maximized, main.WindowState);
                Equal(before, JsonSerializer.Serialize(app.Engine.Snapshot));
            });
            Test("app shortcut registration is opt in for tests and released on quit", () => {
                var directory = Path.Combine(root, "shortcut-lifetime");
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var app = new TimerApplication(new EncryptedStore(directory), directory, show);
                var api = new FakeHotKey(); app.EnableGlobalShortcut(api); app.EnableGlobalShortcut(api);
                Equal(5, api.Registrations.Count); Is(app.Log.Recent().Any(x => x.Event == "shortcut.registered"));
                app.Quit(); Equal(5, api.Unregistrations.Count);
            });
            Test("shortcut conflict reports status and leaves app usable", () => {
                var directory = Path.Combine(root, "shortcut-conflict");
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var app = new TimerApplication(new EncryptedStore(directory), directory, show);
                app.EnableGlobalShortcut(new FakeHotKey { Available = false }); app.Open();
                var main = Application.OpenForms.OfType<MainWindow>().Single();
                Is(Descendants(main).OfType<Label>().Any(x => x.Text.StartsWith("Ctrl+Alt+T is unavailable")));
                Is(app.Log.Recent().Any(x => x.Event == "shortcut.unavailable")); Is(!app.Engine.Snapshot.Timer.IsRunning);
            });
            Test("theme saves encrypted and logs only its enum", () => {
                var directory = Path.Combine(root, "theme"); var store = new EncryptedStore(directory);
                store.Save(new AppState { Theme = AppColorTheme.Glamour }); Equal(AppColorTheme.Glamour, store.Load().Theme);
                var log = new DiagnosticLog(directory); log.Record("theme.changed", value: 3);
                Is(JsonSerializer.Serialize(log.Report(store.Load())).Contains("Glamour")); Equal(1, log.Recent().Count);
            });
            Test("theme selector saves and updates active windows while its sample stays passive", () => {
                var directory = Path.Combine(root, "theme-ui"); var store = new EncryptedStore(directory);
                store.Save(new AppState { Theme = AppTheme.Preference });
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var app = new TimerApplication(store, directory, show);
                using var main = new MainWindow(app); main.Render(app.Engine.Snapshot);
                var selector = Descendants(main).OfType<ComboBox>().Single(x => x.AccessibleName == "App theme");
                var preview = Descendants(main).OfType<ThemePreview>().Single();
                Equal(4, selector.Items.Count); Equal((int)AppTheme.Preference, selector.SelectedIndex);
                foreach (var theme in Enum.GetValues<AppColorTheme>()) {
                    selector.SelectedIndex = (int)theme; Equal(theme, store.Load().Theme);
                    Equal(AppTheme.Background, main.BackColor); Is(preview.AccessibleName!.StartsWith(AppTheme.Name(theme)));
                    Equal(0, Descendants(preview).OfType<Button>().Count()); Is(!preview.TabStop);
                }
                main.Show();
                using var bitmap = new Bitmap(main.Width, main.Height); main.DrawToBitmap(bitmap, main.ClientRectangle);
                using var prompt = new ReflectionWindow(app, new(Guid.NewGuid(), 0, 10, 0, true, "theme draft"));
                prompt.Show(); Equal(AppTheme.Background, prompt.BackColor);
                var editor = Descendants(prompt).OfType<TextBox>().Single(); Equal("theme draft", editor.Text);
                Equal(AppTheme.Field, editor.BackColor);
                var header = Descendants(prompt).OfType<ThemeHeader>().Single();
                Equal(AppTheme.Palette.IsGlamour ? "Georgia" : "Segoe UI", header.Font.Name);
                prompt.Close();
            });
            Test("display preference is encrypted and included in safe diagnostics", () => {
                var directory = Path.Combine(root, "display"); var store = new EncryptedStore(directory);
                var state = new AppState { PopupPosition = ReflectionPopupPosition.BottomLeft }; store.Save(state);
                Equal(state.PopupPosition, new EncryptedStore(directory).Load().PopupPosition);
                var log = new DiagnosticLog(directory); log.Record("display.changed", value: 3);
                var report = JsonSerializer.Serialize(log.Report(state)); Is(report.Contains("BottomLeft")); Equal(1, log.Recent().Count);
            });
            Test("display selector loads saves and preserves an open reflection position", () => {
                var directory = Path.Combine(root, "display-ui");
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var app = new TimerApplication(new EncryptedStore(directory), directory, show);
                using var main = new MainWindow(app);
                var selector = Descendants(main).OfType<ComboBox>().Single(x => x.AccessibleName == "Reflection popup position");
                main.Render(app.Engine.Snapshot); Equal(4, selector.SelectedIndex); Equal(5, selector.Items.Count);
                foreach (var position in Enum.GetValues<ReflectionPopupPosition>()) {
                    selector.SelectedIndex = (int)position; Equal(position, app.Engine.Snapshot.PopupPosition);
                    var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                    using var prompt = new ReflectionWindow(app, new(Guid.NewGuid(), 0, 10, 0, true));
                    prompt.Show();
                    Equal(ReflectionPlacement.Calculate(area, prompt.Size, position, (int)Math.Round(16 * prompt.DeviceDpi / 96d)), prompt.Location);
                    Is(prompt.ActiveControl is TextBox); var original = prompt.Location;
                    app.Engine.SetPopupPosition(ReflectionPopupPosition.Center);
                    Equal(original, prompt.Location); prompt.Close();
                }
                app.Engine.SetPopupPosition(ReflectionPopupPosition.BottomRight); main.Render(app.Engine.Snapshot);
                Equal(4, selector.SelectedIndex);
            });
            Test("invalid custom audio is rejected before saving", () => {
                var empty = Path.Combine(root, "empty.mp3"); File.WriteAllBytes(empty, []);
                var bad = Path.Combine(root, "bad.mp3"); File.WriteAllText(bad, "This is not MP3 audio.");
                var large = Path.Combine(root, "large.mp3"); using (var stream = File.Create(large)) stream.SetLength(50 * 1024 * 1024 + 1);
                foreach (var path in new[] { empty, bad, large, Path.Combine(root, "missing.mp3"), "relative.mp3", "https://example.com/sound.mp3" })
                    Throws<ArgumentException>(() => Mp3AudioBackend.ValidateCustomFile(path));
            });
            Test("sound path is encrypted and excluded from diagnostic exports", () => {
                var directory = Path.Combine(root, "sound-privacy"); var store = new EncryptedStore(directory);
                var state = new AppState { AlertSoundPath = Path.Combine(root, "private-audio-name.mp3") }; store.Save(state);
                state.Audio = new() { Success = new() { Mp3Path = state.AlertSoundPath, Behavior = SoundBehavior.Assertive } };
                state.Timer = state.Timer with { LowTime = new() { Enabled = true, Mp3Path = state.AlertSoundPath } };
                state.Schedules.Add(new(Guid.NewGuid(), 10000, 300, false, 50) { LowTime = state.Timer.LowTime }); store.Save(state);
                Equal(SoundBehavior.Assertive, store.Load().Audio!.Success.Behavior);
                Equal(state.AlertSoundPath, store.Load().Schedules.Single().LowTime.Mp3Path);
                Equal(state.AlertSoundPath, store.Load().AlertSoundPath);
                Is(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "state.dat"))).Contains("private-audio-name"));
                var log = new DiagnosticLog(directory); log.Record("sound.changed"); log.Record("sound.fallback");
                var report = JsonSerializer.Serialize(log.Report(state)); Is(!report.Contains("private-audio-name")); Is(!report.Contains(root)); Equal(2, log.Recent().Count);
            });
            Test("desktop startup constructs both timer and scheduling forms", () => {
                var directory = Path.Combine(root, "startup");
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var app = new TimerApplication(new EncryptedStore(directory), directory, show);
                app.OpenUnlessTray(false);
                Is(!app.Engine.Snapshot.Timer.IsRunning);
            });
            Test("Audio settings UI saves each mode, source and threshold independently", () => {
                var directory = Path.Combine(root, "audio-ui");
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var app = new TimerApplication(new EncryptedStore(directory), directory, show);
                using var main = new MainWindow(app, _ => { }); main.Render(app.Engine.Snapshot); main.Show();
                Descendants(main).OfType<TabControl>().Single().SelectedIndex = 3;
                var control = Descendants(main).OfType<AudioSettingsControl>().Single();
                var threshold = Descendants(control).OfType<NumericUpDown>().Single(x => x.AccessibleName == "Default low-time threshold in seconds"); threshold.Value = 120;
                var unitLabel = threshold.Parent!.Parent!.Controls.OfType<Label>().Single(x => x.Text == "seconds remaining");
                Equal(threshold.Parent.Height, unitLabel.Height); Equal(threshold.Parent.Top, unitLabel.Top);
                Equal(ContentAlignment.MiddleLeft, unitLabel.TextAlign);
                foreach (var kind in Enum.GetValues<SoundEvent>()) {
                    var behavior = Descendants(control).OfType<ComboBox>().Single(x => x.AccessibleName == kind + " playback behavior");
                    Equal(0, behavior.SelectedIndex); behavior.SelectedIndex = 1;
                    Equal(SoundBehavior.Assertive, AudioSettings.From(app.Engine.Snapshot).For(kind).Behavior);
                    var source = Descendants(control).OfType<ComboBox>().Single(x => x.AccessibleName == kind + " sound");
                    source.SelectedItem = SoundLibrary.Name(LibrarySound.ChampionBattle);
                    Equal(LibrarySound.ChampionBattle, AudioSettings.From(app.Engine.Snapshot).For(kind).Track);
                    Equal(SoundBehavior.Assertive, AudioSettings.From(app.Engine.Snapshot).For(kind).Behavior);
                    Equal(source.Parent!.Parent, behavior.Parent!.Parent);
                    Equal(source.Parent.Height, behavior.Parent.Height);
                    Equal(source.Parent.Top, behavior.Parent.Top);
                    var row = source.Parent.Parent!;
                    var preview = row.Controls.OfType<Button>().Single(x => x.Text == "Preview audio");
                    var choose = row.Controls.OfType<Button>().Single(x => x.Text == "Choose MP3…");
                    Equal(preview.Top, choose.Top); Equal(source.Parent.Top, preview.Top);
                    Is(preview.Right < choose.Left); Is(choose.Right <= row.ClientSize.Width);
                    control.LoadOptions(AudioSettings.From(app.Engine.Snapshot));
                    Equal(120, control.DefaultThresholdSeconds);
                }
                Is(!Descendants(control).OfType<Button>().Any(x => x.Text == "Save threshold"));
                Equal(60, AudioSettings.From(app.Engine.Snapshot).LowTimeThresholdSeconds);
                Descendants(main).OfType<Button>().Single(x => x.Text == "Save settings").PerformClick();
                Equal(120, AudioSettings.From(app.Engine.Snapshot).LowTimeThresholdSeconds);
                Equal(2, Descendants(main).OfType<LowTimeControl>().Count());
                Is(app.Engine.Snapshot.Timer.LowTime.Enabled); Is(!app.Engine.Snapshot.Timer.IsRunning);
            });
            Test("encrypted state round-trip without plaintext credentials", () => {
                var store = new EncryptedStore(Path.Combine(root, "roundtrip")); var state = new AppState { Connection = Connection };
                state.Prompts.Add(new(Guid.NewGuid(), 0, 1, 0, true, "private-draft-sentinel")); store.Save(state);
                var raw = File.ReadAllBytes(Path.Combine(root, "roundtrip", "state.dat")); Is(!Encoding.UTF8.GetString(raw).Contains(Connection.ApiToken)); Is(!Encoding.UTF8.GetString(raw).Contains("private-draft-sentinel"));
                Equal(Connection, store.Load().Connection); Equal("private-draft-sentinel", store.Load().Prompts[0].Draft);
            });
            Test("backup recovery pauses timers and quarantines pending uploads", () => {
                var directory = Path.Combine(root, "backup"); var store = new EncryptedStore(directory);
                var original = new AppState { ExtensionDisabledConfirmed = true, Timer = new TimerState { IsRunning = true, EndTime = DateTimeOffset.Now.AddMinutes(1).ToUnixTimeMilliseconds() }, Outbox = [new OutboxItem { Message = "original" }] };
                store.Save(original); store.Save(original with { LoggingEnabled = false }); File.WriteAllText(Path.Combine(directory, "state.dat"), "corrupt test data");
                var recovered = store.Load(); Is(store.RecoveryNotice is not null); Is(!recovered.Timer.IsRunning); Is(!recovered.ExtensionDisabledConfirmed); Equal(DeliveryStatus.NeedsReview, recovered.Outbox[0].Status);
                Equal(DeliveryStatus.NeedsReview, new EncryptedStore(directory).Load().Outbox[0].Status); Equal(1, Directory.GetFiles(directory, "state.dat.unreadable-*").Length);
            });
            Test("corruption without a backup is not silently reset", () => { var directory = Path.Combine(root, "no-backup"); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "state.dat"), "bad test data"); Throws<System.Security.Cryptography.CryptographicException>(() => new EncryptedStore(directory).Load()); Equal("bad test data", File.ReadAllText(Path.Combine(directory, "state.dat"))); });
            Test("diagnostic export excludes reflection reasons text and credentials", () => {
                var log = new DiagnosticLog(Path.Combine(root, "logs")); log.Record("timer.started", value: 30); log.Record("private-sentinel");
                var state = new AppState { Connection = Connection,
                    Prompts = [new(Guid.NewGuid(), 0, 1, 0, true, "private-sentinel") { EarlyEndReason = "private-reason" }],
                    Outbox = [new OutboxItem { Message = "private-sentinel", ErrorKind = "private-sentinel", EarlyEndReason = "private-reason",
                        ReceiverUrl = Connection.WebAppUrl, RetryProtected = true, ActualDurationSeconds = 8, DurationSeconds = 30, EndedEarly = true }] };
                var report = JsonSerializer.Serialize(log.Report(state)); Is(!report.Contains("private-sentinel")); Is(!report.Contains("private-reason"));
                Is(!report.Contains(Connection.ApiToken)); Is(!report.Contains(Connection.SheetUrl)); Is(!report.Contains(Connection.WebAppUrl));
                Is(report.Contains("3.12.2")); Is(report.Contains("RetryProtected")); Is(report.Contains("ScheduleOverlap")); Equal(1, log.Recent().Count);
            });
            Test("diagnostic opt-out, retention and clear", () => { var log = new DiagnosticLog(Path.Combine(root, "retention")); log.Record(new Activity(DateTimeOffset.Now.AddDays(-8).ToUnixTimeMilliseconds(), "timer.started")); Equal(0, log.Recent().Count); log.Enabled = false; log.Record("timer.started"); Equal(0, log.Recent().Count); log.Enabled = true; log.Record("timer.paused"); log.Clear(); Equal(0, log.Recent().Count); Equal(0, new DiagnosticLog(Path.Combine(root, "retention")).Recent().Count); });
            Test("duration input clears untouched 25-minute preset", () => { using var control = new DurationControl(); var numbers = Descendants(control).OfType<NumericUpDown>().ToArray(); numbers[0].Value = 1; Equal(3600, control.Seconds); Is(control.Dirty); control.LoadSeconds(1500, true); numbers[1].Value = 10; numbers[0].Value = 1; Equal(4200, control.Seconds); });
            Test("typed hours update immediately with a live preview subscriber", () => { using var control = new DurationControl(); var preview = 0; control.UserChanged += () => preview = control.Seconds; var numbers = Descendants(control).OfType<NumericUpDown>().ToArray(); numbers[0].Text = "1"; Equal(3600, preview); Equal(0m, numbers[1].Value); });
        }
        finally {
            // Only this test-created, uniquely named child of the system temp dir.
            var resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                && Path.GetFileName(resolved).StartsWith("ReflectionTimer-tests-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }
    private static void TestTheme()
    {
        Test("tab selection and separators contrast in every theme", () => {
            foreach (var theme in Enum.GetValues<AppColorTheme>()) {
                var p = AppTheme.PaletteFor(theme);
                Is(Contrast(p.PrimaryButton, p.AccentText) >= 4.5);
                Is(Contrast(p.Raised, p.Text) >= 4.5);
                Is(Contrast(p.Raised, p.Muted) >= 3);
                Is(Contrast(p.Raised, p.PrimaryButton) >= 3);
            }
        });
        Test("themed native tabs retain pages, drafts, selection and scalable header space", () => {
            using var form = new Form { ClientSize = new(880, 600), Font = new("Segoe UI", 10) };
            using var tabs = new ThemeTabs { Dock = DockStyle.Fill };
            form.Controls.Add(tabs);
            foreach (var name in new[] { "Timer", "Scheduling session times", "Outbox", "Settings", "Diagnostics" }) tabs.TabPages.Add(name);
            var draft = new TextBox { Text = "unsaved draft" }; tabs.TabPages[0].Controls.Add(draft);
            AppTheme.Apply(form); form.Show();
            Equal(TabDrawMode.OwnerDrawFixed, tabs.DrawMode); Equal(TabAppearance.FlatButtons, tabs.Appearance);
            Equal("App sections", tabs.AccessibleName);
            Is(tabs.ItemSize.Height > tabs.Font.Height + 10);
            using var bold = new Font(tabs.Font, FontStyle.Bold);
            for (var i = 0; i < tabs.TabCount; i++) {
                tabs.SelectedIndex = i; tabs.Refresh();
                Equal(i, tabs.SelectedIndex);
                var bounds = tabs.GetTabRect(i);
                Is(bounds.Width > 40); Is(bounds.Right <= tabs.ClientSize.Width);
                Is(bounds.Width - 16 * tabs.DeviceDpi / 96 >= TextRenderer.MeasureText(tabs.TabPages[i].Text, bold).Width);
                if (i > 0) Is(bounds.Left >= tabs.GetTabRect(i - 1).Right);
            }
            tabs.SelectedIndex = 0; draft.Focus();
            Equal(0, tabs.SelectedIndex); Equal("unsaved draft", draft.Text); Is(!draft.IsDisposed);
            using var bitmap = new Bitmap(tabs.Width, tabs.Height); tabs.DrawToBitmap(bitmap, tabs.ClientRectangle);
            var selected = tabs.GetTabRect(0); var inactive = tabs.GetTabRect(1);
            Equal(AppTheme.Palette.PrimaryButton.ToArgb(), bitmap.GetPixel(selected.X + 8, selected.Y + 8).ToArgb());
            Equal(AppTheme.Raised.ToArgb(), bitmap.GetPixel(inactive.X + 8, inactive.Y + 8).ToArgb());
            var previousHeight = tabs.ItemSize.Height;
            using var larger = new Font("Segoe UI", 24); tabs.Font = larger;
            Is(tabs.ItemSize.Height > previousHeight);
        });
        Test("dark date/time editor preserves local time and validates input", () => {
            using var input = new SessionStartInput { Value = new DateTime(2026, 9, 5, 18, 30, 45, DateTimeKind.Local) };
            Equal("09/05/2026 06:30 PM", input.Text); Equal(18, input.Value.Hour); Equal(30, input.Value.Minute);
            Equal(DateTimeKind.Local, input.Value.Kind); Equal(0, input.Value.Second);
            Equal(0, SessionStartInput.Parse("9/5/2026 12:00 AM").Hour);
            Equal(12, SessionStartInput.Parse("9/5/2026 12:00 PM").Hour);
            Throws<ArgumentException>(() => SessionStartInput.Parse("02/30/2026 01:00 PM"));
            Throws<ArgumentException>(() => SessionStartInput.Parse("not a date"));
            AppTheme.Apply(input); Equal(AppTheme.Field, input.BackColor);
        });
        Test("dark theme text and semantic colors have readable contrast", () => {
            if (SystemInformation.HighContrast) return; // The user's accessibility palette takes precedence.
            foreach (var background in new[] { AppTheme.Background, AppTheme.Field, AppTheme.Raised })
                foreach (var foreground in new[] { AppTheme.Text, AppTheme.Muted, AppTheme.Warning, AppTheme.Error, AppTheme.Accent })
                    Is(Contrast(background, foreground) >= 4.5);
            Is(Contrast(AppTheme.Accent, AppTheme.AccentText) >= 4.5);
            Is(Contrast(AppTheme.Selection, AppTheme.SelectionText) >= 4.5);
        });
        Test("theme styles nested inputs without changing values or masking", () => {
            using var form = new Form(); var panel = new FlowLayoutPanel(); form.Controls.Add(panel);
            var input = new TextBox { Text = "unchanged", UseSystemPasswordChar = true };
            var number = new NumericUpDown { Value = 12 }; var check = new CheckBox { Checked = true };
            var warning = new Label { Text = "Warning", ForeColor = AppTheme.Warning };
            panel.Controls.AddRange([input, number, check, warning]); AppTheme.Apply(form); AppTheme.Apply(form);
            Equal(AppTheme.Background, form.BackColor); Equal(AppTheme.Background, panel.BackColor);
            Equal(AppTheme.Field, input.BackColor); Equal(AppTheme.Field, number.BackColor);
            Equal("unchanged", input.Text); Is(input.UseSystemPasswordChar); Equal(12m, number.Value); Is(check.Checked);
            Equal(AppTheme.Warning, warning.ForeColor);
        });
        Test("dark table theme covers headers, rows and selected cells", () => {
            using var grid = Widgets.Grid("Entry", "Status"); grid.Rows.Add("example", "Pending");
            Equal(AppTheme.Field, grid.BackgroundColor); Is(!grid.EnableHeadersVisualStyles);
            Equal(AppTheme.Raised, grid.ColumnHeadersDefaultCellStyle.BackColor);
            Equal(AppTheme.Selection, grid.DefaultCellStyle.SelectionBackColor);
            Equal(AppTheme.SelectionText, grid.DefaultCellStyle.SelectionForeColor);
            Equal("example", grid.Rows[0].Cells[0].Value);
        });
        Test("primary and secondary buttons retain their dark-theme roles", () => {
            using var primary = Widgets.Button("Save & send", (_, _) => { }, true);
            using var secondary = Widgets.Button("Later", (_, _) => { });
            using var form = new Form(); form.Controls.AddRange([primary, secondary]);
            var original = primary.BackColor; AppTheme.Apply(form);
            Equal(original, primary.BackColor); Equal(AppTheme.AccentText, primary.ForeColor);
            Equal(AppTheme.Raised, secondary.BackColor); Equal(AppTheme.Text, secondary.ForeColor);
            Is(!primary.UseMnemonic); Equal(FlatStyle.Flat, secondary.FlatStyle);
        });
    }
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte value) { var v = value / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var first = Luminance(a); var second = Luminance(b); return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descendants(x)));
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static HttpResponseMessage Redirect(string value, HttpStatusCode status = HttpStatusCode.Found) { var response = new HttpResponseMessage(status); response.Headers.Location = new Uri(value); return response; }
    private static void Test(string name, Action action) { try { action(); Console.WriteLine("PASS " + name); passed++; } catch (Exception error) { Console.WriteLine("FAIL " + name + ": " + error); failed++; } }
    private static async Task TestAsync(string name, Func<Task> action) { try { await action(); Console.WriteLine("PASS " + name); passed++; } catch (Exception error) { Console.WriteLine("FAIL " + name + ": " + error); failed++; } }
    private static void Is(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class MemoryStore : IStateStore
    {
        public AppState Data = new(); public int Writes; public bool Fail;
        public AppState Load() => DataJson.Clone(Data);
        public void Save(AppState state) { if (Fail) throw new IOException("Simulated disk failure"); Data = DataJson.Clone(state); Writes++; }
    }
    private sealed class FakeHotKey : IHotKeyRegistration
    {
        public bool Available = true;
        public uint? BlockedKey;
        public readonly List<(nint Window, int Id, uint Modifiers, uint Key)> Registrations = [];
        public readonly List<(nint Window, int Id)> Unregistrations = [];
        public bool Register(nint window, int id, uint modifiers, uint key) {
            Registrations.Add((window, id, modifiers, key)); return Available && BlockedKey != key;
        }
        public bool Unregister(nint window, int id) { Unregistrations.Add((window, id)); return true; }
    }
    private sealed class Fixture
    {
        public DateTimeOffset Time = new(2026, 9, 5, 15, 0, 0, TimeSpan.FromHours(-4));
        public readonly MemoryStore Store = new(); public readonly TimerEngine Engine;
        public Fixture() => Engine = new(Store, () => Time);
        public void Move(double seconds) => Time = Time.AddSeconds(seconds);
        public TimerEngine Restart() => new(Store, () => Time);
        public Guid Add(int secondsFromNow, int duration, bool repeat = false, int volume = 0) => Engine.SaveSchedule(null, Time.AddSeconds(secondsFromNow), duration, repeat, volume);
        public Guid Queue() { var id = Engine.TestPrompt(); Engine.QueueReflection(id, "unit test reflection"); return id; }
    }
    private sealed class FakeAudio : IAlertAudioBackend
    {
        public readonly List<(string Path, int Volume)> Calls = [];
        public Func<string, CancellationToken, Task> Play = (_, _) => Task.CompletedTask;
        public Task PlayAsync(string path, AudioLevel level, CancellationToken cancellationToken) {
            Calls.Add((path, level.Volume)); return Play(path, cancellationToken);
        }
    }
    private sealed class FakeHttp(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Body, string? ContentType)> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add((request.Method, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken), request.Content?.Headers.ContentType?.MediaType));
            return respond(Requests.Count - 1);
        }
    }
}
