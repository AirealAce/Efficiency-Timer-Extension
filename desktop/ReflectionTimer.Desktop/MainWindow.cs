using System.Diagnostics;
using System.Text.Json;
using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public sealed class MainWindow : Form
{
    private readonly TimerApplication app;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 56, Padding = new(20, 8, 20, 8), ForeColor = Widgets.Muted };
    private readonly Label display = new() { Width = 750, Height = 90, TextAlign = ContentAlignment.MiddleCenter, Font = new("Consolas", 48, FontStyle.Bold), ForeColor = Widgets.Ink };
    private readonly Label timerStatus = Widgets.Text("");
    private readonly Label migration = Widgets.Text("");
    private readonly Label pending = Widgets.Text("");
    private readonly DurationControl duration = new();
    private readonly AutoRestartOptions repeat = new();
    private readonly VolumeControl volume = new();
    private readonly Button start;
    private readonly DataGridView scheduleGrid = Widgets.Grid("Start time", "Duration", "Auto-start", "Auto-start cutoff", "Sound");
    private readonly SessionStartInput scheduledStart = new() { Width = 300, Value = DateTime.Now.AddHours(1) };
    private readonly DurationControl scheduledDuration = new();
    private readonly AutoRestartOptions scheduledRepeat;
    private readonly VolumeControl scheduledVolume = new();
    private readonly Label scheduleHeading = Widgets.Text("Add a scheduled session");
    private readonly Button saveSchedule;
    private Guid? editing;
    private readonly DataGridView outboxGrid = Widgets.Grid("Saved locally", "Destination", "Status", "Attempts");
    private readonly TextBox outboxText = new() { Width = 750, Height = 90, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox sheetUrl = new() { Width = 750 };
    private readonly TextBox webAppUrl = new() { Width = 750 };
    private readonly TextBox token = new() { Width = 750, UseSystemPasswordChar = true };
    private readonly Label alertSoundChoice = Widgets.Text("");
    private readonly ComboBox mode = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox sheetName = new() { Width = 350 };
    private readonly CheckBox logging = new() { Text = "Record local diagnostic events", AutoSize = true };
    private readonly CheckBox login = new() { Text = "Start in the tray when I sign in to Windows", AutoSize = true };
    private readonly CheckBox disabledExtension = new() { Text = "I have turned off the Chrome Reflection Timer extension", AutoSize = true };
    private readonly Label diagnosticsSummary = Widgets.Text("");
    private readonly TextBox diagnosticPreview = new() { Width = 750, Height = 280, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new("Consolas", 10) };
    private bool binding;
    private int soundSelectionVersion;
    private string scheduleSignature = "", outboxSignature = "";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowExit { get; set; }

    public MainWindow(TimerApplication app)
    {
        this.app = app;
        scheduledRepeat = new(() => scheduledStart.Value);
        Text = "Reflection Timer Desktop"; Size = new(940, 810); MinimumSize = new(880, 700);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Segoe UI", 10); BackColor = DarkTheme.Background;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Information;
        var header = new Label { Text = "Reflection Timer", Dock = DockStyle.Top, Height = 72, Font = new("Segoe UI", 26, FontStyle.Bold), ForeColor = Widgets.Green, Padding = new(20, 12, 0, 0) };
        Controls.Add(tabs); Controls.Add(status); Controls.Add(header);
        var timer = Widgets.Page(tabs, "Timer");
        migration.ForeColor = DarkTheme.Warning;
        timer.Controls.Add(migration); timer.Controls.Add(display); timer.Controls.Add(timerStatus); timer.Controls.Add(duration);
        start = Widgets.Button("Start", (_, _) => Safe(() => {
            if (!app.Engine.Snapshot.ExtensionDisabledConfirmed) throw new InvalidOperationException("Turn off the Chrome extension, then confirm the switch in Settings.");
            var current = app.Engine.Snapshot.Timer;
            if (current.IsRunning) app.Engine.Pause();
            else if (!duration.Dirty && current.RemainingSeconds > 0 && current.RemainingSeconds < current.DurationSeconds) {
                app.Engine.SetPreferences(repeat.AutoRestart, volume.Value, repeat.AutoRestartUntil);
                app.Engine.Resume();
            }
            else app.Engine.Start(duration.Seconds, repeat.AutoRestart, volume.Value, repeat.AutoRestartUntil);
            duration.LoadSeconds(app.Engine.Snapshot.Timer.DurationSeconds, true);
        }), true);
        timer.Controls.Add(Widgets.Row(start, Widgets.Button("Reset", (_, _) => Safe(() => { app.Engine.Reset(duration.Dirty ? duration.Seconds : null); duration.LoadSeconds(app.Engine.Snapshot.Timer.DurationSeconds, true); }))));
        timer.Controls.Add(repeat); timer.Controls.Add(volume);
        repeat.UserChanged += SaveTimerPreferences;
        volume.UserChanged += SaveTimerPreferences;
        duration.UserChanged += RenderClock;
        timer.Controls.Add(Widgets.Row(Widgets.Button("Test reflection prompt", (_, _) => Safe(() => app.Engine.TestPrompt())),
            Widgets.Button("Pending reflections", (_, _) => app.ShowReflections()), Widgets.Button("Mark issue", (_, _) => app.MarkIssue())));
        timer.Controls.Add(pending);
        timer.Controls.Add(Widgets.Text("Closing this window keeps the timer running in the tray. Right-click its tray icon to quit. Test reflections only go to the test tab."));
        timer.Controls.Add(Widgets.Button("Quit desktop app", (_, _) => app.Quit()));

        var schedule = Widgets.Page(tabs, "Scheduling session times");
        schedule.Controls.Add(Widgets.Text("Each one-time appointment has its own duration, repeat setting, auto-start cutoff, and sound level. A scheduled start takes over the current timer. After downtime, only the latest missed appointment starts; future appointments remain queued."));
        scheduleGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        scheduleGrid.Columns[0].FillWeight = 160; scheduleGrid.Columns[1].FillWeight = 85;
        scheduleGrid.Columns[2].FillWeight = 90; scheduleGrid.Columns[3].FillWeight = 160; scheduleGrid.Columns[4].FillWeight = 55;
        schedule.Controls.Add(scheduleGrid);
        schedule.Controls.Add(Widgets.Row(Widgets.Button("Edit selected", (_, _) => EditSelectedSchedule()), Widgets.Button("Remove selected", (_, _) => Safe(() => {
            if (SelectedSchedule() is { } selected) { app.Engine.RemoveSchedule(selected.Id); if (editing == selected.Id) ResetScheduleEditor(); }
        })), Widgets.Button("Import extension schedules…", (_, _) => ImportSchedules())));
        schedule.Controls.Add(scheduleHeading); schedule.Controls.Add(Widgets.Text("Start date and time (your local time zone)")); schedule.Controls.Add(scheduledStart);
        schedule.Controls.Add(scheduledDuration); schedule.Controls.Add(scheduledRepeat); schedule.Controls.Add(scheduledVolume);
        saveSchedule = Widgets.Button("Add session", (_, _) => Safe(() => {
            var picked = scheduledStart.Value;
            var minute = new DateTime(picked.Year, picked.Month, picked.Day, picked.Hour, picked.Minute, 0, DateTimeKind.Local);
            app.Engine.SaveSchedule(editing, new DateTimeOffset(minute), scheduledDuration.Seconds, scheduledRepeat.AutoRestart, scheduledVolume.Value, scheduledRepeat.AutoRestartUntil);
            ResetScheduleEditor(); SetStatus("Schedule saved.");
        }), true);
        schedule.Controls.Add(Widgets.Row(saveSchedule, Widgets.Button("Cancel edit / new session", (_, _) => ResetScheduleEditor())));
        schedule.Controls.Add(Widgets.Text("Auto-start repeats this duration until its optional cutoff; it does not move the next appointment earlier. Cutoffs must follow the scheduled start. Pausing, resetting, or reaching a cutoff does not remove future appointments."));

        var outbox = Widgets.Page(tabs, "Outbox");
        outbox.Controls.Add(Widgets.Text("Reflections are saved locally before sending. Pending entries send when a valid connection is available. A timeout or interrupted upload is held for review—not silently retried—because the current Apps Script receiver may already have written it."));
        outbox.Controls.Add(outboxGrid); outboxGrid.SelectionChanged += (_, _) => ShowOutboxText();
        outbox.Controls.Add(outboxText);
        outbox.Controls.Add(Widgets.Row(Widgets.Button("Send pending now", async (_, _) => await app.Sync(), true), Widgets.Button("Retry selected…", async (_, _) => {
            if (SelectedOutbox() is not { } item || item.Status == DeliveryStatus.Sent) return;
            if (MessageBox.Show(this, "Check the Google Sheet first. Retrying an entry that already arrived can create a duplicate. Send it again?", "Confirm retry", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Safe(() => app.Engine.RetryUpload(item.Id)); await app.Sync();
        }), Widgets.Button("Already in Sheet", (_, _) => Safe(() => {
            if (SelectedOutbox() is { Status: DeliveryStatus.NeedsReview } item) app.Engine.MarkAlreadySent(item.Id);
        })), Widgets.Button("Open Google Sheet", (_, _) => OpenSheet())));

        var settings = Widgets.Page(tabs, "Settings");
        settings.Controls.Add(Widgets.Text("Alert sound")); settings.Controls.Add(alertSoundChoice);
        var chooseSound = Widgets.Button("Choose MP3…", async (sender, _) => {
            using var dialog = new OpenFileDialog { Title = "Choose an alert sound", Filter = "MP3 audio|*.mp3", CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var selectionVersion = ++soundSelectionVersion;
            var button = (Button)sender!; button.Enabled = false;
            SetStatus("Checking the MP3…");
            try {
                var path = await Task.Run(() => Mp3AudioBackend.ValidateCustomFile(dialog.FileName));
                if (IsDisposed || selectionVersion != soundSelectionVersion) return;
                app.Engine.SetAlertSound(path); app.Sounds.Stop(); SetStatus("Custom alert sound saved.");
            }
            catch (Exception) { if (!IsDisposed && selectionVersion == soundSelectionVersion) SetStatus("Could not save that sound. Choose a readable MP3 under 50 MB; your previous selection is unchanged.", true); }
            finally { if (!IsDisposed) button.Enabled = true; }
        });
        settings.Controls.Add(Widgets.Row(chooseSound,
            Widgets.Button("Preview sound", async (_, _) => await app.PlayAlertSound(app.Engine.Snapshot.Timer.Volume, true)),
            Widgets.Button("Stop preview", (_, _) => { app.Sounds.Stop(); SetStatus("Sound stopped."); }),
            Widgets.Button("Use extension default", (_, _) => Safe(() => { ++soundSelectionVersion; app.Engine.SetAlertSound(""); app.Sounds.Stop(); SetStatus("Default extension sound restored."); }))));
        settings.Controls.Add(Widgets.Text("Sound choices save immediately and apply to all alerts. Preview uses the Timer sound level; scheduled sessions keep their own volume. Keep a custom MP3 at its selected location. If unavailable, the bundled extension sound plays instead. Audio files and their paths are never uploaded."));
        settings.Controls.Add(Widgets.Text("Switch over: open chrome://extensions, turn off Reflection Timer (leave it installed as a fallback), then check the confirmation below. This app does not read Chrome profile files or collect browser activity."));
        settings.Controls.Add(disabledExtension);
        settings.Controls.Add(Widgets.Text("Google Sheets URL")); settings.Controls.Add(sheetUrl);
        settings.Controls.Add(Widgets.Text("Apps Script deployment URL (must end in /exec)")); settings.Controls.Add(webAppUrl);
        settings.Controls.Add(Widgets.Text("Reflection API token — reuse the value from the extension")); settings.Controls.Add(token);
        mode.Items.AddRange(["Automatically match the date when I save", "Always use a fixed tab"]);
        settings.Controls.Add(Widgets.Text("Destination tab")); settings.Controls.Add(mode); settings.Controls.Add(sheetName);
        mode.SelectedIndexChanged += (_, _) => sheetName.Enabled = mode.SelectedIndex == 1;
        settings.Controls.Add(Widgets.Text("Date routing, new daily tabs from Temp, row borders, alternating timestamps, and hour themes remain handled by your existing Apps Script. Offline entries retain their original save date. Test prompts always use test."));
        settings.Controls.Add(login); settings.Controls.Add(logging);
        settings.Controls.Add(Widgets.Row(Widgets.Button("Save settings", (_, _) => SaveSettings(), true), Widgets.Button("Save & test connection", async (_, _) => {
            if (!SaveSettings()) return;
            SetStatus("Checking the Sheets connection…");
            var result = await app.Sheets.Ping(app.Engine.Snapshot.Connection);
            app.Log.Record("connection.checked", value: result.Success ? 1 : 0);
            SetStatus(result.Success ? "Connection works · " + result.Target : result.DisplayMessage, !result.Success);
        })));
        settings.Controls.Add(Widgets.Text("The API token, reflections, and drafts are encrypted for your Windows account in LocalAppData. Connection settings may be saved incomplete; sending waits until they are valid. Windows sign-in startup is optional and off by default."));

        var diagnostics = Widgets.Page(tabs, "Diagnostics");
        diagnostics.Controls.Add(Widgets.Text("Local event history: timer actions, app focus, schedules, prompt actions, upload outcomes, sleep/resume, and issue markers. No reflection text, credentials, URLs, window titles, or activity from other apps is included in exports. Up to 1,200 events / 7 days."));
        diagnostics.Controls.Add(diagnosticsSummary); diagnostics.Controls.Add(diagnosticPreview);
        diagnostics.Controls.Add(Widgets.Row(Widgets.Button("Mark issue", (_, _) => app.MarkIssue()), Widgets.Button("Refresh", (_, _) => RenderDiagnostics()),
            Widgets.Button("Export diagnostic report…", (_, _) => ExportDiagnostics(), true), Widgets.Button("Clear log…", (_, _) => {
                if (MessageBox.Show(this, "Clear the local diagnostic log? Your timer, schedules, and reflections are not changed.", "Clear diagnostics", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    Safe(() => { app.Log.Clear(); RenderDiagnostics(); });
            })));
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedIndex == 4) RenderDiagnostics(); if (tabs.SelectedIndex == 0) BeginInvoke(FocusHours); };
        var initial = app.Engine.Snapshot;
        sheetUrl.Text = initial.Connection.SheetUrl; webAppUrl.Text = initial.Connection.WebAppUrl; token.Text = initial.Connection.ApiToken;
        mode.SelectedIndex = initial.Connection.SheetMode == "fixed" ? 1 : 0; sheetName.Text = initial.Connection.SheetName;
        logging.Checked = initial.LoggingEnabled; login.Checked = initial.StartAtLogin; disabledExtension.Checked = initial.ExtensionDisabledConfirmed;
        if (!initial.ExtensionDisabledConfirmed || SheetsClient.Validate(initial.Connection) is not null) tabs.SelectedIndex = 3;
        Activated += (_, _) => app.Log.Record("app.activated"); Deactivate += (_, _) => app.Log.Record("app.deactivated");
        FormClosing += (_, e) => {
            if (!AllowExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); app.Log.Record("app.hidden"); }
        };
        DarkTheme.Apply(this);
    }
    public void FocusHours() { if (tabs.SelectedIndex == 0) duration.FocusHours(); }
    public void SetStatus(string text, bool error = false) { status.Text = text; status.ForeColor = error ? DarkTheme.Error : Widgets.Green; }
    private void Safe(Action action) { try { action(); } catch (Exception error) { app.Log.Record("error.unexpected"); SetStatus(error.Message, true); } }
    public static string Clock(int total) => total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60:00}:{total % 60:00}";
    public void RenderClock()
    {
        var state = app.Engine.Snapshot;
        var remaining = TimerEngine.Remaining(state.Timer, app.Engine.Now);
        display.Text = Clock(!state.Timer.IsRunning && duration.Dirty ? duration.Seconds : remaining);
        timerStatus.Text = state.Timer.IsRunning ? "Running · ends " + DateTimeOffset.FromUnixTimeMilliseconds(state.Timer.EndTime!.Value).ToLocalTime().ToString("t")
            : state.Timer.RemainingSeconds is > 0 && state.Timer.RemainingSeconds < state.Timer.DurationSeconds ? "Paused" : "Ready";
    }
    public void Render(AppState state)
    {
        binding = true;
        alertSoundChoice.Text = string.IsNullOrEmpty(state.AlertSoundPath) ? "Default · original extension sound (popup.mp3)" : "Custom MP3 · " + Path.GetFileName(state.AlertSoundPath);
        if (state.Timer.IsRunning || !duration.Dirty) duration.LoadSeconds(state.Timer.DurationSeconds, true);
        duration.Enabled = !state.Timer.IsRunning; repeat.LoadOptions(state.Timer.AutoRestart, state.Timer.AutoRestartUntil); volume.Value = state.Timer.Volume;
        start.Text = state.Timer.IsRunning ? "Pause" : !duration.Dirty && state.Timer.RemainingSeconds > 0 && state.Timer.RemainingSeconds < state.Timer.DurationSeconds ? "Resume" : "Start";
        start.Enabled = state.ExtensionDisabledConfirmed;
        migration.Text = state.ExtensionDisabledConfirmed ? "Desktop timer active · Chrome extension should remain off"
            : "Switch-over pending: turn off the Chrome extension and confirm it in Settings before starting desktop timers.";
        pending.Text = $"{state.Prompts.Count} pending reflection(s) · {state.Outbox.Count(x => x.Status != DeliveryStatus.Sent)} unsent entry/entries";
        var signature = JsonSerializer.Serialize(state.Schedules);
        if (signature != scheduleSignature) {
            var selected = SelectedSchedule()?.Id; scheduleGrid.Rows.Clear();
            foreach (var entry in state.Schedules) {
                var row = scheduleGrid.Rows[scheduleGrid.Rows.Add(DateTimeOffset.FromUnixTimeMilliseconds(entry.StartTime).ToLocalTime().ToString("g"), Clock(entry.DurationSeconds), entry.AutoRestart ? "On" : "Off",
                    entry.AutoRestartUntil.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(entry.AutoRestartUntil.Value).ToLocalTime().ToString("g") : "—", entry.Volume + "%")];
                row.Tag = entry.Id; if (entry.Id == selected) row.Selected = true;
            }
            scheduleSignature = signature;
        }
        signature = JsonSerializer.Serialize(state.Outbox.Select(x => new { x.Id, x.Status, x.Attempts, x.ErrorKind, x.SavedTab }));
        if (signature != outboxSignature) {
            var selected = SelectedOutbox()?.Id; outboxGrid.Rows.Clear();
            foreach (var entry in state.Outbox.AsEnumerable().Reverse()) {
                var row = outboxGrid.Rows[outboxGrid.Rows.Add(entry.SubmittedAt.LocalDateTime.ToString("g"), entry.IsTest ? "test" : entry.SheetMode == "fixed" ? entry.SheetName : entry.SubmittedAt.ToString("MM/dd/yyyy"), entry.Status, entry.Attempts)];
                row.Tag = entry.Id; if (entry.Id == selected) row.Selected = true;
            }
            outboxSignature = signature; ShowOutboxText();
        }
        binding = false; RenderClock();
        if (tabs.SelectedIndex == 4) RenderDiagnostics();
    }
    private ScheduledSession? SelectedSchedule() => scheduleGrid.SelectedRows.Count > 0 && scheduleGrid.SelectedRows[0].Tag is Guid id ? app.Engine.Snapshot.Schedules.FirstOrDefault(x => x.Id == id) : null;
    private OutboxItem? SelectedOutbox() => outboxGrid.SelectedRows.Count > 0 && outboxGrid.SelectedRows[0].Tag is Guid id ? app.Engine.Snapshot.Outbox.FirstOrDefault(x => x.Id == id) : null;
    private void ShowOutboxText() { var entry = SelectedOutbox(); outboxText.Text = entry is null ? "" : entry.Message + (entry.Status == DeliveryStatus.NeedsReview ? "\r\n\r\nNeeds review: " + entry.ErrorKind + ". Check the Sheet before retrying." : ""); }
    private void EditSelectedSchedule()
    {
        if (SelectedSchedule() is not { } entry) return;
        editing = entry.Id; scheduledStart.Value = DateTimeOffset.FromUnixTimeMilliseconds(entry.StartTime).LocalDateTime;
        scheduledDuration.LoadSeconds(entry.DurationSeconds); scheduledRepeat.LoadOptions(entry.AutoRestart, entry.AutoRestartUntil, true); scheduledVolume.Value = entry.Volume;
        scheduleHeading.Text = "Edit scheduled session"; saveSchedule.Text = "Save changes"; scheduledStart.Focus();
    }
    private void ResetScheduleEditor()
    {
        editing = null; scheduledStart.Value = DateTime.Now.AddHours(1); scheduledDuration.LoadSeconds(1500, true); scheduledRepeat.LoadOptions(false, null, true); scheduledVolume.Value = 50;
        scheduleHeading.Text = "Add a scheduled session"; saveSchedule.Text = "Add session";
    }
    private void SaveTimerPreferences()
    {
        if (binding) return;
        Safe(() => {
            app.Engine.SetPreferences(repeat.AutoRestart, volume.Value, repeat.AutoRestartUntil);
            SetStatus("Timer preferences saved.");
        });
    }
    private bool SaveSettings()
    {
        try {
            var connection = new ConnectionSettings { SheetUrl = sheetUrl.Text.Trim(), WebAppUrl = webAppUrl.Text.Trim(), ApiToken = token.Text.Trim(), SheetMode = mode.SelectedIndex == 1 ? "fixed" : "date", SheetName = sheetName.Text.Trim() };
            app.Engine.SaveSettings(connection, logging.Checked, login.Checked, disabledExtension.Checked);
            StartupRegistration.Set(login.Checked);
            SetStatus(SheetsClient.Validate(connection) is { } missing ? "Settings saved locally. " + missing : "Settings saved.");
            _ = app.Sync(); return true;
        } catch (Exception error) { SetStatus("Could not save settings: " + error.Message, true); return false; }
    }
    private void OpenSheet()
    {
        var url = app.Engine.Snapshot.Connection.SheetUrl;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "docs.google.com")
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    private void RenderDiagnostics()
    {
        var events = app.Log.Recent(); diagnosticsSummary.Text = $"{(app.Log.Enabled ? "Recording" : "Paused")} · {events.Count}/1200 events · storage {(app.Log.StorageAvailable ? "available" : "unavailable")}";
        diagnosticPreview.Text = string.Join(Environment.NewLine, events.TakeLast(30).Reverse().Select(x => $"{DateTimeOffset.FromUnixTimeMilliseconds(x.At).ToLocalTime():g}  {x.Event}"));
    }
    private void ExportDiagnostics()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON report|*.json", FileName = "reflection-timer-desktop-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() => { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(app.Log.Report(app.Engine.Snapshot), DataJson.Options)); SetStatus("Diagnostic report exported. Review its activity times before sharing."); });
    }
    private void ImportSchedules()
    {
        using var dialog = new OpenFileDialog { Filter = "Extension diagnostic report|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() => {
            using var json = JsonDocument.Parse(File.ReadAllText(dialog.FileName));
            var list = json.RootElement.GetProperty("snapshot").GetProperty("scheduledSessions");
            var imported = list.EnumerateArray().Select(x => new ScheduledSession(Guid.NewGuid(), x.GetProperty("targetTime").GetInt64(),
                x.GetProperty("requestedDurationSeconds").GetInt32(), x.GetProperty("autoRestart").GetBoolean(), x.GetProperty("sfxVolume").GetInt32())).ToList();
            app.Engine.ImportSchedules(imported); SetStatus("Future schedules imported. Past appointments and duplicate start times were skipped.");
        });
    }
}
