using System;
using System.Collections.Generic;

/// <summary>Battle-local value facts captured by the observed board. No Unity objects or hidden current enemy state.</summary>
[Serializable]
public sealed class AIOperationContext
{
    public int Turn;
    public float OwnMilitaryPower, EnemyObservedPower;
    public bool EnemyStrengthKnown;
    public int OwnUnits, VisibleEnemyUnits;
    public float OwnCrystalHpRatio;
    public bool OwnCrystalThreatened, OwnCrystalDestroyed;
    public EconomicState EconomyState;
    public int TerritoryCount, ArtifactCount;
    public bool ObjectiveKnown, ObjectiveHpKnown, TargetObserved, ObjectiveThreatened;
    public string ObjectiveLifeId;
    public float ObjectiveHpRatio = -1f;
    public int ObjectiveDistance = -1;
    public bool ConfirmedTargetDestroyed, TargetForceConfirmedEliminated;
    public bool ConfirmedDungeonCleared, EnemyThreatResolved;
    public bool ConfirmedResourceAreaSecured;
    public int ConfirmedArtifactAcquisitions;
    public float BeliefUncertainty;
    public int Bread, Wood, Stone, Iron, Water, Wheat, MagicOre, Citizen;
    public float ArmyUtilization;
    public int AssignedUnitCount, UtilizedAssignedUnitCount;
    public int OwnLosses, EnemyKills, ExploredTiles, NewEnemyContacts, KnownImportantFacilities;
    // Cumulative, event-backed losses are not inferred from enemies disappearing into Fog.
    public float OwnLossPower, EnemyKilledPower;
    public bool DiversionConfirmed, RouteOpened, FlankConfirmed, SurroundConfirmed;
    public bool EnemyRetreatConfirmed, EnemyDelayConfirmed;
    public float ObjectiveProgress, PreparationContribution, ExploitationProgress, ForecastRisk;
    public float ResourceInvestment, ResourceSpendValue, EconomyProductionValue, ApSpent;
    public List<string> OwnUnitLifeIds = new List<string>();
    public List<string> VisibleEnemyLifeIds = new List<string>();
    public List<AIOperationObservedUnit> ObservedEnemies = new List<AIOperationObservedUnit>();
    public AIOperationContext Copy()
    {
        var copy = (AIOperationContext)MemberwiseClone();
        copy.OwnUnitLifeIds = OwnUnitLifeIds == null ? new List<string>() : new List<string>(OwnUnitLifeIds);
        copy.VisibleEnemyLifeIds = VisibleEnemyLifeIds == null ? new List<string>() : new List<string>(VisibleEnemyLifeIds);
        copy.ObservedEnemies = new List<AIOperationObservedUnit>();
        if (ObservedEnemies != null) foreach (var enemy in ObservedEnemies) if (enemy != null) copy.ObservedEnemies.Add(enemy.Copy());
        return copy;
    }
}

[Serializable]
public sealed class AIOperationObservedUnit
{
    public string LifeId, Category;
    public int X, Z;
    public float Power;
    public bool ThreatensObjective;
    public AIOperationObservedUnit Copy() => (AIOperationObservedUnit)MemberwiseClone();
}
