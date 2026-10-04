using System.Diagnostics;

/// <summary>Bounds both active CPU work and wall time, including cooperative frame waits.</summary>
public static class AITurnBudget
{
    static Stopwatch clock;
    static Stopwatch wallClock;
    static double limit;
    public static void Begin(double milliseconds)
    {
        limit = double.IsNaN(milliseconds) ? 0 : System.Math.Max(0, milliseconds);
        clock = Stopwatch.StartNew();
        wallClock = Stopwatch.StartNew();
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
