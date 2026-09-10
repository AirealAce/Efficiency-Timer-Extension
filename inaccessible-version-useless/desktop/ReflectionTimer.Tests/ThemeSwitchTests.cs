using System.Reflection;
using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static T AppField<T>(TimerApplication app, string name) => (T)typeof(TimerApplication)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static void AssertThemeSurface(Control root)
    {
        Equal(AppTheme.Background, root.BackColor);
        foreach (var control in Descendants(root)) {
            if (control is InputFrame frame) { Equal(AppTheme.Field, frame.BackColor); Equal(AppTheme.Field, frame.Editor.BackColor); }
            if (control is TextBox box) { Equal(AppTheme.Field, box.BackColor); Equal(AppTheme.Text, box.ForeColor); }
            if (control is SettingsSection section) { Equal(AppTheme.Raised, section.BackColor); Equal(AppTheme.Text, section.ForeColor); }
            if (control is Button button) {
                Is(button.BackColor == AppTheme.Palette.PrimaryButton || button.BackColor == AppTheme.Raised);
                Equal(button.FlatAppearance.BorderSize == 0 ? AppTheme.AccentText : AppTheme.Text, button.ForeColor);
            }
            if (control is ThemeHeader header) {
                Equal(AppTheme.Palette.IsGlamour ? "Georgia" : "Segoe UI", header.Font.Name);
                Equal(AppTheme.Palette.IsGlamour ? AppTheme.Raised : AppTheme.Background, header.BackColor);
            }
            if (control is DataGridView grid) {
                Equal(AppTheme.Field, grid.BackgroundColor); Equal(AppTheme.Raised, grid.ColumnHeadersDefaultCellStyle.BackColor);
                Equal(AppTheme.Selection, grid.DefaultCellStyle.SelectionBackColor); Equal(AppTheme.SelectionText, grid.DefaultCellStyle.SelectionForeColor);
            }
        }
    }

    private static void TestThemeSwitches()
    {
        var original = AppTheme.Preference;
        try {
            foreach (var start in Enum.GetValues<AppColorTheme>())
                foreach (var target in Enum.GetValues<AppColorTheme>().Where(x => x != start))
                    Test($"live theme {start} to {target} updates every surface without replacing drafts or timer", () => {
                        var prompt = new ReflectionPrompt(Guid.NewGuid(), DateTimeOffset.Now.ToUnixTimeMilliseconds(), 60, 0, false, "Unsent theme draft") {
                            EndedEarly = true, EarlyEndReason = "Unsent reason", ActualDurationSeconds = 10
                        };
                        WithEndEarlyApp((app, directory) => {
                            var (main, tabs, full, _) = TimerShortcutControls(app); tabs.SelectedIndex = 3;
                            main.Show(); app.FocusCompactTimer(); Application.DoEvents();
                            var mini = AppField<FloatingTimerWindow>(app, "floating");
                            var popup = Application.OpenForms.OfType<ReflectionWindow>().Single();
                            var response = ReflectionResponse(popup); response.Select(2, 5);
                            var reason = Descendants(popup).OfType<TextBox>().Single(x => x.AccessibleName == "Reason for ending early");
                            var schedule = new ScheduledSession(Guid.NewGuid(), app.Engine.Now, 120, false, 0);
                            using var conflict = new ScheduleConflictWindow(app, schedule); conflict.Show();
                            var source = Descendants(main).OfType<ComboBox>().Single(x => x.AccessibleName == "App theme");
                            var threshold = Descendants(main).OfType<NumericUpDown>().Single(x => x.AccessibleName == "Default low-time threshold in seconds");
                            threshold.Text = "123";
                            var scheduleDuration = Descendants(tabs.TabPages[1]).OfType<DurationControl>().Single();
                            CompactPart(scheduleDuration, "Seconds").Text = "100";
                            app.Engine.Start(9000, true, 0, app.Engine.Now + 3600000); Application.DoEvents();
                            var before = app.Engine.Snapshot.Timer;
                            var identities = new[] { main.Handle, mini.Handle, popup.Handle, conflict.Handle };
                            var bounds = popup.Bounds; var texts = new[] { response.Text, reason.Text, threshold.Text };
                            main.Hide(); // Hidden forms must update too, without being shown.
                            source.SelectedIndex = (int)target; Application.DoEvents();
                            Equal(target, app.Engine.Snapshot.Theme); Equal(target, new EncryptedStore(directory).Load().Theme);
                            Equal(target, AppTheme.Preference); Is(!main.Visible); Equal(3, tabs.SelectedIndex);
                            Equal(before, app.Engine.Snapshot.Timer); Equal(bounds, popup.Bounds);
                            Is(identities.SequenceEqual(new[] { main.Handle, mini.Handle, popup.Handle, conflict.Handle }));
                            Equal(texts[0], response.Text); Equal(texts[1], reason.Text); Equal(texts[2], threshold.Text);
                            Equal(2, response.SelectionStart); Equal(5, response.SelectionLength);
                            Equal("100", CompactPart(scheduleDuration, "Seconds").Text);
                            Equal(1, app.Engine.Snapshot.Prompts.Count); Equal(0, app.Engine.Snapshot.Outbox.Count);
                            foreach (var form in new Form[] { main, mini, popup, conflict }) AssertThemeSurface(form);
                            var menu = mini.ContextMenuStrip!; Equal(AppTheme.Raised, menu.BackColor); Equal(AppTheme.Text, menu.Items[0].ForeColor);
                            var tray = AppField<NotifyIcon>(app, "tray"); Equal(AppTheme.Raised, tray.ContextMenuStrip!.BackColor);
                            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemInformation.HighContrast) {
                                // Caption color is a Set-only DWM attribute.
                                Is(AppTheme.ApplyWindowFrame(popup));
                            }
                            // New popups inherit the active theme, even while earlier drafts exist.
                            using var later = new ReflectionWindow(app, prompt with { Id = Guid.NewGuid() });
                            AssertThemeSurface(later);
                            conflict.Dispose();
                        }, new AppState { Theme = start, ExtensionDisabledConfirmed = true, Timer = new() { Volume = 0 }, Prompts = [prompt] });
                    });
            Test("theme text roles and primary buttons survive contrast-color collisions and repeated applies", () => {
                using var form = new Form();
                var labels = Enum.GetValues<ThemeTextRole>().Select(role => { var label = new Label { Text = role.ToString() }; AppTheme.SetTextColor(label, role); return label; }).ToArray();
                var primary = Widgets.Button("Save", (_, _) => { }, true); var secondary = Widgets.Button("Later", (_, _) => { });
                form.Controls.AddRange(labels); form.Controls.AddRange([primary, secondary]); AppTheme.Apply(form);
                foreach (var theme in new[] { AppColorTheme.HighContrast, AppColorTheme.Light, AppColorTheme.Glamour, AppColorTheme.Dark }) {
                    AppTheme.Change(theme); AppTheme.Apply(form); AppTheme.Apply(form);
                    Is(labels.Select(x => x.ForeColor).SequenceEqual(new[] { AppTheme.Text, AppTheme.Muted, AppTheme.Accent, AppTheme.Warning, AppTheme.Error }));
                    Equal(AppTheme.Palette.PrimaryButton, primary.BackColor); Equal(AppTheme.Raised, secondary.BackColor);
                }
            });
            Test("failed theme save restores selector and leaves all current windows unchanged", () => {
                WithEndEarlyApp((app, directory) => {
                    var (main, _, _, _) = TimerShortcutControls(app); app.FocusCompactTimer(); Application.DoEvents();
                    var source = Descendants(main).OfType<ComboBox>().Single(x => x.AccessibleName == "App theme");
                    var previous = app.Engine.Snapshot.Theme; var palette = AppTheme.Palette;
                    using var blocked = new FileStream(Path.Combine(directory, "state.dat.tmp"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    source.SelectedIndex = previous == AppColorTheme.Light ? 0 : 1; Application.DoEvents();
                    Equal(previous, app.Engine.Snapshot.Theme); Equal((int)previous, source.SelectedIndex); Equal(palette, AppTheme.Palette);
                    AssertThemeSurface(main); AssertThemeSurface(AppField<FloatingTimerWindow>(app, "floating"));
                });
            });
        }
        finally { AppTheme.Change(original); }
    }

    private static void RenderThemeSwitchPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        WithEndEarlyApp((app, _) => {
            var (main, tabs, _, _) = TimerShortcutControls(app);
            app.FocusCompactTimer(); var mini = AppField<FloatingTimerWindow>(app, "floating");
            var prompt = new ReflectionPrompt(Guid.NewGuid(), app.Engine.Now, 1800, 0, false, "A preserved reflection draft.") { EndedEarly = true, ActualDurationSeconds = 480, EarlyEndReason = "A preserved reason." };
            using var popup = new ReflectionWindow(app, prompt); popup.Show(); main.Show();
            var source = Descendants(main).OfType<ComboBox>().Single(x => x.AccessibleName == "App theme");
            foreach (var theme in new[] { AppColorTheme.Glamour, AppColorTheme.Dark, AppColorTheme.Light, AppColorTheme.HighContrast }) {
                tabs.SelectedIndex = 3; source.SelectedIndex = (int)theme; Application.DoEvents();
                tabs.SelectedIndex = 0; Application.DoEvents();
                foreach (var pair in new[] { ("main", (Form)main), ("compact", (Form)mini), ("reflection", (Form)popup) }) {
                    using var image = new Bitmap(pair.Item2.Width, pair.Item2.Height);
                    pair.Item2.DrawToBitmap(image, new Rectangle(Point.Empty, pair.Item2.Size));
                    image.Save(Path.Combine(directory, $"{theme}-{pair.Item1}.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        });
    }
}
