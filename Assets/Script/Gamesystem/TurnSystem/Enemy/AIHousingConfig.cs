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
    [Header("実行予定の行動と市民AP（ゲーム側のAP上限・計算を使用）")]
    public bool EnableAPCapacityPlanning = true;
    [Range(1, 64), Tooltip("同じ駒の移動は１手、継続可能な攻撃は２手まで数えます。住宅のために常時最大APを要求しません。")]
    public int APPlanningActionLimit = 32;
    [Range(1, 40), Tooltip("市民が実際に回復するまで待てる期間。この期間で到達できないAP容量を先行投資しません。")]
    public int APPreparationTurns = 10;
    [InspectorName("部隊行動と同時に行う増援のAPも準備する")] public bool EnableRecruitmentAPPlanning;
    [InspectorName("連続攻撃のAPも準備する")] public bool EnableRepeatAttackAPPlanning = true;
    [InspectorName("届く市民数まで段階的に住宅を準備する")] public bool EnableIncrementalAPPreparation;
    [Header("中長期の軍備計画（軍隊数の上限ではなく補充目標）")]
    [InspectorName("作戦に応じて中長期の補充目標を増やす")] public bool EnableDevelopmentGrowth = true;
    [Range(1, 100), InspectorName("先行準備の期間（ターン）"), Tooltip("この期間内に必要になる兵種について、購入に必要な市民容量を先に準備します。費用全額を一括予約しません。")]
    public int DevelopmentPlanningTurns = 20;
    [Range(2, 12), InspectorName("平時の常備戦闘員目標")] public int MinimumStandingFighters = 4;
    [Range(1, 100), InspectorName("常備軍を整え始めるターン")] public int StandingArmyStartTurn = 10;
    [Range(1, 200), InspectorName("軍備拡張を考え始めるターン")] public int DevelopmentGrowthStartTurn = 20;
    [Range(1, 100), InspectorName("軍備目標を見直す間隔")] public int DevelopmentGrowthIntervalTurns = 20;
    [Range(0, 8), InspectorName("間隔ごとの追加補充目標")] public int DevelopmentFightersPerInterval = 1;
    [Range(2, 64), InspectorName("中長期の戦闘員目標（全軍上限ではない）")] public int DevelopmentMaximumFighters = 12;
    [InspectorName("兵種ごとの将来需要"), Tooltip("必要な兵種・数・必要時期。現存数を差し引き、職種上限と合法な定義を守ります。空なら既存の役割補充を使います。")]
    public AIArmyDevelopmentTarget[] DevelopmentRoster = System.Array.Empty<AIArmyDevelopmentTarget>();
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
    void OnValidate()
    {
        Revision = Revision < int.MaxValue ? Revision + 1 : 0;
        GameContentRevision.NotifyChanged();
    }
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
    static void ResetRuntimeCache() => active = null;
    public float ConsiderRatio => Mathf.Clamp01(Finite(ConsiderCitizenRatio, .75f));
    public float UrgentRatio => Mathf.Clamp(Finite(UrgentCitizenRatio, .90f), ConsiderRatio, 1f);
    public int Horizon => Mathf.Clamp(ForecastTurns, 1, 8);
    public int EmergencyWait => Mathf.Clamp(EmergencyRecruitWaitTurns, 1, 7);
    public int FreeTarget => Mathf.Clamp(MinimumFreeCitizenTarget, 0, 1000);
    public int DevelopmentHorizon => Mathf.Clamp(DevelopmentPlanningTurns, 1, 100);
    public int APActionLimit => Mathf.Clamp(APPlanningActionLimit, 1, 64);
    public int APPreparationHorizon => Mathf.Clamp(APPreparationTurns, 1, 40);
    internal static float Weight(float value) => Mathf.Clamp(Finite(value, 0), 0, 10000);
    static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
}

