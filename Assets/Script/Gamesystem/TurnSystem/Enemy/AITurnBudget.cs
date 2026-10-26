using System.Diagnostics;

/// <summary>Bounds both active CPU work and wall time, including cooperative frame waits.</summary>
public static class AITurnBudget
{
    static Stopwatch clock;
    static Stopwatch wallClock;
    static double limit;
    public static void Begin(double milliseconds)
    {
        limit = Normalize(milliseconds);
        clock = Stopwatch.StartNew();
        wallClock = Stopwatch.StartNew();
    }
    static double Normalize(double milliseconds) => double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)
        ? 0 : System.Math.Max(0, milliseconds);
    public static double LimitMs => limit;
    public static double ElapsedMs => clock == null ? 0 : System.Math.Max(clock.Elapsed.TotalMilliseconds,
        wallClock?.Elapsed.TotalMilliseconds ?? 0);
    /// <summary>Commit one start-of-turn policy without refunding setup time.</summary>
    public static void SetLimitWithoutRestart(double milliseconds)
    {
        if (clock == null || wallClock == null) { Begin(milliseconds); return; }
        limit = Normalize(milliseconds);
    }
    public static double RemainingMs => clock == null ? 5000 :
        System.Math.Max(0, limit - System.Math.Max(clock.Elapsed.TotalMilliseconds, wallClock.Elapsed.TotalMilliseconds));
    // Waiting does not count as CPU work, but still counts toward the user's wait limit.
    public static void Pause() => clock?.Stop();
    public static void Resume() => clock?.Start();
    public static void SuspendWaitLimit() { clock?.Stop(); wallClock?.Stop(); }
    public static void ResumeWaitLimit() => wallClock?.Start();
    public static bool Expired => RemainingMs <= 0;
}
