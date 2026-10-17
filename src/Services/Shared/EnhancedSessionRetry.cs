namespace ExHyperV.Services;

// A guest may advertise enhanced mode before its logon service is ready.
// Retry twice per availability period, without reconnecting continuously on a permanent failure.
internal sealed class EnhancedSessionRetry
{
    private int _scheduledRetries;
    private DateTimeOffset? _nextAttempt;

    internal bool IsDue(DateTimeOffset now) => _nextAttempt is { } due && now >= due;
    internal void Consume() => _nextAttempt = null;

    internal void Schedule(DateTimeOffset now)
    {
        _nextAttempt = _scheduledRetries < 2
            ? now.AddSeconds(5 * ++_scheduledRetries)
            : null;
    }

    internal void Reset()
    {
        _scheduledRetries = 0;
        _nextAttempt = null;
    }
}
