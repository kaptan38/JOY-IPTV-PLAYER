namespace JoyIptv;

internal sealed record RecoveryAttempt(int Number, int DelaySeconds);
internal sealed class RecoveryPolicy
{
    private int generation, attempts, tier;
    internal int AutomaticCacheMs => new[] { 1500, 3000, 5000, 9000 }[tier];
    internal void Reset(int value) { generation = value; attempts = 0; tier = 0; }
    internal RecoveryAttempt? TrySchedule(int value)
    {
        if (value != generation || attempts >= 3) return null;
        attempts++; return new RecoveryAttempt(attempts, 1 << attempts);
    }
    internal void ObserveBuffering(TimeSpan duration)
    { if (duration >= TimeSpan.FromSeconds(2)) tier = Math.Min(3, tier + 1); }
    internal void ObserveStable(TimeSpan duration)
    { if (duration >= TimeSpan.FromSeconds(60)) tier = 0; }
}
