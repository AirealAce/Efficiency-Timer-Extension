using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public record ThemePalette(Color Background, Color Field, Color Raised, Color Border, Color Text,
    Color Muted, Color Accent, Color AccentText, Color Warning, Color Error, Color Selection,
    Color SelectionText, Color Decoration, bool IsGlamour = false, bool IsSystemContrast = false)
{
    public Color PrimaryButton => IsSystemContrast ? SystemColors.Highlight : Accent;
}

// The palette and native color mode are selected together, before creating UI.
// Theme changes are saved for next launch, never rebuilding an active draft or timer.
public static class AppTheme
{
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
        // The .NET 10 API is still marked experimental. Opt in only here, before
        // any controls exist, without altering the user's Windows theme settings.
#pragma warning disable WFO5001
        Application.SetColorMode(!Palette.IsSystemContrast && Preference is AppColorTheme.Dark or AppColorTheme.HighContrast
            ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
    }

    public static void Apply(Control control)
    {
        if (control is ThemePreview or ThemeHeader or SettingsSection) return; // These paint their own palette.
        if (control is DataGridView grid) { ApplyGrid(grid); return; }
        if (control is Button button) {
            // Match the shared field surface even after inherited font/DPI changes.
            button.AutoSize = false;
            button.Width = Math.Max(button.MinimumSize.Width, button.GetPreferredSize(Size.Empty).Width);
            button.Height = Widgets.FieldHeight(button);
            return; // Widgets.Button already applies semantic primary/secondary colors.
        }
        if (control is InputFrame frame) { Apply(frame.Editor); return; }
        control.BackColor = Background;
        if (control is not Label || control.ForeColor == SystemColors.ControlText) control.ForeColor = Text;

        switch (control)
        {
            case RowLabel label:
                label.Height = Widgets.FieldHeight(label);
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
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                break;
        }
        foreach (var child in control.Controls.Cast<Control>().ToArray()) Apply(child);
    }

    public static void ApplyButton(Button button, bool primary)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Palette.PrimaryButton : Raised;
        button.ForeColor = primary ? AccentText : Text;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = primary ? button.BackColor : Field;
        button.FlatAppearance.MouseDownBackColor = primary ? button.BackColor : Selection;
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
