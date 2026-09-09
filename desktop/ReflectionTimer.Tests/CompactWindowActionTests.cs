using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void RenderWindowActionsPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var theme in Enum.GetValues<AppColorTheme>()) WithEndEarlyApp((app, _) => {
            var (main, tabs, _, _) = TimerShortcutControls(app);
            main.ClientSize = new(930, 760); app.Open(); tabs.SelectedIndex = 3; Application.DoEvents();
            void Capture(Control control, string name) {
                using var bitmap = new Bitmap(control.Width, control.Height);
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
                bitmap.Save(Path.Combine(directory, theme + "-" + name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            Capture(main, "settings");
            tabs.SelectedIndex = 0; Application.DoEvents(); Capture(main, "timer");
            main.Hide(); app.FocusCompactTimer(); Application.DoEvents();
            var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
            Capture(mini, "compact"); mini.ShrinkToTimeOnly(); Application.DoEvents();
            mini.SetHoverControls(false); Capture(mini, "tiny");
            mini.SetHoverControls(true); Capture(mini, "tiny-hover");
        }, new AppState { Theme = theme, ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 } });
    }

    private static CompactWindowButton WindowAction(FloatingTimerWindow mini, string name) =>
        Descendants(mini).OfType<CompactWindowButton>().Single(x => x.Name == name);

    private static void TestCompactWindowActions()
    {
        foreach (var mode in new[] { "idle", "paused", "running" }) Test("caption controls navigate without changing the " + mode + " timer", () => {
            WithEndEarlyApp((app, directory) => {
                var (main, tabs, full, _) = TimerShortcutControls(app);
                if (mode != "idle") app.Engine.Start(300, true, 0, app.Engine.Now + 3600000);
                if (mode == "paused") app.Engine.Pause();
                tabs.SelectedIndex = 3; main.Hide(); app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var timer = app.Engine.Snapshot.Timer; var seconds = full.Seconds;
                var shrink = WindowAction(mini, "CompactWindowShrink");
                var expand = WindowAction(mini, "CompactWindowExpand");
                var close = WindowAction(mini, "CompactWindowClose");
                Is(shrink.Visible && expand.Visible && close.Visible);
                Is(shrink.Right <= expand.Left && expand.Right <= close.Left);
                Equal(shrink.Top, close.Top); Equal(shrink.Size, close.Size);
                expand.PerformClick(); Application.DoEvents();
                Is(main.Visible); Equal(0, tabs.SelectedIndex); Equal(timer, app.Engine.Snapshot.Timer);
                main.Hide(); app.FocusCompactTimer(); Application.DoEvents();
                shrink.PerformClick(); Application.DoEvents();
                Is(mini.Visible && !mini.Duration.Visible); Is(app.Engine.Snapshot.ShowFloatingTimer);
                var tinyBounds = mini.Bounds; var handle = mini.Handle;
                mini.SetHoverControls(true);
                var tinyExpand = WindowAction(mini, "TinyWindowExpand");
                Is(tinyExpand.Visible); Equal(tinyBounds, mini.Bounds); Equal(handle, mini.Handle);
                foreach (var button in Descendants(mini).OfType<CompactWindowButton>().Where(x => x.Visible)) {
                    var rect = mini.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                    Is(mini.ClientRectangle.Contains(rect));
                }
                mini.SetHoverControls(false); Is(!tinyExpand.Visible); Equal(tinyBounds, mini.Bounds);
                mini.SetHoverControls(true); tinyExpand.PerformClick(); Application.DoEvents();
                Is(mini.Duration.Visible); Is(!main.Visible); Equal(mode == "running", mini.Duration.ReadOnly);
                Equal(timer, app.Engine.Snapshot.Timer); Equal(seconds, full.Seconds);
                WindowAction(mini, "CompactWindowShrink").PerformClick(); Application.DoEvents(); mini.SetHoverControls(true);
                WindowAction(mini, "TinyWindowShrink").PerformClick(); Application.DoEvents();
                Is(!mini.Visible); Is(!new EncryptedStore(directory).Load().ShowFloatingTimer);
                Equal(timer, app.Engine.Snapshot.Timer); Equal(0, app.Engine.Snapshot.Prompts.Count);
                app.FocusCompactTimer(); close.PerformClick(); Application.DoEvents();
                Is(!mini.Visible && !app.Engine.Snapshot.ShowFloatingTimer); Equal(timer, app.Engine.Snapshot.Timer);
                app.FocusCompactTimer(); mini.ShrinkToTimeOnly(); mini.SetHoverControls(true);
                WindowAction(mini, "TinyWindowClose").PerformClick(); Application.DoEvents();
                Is(!mini.Visible && !app.Engine.Snapshot.ShowFloatingTimer); Equal(timer, app.Engine.Snapshot.Timer);
            });
        });
        Test("caption actions keep their small size and readable theme colors", () => {
            WithEndEarlyApp((app, _) => {
                app.FocusCompactTimer(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var button = WindowAction(mini, "CompactWindowExpand"); var size = button.Size;
                var original = AppTheme.Preference;
                try {
                    foreach (var theme in Enum.GetValues<AppColorTheme>()) {
                        AppTheme.Change(theme); Application.DoEvents();
                        Equal(size, button.Size); Equal(AppTheme.Text, button.ForeColor); Equal(AppTheme.Raised, button.BackColor);
                        Equal("Open main timer page", button.AccessibleName);
                    }
                }
                finally { AppTheme.Change(original); }
            });
        });
    }
}
