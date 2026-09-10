using System.Drawing.Drawing2D;
using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public sealed class ThemeHeader : Label
{
    private readonly bool compact;
    private Font? headingFont;
    private bool? decorated;
    public ThemeHeader(string title, bool compact = false)
    {
        this.compact = compact;
        Text = title; Dock = DockStyle.Top;
        RefreshPalette();
    }
    internal void RefreshPalette()
    {
        var glamour = AppTheme.Palette.IsGlamour;
        if (decorated != glamour) {
            decorated = glamour;
            Height = LogicalToDeviceUnits(compact ? (glamour ? 62 : 45) : (glamour ? 90 : 72));
            var old = headingFont;
            headingFont = new(glamour ? "Georgia" : "Segoe UI", compact ? 19 : 26, glamour ? FontStyle.Italic : FontStyle.Bold);
            Font = headingFont; old?.Dispose();
            Padding = compact ? new(0, LogicalToDeviceUnits(3), 0, LogicalToDeviceUnits(10))
                : new(LogicalToDeviceUnits(20), LogicalToDeviceUnits(12), 0, LogicalToDeviceUnits(12));
        }
        ForeColor = compact ? AppTheme.Text : AppTheme.Accent;
        BackColor = glamour ? AppTheme.Raised : AppTheme.Background;
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (AppTheme.Palette.IsGlamour) {
            using var line = new Pen(AppTheme.Palette.Decoration);
            e.Graphics.DrawLine(line, 0, Height - 3, Width, Height - 3);
            var bowSize = Math.Min(Height - 18, 54 * DeviceDpi / 96);
            if (TextRenderer.MeasureText(Text, Font).Width + Padding.Left + bowSize + 36 < Width)
                GlamourOrnament.Draw(e.Graphics, new(Width - bowSize - 24, (Height - bowSize) / 2, bowSize, bowSize), AppTheme.Palette);
        }
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) headingFont?.Dispose(); }
}

// A passive sample, never a second editor or a button that can submit a reflection.
public sealed class ThemePreview : UserControl
{
    private readonly Label title = new() { AutoSize = false, Location = new(18, 12), Size = new(650, 30) };
    private readonly Label description = new() { AutoSize = false, Location = new(18, 47), Size = new(670, 25) };
    private readonly Label sample = new() { Text = "Your moment to reflect…", Location = new(18, 86), Size = new(450, 38), Padding = new(10, 9, 0, 0), BorderStyle = BorderStyle.FixedSingle };
    private readonly Label action = new() { Text = "Save & send", UseMnemonic = false, Location = new(485, 86), Size = new(145, 38), TextAlign = ContentAlignment.MiddleCenter };
    private ThemePalette palette = AppTheme.Palette;
    private AppColorTheme? shown;
    private Font? titleFont;
    public ThemePreview()
    {
        Size = new(750, 142); Margin = new(0, 6, 0, 10); TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Controls.AddRange([title, description, sample, action]);
        foreach (Control child in Controls) child.TabStop = false;
    }
    public void ShowTheme(AppColorTheme theme)
    {
        theme = AppTheme.Normalize(theme);
        if (shown == theme && palette == AppTheme.PaletteFor(theme, AppTheme.Palette.IsSystemContrast)) return;
        shown = theme;
        palette = AppTheme.PaletteFor(theme, AppTheme.Palette.IsSystemContrast);
        BackColor = palette.Background;
        title.Text = AppTheme.Name(theme) + " · preview";
        description.Text = palette.IsSystemContrast ? "Windows contrast colors take priority over decorative themes."
            : theme switch {
                AppColorTheme.Glamour => "Blush satin · raspberry accents · rose-gold bows · pearl surfaces",
                AppColorTheme.Light => "Airy surfaces, dark ink, and forest-green accents.",
                AppColorTheme.HighContrast => "Black and white with bright yellow highlights.",
                _ => "Charcoal surfaces, light text, and mint accents."
            };
        var oldFont = titleFont;
        titleFont = new(palette.IsGlamour ? "Georgia" : "Segoe UI", 15, palette.IsGlamour ? FontStyle.Italic : FontStyle.Bold);
        title.Font = titleFont; oldFont?.Dispose();
        title.BackColor = description.BackColor = palette.Background;
        title.ForeColor = palette.Text; description.ForeColor = palette.Muted;
        sample.BackColor = palette.Field; sample.ForeColor = palette.IsSystemContrast ? SystemColors.WindowText : palette.Text;
        action.BackColor = palette.PrimaryButton; action.ForeColor = palette.AccentText;
        AccessibleName = AppTheme.Name(theme) + " theme preview";
        AccessibleDescription = description.Text + " Sample reflection field and save button. This preview is not interactive.";
        Invalidate();
    }
    internal void RefreshPalette() => ShowTheme(shown ?? AppTheme.Preference);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(palette.Decoration);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        if (palette.IsGlamour) {
            var size = (int)(44 * DeviceDpi / 96d);
            GlamourOrnament.Draw(e.Graphics, new(Width - size - 20, Height - size - 14, size, size), palette);
        }
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) titleFont?.Dispose();
    }
}

internal static class GlamourOrnament
{
    public static void Draw(Graphics graphics, Rectangle bounds, ThemePalette palette)
    {
        var saved = graphics.Save();
        try {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / 60f, bounds.Height / 60f);
            using var fill = new SolidBrush(palette.Raised);
            using var outline = new Pen(palette.Decoration, 1.5f);
            using var path = new GraphicsPath();
            path.AddBezier(30, 27, 2, 2, 0, 48, 30, 31);
            path.AddBezier(30, 31, 60, 48, 58, 2, 30, 27);
            path.CloseFigure(); graphics.FillPath(fill, path); graphics.DrawPath(outline, path);
            graphics.DrawBezier(outline, 28, 31, 23, 39, 21, 47, 13, 52);
            graphics.DrawBezier(outline, 32, 31, 37, 39, 39, 47, 47, 52);
            using var jewel = new SolidBrush(palette.Accent);
            graphics.FillEllipse(jewel, 26, 24, 8, 10);
            graphics.DrawLine(outline, 49, 3, 49, 13); graphics.DrawLine(outline, 44, 8, 54, 8);
        }
        finally { graphics.Restore(saved); }
    }
}
