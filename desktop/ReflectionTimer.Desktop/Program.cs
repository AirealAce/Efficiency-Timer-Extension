using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReflectionTimerDesktop");
        var index = Array.IndexOf(args, "--data-dir");
        if (index >= 0 && index + 1 < args.Length) directory = Path.GetFullPath(args[index + 1]);
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory)))[..16];
        using var mutex = new Mutex(true, @"Local\ReflectionTimerDesktop-" + suffix, out var first);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ReflectionTimerDesktopShow-" + suffix);
        if (!first) { show.Set(); return; }
        try
        {
            var store = new EncryptedStore(directory);
            if (args.Contains("--import-connection"))
            {
                // Explicit stdin import for a user-provided connection, never Chrome profile access.
                var connection = JsonSerializer.Deserialize<ConnectionSettings>(Console.In.ReadToEnd(), DataJson.Options) ?? throw new InvalidDataException("No connection supplied.");
                var error = SheetsClient.Validate(connection);
                if (error is not null) throw new InvalidDataException(error);
                var saved = store.Load(); saved.Connection = connection; store.Save(saved);
                return;
            }
            if (args.Contains("--check-connection"))
            {
                using var client = new SheetsClient();
                var reply = client.Ping(store.Load().Connection).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(reply, DataJson.Options));
                Environment.ExitCode = reply.Success ? 0 : 1;
                return;
            }
            AppTheme.Initialize(() => store.Load().Theme);
            using var app = new TimerApplication(store, directory, show);
            Application.ThreadException += (_, _) => app.ShowError("An unexpected app error occurred. Your last committed state is retained. Export diagnostics if this repeats.");
            app.OpenUnlessTray(args.Contains("--tray"));
            Application.Run(app);
        }
        catch (Exception)
        {
            Environment.ExitCode = 1;
            if (args.Contains("--import-connection") || args.Contains("--check-connection"))
            {
                Console.Error.WriteLine("Connection command failed. Check the supplied settings and local data access; no credentials were printed.");
                return;
            }
            MessageBox.Show("Reflection Timer could not open its local data. Nothing has been reset or erased. Check disk access and keep the data files for recovery.",
                "Reflection Timer — unable to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { mutex.ReleaseMutex(); }
    }
}
