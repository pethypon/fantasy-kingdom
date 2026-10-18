using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Enemy-base opportunities integrated with the existing operation lifecycle and legal observed index.</summary>
public sealed partial class AIOperationManager
{
    readonly HashSet<string> previouslyVisibleBases = new HashSet<string>();
    readonly List<AIKnownEnemyBase> rankedBases = new List<AIKnownEnemyBase>(128);
    readonly Dictionary<string, AIKnownEnemyBase> knownBaseIndex = new Dictionary<string, AIKnownEnemyBase>(128, StringComparer.Ordinal);
    readonly List<UnitData> baseRecruitDefinitions = new List<UnitData>(24);
    AIOperationBattleState indexedBaseState;
    AIBoardState recruitmentBoard;
    int recruitmentGeneration = -1, reinforcementCitizens = 1;
    float reinforcementPower;
    public IReadOnlyList<AIKnownEnemyBase> KnownEnemyBases => state.KnownEnemyBases;
    public int OperationRequiredCitizenDemand { get; private set; }
    int BaseLimit => Mathf.Clamp(config.MaximumKnownBases, 8, 256);
    int DefenseRadius => Mathf.Clamp(config.ObjectiveDefenseRadius, 1, 10);
    int RallyRadius => Mathf.Clamp(config.BaseRallyRadius, 1, 10);
    float AvailableForceFraction => Mathf.Clamp(AIOperationConfig.Finite(config.BaseAvailableForceFraction, .5f), .1f, .75f);
    AIKnownEnemyBase FindKnownBase(string life)
    {
        if (indexedBaseState != state) RebuildKnownBaseIndex();
        return life != null && knownBaseIndex.TryGetValue(life, out var entry) ? entry : null;
    }
    void RebuildKnownBaseIndex()
    {
        knownBaseIndex.Clear(); indexedBaseState = state;
        if (state.KnownEnemyBases != null) foreach (var entry in state.KnownEnemyBases)
            if (entry != null && !string.IsNullOrEmpty(entry.StableTargetId)) knownBaseIndex[entry.StableTargetId] = entry;
    }

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
            int fresh = Mathf.Clamp(config.BaseIntelFreshTurns, 1, 12);
            entry.IntelConfidence = age <= fresh ? AIOperationConfig.Unit(config.FreshBaseIntelConfidence) : age <= fresh * 3
                ? AIOperationConfig.Unit(config.StaleBaseIntelConfidence) : AIOperationConfig.Unit(config.OldBaseIntelConfidence);
        }
        RebuildKnownBaseIndex();
        foreach (var target in features.ObservedUnits(board, turn))
        {
            if (target == null || !board.IsVisibleToEnemy(target.transform.position) || target.team == board.ActorTeam || !IsBaseTarget(target)) continue;
            string life = target.ReflectionLifeId;
            var entry = FindKnownBase(life);
            if (entry == null)
            {
                if (state.KnownEnemyBases.Count >= BaseLimit)
                {
                    int oldest = -1;
                    for (int i = 0; i < state.KnownEnemyBases.Count; i++)
                    {
                        var previous = state.KnownEnemyBases[i]; bool assigned = false;
                        foreach (var plan in state.ActiveOperations) if (Running(plan) && plan.TargetLifeId == previous.StableTargetId) assigned = true;
                        if (!assigned && (oldest < 0 || PreferEviction(previous, state.KnownEnemyBases[oldest]))) oldest = i;
                    }
                    if (oldest < 0) continue;
                    knownBaseIndex.Remove(state.KnownEnemyBases[oldest].StableTargetId);
                    state.KnownEnemyBases.RemoveAt(oldest);
                }
                bool firstDiscovery = RecordBaseDiscovery(life);
                entry = new AIKnownEnemyBase { StableTargetId = life, FirstSeenTurn = turn,
                    DiscoveryBonusTurn = firstDiscovery ? turn : -1, RediscoveryCount = firstDiscovery ? 0 : 1 };
                state.KnownEnemyBases.Add(entry);
                knownBaseIndex[life] = entry;
            }
            else if (!previouslyVisibleBases.Contains(life) && entry.LastSeenTurn < turn)
                entry.RediscoveryCount = Math.Min(1000000, entry.RediscoveryCount + 1);
            entry.Type = ClassifyBase(target); entry.X = Mathf.RoundToInt(target.transform.position.x); entry.Z = Mathf.RoundToInt(target.transform.position.z);
            entry.CurrentlyVisible = true; entry.LastSeenTurn = turn; entry.IntelConfidence = 1;
            entry.LastKnownHpRatio = Mathf.Clamp01((float)target.HP / Mathf.Max(1, target.MaxHP));
            entry.EstimatedStrategicValue = BaseValue(entry.Type); entry.EstimatedDefense = 0;
            entry.DefenseIntelKnown = true;
            for (int dx = -DefenseRadius; dx <= DefenseRadius; dx++)
                for (int dz = -DefenseRadius; dz <= DefenseRadius; dz++)
                {
                    int x = entry.X + dx, z = entry.Z + dz;
                    if (x < 0 || z < 0 || board.MapCreate != null && (x >= board.MapCreate.maxX || z >= board.MapCreate.maxZ)) continue;
                    if (!board.IsVisibleToEnemy(new Vector3(x, 0, z))) entry.DefenseIntelKnown = false;
                }
            foreach (var defender in board.Enemies.Near(target.transform.position, DefenseRadius))
                if (defender != null && board.IsVisibleToEnemy(defender.transform.position) && (defender.type == Type.Unit || defender.ATK > 0)
                    && AIOperationFeatureExtractor.Distance(target.transform.position, defender.transform.position) <= DefenseRadius)
                    entry.EstimatedDefense += AIOperationFeatureExtractor.MilitaryPower(defender);
            if (target.kind == Kind.Crystal && target.ATK > 0) entry.EstimatedDefense += AIOperationFeatureExtractor.MilitaryPower(target);
        }
    }
    static bool PreferEviction(AIKnownEnemyBase candidate, AIKnownEnemyBase current)
        => candidate.ConfirmedDestroyed != current.ConfirmedDestroyed ? candidate.ConfirmedDestroyed
            : candidate.LastSeenTurn != current.LastSeenTurn ? candidate.LastSeenTurn < current.LastSeenTurn
            : string.CompareOrdinal(candidate.StableTargetId, current.StableTargetId) < 0;

    bool RecordBaseDiscovery(string life)
    {
        if (state.BaseDiscoveryBits == null || state.BaseDiscoveryBits.Length != 64) state.BaseDiscoveryBits = new int[64];
        return RecordBaseDiscovery(life, state.BaseDiscoveryBits);
    }
    static bool RecordBaseDiscovery(string life, int[] bits)
    {
        uint first = LifeHash(life), second = unchecked(first * 2246822519u + 3266489917u);
        int a = (int)(first & 2047), b = (int)(second & 2047);
        bool seen = (bits[a >> 5] & 1 << (a & 31)) != 0 && (bits[b >> 5] & 1 << (b & 31)) != 0;
        bits[a >> 5] |= 1 << (a & 31); bits[b >> 5] |= 1 << (b & 31);
        return !seen;
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
    static bool IsBaseTarget(Status target) => target.kind == Kind.Crystal || target.kind == Kind.SubCrystal
        || (target.type == Type.Building || target.type == Type.Wall) && FacilityData.Table.ContainsKey(target.facilityKind);
    float BaseValue(AIEnemyBaseType type) => AIOperationConfig.NonNegative(type == AIEnemyBaseType.MainCrystal ? config.MainCrystalStrategicValue
        : type == AIEnemyBaseType.SubCrystal ? config.SubCrystalStrategicValue : type == AIEnemyBaseType.MilitaryFacility ? config.MilitaryFacilityStrategicValue
        : type == AIEnemyBaseType.EconomyFacility ? config.EconomyFacilityStrategicValue : type == AIEnemyBaseType.ResourceFacility ? config.ResourceFacilityStrategicValue : config.SupportFacilityStrategicValue);

    static bool SupplyRecoveryRequired(AIBoardState board)
        => board.Governor != null ? board.Governor.Mode == StrategicMode.EconomicRecovery
            : AIStrategicGovernor.RequiresEconomicRecovery(board.ProductionDemand);

    void GenerateKnownBaseCandidates(AIBoardState board, int turn, AIOperationContext facts, AIPersonality personality, AIPlayerModel.Profile player)
    {
        OperationRequiredCitizenDemand = 0;
        if (!config.EnableBaseAssault || SupplyRecoveryRequired(board)
            || facts.OwnCrystalThreatened || strategy == TurnStrategy.RetreatRegroup) return;
        rankedBases.Clear();
        foreach (var entry in state.KnownEnemyBases) if (!entry.ConfirmedDestroyed) rankedBases.Add(entry);
        rankedBases.Sort((a, b) =>
        {
            int rank = BaseOpportunityValue(b, board).CompareTo(BaseOpportunityValue(a, board));
            return rank != 0 ? rank : string.CompareOrdinal(a.StableTargetId, b.StableTargetId);
        });
        float force = 0;
        foreach (var actor in ownArmy) if (actor.kind != Kind.Scout) force += AIOperationFeatureExtractor.MilitaryPower(actor);
        // Reserve half the force for defense; required production is an economic hint, never an unlimited order.
        float available = force * AvailableForceFraction;
        for (int i = 0; i < Math.Min(3, rankedBases.Count); i++)
        {
            var entry = rankedBases[i]; var position = new Vector3(entry.X, 0, entry.Z);
            int distance = AIOperationFeatureExtractor.Distance(board.EnemyCrystalPos, position);
            float required = RequiredBaseForce(entry);
            Vector3 approach = SelectBaseApproach(board, position, out bool routeConfirmed);
            float score = entry.EstimatedStrategicValue - distance * AIOperationConfig.NonNegative(config.BaseDistancePenalty)
                - (1 - entry.IntelConfidence) * AIOperationConfig.NonNegative(config.UnknownBaseIntelPenalty)
                - entry.EstimatedDefense / Mathf.Max(1, available) * AIOperationConfig.NonNegative(config.BaseDefenseRiskWeight)
                - EstimateBaseRouteRisk(board, board.EnemyCrystalPos, approach) * AIOperationConfig.NonNegative(config.BaseRouteRiskWeight)
                + Mathf.Clamp(available / Mathf.Max(1, required) - 1, -1, 1) * AIOperationConfig.NonNegative(config.BaseForceAdvantageWeight);
            if (entry.DiscoveryBonusTurn == turn) score += AIOperationConfig.NonNegative(config.FirstBaseDiscoveryBonus);
            if (RecentBaseSuccess(turn) && entry.CurrentlyVisible && entry.DefenseIntelKnown && available >= required)
                score += AIOperationConfig.NonNegative(config.BaseSuccessfulOpportunityBonus);
            if (score < AIOperationConfig.NonNegative(config.MinimumBaseAssaultScore)) continue;
            bool weak = available < required;
            bool stale = !entry.CurrentlyVisible || !entry.DefenseIntelKnown || entry.IntelConfidence < .9f || !routeConfirmed;
            bool raid = entry.Type == AIEnemyBaseType.EconomyFacility || entry.Type == AIEnemyBaseType.ResourceFacility;
            bool turtle = player != null && player.Turtle > AIOperationConfig.Unit(config.BaseTurtleThreshold);
            AIBaseAssaultPattern first = raid ? AIBaseAssaultPattern.RaidAndWithdraw : stale || weak ? AIBaseAssaultPattern.ScoutThenAssault
                : entry.EstimatedDefense > available / Mathf.Max(1, AIOperationConfig.Finite(config.BaseAggressiveForceRatio, 2)) || turtle
                    ? AIBaseAssaultPattern.FlankAssault : AIBaseAssaultPattern.DirectAssault;
            var second = stale || weak || raid ? first : turtle ? AIBaseAssaultPattern.DiversionAssault : AIBaseAssaultPattern.SiegeAssault;
            for (int pattern = 0; pattern < 2; pattern++)
            {
                var chosen = pattern == 0 ? first : second;
                if (pattern == 1 && chosen == first) continue;
                var steps = BaseSteps(chosen, turn);
                if (steps.Count > config.StepLimit) continue;
                var goal = entry.Type == AIEnemyBaseType.MainCrystal ? AIOperationGoal.DestroyEnemyCrystal
                    : entry.Type == AIEnemyBaseType.SubCrystal ? AIOperationGoal.DestroySubCrystal : AIOperationGoal.BreakEnemyDefense;
                var candidate = AddCandidate(board, turn, goal, 3, score - pattern * 2, null, personality, player);
                if (candidate == null) continue;
                var plan = candidate.Plan; plan.IsBaseAssault = true; plan.BaseAssaultPattern = chosen; plan.BaseTargetType = entry.Type;
                plan.TargetLifeId = entry.StableTargetId; plan.TargetCategory = entry.Type == AIEnemyBaseType.MainCrystal ? "Crystal" : entry.Type == AIEnemyBaseType.SubCrystal ? "SubCrystal" : "Building";
                plan.HasTargetPosition = true; plan.TargetX = entry.X; plan.TargetZ = entry.Z; plan.BaseIntelConfidence = entry.IntelConfidence;
                plan.EstimatedBaseDefense = entry.EstimatedDefense; plan.BaseDefenseKnown = entry.DefenseIntelKnown;
                plan.BaseRouteConfirmed = routeConfirmed; plan.HasBaseApproachPoint = true;
                plan.BaseApproachX = Mathf.RoundToInt(approach.x); plan.BaseApproachZ = Mathf.RoundToInt(approach.z);
                plan.RequiredAssaultForce = required; plan.RequiredAdditionalCitizens = BaseReinforcementCitizenDemand(board, required - available);
                Vector3 midpoint = Vector3.Lerp(board.EnemyCrystalPos, position, .5f);
                Vector3 rally = FindKnownStagingCell(board, midpoint, board.EnemyCrystalPos);
                plan.RallyX = Mathf.RoundToInt(rally.x); plan.RallyZ = Mathf.RoundToInt(rally.z);
                plan.StrategicReason = "合法的に発見した拠点を再評価。" + BasePatternLabel(chosen)
                    + " / 情報確度 " + entry.IntelConfidence.ToString("F1") + " / 必要戦力 " + required.ToString("F0")
                    + (weak ? " / 戦力不足のため補充と集結を優先" : stale ? " / 情報不足のため再偵察を優先" : " / 準備が整い次第接近");
                plan.Steps = steps; plan.PatternKey = Pattern(plan.Steps);
                RefreshCandidateContext(candidate, board, turn);
                candidate.Experience = Profile.GetModifier(plan.ContextKey, goal, plan.PatternKey, config);
                OperationRequiredCitizenDemand = Math.Max(OperationRequiredCitizenDemand, plan.RequiredAdditionalCitizens);
            }
        }
        foreach (var plan in state.ActiveOperations)
            if (plan.IsBaseAssault && Running(plan) && plan.Status != AIOperationStatus.Suspended && !plan.CurrentContext.ConfirmedTargetDestroyed)
                OperationRequiredCitizenDemand = Math.Max(OperationRequiredCitizenDemand, plan.RequiredAdditionalCitizens);
    }
    float BaseOpportunityValue(AIKnownEnemyBase entry, AIBoardState board)
        => entry.EstimatedStrategicValue / (1 + entry.EstimatedDefense
            + AIOperationFeatureExtractor.Distance(board.EnemyCrystalPos, new Vector3(entry.X, 0, entry.Z)) * AIOperationConfig.NonNegative(config.BaseDistancePenalty)
            + (1 - entry.IntelConfidence) * AIOperationConfig.NonNegative(config.UnknownBaseIntelPenalty));
    float RequiredBaseForce(AIKnownEnemyBase entry)
        => Mathf.Max(AIOperationConfig.NonNegative(entry.Type == AIEnemyBaseType.MainCrystal ? config.MinimumMainCrystalAssaultForce : config.MinimumBaseAssaultForce),
            (entry.EstimatedDefense + (entry.DefenseIntelKnown ? 0 : AIOperationConfig.NonNegative(config.BaseUnknownDefenseEstimate)))
                * Mathf.Max(1, AIOperationConfig.Finite(config.BaseSafeForceRatio, 1.2f)) * (2 - entry.IntelConfidence));
    int BaseReinforcementCitizenDemand(AIBoardState board, float missingForce)
    {
        if (missingForce <= 0) return 0;
        if (recruitmentBoard != board || recruitmentGeneration != board.Generation)
        {
            recruitmentBoard = board; recruitmentGeneration = board.Generation;
            reinforcementPower = Mathf.Max(1, AIOperationConfig.Finite(config.BaseFallbackReinforcementPower, 20)); reinforcementCitizens = 1;
            board.CollectRecruitDefinitions(baseRecruitDefinitions);
            float bestEfficiency = -1;
            foreach (var data in baseRecruitDefinitions)
            {
                if (data == null || !data.IsValidForAuthoring || data.kind == Kind.None || data.kind == Kind.King || data.kind == Kind.Boss
                    || data.kind == Kind.Scout || data.kind == Kind.Crystal || data.kind == Kind.SubCrystal || data.kind == Kind.WoodWall || data.kind == Kind.StoneWall) continue;
                float power = Mathf.Max(0, data.baseATK) + Mathf.Max(0, data.baseDEF) * .5f;
                if (power <= 0) continue;
                float efficiency = power / Mathf.Max(.25f, data.costCitizen);
                if (efficiency > bestEfficiency)
                { bestEfficiency = efficiency; reinforcementPower = power; reinforcementCitizens = data.costCitizen; }
            }
        }
        int units = Mathf.Clamp(Mathf.CeilToInt(missingForce / reinforcementPower), 0, Mathf.Clamp(config.MaximumBaseReinforcementUnits, 1, 16));
        return (int)Math.Min(128L, (long)units * reinforcementCitizens);
    }
    bool RecentBaseSuccess(int turn)
    {
        if (state.CompletedOperations.Count == 0) return false;
        var previous = state.CompletedOperations[state.CompletedOperations.Count - 1];
        return previous.IsBaseAssault && previous.PrimaryGoalAchieved && previous.Status == AIOperationStatus.Success
            && turn >= previous.ActualEndTurn && turn - previous.ActualEndTurn <= Mathf.Clamp(config.BaseIntelFreshTurns, 1, 12);
    }
    bool ShouldPromoteDiscoveredBase(AIOperationCandidate candidate, AIOperationPlan current)
    {
        if (!candidate.Plan.IsBaseAssault || candidate.Priority != current.StrategicPriority
            || current.PrimaryGoal != AIOperationGoal.ScoutRegion && current.PrimaryGoal != AIOperationGoal.LocateEnemyMainForce) return false;
        float scoutingScore = CurrentScoutingBaseScore;
        foreach (var other in candidates)
            if (other.Plan.PrimaryGoal == current.PrimaryGoal && other.Plan.TargetLifeId == current.TargetLifeId)
            { scoutingScore = other.FinalScore; break; }
        return candidate.FinalScore >= scoutingScore + AIOperationConfig.NonNegative(config.BaseDiscoverySwitchMargin);
    }
    // Three corridors and a small staging neighborhood keep route work independent of total map area.
    // Unknown terrain never supplies a confirmed route. It produces a scout/preparation plan instead.
    Vector3 SelectBaseApproach(AIBoardState board, Vector3 target, out bool routeConfirmed)
    {
        Vector3 best = target; float bestCost = float.PositiveInfinity; routeConfirmed = false;
        Vector3 origin = board.EnemyCrystalPos; int nearest = int.MaxValue;
        foreach (var actor in ownArmy)
        {
            if (actor == null || !actor.IsAlive) continue;
            int distance = AIOperationFeatureExtractor.Distance(actor.transform.position, target);
            if (distance < nearest) { nearest = distance; origin = actor.transform.position; }
        }
        int radius = RallyRadius;
        for (int lane = -1; lane <= 1; lane++)
        {
            Vector3 desired = lane == 0 ? target : target + new Vector3(lane * radius, 0, -radius);
            Vector3 candidate = FindKnownStagingCell(board, desired, target);
            bool clear = KnownBaseCorridorClear(board, origin, candidate) && KnownBaseCorridorClear(board, candidate, target);
            float detour = Mathf.Max(0, AIOperationFeatureExtractor.Distance(origin, candidate)
                + AIOperationFeatureExtractor.Distance(candidate, target) - AIOperationFeatureExtractor.Distance(origin, target));
            float cost = EstimateBaseRouteRisk(board, origin, candidate) + EstimateBaseRouteRisk(board, candidate, target)
                + detour * AIOperationConfig.NonNegative(config.BaseDistancePenalty)
                + (clear ? 0 : AIOperationConfig.NonNegative(config.UnknownBaseIntelPenalty));
            if (cost < bestCost) { bestCost = cost; best = candidate; routeConfirmed = clear; }
        }
        return best;
    }
    Vector3 FindKnownStagingCell(AIBoardState board, Vector3 desired, Vector3 fallback)
    {
        if (board.MapCreate == null) return desired;
        Vector3 best = fallback; float bestCost = float.PositiveInfinity;
        int centerX = Mathf.RoundToInt(desired.x), centerZ = Mathf.RoundToInt(desired.z);
        for (int dx = -2; dx <= 2; dx++) for (int dz = -2; dz <= 2; dz++)
        {
            var point = new Vector3(centerX + dx, 0, centerZ + dz);
            if (!board.IsTerrainKnown(point) || !board.MapCreate.TryGetHeight(centerX + dx, centerZ + dz, out float height)) continue;
            point.y = height;
            float cost = Mathf.Max(Math.Abs(dx), Math.Abs(dz))
                + board.Belief.RiskAt(point, board.ReconThreatLevel) * AIOperationConfig.NonNegative(config.BaseRouteRiskWeight);
            if (cost < bestCost) { bestCost = cost; best = point; }
        }
        return best;
    }
    bool KnownBaseCorridorClear(AIBoardState board, Vector3 start, Vector3 target)
    {
        int distance = AIOperationFeatureExtractor.Distance(start, target);
        int limit = Mathf.Clamp(config.BaseRouteSampleLimit, 8, 64);
        if (distance > limit) return false;
        for (int step = 0; step <= distance; step++)
            if (!board.IsTerrainKnown(Vector3.Lerp(start, target, distance == 0 ? 0 : (float)step / distance))) return false;
        return board.MapCreate == null || board.MapCreate.CanTraverse(start, target);
    }
    float EstimateBaseRouteRisk(AIBoardState board, Vector3 start, Vector3 target)
    {
        int samples = Math.Min(8, Math.Max(1, AIOperationFeatureExtractor.Distance(start, target)));
        float risk = 0;
        for (int step = 1; step <= samples; step++) risk += board.Belief.RiskAt(Vector3.Lerp(start, target, (float)step / samples), board.ReconThreatLevel);
        return risk / samples;
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
        for (int i = 0; i < list.Count; i++)
        {
            list[i].StepId = i + 1; list[i].StartTurn = turn;
            list[i].Purpose = list[i].Type == AIOperationStepType.Scout ? "拠点と周辺防衛・進軍経路を再確認"
                : list[i].Type == AIOperationStepType.Regroup ? "必要戦力を揃え安全な集合地点へ集結"
                : list[i].Type == AIOperationStepType.Approach ? "既知の危険度と地形を比較して接近"
                : list[i].Type == AIOperationStepType.Attack ? "防衛駒の排除と拠点への攻撃を比較"
                : list[i].Type == AIOperationStepType.DestroyObjective ? "主目標の正式な破壊を確認"
                : list[i].Type == AIOperationStepType.Retreat ? "目的達成後は追撃せず集合地点へ撤退"
                : "既存の側面・陽動行動を利用";
        }
        return list;
    }
    float AssignedAssaultForce(AIOperationPlan plan)
    { float force = 0; foreach (string life in plan.AssignedUnitLifeIds) if (features.TryOwn(life, out var actor) && actor != null && actor.IsAlive && actor.kind != Kind.Scout) force += AIOperationFeatureExtractor.MilitaryPower(actor); return force; }
    Vector3 BaseMovementAim(AIOperationPlan plan, Status actor)
    {
        var entry = FindKnownBase(plan.TargetLifeId);
        if (plan.CurrentContext.ConfirmedTargetDestroyed || CurrentStep(plan)?.Type == AIOperationStepType.Retreat)
            return new Vector3(plan.RallyX, 0, plan.RallyZ);
        var step = CurrentStep(plan)?.Type;
        if (step == AIOperationStepType.Regroup || actor.kind != Kind.Scout && (plan.Status == AIOperationStatus.Preparing
            || entry == null || !entry.CurrentlyVisible || step == AIOperationStepType.Scout))
            return new Vector3(plan.RallyX, 0, plan.RallyZ);
        if (plan.BaseAssaultPattern == AIBaseAssaultPattern.DiversionAssault && step == AIOperationStepType.Diversion && actor.kind == Kind.Scout)
        {
            if (legacyPlan != null && legacyPlan.Active && legacyPlan.Goal == AIPlanGoal.EasternFeint
                && AIOperationFeatureExtractor.Distance(legacyPlan.Target, new Vector3(plan.TargetX, 0, plan.TargetZ)) <= DefenseRadius)
                return legacyPlan.DemonstrationPoint;
            // Demonstrate on the opposite side of the assault corridor. Only observed defender
            // movement in the existing feature extractor can confirm that the diversion worked.
            var demonstration = new Vector3(plan.TargetX + (plan.BaseApproachX >= plan.TargetX ? -1 : 1) * RallyRadius * 2, 0, plan.TargetZ);
            if (refreshedBoard?.MapCreate != null)
            { demonstration.x = Mathf.Clamp(demonstration.x, 0, refreshedBoard.MapCreate.maxX - 1); demonstration.z = Mathf.Clamp(demonstration.z, 0, refreshedBoard.MapCreate.maxZ - 1); }
            return demonstration;
        }
        if (plan.HasBaseApproachPoint && (step == AIOperationStepType.Approach || step == AIOperationStepType.Flank
            || step == AIOperationStepType.Diversion || actor.kind == Kind.Scout))
        {
            var approach = new Vector3(plan.BaseApproachX, 0, plan.BaseApproachZ);
            if (AIOperationFeatureExtractor.Distance(actor.transform.position, approach) > 1) return approach;
        }
        return new Vector3(plan.TargetX, 0, plan.TargetZ);
    }
    static uint LifeHash(string life)
    { uint hash = 2166136261; if (life != null) foreach (char character in life) hash = unchecked((hash ^ character) * 16777619); return hash; }
    static string BasePatternLabel(AIBaseAssaultPattern pattern) => pattern == AIBaseAssaultPattern.DirectAssault ? "正面攻略"
        : pattern == AIBaseAssaultPattern.ScoutThenAssault ? "偵察後に攻略"
        : pattern == AIBaseAssaultPattern.FlankAssault ? "側面から攻略"
        : pattern == AIBaseAssaultPattern.DiversionAssault ? "陽動しながら攻略"
        : pattern == AIBaseAssaultPattern.SiegeAssault ? "防衛排除後に攻略" : "経済施設を襲撃して撤退";
    float BaseAttackBonus(AIOperationPlan plan, AIAction action, AIBoardState board)
    {
        if (action.Unit == null || action.TargetUnit == null || plan.CurrentContext.ConfirmedTargetDestroyed
            || !features.TryObserved(action.TargetUnit.ReflectionLifeId, out var observedTarget) || observedTarget != action.TargetUnit
            || !board.IsVisibleToEnemy(observedTarget.transform.position)) return 0;
        if (observedTarget.ReflectionLifeId == plan.TargetLifeId)
        {
            if (action.ActionType == AIActionType.Attack && DamageCalculator.CalcNormal(action.Unit, observedTarget) >= observedTarget.HP) return 1.5f;
            var step = CurrentStep(plan)?.Type;
            if (plan.Status == AIOperationStatus.Preparing || step == AIOperationStepType.Scout || step == AIOperationStepType.Regroup) return 0;
            if (step == AIOperationStepType.Diversion && !plan.CurrentContext.DiversionConfirmed) return 0;
            // A guarded, healthy base can wait while its defenders are suppressed. A verified lethal
            // hit above remains worthwhile even in that situation; no special damage rule is added.
            return plan.EstimatedBaseDefense > AssignedAssaultForce(plan) ? .25f : 1;
        }
        return observedTarget.type == Type.Unit
            && AIOperationFeatureExtractor.Distance(observedTarget.transform.position, new Vector3(plan.TargetX, 0, plan.TargetZ)) <= DefenseRadius ? 1.25f : 0;
    }
    float BaseMovementBonus(AIOperationPlan plan, AIAction action, AIBoardState board, Vector3 destination, int before, int after)
    {
        float previousRisk = board.Belief.RiskAt(action.Unit.transform.position, board.ReconThreatLevel);
        float nextRisk = board.Belief.RiskAt(action.TargetPos, board.ReconThreatLevel);
        float riskIncrease = Mathf.Max(0, nextRisk - previousRisk) / Mathf.Max(1, action.Unit.HP);
        float progress = after < before ? 1 : after <= before + 1 && nextRisk < previousRisk ? .75f : 0;
        // A slightly longer, safer approach may outrank the exposed shortest step. This is only a
        // bounded hint within already legal candidates, so tactical survival and priorities still win.
        return Mathf.Clamp(progress - riskIncrease * AIOperationConfig.NonNegative(config.BaseRouteRiskWeight), 0, 1.5f);
    }
    float BaseProductionBonus(AIOperationPlan plan, AIAction action, AIBoardState board)
    {
        if (!plan.IsBaseAssault || plan.Status != AIOperationStatus.Preparing || plan.RequiredAssaultForce <= AssignedAssaultForce(plan)
            || plan.CurrentContext.ConfirmedTargetDestroyed || SupplyRecoveryRequired(board)
            || board.Governor?.Mode == StrategicMode.EmergencyDefense || action.SummonKind == Kind.King) return 0;
        return action.SummonKind == Kind.Scout ? CurrentStep(plan)?.Type == AIOperationStepType.Scout && !HasScout() ? 1 : 0 : 1;
    }
    void ReevaluateBaseAssault(AIOperationPlan plan, AIBoardState board, int turn)
    {
        if (!plan.IsBaseAssault) return;
        var entry = FindKnownBase(plan.TargetLifeId);
        if (entry == null || !ValidBasePlan(plan) || board.MapCreate != null
            && (plan.TargetX >= board.MapCreate.maxX || plan.TargetZ >= board.MapCreate.maxZ
                || plan.RallyX >= board.MapCreate.maxX || plan.RallyZ >= board.MapCreate.maxZ
                || plan.HasBaseApproachPoint && (plan.BaseApproachX >= board.MapCreate.maxX || plan.BaseApproachZ >= board.MapCreate.maxZ)))
        { Complete(plan, board, turn, AIOperationAbortReason.ObjectiveUnavailable); return; }
        plan.BaseIntelConfidence = entry.IntelConfidence;
        if (entry.ConfirmedDestroyed) { plan.CurrentContext.ConfirmedTargetDestroyed = true; return; }
        plan.EstimatedBaseDefense = entry.EstimatedDefense; plan.BaseDefenseKnown = entry.DefenseIntelKnown;
        plan.RequiredAssaultForce = RequiredBaseForce(entry);
        float assignedForce = AssignedAssaultForce(plan);
        plan.RequiredAdditionalCitizens = BaseReinforcementCitizenDemand(board, plan.RequiredAssaultForce - assignedForce);
        var approach = SelectBaseApproach(board, new Vector3(plan.TargetX, 0, plan.TargetZ), out bool confirmedRoute);
        plan.BaseRouteConfirmed = confirmedRoute; plan.HasBaseApproachPoint = true;
        plan.BaseApproachX = Mathf.RoundToInt(approach.x); plan.BaseApproachZ = Mathf.RoundToInt(approach.z);
        if (!entry.CurrentlyVisible && turn - entry.LastSeenTurn > config.BaseIntelFreshTurns && plan.Revisions.Count < config.RevisionLimit)
        {
            if (CurrentStep(plan)?.Type != AIOperationStepType.Scout) Revise(plan, turn, "拠点の防衛情報が古いため再偵察", true);
            plan.Status = AIOperationStatus.Preparing;
        }
        if (assignedForce < plan.RequiredAssaultForce && plan.Status != AIOperationStatus.Suspended) plan.Status = AIOperationStatus.Preparing;
        if (entry.CurrentlyVisible && entry.DefenseIntelKnown && confirmedRoute && CurrentStep(plan)?.Type == AIOperationStepType.Scout)
        { var step = CurrentStep(plan); step.Completed = true; step.EndTurn = turn; plan.CurrentStep++; plan.LastProgressTurn = turn; }
        if (CurrentStep(plan)?.Type == AIOperationStepType.Regroup && assignedForce >= plan.RequiredAssaultForce)
        {
            bool gathered = plan.AssignedUnitLifeIds.Count > 0;
            foreach (string life in plan.AssignedUnitLifeIds)
                if (!features.TryOwn(life, out var actor) || AIOperationFeatureExtractor.Distance(actor.transform.position, new Vector3(plan.RallyX, 0, plan.RallyZ)) > RallyRadius) gathered = false;
            if (gathered) { var step = CurrentStep(plan); step.Completed = true; step.EndTurn = turn; plan.CurrentStep++; plan.LastProgressTurn = turn; }
        }
    }
    bool BaseWithdrawalPending(AIOperationPlan plan, int turn)
    {
        if (!plan.IsBaseAssault || plan.BaseAssaultPattern != AIBaseAssaultPattern.RaidAndWithdraw || !plan.CurrentContext.ConfirmedTargetDestroyed) return false;
        bool alive = false, safe = true;
        foreach (string life in plan.AssignedUnitLifeIds)
            if (features.TryOwn(life, out var actor) && actor != null && actor.IsAlive)
            { alive = true; if (AIOperationFeatureExtractor.Distance(actor.transform.position, new Vector3(plan.RallyX, 0, plan.RallyZ)) > RallyRadius) safe = false; }
        if (alive && safe)
        {
            plan.BaseWithdrawalComplete = true;
            var last = plan.Steps.Count > 0 ? plan.Steps[plan.Steps.Count - 1] : null;
            if (last != null) { last.Completed = true; last.EndTurn = turn; }
            return false;
        }
        if (!alive || turn > plan.MaximumEndTurn) return false;
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
    static bool ValidBase(AIKnownEnemyBase entry) => entry != null && !string.IsNullOrEmpty(entry.StableTargetId) && entry.StableTargetId.Length <= 64
        && entry.X >= 0 && entry.Z >= 0 && entry.FirstSeenTurn >= 0 && entry.LastSeenTurn >= entry.FirstSeenTurn
        && KnownBaseType(entry.Type)
        && !float.IsNaN(entry.IntelConfidence) && !float.IsInfinity(entry.IntelConfidence)
        && entry.IntelConfidence >= 0 && entry.IntelConfidence <= 1 && entry.LastKnownHpRatio >= 0 && entry.LastKnownHpRatio <= 1
        && !float.IsNaN(entry.EstimatedStrategicValue) && !float.IsInfinity(entry.EstimatedStrategicValue) && entry.EstimatedStrategicValue >= 0
        && !float.IsNaN(entry.LastKnownHpRatio) && !float.IsInfinity(entry.LastKnownHpRatio)
        && entry.DiscoveryBonusTurn >= -1 && entry.DiscoveryBonusTurn <= entry.LastSeenTurn
        && entry.RediscoveryCount >= 0 && entry.RediscoveryCount <= 1000000
        && !float.IsNaN(entry.EstimatedDefense) && !float.IsInfinity(entry.EstimatedDefense) && entry.EstimatedDefense >= 0;
    void SanitizeKnownBases(AIOperationBattleState copy)
    {
        if (copy.KnownEnemyBases == null) copy.KnownEnemyBases = new List<AIKnownEnemyBase>();
        var ids = new HashSet<string>(); copy.KnownEnemyBases.RemoveAll(entry => !ValidBase(entry) || !ids.Add(entry.StableTargetId));
        if (copy.KnownEnemyBases.Count > BaseLimit) copy.KnownEnemyBases.RemoveRange(BaseLimit, copy.KnownEnemyBases.Count - BaseLimit);
        if (copy.BaseDiscoveryBits == null || copy.BaseDiscoveryBits.Length != 64) copy.BaseDiscoveryBits = new int[64];
        foreach (var entry in copy.KnownEnemyBases) entry.CurrentlyVisible = false;
        // Legacy payloads have no filter. Seed it from retained observations without inventing
        // discovery rewards; future reloads must not turn those same targets into first discoveries.
        foreach (var entry in copy.KnownEnemyBases) RecordBaseDiscovery(entry.StableTargetId, copy.BaseDiscoveryBits);
    }
    static bool ValidBasePlan(AIOperationPlan plan) => !plan.IsBaseAssault || !string.IsNullOrEmpty(plan.TargetLifeId) && plan.TargetLifeId.Length <= 64
        && plan.HasTargetPosition && plan.TargetX >= 0 && plan.TargetZ >= 0 && plan.RallyX >= 0 && plan.RallyZ >= 0
        && (!plan.HasBaseApproachPoint || plan.BaseApproachX >= 0 && plan.BaseApproachZ >= 0)
        && KnownAssaultPattern(plan.BaseAssaultPattern) && KnownBaseType(plan.BaseTargetType)
        && !float.IsNaN(plan.RequiredAssaultForce) && !float.IsInfinity(plan.RequiredAssaultForce) && plan.RequiredAssaultForce >= 0
        && !float.IsNaN(plan.EstimatedBaseDefense) && !float.IsInfinity(plan.EstimatedBaseDefense) && plan.EstimatedBaseDefense >= 0
        && !float.IsNaN(plan.BaseIntelConfidence) && !float.IsInfinity(plan.BaseIntelConfidence) && plan.BaseIntelConfidence >= 0 && plan.BaseIntelConfidence <= 1
        && plan.RequiredAdditionalCitizens >= 0 && plan.RequiredAdditionalCitizens <= 128;
    static bool KnownBaseType(AIEnemyBaseType type)
    {
        switch (type)
        {
            case AIEnemyBaseType.MainCrystal: case AIEnemyBaseType.SubCrystal: case AIEnemyBaseType.MilitaryFacility:
            case AIEnemyBaseType.EconomyFacility: case AIEnemyBaseType.ResourceFacility: case AIEnemyBaseType.SupportFacility: return true;
            default: return false;
        }
    }
    static bool KnownAssaultPattern(AIBaseAssaultPattern pattern)
    {
        switch (pattern)
        {
            case AIBaseAssaultPattern.DirectAssault: case AIBaseAssaultPattern.ScoutThenAssault: case AIBaseAssaultPattern.FlankAssault:
            case AIBaseAssaultPattern.DiversionAssault: case AIBaseAssaultPattern.SiegeAssault: case AIBaseAssaultPattern.RaidAndWithdraw: return true;
            default: return false;
        }
    }
}
