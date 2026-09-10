using System.Globalization;
using System.Text.Json;
using ReflectionTimer.Core;

namespace ReflectionTimer.Accessible;

// A presentation adapter over the production engine. Never constructs a SheetsClient.
public sealed class PreviewSession
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public TimerEngine Engine { get; }
    public event Action<string>? Announcement;
    public PreviewSession(IStateStore store, Func<DateTimeOffset>? clock = null)
    {
        Engine = new(store, clock);
        Engine.LowTimeReached += timer => Announcement?.Invoke($"Low time. {SpeakTime(TimerEngine.Remaining(timer, Engine.Now))} remaining.");
    }
    public static AppState SampleState(DateTimeOffset now) => new() {
        ExtensionDisabledConfirmed = true,
        ScheduleOverlap = ScheduleOverlapPolicy.Wait,
        Schedules = [new(Guid.NewGuid(), now.AddDays(7).ToUnixTimeMilliseconds(), 900, false, 50),
                     new(Guid.NewGuid(), now.AddDays(8).ToUnixTimeMilliseconds(), 1200, false, 50)],
        Outbox = [new() { Message = "Sample reflection: I completed my reading.", SubmittedAt = now.AddMinutes(-30), DurationSeconds = 900,
            ActualDurationSeconds = 900, IsTest = true, Status = DeliveryStatus.Pending },
            new() { Message = "Sample reflection: I will take a short break.", SubmittedAt = now.AddMinutes(-15), DurationSeconds = 600,
            ActualDurationSeconds = 360, IsTest = true, Status = DeliveryStatus.NeedsReview, ErrorKind = "sample", Attempts = 1 }]
    };
    public object Clock()
    {
        var timer = Engine.Snapshot.Timer;
        var seconds = TimerEngine.Remaining(timer, Engine.Now);
        return new { seconds, text = SpeakTime(seconds), status = Status(timer) };
    }
    public static string Status(TimerState timer) => timer.IsRunning ? "Running" : TimerEngine.IsPaused(timer) ? "Paused" : timer.RemainingSeconds == 0 ? "Finished" : "Ready";
    public static string SpeakTime(int total)
    {
        var parts = new List<string>();
        if (total / 3600 > 0) parts.Add($"{total / 3600} hour{(total / 3600 == 1 ? "" : "s")}");
        if (total / 60 % 60 > 0) parts.Add($"{total / 60 % 60} minute{(total / 60 % 60 == 1 ? "" : "s")}");
        if (total % 60 > 0 || parts.Count == 0) parts.Add($"{total % 60} second{(total % 60 == 1 ? "" : "s")}");
        return string.Join(" ", parts);
    }
    // Only fields needed by these views cross the bridge. No connection object or custom paths.
    public object View()
    {
        var state = Engine.Snapshot;
        return new {
            clock = Clock(), timer = new { state.Timer.DurationSeconds, state.Timer.AutoRestart, state.Timer.LowTime.Enabled,
                threshold = state.Timer.LowTime.ThresholdSeconds ?? 15 },
            prompts = state.Prompts.Select(p => new { p.Id, p.IsCheckIn, p.EndedEarly, p.Draft, p.EarlyEndReason,
                allotted = SpeakTime(p.DurationSeconds), actual = p.ActualDurationSeconds is { } actual ? SpeakTime(actual) : "Unavailable",
                completed = DateTimeOffset.FromUnixTimeMilliseconds(p.CompletedAt).ToLocalTime().ToString("g") }),
            schedules = state.Schedules.Select(s => new { s.Id, start = DateTimeOffset.FromUnixTimeMilliseconds(s.StartTime).ToLocalTime().ToString("g"),
                duration = SpeakTime(s.DurationSeconds), repeat = s.AutoRestart ? "On" : "Off", lowTime = s.LowTime.Enabled ? "On" : "Off",
                status = s.AwaitingDecision ? "Needs choice" : s.WaitingForCurrentSession ? "Waiting" : "Scheduled" }),
            outbox = state.Outbox.Select(o => new { o.Id, saved = o.SubmittedAt.LocalDateTime.ToString("g"),
                destination = "Local preview only", status = o.Status == DeliveryStatus.Sent ? "Simulated success" : o.Status.ToString(),
                o.Attempts, o.Message, duration = SpeakTime(o.DurationSeconds) })
        };
    }
    public void Tick()
    {
        var before = Engine.Snapshot;
        Engine.Advance();
        var after = Engine.Snapshot;
        if (after.Prompts.Any(p => before.Prompts.All(old => old.Id != p.Id)))
            Announcement?.Invoke("Session finished. A reflection is available under Pending reflections.");
        else if (!before.Timer.IsRunning && after.Timer.IsRunning) Announcement?.Invoke("Scheduled timer started.");
    }
    public CommandResult Execute(string action, JsonElement data)
    {
        var state = Engine.Snapshot;
        switch (action)
        {
            case "toggle":
                if (state.Timer.IsRunning) { Engine.Pause(); return new("Timer paused."); }
                var seconds = Number(data, "seconds", 1, TimerEngine.MaxDuration);
                if (TimerEngine.IsPaused(state.Timer) && seconds == state.Timer.DurationSeconds) Engine.Resume();
                else Engine.Start(seconds, Flag(data, "repeat"), state.Timer.Volume, lowTime: new LowTimeOptions {
                    Enabled = Flag(data, "lowTime"), ThresholdSeconds = Number(data, "threshold", 1, TimerEngine.MaxDuration) });
                return new("Timer running.");
            case "reset":
                Engine.Reset(Number(data, "seconds", 1, TimerEngine.MaxDuration)); return new("Timer reset.");
            case "end":
                var previous = state.Prompts.Select(p => p.Id).ToHashSet();
                if (!Engine.EndEarly()) throw new ArgumentException("Start or resume the timer before ending it early.");
                return new("Session ended. Reflection opened.", Engine.Snapshot.Prompts.First(p => !previous.Contains(p.Id)).Id);
            case "checkIn": return new("Check-in opened.", Engine.CheckIn());
            case "testReflection": return new("Practice reflection opened.", Engine.TestPrompt());
            case "openReflection":
                var prompt = RequiredPrompt(Id(data)); return new("", prompt.Id);
            case "draft":
                var draftId = Id(data); RequiredPrompt(draftId);
                Engine.SaveDraft(draftId, Text(data, "text", 5000), Text(data, "reason", 1000)); return new("");
            case "queue":
                Engine.QueueReflection(Id(data), Text(data, "text", 5000), Text(data, "reason", 1000));
                return new("Reflection saved in the local Outbox. Nothing was sent online.", Close: true);
            case "schedule":
                var date = Text(data, "start", 40);
                if (!DateTime.TryParseExact(date, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                    throw new ArgumentException("Enter a valid local start date and time.");
                if (TimeZoneInfo.Local.IsInvalidTime(start) || TimeZoneInfo.Local.IsAmbiguousTime(start))
                    throw new ArgumentException("Choose an unambiguous local time outside the daylight-saving clock change.");
                Engine.SaveSchedule(null, new DateTimeOffset(start), Number(data, "seconds", 1, TimerEngine.MaxDuration), false, 50);
                return new("Session scheduled.");
            case "removeSchedule":
                var id = Id(data);
                if (state.Schedules.All(s => s.Id != id)) throw new ArgumentException("That schedule is no longer available.");
                Engine.RemoveSchedule(id); return new("Scheduled session removed.");
            case "simulate":
                // Deliberately no HTTP client. This exercises row updates with local records only.
                var item = state.Outbox.SingleOrDefault(o => o.Id == Id(data)) ?? throw new ArgumentException("Entry not found.");
                Engine.FinishUpload(item.Id, true, "", "Preview");
                return new("Simulated success. No data was sent.");
            default: throw new ArgumentException("Unknown preview command.");
        }
    }
    private ReflectionPrompt RequiredPrompt(Guid id) => Engine.Snapshot.Prompts.SingleOrDefault(p => p.Id == id) ?? throw new ArgumentException("That reflection is no longer pending.");
    private static Guid Id(JsonElement data) => Guid.TryParse(Text(data, "id", 36), out var id) ? id : throw new ArgumentException("Invalid record identifier.");
    private static int Number(JsonElement data, string name, int min, int max) => data.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && number >= min && number <= max
        ? number : throw new ArgumentException($"Enter a valid {name} between {min} and {max}.");
    private static bool Flag(JsonElement data, string name) => data.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
    private static string Text(JsonElement data, string name, int max) => data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= max
        ? text : throw new ArgumentException($"Enter valid {name} (up to {max} characters).");
}
public record CommandResult(string Message, Guid? OpenReflection = null, bool Close = false);
