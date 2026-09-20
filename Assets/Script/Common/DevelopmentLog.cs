using System.Diagnostics;
public static class DevelopmentLog
{
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(object message) => UnityEngine.Debug.Log(message);
}
