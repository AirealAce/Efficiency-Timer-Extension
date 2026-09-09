using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestCheckIns()
    {
        Test("check-in captures active elapsed at submission, not opening, without changing timer", () => {
            var f = new Fixture(); f.Engine.Start(300, true, 0, f.Engine.Now + 900000);
            f.Move(10); var before = f.Engine.Snapshot.Timer; var id = f.Engine.CheckIn();
            Equal(id, f.Engine.CheckIn()); Equal(1, f.Engine.Snapshot.Prompts.Count);
            f.Move(15); f.Engine.QueueReflection(id, "check-in", "not an early finish");
            var item = f.Engine.Snapshot.Outbox.Single(); Equal(25, item.ActualDurationSeconds!.Value);
            Equal(300, item.DurationSeconds); Is(item.IsCheckIn); Is(!item.EndedEarly); Is(!item.IsTest); Equal("", item.EarlyEndReason);
            Equal(before, f.Engine.Snapshot.Timer); Equal(0, f.Engine.Snapshot.Prompts.Count);
            f.Move(5); var second = f.Engine.CheckIn(); Is(second != id); f.Engine.QueueReflection(second, "another check-in");
            Equal(30, f.Engine.Snapshot.Outbox.Last().ActualDurationSeconds!.Value); // Cumulative, not an extra work interval.
        });
        Test("check-in excludes pauses and persists session identity and draft through restart", () => {
            var f = new Fixture(); f.Engine.Start(300, false, 0); f.Move(10.5); var id = f.Engine.CheckIn();
            f.Engine.Pause(); f.Move(500); f.Engine.SaveDraft(id, "saved check-in");
            var engine = f.Restart(); Equal(id, engine.CheckIn()); Equal("saved check-in", engine.Snapshot.Prompts.Single().Draft);
            engine.Resume(); f.Move(10.5); engine.Pause(); f.Move(300); engine.QueueReflection(id, "saved check-in");
            Equal(21, engine.Snapshot.Outbox.Single().ActualDurationSeconds!.Value); Is(TimerEngine.IsPaused(engine.Snapshot.Timer));
        });
        foreach (var transition in new[] { "complete", "early", "reset", "start", "schedule", "pause-at-zero" })
            Test("delayed check-in stays with original session after " + transition, () => {
                var f = new Fixture(); f.Engine.Start(60, true, 0); f.Move(10); var id = f.Engine.CheckIn();
                var expected = transition is "complete" or "pause-at-zero" ? 60 : 20;
                f.Move(expected - 10);
                switch (transition) {
                    case "complete": f.Engine.Advance(); break;
                    case "early": f.Engine.EndEarly(); break;
                    case "reset": f.Engine.Reset(); break;
                    case "start": f.Engine.Start(900, false, 0); break;
                    case "schedule": f.Add(1, 900); f.Move(1); expected++; f.Engine.Advance(); break;
                    case "pause-at-zero": f.Engine.Pause(); break;
                }
                var draft = f.Engine.Snapshot.Prompts.Single(p => p.Id == id); Is(draft.CheckInSessionId is null);
                f.Move(30); var restored = f.Restart(); restored.QueueReflection(id, "late check-in");
                Equal(expected, restored.Snapshot.Outbox.Single().ActualDurationSeconds!.Value);
                Is(restored.Snapshot.Outbox.Single().IsCheckIn); Equal(60, restored.Snapshot.Outbox.Single().DurationSeconds);
            });
        Test("check-in before deadline tick clamps at allotted duration", () => {
            var f = new Fixture(); f.Engine.Start(10, true, 0); var id = f.Engine.CheckIn();
            f.Move(100); f.Engine.QueueReflection(id, "deadline race");
            Equal(10, f.Engine.Snapshot.Outbox.Single().ActualDurationSeconds!.Value);
            f.Engine.Advance(); Is(!f.Engine.Snapshot.Prompts.Single().IsCheckIn);
        });
        Test("legacy running timer upgrades identity without resetting timing or options", () => {
            var f = new Fixture(); f.Store.Data.Timer = new() { IsRunning = true, DurationSeconds = 60, RemainingSeconds = 60, EndTime = f.Engine.Now + 40000, AutoRestart = true, Volume = 0 };
            var e = f.Restart(); var before = e.Snapshot.Timer; var id = e.CheckIn();
            Equal(before, e.Snapshot.Timer with { SessionId = null }); Is(e.Snapshot.Timer.SessionId is not null);
            e.QueueReflection(id, "legacy session"); Equal(20, e.Snapshot.Outbox.Single().ActualDurationSeconds!.Value);
        });
        Test("idle and finished timers do not fabricate check-ins", () => {
            var f = new Fixture(); Throws<ArgumentException>(() => f.Engine.CheckIn()); Equal(0, f.Store.Writes);
            f.Engine.Start(10, false, 0); f.Move(10); Throws<ArgumentException>(() => f.Engine.CheckIn());
            f.Engine.Advance(); Throws<ArgumentException>(() => f.Engine.CheckIn()); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        Test("failed check-in creation and save are atomic and retry measures successful submission time", () => {
            var f = new Fixture(); f.Engine.Start(60, true, 0); var before = f.Engine.Snapshot.Timer;
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.CheckIn()); Equal(0, f.Engine.Snapshot.Prompts.Count); Equal(before, f.Engine.Snapshot.Timer);
            f.Store.Fail = false; var id = f.Engine.CheckIn(); f.Move(5);
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.QueueReflection(id, "draft"));
            Equal(1, f.Engine.Snapshot.Prompts.Count); Equal(0, f.Engine.Snapshot.Outbox.Count);
            f.Store.Fail = false; f.Move(5); f.Engine.QueueReflection(id, "draft"); Equal(10, f.Engine.Snapshot.Outbox.Single().ActualDurationSeconds!.Value);
        });
        Test("comma focuses one check-in, preserves old draft, and send defers backlog without changing timer", () => {
            var old = new ReflectionPrompt(Guid.NewGuid(), DateTimeOffset.Now.ToUnixTimeMilliseconds(), 30, 0, false, "older reflection") { ActualDurationSeconds = 30 };
            WithEndEarlyApp((app, directory) => {
                TimerShortcutControls(app); var previous = Application.OpenForms.OfType<ReflectionWindow>().Single();
                ReflectionResponse(previous).Text = "edited old draft";
                app.Engine.Start(9000, true, 0); var before = app.Engine.Snapshot.Timer;
                PressComma(app); var popup = Application.OpenForms.OfType<ReflectionWindow>().Single();
                Is(previous.IsDisposed); Is(popup.Text.EndsWith("check-in"));
                Equal("edited old draft", new EncryptedStore(directory).Load().Prompts.Single(p => p.Id == old.Id).Draft);
                Is(!Descendants(popup).OfType<TextBox>().Any(x => x.AccessibleName == "Reason for ending early"));
                var response = ReflectionResponse(popup); response.Text = "new check-in"; response.Select(2, 4);
                PressComma(app); Equal(popup, Application.OpenForms.OfType<ReflectionWindow>().Single());
                Is(response.ContainsFocus); Equal(2, response.SelectionStart); Equal(4, response.SelectionLength);
                Equal(2, app.Engine.Snapshot.Prompts.Count);
                Descendants(popup).OfType<Button>().Single(x => x.Text == "Later").PerformClick(); Application.DoEvents();
                PressComma(app); popup = Application.OpenForms.OfType<ReflectionWindow>().Single(); Equal("new check-in", ReflectionResponse(popup).Text);
                var key = new KeyEventArgs(Keys.Control | Keys.Enter);
                typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(ReflectionResponse(popup), [key]);
                Application.DoEvents(); Is(key.SuppressKeyPress); Is(popup.IsDisposed); Equal(0, Application.OpenForms.OfType<ReflectionWindow>().Count());
                Equal(before, app.Engine.Snapshot.Timer); Is(app.Engine.Snapshot.Outbox.Single().IsCheckIn);
                Equal(old.Id, app.Engine.Snapshot.Prompts.Single().Id);
                FocusPending(app); Equal("edited old draft", ReflectionResponse(Application.OpenForms.OfType<ReflectionWindow>().Single()).Text);
            }, new AppState { ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 }, Prompts = [old] });
        });
        Test("comma while idle leaves old reflections intact without inventing elapsed time", () => {
            WithEndEarlyApp((app, _) => { TimerShortcutControls(app); var before = app.Engine.Snapshot.Timer; PressComma(app);
                Equal(before, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count); Equal(0, app.Engine.Snapshot.Outbox.Count);
            });
        });
    }

    private static async Task TestCheckInHttp()
    {
        await TestAsync("check-ins require explicit receiver support before sending text", async () => {
            foreach (var supported in new[] { false, true }) {
                var handler = new FakeHttp(_ => Json(JsonSerializer.Serialize(new { success = true, supportsCheckIns = supported, sheet = "test", target = "Synthetic / test" })));
                using var client = new SheetsClient(handler);
                var reply = await client.Upload(Connection, new OutboxItem { IsCheckIn = true, Message = "synthetic check-in", ActualDurationSeconds = 25, DurationSeconds = 60, SheetUrl = Connection.SheetUrl, IsTest = true });
                Equal(supported, reply.Success); Equal(supported ? 2 : 1, handler.Requests.Count);
                using var ping = JsonDocument.Parse(handler.Requests[0].Body); Equal("", ping.RootElement.GetProperty("message").GetString());
                if (supported) { using var body = JsonDocument.Parse(handler.Requests[1].Body);
                    Is(body.RootElement.GetProperty("isCheckIn").GetBoolean()); Is(!body.RootElement.GetProperty("endedEarly").GetBoolean());
                    Equal(25, body.RootElement.GetProperty("actualDurationSeconds").GetInt32()); Is(body.RootElement.GetProperty("isTest").GetBoolean());
                } else Equal("receiver_update_required", reply.ErrorKind);
            }
        });
    }
}
