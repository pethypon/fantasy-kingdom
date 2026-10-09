using UnityEngine;

/// <summary>Optional, bounded experience layer. It never changes game rules or strategic priority.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/自己評価・日記設定", fileName = "ReflectionConfig")]
public sealed class AIReflectionConfig : ScriptableObject
{
    public bool EnableReflection = true;
    public bool ApplyLearningToSelection = true;
    public bool EnableConsoleDiary = true;
    public bool EnableFileDiary = true;
    public bool EnableCandidateDebug;
    [Min(1)] public int DiaryIntervalOwnTurns = 15;
    [Min(16)] public int MaxKnowledgeEntries = 2048;
    [Min(16)] public int MaxRecentRecords = 128;
    [Min(64)] public int MaxRewardEvents = 8192;
    [Range(.001f, 1f)] public float ReflectionLearningRate = .2f;
    [Range(.001f, 1f)] public float MinimumLearningRate = .02f;
    public float MinLearnedValue = -5f;
    public float MaxLearnedValue = 5f;
    [Range(0f, 2f)] public float LearningInfluenceMultiplier = 1f;
    // BALANCE_TODO: self-evaluation penalties are provisional and adjustable here only.
    [Min(0f)] public float RepeatedNoProgressPenalty = .25f;
    [Min(0f)] public float OscillationPenalty = .5f;
    [Min(0f)] public float FailedActionPenalty = .25f;
    public float DefeatReward;
    public const float NormalKillReward = 1f, FormationKillReward = 2f;
    public const float ArtifactReward = 1f, EconomyStableReward = 1f, VictoryReward = 100f;

    static AIReflectionConfig active;
    public static AIReflectionConfig Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<AIReflectionConfig>("AI/ReflectionConfig");
            if (active == null)
            {
                active = CreateInstance<AIReflectionConfig>();
                active.hideFlags = HideFlags.HideAndDontSave;
            }
            return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActive() => active = null;
    internal static float Finite(float value, float fallback = 0f)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    internal int EntryLimit => Mathf.Clamp(MaxKnowledgeEntries, 16, 10000);
    internal int RecordLimit => Mathf.Clamp(MaxRecentRecords, 16, 1024);
    internal int EventLimit => Mathf.Clamp(MaxRewardEvents, 64, 65536);
    internal float Lower => Mathf.Clamp(Finite(MinLearnedValue, -5), -100, 0);
    internal float Upper => Mathf.Clamp(Finite(MaxLearnedValue, 5), 0, 100);
}
