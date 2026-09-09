using ReflectionTimer.Core;
using System.Runtime.InteropServices;

namespace ReflectionTimer.Desktop;

public sealed class FloatingTimerWindow : Form
{
    private readonly TimerApplication app;
    private readonly Font editorFont = new("Consolas", 30, FontStyle.Bold);
    private readonly Font runningFont = new("Consolas", 18, FontStyle.Bold);
    private readonly Label countdown = new() { Width = 336, Height = 64, TextAlign = ContentAlignment.MiddleCenter,
        AccessibleName = "Time remaining", Margin = Padding.Empty };
    internal DurationControl Duration { get; } = new(true);
    private readonly TransportButton back, pause, forward;
    private readonly ToolTip actionTips = new();
    private readonly TableLayoutPanel actions;
    private readonly FlowLayoutPanel content;
    private readonly Panel caption = new() { Width = 336, Height = 26, Margin = new(0, 0, 0, 6) };
    private readonly Label captionTitle = new() { Text = "Reflection Timer", AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Compact timer title" };
    private readonly Panel hoverActions = new() { Name = "TinyWindowActions", Visible = false, Size = new(48, 16), Margin = Padding.Empty };
    private readonly CompactWindowButton[] captionButtons, hoverButtons;
    private readonly System.Windows.Forms.Timer hoverCheck = new() { Interval = 100 };
    private readonly ContextMenuStrip quickActions = new();
    private readonly ToolStripMenuItem pauseMenu;
    private readonly CheckBox autoStart = new() { Text = "Auto-start", AutoSize = true,
        AccessibleName = "Auto-start next session", Anchor = AnchorStyles.Left, Margin = new(0, 0, 8, 0) };
    private readonly System.Windows.Forms.Timer positionSave = new() { Interval = 250 };
    private bool placed, placing, moving, positionDirty, binding;
    private bool controlsRevealed;
    private bool timeOnlyRequested, lastRunning;
    private long? revealedDeadline;
    private bool? countdownOnly;
    private int layoutDpi, layoutDigits;
    private (int? Left, int? Top, FloatingTimerPlacement Placement)? applied;
    protected override bool ShowWithoutActivation => true;
    // ShowInTaskbar alone does not express the Alt+Tab contract. Keep the
    // native tool-window style even when the running view has no title bar.
    protected override CreateParams CreateParams
    {
        get {
            var value = base.CreateParams;
            value.ExStyle = (value.ExStyle | 0x00000080) & ~0x00040000; // TOOLWINDOW, not APPWINDOW
            return value;
        }
    }

    public FloatingTimerWindow(TimerApplication app)
    {
        this.app = app;
        Text = "Reflection Timer · compact"; TopMost = true; ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
        Font = new("Segoe UI", 10); Padding = new(10); countdown.Font = editorFont;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        captionButtons = CreateWindowActions("Compact", tiny: false);
        hoverButtons = CreateWindowActions("Tiny", tiny: true);
        caption.Controls.Add(captionTitle); caption.Controls.AddRange(captionButtons);
        hoverActions.Controls.AddRange(hoverButtons);
        back = new(TransportIcon.Back, "CompactReset", "Reset timer", (_, _) => app.ResetTimer());
        pause = new(TransportIcon.Play, "CompactStartPause", "Start timer", (_, _) => app.ToggleTimerPause());
        forward = new(TransportIcon.Forward, "CompactEndEarly", "End timer early", (_, _) => app.EndTimerEarly());
        var open = Widgets.Button("App", (_, _) => app.Open());
        open.MinimumSize = new(58, 38);
        open.Margin = new(0, 4, 8, 4); forward.Margin = new(0, 4, 0, 4);
        back.AccessibleDescription = "Back: reset the timer to its configured duration, just like Reset in the full app.";
        forward.AccessibleDescription = "Forward: end the running session early and open its reflection. Auto-start still applies.";
        actionTips.SetToolTip(back, "Reset timer"); actionTips.SetToolTip(forward, "End timer early");
        actions = new TableLayoutPanel { MinimumSize = new(336, 0), AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) actions.ColumnStyles.Add(new(SizeType.AutoSize));
        actions.Controls.Add(autoStart, 0, 0); actions.Controls.Add(open, 1, 0);
        actions.Controls.Add(back, 2, 0); actions.Controls.Add(pause, 3, 0); actions.Controls.Add(forward, 4, 0);
        content = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, Location = new(10, 10), Margin = Padding.Empty };
        content.Controls.Add(caption); content.Controls.Add(countdown); content.Controls.Add(Duration); content.Controls.Add(actions);
        Controls.Add(content); Controls.Add(hoverActions); AppTheme.Apply(this);
        pauseMenu = new ToolStripMenuItem("Start", null, (_, _) => app.ToggleTimerPause());
        quickActions.Items.Add(pauseMenu);
        quickActions.Items.Add("App", null, (_, _) => app.Open());
        quickActions.Items.Add("Expand compact controls", null, (_, _) => app.FocusCompactTimer());
        quickActions.Items.Add("Hide compact timer", null, (_, _) => app.SetFloatingTimer(false));
        AppTheme.ApplyMenu(quickActions);
        ContextMenuStrip = countdown.ContextMenuStrip = content.ContextMenuStrip = quickActions;
        countdown.AccessibleDescription = "Drag to move. Right-click for Pause, App, or Hide compact timer.";
        countdown.MouseDown += DragCountdown; content.MouseDown += DragCountdown; MouseDown += DragCountdown;
        caption.MouseDown += DragCaption; captionTitle.MouseDown += DragCaption;
        countdown.MouseEnter += (_, _) => SetHoverControls(true);
        content.MouseEnter += (_, _) => SetHoverControls(true);
        MouseEnter += (_, _) => SetHoverControls(true);
        hoverCheck.Tick += (_, _) => SetHoverControls(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        autoStart.CheckedChanged += (_, _) => {
            if (binding) return;
            try {
                var timer = app.Engine.Snapshot.Timer;
                // Match the full timer: turning repeat off also clears its cutoff.
                // Only preferences change; a live session and duration drafts stay intact.
                app.Engine.SetPreferences(autoStart.Checked, timer.Volume, autoStart.Checked ? timer.AutoRestartUntil : null);
            } catch { app.ShowError("Could not save auto-start. Your last saved timer preference is unchanged."); }
            finally { Render(app.Engine.Snapshot, app.Engine.Now); }
        };
        Duration.SubmitRequested += () => { if (!app.Engine.Snapshot.Timer.IsRunning) app.ToggleTimerPause(); };
        positionSave.Tick += (_, _) => { positionSave.Stop(); if (!moving) SavePosition(); };
        ResizeBegin += (_, _) => moving = true;
        ResizeEnd += (_, _) => { moving = false; SavePosition(); };
        LocationChanged += (_, _) => {
            if (!placed || placing) return;
            positionDirty = true; positionSave.Stop(); positionSave.Start();
        };
        FormClosing += (_, e) => {
            SavePosition();
            if (e.CloseReason == CloseReason.UserClosing) {
                // X hides only this optional view. The engine keeps running.
                e.Cancel = true; app.SetFloatingTimer(false);
            }
        };
    }
    private CompactWindowButton[] CreateWindowActions(string prefix, bool tiny)
    {
        var shrink = new CompactWindowButton(CompactWindowAction.Shrink, prefix + "WindowShrink", (_, _) => {
            if (tiny) app.SetFloatingTimer(false); else ShrinkToTimeOnly();
        });
        var expand = new CompactWindowButton(CompactWindowAction.Expand, prefix + "WindowExpand", (_, _) => {
            if (tiny) app.FocusCompactTimer(); else app.OpenTimerPage();
        });
        var close = new CompactWindowButton(CompactWindowAction.Close, prefix + "WindowClose", (_, _) => app.SetFloatingTimer(false));
        shrink.AccessibleName = tiny ? "Hide compact timer" : "Shrink to time-only view";
        expand.AccessibleName = tiny ? "Expand compact view" : "Open main timer page";
        close.AccessibleName = "Close compact view";
        foreach (var button in new[] { shrink, expand, close }) {
            button.AccessibleDescription = "Changes only the window; the timer keeps its current state.";
            actionTips.SetToolTip(button, button.AccessibleName);
        }
        return [shrink, expand, close];
    }
    internal void SetHoverControls(bool hovering)
    {
        if (IsDisposed || Disposing) return;
        var visible = countdownOnly == true && Visible && (hovering || hoverActions.ContainsFocus);
        if (hoverActions.Visible != visible) hoverActions.Visible = visible;
        if (visible) hoverActions.BringToFront();
    }
    private void LayoutWindowActions()
    {
        var side = CompactWindowButton.SideForDpi(DeviceDpi, tiny: false);
        var tinySide = CompactWindowButton.SideForDpi(DeviceDpi, tiny: true);
        caption.Size = new(Duration.Width, side);
        captionTitle.Bounds = new(0, 0, Math.Max(0, caption.Width - side * 3), side);
        for (var i = 0; i < 3; i++) {
            captionButtons[i].Bounds = new(caption.Width - side * (3 - i), 0, side, side);
            hoverButtons[i].Bounds = new(tinySide * i, 0, tinySide, tinySide);
        }
        var timerBounds = RectangleToClient(countdown.RectangleToScreen(countdown.ClientRectangle));
        hoverActions.Bounds = new(Math.Max(0, timerBounds.Right - tinySide * 3), timerBounds.Top + (timerBounds.Height - tinySide) / 2, tinySide * 3, tinySide);
        hoverActions.BringToFront();
    }
    private void DragCaption(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var point = Cursor.Position;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, 2, (point.Y << 16) | (point.X & 0xffff));
    }
    private void DragCountdown(object? sender, MouseEventArgs e)
    {
        if (countdownOnly != true || e.Button != MouseButtons.Left) return;
        DragCaption(sender, e);
    }
    private void ApplyLayout(TimerState timer)
    {
        var tiny = timeOnlyRequested || (timer.IsRunning && !controlsRevealed);
        var digits = tiny ? (timer.IsRunning ? MainWindow.Clock(timer.DurationSeconds).Length : countdown.Text.Length) : 0;
        if (moving || (countdownOnly == tiny && layoutDpi == DeviceDpi && layoutDigits == digits)) return;
        var origin = Location;
        var wasPlacing = placing; placing = true;
        SuspendLayout(); content.SuspendLayout();
        try {
            countdownOnly = tiny; layoutDpi = DeviceDpi; layoutDigits = digits;
            int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            int EditorScale(int value) => (int)Math.Round(value * Duration.Width / 336f);
            Duration.Visible = actions.Visible = caption.Visible = !tiny;
            hoverActions.Visible = false;
            Padding = tiny ? new(Scale(8), Scale(4), Scale(8), Scale(4)) : new(EditorScale(10));
            content.Location = new(Padding.Left, Padding.Top);
            countdown.Font = tiny ? runningFont : editorFont;
            if (tiny) {
                // Reserve all digits for this session, so crossing an hour does
                // not move the widget. Measure on its actual DPI to avoid clipping.
                using var graphics = countdown.CreateGraphics();
                var size = TextRenderer.MeasureText(graphics, new string('8', digits), countdown.Font, Size.Empty, TextFormatFlags.NoPadding);
                countdown.Size = new(Math.Max(Scale(80), size.Width + Scale(4)), Math.Max(Scale(32), size.Height + Scale(2)));
            }
            // The duration/footer controls are already scaled by WinForms.
            // Reuse their width rather than applying the monitor DPI twice.
            else {
                countdown.Size = new(Duration.Width, EditorScale(64));
                actions.MinimumSize = new(Duration.Width, 0);
            }
            countdown.Cursor = tiny ? Cursors.SizeAll : Cursors.Default;
        }
        finally {
            content.ResumeLayout(true); ResumeLayout(true); PerformLayout();
            LayoutWindowActions();
            Location = origin; placing = wasPlacing;
        }
        applied = null; // Reapply the saved anchor with the new size; never save an automatic resize as a drag.
    }
    internal void SavePosition()
    {
        if (!placed || placing || !positionDirty) return;
        positionSave.Stop();
        try {
            app.Engine.SetFloatingTimerPosition(Left, Top);
            applied = (Left, Top, FloatingTimerPlacement.Custom); positionDirty = false;
        } catch { app.ShowError("Could not save the compact timer position. Try moving it again."); }
    }
    internal static Point FitToScreen(Rectangle area, Size size, Point desired) => new(
        Math.Clamp(desired.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(desired.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
    internal static Point PresetPosition(Rectangle area, Size size, FloatingTimerPlacement placement)
    {
        var x = placement is FloatingTimerPlacement.TopLeft or FloatingTimerPlacement.BottomLeft ? area.Left + 16
            : placement is FloatingTimerPlacement.TopRight or FloatingTimerPlacement.BottomRight ? area.Right - size.Width - 16
            : area.Left + (area.Width - size.Width) / 2;
        var y = placement is FloatingTimerPlacement.TopLeft or FloatingTimerPlacement.TopRight or FloatingTimerPlacement.TopCenter ? area.Top + 16
            : placement is FloatingTimerPlacement.BottomLeft or FloatingTimerPlacement.BottomRight or FloatingTimerPlacement.BottomCenter ? area.Bottom - size.Height - 16
            : area.Top + (area.Height - size.Height) / 2;
        return FitToScreen(area, size, new(x, y));
    }
    public void FocusDuration(bool revealRunningControls = false)
    {
        if (revealRunningControls) {
            timeOnlyRequested = false;
            controlsRevealed = app.Engine.Snapshot.Timer.IsRunning;
            revealedDeadline = app.Engine.Snapshot.Timer.EndTime;
            Render(app.Engine.Snapshot, app.Engine.Now);
        }
        WindowActivation.Focus(this);
        if (Enabled) Duration.FocusFirstPositivePart();
    }
    internal bool ShrinkToTimeOnly()
    {
        if (IsDisposed || Disposing || countdownOnly == true) return false;
        SavePosition();
        controlsRevealed = false; timeOnlyRequested = true;
        Render(app.Engine.Snapshot, app.Engine.Now);
        return true;
    }
    private void CollapseControls()
    {
        if (!controlsRevealed || IsDisposed || Disposing) return;
        controlsRevealed = false;
        Render(app.Engine.Snapshot, app.Engine.Now);
    }
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // Recheck after activation/layout events settle; don't collapse a newly
        // focused editor while its native border is being restored.
        if (controlsRevealed && IsHandleCreated && !Disposing)
            BeginInvoke(() => { if (!IsDisposed && !Disposing && !ContainsFocus && !quickActions.Visible) CollapseControls(); });
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && controlsRevealed) { CollapseControls(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    public void Render(AppState state, long now)
    {
        var timer = state.Timer;
        // A manual shrink survives ordinary refreshes. Starting, pausing, or
        // finishing resumes the normal automatic running/editor presentation.
        if (!state.ShowFloatingTimer || timer.IsRunning != lastRunning) timeOnlyRequested = false;
        lastRunning = timer.IsRunning;
        if (!state.ShowFloatingTimer || !timer.IsRunning || timer.EndTime != revealedDeadline) controlsRevealed = false;
        var shown = app.DisplaySeconds(timer, now);
        var valid = timer.IsRunning || !Duration.Dirty || Duration.TryGetSeconds(out shown, out _);
        countdown.Text = valid ? MainWindow.Clock(shown) : "—";
        AppTheme.SetTextColor(countdown, valid ? ThemeTextRole.Text : ThemeTextRole.Error);
        Duration.ReadOnly = timer.IsRunning;
        Duration.Enabled = !timer.IsRunning || controlsRevealed;
        Duration.AccessibleDescription = timer.IsRunning ? "Pause the timer to edit its duration." : "Duration shared with the full timer.";
        var action = timer.IsRunning ? "Pause" : !Duration.Dirty && TimerEngine.IsPaused(timer) ? "Resume" : "Start";
        pause.Icon = timer.IsRunning ? TransportIcon.Pause : TransportIcon.Play;
        pause.AccessibleName = action + " timer";
        pause.AccessibleDescription = timer.IsRunning ? "Pause the current session without ending it." : "Start or resume using the shared duration and timer options.";
        actionTips.SetToolTip(pause, action + " timer");
        pause.Enabled = state.ExtensionDisabledConfirmed && (timer.IsRunning || app.CanStartTimer);
        var validDuration = !Duration.Dirty || (Duration.TryGetSeconds(out var total, out _) && total > 0);
        back.Enabled = state.ExtensionDisabledConfirmed && validDuration &&
            (timer.IsRunning || TimerEngine.IsPaused(timer) || timer.RemainingSeconds != timer.DurationSeconds || Duration.Dirty || timer.LowTimePlayed);
        forward.Enabled = state.ExtensionDisabledConfirmed && timer.IsRunning;
        pauseMenu.Text = action; pauseMenu.Enabled = pause.Enabled;
        binding = true;
        try { autoStart.Checked = timer.AutoRestart; } finally { binding = false; }
        autoStart.Enabled = state.ExtensionDisabledConfirmed;
        ApplyLayout(timer);
        if (!state.ShowFloatingTimer) { hoverCheck.Stop(); hoverActions.Visible = false; if (Visible) Hide(); return; }
        var desired = (state.FloatingTimerLeft, state.FloatingTimerTop, state.FloatingPlacement);
        if ((!placed || applied != desired) && !moving && !positionDirty) {
            PerformLayout();
            var point = state.FloatingTimerLeft is { } left && state.FloatingTimerTop is { } top ? new Point(left, top) : placed ? Location : Cursor.Position;
            var area = Screen.FromPoint(point).WorkingArea;
            point = state.FloatingPlacement != FloatingTimerPlacement.Custom ? PresetPosition(area, Size, state.FloatingPlacement)
                : state.FloatingTimerLeft is null ? PresetPosition(area, Size, FloatingTimerPlacement.BottomLeft) : FitToScreen(area, Size, point);
            placing = true;
            try { Location = point; } finally { placing = false; }
            placed = true; applied = desired;
        }
        // Re-clamp after screen/DPI/layout changes, but never fight a live drag.
        if (placed && !moving && !positionDirty) {
            var area = Screen.FromPoint(Location).WorkingArea;
            var fitted = state.FloatingPlacement == FloatingTimerPlacement.Custom ? FitToScreen(area, Size, Location)
                : PresetPosition(area, Size, state.FloatingPlacement);
            placing = true;
            try { Location = fitted; } finally { placing = false; }
        }
        if (!Visible) Show();
        if (countdownOnly == true) hoverCheck.Start(); else hoverCheck.Stop();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { SavePosition(); positionSave.Dispose(); hoverCheck.Dispose(); quickActions.Dispose(); actionTips.Dispose(); }
        base.Dispose(disposing);
        if (disposing) { editorFont.Dispose(); runningFont.Dispose(); }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
