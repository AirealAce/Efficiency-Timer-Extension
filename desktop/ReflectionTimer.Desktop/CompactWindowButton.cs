using System.Drawing.Drawing2D;

namespace ReflectionTimer.Desktop;

internal enum CompactWindowAction { Shrink, Expand, Close }

// Small, accessible caption actions; painted vectors stay crisp at every DPI.
internal sealed class CompactWindowButton : Button
{
    internal CompactWindowAction Action { get; }
    internal CompactWindowButton(CompactWindowAction action, string name, EventHandler click)
    {
        Action = action; Name = name; AccessibleRole = AccessibleRole.PushButton;
        Text = ""; UseMnemonic = false; AutoSize = false;
        Size = new(26, 26); Margin = Padding.Empty; Padding = Padding.Empty;
        ApplyPalette(); Click += click;
    }
    internal static int SideForDpi(int dpi, bool tiny) =>
        Math.Max(tiny ? 12 : 22, (int)Math.Round((tiny ? 16 : 26) * dpi / 96f));

    internal void ApplyPalette()
    {
        AppTheme.ApplyButton(this, false);
        if (Action != CompactWindowAction.Close) return;
        // Keep the familiar red close action distinct in every app theme.
        // White retains readable contrast in normal, hovered and pressed states.
        BackColor = Color.FromArgb(0xC4, 0x2B, 0x1C); ForeColor = Color.White;
        FlatAppearance.BorderColor = AppTheme.Palette.IsSystemContrast || AppTheme.Preference == ReflectionTimer.Core.AppColorTheme.HighContrast
            ? Color.White : BackColor;
        FlatAppearance.MouseOverBackColor = Color.FromArgb(0xE8, 0x11, 0x23);
        FlatAppearance.MouseDownBackColor = Color.FromArgb(0xA4, 0x26, 0x2C);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var saved = e.Graphics.Save();
        try {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var cx = ClientSize.Width / 2f; var cy = ClientSize.Height / 2f;
            var radius = Math.Min(ClientSize.Width, ClientSize.Height) * .19f;
            using var pen = new Pen(ForeColor, Math.Max(1.4f, DeviceDpi / 72f));
            if (Action == CompactWindowAction.Close) {
                e.Graphics.DrawLine(pen, cx - radius, cy - radius, cx + radius, cy + radius);
                e.Graphics.DrawLine(pen, cx - radius, cy + radius, cx + radius, cy - radius);
            }
            else {
                e.Graphics.DrawLine(pen, cx - radius, cy, cx + radius, cy);
                if (Action == CompactWindowAction.Expand) e.Graphics.DrawLine(pen, cx, cy - radius, cx, cy + radius);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), ForeColor, BackColor);
        }
        finally { e.Graphics.Restore(saved); }
    }
}
