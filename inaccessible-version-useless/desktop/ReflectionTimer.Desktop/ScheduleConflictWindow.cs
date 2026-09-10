using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public sealed class ScheduleConflictWindow : Form
{
    private readonly TimerApplication app;
    private readonly ScheduledSession session;
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 42, ForeColor = AppTheme.Error };
    private bool resolved;
    public Guid SessionId => session.Id;
    public ScheduleConflictWindow(TimerApplication app, ScheduledSession session)
    {
        this.app = app; this.session = session;
        AppTheme.SetTextColor(status, ThemeTextRole.Error);
        Text = "Reflection Timer — scheduled session ready"; ClientSize = new(590, 345); MinimumSize = new(600, 365);
        Font = new("Segoe UI", 11); Padding = new(18); StartPosition = FormStartPosition.CenterScreen;
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        content.Controls.Add(Widgets.Text($"Your {MainWindow.Clock(session.DurationSeconds)} scheduled session is ready. The current timer continues until you choose.", 535));
        content.Controls.Add(Widgets.Button("End current with a reflection and start scheduled", (_, _) => Choose(ScheduleDecision.StartNow), true));
        content.Controls.Add(Widgets.Button("Wait until the current session ends", (_, _) => Choose(ScheduleDecision.Wait)));
        content.Controls.Add(Widgets.Button("Skip this scheduled session", (_, _) => Choose(ScheduleDecision.Skip)));
        Controls.Add(content); Controls.Add(status); AppTheme.Apply(this);
        FormClosing += (_, e) => {
            if (resolved) return;
            try { app.Engine.ResolveSchedule(session.Id, ScheduleDecision.Wait); resolved = true; }
            catch { e.Cancel = true; status.Text = "Could not save your choice. The current timer is unchanged."; }
        };
    }
    private void Choose(ScheduleDecision decision)
    {
        try { app.Engine.ResolveSchedule(session.Id, decision); resolved = true; Close(); }
        catch { status.Text = "Could not save your choice. The current timer is unchanged."; app.PlayFeedback(false); }
    }
}
