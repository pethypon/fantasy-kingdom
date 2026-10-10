using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIReflectionArmyRole { Attack, Defense, Reserve, Scout, Flank, Dungeon, Escort, Support }

[Serializable]
public sealed class AIArmyRoleAnnotation
{
    public string ActorLifeId;
    public AIReflectionArmyRole Role;
    public bool Utilized;
}

/// <summary>Own and observed/belief values collected once at turn end; also accepts deterministic fixtures.</summary>
[Serializable]
public sealed class AIArmyTurnEvidence
{
    public int OwnTurn, UnitCount, EnemyUnitCount, UtilizedUnitCount;
    public float OwnPower, EnemyPower, Upkeep;
    public EconomicState EconomyState;
    public bool EnemyStrengthKnown, HasObjective, CrystalThreatened, ReserveNecessary, DefenseValue, PreserveAP;
    public bool AttackOpportunity, EnemyFar, ArmyNearBase;
    public bool CombatParticipation, ObjectiveProgress, ScoutActivity, SupportActivity;
    public List<AIArmyRoleAnnotation> Roles = new List<AIArmyRoleAnnotation>();
    public float PowerRatio => OwnPower / Math.Max(1, EnemyPower);
    public float UtilizationRatio => UnitCount > 0 ? Mathf.Clamp01(UtilizedUnitCount / (float)UnitCount) : 0;
    internal AIArmyTurnEvidence CopyValues() => (AIArmyTurnEvidence)MemberwiseClone();
}

[Serializable]
public sealed class AIArmyUtilizationSample
{
    public int OwnTurn, UnitCount, UtilizedUnitCount;
    public float Upkeep;
    public EconomicState EconomyState;
    public bool ObjectiveProgress;
}

[Serializable]
public sealed class AIArmyUtilizationState
{
    public int Version = 1, LastOwnTurn = -1, IdleOwnTurns, MissedOwnTurns, TurtleOwnTurns;
    public List<AIArmyUtilizationSample> History = new List<AIArmyUtilizationSample>();
}

public sealed class AIArmyUtilizationResult
{
    public AIRewardBreakdown Breakdown = new AIRewardBreakdown();
    public List<AIFailureReason> Reasons = new List<AIFailureReason>();
    public bool IdleArmy, ExcessiveProduction, MissedOpportunity, UnnecessaryTurtle;
    public float UtilizationRatio, OwnPower, EnemyPower;
    public int IdleOwnTurns, MissedOwnTurns;
    public float Reward => Breakdown.Total;
}

/// <summary>Turn-level evidence analysis. It annotates roles without changing assignments, rules or candidates.</summary>
public sealed class AIArmyUtilizationAnalyzer
{
    readonly AIReflectionConfig config;
    AIArmyUtilizationState state = new AIArmyUtilizationState();
    readonly HashSet<string> utilized = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<int> utilizedRuntime = new HashSet<int>();
    readonly HashSet<int> visibleEnemyIds = new HashSet<int>();
    readonly Dictionary<string, AIReflectionArmyRole> roles = new Dictionary<string, AIReflectionArmyRole>(StringComparer.Ordinal);
    int ProductionWindow => Mathf.Clamp(config.ArmyProductionWindowTurns, 1, 32);
    public AIArmyUtilizationAnalyzer(AIReflectionConfig config = null)
        => this.config = config != null ? config : AIReflectionConfig.Active;

