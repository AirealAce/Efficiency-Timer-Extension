namespace ReflectionTimer.Accessible;

internal sealed class PreviewApplication : ApplicationContext
{
    internal PreviewSession Session { get; }
    internal string ProfileDirectory { get; }
    private readonly List<PreviewWindow> windows = [];
    private readonly System.Windows.Forms.Timer pulse = new() { Interval = 1000 };
    private PreviewWindow? active;
    private bool closing, tickFailed;
    internal PreviewApplication(PreviewSession session, string directory)
    {
        Session = session; ProfileDirectory = directory;
        MainForm = Create("main");
        session.Engine.Changed += () => Broadcast(new { type = "state", state = session.View() });
        session.Announcement += Announce;
        pulse.Tick += (_, _) => {
            try { Session.Tick(); Broadcast(new { type = "clock", clock = Session.Clock() }); tickFailed = false; }
            catch { if (!tickFailed) Announce("Could not save a timer update. Your last saved state is retained."); tickFailed = true; }
        };
        pulse.Start(); MainForm.Show();
    }
    private PreviewWindow Create(string view, Guid? prompt = null)
    {
        var window = new PreviewWindow(this, view, prompt);
        windows.Add(window); window.Activated += (_, _) => active = window;
        window.FormClosed += (_, _) => { windows.Remove(window); if (active == window) active = null; };
        return window;
    }
    internal void Open(string view, Guid? prompt = null)
    {
        var window = windows.FirstOrDefault(w => w.View == view && w.PromptId == prompt) ?? Create(view, prompt);
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        window.Show(); window.Activate();
    }
    internal void Broadcast(object message) { foreach (var window in windows.ToArray()) window.Post(message); }
    internal void Announce(string message)
    {
        if (message.Length == 0) return;
        (active is { Visible: true } ? active : MainForm as PreviewWindow)?.Post(new { type = "announcement", message });
    }
    internal async Task CloseMainAsync()
    {
        if (closing) return;
        closing = true;
        try {
            foreach (var window in windows.ToArray()) await window.FlushDraftAsync();
            pulse.Stop();
            foreach (var window in windows.Where(w => w != MainForm).ToArray()) window.CloseAfterSave();
            ((PreviewWindow)MainForm!).CloseAfterSave();
        }
        catch { Announce("Could not save a reflection draft. The preview is staying open. Try again."); }
        finally { closing = false; }
    }
    protected override void Dispose(bool disposing) { if (disposing) pulse.Dispose(); base.Dispose(disposing); }
}
