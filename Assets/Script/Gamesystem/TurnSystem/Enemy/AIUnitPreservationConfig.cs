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
    static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
}
