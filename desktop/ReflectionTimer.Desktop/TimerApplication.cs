using System.Media;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public sealed class TimerApplication : ApplicationContext
{
    public TimerEngine Engine { get; }
    public DiagnosticLog Log { get; }
    public SheetsClient Sheets { get; } = new();
    private readonly MainWindow main;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer pulse = new() { Interval = 1000 };
    private readonly EventWaitHandle showRequest;
    private readonly HashSet<Guid> postponed = [];
    private ReflectionWindow? reflection;
    private int syncing;
    private long lastSync;
    private bool quitting;

    public TimerApplication(EncryptedStore store, string directory, EventWaitHandle showRequest)
    {
        this.showRequest = showRequest;
        Engine = new(store);
        Log = new(directory) { Enabled = Engine.Snapshot.LoggingEnabled };
        main = new MainWindow(this);
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
        Engine.ActivityRecorded += activity => Log.Record(activity);
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
    public void Open() { main.Show(); main.WindowState = FormWindowState.Normal; main.BringToFront(); main.Activate(); main.FocusHours(); }
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
            main.RenderClock();
            tray.Text = "Reflection Timer · " + MainWindow.Clock(TimerEngine.Remaining(Engine.Snapshot.Timer, Engine.Now));
            if (Engine.Now - lastSync > 15000) { lastSync = Engine.Now; _ = Sync(); }
        }
        catch { Log.Record("error.storage"); main.SetStatus("Could not save a timer update. Check disk access; your last saved state is retained.", true); }
    }
    private void Refresh()
    {
        var state = Engine.Snapshot;
        Log.Enabled = state.LoggingEnabled;
        main.Render(state);
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
        PlayTone(pending.Volume);
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
                Ui(() => main.SetStatus(reply.Success ? $"Reflection sent to {reply.Tab}." : "A reflection needs review in the Outbox. " + reply.DisplayMessage, !reply.Success));
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
        quitting = true; pulse.Stop(); tray.Visible = false;
        reflection?.Dispose(); main.AllowExit = true; main.Close(); ExitThread();
    }
    private static void PlayTone(int volume)
    {
        if (volume <= 0) return;
        _ = Task.Run(() => {
            try {
                const int rate = 22050, samples = 11025;
                using var stream = new MemoryStream();
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true)) {
                    writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
                    writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                    writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples * 2);
                    for (var i = 0; i < samples; i++) {
                        var envelope = Math.Min(1d, i / 500d) * Math.Max(0, 1 - i / (double)samples);
                        writer.Write((short)(Math.Sin(i * 2 * Math.PI * 660 / rate) * 18000 * Math.Clamp(volume, 0, 100) / 100 * envelope));
                    }
                }
                stream.Position = 0; using var player = new SoundPlayer(stream); player.PlaySync();
            } catch { /* Audio failure must not affect the timer or saved reflection. */ }
        });
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            quitting = true; SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.SessionSwitch -= SessionChanged;
            pulse.Dispose(); tray.Dispose(); reflection?.Dispose(); main.Dispose(); Sheets.Dispose();
        }
        base.Dispose(disposing);
    }
}