    public AIArmyTurnEvidence Capture(AIBoardState board, IEnumerable<AIActionRecord> records, TurnStrategy strategy, int ownTurn)
    {
        var evidence = new AIArmyTurnEvidence { OwnTurn = ownTurn };
        if (board == null) return evidence;
        utilized.Clear(); utilizedRuntime.Clear(); roles.Clear(); visibleEnemyIds.Clear();
        evidence.EconomyState = board.ProductionDemand?.State ?? EconomicState.Healthy;
        evidence.CrystalThreatened = board.Governor?.Mode == StrategicMode.EmergencyDefense;
        var goal = board.Governor?.Objective;
        evidence.HasObjective = goal != null && goal.Active && !goal.Suspended;
        float defenseRadius = Mathf.Max(1, AIReflectionConfig.Finite(config.ArmyDefenseRadius, 6));
        float closestEnemy = float.PositiveInfinity;
        int atBase = 0;
        foreach (var unit in board.AliveEnemyUnits)
        {
            if (!ArmyUnit(unit) || unit.team != board.ActorTeam) continue;
            evidence.UnitCount++; evidence.OwnPower += Power(unit);
            if (Distance(unit.transform.position, board.EnemyCrystalPos) <= defenseRadius) atBase++;
            var definition = unit.GrowthData != null ? unit.GrowthData : board.ResolveUnitDefinition(unit.kind);
            if (definition != null)
            {
                var upkeep = definition.GetUpkeep(unit.Level);
                evidence.Upkeep += (float)Math.Max(0, upkeep.Bread) + Math.Max(0, upkeep.Iron) + Math.Max(0, upkeep.Wood)
                    + Math.Max(0, upkeep.Stone) + Math.Max(0, upkeep.MagicOre) + Math.Max(0, upkeep.Water);
            }
        }
        foreach (var enemy in board.AlivePlayerUnits)
        {
            if (enemy == null || !enemy.IsAlive || !board.IsVisibleToEnemy(enemy.transform.position)) continue;
            visibleEnemyIds.Add(enemy.GetInstanceID()); evidence.EnemyPower += Power(enemy);
            if (enemy.type == Type.Unit) evidence.EnemyUnitCount++;
            closestEnemy = Mathf.Min(closestEnemy, Distance(enemy.transform.position, board.EnemyCrystalPos));
        }
        // Stale contacts contribute only last observed attack power, never their current hidden HP/transform.
        bool beliefReserve = false;
        foreach (var contact in board.Belief.Contacts)
        {
            if (visibleEnemyIds.Contains(contact.Id)) continue;
            evidence.EnemyPower += Mathf.Max(0, contact.ObservedAttackPower);
            foreach (var cell in contact.Cells)
                if (cell.Value > 0 && Distance(cell.Key, board.EnemyCrystalPos) <= defenseRadius * 2) beliefReserve = true;
        }
        // An unobserved opponent cannot establish a safe advantage or a missed legal attack.
        evidence.EnemyStrengthKnown = visibleEnemyIds.Count > 0 && evidence.EnemyPower > 0;
        evidence.ArmyNearBase = evidence.UnitCount > 0 && atBase / (float)evidence.UnitCount
            >= Mathf.Clamp01(AIReflectionConfig.Finite(config.ArmyAtBaseFraction, .6f));
        evidence.EnemyFar = evidence.EnemyStrengthKnown && closestEnemy > Mathf.Max(defenseRadius, AIReflectionConfig.Finite(config.ArmyEnemyFarDistance, 12));
        evidence.ReserveNecessary = evidence.ArmyNearBase && (beliefReserve || closestEnemy <= defenseRadius * 2);
        evidence.DefenseValue = evidence.CrystalThreatened || evidence.ArmyNearBase
            && (closestEnemy <= defenseRadius || evidence.EnemyStrengthKnown && evidence.OwnPower <= evidence.EnemyPower);

        if (records != null) foreach (var record in records)
        {
            if (record == null || record.Turn != board.TurnCount || !record.ExecutionSucceeded) continue;
            var outcome = record.Outcome;
            bool combat = outcome != null && (outcome.DamageDealt > 0 || outcome.EnemyKills > 0);
            bool objective = outcome != null && outcome.DistanceToObjectiveDelta > 0;
            bool scout = outcome != null && (outcome.NewTilesRevealed > 0 || outcome.NewEnemyLifeIds?.Count > 0);
            bool support = outcome != null && (outcome.HealingDone > 0 || outcome.EffectChanged || outcome.ShieldReduced
                || outcome.ShieldGranted || outcome.ApRecovered > 0);
            bool defense = record.Before != null && record.After != null && record.Before.OwnCrystalThreatened
                && (!record.After.OwnCrystalThreatened || record.After.OwnCrystalHP > record.Before.OwnCrystalHP || combat);
            evidence.CombatParticipation |= combat; evidence.ObjectiveProgress |= objective;
            evidence.ScoutActivity |= scout; evidence.SupportActivity |= support; evidence.DefenseValue |= defense;
            if (combat || objective || scout || support || defense)
            {
                if (!string.IsNullOrEmpty(record.ActorLifeId)) utilized.Add(record.ActorLifeId);
                if (record.ActorRuntimeId != 0) utilizedRuntime.Add(record.ActorRuntimeId);
            }
            if (record.Before?.CanAttackObservedEnemy == true) evidence.AttackOpportunity = true;
            if (!string.IsNullOrEmpty(record.ActorLifeId))
            {
                if (record.ActionType == AIActionType.Surround) roles[record.ActorLifeId] = AIReflectionArmyRole.Flank;
                else if (record.ActionType == AIActionType.Support) roles[record.ActorLifeId] = AIReflectionArmyRole.Support;
                else if (record.ActionType == AIActionType.DefenseRepos) roles[record.ActorLifeId] = AIReflectionArmyRole.Defense;
            }
        }
        bool affordableOpportunity = false;
        foreach (var unit in board.AliveEnemyUnits)
        {
            if (!ArmyUnit(unit) || unit.team != board.ActorTeam) continue;
            bool used = utilized.Contains(unit.ReflectionLifeId) || utilizedRuntime.Contains(unit.GetInstanceID());
            var role = unit.kind == Kind.Scout ? AIReflectionArmyRole.Scout
                : unit.kind == Kind.Priest ? AIReflectionArmyRole.Support : AIReflectionArmyRole.Attack;
            if (roles.TryGetValue(unit.ReflectionLifeId, out var actionRole)) role = actionRole;
            if (Distance(unit.transform.position, board.EnemyCrystalPos) <= defenseRadius && (evidence.DefenseValue || evidence.ReserveNecessary))
            { used = true; role = evidence.DefenseValue ? AIReflectionArmyRole.Defense : AIReflectionArmyRole.Reserve; }
            if (used) evidence.UtilizedUnitCount++;
            if (evidence.Roles.Count < 512) evidence.Roles.Add(new AIArmyRoleAnnotation { ActorLifeId = unit.ReflectionLifeId, Role = role, Utilized = used });
            if (!affordableOpportunity && CanProgress(board, unit, goal)) affordableOpportunity = true;
        }
        evidence.AttackOpportunity |= affordableOpportunity;
        evidence.PreserveAP = board.EnemyAP <= 0 || evidence.EconomyState >= EconomicState.Crisis
            || board.Governor?.Mode == StrategicMode.EconomicRecovery;
        return evidence;
    }

