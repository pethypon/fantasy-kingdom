using System;
using UnityEngine;

/// <summary>Authorable R2 economy policy. Game economy recipes remain the source of resource amounts.</summary>
[Serializable]
public sealed class AIEconomySettings
{
    [Range(1, 8)] public int forecastTurns = 4;
    [Range(1, 8)] public int reserveTurns = 3;
    [Range(1, 8)] public int recoveryWindowTurns = 4;
    [Range(0, 8)] public int breadCoverageMarginTurns = 1;
    [Range(0, 1)] public float plannedDemandWeight = .35f;
    [Min(0)] public float needWeight = 90f;
    [Min(0)] public float coverageWeight = 60f;
    [Min(0)] public float chainRecoveryWeight = 65f;
    [Min(0)] public float reserveRecoveryWeight = 25f;
    [Min(0)] public float overstockPenalty = 100f;
    [Min(0)] public float warningMilitaryPenalty = 40f;
    [Min(0)] public float localMilitarySaturationPenalty = 15f;
    [Min(0)] public float uncoveredWallThreatPenalty = 20f;
    [Range(0, 1)] public float emergencyKingDamageFraction = .4f;
    [Range(0, 1)] public float emergencyCrystalDamageFraction = .75f;
    [Range(0, 1)] public float criticalReserveFraction = .25f;
    public bool enableDecisionLogs;
    [Range(0, 1)] public float sustainedDeficitUrgency = .35f;
    [Min(.001f)] public float deficitEpsilon = .01f;
    [Range(1, 8)] public int plannedUnitLevel = 6;
    [Min(0)] public float plannedBuildActions = 1f;
    [Min(0)] public float plannedSummonActions = 1f;
    [Range(0, 1)] public float explorationSummonWeight = .5f;
    public FacilityData.ProductionBundle minimumOperationalBuffer = new FacilityData.ProductionBundle
    {
        Wood = 20, Stone = 20, Iron = 3, Wheat = 3, Bread = 5, Water = 8, Citizen = 3
    };
    public FacilityData.ProductionBundle plannedBuildCost = new FacilityData.ProductionBundle
    {
        Wood = 40, Stone = 30, Water = 10, Citizen = 1
    };

    static readonly AIEconomySettings defaults = new AIEconomySettings();
    public static AIEconomySettings Active => GameAuthoringRules.Active?.aiEconomy ?? defaults;
    public int BreadReserveTurns => Mathf.Clamp(Mathf.Max(reserveTurns, GameAuthoringRules.Active?.aiBreadReserveTurns ?? 1), 1, 8);
    public float WarningMilitaryBuildPenalty => NonNegative(warningMilitaryPenalty);
    public float LocalWallSaturationPenalty => NonNegative(localMilitarySaturationPenalty);
    public float UncoveredWallThreatPenalty => NonNegative(uncoveredWallThreatPenalty);
    internal float Epsilon => Mathf.Max(.001f, NonNegative(deficitEpsilon));
    internal static float NonNegative(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
}
