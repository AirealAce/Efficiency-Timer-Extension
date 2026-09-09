using System.Text.Json;

namespace ReflectionTimer.Core;

public static class DataJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}

public record TimerState
{
    public Guid? SessionId { get; init; }
    public bool IsRunning { get; init; }
    public int DurationSeconds { get; init; } = 1500;
    public int RemainingSeconds { get; init; } = 1500;
    public long? PausedRemainingMilliseconds { get; init; }
    public long? EndTime { get; init; }
    public bool AutoRestart { get; init; }
    public long? AutoRestartUntil { get; init; }
    public int Volume { get; init; } = 50;
    public LowTimeOptions LowTime { get; init; } = new();
    public bool LowTimePlayed { get; init; }
}

public record ScheduledSession(Guid Id, long StartTime, int DurationSeconds, bool AutoRestart, int Volume, long? AutoRestartUntil = null)
{
    public LowTimeOptions LowTime { get; init; } = new();
    public bool WaitingForCurrentSession { get; init; }
    public bool AwaitingDecision { get; init; }
}
public enum ScheduleOverlapPolicy { EndWithReflection = 0, Ask = 1, Wait = 2 }
public enum ScheduleDecision { StartNow = 0, Wait = 1, Skip = 2 }
public record ReflectionPrompt(Guid Id, long CompletedAt, int DurationSeconds, int Volume, bool IsTest, string Draft = "")
{
    // Null means an older prompt did not capture actual elapsed time.
    public int? ActualDurationSeconds { get; init; }
    public bool EndedEarly { get; init; }
    public string EarlyEndReason { get; init; } = "";
    public bool IsCheckIn { get; init; }
    public Guid? CheckInSessionId { get; init; }
}

public record ConnectionSettings
{
    public string SheetUrl { get; init; } = "";
    public string WebAppUrl { get; init; } = "";
    public string ApiToken { get; init; } = "";
    public string SheetMode { get; init; } = "date";
    public string SheetName { get; init; } = "Template";
}

public enum DeliveryStatus { Pending, Sending, Sent, NeedsReview }
public enum ReflectionPopupPosition { Center = 0, TopLeft = 1, TopRight = 2, BottomLeft = 3, BottomRight = 4 }
public enum AppColorTheme { Dark = 0, Light = 1, HighContrast = 2, Glamour = 3 }
public enum FloatingTimerPlacement { Custom = 0, Center = 1, TopLeft = 2, TopRight = 3, BottomLeft = 4, BottomRight = 5, TopCenter = 6, BottomCenter = 7 }
public record OutboxItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Message { get; init; } = "";
    public DateTimeOffset SubmittedAt { get; init; }
    public int DurationSeconds { get; init; }
    public int? ActualDurationSeconds { get; init; }
    public bool EndedEarly { get; init; }
    public bool IsCheckIn { get; init; }
    public string EarlyEndReason { get; init; } = "";
    public bool IsTest { get; init; }
    public string SheetUrl { get; init; } = "";
    public string SheetMode { get; init; } = "date";
    public string SheetName { get; init; } = "Template";
    public DeliveryStatus Status { get; init; }
    public int Attempts { get; init; }
    public bool RetryProtected { get; init; }
    public string ReceiverUrl { get; init; } = "";
    public long? NextAttemptAt { get; init; }
    public string ErrorKind { get; init; } = "";
    public string SavedTab { get; init; } = "";
}

public record AppState
{
    public int FormatVersion { get; init; } = 1;
    public TimerState Timer { get; set; } = new();
    public List<ScheduledSession> Schedules { get; set; } = [];
    public ScheduleOverlapPolicy ScheduleOverlap { get; set; } = ScheduleOverlapPolicy.EndWithReflection;
    public List<ReflectionPrompt> Prompts { get; set; } = [];
    public List<OutboxItem> Outbox { get; set; } = [];
    public ConnectionSettings Connection { get; set; } = new();
    public ConnectionSettings? SetupDraft { get; set; } // Encrypted; never used for uploads before setup completes.
    public bool? SetupDraftUsesExistingReceiver { get; set; }
    public string AlertSoundPath { get; set; } = ""; // Empty means the bundled extension sound.
    public AudioSettings? Audio { get; set; } // Null migrates the existing session-end MP3 without changing it.
    public ReflectionPopupPosition PopupPosition { get; set; } = ReflectionPopupPosition.Center;
    public bool ShowFloatingTimer { get; set; }
    public int? FloatingTimerLeft { get; set; }
    public int? FloatingTimerTop { get; set; }
    public FloatingTimerPlacement FloatingPlacement { get; set; }
    public AppColorTheme Theme { get; set; } = AppColorTheme.Dark;
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
