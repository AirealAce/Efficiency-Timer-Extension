using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestFloatingTimer()
    {
        Test("floating view preference and position survive restart", () => {
            var f = new Fixture(); Is(!f.Engine.Snapshot.ShowFloatingTimer);
            f.Engine.SetFloatingTimer(true); f.Engine.SetFloatingTimerPosition(-250, 90);
            var state = f.Restart().Snapshot; Is(state.ShowFloatingTimer); Equal(-250, state.FloatingTimerLeft!.Value); Equal(90, state.FloatingTimerTop!.Value);
        });
        Test("floating timer clamps to work area including disconnected monitors", () => {
            Equal(new Point(0, 0), FloatingTimerWindow.FitToScreen(new(0, 0, 800, 600), new(330, 180), new(-900, -20)));
            Equal(new Point(470, 420), FloatingTimerWindow.FitToScreen(new(0, 0, 800, 600), new(330, 180), new(9999, 9999)));
            Equal(new Point(-1920, 0), FloatingTimerWindow.FitToScreen(new(-1920, 0, 1920, 1080), new(330, 180), new(-1920, 0)));
        });
        Test("floating timer shares engine, pauses, resumes and hides without quitting", () => {
            WithEndEarlyApp((app, _) => {
                app.SetFloatingTimer(true); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single(); Is(mini.TopMost && !mini.ShowInTaskbar);
                app.Engine.Start(600, false, 0); Application.DoEvents();
                var toggle = mini.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == "Pause"); toggle.PerformClick(); Application.DoEvents(); Is(!app.Engine.Snapshot.Timer.IsRunning);
                toggle.PerformClick(); Application.DoEvents(); Is(app.Engine.Snapshot.Timer.IsRunning);
                mini.Close(); Application.DoEvents(); Is(!mini.Visible && !mini.IsDisposed); Is(!app.Engine.Snapshot.ShowFloatingTimer); Is(app.Engine.Snapshot.Timer.IsRunning);
                app.SetFloatingTimer(true); Application.DoEvents(); Equal(1, Application.OpenForms.OfType<FloatingTimerWindow>().Count());
            });
        });
    }
}
