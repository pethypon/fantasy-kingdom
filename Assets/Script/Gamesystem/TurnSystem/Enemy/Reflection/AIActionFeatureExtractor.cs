using System.Collections.Generic;
using UnityEngine;

/// <summary>Extracts only own/observed board values; aggregate work is reused for a board generation.</summary>
public sealed class AIActionFeatureExtractor
{
    readonly AIReflectionConfig config;
    AIBoardState cachedBoard;
    int generation = -1, ownUnits, enemyUnits, buildings;
    readonly Dictionary<Status, Vector2> localPower = new Dictionary<Status, Vector2>();
    readonly Dictionary<Status, (bool attack, bool outside)> combatFacts = new Dictionary<Status, (bool, bool)>();
    readonly HashSet<Status> observedEnemies = new HashSet<Status>();
    readonly List<string> visibleEnemyLifeIds = new List<string>();
    float beliefUncertainty;
    bool beliefPrepared;
    readonly Dictionary<(Status actor, Status target, AIActionType type, FacilityKind facility, FacilityDefinitionData facilityDefinition,
        Kind summon, UnitData summonDefinition, SkillData skill, int distance, TurnStrategy strategy, int threat),
        (string context, string action)> selectionKeys = new Dictionary<(Status, Status, AIActionType, FacilityKind,
            FacilityDefinitionData, Kind, UnitData, SkillData, int, TurnStrategy, int), (string, string)>();

    public AIActionFeatureExtractor(AIReflectionConfig config = null)
        => this.config = config != null ? config : AIReflectionConfig.Active;

    public void GetSelectionKeys(AIAction action, AIBoardState board, int turn, TurnStrategy strategy, int threatLevel,
        int artifacts, out string contextKey, out string actionKey)
    {
        if (board != null) Prepare(board);
        int distance = action.Unit != null && board != null && action.Unit.team == board.ActorTeam
            ? Distance(action.Unit.transform.position, action.TargetPos) : 0;
        int distanceBand = distance <= 1 ? 0 : distance <= 3 ? 1 : distance <= 6 ? 2 : 3;
        int threatBand = threatLevel <= 9 ? 0 : threatLevel <= 30 ? 1 : 2;
        var key = (action.Unit, action.TargetUnit, action.ActionType, action.Facility, action.FacilityDefinition,
            action.SummonKind, action.SummonDefinition, action.Skill, distanceBand, strategy, threatBand);
        if (!selectionKeys.TryGetValue(key, out var keys))
        {
            var snapshot = Capture(action, board, turn, strategy, threatLevel, artifacts, false);
            keys = (ContextKey(snapshot), ActionKey(action, snapshot));
            // Candidate sets are already bounded by the existing AI. This extra guard also bounds external callers.
            if (selectionKeys.Count < 4096) selectionKeys[key] = keys;
        }
        contextKey = keys.context; actionKey = keys.action;
    }

