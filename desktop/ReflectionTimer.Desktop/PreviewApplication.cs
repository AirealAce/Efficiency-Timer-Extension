using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

namespace ReflectionTimer.Accessible;

internal sealed class PreviewApplication : ApplicationContext
{
    internal PreviewSession Session { get; }
    internal string ProfileDirectory { get; }
    internal string? ProfileName { get; }
    internal bool StartInTray { get; }
    internal object ShortcutState => shortcuts.Status;
    internal bool AppViewVisible => MainForm is { IsDisposed: false, Visible: true, WindowState: not FormWindowState.Minimized };
    internal PreviewServices Services { get; }
    internal string? RecoveryNotice { get; set; }
    private readonly List<PreviewWindow> windows = [];
    private readonly System.Windows.Forms.Timer pulse = new() { Interval = 1000 };
    private PreviewWindow? active;
    private bool closing, tickFailed;
    private bool? publishedAppViewVisible;
    private long lastSync;
    private readonly NotifyIcon tray;
    private (AppColorTheme Theme, bool Contrast)? menuTheme;
    private readonly PreviewShortcuts shortcuts;
    private readonly ConsecutiveShortcutPresses compactPresses=new();
    private readonly ReflectionPromptCoordinator promptCoordinator;
    internal PreviewApplication(PreviewSession session, string directory, string? recoveryNotice = null, bool startInTray = false, string? profileName = null)
    {
        Session = session; ProfileDirectory = directory; ProfileName=profileName; StartInTray=startInTray; RecoveryNotice=recoveryNotice;
        Services = new(session.Engine, directory); Services.Announcement += Announce;
        promptCoordinator=new(session,()=>windows.Where(w=>w.View=="reflection"&&!w.IsDisposed).Cast<IReflectionPromptWindow>().ToArray(),ShowReflection,()=>{_ = Services.Sync();});
        MainForm = Create("main");
        var menu=new ContextMenuStrip();
        menu.Items.Add("App view",null,(_,_)=>Open("main"));menu.Items.Add("Compact view",null,(_,_)=>Open("compact"));
        menu.Items.Add("Show / hide floating timer",null,(_,_)=>ToggleCompactVisibility());
        menu.Items.Add("Pending reflections",null,(_,_)=>{var p=Session.Engine.Snapshot.Prompts.LastOrDefault();if(p is not null)Open("reflection",p.Id);else Announce("No pending reflections.");});
        menu.Items.Add("Quit desktop app",null,async(_,_)=>await CloseMainAsync());
        tray=new(){Text="Reflection Timer",Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Information,Visible=true,ContextMenuStrip=menu};
        tray.DoubleClick+=(_,_)=>Open("main");
        session.Engine.Changed += () => {ApplyTheme();Broadcast(new { type = "state", state = session.View() });};
        session.Announcement += Announce;
        session.DurationDraftChanged+=parts=>Broadcast(new{type="durationDraft",parts});
        pulse.Tick += (_, _) => {
            try { var pending=Session.Engine.Snapshot.Prompts.Select(p=>p.Id).ToHashSet();
                Session.Tick();
                foreach(var prompt in Session.Engine.Snapshot.Prompts.Where(p=>!pending.Contains(p.Id))) _ = OpenReflectionAsync(prompt.Id,false);
                ApplyTheme();Broadcast(new { type = "clock", clock = Session.Clock() }); tickFailed = false;
                if (Session.Engine.Now - lastSync >= 15000) { lastSync = Session.Engine.Now; _ = Services.Sync(); if(shortcuts?.RetryUnavailable()==true)Broadcast(new{type="shortcuts",shortcuts=ShortcutState}); } }
            catch { if (!tickFailed) Announce("Could not save a timer update. Your last saved state is retained."); tickFailed = true; }
        };
        shortcuts = new PreviewShortcuts([
            Shortcut(0, ()=>{compactPresses.Reset();if(WindowActivation.IsForeground(MainForm))MainForm.Hide();else Open("main");}),
            Shortcut(1, ()=>{compactPresses.Reset();var result=Session.Execute("startOrEnd",System.Text.Json.JsonSerializer.SerializeToElement(new{}));if(result.OpenReflection is {} id)Open("reflection",id);}),
            Shortcut(2, ()=>{compactPresses.Reset();var compact=windows.FirstOrDefault(w=>w.View=="compact");if(compact is null)Open("compact");else if(compact.IsTimeOnly)ToggleCompactVisibility();else compact.Post(new{type="shrinkCompact"});}),
            Shortcut(3, ()=>{if(compactPresses.Press())Open("main",timerPage:true);else Open("compact");}),
            Shortcut(4, ()=>{compactPresses.Reset();ReflectionShortcut.Invoke(windows.Where(w=>w.View=="reflection"&&!w.IsDisposed),OpenPendingOrCheckIn);})
        ], (id,available)=>Services.Log.Record(available?"shortcut.registered":"shortcut.unavailable",value:id));
        ApplyTheme();pulse.Start(); if(!startInTray)MainForm.Show(); ApplyDisplayPreferences();
    }
    private void ApplyTheme()
    {
        foreach(var window in windows.ToArray()){window.ApplyWindowTheme();window.ApplyTopMost();}
        var preference=(Session.Engine.Snapshot.Theme,SystemInformation.HighContrast);
        if(menuTheme==preference)return;
        menuTheme=preference;PreviewTheme.ApplyMenu(tray.ContextMenuStrip!,PreviewTheme.Palette(preference.Item1,preference.Item2));
    }
    private Action Shortcut(int id, Action action) => () =>
    {
        if(closing)return;
        Services.Log.Record("shortcut.used",value:id);
        try { action(); }
        catch(Exception e) { if(id!=4)Open("main"); Announce(e is ArgumentException?e.Message:"That action is unavailable. Your timer is retained."); }
    };
    private void OpenPendingOrCheckIn()
    {
        if(Session.ReflectionForShortcut() is {} id)Open("reflection",id);
        else Announce("No pending reflection. Start a timer before making a check-in.");
    }
    internal void SetStartup(bool enabled)
    {
        var previous=Session.Engine.Snapshot.StartAtLogin;
        if(previous==enabled)return;
        var profile=ProfileName;
        PreviewStartup.Set(profile,enabled);
        try { var state=Session.Engine.Snapshot;Session.Engine.SaveSettings(state.Connection,state.LoggingEnabled,enabled,state.ExtensionDisabledConfirmed); }
        catch { PreviewStartup.Set(profile,previous); throw; }
    }
    internal void ToggleCompactVisibility(){Session.Engine.SetFloatingTimer(!Session.Engine.Snapshot.ShowFloatingTimer);ApplyDisplayPreferences();}
    private PreviewWindow Create(string view, Guid? prompt = null)
    {
        var window = new PreviewWindow(this, view, prompt);
        windows.Add(window); window.Activated += (_, _) => {active = window;Services.Log.Record("app.activated");};
        window.Deactivate+=(_,_)=>Services.Log.Record("app.deactivated");
        if(view=="main") {
            window.VisibleChanged+=(_,_)=>PublishAppViewVisibility();
            window.Resize+=(_,_)=>PublishAppViewVisibility();
        }
        window.FormClosed += (_, _) => { windows.Remove(window); if (active == window) active = null; };
        return window;
    }
    internal void Open(string view, Guid? prompt = null, bool timerPage=false)
    {
        if(view=="reflection"){if(prompt is {} id)_ = OpenReflectionAsync(id,true);return;}
        if(view=="compact" && !Session.Engine.Snapshot.ShowFloatingTimer) Session.Engine.SetFloatingTimer(true);
        var window = windows.FirstOrDefault(w => w.View == view && w.PromptId == prompt);
        if(window is null){window=Create(view,prompt);window.ApplyPosition();}
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
        WindowActivation.Focus(window);
        if(WindowActivation.CanReceiveFocus(window))window.FocusControls(timerPage);
    }
    private async Task OpenReflectionAsync(Guid id,bool activate)
    {
        try {await promptCoordinator.OpenAsync(id,activate,()=>closing);}
        catch {Announce("The previous reflection could not be saved. It stays open; the new reflection is saved under Pending reflections. Try opening it again.");}
    }
    private void ShowReflection(Guid id,bool activate)
    {
        var window=windows.FirstOrDefault(w=>w.View=="reflection"&&w.PromptId==id);
        if(window is null){window=Create("reflection",id);window.ApplyPosition();}
        if(activate){WindowActivation.Focus(window);if(WindowActivation.CanReceiveFocus(window))window.FocusControls();}
        else window.Show();
    }
    internal void ApplyDisplayPreferences()
    {
        var compact=windows.FirstOrDefault(w=>w.View=="compact");
        if(Session.Engine.Snapshot.ShowFloatingTimer) { compact ??= Create("compact"); compact.Show(); }
        else compact?.CloseAfterSave();
        compact?.ApplyPosition();
    }
    internal void Broadcast(object message) { foreach (var window in windows.ToArray()) window.Post(message); }
    private void PublishAppViewVisibility()
    {
        var visible=AppViewVisible;
        if(publishedAppViewVisible==visible)return;
        publishedAppViewVisible=visible;
        Broadcast(new{type="appViewVisibility",visible});
    }
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
            await promptCoordinator.ExclusivelyAsync(async()=>{
                foreach (var window in windows.ToArray()) await window.FlushDraftAsync();
                Services.Log.Record("app.exiting");pulse.Stop();
                foreach (var window in windows.Where(w => w != MainForm).ToArray()) window.CloseAfterSave();
                ((PreviewWindow)MainForm!).CloseAfterSave();
            });
        }
        catch { Announce("Could not save a reflection draft. The app is staying open. Try again."); }
        finally { closing = false; }
    }
    protected override void Dispose(bool disposing) { if (disposing) { shortcuts.Dispose();tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose();pulse.Dispose(); Services.Dispose(); } base.Dispose(disposing); }
}
