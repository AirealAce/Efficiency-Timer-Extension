namespace ReflectionTimer.Desktop;

// Keep native tab semantics, keyboard navigation, and page lifetime; only paint
// the headers ourselves so selection is visible in every palette and focus state.
public sealed class ThemeTabs : TabControl
{
    private Font? selectedFont;

    public ThemeTabs()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        Appearance = TabAppearance.FlatButtons;
        SizeMode = TabSizeMode.Normal;
        AccessibleName = "App sections";
        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        var old = selectedFont;
        selectedFont = new Font(Font, FontStyle.Bold);
        old?.Dispose();
        // Native widths are measured with the regular font; leave room for the
        // wider bold label as well, especially "Scheduling session times".
        Padding = new Point(LogicalToDeviceUnits(28), LogicalToDeviceUnits(4));
        ItemSize = new Size(0, Math.Max(LogicalToDeviceUnits(42), Font.Height + LogicalToDeviceUnits(22)));
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateMetrics();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateMetrics();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabCount) return;
        var p = AppTheme.Palette;
        var selected = e.Index == SelectedIndex;
        var background = selected ? p.PrimaryButton : p.Raised;
        var foreground = selected ? p.AccentText : p.Text;
        var inset = Math.Max(1, LogicalToDeviceUnits(2));
        var bounds = Rectangle.Inflate(e.Bounds, -inset, -inset);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var gap = new SolidBrush(p.Background);
        e.Graphics.FillRectangle(gap, e.Bounds);
        using var fill = new SolidBrush(background);
        e.Graphics.FillRectangle(fill, bounds);
        // Muted text color also gives inactive outlines ample contrast in Dark.
        using var border = new Pen(selected ? p.PrimaryButton : p.Muted);
        e.Graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        var textBounds = Rectangle.Inflate(bounds, -LogicalToDeviceUnits(6), -LogicalToDeviceUnits(5));
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, selected ? selectedFont ?? Font : Font,
            textBounds, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        // A thick underline and bold label identify selection without color alone.
        if (selected) {
            using var marker = new SolidBrush(foreground);
            var thickness = Math.Max(2, LogicalToDeviceUnits(3));
            e.Graphics.FillRectangle(marker, bounds.X + inset, bounds.Bottom - thickness - inset,
                bounds.Width - 2 * inset, thickness);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -2 * inset, -2 * inset), foreground, background);
        }
        base.OnDrawItem(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { selectedFont?.Dispose(); selectedFont = null; }
    }
}