    public AIActionContextSnapshot Capture(AIAction action, AIBoardState board, int turn,
        TurnStrategy strategy, int threatLevel, int artifacts, bool includeDetailedFacts = true)
    {
        var snapshot = new AIActionContextSnapshot { Turn = turn, Strategy = strategy.ToString(),
            CurrentThreatLevel = threatLevel, ArtifactCount = artifacts,
            ActionApCost = action != null ? action.APCost : 0,
            IsApBuff = action != null && action.ActionType == AIActionType.SkillUse && action.Skill?.GrantBuff == BuffType.Haste,
            ActionType = action != null ? action.ActionType : AIActionType.Wait };
        if (board == null) return snapshot;
        Prepare(board);
        snapshot.ActorAp = board.EnemyAP;
        snapshot.OwnUnitCount = ownUnits; snapshot.VisibleEnemyUnitCount = enemyUnits;
        snapshot.VisibleAllyCount = board.AliveEnemyUnits.Count;
        snapshot.VisibleEnemyCount = board.AlivePlayerUnits.Count;
        snapshot.OwnBuildingCount = buildings;
        snapshot.ExploredTiles = board.OwnExploredCells.Count;
        snapshot.TerritoryCount = board.OwnTerritoryCount;
        snapshot.OwnCrystalHP = board.EnemyCrystalHP;
        snapshot.OwnCrystalHpRatio = (float)board.EnemyCrystalHP / Mathf.Max(1, board.EnemyCrystalMaxHP);
        board.Governor?.Evaluate(board);
        snapshot.OwnCrystalThreatened = board.Governor?.Mode == StrategicMode.EmergencyDefense;
        snapshot.EmergencyDefense = snapshot.OwnCrystalThreatened;
        snapshot.EconomyState = board.ProductionDemand?.State ?? EconomicState.Healthy;
        snapshot.MatchPhase = Phase(snapshot);
        if (includeDetailedFacts)
        {
            snapshot.VisibleEnemyLifeIds = new List<string>(visibleEnemyLifeIds);
            if (!beliefPrepared)
            {
                beliefPrepared = true; beliefUncertainty = 0;
                foreach (var contact in board.Belief.Contacts)
                {
                    beliefUncertainty += contact.UnknownProbability;
                    foreach (float probability in contact.Cells.Values) beliefUncertainty += probability * (1 - probability);
                }
            }
            snapshot.BeliefUncertainty = beliefUncertainty;
        }
        var resources = board.EnemyResources;
        if (resources != null)
        {
            snapshot.Wood = resources.Wood; snapshot.Stone = resources.Stone;
            snapshot.Iron = resources.Iron; snapshot.MagicOre = resources.MagicOre;
            snapshot.Wheat = resources.Wheat; snapshot.Bread = resources.Bread;
            snapshot.Water = resources.Water; snapshot.Citizen = resources.Citizen;
        }
        if (action == null) return snapshot;
        snapshot.TargetX = Mathf.RoundToInt(action.TargetPos.x);
        snapshot.TargetY = Mathf.RoundToInt(action.TargetPos.z);
        Status actor = action.Unit;
        if (actor != null && actor.team == board.ActorTeam)
        {
            snapshot.ActorRole = Role(actor.kind);
            snapshot.ActorHP = Mathf.Max(0, actor.HP);
            snapshot.ActorHpRatio = (float)snapshot.ActorHP / Mathf.Max(1, actor.MaxHP);
            Vector3 position = actor.transform.position;
            snapshot.ActorX = Mathf.RoundToInt(position.x); snapshot.ActorY = Mathf.RoundToInt(position.z);
            snapshot.ActorHeight = Mathf.RoundToInt(position.y); snapshot.ActorDirection = (int)actor.direction;
            snapshot.ActorEffectCount = actor.ActiveEffects?.Count ?? 0;
            snapshot.ActorShieldTurns = actor.ShieldTurns;
            snapshot.ActorEffectSignature = includeDetailedFacts ? EffectSignature(actor) : 0;
            snapshot.OwnCrystalDistance = Distance(position, board.EnemyCrystalPos);
            snapshot.IsRangedActor = snapshot.ActorRole == "Ranged"
                || actor.kind != Kind.Scout && AIBoardQuery.EstimateAttackRange(actor) > AIReflectionConfig.NonNegative(config.RangedMinimumAttackRange);
            if (includeDetailedFacts)
            {
                if (!combatFacts.TryGetValue(actor, out var facts))
                {
                    facts = CombatFacts(actor, board); combatFacts[actor] = facts;
                }
                snapshot.CanAttackObservedEnemy = facts.attack;
                snapshot.OutsideObservedEnemyRange = facts.outside;
            }
            if (!localPower.TryGetValue(actor, out var powers))
            {
                powers = LocalPowers(board, position);
                localPower[actor] = powers;
            }
            snapshot.LocalAllyPower = powers.x; snapshot.LocalEnemyPower = powers.y;
            snapshot.IncomingDamage = board.EstimateCounterDamageAt(position, actor);
            var objective = board.Governor?.Objective;
            if (board.Exploration.TryGetAssignedTarget(actor, out var ownGoal))
            {
                snapshot.HasObjective = true;
                snapshot.DistanceToObjective = Mathf.CeilToInt(board.Exploration.GetObjective(actor.GetInstanceID())?.LastDistance
                    ?? Distance(position, ownGoal));
            }
            else if (objective != null && objective.Active && !objective.Suspended)
            {
                snapshot.HasObjective = true;
                snapshot.DistanceToObjective = Distance(position, objective.Target);
            }
            else if (action.ActionType == AIActionType.DefenseRepos || action.ActionType == AIActionType.Retreat)
            {
                snapshot.HasObjective = true;
                snapshot.DistanceToObjective = Distance(position, board.EnemyCrystalPos);
            }
            snapshot.DistanceToTarget = Distance(position, action.TargetPos);
        }
        else snapshot.ActorRole = action.ActionType == AIActionType.Summon ? Role(action.SummonKind) : "Builder";

        Status target = action.TargetUnit;
        // An enemy target reference may still exist after leaving vision. Do not inspect its hidden values.
        if (target != null && (target.team == board.ActorTeam || observedEnemies.Contains(target)))
        {
            snapshot.TargetObserved = true; snapshot.TargetHP = Mathf.Max(0, target.HP);
            snapshot.TargetIsAlly = target.team == board.ActorTeam;
            snapshot.TargetIsSelf = target == actor;
            snapshot.KnownTargetLifeId = target.ReflectionLifeId;
            snapshot.TargetShieldTurns = target.ShieldTurns;
            snapshot.TargetCategory = target.type == Type.Unit ? Role(target.kind)
                : target.kind == Kind.Crystal ? "Crystal" : target.kind == Kind.SubCrystal ? "SubCrystal" : "Building";
            snapshot.TargetX = Mathf.RoundToInt(target.transform.position.x);
            snapshot.TargetY = Mathf.RoundToInt(target.transform.position.z);
            snapshot.TargetEffectCount = target.ActiveEffects?.Count ?? 0;
            snapshot.TargetEffectSignature = includeDetailedFacts ? EffectSignature(target) : 0;
        }
        else if (action.ActionType == AIActionType.Build || action.ActionType == AIActionType.Upgrade)
            snapshot.TargetCategory = "Facility:" + action.Facility;
        else if (action.ActionType == AIActionType.SubCrystal) snapshot.TargetCategory = "SubCrystal";
        else if (action.ActionType == AIActionType.Summon) snapshot.TargetCategory = Role(action.SummonKind);
        else if (target == null) snapshot.TargetCategory = "Cell";
        if (includeDetailedFacts && action.ActionType == AIActionType.SkillUse && action.Skill != null)
        {
            if (snapshot.TargetIsAlly && !string.IsNullOrEmpty(snapshot.KnownTargetLifeId))
                snapshot.SupportTargetLifeIds.Add(snapshot.KnownTargetLifeId);
            if (actor != null && actor.team == board.ActorTeam && (action.Skill.BuffToSelf || target == null
                || action.Skill.Target == SkillTarget.Self || action.Skill.Target == SkillTarget.SelfArea))
                if (!snapshot.SupportTargetLifeIds.Contains(actor.ReflectionLifeId)) snapshot.SupportTargetLifeIds.Add(actor.ReflectionLifeId);
        }
        if (includeDetailedFacts && action.ActionType == AIActionType.SkillUse && action.AreaTargets != null)
            unchecked
            {
                foreach (var affected in action.AreaTargets)
                    if (affected != null && (affected.team == board.ActorTeam || observedEnemies.Contains(affected)))
                    {
                        if (snapshot.AreaEffects.Count >= Mathf.Clamp(config.MaxActionEffectTargets, 1, 1024)) break;
                        bool ally = affected.team == board.ActorTeam;
                        int signature = EffectSignature(affected) * 31 + affected.ShieldTurns;
                        snapshot.AreaEffectSignature += signature;
                        snapshot.AreaEffects.Add(new AIReflectionEffectObservation { LifeId = affected.ReflectionLifeId,
                            Signature = signature, IsAlly = ally, HP = ally ? Mathf.Max(0, affected.HP) : 0 });
                        if (ally && !snapshot.SupportTargetLifeIds.Contains(affected.ReflectionLifeId))
                            snapshot.SupportTargetLifeIds.Add(affected.ReflectionLifeId);
                    }
            }
        return snapshot;
    }