    public AIArmyUtilizationResult EndTurn(int ownTurn, AIArmyTurnEvidence evidence)
    {
        var result = new AIArmyUtilizationResult();
        if (evidence == null || ownTurn < 0 || ownTurn <= state.LastOwnTurn) return result;
        evidence = evidence.CopyValues();
        Sanitize(evidence);
        result.OwnPower = evidence.OwnPower; result.EnemyPower = evidence.EnemyPower; result.UtilizationRatio = evidence.UtilizationRatio;
        bool safeToJudge = evidence.UnitCount > 0 && evidence.EnemyStrengthKnown && evidence.EnemyPower > 0
            && !evidence.CrystalThreatened && !evidence.DefenseValue && !evidence.ReserveNecessary;
        bool strong = evidence.PowerRatio >= Mathf.Max(1, AIReflectionConfig.Finite(config.ArmyStrongPowerRatio, 1.5f));
        bool useful = evidence.CombatParticipation || evidence.ObjectiveProgress || evidence.ScoutActivity || evidence.SupportActivity;
        bool idle = safeToJudge && !evidence.PreserveAP && strong && evidence.HasObjective && !useful;
        bool opportunity = idle && evidence.AttackOpportunity && evidence.EconomyState < EconomicState.Crisis;
        bool turtle = idle && evidence.ArmyNearBase && evidence.EnemyFar && evidence.EconomyState == EconomicState.Healthy;
        bool consecutive = ownTurn == state.LastOwnTurn + 1;
        state.IdleOwnTurns = idle ? consecutive ? state.IdleOwnTurns + 1 : 1 : 0;
        state.MissedOwnTurns = opportunity ? consecutive ? state.MissedOwnTurns + 1 : 1 : 0;
        state.TurtleOwnTurns = turtle ? consecutive ? state.TurtleOwnTurns + 1 : 1 : 0;
        state.LastOwnTurn = ownTurn;
        result.IdleOwnTurns = state.IdleOwnTurns; result.MissedOwnTurns = state.MissedOwnTurns;
        result.IdleArmy = state.IdleOwnTurns >= Mathf.Clamp(config.IdleArmyTurnThreshold, 1, 64);
        result.MissedOpportunity = state.MissedOwnTurns >= Mathf.Clamp(config.MissedOpportunityTurnThreshold, 1, 64);
        result.UnnecessaryTurtle = state.TurtleOwnTurns >= Mathf.Clamp(config.IdleArmyTurnThreshold, 1, 64);
        state.History.Add(new AIArmyUtilizationSample { OwnTurn = ownTurn, UnitCount = evidence.UnitCount,
            UtilizedUnitCount = evidence.UtilizedUnitCount, Upkeep = evidence.Upkeep,
            EconomyState = evidence.EconomyState, ObjectiveProgress = evidence.ObjectiveProgress });
        while (state.History.Count > ProductionWindow + 1 || state.History.Count > 0 && ownTurn - state.History[0].OwnTurn > ProductionWindow)
            state.History.RemoveAt(0);
        if (state.History.Count > 1)
        {
            var baseline = state.History[0]; int units = 0, used = 0; bool objectiveProgress = false;
            foreach (var sample in state.History) { units += sample.UnitCount; used += sample.UtilizedUnitCount; objectiveProgress |= sample.ObjectiveProgress; }
            float utilization = units > 0 ? used / (float)units : 0;
            result.ExcessiveProduction = safeToJudge && evidence.OwnPower > evidence.EnemyPower
                && evidence.UnitCount >= evidence.EnemyUnitCount
                && evidence.UnitCount - baseline.UnitCount >= Mathf.Clamp(config.ArmyOverproductionUnitGrowth, 1, 1000)
                && utilization < Mathf.Clamp01(AIReflectionConfig.Finite(config.ArmyLowUtilizationThreshold, .4f))
                && evidence.Upkeep > baseline.Upkeep && evidence.EconomyState > baseline.EconomyState && !objectiveProgress;
        }
        if (result.IdleArmy) { result.Breakdown.IdleArmyPenalty = -Penalty(config.IdleArmyPenalty); result.Reasons.Add(AIFailureReason.IdleArmy); }
        if (result.ExcessiveProduction) { result.Breakdown.OverproductionPenalty = -Penalty(config.ExcessiveProductionPenalty); result.Reasons.Add(AIFailureReason.ExcessiveProduction); }
        if (result.MissedOpportunity) { result.Breakdown.MissedOpportunityPenalty = -Penalty(config.MissedOpportunityPenalty); result.Reasons.Add(AIFailureReason.MissedOpportunity); }
        if (result.UnnecessaryTurtle) { result.Breakdown.FailurePenalty = -Penalty(config.UnnecessaryTurtlePenalty); result.Reasons.Add(AIFailureReason.UnnecessaryTurtle); }
        float totalPenalty = -result.Breakdown.Total;
        float cap = Mathf.Clamp(AIReflectionConfig.Finite(config.MaxArmyTurnPenalty, 1), 0, 3);
        if (totalPenalty > cap && totalPenalty > 0)
        {
            float scale = cap / totalPenalty;
            result.Breakdown.IdleArmyPenalty *= scale; result.Breakdown.OverproductionPenalty *= scale;
            result.Breakdown.MissedOpportunityPenalty *= scale; result.Breakdown.FailurePenalty *= scale;
        }
        return result;
    }

