using NAudio.Wave;

namespace ReflectionTimer.Desktop;

public enum AlertSoundResult { Played, DefaultFallback, Muted, Cancelled, Failed }

public interface IAlertAudioBackend
{
    Task PlayAsync(string path, int volume, CancellationToken cancellationToken);
}

public sealed class Mp3AudioBackend : IAlertAudioBackend
{
    public static string ValidateCustomFile(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a local MP3 file.");
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new ArgumentException("That MP3 file is no longer available. Choose it again.");
        if (new FileInfo(fullPath).Length is <= 0 or > 50 * 1024 * 1024)
            throw new ArgumentException("Choose a non-empty MP3 file no larger than 50 MB.");
        try {
            using var reader = new AudioFileReader(fullPath);
            if (reader.TotalTime <= TimeSpan.Zero || reader.Read(new byte[4096], 0, 4096) == 0)
                throw new InvalidDataException();
        } catch (Exception) { throw new ArgumentException("That file could not be decoded as audio. Choose another MP3."); }
        return fullPath;
    }

    public async Task PlayAsync(string path, int volume, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new AudioFileReader(path) { Volume = Math.Clamp(volume, 0, 100) / 100f };
        using var output = new WaveOut();
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) => stopped.TrySetResult(e.Exception);
        output.Init(reader);
        cancellationToken.ThrowIfCancellationRequested();
        output.Play();
        using var registration = cancellationToken.Register(() => {
            try { output.Stop(); }
            catch (Exception error) { stopped.TrySetResult(error); }
        });
        // Wait for the audio thread before disposing the decoder/device, even
        // after Stop. Neither playback nor decoding blocks the timer's UI thread.
        var failure = await stopped.Task.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is not null) throw new IOException("Audio playback failed.", failure);
    }
}

public sealed class AlertSoundPlayer : IDisposable
{
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "popup.mp3");
    private readonly IAlertAudioBackend backend;
    private readonly string defaultPath;
    private readonly object gate = new();
    private readonly SemaphoreSlim serial = new(1, 1);
    private CancellationTokenSource? current;
    private bool disposed;

    public AlertSoundPlayer(IAlertAudioBackend? backend = null, string? defaultPath = null)
    {
        this.backend = backend ?? new Mp3AudioBackend();
        this.defaultPath = defaultPath ?? BundledPath;
    }

    public Task<AlertSoundResult> PlayAsync(string customPath, int volume)
    {
        lock (gate) {
            if (disposed) return Task.FromResult(AlertSoundResult.Cancelled);
            current?.Cancel();
            var request = current = new CancellationTokenSource();
            return Task.Run(() => RunAsync(customPath, Math.Clamp(volume, 0, 100), request));
        }
    }

    private async Task<AlertSoundResult> RunAsync(string customPath, int volume, CancellationTokenSource request)
    {
        var acquired = false;
        try {
            await serial.WaitAsync(request.Token).ConfigureAwait(false); acquired = true;
            request.Token.ThrowIfCancellationRequested();
            if (volume == 0) return AlertSoundResult.Muted;
            if (!string.IsNullOrEmpty(customPath)) {
                try {
                    await backend.PlayAsync(customPath, volume, request.Token).ConfigureAwait(false);
                    return AlertSoundResult.Played;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) {
                    request.Token.ThrowIfCancellationRequested();
                    await backend.PlayAsync(defaultPath, volume, request.Token).ConfigureAwait(false);
                    return AlertSoundResult.DefaultFallback;
                }
            }
            await backend.PlayAsync(defaultPath, volume, request.Token).ConfigureAwait(false);
            return AlertSoundResult.Played;
        }
        catch (OperationCanceledException) { return AlertSoundResult.Cancelled; }
        catch (Exception) { return AlertSoundResult.Failed; }
        finally {
            if (acquired) serial.Release();
            lock (gate) { if (ReferenceEquals(current, request)) current = null; request.Dispose(); }
        }
    }

    public void Stop() { lock (gate) current?.Cancel(); }
    public void Dispose() { lock (gate) { disposed = true; current?.Cancel(); } }
}
