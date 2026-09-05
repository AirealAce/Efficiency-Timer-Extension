using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public sealed class ReflectionWindow : Form
{
    private readonly TimerApplication app;
    private readonly ReflectionPrompt prompt;
    private readonly TextBox response = new() { Multiline = true, MaxLength = 5000, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 44, ForeColor = Widgets.Muted };
    private readonly System.Windows.Forms.Timer draftDelay = new() { Interval = 500 };
    private bool saving;
    public ReflectionWindow(TimerApplication app, ReflectionPrompt prompt)
    {
        this.app = app; this.prompt = prompt;
        Text = prompt.IsTest ? "Reflection Timer — test prompt" : "Reflection Timer — session complete";
        Size = new(560, 440); MinimumSize = new(480, 360); StartPosition = FormStartPosition.CenterScreen;
        Font = new("Segoe UI", 11); Padding = new(20); BackColor = DarkTheme.Background;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Information;
        var heading = new Label { Text = "How did you spend your time?", Dock = DockStyle.Top, Height = 45, Font = new("Segoe UI", 19, FontStyle.Bold), ForeColor = Widgets.Ink };
        var context = new Label { Text = prompt.IsTest ? "TEST MODE · saves only to the test tab" : $"{MainWindow.Clock(prompt.DurationSeconds)} session · saved locally before sending", Dock = DockStyle.Top, Height = 42, ForeColor = Widgets.Green };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 53, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.Add(Widgets.Button("Save & send", (_, _) => Save(), true));
        actions.Controls.Add(Widgets.Button("Later", (_, _) => { PersistDraft(); app.Log.Record("prompt.later", prompt.Id); Close(); }));
        actions.Controls.Add(Widgets.Button("Skip", (_, _) => {
            try { app.Engine.SkipPrompt(prompt.Id); Close(); } catch { status.Text = "Could not save that change. Your reflection is still available."; }
        }));
        Controls.Add(response); Controls.Add(context); Controls.Add(heading); Controls.Add(status); Controls.Add(actions);
        response.Text = prompt.Draft;
        response.TextChanged += (_, _) => { status.Text = response.TextLength + " / 5,000 · Ctrl+Enter to save"; draftDelay.Stop(); draftDelay.Start(); };
        draftDelay.Tick += (_, _) => { draftDelay.Stop(); PersistDraft(); };
        response.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Save(); } };
        Shown += (_, _) => { response.Focus(); response.SelectionStart = response.TextLength; };
        FormClosing += (_, e) => { if (!saving && !PersistDraft()) e.Cancel = true; };
        FormClosed += (_, _) => draftDelay.Dispose();
        DarkTheme.Apply(this);
    }
    public bool PersistDraft()
    {
        try { app.Engine.SaveDraft(prompt.Id, response.Text); return true; }
        catch { status.Text = "Draft could not be saved. Keep this window open and check disk access."; return false; }
    }
    private void Save()
    {
        if (saving) return;
        try {
            app.Engine.QueueReflection(prompt.Id, response.Text); saving = true; draftDelay.Stop(); Close(); _ = app.Sync();
        } catch (Exception error) { status.Text = error.Message; status.ForeColor = DarkTheme.Error; response.Focus(); }
    }
}
