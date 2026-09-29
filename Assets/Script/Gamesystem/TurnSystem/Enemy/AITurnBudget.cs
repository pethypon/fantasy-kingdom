using System.Diagnostics;
public static class AITurnBudget
{
    static Stopwatch clock;
    static double limit;
    public static void Begin(double milliseconds) { limit = System.Math.Max(0,milliseconds); clock = Stopwatch.StartNew(); }
    public static double RemainingMs => clock == null ? 5000 : System.Math.Max(0,limit-clock.Elapsed.TotalMilliseconds);
    public static void Pause() => clock?.Stop();
    public static void Resume() => clock?.Start();
    public static bool Expired => RemainingMs <= 0;
}
