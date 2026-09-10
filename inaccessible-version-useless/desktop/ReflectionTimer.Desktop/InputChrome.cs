using System.Runtime.InteropServices;

namespace ReflectionTimer.Desktop;

// Keep native editors (keyboard navigation, selection and accessibility) inside
// a consistently sized field, rather than stretching their text or hit targets.
public sealed class InputFrame : Panel
{
    public Control Editor { get; }
    public InputFrame(Control editor)
    {
        var originalWidth = editor.Width;
        SuspendLayout();
        Editor = editor;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false; TabIndex = editor.TabIndex; Font = editor.Font;
        Width = originalWidth; Height = Widgets.FieldHeight(this);
        Margin = new(0, 4, 10, 4); BackColor = AppTheme.Field;
        editor.Margin = Padding.Empty;
        if (editor is TextBox text) text.BorderStyle = BorderStyle.None;
        if (editor is NumericUpDown number) number.BorderStyle = BorderStyle.None;
        Controls.Add(editor);
        editor.Enter += (_, _) => Invalidate(); editor.Leave += (_, _) => Invalidate();
        editor.EnabledChanged += (_, _) => Invalidate();
        MouseDown += (_, _) => editor.Focus();
        ResumeLayout(true);
    }
    public static void Wrap(Control editor)
    {
        if (editor.Parent is null or InputFrame) return;
        var parent = editor.Parent; var index = parent.Controls.GetChildIndex(editor);
        var location = editor.Location; var anchor = editor.Anchor; var dock = editor.Dock;
        parent.SuspendLayout();
        try {
            var frame = new InputFrame(editor) { Location = location, Anchor = anchor, Dock = dock };
            editor.Anchor = AnchorStyles.Top | AnchorStyles.Left; editor.Dock = DockStyle.None;
            parent.Controls.Add(frame); parent.Controls.SetChildIndex(frame, index);
        }
        finally { parent.ResumeLayout(true); }
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (Editor is not null) {
            var inset = Math.Max(4, 7 * DeviceDpi / 96);
            Editor.SetBounds(inset, (Height - Editor.Height) / 2, Math.Max(1, Width - 2 * inset), Editor.Height);
        }
        base.OnLayout(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var thickness = Math.Max(1, DeviceDpi / 96) * (ContainsFocus ? 2 : 1);
        using var pen = new Pen(ContainsFocus ? AppTheme.Accent : AppTheme.Border, thickness);
        e.Graphics.DrawRectangle(pen, thickness / 2f, thickness / 2f, Width - thickness, Height - thickness);
    }
}

// Local to this application's UI thread: no global hook, external activity or
// input logging. Merely focusing an editor (including Ctrl+Alt+T) never arms it.
public sealed class ClickToScrollInputs : IMessageFilter, IDisposable
{
    private Control? armed;
    private Form? armedForm;
    private readonly bool installed;
    public ClickToScrollInputs(bool install = true)
    {
        installed = install;
        if (install) Application.AddMessageFilter(this);
    }
    private static Control? EditorFor(Control? control)
    {
        for (var current = control; current is not null; current = current.Parent) {
            if (current.Parent is NumericUpDown number) return number;
            if (current is InputFrame frame) return frame.Editor;
            if (current is NumericUpDown or ComboBox or TextBoxBase or TrackBar) return current;
        }
        return null;
    }
    private void Disarm(object? sender = null, EventArgs? e = null)
    {
        if (armed is null) return;
        armed.Leave -= Disarm; armed.Disposed -= Disarm;
        if (armedForm is not null) armedForm.Deactivate -= Disarm;
        armedForm = null;
        armed = null;
    }
    public bool PreFilterMessage(ref Message message)
    {
        const int leftDown = 0x0201, wheel = 0x020A, horizontalWheel = 0x020E, activateApp = 0x001C;
        if (message.Msg == activateApp && message.WParam == 0) { Disarm(); return false; }
        if (message.Msg is not (leftDown or wheel or horizontalWheel)) return false;
        var target = Control.FromChildHandle(message.HWnd);
        var editor = EditorFor(target);
        if (message.Msg == leftDown) {
            Disarm();
            if (editor is { Enabled: true }) {
                armed = editor; armed.Leave += Disarm; armed.Disposed += Disarm;
                armedForm = editor.FindForm();
                if (armedForm is not null) armedForm.Deactivate += Disarm;
            }
            return false;
        }
        // Native combo popup lists may not have their own managed Control.
        if (editor is null) return false;
        if (ReferenceEquals(editor, armed) && editor.ContainsFocus && editor.Enabled) return false;
        for (var parent = editor.Parent; parent is not null; parent = parent.Parent) {
            if (parent is ScrollableControl { AutoScroll: true } scroll) {
                SendMessage(scroll.Handle, message.Msg, message.WParam, message.LParam);
                break;
            }
        }
        return true;
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);
    public void Dispose()
    {
        Disarm();
        if (installed) Application.RemoveMessageFilter(this);
    }
}

public sealed class SettingsSection : Label
{
    public SettingsSection(string title)
    {
        Text = title; UseMnemonic = false; Width = 750; Height = 48; AutoSize = false;
        Font = new("Segoe UI", 12, FontStyle.Bold);
        Padding = new(12, 0, 0, 0); TextAlign = ContentAlignment.MiddleLeft;
        Margin = new(0, 24, 0, 14); BackColor = AppTheme.Raised; ForeColor = AppTheme.Text;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(AppTheme.Border, Math.Max(1, DeviceDpi / 96));
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        using var brush = new SolidBrush(AppTheme.Accent);
        e.Graphics.FillRectangle(brush, 0, 0, Math.Max(3, 3 * DeviceDpi / 96), Height);
    }
}
