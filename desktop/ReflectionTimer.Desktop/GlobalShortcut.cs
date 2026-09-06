using System.Runtime.InteropServices;

namespace ReflectionTimer.Desktop;

public interface IHotKeyRegistration
{
    bool Register(nint window, int id, uint modifiers, uint key);
    bool Unregister(nint window, int id);
}

// A message-only window receives just this registered chord, not a keyboard hook
// or a stream of the user's keystrokes. Keep its lifetime independent of Hide().
public sealed class GlobalShortcut : NativeWindow, IDisposable
{
    internal const int HotKeyMessage = 0x0312, HotKeyId = 0x5254;
    internal const uint Modifiers = 0x0002 | 0x0001 | 0x4000; // Control + Alt + NoRepeat
    internal const uint Key = 0x54; // T
    private readonly IHotKeyRegistration registration;
    private readonly Action pressed;
    private bool disposed;
    public bool IsRegistered { get; private set; }

    public GlobalShortcut(Action pressed, IHotKeyRegistration? registration = null)
    {
        this.pressed = pressed; this.registration = registration ?? new WindowsHotKeyRegistration();
        CreateHandle(new CreateParams { Caption = "Reflection Timer shortcut", Parent = new nint(-3) }); // HWND_MESSAGE
        try { IsRegistered = this.registration.Register(Handle, HotKeyId, Modifiers, Key); }
        catch { DestroyHandle(); throw; }
    }

    internal bool Dispatch(int message, nint id)
    {
        if (disposed || !IsRegistered || message != HotKeyMessage || id != HotKeyId) return false;
        pressed(); return true;
    }
    protected override void WndProc(ref Message message)
    {
        if (Dispatch(message.Msg, message.WParam)) { message.Result = 0; return; }
        base.WndProc(ref message);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { if (IsRegistered) registration.Unregister(Handle, HotKeyId); }
        finally { IsRegistered = false; DestroyHandle(); }
        GC.SuppressFinalize(this);
    }
}

internal sealed class WindowsHotKeyRegistration : IHotKeyRegistration
{
    public bool Register(nint window, int id, uint modifiers, uint key) => RegisterHotKey(window, id, modifiers, key);
    public bool Unregister(nint window, int id) => UnregisterHotKey(window, id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}

internal static class WindowActivation
{
    public static void Focus(Form window)
    {
        window.Show();
        if (window.WindowState == FormWindowState.Minimized) ShowWindow(window.Handle, 9); // SW_RESTORE
        else ShowWindow(window.Handle, 5); // SW_SHOW also overrides a hidden launcher STARTUPINFO on first open.
        window.BringToFront(); window.Activate();
        // An owned modal (including a native file picker) must remain in front of
        // its disabled owner; never dismiss it or redirect typing behind it.
        SetForegroundWindow(window.Enabled ? window.Handle : GetLastActivePopup(window.Handle));
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    private static extern nint GetLastActivePopup(nint window);
}
