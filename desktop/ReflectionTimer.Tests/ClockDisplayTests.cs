using System.Text.Json;
using ReflectionTimer.Accessible;
using ReflectionTimer.Core;

static class ClockDisplayTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var store = new MemoryStore { State = new AppState() };
        var session = new PreviewSession(store, () => now);
        JsonElement Clock(PreviewSession value) => JsonSerializer.SerializeToElement(value.Clock(), PreviewSession.Json);
        int Seconds(PreviewSession value) => Clock(value).GetProperty("seconds").GetInt32();
        session.Engine.Start(900, false, 50);
        now = now.AddSeconds(30);
        session.Engine.Pause();
        check(Seconds(session) == 870 && Clock(session).GetProperty("status").GetString() == "Paused", "Paused clock keeps the remaining time instead of the full duration");
        session.Engine.Resume();
        now = now.AddSeconds(870);
        check(Seconds(session) == 0, "Countdown can reach zero before completion is processed");
        session.Tick();
        check(Seconds(session) == 900 && Clock(session).GetProperty("text").GetString() == "15 minutes" && Clock(session).GetProperty("status").GetString() == "Finished", "Finished visual and spoken clocks return to the entered duration");
        check(session.Engine.Snapshot.Timer.RemainingSeconds == 0 && session.Engine.Snapshot.Prompts.Single().ActualDurationSeconds == 900, "Display reset retains completed-session accounting and its reflection");
        session.Tick();
        var reopened = new PreviewSession(store, () => now);
        check(Seconds(session) == 900 && Seconds(reopened) == 900 && reopened.Engine.Snapshot.Prompts.Count == 1, "Later ticks and reopening a finished timer retain the duration preview without duplicating reflections");
        session.SetDurationDraft(["0","0","20"]);
        check(Seconds(session)==20&&Clock(session).GetProperty("text").GetString()=="20 seconds", "Read-time feedback after completion follows the edited duration boxes");
        session.SetDurationDraft(["","0","0"]);
        check(Seconds(session)==0&&Clock(session).GetProperty("text").GetString()=="0 seconds", "Empty or zero duration units produce a zero preview after completion");
        session.SetDurationDraft(null);
        check(Seconds(session)==900&&session.Engine.Snapshot.Timer.RemainingSeconds==0,"Clearing the edit restores the original duration without changing completed-session accounting");
        session.Engine.Start(120, false, 50);
        now = now.AddSeconds(17);
        session.Engine.EndEarly();
        check(Seconds(session) == 120 && session.Engine.Snapshot.Prompts.Last().ActualDurationSeconds == 17 && session.Engine.Snapshot.Prompts.Last().EndedEarly, "Early end restores the entered duration and retains actual time spent");
        session.Engine.Start(60, true, 50);
        now = now.AddSeconds(60);
        session.Tick();
        now = now.AddSeconds(4);
        check(Seconds(session) == 56 && Clock(session).GetProperty("status").GetString() == "Running", "Auto-start continues counting down the next session");
        session.Engine.SetPreferences(false, 50);
        session.Engine.SaveSchedule(null, now.AddSeconds(56), 300, false, 50);
        now = now.AddSeconds(56);
        session.Tick();
        now = now.AddSeconds(2);
        check(Seconds(session) == 298 && session.Engine.Snapshot.Timer.DurationSeconds == 300, "Scheduled handoff shows the new session countdown");
    }
}
