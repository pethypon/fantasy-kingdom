using System;
using System.Collections.Generic;

public readonly struct AIActionToken
{
    public readonly long Id;
    public AIActionToken(long id) { Id = id; }
    public bool IsValid => Id > 0;
}

public enum AIFailureReason
{
    None, ExecutionFailed, InvalidTarget, TargetLost, NoDamage, NoProgress,
    RepeatedNoProgress, PositionWorsened, ResourceShortage, EconomyWorsened,
    UnitLost, CrystalExposed, Overextended, FailedRetreat, PoorTargetSelection, ArtifactLost, Other
}
public enum AIStrategicOutcome { Neutral, Progress, Failure }

/// <summary>Value-only observed facts; no Unity objects survive into records or JSON.</summary>
[Serializable]
public sealed class AIActionContextSnapshot
{
    public int Turn;
    public AIActionType ActionType;
    public string Strategy = "Balanced", ActorRole = "None", TargetCategory = "None";
    public float ActorHpRatio, ActorAp, LocalAllyPower, LocalEnemyPower;
    public int ActorHP, ActorX, ActorY, ActorHeight, ActorDirection;
    public int TargetHP = -1, TargetShieldTurns, TargetX, TargetY;
    public bool TargetObserved;
    public int VisibleEnemyCount, VisibleAllyCount, DistanceToTarget, DistanceToObjective;
    public bool HasObjective, OwnCrystalThreatened;
    public float OwnCrystalHpRatio;
    public int OwnCrystalHP;
    public EconomicState EconomyState;
    public int Bread, Wood, Stone, Iron, MagicOre, Wheat, Water, Citizen;
    public int TerritoryCount, OwnUnitCount, OwnBuildingCount, VisibleEnemyUnitCount;
    public int ArtifactCount, CurrentThreatLevel, ExploredTiles, ActorEffectCount, TargetEffectCount;
    public float IncomingDamage;
    public float LocalPowerRatio => LocalAllyPower / Math.Max(1f, LocalEnemyPower);
    public AIActionContextSnapshot Copy() => (AIActionContextSnapshot)MemberwiseClone();
}

[Serializable]
public sealed class AIActionOutcome
{
    public int DamageDealt, DamageTaken, HealingDone, EnemyKills, FormationKills, OwnLosses;
    public int ArtifactsAcquired, TerritoryDelta, CrystalHpDelta, NewTilesRevealed;
    public int DistanceToObjectiveDelta, OwnBuildingDelta, OwnUnitDelta;
    public float LocalPowerRatioDelta;
    public EconomicState EconomyBefore, EconomyAfter;
    public int WoodDelta, StoneDelta, IronDelta, MagicOreDelta, WheatDelta, BreadDelta, WaterDelta, CitizenDelta;
    public bool ShieldReduced, EffectChanged, DirectionChanged;

