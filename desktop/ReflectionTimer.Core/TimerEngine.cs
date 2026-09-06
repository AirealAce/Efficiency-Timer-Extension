namespace ReflectionTimer.Core;

// All state transitions commit a detached snapshot before becoming visible.
// No UI window or asynchronous upload owns the timer's lifetime.
public sealed class TimerEngine
{
    public const int MaxDuration = 365 * 24 * 3600;
    private readonly object gate = new();
    private readonly IStateStore store;
    private readonly Func<DateTimeOffset> clock;
    private AppState state;
    public event Action? Changed;
    public event Action<Activity>? ActivityRecorded;
    public AppState Snapshot { get { lock (gate) return DataJson.Clone(state); } }
    public long Now => clock().ToUnixTimeMilliseconds();

    public TimerEngine(IStateStore store, Func<DateTimeOffset>? clock = null)
    {
        this.store = store;
        this.clock = clock ?? (() => DateTimeOffset.Now);
        state = store.Load();
        if (state.FormatVersion != 1) throw new InvalidDataException("Unsupported local data version. Data was not changed.");
        // An interrupted upload is ambiguous: do not blindly retry a legacy receiver.
        if (state.Outbox.Any(x => x.Status == DeliveryStatus.Sending))
            Change("upload.recovered", s => s.Outbox = s.Outbox.Select(x => x.Status == DeliveryStatus.Sending
                ? x with { Status = DeliveryStatus.NeedsReview, ErrorKind = "interrupted" } : x).ToList());
    }

    public static int Remaining(TimerState timer, long now) => timer.IsRunning && timer.EndTime.HasValue
        ? (int)Math.Clamp(Math.Ceiling((timer.EndTime.Value - now) / 1000d), 0, MaxDuration)
        : timer.RemainingSeconds;

    private void Change(string name, Action<AppState> mutation, Guid? id = null, long? value = null)
    {
        lock (gate)
        {
            var next = DataJson.Clone(state);
            mutation(next);
            store.Save(next); // A failed disk write leaves the current state untouched.
            state = next;
        }
        ActivityRecorded?.Invoke(new Activity(Now, name, id, value));
        Changed?.Invoke();
    }

    public static void ValidateDuration(int seconds)
    {
        if (seconds is < 1 or > MaxDuration) throw new ArgumentException("Choose a duration greater than zero (up to one year).");
    }
    private static void ValidateCutoff(long? until, long after)
    {
        if (!until.HasValue) return;
        if (until <= after) throw new ArgumentException("Choose an auto-start cutoff after the session start (and in the future for the regular timer).");
        _ = DateTimeOffset.FromUnixTimeMilliseconds(until.Value);
    }
    private static bool CutoffDue(TimerState timer, long now) => timer.AutoRestartUntil <= now;
    private static void ExpireAutoRestart(AppState state, long now)
    {
        if (CutoffDue(state.Timer, now)) state.Timer = state.Timer with { AutoRestart = false, AutoRestartUntil = null };
    }
    private static TimerState Started(int seconds, bool repeat, int volume, long now, long? until = null) => new()
    {
        IsRunning = true, DurationSeconds = seconds, RemainingSeconds = seconds,
        EndTime = now + seconds * 1000L, AutoRestart = (repeat || until.HasValue) && !(until <= now),
        AutoRestartUntil = until > now ? until : null, Volume = Math.Clamp(volume, 0, 100)
    };

    public void Start(int seconds, bool repeat, int volume, long? autoRestartUntil = null)
    {
        ValidateDuration(seconds);
        Change("timer.started", s => {
            var now = Now;
            ValidateCutoff(autoRestartUntil, now);
            s.Timer = Started(seconds, repeat, volume, now, autoRestartUntil);
        }, value: seconds);
    }
    public void Pause() => Change("timer.paused", s => {
        var now = Now;
        ExpireAutoRestart(s, now);
        // A click can arrive after the deadline but before the one-second UI tick.
        // Pausing must not silently discard that completed session's reflection.
        if (s.Timer.IsRunning && s.Timer.EndTime <= now)
            s.Prompts.Add(new(Guid.NewGuid(), s.Timer.EndTime!.Value, s.Timer.DurationSeconds, s.Timer.Volume, false));
        s.Timer = s.Timer with { IsRunning = false, RemainingSeconds = Remaining(s.Timer, now), EndTime = null };
    });
    public void Resume()
    {
        Change("timer.resumed", s => {
            ExpireAutoRestart(s, Now);
            if (s.Timer.IsRunning) return;
            ValidateDuration(s.Timer.RemainingSeconds);
            s.Timer = s.Timer with { IsRunning = true, EndTime = Now + s.Timer.RemainingSeconds * 1000L };
        });
    }
    public void Reset(int? duration = null) => Change("timer.reset", s => {
        ExpireAutoRestart(s, Now);
        var seconds = duration ?? s.Timer.DurationSeconds;
        ValidateDuration(seconds);
        s.Timer = s.Timer with { IsRunning = false, DurationSeconds = seconds, RemainingSeconds = seconds, EndTime = null };
    });
    public void SetPreferences(bool repeat, int volume, long? autoRestartUntil = null) => Change("timer.preferences", s => {
        ValidateCutoff(autoRestartUntil, Now);
        s.Timer = s.Timer with { AutoRestart = repeat || autoRestartUntil.HasValue,
            AutoRestartUntil = autoRestartUntil, Volume = Math.Clamp(volume, 0, 100) };
    });

