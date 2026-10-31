using UnityEngine;

/// <summary>AI risk policy only. Does not alter HP, attacks, AP or recovery rules.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/負傷兵の保全設定", fileName = "UnitPreservationConfig")]
public sealed class AIUnitPreservationConfig : ScriptableObject
{
    public bool Enabled = true;
    [Range(.05f, .75f)] public float WoundedHpRatio = .35f;
    [Range(.35f, 1f)] public float RejoinHpRatio = .60f;
    [Range(1, 2)] public int ObservedExchangeWaves = 2;
    [Range(1, 128)] public int MaximumTrackedUnits = 128;
    [Header("Bounded retreat policy (AI tuning only)")]
    [Range(1, 6)] public int RetreatMaximumSteps = 4;
    [Range(16, 256)] public int RetreatMaximumNodes = 96;
    [Range(0, 1)] public int RetreatFutureOwnTurns = 1;
    [Range(0, .5f)] public float RefugeMaximumHpRiskRatio = .10f;
    [Range(0, 1)] public float RejoinBarrierRatio = .40f;
    [Range(1, 12)] public int MaximumRecoveryIdleTurns = 8;
    public int Revision { get; private set; }
    void OnValidate() => Revision = Revision < int.MaxValue ? Revision + 1 : 0;
    static AIUnitPreservationConfig active;
    static bool ownsActive;
    public static AIUnitPreservationConfig Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<AIUnitPreservationConfig>("AI/UnitPreservationConfig");
            if (active == null)
            { active = CreateInstance<AIUnitPreservationConfig>(); active.hideFlags = HideFlags.HideAndDontSave; ownsActive = true; }
            return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        if (ownsActive && active != null) Object.DestroyImmediate(active);
        active = null; ownsActive = false;
    }
    public float WoundedRatio => Mathf.Clamp(Finite(WoundedHpRatio, .35f), .05f, .75f);
    public float RejoinRatio => Mathf.Clamp(Finite(RejoinHpRatio, .6f), WoundedRatio + .05f, 1);
    public int Waves => Mathf.Clamp(ObservedExchangeWaves, 1, 2);
    public int Limit => Mathf.Clamp(MaximumTrackedUnits, 1, 128);
    public int RouteDepth => Mathf.Clamp(RetreatMaximumSteps, 1, 6);
    public int RouteNodes => Mathf.Clamp(RetreatMaximumNodes, 16, 256);
    public int RouteFutureTurns => Mathf.Clamp(RetreatFutureOwnTurns, 0, 1);
    public float RefugeRiskRatio => Mathf.Clamp(Finite(RefugeMaximumHpRiskRatio, .1f), 0, .5f);
    public float BarrierRejoinRatio => Mathf.Clamp01(Finite(RejoinBarrierRatio, .4f));
    public int RecoveryIdleTurns => Mathf.Clamp(MaximumRecoveryIdleTurns, 1, 12);
    static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
}