    // Query only already-known cells before asking shared terrain rules; no hidden terrain or occupants are read.
    bool CanProgress(AIBoardState board, Status unit, PersistentStrategicObjective goal)
    {
        if (StatusEffectSystem.IsStunned(unit)) return false;
        if (BoardActionProfile.For(unit)?.CanAttack != false && board.CalcAttackCost(unit) <= board.EnemyAP)
            foreach (var enemy in board.AlivePlayerUnits)
            {
                if (enemy == null || !enemy.IsAlive || !board.IsVisibleToEnemy(enemy.transform.position)) continue;
                var offset = enemy.transform.position - unit.transform.position;
                if (AttackPatterns.CanAttack(unit, unit.direction, offset.x, offset.z)
                    && KnownLine(board, unit.transform.position, enemy.transform.position, false)) return true;
            }
        if (goal == null || !goal.Active || goal.Suspended || unit.HasMovedThisTurn || StatusEffectSystem.IsMovementBlocked(unit)
            || BoardActionProfile.For(unit)?.CanMove == false) return false;
        var map = board.MapCreate != null ? board.MapCreate : board.ReconMap;
        if (map == null) return false;
        int direction = MovePatterns.IsDirectionIndependent(unit) ? 1 : MovePatterns.DirZ(unit.direction);
        var offsets = MovePatterns.Offsets(unit);
        for (int i = 0; i < offsets.Count && i < 256; i++)
        {
            var cell = GridHelper.ToGridXZ(unit.transform.position) + new Vector3Int(offsets[i].x, 0, offsets[i].y * direction);
            if (!board.IsTerrainKnown(cell) || !KnownLine(board, unit.transform.position, cell, true)
                || !map.TryGetHeight(cell.x, cell.z, out float height)) continue;
            var position = new Vector3(cell.x, height, cell.z);
            if (Distance(position, goal.Target) >= Distance(unit.transform.position, goal.Target)
                || board.CalcMoveCost(unit, position) > board.EnemyAP) continue;
            bool occupied = GridHelper.MatchXZ(position, GridHelper.ToGrid(board.EnemyCrystalPos));
            foreach (var ally in board.AliveEnemyUnits) if (ally != null && GridHelper.MatchXZ(position, ally.GridPosition)) occupied = true;
            foreach (var enemy in board.AlivePlayerUnits) if (enemy != null && GridHelper.MatchXZ(position, enemy.GridPosition)) occupied = true;
            if (!occupied && board.EstimateCounterDamageAt(position, unit) < unit.HP) return true;
        }
        return false;
    }
    static bool KnownLine(AIBoardState board, Vector3 from, Vector3 to, bool movement)
    {
        var map = board.MapCreate != null ? board.MapCreate : board.ReconMap;
        if (map == null) return false;
        int x = Mathf.RoundToInt(from.x), z = Mathf.RoundToInt(from.z), targetX = Mathf.RoundToInt(to.x), targetZ = Mathf.RoundToInt(to.z);
        int dx = Math.Abs(targetX - x), dz = Math.Abs(targetZ - z), sx = Math.Sign(targetX - x), sz = Math.Sign(targetZ - z), ix = 0, iz = 0;
        if (dx > 128 || dz > 128) return false;
        while (ix < dx || iz < dz)
        {
            int decision = (1 + 2 * ix) * dz - (1 + 2 * iz) * dx;
            if (decision == 0)
            {
                if (!KnownOpen(board, map, x + sx, z, movement) || !KnownOpen(board, map, x, z + sz, movement)) return false;
                x += sx; z += sz; ix++; iz++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { z += sz; iz++; }
            if (movement || x != targetX || z != targetZ)
                if (!KnownOpen(board, map, x, z, movement)) return false;
        }
        return board.IsTerrainKnown(to);
    }
    static bool KnownOpen(AIBoardState board, MapCreate map, int x, int z, bool movement)
        => board.IsTerrainKnown(new Vector3(x, 0, z)) && !map.IsHighMountain(x, z) && (!movement || !map.IsRiver(x, z));
    static bool ArmyUnit(Status unit) => unit != null && unit.IsAlive && unit.type == Type.Unit && unit.kind != Kind.King;
    static float Power(Status unit) => (Mathf.Max(0, unit.ATK) + Mathf.Max(0, unit.DEF) * .5f)
        * Mathf.Clamp01(unit.HP / (float)Mathf.Max(1, unit.MaxHP));
    static float Distance(Vector3 a, Vector3 b) => GridHelper.ChebyshevDistance(a, b);
    static float Penalty(float value) => Mathf.Clamp(AIReflectionConfig.Finite(value), 0, 3);
    static void Sanitize(AIArmyTurnEvidence evidence)
    {
        evidence.UnitCount = Mathf.Clamp(evidence.UnitCount, 0, 100000); evidence.EnemyUnitCount = Mathf.Clamp(evidence.EnemyUnitCount, 0, 100000);
        evidence.UtilizedUnitCount = Mathf.Clamp(evidence.UtilizedUnitCount, 0, evidence.UnitCount);
        evidence.OwnPower = Mathf.Clamp(AIReflectionConfig.Finite(evidence.OwnPower), 0, 10000000);
        evidence.EnemyPower = Mathf.Clamp(AIReflectionConfig.Finite(evidence.EnemyPower), 0, 10000000);
        evidence.Upkeep = Mathf.Clamp(AIReflectionConfig.Finite(evidence.Upkeep), 0, 10000000);
        if (evidence.EconomyState < EconomicState.Healthy || evidence.EconomyState > EconomicState.Collapse) evidence.EconomyState = EconomicState.Collapse;
    }
    public AIArmyUtilizationState Capture()
    {
        var result = new AIArmyUtilizationState { LastOwnTurn = state.LastOwnTurn, IdleOwnTurns = state.IdleOwnTurns,
            MissedOwnTurns = state.MissedOwnTurns, TurtleOwnTurns = state.TurtleOwnTurns };
        foreach (var sample in state.History) result.History.Add(Copy(sample));
        return result;
    }
    public void Restore(AIArmyUtilizationState saved)
    {
        state = new AIArmyUtilizationState();
        if (saved == null || saved.Version != 1) return;
        state.LastOwnTurn = Mathf.Clamp(saved.LastOwnTurn, -1, 1000000);
        state.IdleOwnTurns = Mathf.Clamp(saved.IdleOwnTurns, 0, 1000000);
        state.MissedOwnTurns = Mathf.Clamp(saved.MissedOwnTurns, 0, 1000000);
        state.TurtleOwnTurns = Mathf.Clamp(saved.TurtleOwnTurns, 0, 1000000);
        if (saved.History == null) return;
        for (int i = Math.Max(0, saved.History.Count - ProductionWindow - 1); i < saved.History.Count; i++)
        {
            var sample = saved.History[i];
            if (sample == null || sample.OwnTurn < 0 || sample.OwnTurn > state.LastOwnTurn
                || state.History.Count > 0 && sample.OwnTurn <= state.History[state.History.Count - 1].OwnTurn
                || sample.EconomyState < EconomicState.Healthy || sample.EconomyState > EconomicState.Collapse) continue;
            var copy = Copy(sample); copy.UnitCount = Mathf.Clamp(copy.UnitCount, 0, 100000);
            copy.UtilizedUnitCount = Mathf.Clamp(copy.UtilizedUnitCount, 0, copy.UnitCount);
            copy.Upkeep = Mathf.Clamp(AIReflectionConfig.Finite(copy.Upkeep), 0, 10000000); state.History.Add(copy);
        }
    }
    static AIArmyUtilizationSample Copy(AIArmyUtilizationSample value) => new AIArmyUtilizationSample
    { OwnTurn = value.OwnTurn, UnitCount = value.UnitCount, UtilizedUnitCount = value.UtilizedUnitCount,
        Upkeep = value.Upkeep, EconomyState = value.EconomyState, ObjectiveProgress = value.ObjectiveProgress };
}
