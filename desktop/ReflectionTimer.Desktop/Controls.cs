using ReflectionTimer.Core;
using System.Globalization;

namespace ReflectionTimer.Desktop;

public static class Widgets
{
    public static Color Ink => AppTheme.Text;
    public static Color Muted => AppTheme.Muted;
    public static Color Green => AppTheme.Accent;
    public static int FieldHeight(Control control) => Math.Max(38 * control.DeviceDpi / 96, control.Font.Height + 14 * control.DeviceDpi / 96);
    public static Label Text(string text, int width = 750) => new() { Text = text, AutoSize = true, MaximumSize = new(width, 0), ForeColor = Muted, Margin = new(0, 6, 0, 10) };
    public static Label RowText(string text, int width) => new RowLabel { Text = text, Width = width, Height = 38,
        TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted, Margin = new(0, 4, 10, 4) };
    public static Button Button(string text, EventHandler action, bool primary = false)
    {
        var button = new Button { Text = text, UseMnemonic = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new(110, 38), Padding = new(10, 4, 10, 4), Margin = new(0, 4, 10, 4) };
        AppTheme.ApplyButton(button, primary);
        button.Click += action; return button;
    }
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Width = 750, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new(0, 4, 0, 6) };
        row.Controls.AddRange(controls); return row;
    }
    public static FlowLayoutPanel Page(TabControl tabs, string title)
    {
        var tab = new TabPage(title) { BackColor = AppTheme.Background, Padding = new(18) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        tab.Controls.Add(flow); tabs.TabPages.Add(tab); return flow;
    }
    public static DataGridView Grid(params string[] columns)
    {
        var grid = new DataGridView { Width = 750, Height = 230, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false, BorderStyle = BorderStyle.FixedSingle, Margin = new(0, 6, 0, 8) };
        AppTheme.ApplyGrid(grid);
        foreach (var name in columns) grid.Columns.Add(name.Replace(" ", ""), name);
        return grid;
    }
}

// Sized with the native fields after inheriting the form's font and DPI.
public sealed class RowLabel : Label { }

