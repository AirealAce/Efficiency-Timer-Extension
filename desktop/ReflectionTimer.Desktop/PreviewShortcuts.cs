using ReflectionTimer.Desktop;

namespace ReflectionTimer.Accessible;

// Register just the five original chords; retry only registrations another app owns.
internal sealed class PreviewShortcuts : IDisposable
{
    internal static readonly (uint Key, int Id)[] Chords = [
        (GlobalShortcut.Key, GlobalShortcut.HotKeyId),
        (GlobalShortcut.EndEarlyKey, GlobalShortcut.EndEarlyId),
        (GlobalShortcut.CompactKey, GlobalShortcut.CompactId),
        (GlobalShortcut.CompactFocusKey, GlobalShortcut.CompactFocusId),
        (GlobalShortcut.ReflectionFocusKey, GlobalShortcut.ReflectionFocusId)
    ];
    private readonly GlobalShortcut?[] registrations = new GlobalShortcut?[Chords.Length];
    private readonly Action[] actions;
    private readonly IHotKeyRegistration? backend;
    private readonly Action<int, bool>? statusChanged;
    private bool disposed;
    internal PreviewShortcuts(Action[] actions, Action<int, bool>? statusChanged = null, IHotKeyRegistration? backend = null)
    {
        if (actions.Length != Chords.Length) throw new ArgumentException("Provide each original shortcut action.");
        this.actions = actions; this.statusChanged = statusChanged; this.backend = backend;
        RetryUnavailable(initial: true);
    }
    internal object Status => registrations.Select((shortcut,id)=>new { id, available = shortcut?.IsRegistered == true }).ToArray();
    internal bool RetryUnavailable(bool initial = false)
    {
        if (disposed) return false;
        var changed = false;
        for (var i = 0; i < Chords.Length; i++) {
            if (registrations[i]?.IsRegistered == true) continue;
            registrations[i]?.Dispose(); registrations[i] = null;
            var index = i;
            try { registrations[i] = new GlobalShortcut(()=>actions[index](), backend, Chords[i].Key, Chords[i].Id); }
            catch { /* A later retry can recover a temporarily unavailable registration. */ }
            var available = registrations[i]?.IsRegistered == true;
            changed |= available;
            if (initial || available) statusChanged?.Invoke(i, available);
        }
        return changed;
    }
    internal bool Dispatch(int message, int id) => !disposed && registrations.Any(shortcut => shortcut?.Dispatch(message, id) == true);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var shortcut in registrations) shortcut?.Dispose();
    }
}
