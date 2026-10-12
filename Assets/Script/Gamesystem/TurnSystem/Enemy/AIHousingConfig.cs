using UnityEngine;

/// <summary>Authorable investment policy only. Recipes remain authoritative for costs and capacity gains.</summary>
// Owner/lifetime: Resources application settings; one instance, reset at subsystem registration.
// Failure: missing asset and nonfinite values use validated defaults. No AI actions or collections are retained.
[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/住宅投資の設定", fileName = "HousingConfig")]
public sealed class AIHousingConfig : ScriptableObject
{
    public bool Enabled = true;
    [Range(0f, 1f)] public float ConsiderCitizenRatio = .75f;
    [Range(0f, 1f)] public float UrgentCitizenRatio = .90f;
    [Range(1, 8)] public int ForecastTurns = 5;
    [Min(0)] public int MinimumFreeCitizenTarget = 2;
    [Range(0, 8)] public int ShortTermCombatRecruits = 2;
    [Range(1, 7)] public int EmergencyRecruitWaitTurns = 2;
    [Min(0f)] public float PopulationNeedWeight = 35;
    [Min(0f)] public float FuturePopulationNeedWeight = 65;
    [Min(0f)] public float EconomicStabilityWeight = 15;
    [Min(0f)] public float MilitaryExpansionWeight = 30;
    [Min(0f)] public float ResourceRiskPenalty = 80;
    [Min(0f)] public float EmergencyPenalty = 100;
    [Min(0f)] public float ExcessHousingPenalty = 120;
    [Min(0f)] public float RepeatedHousePenalty = 80;
    public bool EnableDecisionLogs;
    public int Revision { get; private set; }
    void OnValidate() => Revision = Revision < int.MaxValue ? Revision + 1 : 0;
    static AIHousingConfig active;
    public static AIHousingConfig Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<AIHousingConfig>("AI/HousingConfig");
            if (active == null) { active = CreateInstance<AIHousingConfig>(); active.hideFlags = HideFlags.HideAndDontSave; }
            return active;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => active = null;
    public float ConsiderRatio => Mathf.Clamp01(Finite(ConsiderCitizenRatio, .75f));
    public float UrgentRatio => Mathf.Clamp(Finite(UrgentCitizenRatio, .90f), ConsiderRatio, 1f);
    public int Horizon => Mathf.Clamp(ForecastTurns, 1, 8);
    public int EmergencyWait => Mathf.Clamp(EmergencyRecruitWaitTurns, 1, 7);
    public int FreeTarget => Mathf.Clamp(MinimumFreeCitizenTarget, 0, 1000);
    internal static float Weight(float value) => Mathf.Clamp(Finite(value, 0), 0, 10000);
    static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
}

public readonly struct AIHousingAssessment
{
    public readonly bool IsHousing, Needed, EmergencySupply, CapacityBlocked;
    public readonly int FreeCitizens, Capacity, CapacityGain;
    public readonly float UsedSlotRatio, PlannedCitizens, PlannedBuildingCitizens, PlannedRecruitCitizens, PlannedOtherCitizens;
    public readonly float OperationCitizens, MilitaryCitizens, RequiredStock, Score;
    public readonly string Reason;
    internal AIHousingAssessment(bool needed, bool emergency, bool blocked, int free, int capacity, int gain,
        float used, float planned, float buildingCitizens, float recruitCitizens, float otherCitizens,
        float operations, float military, float required, float score, string reason)
    {
        IsHousing = true; Needed = needed; EmergencySupply = emergency; CapacityBlocked = blocked;
        FreeCitizens = free; Capacity = capacity; CapacityGain = gain; UsedSlotRatio = used;
        PlannedCitizens = planned; OperationCitizens = operations; MilitaryCitizens = military;
        PlannedBuildingCitizens = buildingCitizens; PlannedRecruitCitizens = recruitCitizens;
        PlannedOtherCitizens = otherCitizens;
        RequiredStock = required; Score = score; Reason = reason;
    }
    public string Describe() => $"空き市民 {FreeCitizens}/{Capacity} / 使用済枠 {UsedSlotRatio:P0} / 容量増加 +{CapacityGain} / "
        + $"予定需要 {PlannedCitizens:F1} (建築 {PlannedBuildingCitizens:F1}・召喚 {PlannedRecruitCitizens:F1}・外部計画 {PlannedOtherCitizens:F1}) / 作戦需要 {OperationCitizens:F1} / 軍備需要 {MilitaryCitizens:F1} / "
        + $"必要な空き市民 {RequiredStock:F1} / 防衛補充 {EmergencySupply} / 評価 {Score:F1} / 理由 {ReasonText} ({Reason})";
    public string ReasonText => Reason switch
    {
        "housing_capacity_sufficient" => "既存容量で計画を満たせる",
        "housing_existing_growth_can_recover_stock" => "既存容量で補充を待てる。住宅では補充速度が増えない",
        "housing_supply_recovery_first" => "基礎資源・食糧の回復を先に行う",
        "housing_build_forecast_unsafe" => "建築後の必須支払い・生産継続が危険",
        "housing_direct_defense_first" => "容量不足でないため直接防衛を優先する",
        "housing_defense_recruit_forecast_unsafe" => "住宅後の防衛兵の制作・維持を安全に支払えない",
        "housing_defense_cannot_wait" => "市民の補充を待つ間に王・クリスタルが倒される危険がある",
        "housing_defense_capacity_blocked" => "防衛用の駒が市民容量不足で作れない",
        "housing_planned_capacity_shortfall" => "作戦・拡張予定が現在の容量を超える",
        "housing_no_capacity_gain" => "正式データの容量増加がない",
        _ => "未評価"
    };
}