    public static AIActionOutcome Difference(AIActionContextSnapshot before, AIActionContextSnapshot after)
    {
        var result = new AIActionOutcome();
        if (before == null || after == null) return result;
        if (before.TargetObserved && after.TargetObserved && before.TargetHP >= 0 && after.TargetHP >= 0)
        {
            result.DamageDealt = Math.Max(0, before.TargetHP - after.TargetHP);
            result.HealingDone = Math.Max(0, after.TargetHP - before.TargetHP);
            result.ShieldReduced = after.TargetShieldTurns < before.TargetShieldTurns;
        }
        result.DamageTaken = Math.Max(0, before.ActorHP - after.ActorHP);
        result.HealingDone += Math.Max(0, after.ActorHP - before.ActorHP);
        result.OwnLosses = Math.Max(0, before.OwnUnitCount - after.OwnUnitCount);
        result.OwnUnitDelta = after.OwnUnitCount - before.OwnUnitCount;
        result.OwnBuildingDelta = after.OwnBuildingCount - before.OwnBuildingCount;
        result.TerritoryDelta = after.TerritoryCount - before.TerritoryCount;
        result.CrystalHpDelta = after.OwnCrystalHP - before.OwnCrystalHP;
        result.NewTilesRevealed = Math.Max(0, after.ExploredTiles - before.ExploredTiles);
        result.DistanceToObjectiveDelta = before.HasObjective && after.HasObjective
            ? before.DistanceToObjective - after.DistanceToObjective : 0;
        result.LocalPowerRatioDelta = after.LocalPowerRatio - before.LocalPowerRatio;
        result.EconomyBefore = before.EconomyState; result.EconomyAfter = after.EconomyState;
        result.WoodDelta = after.Wood - before.Wood; result.StoneDelta = after.Stone - before.Stone;
        result.IronDelta = after.Iron - before.Iron; result.MagicOreDelta = after.MagicOre - before.MagicOre;
        result.WheatDelta = after.Wheat - before.Wheat; result.BreadDelta = after.Bread - before.Bread;
        result.WaterDelta = after.Water - before.Water; result.CitizenDelta = after.Citizen - before.Citizen;
        result.DirectionChanged = before.ActorDirection != after.ActorDirection;
        result.EffectChanged = before.ActorEffectCount != after.ActorEffectCount || before.TargetEffectCount != after.TargetEffectCount;
        // Kills and artifact rewards are supplied only by authoritative events, never guessed from count/HP.
        return result;
    }
}

[Serializable]
public sealed class AIActionRecord
{
    public long ActionId;
    public string BattleId, ActorRole, TargetCategory, ContextKey, ActionKey;
    public int Turn;
    public AIActionType ActionType;
    public AIActionContextSnapshot Before, After;
    public AIActionOutcome Outcome;
    public float Reward, BaseScore, LearnedModifier, FinalScore;
    public float EconomyStableReward;
    public bool ExecutionSucceeded, MeaningfulProgress, IsRepeatedAction, IsOscillation;
    public AIFailureReason FailureReason;
    public AIStrategicOutcome StrategicOutcome;
    // Match-local attribution only. It never participates in cross-battle knowledge keys.
    public int ActorRuntimeId;
    public string ActorLifeId;
}

[Serializable]
public sealed class FailureReasonCount
{
    public AIFailureReason Reason;
    public int Count;
}

[Serializable]
public sealed class AIReflectionRewardEvent
{
    public string Id;
    public long ActionId;
    public bool Formation;
}

[Serializable]
public sealed class AIReflectionStrategyUse
{
    public TurnStrategy Strategy;
    public int ThreatBand, OwnTurns;
}

/// <summary>Save-game continuation state, separate from the cross-match learning profile.</summary>
[Serializable]
public sealed class AIReflectionBattleState
{
    public int SchemaVersion = 1;
    public string BattleId, Personality;
    public Team Faction;
    public int OwnTurns, LastStartedTurn = -1, LastEndedTurn = -1, ThreatLevel;
    public long NextActionId;
    public bool Ended, Victory;
    public string Result;
    public bool PersistentLearningAllowed = true;
    public float BattleReward, TurnReward;
    public int DiaryCount;
    public AIReflectionInterval Interval = new AIReflectionInterval();
    public AIReflectionInterval BattleSummary = new AIReflectionInterval();
    public List<AIActionRecord> RecentRecords = new List<AIActionRecord>();
    public List<AIReflectionRewardEvent> Kills = new List<AIReflectionRewardEvent>();
    public List<AIReflectionRewardEvent> Artifacts = new List<AIReflectionRewardEvent>();
    public List<int> StableTurns = new List<int>();
    public int TotalKills, TotalFormationKills, TotalArtifacts, TotalOwnLosses;
    public int TotalStableTurns;
    public AIActionContextSnapshot LastSnapshot;
    public AIActionLearningProfile LearningProfile;
    public List<AIReflectionStrategyUse> StrategyUse = new List<AIReflectionStrategyUse>();
}