public sealed class DurationControl : UserControl
{
    private readonly NumericUpDown hours = Number(8760), minutes = Number(59), seconds = Number(59);
    private bool assigning, untouched;
    public bool Dirty { get; private set; }
    public event Action? UserChanged;
    private static NumericUpDown Number(int maximum) => new() { Minimum = 0, Maximum = maximum, Width = 120, Font = new("Segoe UI", 15), TextAlign = HorizontalAlignment.Center };
    public DurationControl()
    {
        Size = new(450, 80); Margin = new(0, 6, 0, 10);
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        foreach (var pair in new[] { ("Hours", hours), ("Minutes", minutes), ("Seconds", seconds) }) {
            var panel = new FlowLayoutPanel { Width = 140, Height = 78, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(new Label { Text = pair.Item1, AutoSize = true, ForeColor = Widgets.Muted }); panel.Controls.Add(pair.Item2); row.Controls.Add(panel);
            void Edited(object? sender, EventArgs args) {
                if (assigning) return;
                if (pair.Item2 == hours && untouched) { assigning = true; minutes.Value = 0; assigning = false; }
                untouched = false; Dirty = true; UserChanged?.Invoke();
            }
            pair.Item2.ValueChanged += Edited;
            // NumericUpDown normally waits for focus to leave before committing
            // typed digits. Mark edits immediately so the preview and preset logic
            // respond while the user types, not only after clicking Start.
            pair.Item2.TextChanged += Edited;
            pair.Item2.Enter += (_, _) => pair.Item2.Select(0, pair.Item2.Text.Length);
        }
        Controls.Add(row); LoadSeconds(1500, true);
    }
    public int Seconds => Math.Min(TimerEngine.MaxDuration, (int)(hours.Value * 3600 + minutes.Value * 60 + seconds.Value));
    public void LoadSeconds(int total, bool clearPresetWhenTypingHours = false)
    {
        assigning = true;
        hours.Value = Math.Clamp(total / 3600, 0, 8760); minutes.Value = Math.Clamp(total / 60 % 60, 0, 59); seconds.Value = Math.Clamp(total % 60, 0, 59);
        assigning = false; Dirty = false; untouched = clearPresetWhenTypingHours && total == 1500;
    }
    public void FocusHours() { if (Enabled) { hours.Focus(); hours.Select(0, hours.Text.Length); } }
}

public sealed class VolumeControl : UserControl
{
    private readonly TrackBar track = new() { Minimum = 0, Maximum = 100, Value = 50, TickFrequency = 10, Width = 260, Height = 42 };
    private readonly Label label = new() { Text = "Sound 50%", AutoSize = true, Width = 110, Padding = new(0, 9, 0, 0) };
    public event Action? UserChanged;
    private bool assigning;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value { get => track.Value; set { assigning = true; track.Value = Math.Clamp(value, 0, 100); label.Text = $"Sound {track.Value}%"; assigning = false; } }
    public VolumeControl()
    {
        Size = new(420, 50); var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        row.Controls.Add(label); row.Controls.Add(track); Controls.Add(row);
        track.Scroll += (_, _) => { label.Text = $"Sound {track.Value}%"; if (!assigning) UserChanged?.Invoke(); };
    }
}

// Shared by the live timer and schedule editor so their repeat/cutoff behavior
// cannot drift. Model refreshes with unchanged options preserve date/time drafts.
public sealed class AutoRestartOptions : UserControl
{
    private readonly CheckBox repeat = new() { Text = "Auto-start next session", AutoSize = true, Margin = new(0, 4, 0, 8) };
    private readonly CheckBox disableAt = new() { Text = "Disable auto-start at", AutoSize = true, Margin = new(0, 6, 12, 0) };
    private readonly SessionStartInput cutoff = new() { Width = 300, Enabled = false, AccessibleName = "Auto-start cutoff date and time" };
    private readonly Func<DateTime> suggestAfter;
    private bool assigning, loaded, loadedRepeat;
    private long? loadedUntil;
    public event Action? UserChanged;
    public bool AutoRestart => repeat.Checked;
    public long? AutoRestartUntil
    {
        get {
            if (!disableAt.Checked) return null;
            try { return new DateTimeOffset(cutoff.Value).ToUnixTimeMilliseconds(); }
            catch (ArgumentException) { throw new ArgumentException("Enter the auto-start cutoff as MM/DD/YYYY hh:mm AM/PM."); }
        }
    }
    public AutoRestartOptions() : this(() => DateTime.Now) { }
    public AutoRestartOptions(Func<DateTime> suggestAfter)
    {
        this.suggestAfter = suggestAfter;
        Width = 750; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Margin = new(0, 6, 0, 4);
        var flow = new FlowLayoutPanel { Width = 750, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.Add(repeat); flow.Controls.Add(Widgets.Row(disableAt, cutoff));
        flow.Controls.Add(Widgets.Text("Local date/time · MM/DD/YYYY hh:mm AM/PM. The current session still finishes."));
        Controls.Add(flow);
        repeat.CheckedChanged += (_, _) => {
            if (assigning) return;
            assigning = true;
            if (!repeat.Checked) { disableAt.Checked = false; cutoff.Enabled = false; }
            assigning = false; UserChanged?.Invoke();
        };
        disableAt.CheckedChanged += (_, _) => {
            if (assigning) return;
            assigning = true;
            cutoff.Enabled = disableAt.Checked;
            if (disableAt.Checked) {
                repeat.Checked = true;
                var after = DateTime.Now;
                try { if (suggestAfter() > after) after = suggestAfter(); } catch (ArgumentException) { }
                try { if (cutoff.Value <= after) cutoff.Value = after.AddHours(1); }
                catch (ArgumentException) { cutoff.Value = after.AddHours(1); }
            }
            assigning = false; UserChanged?.Invoke();
        };
        cutoff.Validated += (_, _) => { if (!assigning && disableAt.Checked) UserChanged?.Invoke(); };
        cutoff.KeyDown += (_, e) => {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (!assigning && disableAt.Checked) UserChanged?.Invoke();
        };
    }
    public void LoadOptions(bool autoRestart, long? until, bool force = false)
    {
        if (!force && loaded && loadedRepeat == autoRestart && loadedUntil == until) return;
        assigning = true;
        repeat.Checked = autoRestart || until.HasValue;
        disableAt.Checked = until.HasValue; cutoff.Enabled = until.HasValue;
        cutoff.Text = until.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(until.Value).LocalDateTime.ToString("MM/dd/yyyy hh:mm tt", CultureInfo.InvariantCulture) : "";
        loaded = true; loadedRepeat = autoRestart; loadedUntil = until;
        assigning = false;
    }
}

// The Win32 date picker still paints a white edit area in native dark mode.
// A themed text field keeps date/time editing readable without custom native
// painting or changing the persisted schedule format and local-time behavior.
public sealed class SessionStartInput : TextBox
{
    public SessionStartInput()
    {
        PlaceholderText = "MM/DD/YYYY hh:mm AM/PM";
        AccessibleName = "Session start date and time";
        AccessibleDescription = "Enter a local date and time, for example 09/05/2026 06:00 PM.";
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public DateTime Value
    {
        get => Parse(Text);
        set => Text = value.ToString("MM/dd/yyyy hh:mm tt", CultureInfo.InvariantCulture);
    }

    public static DateTime Parse(string text)
    {
        if (DateTime.TryParseExact(text.Trim(), ["M/d/yyyy h:mm tt", "MM/dd/yyyy hh:mm tt"], CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var value)) return DateTime.SpecifyKind(value, DateTimeKind.Local);
        throw new ArgumentException("Enter the start date and time as MM/DD/YYYY hh:mm AM/PM (for example, 09/05/2026 06:00 PM).");
    }
}
