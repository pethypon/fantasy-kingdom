using UnityEngine;

public enum AILogLevel { Off, Error, Summary, Verbose, Trace }

/// <summary>
/// Responsibility: presentation/diagnostic budgets only; never changes AI decisions.
/// Owner: Resources configuration. Lifetime: application. Retained items: one instance.
/// Cleanup: subsystem registration. Failure: validated defaults.
/// </summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/パフォーマンス/長時間AI設定", fileName = "PerformanceConfig")]
public sealed class AIPerformanceConfig : ScriptableObject
{
    [Header("計測と保守（AIの思考能力は変更しません）")]
    public bool EnableInstrumentation = true;
    [Min(1)] public int MaintenanceIntervalOwnTurns = 5;
    [Header("表示の滞留対策 / PERF_TODO")]
    [Min(8)] public int PresentationMaxQueue = 256;
    [Min(1)] public int PresentationHighWaterMark = 128;
    [Min(0)] public int PresentationLowWaterMark = 64;
    [Range(.01f, 1f)] public float CatchUpAnimationMultiplier = .25f;
    [Header("隠し日記 / PERF_TODO")]
    [Min(1)] public int MaxVisibleDiaryEvents = 100;
    [Min(256)] public int MaxDiaryVisibleCharacters = 30000;
    [Range(.1f, 30f)] public float DiaryRefreshHz = 5f;
    [Header("ログ / PERF_TODO")]
    public AILogLevel LogLevel = AILogLevel.Summary;
    [Header("耐久試験の相対基準 / PERF_TODO")]
    [Min(1f)] public float MaximumLateTurnTimeRatio = 1.20f;
    [Min(1)] public int RetainedTurnSamples = 1000;
    static AIPerformanceConfig active;
    public static AIPerformanceConfig Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<AIPerformanceConfig>("AI/PerformanceConfig");
            if (active == null) { active = CreateInstance<AIPerformanceConfig>(); active.hideFlags = HideFlags.HideAndDontSave; }
            return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => active = null;
    public int QueueLimit => Mathf.Clamp(PresentationMaxQueue, 8, 4096);
    public int HighWater => Mathf.Clamp(PresentationHighWaterMark, 1, QueueLimit);
    public int LowWater => Mathf.Clamp(PresentationLowWaterMark, 0, HighWater - 1);
    public int DiaryEventLimit => Mathf.Clamp(MaxVisibleDiaryEvents, 1, 500);
    public int DiaryCharacterLimit => Mathf.Clamp(MaxDiaryVisibleCharacters, 256, 60000);
    public int TurnSampleLimit => Mathf.Clamp(RetainedTurnSamples, 500, 2000);
    public float SafeDiaryRefreshHz => FiniteClamp(DiaryRefreshHz, 5f, .1f, 30f);
    public float SafeAnimationMultiplier => FiniteClamp(CatchUpAnimationMultiplier, .25f, .01f, 1f);
    public float SafeLateTurnRatio => FiniteClamp(MaximumLateTurnTimeRatio, 1.20f, 1f, 10f);
    static float FiniteClamp(float value, float fallback, float minimum, float maximum)
        => Mathf.Clamp(float.IsNaN(value) || float.IsInfinity(value) ? fallback : value, minimum, maximum);
}