    public void Advance()
    {
        lock (gate)
        {
            var now = Now;
            var deadlineDue = state.Schedules.Any(x => x.StartTime <= now) || (state.Timer.IsRunning && state.Timer.EndTime <= now);
            var cutoffDue = CutoffDue(state.Timer, now);
            if (!deadlineDue && !cutoffDue) return;
            Change(cutoffDue ? "timer.autoRestartDisabled" : "timer.deadline", s => {
                // A cutoff is independent of the countdown, including while paused.
                // Expire before completion so no extra repeat starts at the boundary.
                ExpireAutoRestart(s, now);
                if (!deadlineDue) return;
                var due = s.Schedules.Where(x => x.StartTime <= now).OrderBy(x => x.StartTime).ToList();
                var latest = due.LastOrDefault();
                // Preserve a completed session's reflection, even if a scheduled
                // appointment takes over. Unfinished sessions are simply replaced.
                if (s.Timer.IsRunning && s.Timer.EndTime <= (latest?.StartTime ?? now))
                    s.Prompts.Add(new(Guid.NewGuid(), s.Timer.EndTime!.Value, s.Timer.DurationSeconds, s.Timer.Volume, false));
                if (latest is not null)
                {
                    s.Schedules.RemoveAll(x => x.StartTime <= now);
                    s.Timer = Started(latest.DurationSeconds, latest.AutoRestart, latest.Volume, now, latest.AutoRestartUntil);
                }
                else if (s.Timer.AutoRestart)
                    s.Timer = Started(s.Timer.DurationSeconds, true, s.Timer.Volume, now, s.Timer.AutoRestartUntil);
                else s.Timer = s.Timer with { IsRunning = false, RemainingSeconds = 0, EndTime = null };
            }, value: dueCount(state, now));
        }
        static long dueCount(AppState value, long now) => value.Schedules.Count(x => x.StartTime <= now);
    }

