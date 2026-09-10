using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestSessionDetails()
    {
        Test("actual elapsed excludes pauses including accumulated partial seconds", () => {
            var f = new Fixture(); f.Engine.Start(60, false, 0);
            f.Move(10.5); f.Engine.Pause(); f.Move(500); f.Engine.Resume(); f.Move(10.5); f.Engine.Pause();
            f.Move(200); var e = f.Restart(); e.Resume(); f.Move(1); e.EndEarly();
            var p = e.Snapshot.Prompts.Single(); Equal(22, p.ActualDurationSeconds!.Value); Equal(60, p.DurationSeconds); Is(p.EndedEarly);
        });
        Test("early detail is captured before auto restart and retained with its reason", () => {
            var f = new Fixture(); f.Engine.Start(1800, true, 0); f.Move(480); f.Engine.EndEarly();
            var p = f.Engine.Snapshot.Prompts.Single(); Equal(480, p.ActualDurationSeconds!.Value); Is(p.EndedEarly);
            Equal(1800, TimerEngine.Remaining(f.Engine.Snapshot.Timer, f.Engine.Now));
            f.Engine.SaveDraft(p.Id, "reflection", "  appointment  "); var restored = f.Restart();
            Equal("  appointment  ", restored.Snapshot.Prompts.Single().EarlyEndReason);
            restored.QueueReflection(p.Id, "reflection"); var item = restored.Snapshot.Outbox.Single();
            Equal(480, item.ActualDurationSeconds!.Value); Equal(1800, item.DurationSeconds); Equal("appointment", item.EarlyEndReason); Is(item.EndedEarly);
        });
        foreach (var delay in new[] { 0, 15, 100 }) Test("completion metadata at elapsed " + delay, () => {
            var f = new Fixture(); f.Engine.Start(15, false, 0); f.Move(delay); f.Engine.EndEarly();
            var p = f.Engine.Snapshot.Prompts.Single(); Equal(Math.Min(15, delay), p.ActualDurationSeconds!.Value); Equal(delay < 15, p.EndedEarly);
        });
        Test("normal deadline records allotted time and no early reason", () => {
            var f = new Fixture(); f.Engine.Start(15, false, 0); f.Move(100); f.Engine.Advance();
            var p = f.Engine.Snapshot.Prompts.Single(); Equal(15, p.ActualDurationSeconds!.Value); Is(!p.EndedEarly);
            f.Engine.QueueReflection(p.Id, "done", "not applicable"); Equal("", f.Engine.Snapshot.Outbox.Single().EarlyEndReason);
        });
        Test("legacy prompts do not fabricate elapsed time", () => {
            var f = new Fixture(); var p = new ReflectionPrompt(Guid.NewGuid(), 0, 60, 0, false);
            f.Store.Data.Prompts.Add(p); var e = f.Restart(); e.QueueReflection(p.Id, "old draft");
            Is(e.Snapshot.Outbox.Single().ActualDurationSeconds is null);
        });
        Test("reason validation and failed saves preserve the prompt", () => {
            var f = new Fixture(); f.Engine.Start(60, false, 0); f.Move(2); f.Engine.EndEarly(); var p = f.Engine.Snapshot.Prompts.Single();
            Throws<ArgumentException>(() => f.Engine.QueueReflection(p.Id, "work", new string('x', 1001)));
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.QueueReflection(p.Id, "work", "reason"));
            Equal(1, f.Engine.Snapshot.Prompts.Count); Equal(0, f.Engine.Snapshot.Outbox.Count);
        });
        Test("early reflection reason field is smaller and persists", () => {
            WithEndEarlyApp((app, _) => {
                var p = new ReflectionPrompt(Guid.NewGuid(), app.Engine.Now, 60, 0, true) { ActualDurationSeconds = 10, EndedEarly = true, EarlyEndReason = "draft reason" };
                using var form = new ReflectionWindow(app, p); form.Show(); Application.DoEvents();
                var reason = Descendants(form).OfType<TextBox>().Single(x => x.AccessibleName == "Reason for ending early");
                var response = Descendants(form).OfType<TextBox>().Single(x => x.MaxLength == 5000);
                Equal("Reason for ending early", reason.PlaceholderText); Equal("draft reason", reason.Text);
                Is(reason.Visible && reason.Top >= 0 && reason.Height < response.Height);
                form.Hide();
            });
        });
    }
}
