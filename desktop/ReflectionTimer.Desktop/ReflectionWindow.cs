using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public sealed class ReflectionWindow : Form
{
    private readonly TimerApplication app;
    private readonly ReflectionPrompt prompt;
    private readonly Point openingPointer = Cursor.Position;
    private readonly ReflectionPopupPosition popupPosition;
    private readonly TextBox response = new() { Multiline = true, MaxLength = 5000, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
        AccessibleName = "Session reflection" };
    private readonly TextBox reason = new() { Multiline = true, MaxLength = 1000, Dock = DockStyle.Bottom, Height = 65,
        ScrollBars = ScrollBars.Vertical, PlaceholderText = "Reason for ending early", AccessibleName = "Reason for ending early" };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 44, ForeColor = Widgets.Muted };
    private readonly System.Windows.Forms.Timer draftDelay = new() { Interval = 500 };
    private bool saving;
    internal Guid PromptId => prompt.Id;
    public ReflectionWindow(TimerApplication app, ReflectionPrompt prompt)
    {
        this.app = app; this.prompt = prompt;
        AppTheme.SetTextColor(status, ThemeTextRole.Muted);
        popupPosition = app.Engine.Snapshot.PopupPosition;
        Text = prompt.IsCheckIn ? "Reflection Timer — check-in" : prompt.IsTest ? "Reflection Timer — test prompt" : prompt.EndedEarly ? "Reflection Timer — ended early" : "Reflection Timer — session complete";
        Size = new(560, prompt.EndedEarly ? 525 : 440); MinimumSize = new(480, prompt.EndedEarly ? 440 : 360); StartPosition = FormStartPosition.Manual;
        Font = new("Segoe UI", 11); Padding = new(20); BackColor = AppTheme.Background;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Information;
        var heading = new ThemeHeader("How did you spend your time?", compact: true);
        var context = new Label { Text = prompt.IsCheckIn ? "Check-in · elapsed active time is recorded when you send.\r\nThe timer is unchanged; older drafts keep their original session time." : prompt.IsTest ? "TEST MODE · saves only to the test tab" :
            $"{(prompt.ActualDurationSeconds is { } actual ? MainWindow.Clock(actual) + " spent / " : "")}{MainWindow.Clock(prompt.DurationSeconds)} allotted{(prompt.EndedEarly ? " · ended early" : "")}",
            Dock = DockStyle.Top, Height = 42, ForeColor = Widgets.Green };
        AppTheme.SetTextColor(context, ThemeTextRole.Accent);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 53, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.Add(Widgets.Button("Save & send", (_, _) => Save(), true));
        actions.Controls.Add(Widgets.Button("Later", (_, _) => { PersistDraft(); app.Log.Record("prompt.later", prompt.Id); Close(); }));
        actions.Controls.Add(Widgets.Button("Skip", (_, _) => {
            try { app.Engine.SkipPrompt(prompt.Id); Close(); } catch { status.Text = "Could not save that change. Your reflection is still available."; app.PlayFeedback(false); }
        }));
        Controls.Add(response);
        if (prompt.EndedEarly) {
            var reasonPanel = new Panel { Dock = DockStyle.Bottom, Height = 80, Padding = new(0, 12, 0, 0) };
            reasonPanel.Controls.Add(reason); Controls.Add(reasonPanel);
        }
        Controls.Add(context); Controls.Add(heading); Controls.Add(status); Controls.Add(actions);
        response.Text = prompt.Draft;
        reason.Text = prompt.EarlyEndReason;
        reason.TextChanged += (_, _) => { draftDelay.Stop(); draftDelay.Start(); };
        response.TextChanged += (_, _) => { status.Text = response.TextLength + " / 5,000 · Ctrl+Enter to save"; draftDelay.Stop(); draftDelay.Start(); };
        draftDelay.Tick += (_, _) => { draftDelay.Stop(); PersistDraft(); };
        response.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Save(); } };
        reason.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Save(); } };
        Shown += (_, _) => { PlaceOnOpeningScreen(); response.Focus(); response.SelectionStart = response.TextLength; };
        FormClosing += (_, e) => { if (!saving && !PersistDraft()) e.Cancel = true; };
        FormClosed += (_, _) => draftDelay.Dispose();
        AppTheme.Apply(this);
        PlaceOnOpeningScreen(); // Pick the target monitor before the native handle and DPI scaling.
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PlaceOnOpeningScreen();
    }
    private void PlaceOnOpeningScreen()
    {
        // Re-evaluate the working area at opening, after scaling; never move a draft while typing.
        var workArea = Screen.FromPoint(openingPointer).WorkingArea;
        Location = ReflectionPlacement.Calculate(workArea, Size, popupPosition, (int)Math.Round(16 * DeviceDpi / 96d));
    }
    public void FocusResponse()
    {
        if (IsDisposed || Disposing) return;
        var start = response.SelectionStart; var length = response.SelectionLength;
        WindowActivation.Focus(this);
        if (!Enabled) return; // Keep any owned modal in front of its disabled owner.
        response.Focus();
        // Return to the draft without selecting all and risking accidental replacement.
        response.Select(start, length);
    }
    public bool PersistDraft()
    {
        try { app.Engine.SaveDraft(prompt.Id, response.Text, reason.Text); return true; }
        catch { status.Text = "Draft could not be saved. Keep this window open and check disk access."; app.PlayFeedback(false); return false; }
    }
    private void Save()
    {
        if (saving) return;
        try {
            app.QueueReflection(prompt.Id, response.Text, reason.Text); saving = true; draftDelay.Stop(); Close(); _ = app.Sync();
        } catch (Exception error) { status.Text = error.Message; AppTheme.SetTextColor(status, ThemeTextRole.Error); app.PlayFeedback(false); response.Focus(); }
    }
}
