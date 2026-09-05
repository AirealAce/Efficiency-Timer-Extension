using System.Net;
using System.Text;
using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static class Program
{
    private static int passed, failed;
    private static readonly ConnectionSettings Connection = new() { WebAppUrl = "https://script.google.com/macros/s/test-deployment/exec", ApiToken = "unit-test-token-not-a-real-secret" };

    [STAThread]
    private static int Main(string[] args)
    {
        DarkTheme.Initialize();
        if (args is ["--seed-ui", var path]) {
            // An isolated, unconnected fixture; never changes the production data or Chrome.
            if (!Path.GetFileName(path).StartsWith("ReflectionTimer-QA-", StringComparison.Ordinal)) throw new ArgumentException("Use a dedicated ReflectionTimer-QA-* directory.");
            if (Directory.Exists(path)) throw new ArgumentException("The QA directory must be new.");
            new EncryptedStore(path).Save(new AppState { ExtensionDisabledConfirmed = true, Timer = new TimerState { Volume = 0, DurationSeconds = 10, RemainingSeconds = 10 } });
            return 0;
        }
        Test("default and detached snapshots", () => { var f = new Fixture(); Equal(1500, f.Engine.Snapshot.Timer.DurationSeconds); f.Engine.Snapshot.Prompts.Add(new(Guid.NewGuid(), 0, 30, 0, true)); Equal(0, f.Engine.Snapshot.Prompts.Count); });
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
        Test("takeover does not invent reflection for unfinished timer", () => { var f = new Fixture(); f.Engine.Start(100, false, 0); f.Add(10, 60); f.Move(200); f.Engine.Advance(); Equal(0, f.Engine.Snapshot.Prompts.Count); Equal(60, f.Engine.Snapshot.Timer.DurationSeconds); });
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
        RunHttpTests().GetAwaiter().GetResult();
        TestStorage();
        TestTheme();
        Console.WriteLine($"\n{passed} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task RunHttpTests()
    {
        await TestAsync("ping contract uses POST and no reflection", async () => { var handler = new FakeHttp(_ => Json("{\"success\":true,\"target\":\"Book / test\"}")); using var client = new SheetsClient(handler); var result = await client.Ping(Connection); Is(result.Success); Equal("Book / test", result.Target); using var body = JsonDocument.Parse(handler.Requests[0].Body); Equal("ping", body.RootElement.GetProperty("action").GetString()); Equal("", body.RootElement.GetProperty("message").GetString()); });
        await TestAsync("upload preserves test route and save-time zone", async () => { var f = new Fixture(); f.Engine.SaveSettings(Connection, true, false, true); f.Queue(); var item = f.Engine.Snapshot.Outbox.Single(); var handler = new FakeHttp(_ => Json("{\"success\":true,\"sheet\":\"test\"}")); using var client = new SheetsClient(handler); var result = await client.Upload(Connection, item); Is(result.Success); Equal("test", result.Tab); using var body = JsonDocument.Parse(handler.Requests[0].Body); var root = body.RootElement; Equal("appendReflection", root.GetProperty("action").GetString()); Is(root.GetProperty("isTest").GetBoolean()); Equal(240, root.GetProperty("timezoneOffsetMinutes").GetInt32()); Equal(item.SubmittedAt.UtcDateTime.ToString("O"), root.GetProperty("submittedAt").GetString()); Equal(item.Id.ToString(), root.GetProperty("requestId").GetString()); Equal("text/plain", handler.Requests[0].ContentType); });
        await TestAsync("Apps Script 302 switches to body-free GET", async () => { var handler = new FakeHttp(i => i == 0 ? Redirect("https://script.googleusercontent.com/macros/echo?test=1") : Json("{\"success\":true}")); using var client = new SheetsClient(handler); Is((await client.Ping(Connection)).Success); Equal(HttpMethod.Get, handler.Requests[1].Method); Equal("", handler.Requests[1].Body); });
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
            Test("diagnostic export excludes text and credentials", () => { var log = new DiagnosticLog(Path.Combine(root, "logs")); log.Record("timer.started", value: 30); log.Record("private-sentinel"); var state = new AppState { Connection = Connection, Prompts = [new(Guid.NewGuid(), 0, 1, 0, true, "private-sentinel")], Outbox = [new OutboxItem { Message = "private-sentinel", ErrorKind = "private-sentinel" }] }; var report = JsonSerializer.Serialize(log.Report(state)); Is(!report.Contains("private-sentinel")); Is(!report.Contains(Connection.ApiToken)); Is(!report.Contains(Connection.SheetUrl)); Equal(1, log.Recent().Count); });
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
        Test("dark date/time editor preserves local time and validates input", () => {
            using var input = new SessionStartInput { Value = new DateTime(2026, 9, 5, 18, 30, 45, DateTimeKind.Local) };
            Equal("09/05/2026 06:30 PM", input.Text); Equal(18, input.Value.Hour); Equal(30, input.Value.Minute);
            Equal(DateTimeKind.Local, input.Value.Kind); Equal(0, input.Value.Second);
            Equal(0, SessionStartInput.Parse("9/5/2026 12:00 AM").Hour);
            Equal(12, SessionStartInput.Parse("9/5/2026 12:00 PM").Hour);
            Throws<ArgumentException>(() => SessionStartInput.Parse("02/30/2026 01:00 PM"));
            Throws<ArgumentException>(() => SessionStartInput.Parse("not a date"));
            DarkTheme.Apply(input); Equal(DarkTheme.Field, input.BackColor);
        });
        Test("dark theme text and semantic colors have readable contrast", () => {
            if (SystemInformation.HighContrast) return; // The user's accessibility palette takes precedence.
            foreach (var background in new[] { DarkTheme.Background, DarkTheme.Field, DarkTheme.Raised })
                foreach (var foreground in new[] { DarkTheme.Text, DarkTheme.Muted, DarkTheme.Warning, DarkTheme.Error, DarkTheme.Accent })
                    Is(Contrast(background, foreground) >= 4.5);
            Is(Contrast(DarkTheme.Accent, DarkTheme.AccentText) >= 4.5);
            Is(Contrast(DarkTheme.Selection, DarkTheme.SelectionText) >= 4.5);
        });
        Test("theme styles nested inputs without changing values or masking", () => {
            using var form = new Form(); var panel = new FlowLayoutPanel(); form.Controls.Add(panel);
            var input = new TextBox { Text = "unchanged", UseSystemPasswordChar = true };
            var number = new NumericUpDown { Value = 12 }; var check = new CheckBox { Checked = true };
            var warning = new Label { Text = "Warning", ForeColor = DarkTheme.Warning };
            panel.Controls.AddRange([input, number, check, warning]); DarkTheme.Apply(form); DarkTheme.Apply(form);
            Equal(DarkTheme.Background, form.BackColor); Equal(DarkTheme.Background, panel.BackColor);
            Equal(DarkTheme.Field, input.BackColor); Equal(DarkTheme.Field, number.BackColor);
            Equal("unchanged", input.Text); Is(input.UseSystemPasswordChar); Equal(12m, number.Value); Is(check.Checked);
            Equal(DarkTheme.Warning, warning.ForeColor);
        });
        Test("dark table theme covers headers, rows and selected cells", () => {
            using var grid = Widgets.Grid("Entry", "Status"); grid.Rows.Add("example", "Pending");
            Equal(DarkTheme.Field, grid.BackgroundColor); Is(!grid.EnableHeadersVisualStyles);
            Equal(DarkTheme.Raised, grid.ColumnHeadersDefaultCellStyle.BackColor);
            Equal(DarkTheme.Selection, grid.DefaultCellStyle.SelectionBackColor);
            Equal(DarkTheme.SelectionText, grid.DefaultCellStyle.SelectionForeColor);
            Equal("example", grid.Rows[0].Cells[0].Value);
        });
        Test("primary and secondary buttons retain their dark-theme roles", () => {
            using var primary = Widgets.Button("Save & send", (_, _) => { }, true);
            using var secondary = Widgets.Button("Later", (_, _) => { });
            using var form = new Form(); form.Controls.AddRange([primary, secondary]);
            var original = primary.BackColor; DarkTheme.Apply(form);
            Equal(original, primary.BackColor); Equal(DarkTheme.AccentText, primary.ForeColor);
            Equal(DarkTheme.Raised, secondary.BackColor); Equal(DarkTheme.Text, secondary.ForeColor);
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
    private sealed class FakeHttp(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Body, string? ContentType)> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add((request.Method, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken), request.Content?.Headers.ContentType?.MediaType));
            return respond(Requests.Count - 1);
        }
    }
}
