using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public static class Widgets
{
    public static readonly Color Ink = Color.FromArgb(23, 32, 51);
    public static readonly Color Muted = Color.FromArgb(82, 96, 120);
    public static readonly Color Green = Color.FromArgb(21, 128, 61);
    public static Label Text(string text, int width = 750) => new() { Text = text, AutoSize = true, MaximumSize = new(width, 0), ForeColor = Muted, Margin = new(0, 6, 0, 10) };
    public static Button Button(string text, EventHandler action, bool primary = false)
    {
        var button = new Button { Text = text, UseMnemonic = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new(110, 36), Padding = new(10, 4, 10, 4), Margin = new(0, 4, 10, 4) };
        if (primary) { button.BackColor = Green; button.ForeColor = Color.White; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; }
        button.Click += action; return button;
    }
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Width = 750, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new(0, 4, 0, 6) };
        row.Controls.AddRange(controls); return row;
    }
    public static FlowLayoutPanel Page(TabControl tabs, string title)
    {
        var tab = new TabPage(title) { BackColor = Color.White, Padding = new(18) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        tab.Controls.Add(flow); tabs.TabPages.Add(tab); return flow;
    }
    public static DataGridView Grid(params string[] columns)
    {
        var grid = new DataGridView { Width = 750, Height = 230, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new(0, 6, 0, 8) };
        foreach (var name in columns) grid.Columns.Add(name.Replace(" ", ""), name);
        return grid;
    }
}

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