    public Guid SaveSchedule(Guid? id, DateTimeOffset start, int seconds, bool repeat, int volume, long? autoRestartUntil = null)
    {
        ValidateDuration(seconds);
        var itemId = id ?? Guid.NewGuid();
        Change("schedule.saved", s => {
            if (start.ToUnixTimeMilliseconds() <= Now) throw new ArgumentException("Choose a future session start date and time.");
            ValidateCutoff(autoRestartUntil, start.ToUnixTimeMilliseconds());
            if (id.HasValue && !s.Schedules.Any(x => x.Id == id)) throw new ArgumentException("This session already started or was removed.");
            if (!id.HasValue && s.Schedules.Count >= 50) throw new ArgumentException("You can schedule up to 50 sessions.");
            if (s.Schedules.Any(x => x.Id != itemId && x.StartTime == start.ToUnixTimeMilliseconds())) throw new ArgumentException("Another session already starts at that time.");
            s.Schedules.RemoveAll(x => x.Id == itemId);
            s.Schedules.Add(new(itemId, start.ToUnixTimeMilliseconds(), seconds, repeat || autoRestartUntil.HasValue, Math.Clamp(volume, 0, 100), autoRestartUntil));
            s.Schedules = s.Schedules.OrderBy(x => x.StartTime).ToList();
        }, itemId);
        return itemId;
    }
    public void RemoveSchedule(Guid id) => Change("schedule.removed", s => s.Schedules.RemoveAll(x => x.Id == id), id);
    public void ImportSchedules(IEnumerable<ScheduledSession> entries) => Change("schedule.saved", s => {
        foreach (var entry in entries) {
            ValidateDuration(entry.DurationSeconds);
            if (entry.StartTime <= Now || s.Schedules.Any(x => x.StartTime == entry.StartTime)) continue;
            ValidateCutoff(entry.AutoRestartUntil, entry.StartTime);
            if (s.Schedules.Count >= 50) throw new ArgumentException("The import would exceed 50 scheduled sessions. No entries were imported.");
            s.Schedules.Add(entry with { Id = Guid.NewGuid(), AutoRestart = entry.AutoRestart || entry.AutoRestartUntil.HasValue, Volume = Math.Clamp(entry.Volume, 0, 100) });
        }
        s.Schedules = s.Schedules.OrderBy(x => x.StartTime).ToList();
    });
    public Guid TestPrompt()
    {
        var id = Guid.NewGuid();
        Change("prompt.test", s => s.Prompts.Add(new(id, Now, s.Timer.DurationSeconds, s.Timer.Volume, true)), id);
        return id;
    }
    public void SaveDraft(Guid id, string text)
    {
        if (text.Length > 5000) text = text[..5000];
        lock (gate)
        {
            if (!state.Prompts.Any(x => x.Id == id && x.Draft != text)) return;
            Change("prompt.draftSaved", s => s.Prompts = s.Prompts.Select(x => x.Id == id ? x with { Draft = text } : x).ToList(), id);
        }
    }
    public void SkipPrompt(Guid id) => Change("prompt.skipped", s => s.Prompts.RemoveAll(x => x.Id == id), id);
    public void QueueReflection(Guid promptId, string text)
    {
        text = text.Trim();
        if (text.Length is < 1 or > 5000) throw new ArgumentException("Write a reflection between 1 and 5,000 characters.");
        Change("reflection.queued", s => {
            var prompt = s.Prompts.SingleOrDefault(x => x.Id == promptId) ?? throw new ArgumentException("This reflection has already been saved or dismissed.");
            s.Outbox.Add(new() {
                Id = promptId, Message = text, SubmittedAt = clock(), DurationSeconds = prompt.DurationSeconds,
                IsTest = prompt.IsTest, SheetUrl = s.Connection.SheetUrl, SheetMode = s.Connection.SheetMode, SheetName = s.Connection.SheetName
            });
            s.Prompts.RemoveAll(x => x.Id == promptId);
            // Keep at most 200 sent entries. Never automatically prune unsent work.
            var oldSent = s.Outbox.Where(x => x.Status == DeliveryStatus.Sent).OrderByDescending(x => x.SubmittedAt).Skip(200).Select(x => x.Id).ToHashSet();
            s.Outbox.RemoveAll(x => oldSent.Contains(x.Id));
        }, promptId);
    }
    public OutboxItem? BeginUpload()
    {
        lock (gate)
        {
            var item = state.Outbox.FirstOrDefault(x => x.Status == DeliveryStatus.Pending);
            if (item is null) return null;
            Change("upload.started", s => s.Outbox = s.Outbox.Select(x => x.Id == item.Id
                ? x with { Status = DeliveryStatus.Sending, Attempts = x.Attempts + 1, ErrorKind = "" } : x).ToList(), item.Id);
            return state.Outbox.Single(x => x.Id == item.Id);
        }
    }
    public void FinishUpload(Guid id, bool success, string errorKind = "", string tab = "") => Change(success ? "upload.sent" : "upload.needsReview", s =>
        s.Outbox = s.Outbox.Select(x => x.Id == id ? x with { Status = success ? DeliveryStatus.Sent : DeliveryStatus.NeedsReview,
            ErrorKind = SafeError(errorKind), SavedTab = success ? tab : "" } : x).ToList(), id);
    public void RetryUpload(Guid id) => Change("upload.retryRequested", s => {
        if (s.Outbox.Any(x => x.Id == id && x.Status == DeliveryStatus.Sending)) throw new ArgumentException("This reflection is still sending.");
        s.Outbox = s.Outbox.Select(x => x.Id == id && x.Status != DeliveryStatus.Sent
            ? x with { Status = DeliveryStatus.Pending, ErrorKind = "" } : x).ToList();
    }, id);
    public void MarkAlreadySent(Guid id) => Change("upload.confirmedByUser", s =>
        s.Outbox = s.Outbox.Select(x => x.Id == id && x.Status == DeliveryStatus.NeedsReview ? x with { Status = DeliveryStatus.Sent, ErrorKind = "" } : x).ToList(), id);
    public void SaveSettings(ConnectionSettings connection, bool logging, bool startAtLogin, bool extensionDisabled) => Change("settings.saved", s => {
        s.Connection = connection; s.LoggingEnabled = logging; s.StartAtLogin = startAtLogin; s.ExtensionDisabledConfirmed = extensionDisabled;
    });
    public static string SafeError(string kind) => new[] { "timeout", "network", "rejected", "invalid_response", "settings_required", "interrupted", "storage", "unknown" }.Contains(kind) ? kind : "unknown";
}
