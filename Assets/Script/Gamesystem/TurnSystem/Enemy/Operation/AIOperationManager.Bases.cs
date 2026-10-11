using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Enemy-base opportunities integrated with the existing operation lifecycle and legal observed index.</summary>
public sealed partial class AIOperationManager
{
    readonly HashSet<string> previouslyVisibleBases = new HashSet<string>();
    readonly List<AIKnownEnemyBase> rankedBases = new List<AIKnownEnemyBase>(128);
    public IReadOnlyList<AIKnownEnemyBase> KnownEnemyBases => state.KnownEnemyBases;
    public int OperationRequiredCitizenDemand { get; private set; }
    AIKnownEnemyBase FindKnownBase(string life)
    { foreach (var entry in state.KnownEnemyBases) if (entry.StableTargetId == life) return entry; return null; }

    void UpdateKnownBases(AIBoardState board, int turn)
    {
        if (!config.EnableBaseAssault) return;
        if (state.KnownEnemyBases == null) state.KnownEnemyBases = new List<AIKnownEnemyBase>();
        previouslyVisibleBases.Clear();
        for (int i = state.KnownEnemyBases.Count - 1; i >= 0; i--)
        {
            var entry = state.KnownEnemyBases[i];
            if (!ValidBase(entry) || board.MapCreate != null && (entry.X >= board.MapCreate.maxX || entry.Z >= board.MapCreate.maxZ))
            { state.KnownEnemyBases.RemoveAt(i); continue; }
            if (entry.CurrentlyVisible) previouslyVisibleBases.Add(entry.StableTargetId);
            entry.CurrentlyVisible = false;
            int age = Math.Max(0, turn - entry.LastSeenTurn);
            entry.IntelConfidence = age <= config.BaseIntelFreshTurns ? .8f : age <= config.BaseIntelFreshTurns * 3
                ? AIOperationConfig.Unit(config.StaleBaseIntelConfidence) : AIOperationConfig.Unit(config.OldBaseIntelConfidence);
        }
        foreach (var target in features.ObservedUnits(board, turn))
        {
            if (target == null || target.type == Type.Unit || target.team == board.ActorTeam || !board.IsVisibleToEnemy(target.transform.position)) continue;
            string life = target.ReflectionLifeId;
            var entry = FindKnownBase(life);
            if (entry == null)
            {
                int limit = Mathf.Clamp(config.MaximumKnownBases, 8, 256);
                if (state.KnownEnemyBases.Count >= limit)
                {
                    int oldest = -1;
                    for (int i = 0; i < state.KnownEnemyBases.Count; i++)
                    {
                        var previous = state.KnownEnemyBases[i]; bool assigned = false;
                        foreach (var plan in state.ActiveOperations) if (Running(plan) && plan.TargetLifeId == previous.StableTargetId) assigned = true;
                        if (!assigned && (oldest < 0 || previous.ConfirmedDestroyed || previous.LastSeenTurn < state.KnownEnemyBases[oldest].LastSeenTurn)) oldest = i;
                    }
                    if (oldest < 0) continue;
                    state.KnownEnemyBases.RemoveAt(oldest);
                }
                entry = new AIKnownEnemyBase { StableTargetId = life, FirstSeenTurn = turn };
                state.KnownEnemyBases.Add(entry);
            }
            else if (!previouslyVisibleBases.Contains(life) && entry.LastSeenTurn < turn)
                entry.RediscoveryCount = Math.Min(1000000, entry.RediscoveryCount + 1);
            entry.Type = ClassifyBase(target); entry.X = Mathf.RoundToInt(target.transform.position.x); entry.Z = Mathf.RoundToInt(target.transform.position.z);
            entry.CurrentlyVisible = true; entry.LastSeenTurn = turn; entry.IntelConfidence = 1;
            entry.LastKnownHpRatio = Mathf.Clamp01((float)target.HP / Mathf.Max(1, target.MaxHP));
            entry.EstimatedStrategicValue = BaseValue(entry.Type); entry.EstimatedDefense = 0;
            entry.DefenseIntelKnown = true;
            for (int dx = -config.ObjectiveDefenseRadius; dx <= config.ObjectiveDefenseRadius; dx++)
                for (int dz = -config.ObjectiveDefenseRadius; dz <= config.ObjectiveDefenseRadius; dz++)
                {
                    int x = entry.X + dx, z = entry.Z + dz;
                    if (x < 0 || z < 0 || board.MapCreate != null && (x >= board.MapCreate.maxX || z >= board.MapCreate.maxZ)) continue;
                    if (!board.IsVisibleToEnemy(new Vector3(x, 0, z))) entry.DefenseIntelKnown = false;
                }
            foreach (var defender in features.ObservedUnits(board, turn))
                if (defender != null && defender.type == Type.Unit && board.IsVisibleToEnemy(defender.transform.position)
                    && AIOperationFeatureExtractor.Distance(target.transform.position, defender.transform.position) <= config.ObjectiveDefenseRadius)
                    entry.EstimatedDefense += AIOperationFeatureExtractor.MilitaryPower(defender);
        }
    }
    static AIEnemyBaseType ClassifyBase(Status target)
    {
        if (target.kind == Kind.Crystal) return AIEnemyBaseType.MainCrystal;
        if (target.kind == Kind.SubCrystal) return AIEnemyBaseType.SubCrystal;
        switch (target.facilityKind)
        {
            case FacilityKind.LoggingCamp: case FacilityKind.Quarry: case FacilityKind.Mine: case FacilityKind.Well: return AIEnemyBaseType.ResourceFacility;
            case FacilityKind.Field: case FacilityKind.Bakery: return AIEnemyBaseType.EconomyFacility;
            case FacilityKind.Barracks: case FacilityKind.Cannon: case FacilityKind.Mortar: case FacilityKind.HeroSword:
            case FacilityKind.RestraintTrap: case FacilityKind.SpikeTrap: return AIEnemyBaseType.MilitaryFacility;
            default: return AIEnemyBaseType.SupportFacility;
        }
    }
    float BaseValue(AIEnemyBaseType type) => AIOperationConfig.NonNegative(type == AIEnemyBaseType.MainCrystal ? config.MainCrystalStrategicValue
        : type == AIEnemyBaseType.SubCrystal ? config.SubCrystalStrategicValue : type == AIEnemyBaseType.MilitaryFacility ? config.MilitaryFacilityStrategicValue
        : type == AIEnemyBaseType.EconomyFacility ? config.EconomyFacilityStrategicValue : type == AIEnemyBaseType.ResourceFacility ? config.ResourceFacilityStrategicValue : config.SupportFacilityStrategicValue);

