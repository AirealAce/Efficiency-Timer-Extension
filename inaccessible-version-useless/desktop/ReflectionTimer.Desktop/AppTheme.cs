using ReflectionTimer.Core;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ReflectionTimer.Desktop;

public record ThemePalette(Color Background, Color Field, Color Raised, Color Border, Color Text,
    Color Muted, Color Accent, Color AccentText, Color Warning, Color Error, Color Selection,
    Color SelectionText, Color Decoration, bool IsGlamour = false, bool IsSystemContrast = false)
{
    public Color PrimaryButton => IsSystemContrast ? SystemColors.Highlight : Accent;
}

public enum ThemeTextRole { Text, Muted, Accent, Warning, Error }

// Recolor existing controls in place: never rebuild a form, editor or timer.
public static class AppTheme
{
    private sealed class Roles { public bool Primary; public ThemeTextRole? Text; }
    private static readonly ConditionalWeakTable<Control, Roles> roles = new();
    private static readonly List<WeakReference<Control>> surfaces = [];
    private static bool changing;
    public static AppColorTheme Preference { get; private set; } = AppColorTheme.Dark;
    public static ThemePalette Palette { get; private set; } = PaletteFor(AppColorTheme.Dark);
    public static Color Background => Palette.Background;
    public static Color Field => Palette.Field;
    public static Color Raised => Palette.Raised;
    public static Color Border => Palette.Border;
    public static Color Text => Palette.Text;
    public static Color Muted => Palette.Muted;
    public static Color Accent => Palette.Accent;
    public static Color AccentText => Palette.AccentText;
    public static Color Warning => Palette.Warning;
    public static Color Error => Palette.Error;
    public static Color Selection => Palette.Selection;
    public static Color SelectionText => Palette.SelectionText;
    public static AppColorTheme Normalize(AppColorTheme theme) => Enum.IsDefined(theme) ? theme : AppColorTheme.Dark;
    public static string Name(AppColorTheme theme) => Normalize(theme) switch {
        AppColorTheme.Light => "Light", AppColorTheme.HighContrast => "High Contrast", AppColorTheme.Glamour => "Glamour", _ => "Dark"
    };
    public static ThemePalette PaletteFor(AppColorTheme theme, bool systemHighContrast = false)
    {
        if (systemHighContrast) return new(SystemColors.Control, SystemColors.Window, SystemColors.Control, SystemColors.WindowText,
            SystemColors.ControlText, SystemColors.ControlText, SystemColors.ControlText, SystemColors.HighlightText,
            SystemColors.ControlText, SystemColors.ControlText, SystemColors.Highlight, SystemColors.HighlightText,
            SystemColors.ControlText, IsSystemContrast: true);
        static Color C(int hex) => Color.FromArgb((hex >> 16) & 255, (hex >> 8) & 255, hex & 255);
        return theme switch {
            AppColorTheme.Light => new(C(0xF3F6FA), C(0xFFFFFF), C(0xE5EBF2), C(0x6F7C8D), C(0x182537), C(0x44546A),
                C(0x12603E), C(0xFFFFFF), C(0x794600), C(0xA51F38), C(0xC8DFD4), C(0x143F2D), C(0x6F7C8D)),
            AppColorTheme.HighContrast => new(Color.Black, Color.Black, Color.Black, Color.White, Color.White, Color.White,
                Color.Yellow, Color.Black, Color.Yellow, C(0xFFB3B3), Color.Yellow, Color.Black, Color.White),
            AppColorTheme.Glamour => new(C(0xFFF1F7), C(0xEADCF5), C(0xF9D6E5), C(0x965172), C(0x4A1934), C(0x6E3B55),
                C(0xA51D5C), C(0xFFFFFF), C(0x714014), C(0xA21438), C(0xE9D5F4), C(0x4A1934), C(0xB77964), IsGlamour: true),
            _ => new(C(0x161A22), C(0x232A36), C(0x2C3543), C(0x4E5B70), C(0xEFF4FA), C(0xB5C1D2),
                C(0x69DFB0), C(0x09261D), C(0xF9C978), C(0xFF979F), C(0x30594D), C(0xEFF4FA), C(0x4E5B70))
        };
    }

