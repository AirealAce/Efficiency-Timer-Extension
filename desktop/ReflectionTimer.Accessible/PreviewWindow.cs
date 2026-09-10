using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ReflectionTimer.Accessible;

internal sealed class PreviewWindow : Form
{
    internal const string Origin = "https://reflection-timer.invalid";
    internal string View { get; }
    internal Guid? PromptId { get; }
    private readonly PreviewApplication app;
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill, AccessibleName = "Reflection Timer accessibility preview" };
    private bool ready, allowClose, requestingClose;
    private TaskCompletionSource? flush;
    internal PreviewWindow(PreviewApplication app, string view, Guid? prompt)
    {
        this.app = app; View = view; PromptId = prompt;
        Text = view == "main" ? "Reflection Timer — accessibility preview" : view == "compact" ? "Reflection Timer — compact preview" : "Reflection Timer — reflection preview";
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
        Size = view == "main" ? new(980, 820) : view == "compact" ? new(450, 520) : new(650, 680);
        MinimumSize = view == "main" ? new(420, 400) : new(360, 280);
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = view == "main" ? new(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + 35)
            : new(view == "compact" ? area.Left + 16 : Math.Max(area.Left, area.Right - Width - 16), Math.Max(area.Top, area.Bottom - Height - 16));
        TopMost = view == "compact";
        Controls.Add(browser);
        Shown += async (_, _) => await InitializeAsync();
        FormClosing += async (_, e) => {
            if (allowClose) return;
            e.Cancel = true;
            if (View == "main") { await app.CloseMainAsync(); return; }
            if (requestingClose) return;
            requestingClose = true;
            try { await FlushDraftAsync(); CloseAfterSave(); }
            catch { Post(new { type = "announcement", message = "Draft could not be saved. This window is staying open. Try again." }); }
            finally { requestingClose = false; }
        };
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
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => {
                if (!Allowed(e.Request.Uri)) e.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain");
            };
            core.WebMessageReceived += Receive;
            core.NavigationCompleted += (_, e) => { if (!e.IsSuccess) ShowFailure("The local interface could not be loaded. Close and reopen the preview."); };
            core.ProcessFailed += (_, _) => { ready = false; flush?.TrySetException(new IOException("Web view unavailable.")); ShowFailure("The web interface stopped responding. Close and reopen the preview. Previously saved drafts are retained."); };
            core.Navigate(Origin + "/index.html?view=" + View);
        }
        catch (WebView2RuntimeNotFoundException) { ShowFailure("Microsoft Edge WebView2 Runtime is required. Install the Evergreen Runtime from https://developer.microsoft.com/microsoft-edge/webview2/ and reopen the preview."); }
        catch { ShowFailure("The local web interface could not start. Close and reopen the preview. Your saved preview data is retained."); }
    }
    internal static bool Allowed(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "reflection-timer.invalid"
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath is "/index.html" or "/app.js" or "/app.css" or "/ui.js";
    private void ShowFailure(string text)
    {
        if (IsDisposed) return;
        browser.Visible = false;
        var explanation = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = text, AccessibleName = "Preview startup error", Font = new("Segoe UI", 12) };
        Controls.Add(explanation); explanation.BringToFront(); explanation.Focus();
    }
    private void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
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
                ready = true; Post(new { type = "init", view = View, promptId = PromptId, state = app.Session.View() }); Reply(requestId); return;
            }
            if (action == "flushed") { flush?.TrySetResult(); Reply(requestId); return; }
            if (action == "flushFailed") { flush?.TrySetException(new IOException("Draft save failed.")); Reply(requestId); return; }
            if (action == "compact") { app.Open("compact"); Reply(requestId); return; }
            if (action == "main") { app.Open("main"); Reply(requestId); return; }
            if (action == "close") { Reply(requestId); Close(); return; }
            if (action == "readTime") {
                var clock = app.Session.Clock(); Post(new { type = "timeRead", clock }); Reply(requestId); return;
            }
            // A reflection window can edit only its own draft. Main/compact cannot submit drafts.
            if (action is "draft" or "queue") {
                if (PromptId is null || data.GetProperty("id").GetGuid() != PromptId) throw new ArgumentException("This window cannot edit that reflection.");
            } else if (View == "reflection") throw new ArgumentException("That action is unavailable in a reflection window.");
            var result = app.Session.Execute(action, data);
            Reply(requestId);
            if (result.Close) { CloseAfterSave(); app.Announce(result.Message); }
            else if (result.OpenReflection is { } prompt) app.Open("reflection", prompt);
            else app.Announce(result.Message);
        }
        catch (Exception error) {
            var message = error is ArgumentException ? error.Message : "The change could not be saved. Your last saved state is retained. Try again.";
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
}
