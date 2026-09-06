using ReflectionTimer.Core;
using ReflectionTimer.Desktop;

internal static partial class Program
{
    private static void TestInputLayout()
    {
        TestSettingsInteractions();
        Test("native fields and neighboring buttons have equal surfaces and aligned tops", () => {
            using var form = new Form { ClientSize = new(900, 500), Font = new("Segoe UI", 10) };
            var text = new TextBox { Width = 140, Text = "draft", UseSystemPasswordChar = true };
            var number = new NumericUpDown { Width = 120, Value = 15 };
            var combo = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(["Disruptive", "Polite"]); combo.SelectedIndex = 0;
            var button = Widgets.Button("Choose MP3…", (_, _) => { });
            var row = Widgets.Row(text, number, combo, button); form.Controls.Add(row);
            AppTheme.Apply(form); AppTheme.Apply(form); form.Show(); form.PerformLayout();
            Equal(3, Descendants(form).OfType<InputFrame>().Count());
            Equal(140, text.Parent!.Width); Equal(120, number.Parent!.Width); Equal(180, combo.Parent!.Width);
            foreach (var field in Descendants(form).OfType<InputFrame>()) {
                Equal(button.Height, field.Height); Equal(button.Top, field.Top);
                Is(field.Editor.Bounds.Top >= 0); Is(field.Editor.Bottom <= field.ClientSize.Height);
            }
            Equal("draft", text.Text); Is(text.UseSystemPasswordChar); Equal(15m, number.Value);
            Equal(0, combo.SelectedIndex); Equal(0, row.Controls.GetChildIndex(text.Parent!));
            Is(button.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width);
        });
        Test("wheel guard requires a click, disarms on blur and preserves page scrolling", () => {
            using var form = new InputTestForm { ClientSize = new(500, 300) };
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new(0, 1000) };
            var number = new NumericUpDown { Value = 15, Width = 120 };
            var combo = new ComboBox { Top = 50, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(["One", "Two"]); combo.SelectedIndex = 0;
            var text = new TextBox { Top = 100, Text = "draft" };
            var track = new TrackBar { Top = 160 };
            page.Controls.AddRange([number, combo, text, track]); form.Controls.Add(page); form.Show();
            using var guard = new ClickToScrollInputs(install: false);
            bool Filter(Control target, int message, nint wParam = 0) {
                var msg = Message.Create(target.Handle, message, wParam, 0); return guard.PreFilterMessage(ref msg);
            }
            foreach (var input in new Control[] { number, combo, text, track }) {
                input.Focus();
                Is(Filter(input, 0x020A, (nint)(-120 << 16))); // Programmatic/keyboard focus is not a click.
                Is(!Filter(input, 0x0201));
                Is(!Filter(input, 0x020A, (nint)(120 << 16)));
                form.ActiveControl = page;
                input.Focus(); Is(Filter(input, 0x020A, (nint)(-120 << 16)));
            }
            number.Focus();
            var child = number.Controls.OfType<TextBox>().Single();
            Is(!Filter(child, 0x0201)); Is(!Filter(child, 0x020A, (nint)(120 << 16)));
            form.DeactivateForTest(); Is(Filter(child, 0x020A, (nint)(-120 << 16)));
            Is(!Filter(child, 0x0201));
            Is(!Filter(page, 0x0201)); Is(Filter(child, 0x020A, (nint)(-120 << 16)));
            Is(page.AutoScrollPosition.Y < 0); Equal(15m, number.Value); Equal(0, combo.SelectedIndex);
        });
        Test("settings save commits the threshold atomically and keeps other audio choices", () => {
            var f = new Fixture(); f.Engine.SetSound(SoundEvent.Success, new() { Track = LibrarySound.LevelUp });
            f.Engine.SaveSettings(Connection, true, false, true, 120);
            Equal(120, AudioSettings.From(f.Restart().Snapshot).LowTimeThresholdSeconds);
            Equal(LibrarySound.LevelUp, AudioSettings.From(f.Engine.Snapshot).Success.Track);
            var old = f.Engine.Snapshot.Connection; f.Store.Fail = true;
            Throws<IOException>(() => f.Engine.SaveSettings(new(), false, true, false, 300));
            Equal(old, f.Engine.Snapshot.Connection); Equal(120, AudioSettings.From(f.Engine.Snapshot).LowTimeThresholdSeconds);
            f.Store.Fail = false;
            Throws<ArgumentException>(() => f.Engine.SaveSettings(new(), false, true, false, 0));
            Equal(old, f.Engine.Snapshot.Connection); Equal(120, AudioSettings.From(f.Engine.Snapshot).LowTimeThresholdSeconds);
            f.Engine.SaveSettings(Connection, true, false, true); Equal(120, AudioSettings.From(f.Engine.Snapshot).LowTimeThresholdSeconds);
        });
        Test("Glamour fields and section boundaries stand out from the surrounding surface", () => {
            var palette = AppTheme.PaletteFor(AppColorTheme.Glamour);
            Is(Contrast(palette.Background, palette.Field) >= 1.15);
            Is(Contrast(palette.Border, palette.Field) >= 3);
            Is(Contrast(palette.Border, palette.Background) >= 3);
            using var section = new SettingsSection("Audio");
            Is(section.Font.Bold); Is(section.Margin.Top >= 20); Is(section.Height >= 40);
            Equal(AppTheme.Raised, section.BackColor);
        });
    }
    private sealed class InputTestForm : Form
    {
        public void DeactivateForTest() => OnDeactivate(EventArgs.Empty);
    }
}