    public static void Initialize(Func<AppColorTheme>? readPreference = null)
    {
        // Bootstrap once before querying accessibility colors: that query can
        // create a WinForms helper window, after which these setup calls fail.
        // Tests use this same startup sequence to catch ordering regressions.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ApplicationConfiguration.Initialize();
        Preference = Normalize(readPreference?.Invoke() ?? AppColorTheme.Dark);
        Palette = PaletteFor(Preference, SystemInformation.HighContrast);
        // The .NET 10 API is still marked experimental. Set the initial native
        // color mode before controls exist; Change also updates it at runtime.
        // Neither path alters the user's Windows theme settings.
#pragma warning disable WFO5001
        Application.SetColorMode(!Palette.IsSystemContrast && Preference is AppColorTheme.Dark or AppColorTheme.HighContrast
            ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
    }

    public static void Change(AppColorTheme theme)
    {
        theme = Normalize(theme);
        var contrast = SystemInformation.HighContrast;
        if (changing || (Preference == theme && Palette == PaletteFor(theme, contrast))) return;
        changing = true;
        try {
            Preference = theme;
            Palette = PaletteFor(theme, contrast);
            // .NET 10 updates its native color set and sends its local color
            // notification. It may pump UI messages: guard against re-entry.
#pragma warning disable WFO5001
            Application.SetColorMode(!contrast && theme is AppColorTheme.Dark or AppColorTheme.HighContrast
                ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
            Palette = PaletteFor(theme, contrast);
            surfaces.RemoveAll(reference => !reference.TryGetTarget(out var c) || c.IsDisposed);
            foreach (var reference in surfaces.ToArray()) {
                if (!reference.TryGetTarget(out var surface) || surface.IsDisposed) continue;
                surface.SuspendLayout();
                try { Apply(surface); }
                finally { surface.ResumeLayout(true); }
                surface.Invalidate(true);
            }
        }
        finally { changing = false; }
    }

    public static void SetTextColor(Control control, ThemeTextRole role)
    {
        roles.GetOrCreateValue(control).Text = role;
        control.ForeColor = TextColor(role);
    }
    private static Color TextColor(ThemeTextRole role) => role switch {
        ThemeTextRole.Muted => Muted, ThemeTextRole.Accent => Accent,
        ThemeTextRole.Warning => Warning, ThemeTextRole.Error => Error, _ => Text
    };
    private static void TrackSurface(Control control)
    {
        if (surfaces.Any(reference => reference.TryGetTarget(out var existing) && ReferenceEquals(existing, control))) return;
        surfaces.Add(new(control));
        if (control is Form form) {
            form.HandleCreated += (_, _) => ApplyWindowFrame(form);
            form.Activated += (_, _) => ApplyWindowFrame(form);
        }
    }
    internal static bool ApplyWindowFrame(Form form)
    {
        if (!form.IsHandleCreated || form.IsDisposed || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
        // Documented Windows 11 per-window attributes; no OS theme changes.
        int dark = !Palette.IsSystemContrast && Preference is AppColorTheme.Dark or AppColorTheme.HighContrast ? 1 : 0;
        int caption = Palette.IsSystemContrast ? -1 : ColorTranslator.ToWin32(Raised);
        int text = Palette.IsSystemContrast ? -1 : ColorTranslator.ToWin32(Text);
        int border = Palette.IsSystemContrast ? -1 : ColorTranslator.ToWin32(Border);
        var darkResult = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        var captionResult = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
        var textResult = DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
        var borderResult = DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
        return darkResult == 0 && captionResult == 0 && textResult == 0 && borderResult == 0;
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    public static void Apply(Control control)
    {
        if (control.IsDisposed) return;
        if (control is Form form) { TrackSurface(form); ApplyWindowFrame(form); }
        if (control is ThemePreview preview) { preview.RefreshPalette(); return; }
        if (control is ThemeHeader header) { header.RefreshPalette(); return; }
        if (control is SettingsSection section) { section.BackColor = Raised; section.ForeColor = Text; section.Invalidate(); return; }
        if (control is ToolStrip menu) { ApplyMenu(menu); return; }
        if (control is DataGridView grid) { ApplyGrid(grid); return; }
        if (control is CompactWindowButton captionButton) { captionButton.ApplyPalette(); return; }
        if (control is Button button) {
            // Match the shared field surface even after inherited font/DPI changes.
            button.AutoSize = false;
            button.Width = Math.Max(button.MinimumSize.Width, button.GetPreferredSize(Size.Empty).Width);
            button.Height = Widgets.FieldHeight(button);
            ApplyButton(button, roles.GetOrCreateValue(button).Primary);
            return;
        }
        if (control is InputFrame frame) { frame.BackColor = Field; frame.ForeColor = Text; Apply(frame.Editor); frame.Invalidate(); return; }
        control.BackColor = Background;
        if (control is Label) {
            var binding = roles.GetOrCreateValue(control);
            binding.Text ??= control.ForeColor == Error ? ThemeTextRole.Error : control.ForeColor == Warning ? ThemeTextRole.Warning
                : control.ForeColor == Accent ? ThemeTextRole.Accent : control.ForeColor == Muted ? ThemeTextRole.Muted : ThemeTextRole.Text;
            control.ForeColor = TextColor(binding.Text.Value);
        }
        else control.ForeColor = Text;

        switch (control)
        {
            case RowLabel label:
                label.Height = Widgets.FieldHeight(label);
                break;
            case TrackBar track when track.Parent?.Parent is VolumeControl:
                track.Height = Widgets.FieldHeight(track);
                break;
            case TextBox text:
                text.BackColor = Field;
                text.ForeColor = Palette.IsSystemContrast ? SystemColors.WindowText : Text;
                text.BorderStyle = text.Parent is InputFrame ? BorderStyle.None : BorderStyle.FixedSingle;
                if (!text.Multiline) InputFrame.Wrap(text);
                return;
            case NumericUpDown number:
                number.BackColor = Field;
                number.ForeColor = Palette.IsSystemContrast ? SystemColors.WindowText : Text;
                number.BorderStyle = number.Parent is InputFrame ? BorderStyle.None : BorderStyle.FixedSingle;
                InputFrame.Wrap(number);
                return;
            case ComboBox combo:
                combo.BackColor = Field;
                combo.ForeColor = Palette.IsSystemContrast ? SystemColors.WindowText : Text;
                combo.FlatStyle = FlatStyle.Flat;
                InputFrame.Wrap(combo);
                return;
            case CheckBox check:
                check.UseVisualStyleBackColor = false;
                if (check is RowCheckBox) {
                    check.AutoSize = false;
                    check.Width = check.GetPreferredSize(Size.Empty).Width;
                    check.Height = Widgets.FieldHeight(check);
                }
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                break;
        }
        foreach (var child in control.Controls.Cast<Control>().ToArray()) Apply(child);
    }

    public static void ApplyButton(Button button, bool primary)
    {
        roles.GetOrCreateValue(button).Primary = primary;
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Palette.PrimaryButton : Raised;
        button.ForeColor = primary ? AccentText : Text;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = primary ? button.BackColor : Field;
        button.FlatAppearance.MouseDownBackColor = primary ? button.BackColor : Selection;
    }

    public static void ApplyMenu(ToolStrip menu)
    {
        TrackSurface(menu);
        menu.Renderer = new ThemeMenuRenderer();
        menu.BackColor = Raised; menu.ForeColor = Text;
        foreach (ToolStripItem item in menu.Items) {
            item.BackColor = Raised; item.ForeColor = Text;
            if (item is ToolStripDropDownItem drop && drop.HasDropDownItems) ApplyMenu(drop.DropDown);
        }
        menu.Invalidate(true);
    }

    public static void ApplyGrid(DataGridView grid)
    {
        grid.BackgroundColor = Field;
        grid.ForeColor = Text;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.DefaultCellStyle = new DataGridViewCellStyle {
            BackColor = Field, ForeColor = Palette.IsSystemContrast ? SystemColors.WindowText : Text,
            SelectionBackColor = Selection, SelectionForeColor = SelectionText
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Background, ForeColor = Text };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
            BackColor = Raised, ForeColor = Text, SelectionBackColor = Raised, SelectionForeColor = Text
        };
        grid.RowHeadersDefaultCellStyle = grid.ColumnHeadersDefaultCellStyle;
    }
}
