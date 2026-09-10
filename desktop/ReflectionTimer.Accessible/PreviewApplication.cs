using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

namespace ReflectionTimer.Accessible;

internal sealed class PreviewApplication : ApplicationContext
{
    internal PreviewSession Session { get; }
    internal string ProfileDirectory { get; }
    internal PreviewServices Services { get; }
    internal string? RecoveryNotice { get; set; }
    private readonly List<PreviewWindow> windows = [];
    private readonly System.Windows.Forms.Timer pulse = new() { Interval = 1000 };
    private PreviewWindow? active;
    private bool closing, tickFailed;
    private long lastSync;
    private readonly NotifyIcon tray;
    private readonly List<GlobalShortcut> shortcuts=[];
    private readonly ConsecutiveShortcutPresses compactPresses=new();
    internal PreviewApplication(PreviewSession session, string directory, string? recoveryNotice = null)
    {
        Session = session; ProfileDirectory = directory; RecoveryNotice=recoveryNotice;
        Services = new(session.Engine, directory); Services.Announcement += Announce;
        MainForm = Create("main");
        var menu=new ContextMenuStrip();
        menu.Items.Add("App view",null,(_,_)=>Open("main"));menu.Items.Add("Compact view",null,(_,_)=>Open("compact"));
        menu.Items.Add("Show / hide floating timer",null,(_,_)=>ToggleCompactVisibility());
        menu.Items.Add("Pending reflections",null,(_,_)=>{var p=Session.Engine.Snapshot.Prompts.FirstOrDefault();if(p is not null)Open("reflection",p.Id);else Announce("No pending reflections.");});
        menu.Items.Add("Quit accessibility preview",null,async(_,_)=>await CloseMainAsync());
        tray=new(){Text="Reflection Timer — accessibility preview",Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Information,Visible=true,ContextMenuStrip=menu};
        tray.DoubleClick+=(_,_)=>Open("main");
        session.Engine.Changed += () => {foreach(var window in windows.ToArray())window.ApplyWindowTheme();Broadcast(new { type = "state", state = session.View() });};
        session.Announcement += Announce;
        session.DurationDraftChanged+=parts=>Broadcast(new{type="durationDraft",parts});
        pulse.Tick += (_, _) => {
            try { var pending=Session.Engine.Snapshot.Prompts.Select(p=>p.Id).ToHashSet();
                Session.Tick();
                foreach(var prompt in Session.Engine.Snapshot.Prompts.Where(p=>!pending.Contains(p.Id))) { var window=Create("reflection",prompt.Id);window.ApplyPosition();window.Show(); }
                Broadcast(new { type = "clock", clock = Session.Clock() }); tickFailed = false;
                if (Session.Engine.Now - lastSync >= 15000) { lastSync = Session.Engine.Now; _ = Services.Sync(); } }
            catch { if (!tickFailed) Announce("Could not save a timer update. Your last saved state is retained."); tickFailed = true; }
        };
        pulse.Start(); MainForm.Show(); ApplyDisplayPreferences();
        RegisterShortcut(()=>{compactPresses.Reset();if(WindowActivation.IsForeground(MainForm))MainForm.Hide();else Open("main");},GlobalShortcut.Key,GlobalShortcut.HotKeyId);
        RegisterShortcut(()=>{compactPresses.Reset();var result=Session.Execute("end",System.Text.Json.JsonSerializer.SerializeToElement(new{}));if(result.OpenReflection is {} id)Open("reflection",id);},GlobalShortcut.EndEarlyKey,GlobalShortcut.EndEarlyId);
        RegisterShortcut(()=>{compactPresses.Reset();var compact=windows.FirstOrDefault(w=>w.View=="compact");if(compact is null)Open("compact");else if(compact.IsTimeOnly)ToggleCompactVisibility();else compact.Post(new{type="shrinkCompact"});},GlobalShortcut.CompactKey,GlobalShortcut.CompactId);
        RegisterShortcut(()=>{if(compactPresses.Press())Open("main");else Open("compact");},GlobalShortcut.CompactFocusKey,GlobalShortcut.CompactFocusId);
        RegisterShortcut(()=>{compactPresses.Reset();Open("reflection",Session.Engine.CheckIn());},GlobalShortcut.ReflectionFocusKey,GlobalShortcut.ReflectionFocusId);
    }
    private void RegisterShortcut(Action action,uint key,int id)
    {
        try{var shortcut=new GlobalShortcut(()=>{try{action();}catch(Exception e){Announce(e is ArgumentException?e.Message:"That action is unavailable. Your timer is retained.");}},key:key,id:id);shortcuts.Add(shortcut);Services.Log.Record(shortcut.IsRegistered?"shortcut.registered":"shortcut.unavailable",value:id);}
        catch{Services.Log.Record("shortcut.unavailable",value:id);}
    }
    internal void ToggleCompactVisibility(){Session.Engine.SetFloatingTimer(!Session.Engine.Snapshot.ShowFloatingTimer);ApplyDisplayPreferences();}
    private PreviewWindow Create(string view, Guid? prompt = null)
    {
        var window = new PreviewWindow(this, view, prompt);
        windows.Add(window); window.Activated += (_, _) => active = window;
        window.FormClosed += (_, _) => { windows.Remove(window); if (active == window) active = null; };
        return window;
    }
    internal void Open(string view, Guid? prompt = null)
    {
        if(view=="compact" && !Session.Engine.Snapshot.ShowFloatingTimer) Session.Engine.SetFloatingTimer(true);
        var window = windows.FirstOrDefault(w => w.View == view && w.PromptId == prompt);
        if(window is null){window=Create(view,prompt);window.ApplyPosition();}
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        WindowActivation.Focus(window);
        if(view is "main" or "compact")window.FocusControls();
    }
    internal void ApplyDisplayPreferences()
    {
        var compact=windows.FirstOrDefault(w=>w.View=="compact");
        if(Session.Engine.Snapshot.ShowFloatingTimer) { compact ??= Create("compact"); compact.Show(); }
        else compact?.CloseAfterSave();
        compact?.ApplyPosition();
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
    protected override void Dispose(bool disposing) { if (disposing) { foreach(var shortcut in shortcuts)shortcut.Dispose();tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose();pulse.Dispose(); Services.Dispose(); } base.Dispose(disposing); }
}
