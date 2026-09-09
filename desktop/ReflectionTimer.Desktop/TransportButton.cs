using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ReflectionTimer.Desktop;

internal enum TransportIcon { Back, Play, Pause, Forward }

// Native button behavior/accessibility with vector icons painted directly onto
// the themed button surface: no font glyph dependency or separate image background.
internal sealed class TransportButton : Button
{
    private TransportIcon icon;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal TransportIcon Icon {
        get => icon;
        set { if (icon != value) { icon = value; Invalidate(); } }
    }
    internal TransportButton(TransportIcon icon, string name, string label, EventHandler action)
    {
        Icon = icon; Name = name; AccessibleName = label; AccessibleRole = AccessibleRole.PushButton;
        Text = ""; UseMnemonic = false; AutoSize = false;
        Size = MinimumSize = new(38, 38); Padding = Padding.Empty; Margin = new(0, 4, 6, 4);
        AppTheme.ApplyButton(this, primary: true);
        Click += action;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var saved = e.Graphics.Save();
        try {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var size = Math.Min(ClientSize.Width, ClientSize.Height) * .44f;
            var cx = ClientSize.Width / 2f; var cy = ClientSize.Height / 2f;
            var color = Enabled ? ForeColor : Color.FromArgb(100, ForeColor);
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, Math.Max(1.5f, size / 8)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (Icon == TransportIcon.Play) {
                e.Graphics.FillPolygon(brush, new PointF[] { new(cx - size * .38f, cy - size / 2), new(cx + size * .5f, cy), new(cx - size * .38f, cy + size / 2) });
            }
            else if (Icon == TransportIcon.Pause) {
                e.Graphics.FillRectangle(brush, cx - size * .4f, cy - size / 2, size * .25f, size);
                e.Graphics.FillRectangle(brush, cx + size * .15f, cy - size / 2, size * .25f, size);
            }
            else {
                var direction = Icon == TransportIcon.Back ? -1 : 1;
                e.Graphics.DrawLine(pen, cx - size / 2, cy, cx + size / 2, cy);
                e.Graphics.DrawLines(pen, new PointF[] { new(cx, cy - size / 2), new(cx + direction * size / 2, cy), new(cx, cy + size / 2) });
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, BackColor);
        }
        finally { e.Graphics.Restore(saved); }
    }
}