[System.Serializable]
public sealed class AIArmyDevelopmentTarget
{
    [InspectorName("兵種")] public Kind Kind = Kind.Knight;
    [Range(0, 64), InspectorName("必要な現存数")] public int DesiredCount = 1;
    [Range(1, 1000), InspectorName("必要になるターン")] public int NeededFromTurn = 20;
}

public readonly struct AIHousingAssessment
{
    public readonly bool IsHousing, Needed, EmergencySupply, CapacityBlocked;
    public readonly int FreeCitizens, Capacity, CapacityGain;
    public readonly float UsedSlotRatio, PlannedCitizens, PlannedBuildingCitizens, PlannedRecruitCitizens, PlannedOtherCitizens;
    public readonly float OperationCitizens, MilitaryCitizens, RequiredStock, Score;
    public readonly string Reason;
    public readonly AIHousingAPPlan APPlan;
    public readonly FacilityData.ResourceCost Cost;
    internal AIHousingAssessment(bool needed, bool emergency, bool blocked, int free, int capacity, int gain,
        float used, float planned, float buildingCitizens, float recruitCitizens, float otherCitizens,
        float operations, float military, float required, float score, string reason, AIHousingAPPlan apPlan = default,
        FacilityData.ResourceCost cost = default)
    {
        IsHousing = true; Needed = needed; EmergencySupply = emergency; CapacityBlocked = blocked;
        FreeCitizens = free; Capacity = capacity; CapacityGain = gain; UsedSlotRatio = used;
        PlannedCitizens = planned; OperationCitizens = operations; MilitaryCitizens = military;
        PlannedBuildingCitizens = buildingCitizens; PlannedRecruitCitizens = recruitCitizens;
        PlannedOtherCitizens = otherCitizens;
        RequiredStock = required; Score = score; Reason = reason;
        APPlan = apPlan;
        Cost = cost;
    }
    public string Describe() => $"空き市民 {FreeCitizens}/{Capacity} / 使用済枠 {UsedSlotRatio:P0} / 容量増加 +{CapacityGain} / "
        + $"予定需要 {PlannedCitizens:F1} (建築 {PlannedBuildingCitizens:F1}・召喚 {PlannedRecruitCitizens:F1}・外部計画 {PlannedOtherCitizens:F1}) / 作戦需要 {OperationCitizens:F1} / 軍備需要 {MilitaryCitizens:F1} / "
        + $"必要な空き市民 {RequiredStock:F1} / {APPlan.Describe()} / 費用 木{Cost.Wood} 石{Cost.Stone} 鉄{Cost.Iron} 魔石{Cost.MagicOre} 水{Cost.Water} 市民{Cost.Citizen} / "
        + $"防衛補充 {EmergencySupply} / 評価 {Score:F1} / 理由 {ReasonText} ({Reason})";
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
        "housing_ap_capacity_shortfall" => "合法な予定行動のAPが現在の市民容量で届かない",
        "housing_ap_food_reserve_unsafe" => "AP用の市民が回復した後の食糧留保が不足する",
        _ => "未評価"
    };
}

/// <summary>Conditional next-turn demand over current legal geometry, never an executable reservation or extra AP.</summary>
public readonly struct AIHousingAPPlan
{
    public readonly int PlannedActions, DesiredAP, StockAP, CapacityAP, RequiredCitizens, GrowthWaitTurns;
    public readonly string Reason;
    internal AIHousingAPPlan(int actions, int desired, int stockAP, int capacityAP, int required, int wait, string reason)
    { PlannedActions = actions; DesiredAP = desired; StockAP = stockAP; CapacityAP = capacityAP; RequiredCitizens = required; GrowthWaitTurns = wait; Reason = reason; }
    public string Describe() => $"次ターン予定 {PlannedActions}手 / AP {StockAP}・目標 {DesiredAP}・容量時 {CapacityAP} / AP用市民 {RequiredCitizens}・回復待ち {GrowthWaitTurns}T ({Reason})";
}
