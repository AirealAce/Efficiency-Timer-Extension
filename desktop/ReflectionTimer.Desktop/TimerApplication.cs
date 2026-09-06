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
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer pulse = new() { Interval = 1000 };
    private readonly EventWaitHandle showRequest;
    private readonly HashSet<Guid> postponed = [];
    private ReflectionWindow? reflection;
    private GlobalShortcut? focusShortcut;
    private int syncing;
    private long lastSync;
    private bool quitting;
    private readonly bool enableAudio;
    private readonly HashSet<Guid> soundedPrompts = [];
    private long lastFailureSound;
    private bool timerSaveFailed;
    private long previewVersion;

    public TimerApplication(EncryptedStore store, string directory, EventWaitHandle showRequest, bool enableAudio = false,
        IAlertAudioBackend? audioBackend = null, TimeProvider? audioTimeProvider = null, Action<bool>? updateStartup = null)
    {
        this.enableAudio = enableAudio; // Tests opt out by default; the desktop entry point opts in.
        Sounds = new(audioBackend, timeProvider: audioTimeProvider);
        this.showRequest = showRequest;
        Engine = new(store);
        Log = new(directory) { Enabled = Engine.Snapshot.LoggingEnabled };
        main = new MainWindow(this, updateStartup);
        _ = main.Handle; // UI callbacks always have a synchronization target, even in tray mode.
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Reflection Timer", null, (_, _) => Open());
        menu.Items.Add("Pending reflections", null, (_, _) => ShowReflections());
        menu.Items.Add("Mark issue for debugging", null, (_, _) => MarkIssue());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        tray = new NotifyIcon { Text = "Reflection Timer Desktop", Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Information,
            Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Open();
        tray.BalloonTipClicked += (_, _) => ShowReflections();
        Engine.ActivityRecorded += activity => {
            Log.Record(activity);
            if (activity.Event is "timer.paused" or "timer.reset" or "timer.started") Sounds.Stop(SoundEvent.LowTime);
            if (activity.Event is "timer.deadline" or "timer.autoRestartDisabled" && activity.Value > 0) Sounds.Stop(SoundEvent.LowTime);
            if (activity.Event == "timer.lowTimeOptions" && !Engine.Snapshot.Timer.LowTime.Enabled) Sounds.Stop(SoundEvent.LowTime);
        };
        Engine.LowTimeReached += timer => Ui(() => {
            var current = Engine.Snapshot.Timer;
            if (!current.IsRunning || !current.LowTime.Enabled || current.EndTime != timer.EndTime || TimerEngine.Remaining(current, Engine.Now) <= 0) return;
            _ = PlaySound(SoundEvent.LowTime, timer.Volume, AudioSettings.From(Engine.Snapshot).ForLowTime(timer.LowTime));
        });
        Engine.Changed += () => Ui(Refresh);
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        pulse.Tick += (_, _) => Tick();
        pulse.Start();
        Log.Record("app.started");
        Refresh();
        if (store.RecoveryNotice is not null) ShowError(store.RecoveryNotice);
    }
    public void OpenUnlessTray(bool trayOnly) { if (!trayOnly || !Engine.Snapshot.ExtensionDisabledConfirmed) Open(); }
    public void Open()
    {
        if (quitting || main.IsDisposed) return;
        WindowActivation.Focus(main);
        if (main.Enabled) main.FocusHours();
    }
    public void EnableGlobalShortcut(IHotKeyRegistration? registration = null)
    {
        if (quitting || focusShortcut is not null) return;
        try {
            focusShortcut = new GlobalShortcut(() => {
                if (quitting) return;
                Log.Record("shortcut.used"); Open();
            }, registration);
        }
        catch { Log.Record("error.unexpected"); }
        var available = focusShortcut?.IsRegistered == true;
        Log.Record(available ? "shortcut.registered" : "shortcut.unavailable");
        main.SetShortcutStatus(available);
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
    private void Tick()
    {
        try
        {
            if (showRequest.WaitOne(0)) Open();
            if (Engine.Snapshot.ExtensionDisabledConfirmed) Engine.Advance();
            timerSaveFailed = false;
            main.RenderClock();
            tray.Text = "Reflection Timer · " + MainWindow.Clock(TimerEngine.Remaining(Engine.Snapshot.Timer, Engine.Now));
            if (Engine.Now - lastSync > 15000) { lastSync = Engine.Now; _ = Sync(); }
        }
        catch { Log.Record("error.storage"); main.SetStatus("Could not save a timer update. Check disk access; your last saved state is retained.", true, silent: timerSaveFailed); timerSaveFailed = true; }
    }
    private void Refresh()
    {
        var state = Engine.Snapshot;
        Log.Enabled = state.LoggingEnabled;
        main.Render(state);
        var newPrompts = state.Prompts.Where(x => !soundedPrompts.Contains(x.Id)).ToList();
        foreach (var prompt in newPrompts) soundedPrompts.Add(prompt.Id);
        soundedPrompts.IntersectWith(state.Prompts.Select(x => x.Id));
        // Sound each completion even while another reflection remains open; Later
        // and reopening that same draft do not replay the session-end effect.
        if (newPrompts.LastOrDefault() is { } completed) _ = PlayAlertSound(completed.Volume);
        EnsurePrompt();
    }
    public void ShowReflections()
    {
        postponed.Clear();
        if (reflection is not null) { reflection.Show(); reflection.Activate(); }
        else EnsurePrompt(true);
        if (Engine.Snapshot.Prompts.Count == 0) { Open(); main.SetStatus("No reflections are waiting. Use Test reflection prompt to try one safely in the test tab."); }
    }
    private void EnsurePrompt(bool requested = false)
    {
        if (quitting || reflection is not null) return;
        var state = Engine.Snapshot;
        var pending = state.Prompts.FirstOrDefault(x => !postponed.Contains(x.Id) && (requested || x.IsTest || state.ExtensionDisabledConfirmed));
        if (pending is null) return;
        reflection = new ReflectionWindow(this, pending);
        reflection.FormClosed += (_, _) => {
            if (Engine.Snapshot.Prompts.Any(x => x.Id == pending.Id)) postponed.Add(pending.Id);
            reflection = null;
            Ui(() => EnsurePrompt());
        };
        Log.Record("prompt.shown", pending.Id);
        tray.ShowBalloonTip(5000, pending.IsTest ? "Test reflection" : "Session complete", "Your reflection window is ready.", ToolTipIcon.Info);
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
            for (var sent = 0; sent < 20; sent++)
            {
                var item = Engine.BeginUpload();
                if (item is null) break;
                var reply = await Sheets.Upload(settings, item);
                Engine.FinishUpload(item.Id, reply.Success, reply.ErrorKind, reply.Tab);
                Ui(() => main.SetStatus(reply.Success ? $"Reflection sent to {reply.Tab}." : "A reflection needs review in the Outbox. " + reply.DisplayMessage, !reply.Success, success: reply.Success));
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
        quitting = true; Sounds.Stop(); focusShortcut?.Dispose(); pulse.Stop(); tray.Visible = false;
        reflection?.Dispose(); main.AllowExit = true; main.Close(); ExitThread();
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
        var result = await Sounds.PlayAsync(SoundLibrary.Resolve(kind, setting), volume, setting.Behavior, kind, SoundLibrary.Fallback(kind), preview);
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
            ? "None selected — this audio is disabled." : "Sound is muted. Raise the Timer sound level to preview it.");
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            quitting = true; SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.SessionSwitch -= SessionChanged;
            focusShortcut?.Dispose(); pulse.Dispose(); tray.Dispose(); reflection?.Dispose(); main.Dispose(); Sheets.Dispose(); Sounds.Dispose();
        }
        base.Dispose(disposing);
    }
}
