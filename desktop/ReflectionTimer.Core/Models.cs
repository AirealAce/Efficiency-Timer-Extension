using System.Text.Json;

namespace ReflectionTimer.Core;

public static class DataJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}

public record TimerState
{
    public bool IsRunning { get; init; }
    public int DurationSeconds { get; init; } = 1500;
    public int RemainingSeconds { get; init; } = 1500;
    public long? EndTime { get; init; }
    public bool AutoRestart { get; init; }
    public long? AutoRestartUntil { get; init; }
    public int Volume { get; init; } = 50;
}

public record ScheduledSession(Guid Id, long StartTime, int DurationSeconds, bool AutoRestart, int Volume, long? AutoRestartUntil = null);
public record ReflectionPrompt(Guid Id, long CompletedAt, int DurationSeconds, int Volume, bool IsTest, string Draft = "");

public record ConnectionSettings
{
    public string SheetUrl { get; init; } = "https://docs.google.com/spreadsheets/d/synthetic-spreadsheet-id-for-tests/edit";
    public string WebAppUrl { get; init; } = "";
    public string ApiToken { get; init; } = "";
    public string SheetMode { get; init; } = "date";
    public string SheetName { get; init; } = "Template";
}

public enum DeliveryStatus { Pending, Sending, Sent, NeedsReview }
public enum ReflectionPopupPosition { Center = 0, TopLeft = 1, TopRight = 2, BottomLeft = 3, BottomRight = 4 }
public record OutboxItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Message { get; init; } = "";
    public DateTimeOffset SubmittedAt { get; init; }
    public int DurationSeconds { get; init; }
    public bool IsTest { get; init; }
    public string SheetUrl { get; init; } = "";
    public string SheetMode { get; init; } = "date";
    public string SheetName { get; init; } = "Template";
    public DeliveryStatus Status { get; init; }
    public int Attempts { get; init; }
    public string ErrorKind { get; init; } = "";
    public string SavedTab { get; init; } = "";
}

public record AppState
{
    public int FormatVersion { get; init; } = 1;
    public TimerState Timer { get; set; } = new();
    public List<ScheduledSession> Schedules { get; set; } = [];
    public List<ReflectionPrompt> Prompts { get; set; } = [];
    public List<OutboxItem> Outbox { get; set; } = [];
    public ConnectionSettings Connection { get; set; } = new();
    public string AlertSoundPath { get; set; } = ""; // Empty means the bundled extension sound.
    public ReflectionPopupPosition PopupPosition { get; set; } = ReflectionPopupPosition.Center;
    public bool LoggingEnabled { get; set; } = true;
    public bool StartAtLogin { get; set; }
    public bool ExtensionDisabledConfirmed { get; set; }
}

public interface IStateStore
{
    AppState Load();
    void Save(AppState state);
}

public record Activity(long At, string Event, Guid? ItemId = null, long? Value = null);
