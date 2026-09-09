using System.Diagnostics;
using System.Text.Json;
using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public sealed class MainWindow : Form
{
    private readonly TimerApplication app;
    private readonly Action<bool> updateStartup;
    private readonly ThemeTabs tabs = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = Widgets.Muted };
    private readonly Button saveSettingsButton;
    public long StatusRevision { get; private set; }
    private readonly Label display = new() { Width = 750, Height = 90, TextAlign = ContentAlignment.MiddleCenter, Font = new("Consolas", 48, FontStyle.Bold), ForeColor = Widgets.Ink };
    private readonly Label timerStatus = Widgets.Text("");
    private readonly Label migration = Widgets.Text("");
    private readonly Label pending = Widgets.Text("");
    private readonly DurationControl duration = new();
    private readonly AutoRestartOptions repeat = new();
    private readonly LowTimeControl lowTime = new();
    private readonly VolumeControl volume = new();
    private readonly Button start;
    private readonly SessionStartInput timerStart = new() { Width = 300, Value = DateTime.Now.AddHours(1), AccessibleName = "Start timer at" };
    private readonly DataGridView scheduleGrid = Widgets.Grid("Start time", "Duration", "Auto-start", "Auto-start cutoff", "Sound", "Low on time", "Status");
    private readonly ComboBox scheduleOverlap = new() { Width = 460, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Schedule overlap behavior" };
    private readonly SessionStartInput scheduledStart = new() { Width = 300, Value = DateTime.Now.AddHours(1) };
    private readonly DurationControl scheduledDuration = new();
    private readonly AutoRestartOptions scheduledRepeat;
    private readonly LowTimeControl scheduledLowTime = new();
    private readonly VolumeControl scheduledVolume = new();
    private readonly Label scheduleHeading = Widgets.Text("Add a scheduled session");
    private readonly Button saveSchedule;
    private Guid? editing;
    private readonly DataGridView outboxGrid = Widgets.Grid("Saved locally", "Destination", "Status", "Attempts");
    private readonly TextBox outboxText = new() { Width = 750, Height = 145, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox sheetUrl = new() { Width = 750 };
    private readonly TextBox webAppUrl = new() { Width = 750 };
    private readonly TextBox token = new() { Width = 750, UseSystemPasswordChar = true };
    private readonly AudioSettingsControl audio;
    private readonly ComboBox popupPosition = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Reflection popup position" };
    private readonly ComboBox themeChoice = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "App theme" };
    private readonly ThemePreview themePreview = new();
    private readonly CheckBox floatingChoice = new() { Text = "Show compact floating timer (always on top)", AutoSize = true };
    private readonly ComboBox floatingPlacement = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Compact timer position" };
    private readonly Label themeNotice = Widgets.Text("");
    private readonly Label shortcutNotice = Widgets.Text("Ctrl+Alt+T is disabled in this isolated test session.");
    private readonly Label endEarlyNotice = Widgets.Text("Ctrl+Alt+` is disabled in this isolated test session.");
    private readonly Label compactNotice = Widgets.Text("Ctrl+Alt+/ is disabled in this isolated test session.");
    private readonly Label compactFocusNotice = Widgets.Text("Ctrl+Alt+. is disabled in this isolated test session.");
    private readonly Label reflectionFocusNotice = Widgets.Text("Ctrl+Alt+, is disabled in this isolated test session.");
    private readonly ComboBox mode = new() { Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox sheetName = new() { Width = 350 };
    private readonly CheckBox logging = new() { Text = "Record local diagnostic events", AutoSize = true };
    private readonly CheckBox login = new() { Text = "Start in the tray when I sign in to Windows", AutoSize = true };
    private readonly CheckBox disabledExtension = new() { Text = "The Chrome timer extension is off, or I never installed it", AutoSize = true };
    private readonly Label diagnosticsSummary = Widgets.Text("");
    private readonly TextBox diagnosticPreview = new() { Width = 750, Height = 280, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new("Consolas", 10) };
    private bool binding;
    private long? selectableRunningDeadline;
    private string scheduleSignature = "", outboxSignature = "";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowExit { get; set; }

    public MainWindow(TimerApplication app, Action<bool>? updateStartup = null)
    {
        this.app = app;
        this.updateStartup = updateStartup ?? StartupRegistration.Set;
        scheduledRepeat = new(() => scheduledStart.Value);
        Text = "Reflection Timer Desktop"; Size = new(940, 810); MinimumSize = new(880, 700);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Segoe UI", 10); BackColor = AppTheme.Background;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Information;
        var header = new ThemeHeader("Reflection Timer");
        saveSettingsButton = Widgets.Button("Save settings", (_, _) => SaveSettings(), true);
        saveSettingsButton.Anchor = AnchorStyles.Right;
        saveSettingsButton.Margin = new(14, 0, 0, 0);
        var statusBar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new(20, 8, 20, 8), ColumnCount = 2, RowCount = 1 };
        statusBar.ColumnStyles.Add(new(SizeType.Percent, 100)); statusBar.ColumnStyles.Add(new(SizeType.AutoSize));
        statusBar.RowStyles.Add(new(SizeType.Percent, 100));
        statusBar.Controls.Add(status, 0, 0); statusBar.Controls.Add(saveSettingsButton, 1, 0);
        Controls.Add(tabs); Controls.Add(statusBar); Controls.Add(header);
        var timer = Widgets.Page(tabs, "Timer");
        AppTheme.SetTextColor(migration, ThemeTextRole.Warning);
        AppTheme.SetTextColor(display, ThemeTextRole.Text);
        AppTheme.SetTextColor(status, ThemeTextRole.Muted);
        timer.Controls.Add(migration); timer.Controls.Add(display); timer.Controls.Add(timerStatus); timer.Controls.Add(duration);
        start = Widgets.Button("Start", (_, _) => Safe(() => {
            if (!app.Engine.Snapshot.ExtensionDisabledConfirmed) throw new InvalidOperationException("Turn off the Chrome extension, then confirm the switch in Settings.");
            var current = app.Engine.Snapshot.Timer;
            if (current.IsRunning) app.Engine.Pause();
            else StartOrResumeTimer();
            duration.LoadSeconds(app.Engine.Snapshot.Timer.DurationSeconds, true);
        }), true);
        // Enter in the regular duration editor starts/resumes through the same
        // validation and options as Start. Repeated Enter must not pause it.
        duration.SubmitRequested += () => { if (!app.Engine.Snapshot.Timer.IsRunning) start.PerformClick(); };
        timer.Controls.Add(Widgets.Row(start, Widgets.Button("Reset", (_, _) => Safe(ResetTimer))));
        timer.Controls.Add(Widgets.Row(Widgets.RowText("Start timer at", 125), timerStart,
            Widgets.Button("Schedule session", (_, _) => ScheduleTimerSession())));
        timer.Controls.Add(Widgets.Text("Uses the duration and options on this page. View or cancel it in Scheduling session times."));
        timer.Controls.Add(repeat); timer.Controls.Add(lowTime); timer.Controls.Add(volume);
        lowTime.UserChanged += () => {
            if (binding) return;
            try { app.Engine.SetLowTime(lowTime.Selection); SetStatus("Low-time options saved."); }
            catch (Exception error) { lowTime.LoadOptions(app.Engine.Snapshot.Timer.LowTime, AudioSettings.From(app.Engine.Snapshot).LowTimeThresholdSeconds, true); SetStatus(error.Message, true); }
        };
        lowTime.Error += text => SetStatus(text, true);
        scheduledLowTime.Error += text => SetStatus(text, true);
        lowTime.PreviewRequested += (options, automatic) => _ = app.PlaySound(SoundEvent.LowTime, volume.Value,
            AudioSettings.From(app.Engine.Snapshot).ForLowTime(options), preview: true, announcePreview: !automatic);
        scheduledLowTime.PreviewRequested += (options, automatic) => {
            if (automatic) SetStatus("Session audio selected. Save the session to keep this choice.");
            _ = app.PlaySound(SoundEvent.LowTime, app.Engine.Snapshot.Timer.Volume,
                AudioSettings.From(app.Engine.Snapshot).ForLowTime(options), preview: true, announcePreview: !automatic);
        };
        repeat.UserChanged += SaveTimerPreferences;
        volume.UserChanged += () => {
            if (binding) return;
            Safe(() => app.Engine.SetAppVolume(volume.Value));
            volume.Value = app.Engine.Snapshot.Timer.Volume;
        };
        duration.DraftChanged += RenderClock;
        timer.Controls.Add(Widgets.Row(Widgets.Button("Test reflection prompt", (_, _) => Safe(() => app.Engine.TestPrompt())),
            Widgets.Button("Pending reflections", (_, _) => app.ShowReflections()), Widgets.Button("Mark issue", (_, _) => app.MarkIssue())));
        timer.Controls.Add(pending);
        timer.Controls.Add(Widgets.Button("Show / hide floating timer", (_, _) => app.SetFloatingTimer(!app.Engine.Snapshot.ShowFloatingTimer)));
        timer.Controls.Add(Widgets.Text("Closing this window keeps the timer running in the tray. Right-click its tray icon to quit. Test reflections only go to the test tab."));
        timer.Controls.Add(Widgets.Button("Quit desktop app", (_, _) => app.Quit()));

        var schedule = Widgets.Page(tabs, "Scheduling session times");
        schedule.Controls.Add(Widgets.Text("Each appointment keeps its own duration, repeat, cutoff and audio options. Choose how a due appointment handles a running or paused session. After downtime, only the latest newly missed appointment is kept."));
        schedule.Controls.Add(Widgets.Text("When a scheduled session overlaps"));
        scheduleOverlap.Items.AddRange(["End current with a reflection, then start scheduled", "Ask me what to do", "Wait until the current session ends"]);
        schedule.Controls.Add(scheduleOverlap);
        scheduleOverlap.SelectedIndexChanged += (_, _) => { if (!binding && scheduleOverlap.SelectedIndex >= 0) Safe(() => app.Engine.SetScheduleOverlap((ScheduleOverlapPolicy)scheduleOverlap.SelectedIndex)); };
        scheduleGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        scheduleGrid.Columns[0].FillWeight = 160; scheduleGrid.Columns[1].FillWeight = 85;
        scheduleGrid.Columns[2].FillWeight = 90; scheduleGrid.Columns[3].FillWeight = 160; scheduleGrid.Columns[4].FillWeight = 55;
        scheduleGrid.Columns[4].MinimumWidth = 65;
        schedule.Controls.Add(scheduleGrid);
        schedule.Controls.Add(Widgets.Row(Widgets.Button("Edit selected", (_, _) => EditSelectedSchedule()), Widgets.Button("Remove selected", (_, _) => Safe(() => {
            if (SelectedSchedule() is { } selected) { app.Engine.RemoveSchedule(selected.Id); if (editing == selected.Id) ResetScheduleEditor(); }
        })), Widgets.Button("Import extension schedules…", (_, _) => ImportSchedules())));
        schedule.Controls.Add(scheduleHeading); schedule.Controls.Add(Widgets.Text("Start date and time (your local time zone)")); schedule.Controls.Add(scheduledStart);
        schedule.Controls.Add(scheduledDuration); schedule.Controls.Add(scheduledRepeat); schedule.Controls.Add(scheduledLowTime); schedule.Controls.Add(scheduledVolume);
        saveSchedule = Widgets.Button("Add session", (_, _) => Safe(() => {
            var picked = scheduledStart.Value;
            var minute = new DateTime(picked.Year, picked.Month, picked.Day, picked.Hour, picked.Minute, 0, DateTimeKind.Local);
            app.Engine.SaveSchedule(editing, new DateTimeOffset(minute), scheduledDuration.CommitSeconds(), scheduledRepeat.AutoRestart, scheduledVolume.Value, scheduledRepeat.AutoRestartUntil, scheduledLowTime.Selection);
            ResetScheduleEditor(); SetStatus("Schedule saved.", success: true);
        }), true);
        schedule.Controls.Add(Widgets.Row(saveSchedule, Widgets.Button("Cancel edit / new session", (_, _) => ResetScheduleEditor())));
        schedule.Controls.Add(Widgets.Text("Auto-start repeats this duration until its optional cutoff; it does not move the next appointment earlier. Cutoffs must follow the scheduled start. Pausing, resetting, or reaching a cutoff does not remove future appointments."));

        var outbox = Widgets.Page(tabs, "Outbox");
        outbox.Controls.Add(Widgets.Text("Reflections save locally first. With the updated receiver, temporary failures retry automatically using the same entry ID, with increasing delays up to five minutes. Partial writes, legacy uploads and changed receivers still need review. After eight attempts, automatic retries stop."));
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
        settings.Controls.Add(Widgets.Row(Widgets.Button("Guided setup / another PC", (_, _) => ShowSetup(), true),
            Widgets.Button("Setup guide", (_, _) => SetupWindow.OpenGuide())));
        settings.Controls.Add(new SettingsSection("Display") { Margin = new(0, 0, 0, 14) });
        settings.Controls.Add(floatingChoice);
        floatingChoice.CheckedChanged += (_, _) => { if (!binding) app.SetFloatingTimer(floatingChoice.Checked); };
        settings.Controls.Add(Widgets.Text("Compact timer position"));
        floatingPlacement.Items.AddRange(["Remember dragged position", "Center", "Top left", "Top right", "Bottom left", "Bottom right", "Top center", "Bottom center"]);
        settings.Controls.Add(floatingPlacement);
        floatingPlacement.SelectedIndexChanged += (_, _) => {
            if (!binding && floatingPlacement.SelectedIndex >= 0) Safe(() => app.Engine.SetFloatingTimerPlacement((FloatingTimerPlacement)floatingPlacement.SelectedIndex));
        };
        settings.Controls.Add(Widgets.Text("Dragging saves a custom position, including across app and PC restarts. Presets use that screen; if it is disconnected, the timer stays on an available screen. Duration fields are shared with the main timer and editable while stopped or paused."));
        settings.Controls.Add(Widgets.Text("Keyboard shortcuts")); settings.Controls.Add(shortcutNotice); settings.Controls.Add(endEarlyNotice); settings.Controls.Add(compactNotice); settings.Controls.Add(compactFocusNotice); settings.Controls.Add(reflectionFocusNotice);
        settings.Controls.Add(Widgets.Text("App theme"));
        themeChoice.Items.AddRange(["Dark", "Light", "High Contrast", "Glamour"]);
        settings.Controls.Add(themeChoice); settings.Controls.Add(themePreview); settings.Controls.Add(themeNotice);
        themeChoice.SelectedIndexChanged += (_, _) => {
            if (binding || themeChoice.SelectedIndex < 0) return;
            try {
                app.Engine.SetTheme((AppColorTheme)themeChoice.SelectedIndex);
                AppTheme.Change(app.Engine.Snapshot.Theme);
                RenderThemeChoice(app.Engine.Snapshot.Theme);
                SetStatus("Theme saved. " + themeNotice.Text);
            }
            catch {
                RenderThemeChoice(app.Engine.Snapshot.Theme);
                SetStatus("Could not save the theme. Your previous choice is unchanged.", true);
            }
        };
        settings.Controls.Add(Widgets.Text("Reflection popup position"));
        popupPosition.Items.AddRange(["Center", "Top left", "Top right", "Bottom left", "Bottom right"]);
        settings.Controls.Add(popupPosition);
        popupPosition.SelectedIndexChanged += (_, _) => {
            if (binding || popupPosition.SelectedIndex < 0) return;
            try {
                app.Engine.SetPopupPosition((ReflectionPopupPosition)popupPosition.SelectedIndex);
                SetStatus("Popup position saved. Applies the next time a reflection window opens.");
            }
            catch {
                binding = true;
                popupPosition.SelectedIndex = PopupPositionIndex(app.Engine.Snapshot.PopupPosition);
                binding = false;
                SetStatus("Could not save the popup position. Your previous setting is unchanged.", true);
            }
        };
        settings.Controls.Add(Widgets.Text("Saves immediately for regular, scheduled, and test reflections. Uses the screen containing your mouse pointer when the popup opens, leaving space for the taskbar. An already-open reflection stays where it is."));
        audio = new AudioSettingsControl(app);
        audio.Status += (text, error) => SetStatus(text, error);
        settings.Controls.Add(audio);
        settings.Controls.Add(new SettingsSection("Avoid duplicate timers"));
        settings.Controls.Add(Widgets.Text("Never used the Chrome extension? Check the confirmation below. If you have it, turn it off in chrome://extensions first. This desktop app is independent and does not read Chrome profiles or browsing activity."));
        settings.Controls.Add(disabledExtension);
        settings.Controls.Add(new SettingsSection("Google Sheets connection"));
        settings.Controls.Add(Widgets.Text("Google Sheets URL")); settings.Controls.Add(sheetUrl);
        settings.Controls.Add(Widgets.Text("Apps Script deployment URL (must end in /exec)")); settings.Controls.Add(webAppUrl);
        settings.Controls.Add(Widgets.Text("Private Reflection API token — Guided setup creates one, or reuse your existing connection")); settings.Controls.Add(token);
        mode.Items.AddRange(["Automatically match the date when I save", "Always use a fixed tab"]);
        settings.Controls.Add(Widgets.Text("Destination tab")); settings.Controls.Add(mode); settings.Controls.Add(sheetName);
        mode.SelectedIndexChanged += (_, _) => sheetName.Enabled = mode.SelectedIndex == 1;
        settings.Controls.Add(Widgets.Text("Your own Apps Script handles dated tabs, your configured template, column colors, row borders, and hour themes. Offline entries retain their original save date. Test prompts always use test. No developer spreadsheet or credentials are prefilled."));
        settings.Controls.Add(new SettingsSection("Startup & diagnostics"));
        settings.Controls.Add(login); settings.Controls.Add(logging);
        settings.Controls.Add(new SettingsSection("Save settings"));
        settings.Controls.Add(Widgets.Text("Ctrl+Enter or the bottom-right Save settings button saves your settings from anywhere on this page."));
        settings.Controls.Add(Widgets.Row(Widgets.Button("Save & test connection", async (_, _) => {
            if (!SaveSettings(feedback: false)) return;
            SetStatus("Checking the Sheets connection…");
            var result = await app.Sheets.Ping(app.Engine.Snapshot.Connection);
            app.Log.Record("connection.checked", value: result.Success ? 1 : 0);
            SetStatus(result.Success ? "Connection works · " + result.Target : result.DisplayMessage, !result.Success, success: result.Success);
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
        tabs.SelectedIndexChanged += (_, _) => { saveSettingsButton.Visible = tabs.SelectedIndex == 3; if (tabs.SelectedIndex == 4) RenderDiagnostics(); if (tabs.SelectedIndex == 0) BeginInvoke(FocusDuration); };
        var initial = app.Engine.Snapshot;
        sheetUrl.Text = initial.Connection.SheetUrl; webAppUrl.Text = initial.Connection.WebAppUrl; token.Text = initial.Connection.ApiToken;
        mode.SelectedIndex = initial.Connection.SheetMode == "fixed" ? 1 : 0; sheetName.Text = initial.Connection.SheetName;
        logging.Checked = initial.LoggingEnabled; login.Checked = initial.StartAtLogin; disabledExtension.Checked = initial.ExtensionDisabledConfirmed;
        if (!initial.ExtensionDisabledConfirmed || SheetsClient.Validate(initial.Connection) is not null) tabs.SelectedIndex = 3;
        saveSettingsButton.Visible = tabs.SelectedIndex == 3;
        Activated += (_, _) => app.Log.Record("app.activated");
        Deactivate += (_, _) => {
            app.Log.Record("app.deactivated");
            selectableRunningDeadline = null;
            duration.Enabled = !app.Engine.Snapshot.Timer.IsRunning;
        };
        FormClosing += (_, e) => {
            if (!AllowExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); app.Log.Record("app.hidden"); }
        };
        AppTheme.Apply(this);
    }
    public void FocusDuration() { if (tabs.SelectedIndex == 0) duration.FocusFirstPositivePart(); }
    internal void ShowSetup()
    {
        if (!Enabled || IsDisposed) return;
        var draft = app.Engine.Snapshot.SetupDraft ?? new ConnectionSettings { SheetUrl = sheetUrl.Text.Trim(), WebAppUrl = webAppUrl.Text.Trim(), ApiToken = token.Text.Trim(), SheetMode = mode.SelectedIndex == 1 ? "fixed" : "date", SheetName = sheetName.Text.Trim() };
        using var setup = new SetupWindow(app, draft);
        if (setup.ShowDialog(this) != DialogResult.OK || !setup.Connected) return;
        var connected = app.Engine.Snapshot.Connection;
        sheetUrl.Text = connected.SheetUrl; webAppUrl.Text = connected.WebAppUrl; token.Text = connected.ApiToken;
        mode.SelectedIndex = connected.SheetMode == "fixed" ? 1 : 0; sheetName.Text = connected.SheetName;
        disabledExtension.Checked = true;
        SetStatus("Your spreadsheet connection is ready. Other settings are unchanged.", success: true);
        tabs.SelectedIndex = 0;
    }
    internal void FocusTimerPage()
    {
        if (!Enabled) return; // Do not redirect typing behind an owned modal dialog.
        tabs.SelectedIndex = 0;
        var state = app.Engine.Snapshot;
        selectableRunningDeadline = state.Timer.IsRunning ? state.Timer.EndTime : null;
        Render(state); // Settle queued shared-field updates before selecting text.
        FocusDuration();
    }
    internal DurationControl TimerDuration => duration;
    internal void ResetTimer()
    {
        app.Engine.Reset(duration.Dirty ? duration.CommitSeconds() : null);
        duration.LoadSeconds(app.Engine.Snapshot.Timer.DurationSeconds, true);
    }
    internal bool CanStartTimer {
        get {
            try {
                var state = app.Engine.Snapshot;
                if (!state.ExtensionDisabledConfirmed) return false;
                var seconds = !duration.Dirty && TimerEngine.IsPaused(state.Timer) ? state.Timer.RemainingSeconds : duration.Seconds;
                TimerEngine.ValidateDuration(seconds);
                if (repeat.AutoRestartUntil <= app.Engine.Now) return false;
                AudioSettings.Validate(lowTime.Selection);
                return true;
            } catch (ArgumentException) { return false; }
        }
    }
    private void StartOrResumeTimer()
    {
        if (!app.Engine.Snapshot.ExtensionDisabledConfirmed) throw new InvalidOperationException("Turn off the Chrome extension, then confirm the switch in Settings.");
        var current = app.Engine.Snapshot.Timer;
        if (current.IsRunning) return;
        if (!duration.Dirty && TimerEngine.IsPaused(current)) {
            app.Engine.SetPreferences(repeat.AutoRestart, volume.Value, repeat.AutoRestartUntil);
            app.Engine.SetLowTime(lowTime.Selection);
            app.Engine.Resume();
        }
        else app.Engine.Start(duration.CommitSeconds(), repeat.AutoRestart, volume.Value, repeat.AutoRestartUntil, lowTime.Selection);
        duration.LoadSeconds(app.Engine.Snapshot.Timer.DurationSeconds, true);
    }
    internal void StartTimerFromShortcut()
    {
        // PerformClick does nothing when the Timer tab/window is hidden. Call
        // the same validated operation directly, including uncommitted HH/MM/SS.
        try { StartOrResumeTimer(); }
        catch (Exception error) {
            app.Log.Record("error.unexpected");
            app.Open();
            SetStatus("Could not start the timer: " + error.Message, true);
        }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (tabs.SelectedIndex == 3 && keyData == (Keys.Control | Keys.Enter)) { SaveSettings(); return true; }
        if (tabs.SelectedIndex == 3 && keyData == Keys.Enter && audio.ThresholdContainsFocus) { audio.LeaveThreshold(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    public void SetShortcutStatus(bool available, bool endEarlyAvailable, bool compactAvailable = false, bool compactFocusAvailable = false, bool reflectionFocusAvailable = false)
    {
        compactNotice.Text = compactAvailable ? "Ctrl+Alt+/ · cycle compact controls → time-only → hidden → controls. Showing controls selects the first positive duration field (Hours if all are zero). A running timer continues unchanged; pause to edit its duration."
            : "Ctrl+Alt+/ could not be registered (another app may use it). You can still show the compact timer using the checkbox or tray menu.";
        compactFocusNotice.Text = compactFocusAvailable ? "Ctrl+Alt+. (period) · press once to select the compact timer input; press twice within 0.8 seconds to select it in the full Timer view. Selects the first positive field from the left (Hours if all are zero). Running durations are read-only; the timer is unchanged."
            : "Ctrl+Alt+. (period) could not be registered (another app may use it). You can still open the compact timer using the checkbox or tray menu.";
        AppTheme.SetTextColor(compactNotice, compactAvailable ? ThemeTextRole.Muted : ThemeTextRole.Warning);
        AppTheme.SetTextColor(compactFocusNotice, compactFocusAvailable ? ThemeTextRole.Muted : ThemeTextRole.Warning);
        reflectionFocusNotice.Text = reflectionFocusAvailable ? "Ctrl+Alt+, (comma) · open a check-in for the current session. Sending records elapsed active time without stopping the timer. Repeated presses focus the same draft. Use Pending reflections for older entries."
            : "Ctrl+Alt+, (comma) could not be registered (another app may use it). Use the tray menu's Check in to current session instead. Pending reflections opens older drafts.";
        AppTheme.SetTextColor(reflectionFocusNotice, reflectionFocusAvailable ? ThemeTextRole.Muted : ThemeTextRole.Warning);
        shortcutNotice.Text = available ? "Ctrl+Alt+T · focus Reflection Timer from any app, including the tray. On the Timer tab, select the first positive duration field from left to right (Hours if all are zero). The app must be running."
            : "Ctrl+Alt+T is unavailable. Another app may have reserved it. Close that app and reopen Reflection Timer to try again. The timer still works normally.";
        AppTheme.SetTextColor(shortcutNotice, available ? ThemeTextRole.Muted : ThemeTextRole.Warning);
        endEarlyNotice.Text = endEarlyAvailable ? "Ctrl+Alt+` (backtick) · start using the Timer page's duration and options, or resume a paused timer. If running, end the session now and open its reflection. Auto-start and its cutoff work as usual; future schedules stay unchanged."
            : "Ctrl+Alt+` (backtick) is unavailable. Another app may have reserved it. Close that app and reopen Reflection Timer to try again.";
        AppTheme.SetTextColor(endEarlyNotice, endEarlyAvailable ? ThemeTextRole.Muted : ThemeTextRole.Warning);
        if (!available || !endEarlyAvailable || !compactAvailable || !compactFocusAvailable || !reflectionFocusAvailable) SetStatus("A keyboard shortcut could not be registered. See Settings → Keyboard shortcuts.", true);
    }
    private static int PopupPositionIndex(ReflectionPopupPosition position) => Enum.IsDefined(position) ? (int)position : 0;
    private void RenderThemeChoice(AppColorTheme theme)
    {
        var wasBinding = binding; binding = true;
        try {
            theme = AppTheme.Normalize(theme);
            themeChoice.SelectedIndex = (int)theme; themePreview.ShowTheme(theme);
            themeNotice.Text = AppTheme.Palette.IsSystemContrast
                ? "Windows high-contrast colors take priority. Your chosen theme returns when Windows high contrast is off."
                : "Active theme. Changes save and apply immediately to all app windows; no restart needed.";
        }
        finally { binding = wasBinding; }
    }
    public void SetStatus(string text, bool error = false, bool success = false, bool silent = false)
    {
        ++StatusRevision;
        status.Text = text; AppTheme.SetTextColor(status, error ? ThemeTextRole.Error : ThemeTextRole.Accent);
        if (!silent && (error || success)) app.PlayFeedback(!error);
    }
    private void Safe(Action action) { try { action(); } catch (Exception error) { app.Log.Record("error.unexpected"); SetStatus(error.Message, true); } }
    public static string Clock(int total) => total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60:00}:{total % 60:00}";
    public void RenderClock()
    {
        var state = app.Engine.Snapshot;
        var remaining = app.DisplaySeconds(state.Timer, app.Engine.Now);
        start.Text = state.Timer.IsRunning ? "Pause" : !duration.Dirty && TimerEngine.IsPaused(state.Timer) ? "Resume" : "Start";
        var shown = remaining;
        if (!state.Timer.IsRunning && duration.Dirty && !duration.TryGetSeconds(out shown, out var error)) {
            display.Text = "—"; timerStatus.Text = error; AppTheme.SetTextColor(timerStatus, ThemeTextRole.Error); return;
        }
        display.Text = Clock(shown); AppTheme.SetTextColor(timerStatus, ThemeTextRole.Muted);
        timerStatus.Text = state.Timer.IsRunning ? "Running · ends " + DateTimeOffset.FromUnixTimeMilliseconds(state.Timer.EndTime!.Value).ToLocalTime().ToString("t")
            : TimerEngine.IsPaused(state.Timer) ? "Paused" : "Ready";
    }
    public void Render(AppState state)
    {
        binding = true;
        floatingChoice.Checked = state.ShowFloatingTimer;
        floatingPlacement.SelectedIndex = Enum.IsDefined(state.FloatingPlacement) ? (int)state.FloatingPlacement : 0;
        scheduleOverlap.SelectedIndex = Enum.IsDefined(state.ScheduleOverlap) ? (int)state.ScheduleOverlap : 0;
        RenderThemeChoice(state.Theme);
        popupPosition.SelectedIndex = PopupPositionIndex(state.PopupPosition);
        var sounds = AudioSettings.From(state); audio.LoadOptions(sounds);
        lowTime.LoadOptions(state.Timer.LowTime, sounds.LowTimeThresholdSeconds);
        scheduledLowTime.LoadOptions(scheduledLowTime.Selection, sounds.LowTimeThresholdSeconds);
        if (state.Timer.IsRunning || !duration.Dirty) duration.LoadSeconds(state.Timer.DurationSeconds, true);
        if (!state.Timer.IsRunning || state.Timer.EndTime != selectableRunningDeadline) selectableRunningDeadline = null;
        duration.ReadOnly = state.Timer.IsRunning;
        duration.Enabled = !state.Timer.IsRunning || selectableRunningDeadline.HasValue;
        repeat.LoadOptions(state.Timer.AutoRestart, state.Timer.AutoRestartUntil); volume.Value = state.Timer.Volume;
        start.Text = state.Timer.IsRunning ? "Pause" : !duration.Dirty && TimerEngine.IsPaused(state.Timer) ? "Resume" : "Start";
        start.Enabled = state.ExtensionDisabledConfirmed;
        migration.Text = state.ExtensionDisabledConfirmed ? "Desktop timer active · Use only one timer app per session"
            : "Open Guided setup in Settings. Confirm the Chrome extension is off or not installed before starting.";
        pending.Text = $"{state.Prompts.Count} pending reflection(s) · {state.Outbox.Count(x => x.Status != DeliveryStatus.Sent)} unsent entry/entries";
        var signature = JsonSerializer.Serialize(state.Schedules);
        if (signature != scheduleSignature) {
            var selected = SelectedSchedule()?.Id; scheduleGrid.Rows.Clear();
            foreach (var entry in state.Schedules) {
                var row = scheduleGrid.Rows[scheduleGrid.Rows.Add(DateTimeOffset.FromUnixTimeMilliseconds(entry.StartTime).ToLocalTime().ToString("g"), Clock(entry.DurationSeconds), entry.AutoRestart ? "On" : "Off",
                    entry.AutoRestartUntil.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(entry.AutoRestartUntil.Value).ToLocalTime().ToString("g") : "—", entry.Volume + "%",
                    entry.LowTime.Enabled ? entry.LowTime.ThresholdSeconds is { } seconds ? Clock(seconds) : "Default" : "Off",
                    entry.AwaitingDecision ? "Needs choice" : entry.WaitingForCurrentSession ? "Waiting" : "Scheduled")];
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
    private void ShowOutboxText()
    {
        var entry = SelectedOutbox();
        outboxText.Text = entry is null ? "" : entry.Message + "\r\n\r\n" +
            (entry.ActualDurationSeconds is { } actual ? Clock(actual) + " spent / " : "Actual time unavailable / ") + Clock(entry.DurationSeconds) + " allotted" +
            (entry.IsCheckIn ? " · Check-in" : entry.EndedEarly ? " · ended early\r\nReason: " + (entry.EarlyEndReason.Length > 0 ? entry.EarlyEndReason : "Not supplied") : "") +
            (entry.Status == DeliveryStatus.Pending && entry.NextAttemptAt is { } next ? "\r\nNext retry: " + DateTimeOffset.FromUnixTimeMilliseconds(next).ToLocalTime().ToString("T") : "") +
            (entry.Status == DeliveryStatus.NeedsReview ? "\r\nNeeds review: " + entry.ErrorKind + ". Check the Sheet before retrying." : "");
    }
    private void EditSelectedSchedule()
    {
        if (SelectedSchedule() is not { } entry) return;
        editing = entry.Id; scheduledStart.Value = DateTimeOffset.FromUnixTimeMilliseconds(entry.StartTime).LocalDateTime;
        scheduledDuration.LoadSeconds(entry.DurationSeconds); scheduledRepeat.LoadOptions(entry.AutoRestart, entry.AutoRestartUntil, true); scheduledVolume.Value = entry.Volume;
        scheduledLowTime.LoadOptions(entry.LowTime, AudioSettings.From(app.Engine.Snapshot).LowTimeThresholdSeconds, true);
        scheduleHeading.Text = "Edit scheduled session"; saveSchedule.Text = "Save changes"; scheduledStart.Focus();
    }
    private void ResetScheduleEditor()
    {
        editing = null; scheduledStart.Value = DateTime.Now.AddHours(1); scheduledDuration.LoadSeconds(1500, true); scheduledRepeat.LoadOptions(false, null, true); scheduledVolume.Value = 50;
        scheduledLowTime.LoadOptions(new(), AudioSettings.From(app.Engine.Snapshot).LowTimeThresholdSeconds, true);
        scheduleHeading.Text = "Add a scheduled session"; saveSchedule.Text = "Add session";
    }
    private void ScheduleTimerSession() => Safe(() => {
        var when = new DateTimeOffset(timerStart.Value);
        var seconds = duration.CommitSeconds();
        app.Engine.SaveSchedule(null, when, seconds, repeat.AutoRestart, volume.Value, repeat.AutoRestartUntil, lowTime.Selection);
        SetStatus($"Session scheduled for {when.LocalDateTime:g}. View or cancel it in Scheduling session times.", success: true);
    });
    private void SaveTimerPreferences()
    {
        if (binding) return;
        Safe(() => {
            app.Engine.SetPreferences(repeat.AutoRestart, volume.Value, repeat.AutoRestartUntil);
            SetStatus("Timer preferences saved.");
        });
    }
    private bool SaveSettings(bool feedback = true)
    {
        try {
            var connection = new ConnectionSettings { SheetUrl = sheetUrl.Text.Trim(), WebAppUrl = webAppUrl.Text.Trim(), ApiToken = token.Text.Trim(), SheetMode = mode.SelectedIndex == 1 ? "fixed" : "date", SheetName = sheetName.Text.Trim() };
            app.Engine.SaveSettings(connection, logging.Checked, login.Checked, disabledExtension.Checked, audio.DefaultThresholdSeconds);
            updateStartup(login.Checked);
            var missing = SheetsClient.Validate(connection);
            SetStatus(missing is not null ? "Settings saved locally. " + missing : "Settings saved.", success: feedback);
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
        Safe(() => { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(app.Log.Report(app.Engine.Snapshot), DataJson.Options)); SetStatus("Diagnostic report exported. Review its activity times before sharing.", success: true); });
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
            app.Engine.ImportSchedules(imported); SetStatus("Future schedules imported. Past appointments and duplicate start times were skipped.", success: true);
        });
    }
}
