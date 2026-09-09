using System.Net.NetworkInformation;
using Microsoft.Win32;
using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public sealed class TimerApplication : ApplicationContext
{
    public TimerEngine Engine { get; }
    public DiagnosticLog Log { get; }
    public SheetsClient Sheets { get; } = new();
    public AlertSoundPlayer Sounds { get; }
    private readonly MainWindow main;
    private readonly FloatingTimerWindow floating;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer pulse = new() { Interval = 1000 };
    private readonly EventWaitHandle showRequest;
    private readonly HashSet<Guid> postponed = [];
    private ReflectionWindow? reflection;
    private ScheduleConflictWindow? scheduleConflict;
    private GlobalShortcut? focusShortcut, endEarlyShortcut, compactShortcut, compactFocusShortcut, reflectionFocusShortcut;
    private readonly ConsecutiveShortcutPresses timerFocusPresses;
    private readonly CountdownPresentation countdownPresentation = new();
    private int syncing;
    private long lastSync;
    private bool quitting;
    private readonly bool enableAudio;
    private readonly HashSet<Guid> soundedPrompts = [];
    private long lastFailureSound;
    private bool timerSaveFailed;
    private long previewVersion;

    public TimerApplication(EncryptedStore store, string directory, EventWaitHandle showRequest, bool enableAudio = false,
        IAlertAudioBackend? audioBackend = null, TimeProvider? audioTimeProvider = null, Action<bool>? updateStartup = null,
        TimeProvider? shortcutTimeProvider = null)
    {
        timerFocusPresses = new(shortcutTimeProvider);
        this.enableAudio = enableAudio; // Tests opt out by default; the desktop entry point opts in.
        Sounds = new(audioBackend, timeProvider: audioTimeProvider);
        this.showRequest = showRequest;
        Engine = new(store);
        Log = new(directory) { Enabled = Engine.Snapshot.LoggingEnabled };
        main = new MainWindow(this, updateStartup);
        floating = new FloatingTimerWindow(this);
        main.TimerDuration.DraftChanged += () => {
            if (floating.IsDisposed) return;
            floating.Duration.CopyDraftFrom(main.TimerDuration);
            floating.Render(Engine.Snapshot, Engine.Now);
        };
        floating.Duration.DraftChanged += () => main.TimerDuration.CopyDraftFrom(floating.Duration);
        floating.Duration.CopyDraftFrom(main.TimerDuration);
        _ = main.Handle; // UI callbacks always have a synchronization target, even in tray mode.
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Reflection Timer", null, (_, _) => Open());
        menu.Items.Add("Show / hide floating timer", null, (_, _) => SetFloatingTimer(!Engine.Snapshot.ShowFloatingTimer));
        menu.Items.Add("Check in to current session", null, (_, _) => ShowCheckIn());
        menu.Items.Add("Pending reflections", null, (_, _) => ShowReflections());
        menu.Items.Add("Mark issue for debugging", null, (_, _) => MarkIssue());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        AppTheme.ApplyMenu(menu);
        tray = new NotifyIcon { Text = "Reflection Timer Desktop", Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Information,
            Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Open();
        Engine.ActivityRecorded += activity => {
            Log.Record(activity);
            if (activity.Event is "timer.paused" or "timer.reset" or "timer.started" or "timer.endedEarly") Sounds.Stop(SoundEvent.LowTime);
            if (activity.Event == "schedule.resolved" && activity.Value == (int)ScheduleDecision.StartNow) Sounds.Stop(SoundEvent.LowTime);
            if (activity.Event is "timer.deadline" or "timer.autoRestartDisabled" && activity.Value > 0) Sounds.Stop(SoundEvent.LowTime);
            if (activity.Event == "timer.lowTimeOptions" && !Engine.Snapshot.Timer.LowTime.Enabled) Sounds.Stop(SoundEvent.LowTime);
        };
        Engine.LowTimeReached += timer => Ui(() => {
            var current = Engine.Snapshot.Timer;
            if (!current.IsRunning || !current.LowTime.Enabled || current.EndTime != timer.EndTime || TimerEngine.Remaining(current, Engine.Now) <= 0) return;
            _ = PlaySound(SoundEvent.LowTime, current.Volume, AudioSettings.From(Engine.Snapshot).ForLowTime(timer.LowTime));
        });
        Engine.Changed += () => {
            var state = Engine.Snapshot;
            Sounds.UpdateVolumes(state.Timer.Volume, AudioSettings.From(state));
            Ui(Refresh);
        };
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
        pulse.Tick += (_, _) => Tick();
        pulse.Start();
        Log.Record("app.started");
        Refresh();
        if (store.RecoveryNotice is not null) ShowError(store.RecoveryNotice);
    }
    public void OpenUnlessTray(bool trayOnly) { if (!trayOnly || !Engine.Snapshot.ExtensionDisabledConfirmed) Open(); }
    public void OfferInitialSetup()
    {
        if (SheetsClient.Validate(Engine.Snapshot.Connection) is null && Engine.Snapshot.ExtensionDisabledConfirmed) return;
        Open(); Ui(main.ShowSetup);
    }
    public void Open()
    {
        if (quitting || main.IsDisposed) return;
        WindowActivation.Focus(main);
        if (main.Enabled) main.FocusDuration();
    }
    public void OpenTimerPage()
    {
        if (quitting || main.IsDisposed) return;
        WindowActivation.Focus(main);
        if (main.Enabled) main.FocusTimerPage();
    }
    public void EnableGlobalShortcut(IHotKeyRegistration? registration = null)
    {
        if (quitting || focusShortcut is not null || endEarlyShortcut is not null || compactShortcut is not null || compactFocusShortcut is not null || reflectionFocusShortcut is not null) return;
        try {
            focusShortcut = new GlobalShortcut(() => {
                if (quitting) return;
                timerFocusPresses.Reset(); Log.Record("shortcut.used"); Open();
            }, registration);
        }
        catch { Log.Record("error.unexpected"); }
        try {
            endEarlyShortcut = new GlobalShortcut(() => {
                if (quitting) return;
                timerFocusPresses.Reset(); Log.Record("shortcut.used", value: 1); StartOrEndTimer();
            }, registration, GlobalShortcut.EndEarlyKey, GlobalShortcut.EndEarlyId);
        }
        catch { Log.Record("error.unexpected"); }
        try {
            compactShortcut = new GlobalShortcut(() => {
                if (quitting) return;
                timerFocusPresses.Reset(); Log.Record("shortcut.used", value: 2); ToggleCompactTimer();
            }, registration, GlobalShortcut.CompactKey, GlobalShortcut.CompactId);
        }
        catch { Log.Record("error.unexpected"); }
        var available = focusShortcut?.IsRegistered == true;
        try {
            compactFocusShortcut = new GlobalShortcut(() => {
                if (quitting) return;
                Log.Record("shortcut.used", value: 3);
                if (timerFocusPresses.Press()) {
                    WindowActivation.Focus(main);
                    if (main.Enabled) main.FocusTimerPage();
                }
                else FocusCompactTimer();
            }, registration, GlobalShortcut.CompactFocusKey, GlobalShortcut.CompactFocusId);
        }
        catch { Log.Record("error.unexpected"); }
        var endAvailable = endEarlyShortcut?.IsRegistered == true;
        Log.Record(available ? "shortcut.registered" : "shortcut.unavailable");
        Log.Record(endAvailable ? "shortcut.registered" : "shortcut.unavailable", value: 1);
        var compactAvailable = compactShortcut?.IsRegistered == true;
        Log.Record(compactAvailable ? "shortcut.registered" : "shortcut.unavailable", value: 2);
        var compactFocusAvailable = compactFocusShortcut?.IsRegistered == true;
        Log.Record(compactFocusAvailable ? "shortcut.registered" : "shortcut.unavailable", value: 3);
        try {
            reflectionFocusShortcut = new GlobalShortcut(() => {
                if (quitting) return;
                timerFocusPresses.Reset(); Log.Record("shortcut.used", value: 4); ShowCheckIn();
            }, registration, GlobalShortcut.ReflectionFocusKey, GlobalShortcut.ReflectionFocusId);
        }
        catch { Log.Record("error.unexpected"); }
        var reflectionFocusAvailable = reflectionFocusShortcut?.IsRegistered == true;
        Log.Record(reflectionFocusAvailable ? "shortcut.registered" : "shortcut.unavailable", value: 4);
        main.SetShortcutStatus(available, endAvailable, compactAvailable, compactFocusAvailable, reflectionFocusAvailable);
    }
    public void StartOrEndTimer()
    {
        if (quitting) return;
        if (Engine.Snapshot.Timer.IsRunning) EndTimerEarly();
        else main.StartTimerFromShortcut();
    }
    public void SetFloatingTimer(bool visible)
    {
        try { floating.SavePosition(); Engine.SetFloatingTimer(visible); floating.Render(Engine.Snapshot, Engine.Now); }
        catch { ShowError("Could not save the floating timer preference."); }
    }
    internal int DisplaySeconds(TimerState timer, long now) => countdownPresentation.Seconds(timer, now);
    public void ToggleCompactTimer()
    {
        if (quitting) return;
        if (!Engine.Snapshot.ShowFloatingTimer) FocusCompactTimer();
        else if (!floating.ShrinkToTimeOnly()) SetFloatingTimer(false);
    }
    public void FocusCompactTimer()
    {
        if (quitting) return;
        if (!Engine.Snapshot.ShowFloatingTimer) SetFloatingTimer(true);
        if (Engine.Snapshot.ShowFloatingTimer) {
            // Settle a newly started session's shared fields before selecting;
            // an already queued refresh must not replace the selected text.
            main.Render(Engine.Snapshot);
            floating.FocusDuration(revealRunningControls: true);
        }
    }
    internal bool CanStartTimer => main.CanStartTimer;
    public void ResetTimer()
    {
        if (quitting || !Engine.Snapshot.ExtensionDisabledConfirmed) return;
        try { main.ResetTimer(); Refresh(); }
        catch { ShowError("Could not reset the timer. Your last saved timer is unchanged."); }
    }
    public void ToggleTimerPause()
    {
        try {
            if (!Engine.Snapshot.ExtensionDisabledConfirmed) return;
            if (Engine.Snapshot.Timer.IsRunning) Engine.Pause();
            else main.StartTimerFromShortcut();
            Refresh();
        } catch { ShowError("Could not update the timer. Your last saved timer is unchanged."); }
    }
    public void EndTimerEarly()
    {
        if (quitting || !Engine.Snapshot.ExtensionDisabledConfirmed) return;
        try {
            if (!Engine.EndEarly()) return;
        }
        catch {
            Log.Record("error.storage");
            Open(); main.SetStatus("Could not end the timer early. Your saved timer and reflections are unchanged. Check disk access and try again.", true);
            return;
        }
        Refresh(); // Show the normal completion popup immediately, even from the tray.
        if (reflection is not null) WindowActivation.Focus(reflection);
    }
    public void Ui(Action action)
    {
        if (quitting || main.IsDisposed) return;
        try { main.BeginInvoke(action); }
        catch (InvalidOperationException) when (quitting || main.IsDisposed || !main.IsHandleCreated) { }
    }
    public void ShowError(string message) { Log.Record("error.unexpected"); Ui(() => main.SetStatus(message, true)); }
    public void MarkIssue() { Log.Record("issue.marked", value: TimerEngine.Remaining(Engine.Snapshot.Timer, Engine.Now)); main.SetStatus("Issue marked. Export a diagnostic report from the Diagnostics tab."); }
    private void PowerChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) { Log.Record("system.resume"); Ui(Tick); }
    }
    private void SessionChanged(object? sender, SessionSwitchEventArgs e) { Log.Record("system.session", value: (int)e.Reason); Ui(Tick); }
    private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Ui(Refresh);
    private void Tick()
    {
        try
        {
            if (showRequest.WaitOne(0)) Open();
            if (Engine.Snapshot.ExtensionDisabledConfirmed) Engine.Advance();
            timerSaveFailed = false;
            main.RenderClock();
            floating.Render(Engine.Snapshot, Engine.Now);
            tray.Text = "Reflection Timer · " + MainWindow.Clock(DisplaySeconds(Engine.Snapshot.Timer, Engine.Now));
            if (Engine.Now - lastSync > 15000) { lastSync = Engine.Now; _ = Sync(); }
        }
        catch { Log.Record("error.storage"); main.SetStatus("Could not save a timer update. Check disk access; your last saved state is retained.", true, silent: timerSaveFailed); timerSaveFailed = true; }
    }
    private void Refresh()
    {
        var state = Engine.Snapshot;
        AppTheme.Change(state.Theme);
        Log.Enabled = state.LoggingEnabled;
        main.Render(state);
        floating.Render(state, Engine.Now);
        var newPrompts = state.Prompts.Where(x => !soundedPrompts.Contains(x.Id)).ToList();
        foreach (var prompt in newPrompts) soundedPrompts.Add(prompt.Id);
        soundedPrompts.IntersectWith(state.Prompts.Select(x => x.Id));
        // Sound each completion even while another reflection remains open; Later
        // and reopening that same draft do not replay the session-end effect.
        if (newPrompts.Any(x => !x.IsCheckIn)) _ = PlayAlertSound(state.Timer.Volume);
        EnsurePrompt();
        EnsureScheduleChoice();
    }
    private void EnsureScheduleChoice()
    {
        if (quitting || !Engine.Snapshot.ExtensionDisabledConfirmed) return;
        var pending = Engine.Snapshot.Schedules.FirstOrDefault(x => x.AwaitingDecision);
        if (scheduleConflict is not null) {
            if (!Engine.Snapshot.Schedules.Any(x => x.Id == scheduleConflict.SessionId && x.AwaitingDecision)) {
                scheduleConflict.Dispose(); scheduleConflict = null;
            }
            else return;
        }
        if (pending is null) return;
        scheduleConflict = new ScheduleConflictWindow(this, pending);
        scheduleConflict.FormClosed += (_, _) => { scheduleConflict = null; Ui(EnsureScheduleChoice); };
        scheduleConflict.Show(); scheduleConflict.Activate();
    }
    internal void QueueReflection(Guid promptId, string text, string? earlyEndReason = null)
    {
        Engine.QueueReflection(promptId, text, earlyEndReason);
        // A successful send completes this interaction, not the whole backlog.
        // Do this after the durable save and before Close / queued Refresh callbacks.
        // Existing drafts remain available explicitly; future completions have new
        // IDs and still open normally. A failed save leaves the window unchanged.
        postponed.UnionWith(Engine.Snapshot.Prompts.Select(x => x.Id));
    }
    public void ShowReflections()
    {
        if (quitting) return;
        postponed.Clear();
        if (reflection is null) EnsurePrompt(true);
        reflection?.FocusResponse();
        if (Engine.Snapshot.Prompts.Count == 0) { Open(); main.SetStatus("No reflections are waiting. Use Test reflection prompt to try one safely in the test tab."); }
    }
    public void ShowCheckIn()
    {
        if (quitting || !Engine.Snapshot.ExtensionDisabledConfirmed) return;
        try {
            var id = Engine.CheckIn();
            if (reflection?.PromptId == id) { reflection.FocusResponse(); return; }
            if (reflection is not null) {
                if (!reflection.PersistDraft()) { reflection.FocusResponse(); return; }
                reflection.Close();
                if (reflection is not null) return; // A failed close must not lose a draft.
            }
            postponed.Remove(id);
            OpenPrompt(Engine.Snapshot.Prompts.Single(x => x.Id == id));
        }
        catch (ArgumentException error) { Open(); main.SetStatus(error.Message); }
        catch { Open(); main.SetStatus("Could not save the check-in draft. Your timer and existing reflections are unchanged. Check disk access and try again.", true); }
    }
    private void EnsurePrompt(bool requested = false)
    {
        if (quitting || reflection is not null) return;
        var state = Engine.Snapshot;
        var pending = state.Prompts.FirstOrDefault(x => !postponed.Contains(x.Id) && (requested || !x.IsCheckIn && (x.IsTest || state.ExtensionDisabledConfirmed)));
        if (pending is null) return;
        OpenPrompt(pending);
    }
    private void OpenPrompt(ReflectionPrompt pending)
    {
        reflection = new ReflectionWindow(this, pending);
        reflection.FormClosed += (_, _) => {
            if (Engine.Snapshot.Prompts.Any(x => x.Id == pending.Id)) postponed.Add(pending.Id);
            reflection = null;
            Ui(() => EnsurePrompt());
        };
        Log.Record("prompt.shown", pending.Id);
        reflection.Show();
        reflection.Activate();
    }
    public async Task Sync()
    {
        if (Interlocked.CompareExchange(ref syncing, 1, 0) != 0) return;
        try
        {
            var settings = Engine.Snapshot.Connection;
            if (SheetsClient.Validate(settings) is not null || !NetworkInterface.GetIsNetworkAvailable()) return;
            if (!Engine.Snapshot.Outbox.Any(x => x.Status == DeliveryStatus.Pending && !(x.NextAttemptAt > Engine.Now))) return;
            // Verify capability for every delivery batch, including after receiver rollback.
            var capability = await Sheets.Ping(settings);
            if (!capability.Success) return;
            for (var sent = 0; sent < 20; sent++)
            {
                var item = Engine.BeginUpload(capability.SupportsSafeRetry);
                if (item is null) break;
                var reply = await Sheets.Upload(settings, item);
                Engine.FinishUpload(item.Id, reply.Success, reply.ErrorKind, reply.Tab, reply.Retryable);
                var retrying = Engine.Snapshot.Outbox.Any(x => x.Id == item.Id && x.Status == DeliveryStatus.Pending);
                Ui(() => main.SetStatus(reply.Success ? $"Reflection sent to {reply.Tab}." : retrying
                    ? "Reflection saved locally. Connection interrupted; a protected retry is scheduled."
                    : "A reflection needs review in the Outbox. " + reply.DisplayMessage, !reply.Success && !retrying, success: reply.Success));
                if (!reply.Success) break;
            }
        }
        catch { Log.Record("error.storage"); ShowError("An upload could not be finalized locally. Keep the app data and check the Outbox before retrying."); }
        finally { Interlocked.Exchange(ref syncing, 0); }
    }
    public void Quit()
    {
        if (Engine.Snapshot.Timer.IsRunning && MessageBox.Show(main,
            "Quit the tray app? It cannot alert while closed. The saved deadline will be recovered next time you open it.",
            "Quit Reflection Timer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (reflection is not null && !reflection.PersistDraft()) return;
        Log.Record("app.exiting");
        quitting = true; Sounds.Stop(); focusShortcut?.Dispose(); endEarlyShortcut?.Dispose(); compactShortcut?.Dispose(); compactFocusShortcut?.Dispose(); reflectionFocusShortcut?.Dispose(); pulse.Stop(); tray.Visible = false;
        reflection?.Dispose(); scheduleConflict?.Dispose(); floating.Dispose(); main.AllowExit = true; main.Close(); ExitThread();
    }
    public Task PlayAlertSound(int volume, bool preview = false) => PlaySound(SoundEvent.SessionEnd, volume, preview: preview);
    public void PlayFeedback(bool success)
    {
        if (!success) { var now = Environment.TickCount64; if (now - lastFailureSound < 750) return; lastFailureSound = now; }
        _ = PlaySound(success ? SoundEvent.Success : SoundEvent.Failure, Engine.Snapshot.Timer.Volume);
    }
    public async Task PlaySound(SoundEvent kind, int volume, SoundSetting? setting = null, bool preview = false, bool announcePreview = true)
    {
        if (!enableAudio || quitting) return;
        var version = preview ? ++previewVersion : 0;
        if (preview) {
            Log.Record("sound.preview", value: volume);
            if (announcePreview) main.SetStatus("Playing sound preview (up to 5 seconds)…");
        }
        var statusRevision = main.StatusRevision;
        setting ??= AudioSettings.From(Engine.Snapshot).For(kind);
        Log.Record("sound.requested", value: (int)kind * 10 + (int)setting.Behavior);
        var result = await Sounds.PlayAsync(SoundLibrary.Resolve(kind, setting), volume, setting.Behavior, kind, SoundLibrary.Fallback(kind), preview,
            setting.FadeOutEnabled ? setting.FadeOutAfterSeconds : null, setting.Volume);
        if (quitting) return;
        Log.Record(result switch {
            AlertSoundResult.Played or AlertSoundResult.PreviewFinished => "sound.played", AlertSoundResult.DefaultFallback => "sound.fallback",
            AlertSoundResult.Muted => "sound.muted", AlertSoundResult.Cancelled => "sound.stopped", _ => "sound.failed"
        }, value: volume);
        if (preview && (version != previewVersion || main.StatusRevision != statusRevision)) return;
        // Audio failures must never trigger their own failure sound recursively.
        if (result == AlertSoundResult.DefaultFallback) main.SetStatus("Selected MP3 unavailable. Played a fallback sound.", true, silent: true);
        else if (result == AlertSoundResult.Failed) main.SetStatus("Could not play the sound. Check your audio output; the timer and reflection are unaffected.", true, silent: true);
        else if (preview && announcePreview && result is AlertSoundResult.Played or AlertSoundResult.PreviewFinished) main.SetStatus("Sound preview finished.");
        else if (preview && announcePreview && result == AlertSoundResult.Muted) main.SetStatus(setting.Track == LibrarySound.None
            ? "None selected — this audio is disabled." : "Sound is muted. Raise App sound and this audio's volume to preview it.");
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            quitting = true; SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.SessionSwitch -= SessionChanged;
            SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
            focusShortcut?.Dispose(); endEarlyShortcut?.Dispose(); compactShortcut?.Dispose(); compactFocusShortcut?.Dispose(); reflectionFocusShortcut?.Dispose(); pulse.Dispose(); tray.Dispose(); reflection?.Dispose(); scheduleConflict?.Dispose(); floating.Dispose(); main.Dispose(); Sheets.Dispose(); Sounds.Dispose();
        }
        base.Dispose(disposing);
    }
}
