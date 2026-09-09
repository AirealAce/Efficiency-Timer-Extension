using System.Diagnostics;
using System.Reflection;
using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public sealed class SetupWindow : Form
{
    private readonly TimerApplication app;
    private readonly Func<ConnectionSettings, CancellationToken, bool, Task<SheetReply>> ping;
    private readonly Func<string, bool> confirm;
    private readonly ThemeTabs steps = new() { Dock = DockStyle.Fill };
    private readonly CheckBox existing = new() { Text = "Use an existing receiver (another PC or the extension)", AutoSize = true };
    private readonly TextBox sheet = new() { Width = 750, AccessibleName = "Your Google Sheets URL" };
    private readonly TextBox token = new() { Width = 750, UseSystemPasswordChar = true, AccessibleName = "Your private setup token" };
    private readonly TextBox endpoint = new() { Width = 750, AccessibleName = "Your deployment URL ending in exec" };
    private readonly TextBox transfer = new() { Width = 750, UseSystemPasswordChar = true, MaxLength = 16000, AccessibleName = "Private setup code from another PC" };
    private readonly CheckBox extensionOff = new() { Text = "The Chrome timer extension is off, or I never installed it", AutoSize = true };
    private readonly ComboBox mode = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Setup destination mode" };
    private readonly TextBox fixedTab = new() { Width = 350, AccessibleName = "Setup fixed tab name" };
    private readonly Label status = Widgets.Text("Your spreadsheet stays in your Google account. The app never asks for your Google password.");
    private readonly Label deployNotice = Widgets.Text("");
    private readonly Button generate, copyScript, check, finish;
    private readonly FlowLayoutPanel bottom;
    private readonly FlowLayoutPanel navigation;
    private readonly CancellationTokenSource closing = new();
    private ConnectionSettings? verified;
    private bool busy;
    private bool resourcesDisposed;
    private bool closed;
    private ConnectionSettings savedDraft = new();
    private bool savedExisting;
    public bool Connected { get; private set; }

    public SetupWindow(TimerApplication app, ConnectionSettings initial, Func<ConnectionSettings, CancellationToken, bool, Task<SheetReply>>? ping = null,
        Func<string, bool>? confirm = null)
    {
        this.app = app;
        this.ping = ping ?? ((connection, cancellation, test) => app.Sheets.Ping(connection, cancellation, test));
        this.confirm = confirm ?? (message => MessageBox.Show(this, message, "Reflection Timer setup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes);
        Text = "Reflection Timer · Guided setup"; Size = new(940, 850); MinimumSize = new(880, 700);
        StartPosition = FormStartPosition.CenterParent; Font = new("Segoe UI", 10);
        Controls.Add(steps);
        var heading = new ThemeHeader("Your timer. Your spreadsheet."); Controls.Add(heading);
        bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        finish = Widgets.Button("Save and finish", (_, _) => Finish(), true); finish.Enabled = false;
        check = Widgets.Button("Check connection", async (_, _) => await CheckConnection());
        bottom.Controls.Add(status);
        navigation = Widgets.Row(Widgets.Button("Back", (_, _) => steps.SelectedIndex = Math.Max(0, steps.SelectedIndex - 1)),
            Widgets.Button("Next", (_, _) => steps.SelectedIndex = Math.Min(2, steps.SelectedIndex + 1)),
            check, finish, Widgets.Button("Close setup", (_, _) => Close()));
        bottom.Controls.Add(navigation);
        Controls.Add(bottom);

        var first = Widgets.Page(steps, "1 · Your sheet");
        first.Controls.Add(Widgets.Text("New here? Create a blank spreadsheet in YOUR Google account, then paste its URL below. You do not need to share it with the app's author, make the Sheet public, enable an API, or create a service-account key."));
        first.Controls.Add(Widgets.Row(Widgets.Button("Open Google Sheets", (_, _) => OpenLink("https://docs.google.com/spreadsheets/")),
            Widgets.Button("Read setup guide", (_, _) => OpenGuide()),
            Widgets.Button("Install on this PC", (_, _) => LocalInstallation.InstallInteractive(this))));
        first.Controls.Add(existing);
        first.Controls.Add(Widgets.Text("Your Google Sheets URL")); first.Controls.Add(sheet);
        first.Controls.Add(Widgets.Text("Private Reflection API token — not a Google password or Google-issued API key")); first.Controls.Add(token);
        generate = Widgets.Button("Generate private token", (_, _) => Safe(GeneratePrivateToken));
        first.Controls.Add(Widgets.Row(generate));
        first.Controls.Add(Widgets.Text("New connection: generate a token here. Existing connection: paste the SAME token from your other PC's Settings or Apps Script → Project Settings → Script properties → REFLECTION_API_TOKEN. Do not generate a replacement for an existing deployment."));
        first.Controls.Add(new SettingsSection("Connecting another PC?"));
        first.Controls.Add(Widgets.Text("On the configured PC, use Guided setup → step 3 → Copy private setup code. Paste it here. The code includes a secret and is NOT encrypted: keep it in a password manager or other private transfer, never a public message, screenshot, or GitHub issue."));
        first.Controls.Add(transfer);
        first.Controls.Add(Widgets.Row(Widgets.Button("Use private setup code", (_, _) => Safe(() => {
            var imported = ConnectionSetup.Import(transfer.Text);
            app.Engine.SaveSetupDraft(imported, true);
            LoadConnection(imported); existing.Checked = true; savedDraft = ReadDraft(); savedExisting = true;
            transfer.Clear(); steps.SelectedIndex = 2;
            Notice("Connection details imported as a draft. Check the displayed destination, then Check connection and Save and finish.");
        }))));

        var deploy = Widgets.Page(steps, "2 · Google setup");
        deploy.Controls.Add(deployNotice);
        copyScript = Widgets.Button("Copy setup script", (_, _) => Safe(() => {
            if (string.IsNullOrWhiteSpace(token.Text)) GeneratePrivateToken();
            var script = ConnectionSetup.BuildScript(ReadConnection(), ReadDraft());
            SaveDraft(); Clipboard.SetText(script);
            Notice("Private setup script copied; draft saved encrypted. Paste it only into your own Apps Script project. It contains your secret token; clipboard history may retain it.");
        }), true);
        deploy.Controls.Add(Widgets.Row(copyScript, Widgets.Button("Read detailed guide", (_, _) => OpenGuide())));
        deploy.Controls.Add(Widgets.Text("For a NEW connection:\n\n1. In the selected spreadsheet, open Extensions → Apps Script. Use a new, dedicated project; do not replace unrelated automation.\n\n2. Click Copy setup script here. Replace the new project's starter Code.gs with the copied script and save it.\n\n3. Select setupReflectionTimer in the function dropdown, then Run. Review and authorize it with YOUR Google account. It binds this one spreadsheet and creates missing Temp and test tabs; existing tabs are left alone.\n\n4. Deploy → New deployment → Select type (gear) → Web app. Execute as: Me (your account). Who has access: Anyone. Authorize if asked, then Deploy.\n\n5. Copy the Web app URL ending in /exec and paste it on step 3. The editor URL and /dev test URL will not work."));
        deploy.Controls.Add(Widgets.Text("Why Anyone? The desktop app has no Google browser-login cookie; every write still requires your private token and must match the bound spreadsheet. Do not share the token or script project. The spreadsheet itself can remain private. Your Workspace administrator may prohibit this deployment type; do not bypass that restriction."));
        deploy.Controls.Add(Widgets.Text("Google may show an unverified-app warning for a personal script. Only proceed if it is the project you just created, the developer account is yours, and you trust the code and requested permissions. Otherwise stop. Read the guide for details; no one should ask for your Google password or verification code."));

        var last = Widgets.Page(steps, "3 · Connect");
        last.Controls.Add(Widgets.Text("Apps Script WEB APP URL (https://script.google.com/macros/s/…/exec)")); last.Controls.Add(endpoint);
        last.Controls.Add(Widgets.Text("Existing receiver? Find it under Apps Script → Deploy → Manage deployments → Web app URL. Reuse its token and spreadsheet. You do not need a new deployment for another PC."));
        mode.Items.AddRange(["Automatic dated tabs (recommended)", "Always use a fixed tab"]);
        last.Controls.Add(mode); last.Controls.Add(fixedTab);
        last.Controls.Add(Widgets.Text("Automatic mode matches the date when you send (MM/DD/YYYY or MM/DD/YY), creating a missing daily tab from the receiver's template. Test reflections always use test. Check connection is read-only: it does not write a sample or create a daily tab."));
        last.Controls.Add(extensionOff);
        last.Controls.Add(Widgets.Text("Click Check connection, confirm the destination shown below, then Save and finish. Both your normal destination and test tab are checked, including support for check-ins and safe retries. Your other timer, sound, theme, scheduling, and display settings are retained."));
        last.Controls.Add(Widgets.Row(Widgets.Button("Copy private setup code", (_, _) => Safe(() => {
            Clipboard.SetText(SavedConnectionCode());
            Notice("Your SAVED connection was copied, not any unfinished setup edits. The code contains the token in a readable format, not encryption. Share only with your own trusted PC; clipboard history may retain it.");
        })), Widgets.Button("Clear copied setup data", (_, _) => Safe(() => {
            if (Clipboard.ContainsText()) {
                var text = Clipboard.GetText();
                if (text.StartsWith(ConnectionSetup.CodePrefix, StringComparison.Ordinal) || text.Contains("function setupReflectionTimer()")) Clipboard.Clear();
            }
            transfer.Clear(); Notice("Current setup clipboard data cleared if present. Windows clipboard history or synced clips must be cleared separately by you.");
        }))));
        last.Controls.Add(Widgets.Text("Copy private setup code exports the saved connection only. Finish and save setup first; unfinished edits are not transferred. Each PC keeps its own timers, schedules, local drafts and settings. Only submitted entries share the Sheet. Windows-encrypted state.dat files are not portable to another Windows account or PC."));

        LoadConnection(initial);
        var state = app.Engine.Snapshot;
        existing.Checked = state.SetupDraft == initial && state.SetupDraftUsesExistingReceiver is { } useExisting ? useExisting : initial.WebAppUrl.Length > 0;
        savedDraft = ReadDraft(); savedExisting = existing.Checked;
        extensionOff.Checked = app.Engine.Snapshot.ExtensionDisabledConfirmed;
        existing.CheckedChanged += (_, _) => UpdateMode();
        foreach (var input in new TextBox[] { sheet, token, endpoint, fixedTab }) input.TextChanged += (_, _) => InvalidateCheck();
        mode.SelectedIndexChanged += (_, _) => { fixedTab.Enabled = mode.SelectedIndex == 1; InvalidateCheck(); };
        extensionOff.CheckedChanged += (_, _) => finish.Enabled = verified is not null && extensionOff.Checked && !busy;
        UpdateMode(); AppTheme.Apply(this);
        Resize += (_, _) => FitContent();
        steps.SelectedIndexChanged += (_, _) => FitContent();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitToWorkingArea(Screen.FromControl(Owner ?? this).WorkingArea);
    }
    internal void FitToWorkingArea(Rectangle area)
    {
        // Laptop screens and high-DPI desktops must keep Save/Close reachable.
        StartPosition = FormStartPosition.Manual;
        var available = new Size(Math.Max(1, area.Width - 24), Math.Max(1, area.Height - 24));
        MinimumSize = new(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        Location = new(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        FitContent();
    }
    private void FitContent()
    {
        if (navigation is null || bottom is null) return;
        var width = Math.Max(1, ClientSize.Width - bottom.Padding.Horizontal - 10);
        status.MaximumSize = new(width, 0); navigation.MaximumSize = new(width, 0);
        foreach (TabPage tab in steps.TabPages) {
            if (tab.Controls[0] is not FlowLayoutPanel page) continue;
            if (page.ClientSize.Width < 100) continue;
            var contentWidth = Math.Max(1, page.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);
            foreach (Control control in page.Controls) {
                if (control is Label or FlowLayoutPanel or CheckBox) control.MaximumSize = new(contentWidth, 0);
                else if (control is InputFrame frame) frame.Width = Math.Min(LogicalToDeviceUnits(frame.Editor is ComboBox ? 420 : 750), contentWidth);
                else if (control is TextBox) control.Width = Math.Min(LogicalToDeviceUnits(750), contentWidth);
                else if (control is ComboBox) control.Width = Math.Min(LogicalToDeviceUnits(420), contentWidth);
            }
        }
    }
    internal static string ReadConnection()
    {
        using var stream = typeof(SetupWindow).Assembly.GetManifestResourceStream("ReflectionTimer.Receiver.gs")
            ?? throw new IOException("The setup receiver is missing. Download the complete app package again.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private ConnectionSettings ReadDraft() => new() { SheetUrl = sheet.Text.Trim(), ApiToken = token.Text.Trim(), WebAppUrl = endpoint.Text.Trim(), SheetMode = mode.SelectedIndex == 1 ? "fixed" : "date", SheetName = fixedTab.Text.Trim() };
    private void LoadConnection(ConnectionSettings value) { sheet.Text = value.SheetUrl; token.Text = value.ApiToken; endpoint.Text = value.WebAppUrl; mode.SelectedIndex = value.SheetMode == "fixed" ? 1 : 0; fixedTab.Text = value.SheetName; fixedTab.Enabled = mode.SelectedIndex == 1; }
    private void SaveDraft()
    {
        var draft = ReadDraft(); app.Engine.SaveSetupDraft(draft, existing.Checked);
        savedDraft = draft; savedExisting = existing.Checked;
    }
    internal void GeneratePrivateToken()
    {
        _ = ConnectionSetup.SpreadsheetId(sheet.Text);
        if (token.TextLength > 0 && !confirm("Replace this setup token?\n\nIf you already ran or deployed the script, a replacement will not match it. Keep the existing token or retrieve REFLECTION_API_TOKEN from Script properties.\n\nGenerate a replacement anyway?")) return;
        var draft = ReadDraft() with { ApiToken = ConnectionSetup.NewToken() };
        // Commit before replacing the displayed secret, so a disk failure does
        // not strand a previously saved setup behind a newly displayed token.
        app.Engine.SaveSetupDraft(draft, existing.Checked);
        token.Text = draft.ApiToken; savedDraft = ReadDraft(); savedExisting = existing.Checked;
        Notice("Private token generated and saved encrypted on this PC. Next: copy the setup script on step 2.");
    }
    internal string SavedConnectionCode()
    {
        var connection = app.Engine.Snapshot.Connection;
        if (SheetsClient.Validate(connection) is not null) throw new InvalidOperationException("Finish and save a connection before copying its private setup code.");
        return ConnectionSetup.Export(connection);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!Connected && (savedDraft != ReadDraft() || savedExisting != existing.Checked)) {
            try { SaveDraft(); }
            catch {
                if (!confirm("The setup draft could not be saved. Your active connection is unchanged.\n\nClose and discard these unsaved setup edits? Choose No to keep them open.")) e.Cancel = true;
            }
        }
        base.OnFormClosing(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        closed = true; if (!resourcesDisposed) closing.Cancel();
        base.OnFormClosed(e);
    }
    private void InvalidateCheck() { verified = null; finish.Enabled = false; }
    private void UpdateMode() {
        generate.Enabled = copyScript.Enabled = !existing.Checked; token.ReadOnly = !existing.Checked;
        deployNotice.Text = existing.Checked ? "Existing connection: skip script creation. Keep the current deployment and credentials. Continue to step 3."
            : "This script runs in YOUR Google account, writes only to YOUR chosen spreadsheet, and sends no data to the app's author. It includes a private setup token: never publish the personalized copy.";
    }
    private async Task CheckConnection()
    {
        if (busy || closed || resourcesDisposed) return;
        InvalidateCheck(); var draft = ReadDraft();
        var error = SheetsClient.Validate(draft);
        if (error is not null) { Notice(error, true); return; }
        busy = true; steps.Enabled = check.Enabled = false;
        try {
            SaveDraft(); Notice("Checking your normal destination…");
            var normal = await ping(draft, closing.Token, false);
            if (closed || resourcesDisposed) return;
            if (!normal.Success) { Notice(normal.DisplayMessage, true); return; }
            if (!normal.SupportsSafeRetry || !normal.SupportsCheckIns) { Notice("The deployed receiver needs updating to retain safe retries and check-ins. Follow the receiver-update section of the guide; keep the existing token and Script Properties.", true); return; }
            var test = await ping(draft, closing.Token, true);
            if (closed || resourcesDisposed) return;
            if (!test.Success) { Notice("Normal destination connected, but the test tab check failed. Create a tab named test in that same spreadsheet. " + test.DisplayMessage, true); return; }
            verified = draft;
            Notice("Connection works · " + normal.Target + "\nTest destination · " + test.Target + "\nIf these are yours, confirm the extension is off and choose Save and finish.");
        } catch { if (!closed && !resourcesDisposed) Notice("Could not finish the check. Your active connection was not changed. Check network and local disk access, then try again.", true); }
        finally { if (!closed && !resourcesDisposed) { busy = false; steps.Enabled = check.Enabled = true; finish.Enabled = verified is not null && extensionOff.Checked; } }
    }
    private void Finish() => Safe(() => {
        if (verified is null || verified != ReadDraft()) throw new InvalidOperationException("Check this connection first.");
        app.Engine.CompleteSetup(verified, extensionOff.Checked); Connected = true; DialogResult = DialogResult.OK; Close();
    });
    private void Notice(string text, bool error = false) { status.Text = text; AppTheme.SetTextColor(status, error ? ThemeTextRole.Error : ThemeTextRole.Accent); }
    private void Safe(Action action) { try { action(); } catch (Exception error) { Notice(error is ArgumentException or InvalidOperationException ? error.Message : "Could not complete that action. Check local disk and clipboard access; your active connection is unchanged.", true); } }
    public static void OpenGuide() => OpenLink(Path.Combine(AppContext.BaseDirectory, "START-HERE.html"));
    private static void OpenLink(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { MessageBox.Show("Could not open the guide/browser. Open START-HERE.html from the extracted app folder.", "Reflection Timer"); } }
    protected override void Dispose(bool disposing) { if (disposing && !resourcesDisposed) { resourcesDisposed = true; closing.Cancel(); closing.Dispose(); } base.Dispose(disposing); }
}
