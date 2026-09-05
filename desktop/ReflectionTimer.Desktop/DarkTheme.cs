namespace ReflectionTimer.Desktop;

// One palette for both windows. Native dark mode supplies title bars, scrollbars,
// checkboxes, dialogs, and menus; explicit colors cover app surfaces.
public static class DarkTheme
{
    public static Color Background => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(22, 26, 34);
    public static Color Field => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(35, 42, 54);
    public static Color Raised => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(44, 53, 67);
    public static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(78, 91, 112);
    public static Color Text => SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(239, 244, 250);
    public static Color Muted => SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(181, 193, 210);
    public static Color Accent => SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(105, 223, 176);
    public static Color AccentText => SystemInformation.HighContrast ? SystemColors.HighlightText : Color.FromArgb(9, 38, 29);
    public static Color Warning => SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(249, 201, 120);
    public static Color Error => SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(255, 151, 159);
    public static Color Selection => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(48, 89, 77);
    public static Color SelectionText => SystemInformation.HighContrast ? SystemColors.HighlightText : Text;

    public static void Initialize()
    {
        // The .NET 10 API is still marked experimental. Opt in only here, before
        // any controls exist, without altering the user's Windows theme settings.
#pragma warning disable WFO5001
        Application.SetColorMode(SystemInformation.HighContrast ? SystemColorMode.Classic : SystemColorMode.Dark);
#pragma warning restore WFO5001
    }

    public static void Apply(Control control)
    {
        if (control is DataGridView grid) { ApplyGrid(grid); return; }
        if (control is Button) return; // Widgets.Button already applies semantic primary/secondary colors.
        control.BackColor = Background;
        if (control is not Label) control.ForeColor = Text; // Keep semantic label colors (error, warning, success).

        switch (control)
        {
            case TextBox text:
                text.BackColor = Field;
                text.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Text;
                text.BorderStyle = BorderStyle.FixedSingle;
                return;
            case NumericUpDown number:
                number.BackColor = Field;
                number.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Text;
                number.BorderStyle = BorderStyle.FixedSingle;
                return;
            case ComboBox combo:
                combo.BackColor = Field;
                combo.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Text;
                combo.FlatStyle = FlatStyle.Flat;
                return;
            case CheckBox check:
                check.UseVisualStyleBackColor = false;
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                break;
        }
        foreach (Control child in control.Controls) Apply(child);
    }

    public static void ApplyButton(Button button, bool primary)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? (SystemInformation.HighContrast ? SystemColors.Highlight : Accent) : Raised;
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
            BackColor = Field, ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Text,
            SelectionBackColor = Selection, SelectionForeColor = SelectionText
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Background, ForeColor = Text };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
            BackColor = Raised, ForeColor = Text, SelectionBackColor = Raised, SelectionForeColor = Text
        };
        grid.RowHeadersDefaultCellStyle = grid.ColumnHeadersDefaultCellStyle;
    }
}
