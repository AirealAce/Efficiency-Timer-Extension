using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

internal static class AudioLayout
{
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = Widgets.Row(controls); row.AutoSizeMode = AutoSizeMode.GrowAndShrink; return row;
    }
}

public sealed class SoundSourceControl : UserControl
{
    private readonly ComboBox choice = new() { Width = 380, DropDownWidth = 500, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly List<LibrarySound> tracks = [LibrarySound.Default, LibrarySound.None, .. SoundLibrary.Tracks];
    private SoundSetting value = new();
    private bool binding;
    private int selectionVersion;
    public event Action? UserChanged;
    public event Action<string>? Error;
    public event Action<SoundSetting, bool>? PreviewRequested;
    public SoundSetting Selection => value;

    public SoundSourceControl(SoundEvent kind, bool inherit = false, ComboBox? playback = null)
    {
        Width = 750; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        choice.AccessibleName = (inherit ? "Session low-time" : kind.ToString()) + " sound";
        choice.Items.Add(inherit ? "Use Audio settings sound" : "Default · " + SoundLibrary.Name(SoundLibrary.DefaultFor(kind)));
        foreach (var track in tracks.Skip(1)) choice.Items.Add(SoundLibrary.Name(track));
        choice.SelectedIndex = 0;
        choice.SelectedIndexChanged += (_, _) => {
            if (binding || choice.SelectedIndex < 0) return;
            ++selectionVersion;
            if (choice.SelectedIndex < tracks.Count) value = value with { Mp3Path = "", Track = tracks[choice.SelectedIndex] };
            SelectionChangedByUser();
        };
        var choose = Widgets.Button("Choose MP3…", async (sender, _) => {
            using var dialog = new OpenFileDialog { Title = "Choose a sound", Filter = "MP3 audio|*.mp3", CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var button = (Button)sender!;
            var version = ++selectionVersion; button.Enabled = false;
            try {
                var path = await Task.Run(() => Mp3AudioBackend.ValidateCustomFile(dialog.FileName));
                if (IsDisposed || version != selectionVersion) return;
                LoadSelection(value with { Mp3Path = path, Track = LibrarySound.Default }); SelectionChangedByUser();
            }
            catch { if (!IsDisposed && version == selectionVersion) Error?.Invoke("Could not select that MP3. Choose readable audio under 50 MB; your saved setting is unchanged."); }
            finally { if (!IsDisposed) button.Enabled = true; }
        });
        var preview = Widgets.Button("Preview audio", (_, _) => PreviewRequested?.Invoke(value, false));
        if (playback is not null) {
            choice.Width = 260;
            Controls.Add(AudioLayout.Row(choice, playback, preview, choose));
        }
        else Controls.Add(AudioLayout.Row(choice, preview, choose));
    }
    private void SelectionChangedByUser()
    {
        var selected = value;
        UserChanged?.Invoke();
        // A failed save can synchronously restore the previous selection.
        if (selected == value) PreviewRequested?.Invoke(selected, true);
    }
    public void LoadSelection(SoundSetting setting)
    {
        if (value == setting && choice.SelectedIndex >= 0) return;
        binding = true;
        try {
            value = setting;
            if (choice.Items.Count > tracks.Count) choice.Items.RemoveAt(tracks.Count);
            if (setting.Mp3Path.Length > 0) { choice.Items.Add("Custom MP3 · " + Path.GetFileName(setting.Mp3Path)); choice.SelectedIndex = tracks.Count; }
            else choice.SelectedIndex = Math.Max(0, tracks.IndexOf(setting.Track));
        }
        finally { binding = false; }
    }
}

public sealed class LowTimeControl : UserControl
{
    private readonly CheckBox enabled = new() { Text = "Low on time", AutoSize = true };
    private readonly CheckBox inherit = new() { Text = "Use default threshold", AutoSize = true, Checked = true };
    private readonly NumericUpDown seconds = new() { Minimum = 1, Maximum = TimerEngine.MaxDuration, Value = 60, Width = 120, AccessibleName = "Low-time seconds remaining" };
    private readonly Label defaultLabel = Widgets.Text("Default: 60 seconds remaining");
    private readonly SoundSourceControl source = new(SoundEvent.LowTime, true);
    private readonly FlowLayoutPanel options;
    private bool binding;
    private LowTimeOptions? loaded;
    public event Action? UserChanged;
    public event Action<string>? Error;
    public event Action<LowTimeOptions, bool>? PreviewRequested;
    public LowTimeOptions Selection => new() { Enabled = enabled.Checked, ThresholdSeconds = inherit.Checked ? null : (int)seconds.Value,
        Mp3Path = source.Selection.Mp3Path, Track = source.Selection.Track };
    public LowTimeControl()
    {
        Width = 750; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Margin = new(0, 4, 0, 8);
        var flow = new FlowLayoutPanel { Width = 750, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        options = new FlowLayoutPanel { Width = 750, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        options.Controls.Add(AudioLayout.Row(inherit, seconds, Widgets.RowText("seconds remaining", 170)));
        options.Controls.Add(defaultLabel); options.Controls.Add(source);
        flow.Controls.Add(enabled); flow.Controls.Add(options); Controls.Add(flow);
        void Changed() { if (!binding) UserChanged?.Invoke(); }
        enabled.CheckedChanged += (_, _) => { options.Visible = enabled.Checked; Changed(); };
        inherit.CheckedChanged += (_, _) => { seconds.Enabled = !inherit.Checked; Changed(); };
        seconds.ValueChanged += (_, _) => Changed();
        source.UserChanged += Changed; source.Error += message => Error?.Invoke(message);
        source.PreviewRequested += (_, automatic) => PreviewRequested?.Invoke(Selection, automatic);
        seconds.Enabled = false;
    }
    public void LoadOptions(LowTimeOptions value, int defaultSeconds, bool force = false)
    {
        defaultLabel.Text = $"Settings default: {defaultSeconds} seconds remaining · sound behavior follows Settings → Audio.";
        if (!force && loaded == value) {
            binding = true;
            try { if (inherit.Checked) seconds.Value = Math.Clamp(defaultSeconds, 1, TimerEngine.MaxDuration); }
            finally { binding = false; }
            return;
        }
        binding = true;
        try {
            enabled.Checked = value.Enabled; options.Visible = value.Enabled;
            inherit.Checked = value.ThresholdSeconds is null; seconds.Enabled = !inherit.Checked;
            seconds.Value = Math.Clamp(value.ThresholdSeconds ?? defaultSeconds, 1, TimerEngine.MaxDuration);
            source.LoadSelection(new() { Mp3Path = value.Mp3Path, Track = value.Track }); loaded = value;
        }
        finally { binding = false; }
    }
}

public sealed class AudioSettingsControl : UserControl
{
    private readonly TimerApplication app;
    private readonly Dictionary<SoundEvent, (SoundSourceControl Source, ComboBox Behavior)> editors = [];
    private readonly NumericUpDown threshold = new() { Minimum = 1, Maximum = TimerEngine.MaxDuration, Value = 60, Width = 140, AccessibleName = "Default low-time threshold in seconds" };
    private bool binding;
    private int? loadedThreshold;
    public int DefaultThresholdSeconds => (int)threshold.Value;
    public bool ThresholdContainsFocus => threshold.ContainsFocus;
    public void LeaveThreshold() => FindForm()?.SelectNextControl(threshold, true, true, true, true);
    public event Action<string, bool>? Status;
    public AudioSettingsControl(TimerApplication app)
    {
        this.app = app;
        Width = 750; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var flow = new FlowLayoutPanel { Width = 750, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Controls.Add(flow);
        flow.Controls.Add(new SettingsSection("Audio"));
        flow.Controls.Add(Widgets.Text("Disruptive stops other app audio. Assertive lowers other app audio to 25% until it finishes. Polite plays alongside other app audio. The latest Assertive sound takes priority; earlier sounds recover when it finishes. Other apps are unaffected."));
        flow.Controls.Add(Widgets.Text("Default low-time warning"));
        flow.Controls.Add(AudioLayout.Row(threshold, Widgets.RowText("seconds remaining", 180)));
        flow.Controls.Add(Widgets.Text("Save this default with Save settings or Ctrl+Enter. Enter leaves this field."));
        flow.Controls.Add(Widgets.Text("A timer/session can follow this default or set its own threshold. Low on time is off until checked, plays once per session, and never plays a stale warning after the session ends. If a session starts within its threshold, it alerts on its first tick."));
        foreach (var kind in new[] { SoundEvent.Success, SoundEvent.Failure, SoundEvent.LowTime, SoundEvent.SessionEnd }) {
            var heading = Widgets.Text(kind switch { SoundEvent.SessionEnd => "Session end · time limit reached", SoundEvent.LowTime => "Low on time", _ => kind + " messages" });
            heading.Font = new Font("Segoe UI", 10, FontStyle.Bold); heading.ForeColor = AppTheme.Text; flow.Controls.Add(heading);
            var behavior = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = kind + " playback behavior" };
            behavior.Items.AddRange(["Disruptive", "Assertive", "Polite"]); behavior.SelectedIndex = 0;
            var source = new SoundSourceControl(kind, playback: behavior);
            editors.Add(kind, (source, behavior));
            void Store() { if (!binding) Save(() => app.Engine.SetSound(kind, source.Selection with { Behavior = (SoundBehavior)behavior.SelectedIndex })); }
            source.UserChanged += Store; behavior.SelectedIndexChanged += (_, _) => Store(); source.Error += text => { LoadOptions(AudioSettings.From(app.Engine.Snapshot)); Status?.Invoke(text, true); };
            source.PreviewRequested += (selected, automatic) => _ = app.PlaySound(kind, app.Engine.Snapshot.Timer.Volume,
                selected with { Behavior = (SoundBehavior)behavior.SelectedIndex }, preview: true, announcePreview: !automatic);
            flow.Controls.Add(source);
        }
        flow.Controls.Add(AudioLayout.Row(Widgets.Button("Stop all app audio", (_, _) => { app.Sounds.Stop(); Status?.Invoke("App audio stopped.", false); })));
        flow.Controls.Add(Widgets.Text("Sound selections and playback modes autosave silently. Selecting a sound previews it for up to 5 seconds; None is silent. Each new preview replaces the previous one. Preview and message sounds use the Timer volume; session alerts use that session's volume. Custom files stay at their selected location; audio paths are never uploaded."));
    }
    private void Save(Action action)
    {
        try { action(); Status?.Invoke("Audio settings saved.", false); }
        catch { LoadOptions(AudioSettings.From(app.Engine.Snapshot)); Status?.Invoke("Could not save audio settings. Your previous settings are unchanged.", true); }
    }
    public void LoadOptions(AudioSettings settings)
    {
        binding = true;
        try {
            // Sound choices save immediately and refresh this view. They must
            // not discard a threshold draft waiting for the main Save settings.
            if (loadedThreshold != settings.LowTimeThresholdSeconds)
                threshold.Value = Math.Clamp(settings.LowTimeThresholdSeconds, 1, TimerEngine.MaxDuration);
            loadedThreshold = settings.LowTimeThresholdSeconds;
            foreach (var (kind, editor) in editors) {
                var setting = settings.For(kind); editor.Source.LoadSelection(setting);
                editor.Behavior.SelectedIndex = Enum.IsDefined(setting.Behavior) ? (int)setting.Behavior : 0;
            }
        }
        finally { binding = false; }
    }
}
