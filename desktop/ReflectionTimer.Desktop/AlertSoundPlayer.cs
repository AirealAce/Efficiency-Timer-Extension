using NAudio.Wave;
using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

public enum AlertSoundResult { Played, DefaultFallback, Muted, Cancelled, Failed }

public interface IAlertAudioBackend
{
    Task PlayAsync(string path, AudioLevel level, CancellationToken cancellationToken);
}

public sealed class AudioLevel(int volume)
{
    public int Volume { get; } = Math.Clamp(volume, 0, 100);
    private float multiplier = 1;
    public float Gain => Volume / 100f * Volatile.Read(ref multiplier);
    internal void Duck(bool ducked) => Volatile.Write(ref multiplier, ducked ? .25f : 1f);
}

// Volume changes are read by the audio thread, without touching Windows' mixer
// or device-wide volume. Other apps are never captured, stopped, or attenuated.
internal sealed class LiveGainProvider(ISampleProvider source, AudioLevel level) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public int Read(Span<float> buffer)
    {
        var read = source.Read(buffer); var gain = level.Gain;
        for (var i = 0; i < read; i++) buffer[i] *= gain;
        return read;
    }
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

    public async Task PlayAsync(string path, AudioLevel level, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new AudioFileReader(path);
        using var output = new WaveOut();
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) => stopped.TrySetResult(e.Exception);
        output.Init(new LiveGainProvider(reader, level));
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
    private readonly List<Voice> voices = [];
    private Task stopping = Task.CompletedTask;
    private bool disposed;

    private sealed class Voice(int volume, SoundBehavior behavior, SoundEvent kind)
    {
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly AudioLevel Level = new(volume);
        public readonly SoundBehavior Behavior = behavior;
        public readonly SoundEvent Kind = kind;
        public bool Running;
    }

    public AlertSoundPlayer(IAlertAudioBackend? backend = null, string? defaultPath = null)
    {
        this.backend = backend ?? new Mp3AudioBackend();
        this.defaultPath = defaultPath ?? BundledPath;
    }

    public Task<AlertSoundResult> PlayAsync(string customPath, int volume) =>
        PlayAsync(customPath, volume, SoundBehavior.Disruptive, SoundEvent.SessionEnd);

    public Task<AlertSoundResult> PlayAsync(string path, int volume, SoundBehavior behavior, SoundEvent kind, string? fallback = null)
    {
        lock (gate) {
            if (disposed) return Task.FromResult(AlertSoundResult.Cancelled);
            if (volume <= 0) return Task.FromResult(AlertSoundResult.Muted); // Muted events cannot interrupt audible ones.
            if (!Enum.IsDefined(behavior)) behavior = SoundBehavior.Disruptive;
            if (behavior == SoundBehavior.Disruptive) CancelVoices(voices.ToArray());
            if (voices.Count >= 32) return Task.FromResult(AlertSoundResult.Cancelled);
            var request = new Voice(volume, behavior, kind); voices.Add(request);
            var waitForStops = stopping;
            return Task.Run(() => RunAsync(path, fallback ?? defaultPath, request, waitForStops));
        }
    }

    private async Task<AlertSoundResult> RunAsync(string path, string fallback, Voice request, Task waitForStops)
    {
        var token = request.Cancellation.Token;
        try {
            await waitForStops.WaitAsync(token).ConfigureAwait(false);
            lock (gate) { token.ThrowIfCancellationRequested(); request.Running = true; UpdateGains(); }
            if (!string.IsNullOrEmpty(path)) {
                try {
                    await backend.PlayAsync(path, request.Level, token).ConfigureAwait(false);
                    return AlertSoundResult.Played;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) {
                    token.ThrowIfCancellationRequested();
                    await backend.PlayAsync(fallback, request.Level, token).ConfigureAwait(false);
                    return AlertSoundResult.DefaultFallback;
                }
            }
            await backend.PlayAsync(fallback, request.Level, token).ConfigureAwait(false);
            return AlertSoundResult.Played;
        }
        catch (OperationCanceledException) { return AlertSoundResult.Cancelled; }
        catch (Exception) { return AlertSoundResult.Failed; }
        finally {
            lock (gate) {
                voices.Remove(request); UpdateGains(); request.Cancellation.Dispose(); request.Done.TrySetResult();
            }
        }
    }

    private void UpdateGains()
    {
        var foreground = voices.LastOrDefault(x => x.Running && !x.Cancellation.IsCancellationRequested && x.Behavior == SoundBehavior.Assertive);
        foreach (var voice in voices) voice.Level.Duck(foreground is not null && !ReferenceEquals(voice, foreground));
    }
    private void CancelVoices(Voice[] targets)
    {
        foreach (var voice in targets) voice.Cancellation.Cancel();
        stopping = Task.WhenAll(targets.Select(x => x.Done.Task).Append(stopping));
        UpdateGains();
    }
    public void Stop(SoundEvent? kind = null) { lock (gate) CancelVoices(voices.Where(x => kind is null || x.Kind == kind).ToArray()); }
    public void Dispose() { lock (gate) { disposed = true; CancelVoices(voices.ToArray()); } }
}
