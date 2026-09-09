using System.Text.Json;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestScheduleOverlap()
    {
        Test("default schedule overlap reflects actual time and adopts appointment options", () => {
            var f = new Fixture(); f.Engine.Start(600, true, 40); f.Add(20, 120, false, 15); f.Move(20); f.Engine.Advance();
            var p = f.Engine.Snapshot.Prompts.Single(); Is(p.EndedEarly); Equal(20, p.ActualDurationSeconds!.Value); Equal(600, p.DurationSeconds);
            Equal(120, f.Engine.Snapshot.Timer.DurationSeconds); Equal(15, f.Engine.Snapshot.Timer.Volume); Is(!f.Engine.Snapshot.Timer.AutoRestart);
            f.Engine.Advance(); Equal(1, f.Engine.Snapshot.Prompts.Count);
        });
        Test("paused overlap excludes time spent paused", () => {
            var f = new Fixture(); f.Engine.Start(60, false, 0); f.Move(10); f.Engine.Pause(); f.Add(30, 120); f.Move(30); f.Engine.Advance();
            Equal(10, f.Engine.Snapshot.Prompts.Single().ActualDurationSeconds!.Value); Is(f.Engine.Snapshot.Prompts.Single().EndedEarly);
        });
        Test("wait policy holds once then starts at completion before auto restart", () => {
            var f = new Fixture(); f.Engine.SetScheduleOverlap(ScheduleOverlapPolicy.Wait); f.Engine.Start(60, true, 0, lowTime: new() { Enabled = false }); f.Add(10, 120); f.Move(10); f.Engine.Advance();
            Is(f.Engine.Snapshot.Schedules.Single().WaitingForCurrentSession); Equal(0, f.Engine.Snapshot.Prompts.Count);
            var writes = f.Store.Writes; f.Move(1); f.Engine.Advance(); Equal(writes, f.Store.Writes);
            f.Move(49); f.Engine.Advance(); Equal(120, f.Engine.Snapshot.Timer.DurationSeconds); Equal(60, f.Engine.Snapshot.Prompts.Single().ActualDurationSeconds!.Value);
        });
        Test("waiting appointment starts when current session ends early", () => {
            var f = new Fixture(); f.Engine.SetScheduleOverlap(ScheduleOverlapPolicy.Wait); f.Engine.Start(600, true, 0); f.Add(10, 120); f.Move(10); f.Engine.Advance();
            f.Move(10); f.Engine.EndEarly(); Equal(120, f.Engine.Snapshot.Timer.DurationSeconds); Equal(20, f.Engine.Snapshot.Prompts.Single().ActualDurationSeconds!.Value);
        });
        Test("wait persists across restart and honors an expired appointment cutoff", () => {
            var f = new Fixture(); f.Engine.SetScheduleOverlap(ScheduleOverlapPolicy.Wait); f.Engine.Start(60, false, 0);
            f.Engine.SaveSchedule(null, f.Time.AddSeconds(10), 120, true, 0, f.Time.AddSeconds(30).ToUnixTimeMilliseconds()); f.Move(10); f.Engine.Advance();
            var e = f.Restart(); f.Move(50); e.Advance(); Equal(120, e.Snapshot.Timer.DurationSeconds); Is(!e.Snapshot.Timer.AutoRestart);
        });
        foreach (var decision in Enum.GetValues<ScheduleDecision>()) Test("ask policy persists and resolves " + decision, () => {
            var f = new Fixture(); f.Engine.SetScheduleOverlap(ScheduleOverlapPolicy.Ask); f.Engine.Start(60, false, 0); var id = f.Add(10, 120); f.Move(10); f.Engine.Advance();
            var e = f.Restart(); Is(e.Snapshot.Schedules.Single().AwaitingDecision); Equal(50, TimerEngine.Remaining(e.Snapshot.Timer, e.Now));
            e.ResolveSchedule(id, decision);
            if (decision == ScheduleDecision.StartNow) { Equal(120, e.Snapshot.Timer.DurationSeconds); Is(e.Snapshot.Prompts.Single().EndedEarly); }
            else if (decision == ScheduleDecision.Wait) Is(e.Snapshot.Schedules.Single().WaitingForCurrentSession);
            else { Equal(0, e.Snapshot.Schedules.Count); Equal(0, e.Snapshot.Prompts.Count); Equal(60, e.Snapshot.Timer.DurationSeconds); }
        });
        Test("overlap at the exact deadline is completed, not early", () => {
            var f = new Fixture(); f.Engine.SetScheduleOverlap(ScheduleOverlapPolicy.Ask); f.Engine.Start(10, true, 0); f.Add(10, 120); f.Move(10); f.Engine.Advance();
            Is(!f.Engine.Snapshot.Prompts.Single().EndedEarly); Equal(120, f.Engine.Snapshot.Timer.DurationSeconds); Equal(0, f.Engine.Snapshot.Schedules.Count);
        });
        Test("failed overlap commit loses neither session nor appointment", () => {
            var f = new Fixture(); f.Engine.Start(60, false, 0); f.Add(10, 120); f.Move(10); var before = JsonSerializer.Serialize(f.Engine.Snapshot);
            f.Store.Fail = true; Throws<IOException>(() => f.Engine.Advance()); Equal(before, JsonSerializer.Serialize(f.Engine.Snapshot));
        });
        Test("waiting appointments remain ordered and do not cancel future schedules", () => {
            var f = new Fixture(); f.Engine.SetScheduleOverlap(ScheduleOverlapPolicy.Wait); f.Engine.Start(60, false, 0);
            f.Add(10, 20); f.Add(20, 30); f.Add(200, 40); f.Move(10); f.Engine.Advance(); f.Move(10); f.Engine.Advance(); f.Move(40); f.Engine.Advance();
            Equal(20, f.Engine.Snapshot.Timer.DurationSeconds); f.Move(20); f.Engine.Advance(); Equal(30, f.Engine.Snapshot.Timer.DurationSeconds);
            Equal(1, f.Engine.Snapshot.Schedules.Count);
        });
        Test("ask window offers all choices and X chooses wait", () => {
            var session = new ScheduledSession(Guid.NewGuid(), DateTimeOffset.Now.ToUnixTimeMilliseconds() - 1000, 60, false, 0) { AwaitingDecision = true };
            WithEndEarlyApp((app, _) => {
                Application.DoEvents(); var form = Application.OpenForms.OfType<ScheduleConflictWindow>().Single();
                Equal(3, Descendants(form).OfType<Button>().Count()); form.Close(); Application.DoEvents();
                Is(app.Engine.Snapshot.Schedules.Single().WaitingForCurrentSession);
            }, new AppState { ExtensionDisabledConfirmed = true, ScheduleOverlap = ScheduleOverlapPolicy.Ask, Schedules = [session],
                Timer = new TimerState { IsRunning = true, DurationSeconds = 600, RemainingSeconds = 600, EndTime = DateTimeOffset.Now.AddMinutes(10).ToUnixTimeMilliseconds(), Volume = 0 } });
        });
    }
}
