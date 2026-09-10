using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Runtime.InteropServices;

namespace ReflectionTimer.Accessible;

internal sealed partial class PreviewWindow : Form
{
    internal const string Origin = "https://reflection-timer.invalid";
    internal string View { get; }
    internal Guid? PromptId { get; }
    internal bool IsTimeOnly { get; private set; }
    private readonly PreviewApplication app;
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill, AccessibleName = "Reflection Timer" };
    private bool ready, allowClose, requestingClose;
    private bool focusOnReady, selectTimerOnReady;
    private (ReflectionTimer.Core.AppColorTheme Theme,bool Contrast)? appliedTheme;
    private TaskCompletionSource? flush;
    internal PreviewWindow(PreviewApplication app, string view, Guid? prompt)
    {
        this.app = app; View = view; PromptId = prompt;
        Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??SystemIcons.Information;
        browser.AccessibleName=view=="main"?"Reflection Timer App view":view=="compact"?"Reflection Timer Compact and Time-only view":"Reflection Timer Session end prompt";
        Text = view == "main" ? "Reflection Timer — App view · 4.0.7" : view == "compact" ? "Reflection Timer — Compact view · 4.0.7" : "Reflection Timer — Session end · 4.0.7";
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
        Size = view == "main" ? new(940, 810) : view == "compact" ? new(228, 200) : new(560, app.Session.Engine.Snapshot.Prompts.Any(p=>p.Id==prompt&&p.EndedEarly)?525:440);
        MinimumSize = view == "main" ? new(420, 400) : view == "compact" ? new(80,32) : new(420,360);
        if(view=="compact") { FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; MaximizeBox=false; MinimizeBox=false; }
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = view == "main" ? ViewPlacement.Calculate(area,Size,1)
            : new(view == "compact" ? area.Left + 16 : Math.Max(area.Left, area.Right - Width - 16), Math.Max(area.Top, area.Bottom - Height - 16));
        TopMost = view == "compact";
        Controls.Add(browser);
        // WebView2 handles browser accelerators before DOM keyboard events.
        // Defer the message until its synchronous key handler has returned.
        browser.KeyDown+=(_,e)=>{
            if(View!="main"||!ready||e.KeyCode!=Keys.Tab||(e.Modifiers!=Keys.Control&&e.Modifiers!=(Keys.Control|Keys.Shift)))return;
            var backward=e.Shift;e.Handled=true;e.SuppressKeyPress=true;
            BeginInvoke(()=>Post(new{type="cycleAppTab",backward}));
        };
        HandleCreated+=(_,_)=>ApplyWindowTheme();
        Shown += async (_, _) => await InitializeAsync();
        ResizeEnd+=(_,_)=>{if(View=="compact")try{app.Session.Engine.SetFloatingTimerPosition(Left,Top);}catch{app.Announce("Could not save the compact position.");}};
        FormClosing += async (_, e) => {
            if (allowClose) return;
            e.Cancel = true;
            if (View == "main") { Hide(); return; }
            if (requestingClose) return;
            requestingClose = true;
            try { await FlushDraftAsync(); if(View=="compact") app.Session.Engine.SetFloatingTimer(false); CloseAfterSave(); }
            catch { Post(new { type = "announcement", message = "Draft could not be saved. This window is staying open. Try again." }); }
            finally { requestingClose = false; }
        };
    }
    protected override bool ShowWithoutActivation => View is "compact" or "reflection";
    protected override CreateParams CreateParams {get{var value=base.CreateParams;if(View=="compact")value.ExStyle=(value.ExStyle|0x80)&~0x40000;return value;}}
    internal void FocusControls(bool timerPage=false){if(!ready){focusOnReady=true;selectTimerOnReady=timerPage;return;}Post(new{type=View=="compact"?"expandCompact":View=="reflection"?"focusReflection":"focusTimer",selectTimer=timerPage});}
    internal void ApplyWindowTheme()
    {
        if(!IsHandleCreated)return;var theme=app.Session.Engine.Snapshot.Theme;var contrast=SystemInformation.HighContrast;
        if(appliedTheme==(theme,contrast))return;appliedTheme=(theme,contrast);
        var colors=PreviewTheme.Palette(theme,contrast);
        BackColor=colors.Background;browser.DefaultBackgroundColor=BackColor;
        int dark=!contrast&&(int)theme is 0 or 2?1:0,caption=contrast?-1:ColorTranslator.ToWin32(colors.Raised),text=contrast?-1:ColorTranslator.ToWin32(colors.Text),border=contrast?-1:ColorTranslator.ToWin32(colors.Border);
        DwmSetWindowAttribute(Handle,20,ref dark,4);DwmSetWindowAttribute(Handle,35,ref caption,4);DwmSetWindowAttribute(Handle,36,ref text,4);DwmSetWindowAttribute(Handle,34,ref border,4);
    }
    internal void ApplyPosition()
    {
        if(View=="main") return;
        var state=app.Session.Engine.Snapshot; var area=View=="reflection"?Screen.FromPoint(Cursor.Position).WorkingArea:Screen.FromControl(app.MainForm!).WorkingArea;
        if(View=="compact"&&state.FloatingPlacement==ReflectionTimer.Core.FloatingTimerPlacement.Custom&&state.FloatingTimerLeft is {} left&&state.FloatingTimerTop is {} top){area=Screen.FromPoint(new(left,top)).WorkingArea;Location=new(Math.Clamp(left,area.Left,Math.Max(area.Left,area.Right-Width)),Math.Clamp(top,area.Top,Math.Max(area.Top,area.Bottom-Height)));return;}
        var position=View=="compact" ? (int)state.FloatingPlacement : state.PopupPosition switch {
            ReflectionTimer.Core.ReflectionPopupPosition.TopLeft=>2, ReflectionTimer.Core.ReflectionPopupPosition.TopRight=>3,
            ReflectionTimer.Core.ReflectionPopupPosition.BottomLeft=>4, ReflectionTimer.Core.ReflectionPopupPosition.BottomRight=>5, _=>1 };
        Location=ViewPlacement.Calculate(area,Size,position);
    }
    private async Task InitializeAsync()
    {
        try {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(app.ProfileDirectory, "WebView2"));
            if (IsDisposed) return;
            await browser.EnsureCoreWebView2Async(environment);
            if (IsDisposed) return;
            var core = browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = true;
            core.SetVirtualHostNameToFolderMapping("reflection-timer.invalid", Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (!Allowed(e.Uri)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            browser.ZoomFactorChanged+=(_,_)=>{if(View=="compact")Post(new{type="measureCompact"});};
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => {
                if (!Allowed(e.Request.Uri)) e.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain");
            };
            core.WebMessageReceived += Receive;
            core.NavigationCompleted += (_, e) => { if (!e.IsSuccess) ShowFailure("The local interface could not be loaded. Close and reopen the app."); };
            core.ProcessFailed += (_, _) => { ready = false; flush?.TrySetException(new IOException("Web view unavailable.")); ShowFailure("The web interface stopped responding. Close and reopen the app. Previously saved drafts are retained."); };
            core.Navigate(Origin + (View=="compact" ? "/compact.html" : "/index.html?view=" + View));
        }
        catch (WebView2RuntimeNotFoundException) { ShowFailure("Microsoft Edge WebView2 Runtime is required. Install the Evergreen Runtime from https://developer.microsoft.com/microsoft-edge/webview2/ and reopen the app."); }
        catch { ShowFailure("The local web interface could not start. Close and reopen the app. Your saved data is retained."); }
    }
    internal static bool Allowed(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "reflection-timer.invalid"
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath is "/index.html" or "/app.js" or "/app.css" or "/ui.js" or "/settings.js" or "/audio.js" or "/setup.js" or "/low-time.js" or "/compact.html" or "/compact.js" or "/compact.css" or "/layout.js" or "/themes.css" or "/themes.js";
    private void ShowFailure(string text)
    {
        if (IsDisposed) return;
        browser.Visible = false;
        var explanation = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = text, AccessibleName = "App startup error", Font = new("Segoe UI", 12) };
        Controls.Add(explanation); explanation.BringToFront(); explanation.Focus();
    }
    private async void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Allowed(e.Source)) return;
        string? requestId = null;
        try {
            if (e.WebMessageAsJson.Length > 40000) throw new ArgumentException("Message too large.");
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            requestId = root.GetProperty("requestId").GetString();
            if (requestId is null || requestId.Length > 64) throw new ArgumentException("Invalid request.");
            var action = root.GetProperty("action").GetString() ?? "";
            var data = root.GetProperty("data");
            if (action == "ready") {
                ready = true; Post(new { type = "init", view = View, promptId = PromptId, state = app.Session.View(), appViewVisible = app.AppViewVisible });
                if(View=="main" && app.RecoveryNotice is { } notice) { Post(new { type="announcement", message=notice }); app.RecoveryNotice=null; }
                if(focusOnReady){focusOnReady=false;FocusControls(selectTimerOnReady);}
                if(View=="main"&&!app.StartInTray)ReflectionTimer.Desktop.WindowActivation.Focus(this);
                Reply(requestId); return;
            }
            if (action == "flushed") { flush?.TrySetResult(); Reply(requestId); return; }
            if (action == "flushFailed") { flush?.TrySetException(new IOException("Draft save failed.")); Reply(requestId); return; }
            if (action == "compact") { app.Open("compact"); Reply(requestId); return; }
            if(action=="toggleCompact") { app.ToggleCompactVisibility();Reply(requestId);return; }
            if(action=="quit") { if(View!="main")throw new ArgumentException("Quit from the App view.");Reply(requestId);await app.CloseMainAsync();return; }
            if(action=="durationDraft") {
                if(View=="reflection")throw new ArgumentException("Edit duration in the App or Compact view.");
                var parts=data.GetProperty("parts").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
                app.Session.SetDurationDraft(parts);Reply(requestId);return;
            }
            if(action=="dragCompact") {
                if(View!="compact")throw new ArgumentException("Only the compact window can be dragged here.");
                Reply(requestId);ReleaseCapture();SendMessage(Handle,0x00A1,2,0);return;
            }
            if (action == "compactSize") {
                if(View!="compact") throw new ArgumentException("Only the compact window can request this size.");
                var width=ReadInt(data,"width",80,700);var height=ReadInt(data,"height",32,1000);
                IsTimeOnly=ReadFlag(data,"tiny");
                Text="Reflection Timer — "+(IsTimeOnly?"Time-only":"Compact")+" view · 4.0.7";
                ClientSize=new((int)Math.Ceiling(width*DeviceDpi/96d*browser.ZoomFactor),(int)Math.Ceiling(height*DeviceDpi/96d*browser.ZoomFactor));
                ApplyPosition();Reply(requestId);return;
            }
            if (action == "main") { app.Open("main"); Reply(requestId); return; }
            if (action == "close") { Reply(requestId); Close(); return; }
            if (action == "readTime") {
                var clock = app.Session.Clock(); Post(new { type = "timeRead", clock }); Reply(requestId); return;
            }
            if (await HandleSettings(action, data, requestId)) return;
            // A reflection window can edit only its own draft. Main/compact cannot submit drafts.
            if (action is "draft" or "queue" or "skip") {
                if (PromptId is null || data.GetProperty("id").GetGuid() != PromptId) throw new ArgumentException("This window cannot edit that reflection.");
            } else if (View == "reflection") throw new ArgumentException("That action is unavailable in a reflection window.");
            var result = app.Session.Execute(action, data);
            Reply(requestId);
            if (result.Close) { CloseAfterSave(); app.Announce(result.Message); }
            else if (result.OpenReflection is { } prompt) app.Open("reflection", prompt);
            else app.Announce(result.Message);
        }
        catch (Exception error) {
            var message = error is ArgumentException or InvalidOperationException ? error.Message : "The change could not be saved. Review its current values and try again.";
            if (requestId is not null) Post(new { type = "reply", requestId, error = message });
        }
    }
    private void Reply(string requestId) => Post(new { type = "reply", requestId });
    internal void Post(object message)
    {
        if (ready && !IsDisposed && !browser.IsDisposed) browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, PreviewSession.Json));
    }
    internal async Task FlushDraftAsync()
    {
        if (View != "reflection" || !ready || IsDisposed) return;
        flush = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(new { type = "flush" });
        try { await flush.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { flush = null; }
    }
    internal void CloseAfterSave() { allowClose = true; Close(); }
    protected override void Dispose(bool disposing) { if (disposing) browser.Dispose(); base.Dispose(disposing); }
    [DllImport("user32.dll")]private static extern bool ReleaseCapture();
    [DllImport("user32.dll",EntryPoint="SendMessageW")]private static extern nint SendMessage(nint window,int message,nint wParam,nint lParam);
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
}