    void GenerateKnownBaseCandidates(AIBoardState board, int turn, AIOperationContext facts, AIPersonality personality, AIPlayerModel.Profile player)
    {
        OperationRequiredCitizenDemand = 0;
        if (!config.EnableBaseAssault || facts.EconomyState != EconomicState.Healthy || facts.OwnCrystalThreatened || strategy == TurnStrategy.RetreatRegroup) return;
        rankedBases.Clear();
        foreach (var entry in state.KnownEnemyBases) if (!entry.ConfirmedDestroyed) rankedBases.Add(entry);
        rankedBases.Sort((a, b) => (b.EstimatedStrategicValue / (1 + b.EstimatedDefense)).CompareTo(a.EstimatedStrategicValue / (1 + a.EstimatedDefense)));
        float force = 0; int troops = 0;
        foreach (var actor in ownArmy) if (actor.kind != Kind.Scout) { force += AIOperationFeatureExtractor.MilitaryPower(actor); troops++; }
        // Reserve half the force for defense; required production is an economic hint, never an unlimited order.
        float available = force * .5f;
        for (int i = 0; i < Math.Min(3, rankedBases.Count); i++)
        {
            var entry = rankedBases[i]; var position = new Vector3(entry.X, 0, entry.Z);
            int distance = AIOperationFeatureExtractor.Distance(board.EnemyCrystalPos, position);
            float required = Mathf.Max(entry.Type == AIEnemyBaseType.MainCrystal ? config.MinimumMainCrystalAssaultForce : config.MinimumBaseAssaultForce,
                entry.EstimatedDefense * Mathf.Max(1, config.BaseSafeForceRatio) * (2 - entry.IntelConfidence));
            float score = entry.EstimatedStrategicValue - distance * config.BaseDistancePenalty
                - (1 - entry.IntelConfidence) * config.UnknownBaseIntelPenalty
                - entry.EstimatedDefense / Mathf.Max(1, available) * config.BaseDefenseRiskWeight
                - board.Belief.RiskAt(position, board.ReconThreatLevel);
            if (entry.FirstSeenTurn == turn) score += config.FirstBaseDiscoveryBonus;
            if (score < config.MinimumBaseAssaultScore) continue;
            bool weak = available < required;
            bool stale = !entry.CurrentlyVisible || !entry.DefenseIntelKnown || entry.IntelConfidence < .9f;
            bool raid = entry.Type == AIEnemyBaseType.EconomyFacility || entry.Type == AIEnemyBaseType.ResourceFacility;
            AIBaseAssaultPattern first = stale || weak ? AIBaseAssaultPattern.ScoutThenAssault : raid ? AIBaseAssaultPattern.RaidAndWithdraw
                : entry.EstimatedDefense > available / Mathf.Max(1, config.BaseAggressiveForceRatio) || player?.Turtle > .6f ? AIBaseAssaultPattern.FlankAssault : AIBaseAssaultPattern.DirectAssault;
            var second = stale || weak ? AIBaseAssaultPattern.ScoutThenAssault : player?.Turtle > .6f ? AIBaseAssaultPattern.DiversionAssault : AIBaseAssaultPattern.SiegeAssault;
            for (int pattern = 0; pattern < 2; pattern++)
            {
                var chosen = pattern == 0 ? first : second;
                if (pattern == 1 && chosen == first) continue;
                var goal = entry.Type == AIEnemyBaseType.MainCrystal ? AIOperationGoal.DestroyEnemyCrystal
                    : entry.Type == AIEnemyBaseType.SubCrystal ? AIOperationGoal.DestroySubCrystal : AIOperationGoal.BreakEnemyDefense;
                var candidate = AddCandidate(board, turn, goal, 3, score - pattern * 2, null, personality, player);
                if (candidate == null) continue;
                var plan = candidate.Plan; plan.IsBaseAssault = true; plan.BaseAssaultPattern = chosen; plan.BaseTargetType = entry.Type;
                plan.TargetLifeId = entry.StableTargetId; plan.TargetCategory = entry.Type == AIEnemyBaseType.MainCrystal ? "Crystal" : entry.Type == AIEnemyBaseType.SubCrystal ? "SubCrystal" : "Building";
                plan.HasTargetPosition = true; plan.TargetX = entry.X; plan.TargetZ = entry.Z; plan.BaseIntelConfidence = entry.IntelConfidence;
                plan.RequiredAssaultForce = required; plan.RequiredAdditionalCitizens = Mathf.Clamp(Mathf.CeilToInt((required - available) / Mathf.Max(10, force / Math.Max(1, troops))), 0, 8);
                plan.RallyX = Mathf.RoundToInt(Mathf.Lerp(board.EnemyCrystalPos.x, entry.X, .5f)); plan.RallyZ = Mathf.RoundToInt(Mathf.Lerp(board.EnemyCrystalPos.z, entry.Z, .5f));
                plan.StrategicReason = "合法的に発見した拠点を再評価。" + chosen + " / 情報確度 " + entry.IntelConfidence.ToString("F1") + " / 必要戦力 " + required.ToString("F0");
                plan.Steps = BaseSteps(chosen, turn); plan.PatternKey = Pattern(plan.Steps);
                RefreshCandidateContext(candidate, board, turn);
                plan.ContextKey += "|base=" + entry.Type;
                candidate.Experience = Profile.GetModifier(plan.ContextKey, goal, plan.PatternKey, config);
                OperationRequiredCitizenDemand = Math.Max(OperationRequiredCitizenDemand, plan.RequiredAdditionalCitizens);
            }
        }
    }
    List<AIOperationStep> BaseSteps(AIBaseAssaultPattern pattern, int turn)
    {
        var list = new List<AIOperationStep>();
        if (pattern != AIBaseAssaultPattern.DirectAssault) list.Add(new AIOperationStep { Type = AIOperationStepType.Scout, Required = true });
        list.Add(new AIOperationStep { Type = AIOperationStepType.Regroup }); list.Add(new AIOperationStep { Type = AIOperationStepType.Approach });
        if (pattern == AIBaseAssaultPattern.FlankAssault) list.Add(new AIOperationStep { Type = AIOperationStepType.Flank });
        if (pattern == AIBaseAssaultPattern.DiversionAssault) list.Add(new AIOperationStep { Type = AIOperationStepType.Diversion });
        list.Add(new AIOperationStep { Type = AIOperationStepType.Attack }); list.Add(new AIOperationStep { Type = AIOperationStepType.DestroyObjective, Required = true });
        list.Add(new AIOperationStep { Type = pattern == AIBaseAssaultPattern.RaidAndWithdraw ? AIOperationStepType.Retreat : AIOperationStepType.Regroup });
        for (int i = 0; i < list.Count; i++) { list[i].StepId = i + 1; list[i].StartTurn = turn; list[i].Purpose = "拠点攻略: " + list[i].Type; }
        return list;
    }
    float AssignedAssaultForce(AIOperationPlan plan)
    { float force = 0; foreach (string life in plan.AssignedUnitLifeIds) if (features.TryOwn(life, out var actor) && actor != null && actor.IsAlive && actor.kind != Kind.Scout) force += AIOperationFeatureExtractor.MilitaryPower(actor); return force; }
    Vector3 BaseMovementAim(AIOperationPlan plan, Status actor)
    {
        var entry = FindKnownBase(plan.TargetLifeId);
        if (plan.CurrentContext.ConfirmedTargetDestroyed || CurrentStep(plan)?.Type == AIOperationStepType.Retreat)
            return new Vector3(plan.RallyX, 0, plan.RallyZ);
        if (actor.kind != Kind.Scout && (plan.Status == AIOperationStatus.Preparing || entry == null || !entry.CurrentlyVisible
            || CurrentStep(plan)?.Type == AIOperationStepType.Scout || CurrentStep(plan)?.Type == AIOperationStepType.Regroup))
            return new Vector3(plan.RallyX + StableLane(actor.ReflectionLifeId) - 1, 0, plan.RallyZ);
        if (plan.BaseAssaultPattern == AIBaseAssaultPattern.FlankAssault || plan.BaseAssaultPattern == AIBaseAssaultPattern.DiversionAssault)
        {
            var flank = new Vector3(plan.TargetX + config.BaseRallyRadius * (actor.direction == Direction.N ? 1 : -1), 0, plan.TargetZ - config.BaseRallyRadius);
            if (AIOperationFeatureExtractor.Distance(actor.transform.position, flank) > config.BaseRallyRadius) return flank;
        }
        return new Vector3(plan.TargetX, 0, plan.TargetZ);
    }
    static int StableLane(string life)
    { uint hash = 2166136261; if (life != null) foreach (char character in life) hash = unchecked((hash ^ character) * 16777619); return (int)(hash % 3); }
    void ReevaluateBaseAssault(AIOperationPlan plan, AIBoardState board, int turn)
    {
        if (!plan.IsBaseAssault) return;
        var entry = FindKnownBase(plan.TargetLifeId);
        if (entry == null || board.MapCreate != null && (plan.TargetX < 0 || plan.TargetZ < 0 || plan.TargetX >= board.MapCreate.maxX || plan.TargetZ >= board.MapCreate.maxZ))
        { Complete(plan, board, turn, AIOperationAbortReason.ObjectiveUnavailable); return; }
        plan.BaseIntelConfidence = entry.IntelConfidence;
        if (entry.ConfirmedDestroyed) { plan.CurrentContext.ConfirmedTargetDestroyed = true; return; }
        if (!entry.CurrentlyVisible && turn - entry.LastSeenTurn > config.BaseIntelFreshTurns && plan.Revisions.Count < config.RevisionLimit)
        {
            if (CurrentStep(plan)?.Type != AIOperationStepType.Scout) Revise(plan, turn, "拠点の防衛情報が古いため再偵察", true);
            plan.Status = AIOperationStatus.Preparing;
        }
        if (entry.CurrentlyVisible && entry.EstimatedDefense * config.BaseSafeForceRatio > AssignedAssaultForce(plan)) plan.Status = AIOperationStatus.Preparing;
        if (entry.CurrentlyVisible && entry.DefenseIntelKnown && CurrentStep(plan)?.Type == AIOperationStepType.Scout)
        { var step = CurrentStep(plan); step.Completed = true; step.EndTurn = turn; plan.CurrentStep++; plan.LastProgressTurn = turn; }
        if (CurrentStep(plan)?.Type == AIOperationStepType.Regroup && AssignedAssaultForce(plan) >= plan.RequiredAssaultForce)
        {
            bool gathered = plan.AssignedUnitLifeIds.Count > 0;
            foreach (string life in plan.AssignedUnitLifeIds)
                if (features.TryOwn(life, out var actor) && AIOperationFeatureExtractor.Distance(actor.transform.position, new Vector3(plan.RallyX, 0, plan.RallyZ)) > config.BaseRallyRadius) gathered = false;
            if (gathered) { var step = CurrentStep(plan); step.Completed = true; step.EndTurn = turn; plan.CurrentStep++; plan.LastProgressTurn = turn; }
        }
    }
    bool BaseWithdrawalPending(AIOperationPlan plan, int turn)
    {
        if (!plan.IsBaseAssault || plan.BaseAssaultPattern != AIBaseAssaultPattern.RaidAndWithdraw || !plan.CurrentContext.ConfirmedTargetDestroyed) return false;
        bool alive = false, safe = true;
        foreach (string life in plan.AssignedUnitLifeIds)
            if (features.TryOwn(life, out var actor) && actor != null && actor.IsAlive)
            { alive = true; if (AIOperationFeatureExtractor.Distance(actor.transform.position, new Vector3(plan.RallyX, 0, plan.RallyZ)) > config.BaseRallyRadius) safe = false; }
        if (!alive || safe || turn > plan.MaximumEndTurn) return false;
        plan.CurrentStep = plan.Steps.Count - 1; plan.Status = AIOperationStatus.Active;
        return true;
    }
    /// <summary>Call only for a destruction event the acting faction legally observed. Missing contacts never call this.</summary>
    public void ConfirmKnownBaseDestroyed(string life, bool legallyObserved, bool ownAction, AIBoardState board)
    {
        if (!legallyObserved || string.IsNullOrEmpty(life)) return;
        var entry = FindKnownBase(life); if (entry == null) return;
        entry.ConfirmedDestroyed = true; entry.CurrentlyVisible = false;
        foreach (var plan in state.ActiveOperations)
            if (plan.TargetLifeId == life)
            {
                if (ownAction) plan.CurrentContext.ConfirmedTargetDestroyed = true;
                else Complete(plan, board, Math.Max(0, state.LastStartedOwnTurn), AIOperationAbortReason.ObjectiveDestroyedByOther);
            }
        RemoveFinished();
    }
    static bool ValidBase(AIKnownEnemyBase entry) => entry != null && !string.IsNullOrEmpty(entry.StableTargetId) && entry.StableTargetId.Length <= 128
        && entry.X >= 0 && entry.Z >= 0 && entry.FirstSeenTurn >= 0 && entry.LastSeenTurn >= entry.FirstSeenTurn
        && Enum.IsDefined(typeof(AIEnemyBaseType), entry.Type) && !float.IsNaN(entry.IntelConfidence) && !float.IsInfinity(entry.IntelConfidence)
        && entry.IntelConfidence >= 0 && entry.IntelConfidence <= 1 && entry.LastKnownHpRatio >= 0 && entry.LastKnownHpRatio <= 1
        && !float.IsNaN(entry.EstimatedStrategicValue) && !float.IsInfinity(entry.EstimatedStrategicValue) && entry.EstimatedStrategicValue >= 0
        && !float.IsNaN(entry.LastKnownHpRatio) && !float.IsInfinity(entry.LastKnownHpRatio)
        && entry.RediscoveryCount >= 0 && entry.RediscoveryCount <= 1000000
        && !float.IsNaN(entry.EstimatedDefense) && !float.IsInfinity(entry.EstimatedDefense) && entry.EstimatedDefense >= 0;
    void SanitizeKnownBases(AIOperationBattleState copy)
    {
        if (copy.KnownEnemyBases == null) copy.KnownEnemyBases = new List<AIKnownEnemyBase>();
        var ids = new HashSet<string>(); copy.KnownEnemyBases.RemoveAll(entry => !ValidBase(entry) || !ids.Add(entry.StableTargetId));
        int limit = Mathf.Clamp(config.MaximumKnownBases, 8, 256);
        if (copy.KnownEnemyBases.Count > limit) copy.KnownEnemyBases.RemoveRange(limit, copy.KnownEnemyBases.Count - limit);
        foreach (var entry in copy.KnownEnemyBases) entry.CurrentlyVisible = false;
    }
}