    void Prepare(AIBoardState board)
    {
        if (cachedBoard == board && generation == board.Generation) return;
        cachedBoard = board; generation = board.Generation;
        ownUnits = enemyUnits = buildings = 0; localPower.Clear(); selectionKeys.Clear(); combatFacts.Clear();
        observedEnemies.Clear(); visibleEnemyLifeIds.Clear(); beliefPrepared = false;
        foreach (var unit in board.AliveEnemyUnits) if (unit != null && unit.IsAlive && unit.type == Type.Unit) ownUnits++;
        foreach (var unit in board.AlivePlayerUnits)
            if (unit != null && unit.IsAlive)
            {
                if (unit.type == Type.Unit) enemyUnits++;
                observedEnemies.Add(unit);
                if (visibleEnemyLifeIds.Count < config.EventLimit) visibleEnemyLifeIds.Add(unit.ReflectionLifeId);
            }
        if (board.EnemyBuildingCounts != null)
            foreach (var count in board.EnemyBuildingCounts.Values) buildings += Mathf.Max(0, count);
    }
    string Phase(AIActionContextSnapshot snapshot)
    {
        if (snapshot.Turn >= Mathf.Max(1, config.LatePhaseMinTurns)
            || snapshot.OwnUnitCount >= Mathf.Max(1, config.LatePhaseUnitThreshold)
                && snapshot.TerritoryCount >= Mathf.Max(1, config.LatePhaseTerritoryThreshold)
            || snapshot.OwnCrystalHP > 0 && snapshot.OwnCrystalHpRatio <= Mathf.Clamp01(config.LatePhaseCrystalHpRatio)) return "Late";
        return snapshot.Turn <= Mathf.Max(1, config.EarlyPhaseMaxTurns) ? "Early" : "Mid";
    }
    static int EffectSignature(Status actor)
    {
        if (actor.ActiveEffects == null) return 0;
        unchecked
        {
            int signature = 0;
            foreach (var effect in actor.ActiveEffects)
                if (effect != null) signature += ((int)effect.debuffType * 397 ^ (int)effect.buffType * 31) * 17 + effect.remainingTurns;
            return signature;
        }
    }
    static (bool attack, bool outside) CombatFacts(Status actor, AIBoardState board)
    {
        bool attack = false, threatened = false, hasObservedEnemy = false;
        var position = actor.transform.position;
        foreach (var opponent in board.AlivePlayerUnits)
        {
            if (opponent == null || !opponent.IsAlive) continue;
            hasObservedEnemy = true;
            var enemyPosition = opponent.transform.position;
            if (AttackPatterns.CanAttack(actor, actor.direction, enemyPosition.x - position.x, enemyPosition.z - position.z)
                && KnownAttackLine(board, actor, enemyPosition)) attack = true;
            if (AttackPatterns.CanAttack(opponent, opponent.direction, position.x - enemyPosition.x, position.z - enemyPosition.z)
                && KnownAttackLine(board, opponent, position, true)) threatened = true;
        }
        return (attack, hasObservedEnemy && !threatened);
    }
    static bool KnownAttackLine(AIBoardState board, Status actor, Vector3 destination, bool unknownMayReach = false)
    {
        if (board.ReconMap == null) return unknownMayReach;
        var from = GridHelper.ToGridXZ(actor.transform.position); var to = GridHelper.ToGridXZ(destination);
        if (!board.IsTerrainKnown(from) || !board.IsTerrainKnown(to)) return unknownMayReach;
        int x = from.x, z = from.z, dx = Mathf.Abs(to.x - x), dz = Mathf.Abs(to.z - z);
        int sx = System.Math.Sign(to.x - x), sz = System.Math.Sign(to.z - z), ix = 0, iz = 0;
        // Match MapCreate's supercover before asking it about terrain: no unknown corner is read.
        while (ix < dx || iz < dz)
        {
            int decision = (1 + 2 * ix) * dz - (1 + 2 * iz) * dx;
            if (decision == 0)
            {
                if (!board.IsTerrainKnown(new Vector3(x + sx, 0, z)) || !board.IsTerrainKnown(new Vector3(x, 0, z + sz))) return unknownMayReach;
                x += sx; z += sz; ix++; iz++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { z += sz; iz++; }
            if (!board.IsTerrainKnown(new Vector3(x, 0, z))) return unknownMayReach;
        }
        return board.ReconMap.CanAttackAcrossTerrain(actor, destination);
    }
    static Vector2 LocalPowers(AIBoardState board, Vector3 point)
    {
        Vector2 result = Vector2.zero;
        foreach (var unit in board.AliveEnemyUnits)
            if (unit != null && unit.IsAlive && Distance(point, unit.transform.position) <= 4) result.x += Power(unit);
        foreach (var unit in board.AlivePlayerUnits)
            if (unit != null && unit.IsAlive && Distance(point, unit.transform.position) <= 4) result.y += Power(unit);
        return result;
    }
    static float Power(Status unit) => (Mathf.Max(0, unit.ATK) + Mathf.Max(0, unit.DEF) * .5f)
        * Mathf.Clamp01((float)unit.HP / Mathf.Max(1, unit.MaxHP));
    static int Distance(Vector3 first, Vector3 second) => Mathf.RoundToInt(GridHelper.ChebyshevDistance(first, second));
    public static string Role(Kind kind)
    {
        switch (kind)
        {
            case Kind.King: return "King";
            case Kind.Boss: return "Boss";
            case Kind.Scout: return "Scout";
            case Kind.Priest: return "Healer";
            case Kind.Assassin: return "Assassin";
            case Kind.Archer: case Kind.Crossbow: case Kind.Magicsniper: case Kind.Magic: return "Ranged";
            case Kind.Bomber: return "AreaAttack";
            case Kind.Guardian: return "Guardian";
            default: return "Infantry";
        }
    }
    public static string ContextKey(AIActionContextSnapshot snapshot)
    {
        float ratio = snapshot.LocalPowerRatio;
        int power = ratio < .5f ? 0 : ratio < .85f ? 1 : ratio <= 1.2f ? 2 : ratio <= 2f ? 3 : 4;
        int distance = snapshot.DistanceToTarget <= 1 ? 0 : snapshot.DistanceToTarget <= 3 ? 1
            : snapshot.DistanceToTarget <= 6 ? 2 : 3;
        int threatBand = snapshot.CurrentThreatLevel <= 9 ? 0 : snapshot.CurrentThreatLevel <= 30 ? 1 : 2;
        string key = snapshot.Strategy + "|" + snapshot.ActionType + "|" + snapshot.ActorRole + "|" + snapshot.TargetCategory
            + "|hp" + Mathf.Clamp(Mathf.FloorToInt(snapshot.ActorHpRatio * 4), 0, 3)
            + "|range" + distance + "|power" + power + "|economy" + (int)snapshot.EconomyState
            + "|crystal" + (snapshot.OwnCrystalThreatened ? 1 : 0) + "|height" + Mathf.Clamp(snapshot.ActorHeight, 0, 3)
            + "|threat" + threatBand;
        return string.IsNullOrEmpty(snapshot.MatchPhase) ? key : key + "|phase" + snapshot.MatchPhase;
    }
    public static string ActionKey(AIAction action, AIActionContextSnapshot snapshot)
    {
        string detail = action != null && (action.ActionType == AIActionType.Build || action.ActionType == AIActionType.Upgrade)
            ? ":" + action.Facility + ":" + (action.FacilityDefinition?.definitionId ?? "default")
            : action != null && action.ActionType == AIActionType.SkillUse ? ":skill" + (action.Skill?.Id ?? -1)
            : action != null && action.ActionType == AIActionType.Summon ? ":" + action.SummonKind
                + ":" + (action.SummonDefinition?.definitionId ?? "default") : "";
        return snapshot.ActionType + ":" + snapshot.ActorRole + detail;
    }
}
