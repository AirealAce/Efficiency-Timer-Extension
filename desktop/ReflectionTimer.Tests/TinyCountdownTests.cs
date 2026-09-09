using System.Runtime.InteropServices;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestTinyCountdown()
    {
        TestCompactCycle();
        TestCompactWindowActions();
        Test("compact native window stays out of taskbar and Alt Tab in both layouts", () => {
            WithEndEarlyApp((app, _) => {
                var (main, _, _, _) = TimerShortcutControls(app);
                app.SetFloatingTimer(true); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                void CheckStyle() {
                    var style = ReadWindowStyle(mini.Handle, -20);
                    Is((style & 0x80) != 0); Is((style & 0x40000) == 0);
                    Is(!mini.ShowInTaskbar && mini.TopMost && mini.Visible);
                }
                CheckStyle(); main.Hide(); app.Engine.Start(300, false, 0); Application.DoEvents();
                CheckStyle(); Is(!main.Visible);
                app.Engine.Pause(); Application.DoEvents(); CheckStyle();
                app.SetFloatingTimer(false); app.SetFloatingTimer(true); Application.DoEvents(); CheckStyle();
                Is(!main.Visible);
            });
        });
        foreach (var seconds in new[] { 30, 3661, TimerEngine.MaxDuration }) Test("running compact shows only a smaller readable countdown for " + seconds, () => {
            WithEndEarlyApp((app, _) => {
                app.SetFloatingTimer(true); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var fullSize = mini.Size;
                app.Engine.Start(seconds, false, 0); Application.DoEvents();
                var label = Descendants(mini).OfType<Label>().Single(x => x.Visible);
                Equal("Time remaining", label.AccessibleName); Is(label.Font.SizeInPoints < 30);
                Equal(FormBorderStyle.None, mini.FormBorderStyle);
                Is(mini.Width < fullSize.Width * .7 && mini.Height < fullSize.Height * .4);
                mini.SetHoverControls(false);
                Is(!Descendants(mini).Any(x => x.Visible && x is Button or CheckBox or DurationControl or DurationPartInput));
                using var graphics = label.CreateGraphics();
                var measured = TextRenderer.MeasureText(graphics, label.Text, label.Font, Size.Empty, TextFormatFlags.NoPadding);
                Is(label.Width >= measured.Width && label.Height >= measured.Height);
                var rect = mini.RectangleToClient(label.RectangleToScreen(label.ClientRectangle));
                Is(mini.ClientRectangle.Contains(rect));
                var handle = mini.Handle; var size = mini.Size;
                for (var i = 0; i < 10; i++) mini.Render(app.Engine.Snapshot, app.Engine.Now);
                Equal(size, mini.Size); Equal(handle, mini.Handle);
            });
        });
        Test("tiny context menu pauses and restores the shared editor and right-aligned footer", () => {
            WithEndEarlyApp((app, _) => {
                var (_, _, full, _) = TimerShortcutControls(app);
                app.SetFloatingTimer(true); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single(); var fullSize = mini.Size;
                app.Engine.Start(300, false, 0); Application.DoEvents();
                var toggle = mini.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == "Pause");
                toggle.PerformClick(); Application.DoEvents();
                Is(!app.Engine.Snapshot.Timer.IsRunning && mini.Duration.Visible && mini.Duration.Enabled);
                Equal(fullSize, mini.Size); Equal(FormBorderStyle.None, mini.FormBorderStyle);
                var label = Descendants(mini).OfType<Label>().Single(x => x.AccessibleName == "Time remaining");
                Equal(mini.Duration.Width, label.Width);
                CompactPart(mini.Duration, "Seconds").Text = "10";
                Equal(310, full.Seconds);
                var start = CompactAction(mini, "CompactStartPause");
                var forward = CompactAction(mini, "CompactEndEarly");
                var buttonBounds = mini.RectangleToClient(forward.RectangleToScreen(forward.ClientRectangle));
                Is(Math.Abs(mini.ClientSize.Width - mini.Padding.Right - buttonBounds.Right) <= 1);
                Is(start.Visible); start.PerformClick(); Application.DoEvents();
                Is(app.Engine.Snapshot.Timer.IsRunning); Equal(310, app.Engine.Snapshot.Timer.DurationSeconds);
                Is(!mini.Duration.Visible); Equal(0, app.Engine.Snapshot.Prompts.Count);
            });
        });
        foreach (var repeat in new[] { false, true }) Test("end of tiny session restores controls only when not auto-starting: " + repeat, () => {
            WithEndEarlyApp((app, _) => {
                app.SetFloatingTimer(true); app.Engine.Start(300, repeat, 0); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var size = mini.Size; app.Engine.EndEarly(); Application.DoEvents();
                Equal(repeat, app.Engine.Snapshot.Timer.IsRunning); Equal(!repeat, mini.Duration.Visible);
                if (repeat) Equal(size, mini.Size); else Is(mini.Height > size.Height * 2);
                Equal(1, app.Engine.Snapshot.Prompts.Count);
            });
        });
        Test("automatic compact resizes retain the saved dragged position near screen edges", () => {
            WithEndEarlyApp((app, directory) => {
                app.SetFloatingTimer(true); app.Engine.Start(300, false, 0); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                var area = Screen.FromControl(mini).WorkingArea;
                var point = new Point(area.Right - mini.Width - 8, area.Bottom - mini.Height - 8);
                mini.Location = point; mini.SavePosition(); Application.DoEvents();
                app.Engine.Pause(); Application.DoEvents();
                Is(area.Contains(mini.Bounds));
                var saved = new EncryptedStore(directory).Load();
                Equal(point.X, saved.FloatingTimerLeft); Equal(point.Y, saved.FloatingTimerTop);
                app.Engine.Resume(); Application.DoEvents(); Equal(point, mini.Location);
                saved = new EncryptedStore(directory).Load();
                Equal(point.X, saved.FloatingTimerLeft); Equal(point.Y, saved.FloatingTimerTop);
            });
        });
        Test("tiny placement presets keep their screen anchor through pause and resume", () => {
            WithEndEarlyApp((app, _) => {
                app.SetFloatingTimer(true); app.Engine.Start(300, false, 0); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                foreach (var placement in Enum.GetValues<FloatingTimerPlacement>().Where(x => x != FloatingTimerPlacement.Custom)) {
                    app.Engine.SetFloatingTimerPlacement(placement); Application.DoEvents();
                    void Check() => Equal(FloatingTimerWindow.PresetPosition(Screen.FromControl(mini).WorkingArea, mini.Size, placement), mini.Location);
                    Check(); app.Engine.Pause(); Application.DoEvents(); Check();
                    app.Engine.Resume(); Application.DoEvents(); Check();
                }
            });
        });
        Test("running compact restores in tiny mode from encrypted state and hiding never stops the timer", () => {
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            WithEndEarlyApp((app, _) => {
                var (main, _, _, _) = TimerShortcutControls(app); main.Hide(); Application.DoEvents();
                var mini = Application.OpenForms.OfType<FloatingTimerWindow>().Single();
                Is(mini.Visible && !mini.Duration.Visible); Equal(FormBorderStyle.None, mini.FormBorderStyle);
                var timer = app.Engine.Snapshot.Timer;
                mini.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == "Hide compact timer").PerformClick();
                Application.DoEvents(); Is(!mini.Visible); Equal(timer, app.Engine.Snapshot.Timer);
                app.ToggleCompactTimer(); Application.DoEvents(); Is(mini.Visible && mini.Duration.Visible && !main.Visible);
                Is(mini.Duration.ReadOnly);
                Equal(timer, app.Engine.Snapshot.Timer);
            }, new AppState { ExtensionDisabledConfirmed = true, ShowFloatingTimer = true,
                Timer = new TimerState { DurationSeconds = 300, RemainingSeconds = 300, IsRunning = true, EndTime = now + 300000, Volume = 0 } });
        });
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int ReadWindowStyle(nint window, int index);
}
