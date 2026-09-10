using System.Reflection;

internal static partial class Program
{
    // Exercise normal command-key bubbling from the target control, but supply
    // the intended modifiers explicitly. PreProcessMessage reads the physical
    // keyboard, so the user's unrelated Ctrl/Alt presses can change a test's Enter.
    private static bool DispatchCommandKey(Control control, Keys key) =>
        (bool)control.GetType().GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(control, [Message.Create(control.Handle, 0x0100, (nint)key, 0), key])!;
}
