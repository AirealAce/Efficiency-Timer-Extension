using ReflectionTimer.Core;

namespace ReflectionTimer.Desktop;

// Presentation only: showing the configured duration again must not reset the
// engine, alter actual time, repeat a session, or dismiss its reflection.
internal sealed class CountdownPresentation
{
    private TimerState? previous;
    private long? zeroUntil;
    internal int Seconds(TimerState timer, long now)
    {
        var remaining = TimerEngine.Remaining(timer, now);
        var finished = !timer.IsRunning && remaining == 0 && !TimerEngine.IsPaused(timer);
        if (!finished) zeroUntil = null;
        else if (previous is not null && (previous.IsRunning || previous.RemainingSeconds > 0)) zeroUntil = now + 1000;
        previous = timer;
        return finished && !(now < zeroUntil) ? timer.DurationSeconds : remaining;
    }
}
