using System.Diagnostics;
public static class DevelopmentLog
{
    /// <summary>Check before formatting strings. Verbose hot-path messages are disabled by default.</summary>
    public static bool IsEnabled(AILogLevel level = AILogLevel.Verbose)
    {
        try { return level != AILogLevel.Off && AIPerformanceConfig.Active.LogLevel >= level; }
        catch (System.Exception) { return false; } // Diagnostics must never interrupt an action.
    }
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(object message) { if (IsEnabled()) UnityEngine.Debug.Log(message); }
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Summary(object message) { if (IsEnabled(AILogLevel.Summary)) UnityEngine.Debug.Log(message); }
}
