using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bounded, observation-driven operation ownership above the unchanged tactical AI. An operation
/// supplies a small score hint, never legal-action restrictions or a strategic priority override.
/// </summary>
public sealed class AIOperationManager
{
    readonly AIOperationConfig config;
    readonly AIOperationFeatureExtractor features;
    readonly AIOperationScorer scorer;
    readonly List<AIOperationCandidate> candidates = new List<AIOperationCandidate>(16);
    readonly Dictionary<string, AIOperationPlan> assignments = new Dictionary<string, AIOperationPlan>();
    readonly HashSet<string> observedLives = new HashSet<string>();
    readonly HashSet<string> destroyedLives = new HashSet<string>();
    readonly HashSet<string> artifactEvents = new HashSet<string>();
    readonly HashSet<long> completedIds = new HashSet<long>();
    readonly List<AIOperationActionCredit> credits = new List<AIOperationActionCredit>();
    readonly List<Status> ownArmy = new List<Status>(32);
    readonly List<AIOperationPlan> finished = new List<AIOperationPlan>(3);
    AIOperationBattleState state;
    TurnStrategy strategy;
    AIPlan legacyPlan;
    int refreshedGeneration = -1;
    AIBoardState refreshedBoard;
    AIPersonality personality;
    AIPlayerModel.Profile playerModel;

    public AIOperationManager(AIOperationConfig config = null, AIOperationLearningProfile profile = null)
    {
        this.config = config != null ? config : AIOperationConfig.Active;
        features = new AIOperationFeatureExtractor(this.config);
        scorer = new AIOperationScorer(this.config);
        Profile = profile ?? new AIOperationLearningProfile();
        state = new AIOperationBattleState { LearningProfile = Profile };
    }
    public AIOperationLearningProfile Profile { get; private set; }
    public AIOperationBattleState State => state;
    public int OwnTurns => state.OwnTurns;
    public IReadOnlyList<AIOperationCandidate> LastCandidates => candidates;
    public IReadOnlyList<AIOperationPlan> ActiveOperations => state.ActiveOperations;
    public AIOperationPlan PrimaryOperation
    {
        get { foreach (var plan in state.ActiveOperations) if (plan.IsPrimary && Running(plan)) return plan; return null; }
    }

    public void BeginBattle(Team faction, string battleId, AIOperationBattleState resume = null,
        AIOperationLearningProfile profile = null)
    {
        if (profile != null) Profile = profile;
        if (resume != null && resume.Version == 1 && resume.Faction == faction
            && (string.IsNullOrEmpty(battleId) || resume.BattleId == battleId))
        { Restore(resume); return; }
        state = new AIOperationBattleState { Faction = faction,
            BattleId = string.IsNullOrEmpty(battleId) ? Guid.NewGuid().ToString("N") : battleId,
            LearningProfile = Profile };
        assignments.Clear(); observedLives.Clear(); destroyedLives.Clear(); artifactEvents.Clear(); completedIds.Clear(); credits.Clear();
        features.Invalidate();
    }

    public void BeginTurn(AIBoardState board, TurnStrategy strategy, int ownTurn, AIPlan legacyPlan = null,
        AIPersonality personality = null, AIPlayerModel.Profile playerModel = null)
    {
        if (board == null || !config.Enabled || state.Ended || board.ReconThreatLevel < Mathf.Max(1, config.MinimumThreatLevel)) return;
        ownTurn = Mathf.Max(1, ownTurn);
        this.strategy = strategy; this.legacyPlan = legacyPlan;
        this.personality = personality; this.playerModel = playerModel;
        if (state.LastStartedOwnTurn == ownTurn) return;
        if (ownTurn <= state.OwnTurns) return;
        state.LastStartedOwnTurn = ownTurn; state.LastStartedGlobalTurn = board.TurnCount;
        features.Prepare(board, ownTurn); SeedObservations(board, ownTurn);
        ownArmy.Clear();
        foreach (var actor in features.OwnUnits(board, ownTurn))
            if (actor != null && actor.IsAlive && actor.type == Type.Unit && actor.kind != Kind.King)
                ownArmy.Add(actor);
        ownArmy.Sort((a, b) => string.CompareOrdinal(a.ReflectionLifeId, b.ReflectionLifeId));
        finished.Clear();
        foreach (var plan in state.ActiveOperations)
        {
            plan.CurrentContext = features.Capture(board, plan, ownTurn);
            Reevaluate(plan, board, ownTurn);
            if (!Running(plan)) finished.Add(plan);
        }
        RemoveFinished();
        assignments.Clear();
        foreach (var plan in state.ActiveOperations)
            foreach (string life in plan.AssignedUnitLifeIds)
                if (plan.Status != AIOperationStatus.Suspended)
                if (features.TryOwn(life, out var unit) && unit != null && unit.IsAlive && !assignments.ContainsKey(life))
                    assignments.Add(life, plan);
        GenerateCandidates(board, ownTurn, personality, playerModel);
        var main = PrimaryOperation;
        AIOperationCandidate preferred = SelectCandidate();
        if (preferred != null && (main == null || preferred.Priority < main.StrategicPriority))
        {
            if (main != null)
            {
                Suspend(main, ownTurn, preferred.Priority <= 1 ? AIOperationAbortReason.CrystalEmergency
                    : AIOperationAbortReason.EconomicEmergency);
            }
            Start(preferred.Plan, board, ownTurn, true);
        }
        else if (main == null) PromoteExisting();
        AddAuxiliaryScout(board, ownTurn);
        EnsureOnePrimary();
        foreach (var plan in state.ActiveOperations) Assign(plan, board, ownTurn);
        refreshedBoard = board; refreshedGeneration = board.Generation;
    }

    /// <summary>Refresh current-turn observations without counting a new own turn or replacing retained plans.</summary>
    public void Refresh(AIBoardState board, TurnStrategy strategy, AIPlan legacyPlan = null)
    {
        if (board == null || !config.Enabled || state.Ended || state.LastStartedOwnTurn < 1
            || board.ReconThreatLevel < Mathf.Max(1, config.MinimumThreatLevel)) return;
        this.strategy = strategy; this.legacyPlan = legacyPlan;
        if (refreshedBoard == board && refreshedGeneration == board.Generation) return;
        int turn = state.LastStartedOwnTurn;
        features.Prepare(board, turn);
        ownArmy.Clear();
        foreach (var actor in features.OwnUnits(board, turn))
            if (actor != null && actor.IsAlive && actor.type == Type.Unit && actor.kind != Kind.King) ownArmy.Add(actor);
        ownArmy.Sort((a, b) => string.CompareOrdinal(a.ReflectionLifeId, b.ReflectionLifeId));
        foreach (var plan in state.ActiveOperations)
        {
            plan.CurrentContext = features.Capture(board, plan, turn);
            Reevaluate(plan, board, turn);
        }
        RemoveFinished();
        GenerateCandidates(board, turn, personality, playerModel);
        var primary = PrimaryOperation;
        var candidate = SelectCandidate();
        if (candidate != null && (primary == null || candidate.Priority < primary.StrategicPriority))
        {
            if (primary != null) Suspend(primary, turn, candidate.Priority <= 1
                ? AIOperationAbortReason.CrystalEmergency : AIOperationAbortReason.EconomicEmergency);
            Start(candidate.Plan, board, turn, true);
        }
        EnsureOnePrimary();
        foreach (var plan in state.ActiveOperations) Assign(plan, board, turn);
        refreshedBoard = board; refreshedGeneration = board.Generation;
    }

    public float ActionBonus(AIAction action, AIBoardState board)
    {
        if (action == null || board == null || !config.Enabled || state.Ended) return 0;
        var plan = Match(action, board);
        if (plan == null || plan.Status == AIOperationStatus.Suspended) return 0;
        float bonus = 0;
        var step = CurrentStep(plan);
        if (plan.PrimaryGoal == AIOperationGoal.EconomicRecovery)
        {
            if (action.ActionType == AIActionType.Build || action.ActionType == AIActionType.Upgrade)
            {
                var assessment = board.ProductionDemand?.EvaluateBuild(action);
                if (assessment.HasValue && assessment.Value.ImprovesDeficit) bonus = 1;
            }
        }
        else if (action.ActionType == AIActionType.Attack || action.ActionType == AIActionType.SkillUse)
        {
            // The target may have left visibility since candidate generation. Inspect it only through
            // the current observed index, and do not chase a hidden object's updated position.
            features.Prepare(board, state.LastStartedOwnTurn);
            if (action.TargetUnit != null && board.AlivePlayerUnits.Contains(action.TargetUnit)
                && (string.IsNullOrEmpty(plan.TargetLifeId) || action.TargetUnit.ReflectionLifeId == plan.TargetLifeId)) bonus = 1;
            if (step?.Type == AIOperationStepType.Attack || step?.Type == AIOperationStepType.DestroyObjective) bonus += .5f;
        }
        else if (IsMovement(action.ActionType) && action.Unit != null)
        {
            if (plan.HasTargetPosition)
            {
                Vector3 destination = MovementAim(plan, action.Unit);
                int before = AIOperationFeatureExtractor.Distance(action.Unit.transform.position, destination);
                int after = AIOperationFeatureExtractor.Distance(action.TargetPos, destination);
                if (after < before) bonus = 1;
            }
            if ((plan.PrimaryGoal == AIOperationGoal.ScoutRegion || plan.PrimaryGoal == AIOperationGoal.LocateEnemyMainForce)
                && action.Unit.kind == Kind.Scout && board.Exploration.IsObjectiveProgress(action.Unit, action.TargetPos, board)) bonus += .5f;
            if (step?.Type == AIOperationStepType.Defense && action.ActionType == AIActionType.DefenseRepos) bonus += .5f;
        }
        else if (action.ActionType == AIActionType.SubCrystal
            && (plan.PrimaryGoal == AIOperationGoal.CaptureTerritory || plan.PrimaryGoal == AIOperationGoal.ObtainArtifact)) bonus = 1;
        return Mathf.Clamp(bonus, 0, Mathf.Clamp(AIOperationConfig.Finite(config.MaxActionBonus), 0, 3));
    }

    /// <summary>Attribution starts before execution. No credit or progress is inferred here.</summary>
    public void TagAction(AIActionRecord record, AIAction action)
    {
        if (record == null || action == null || !config.Enabled || state.Ended) return;
        var plan = Match(action, null);
        if (plan == null || plan.Status == AIOperationStatus.Suspended) return;
        record.OperationId = plan.OperationId;
        record.OperationStepId = CurrentStep(plan)?.StepId ?? 0;
        record.OperationContribution = 0;
    }

    /// <summary>Called only after authoritative action execution and reflection outcome capture.</summary>
    public void ObserveAction(AIActionRecord record, AIBoardState board)
    {
        if (record == null || !config.Enabled || state.Ended || record.ActionId <= 0) return;
        int ownTurn = Mathf.Max(1, state.LastStartedOwnTurn);
        var plan = FindActive(record.OperationId);
        int newContacts = 0;
        if (record.Outcome?.NewEnemyLifeIds != null)
            foreach (string life in record.Outcome.NewEnemyLifeIds)
                if (!string.IsNullOrEmpty(life) && observedLives.Count < config.EventLimit && observedLives.Add(life)) newContacts++;
        // Destruction is never inferred from missing contact, absent tile occupant or zero HP display.
        if (record.Outcome?.KilledLifeIds != null)
            foreach (string life in record.Outcome.KilledLifeIds)
            {
                if (string.IsNullOrEmpty(life)) continue;
                if (destroyedLives.Count < config.EventLimit) destroyedLives.Add(life);
                foreach (var active in state.ActiveOperations)
                    if (life == active.TargetLifeId)
                    {
                        if (active.OperationId == record.OperationId)
                        { active.CurrentContext.ConfirmedTargetDestroyed = true; active.LastProgressTurn = ownTurn; }
                        else Complete(active, board, ownTurn, AIOperationAbortReason.ObjectiveDestroyedByOther);
                    }
            }
        if (plan == null || !Running(plan)) return;
        if (plan.ActionIds.Contains(record.ActionId)) return;
        if (plan.ActionIds.Count >= config.RecordLimit) plan.ActionIds.RemoveAt(0);
        plan.ActionIds.Add(record.ActionId);
        if (plan.ActionEvidence.Count >= config.RecordLimit) plan.ActionEvidence.RemoveAt(0);
        plan.ActionEvidence.Add(record);
        var step = FindStep(plan, record.OperationStepId);
        if (step != null)
        {
            if (step.ActionIds.Count >= config.RecordLimit) step.ActionIds.RemoveAt(0);
            step.ActionIds.Add(record.ActionId);
        }
        var outcome = record.Outcome;
        if (record.ExecutionSucceeded && outcome != null)
        {
            var context = plan.CurrentContext;
            context.ApSpent += Mathf.Max(0, record.Before?.ActionApCost ?? 0);
            context.OwnLosses += Mathf.Max(0, outcome.OwnLosses);
            context.EnemyKills += Mathf.Max(0, outcome.EnemyKills);
            context.EnemyKilledPower += Mathf.Max(0, outcome.EnemyKills) * AIOperationConfig.NonNegative(config.EstimatedKillPower);
            int acquisitions = 0;
            foreach (string eventId in outcome.ArtifactEventIds)
                if (!string.IsNullOrEmpty(eventId) && artifactEvents.Count < config.EventLimit && artifactEvents.Add(eventId)) acquisitions++;
            context.ConfirmedArtifactAcquisitions += acquisitions;
            context.ArtifactCount += acquisitions;
            context.NewEnemyContacts += newContacts;
            context.ResourceSpendValue += ResourceSpend(outcome);
            if (record.ActionType == AIActionType.Build || record.ActionType == AIActionType.Upgrade)
                context.ResourceInvestment += ResourceSpend(outcome);
            if (record.MeaningfulProgress && !string.IsNullOrEmpty(record.ActorLifeId)
                && plan.AssignedUnitLifeIds.Contains(record.ActorLifeId) && !plan.UtilizedUnitLifeIds.Contains(record.ActorLifeId))
                plan.UtilizedUnitLifeIds.Add(record.ActorLifeId);
            ConfirmTacticalGoal(plan, record, board);
            record.OperationContribution = Contribution(record, plan);
            if (record.OperationContribution > 0 && record.PreparationCandidate)
                context.PreparationContribution = Mathf.Clamp01(context.PreparationContribution + record.OperationContribution * .25f);
            if (outcome.DistanceToObjectiveDelta > 0 || outcome.NewTilesRevealed > 0 || outcome.DamageDealt > 0
                || outcome.ArtifactsAcquired > 0 || outcome.OwnBuildingDelta > 0
                || outcome.EconomyAfter < outcome.EconomyBefore || outcome.EnemyKills > 0)
                plan.LastProgressTurn = ownTurn;
            ConfirmStep(plan, step, record, ownTurn);
        }
        plan.CurrentContext = features.Capture(board, plan, ownTurn);
        ConfirmTargetForce(plan);
        if (scorer.Evaluate(plan, plan.StartContext, plan.CurrentContext, plan.ActionEvidence, true).PrimaryGoalAchieved)
            Complete(plan, board, ownTurn, AIOperationAbortReason.None);
        RemoveFinished();
    }

    public void EndTurn(AIBoardState board, int ownTurn)
    {
        if (!config.Enabled || state.Ended || ownTurn <= state.OwnTurns || state.LastEndedOwnTurn == ownTurn) return;
        state.OwnTurns = ownTurn; state.LastEndedOwnTurn = ownTurn;
        foreach (var plan in state.ActiveOperations)
        {
            plan.CurrentContext = features.Capture(board, plan, ownTurn);
            Reevaluate(plan, board, ownTurn);
            if (Running(plan)) plan.ProvisionalScore = scorer.Evaluate(plan, plan.StartContext,
                plan.CurrentContext, plan.ActionEvidence, true).Breakdown;
        }
        RemoveFinished();
    }

    public void EndBattle(AIBoardState board, string result)
    {
        if (state.Ended) return;
        int turn = Mathf.Max(state.OwnTurns, state.LastStartedOwnTurn);
        state.OwnTurns = Mathf.Max(0, turn);
        foreach (var plan in state.ActiveOperations)
        {
            plan.CurrentContext = features.Capture(board, plan, turn);
            Complete(plan, board, turn, AIOperationAbortReason.None);
        }
        RemoveFinished(); state.Ended = true;
    }

    public List<AIOperationActionCredit> DrainCredits()
    {
        var result = new List<AIOperationActionCredit>(credits);
        credits.Clear(); return result;
    }

    public void NotifyTargetDestroyed(string lifeId)
    {
        if (string.IsNullOrEmpty(lifeId) || !config.Enabled || state.Ended || destroyedLives.Contains(lifeId)) return;
        if (destroyedLives.Count < config.EventLimit) destroyedLives.Add(lifeId);
        foreach (var plan in state.ActiveOperations)
            if (plan.TargetLifeId == lifeId)
                Complete(plan, null, Mathf.Max(0, state.LastStartedOwnTurn), AIOperationAbortReason.ObjectiveDestroyedByOther);
        RemoveFinished();
    }

    public void NotifyArtifactAcquired(string eventId)
    {
        if (string.IsNullOrEmpty(eventId) || !config.Enabled || state.Ended || artifactEvents.Count >= config.EventLimit
            || !artifactEvents.Add(eventId)) return;
        foreach (var plan in state.ActiveOperations)
            if (plan.PrimaryGoal == AIOperationGoal.ObtainArtifact)
            {
                plan.CurrentContext.ConfirmedArtifactAcquisitions++;
                plan.CurrentContext.ArtifactCount++;
                Complete(plan, null, Mathf.Max(0, state.LastStartedOwnTurn), AIOperationAbortReason.None);
                break;
            }
        RemoveFinished();
    }

    public AIOperationBattleState Snapshot(bool includeProfile = true)
    {
        state.LearningProfile = includeProfile ? Profile : null;
        state.ObservedEnemyLifeIds.Clear(); state.ObservedEnemyLifeIds.AddRange(observedLives);
        state.ConfirmedDestroyedLifeIds.Clear(); state.ConfirmedDestroyedLifeIds.AddRange(destroyedLives);
        state.ArtifactEventIds.Clear(); state.ArtifactEventIds.AddRange(artifactEvents);
        var snapshot = JsonUtility.FromJson<AIOperationBattleState>(JsonUtility.ToJson(state));
        state.LearningProfile = Profile;
        return snapshot;
    }

    public void Restore(AIOperationBattleState value)
    {
        if (value == null || value.Version != 1 || value.OwnTurns < 0 || value.OwnTurns > 1000000
            || value.ActiveOperations == null || value.ActiveOperations.Count > config.ConcurrentLimit
            || value.CompletedOperations == null || value.CompletedOperations.Count > config.CompletedLimit) return;
        var copy = JsonUtility.FromJson<AIOperationBattleState>(JsonUtility.ToJson(value));
        assignments.Clear(); observedLives.Clear(); destroyedLives.Clear(); artifactEvents.Clear(); completedIds.Clear(); credits.Clear();
        copy.ActiveOperations.RemoveAll(p => !ValidPlan(p));
        copy.CompletedOperations.RemoveAll(p => !ValidPlan(p));
        foreach (var plan in copy.ActiveOperations)
            if (plan.ActionEvidence.Count > config.RecordLimit) plan.ActionEvidence.RemoveRange(0, plan.ActionEvidence.Count - config.RecordLimit);
        copy.ObservedEnemyLifeIds = SanitizeLives(copy.ObservedEnemyLifeIds);
        copy.ConfirmedDestroyedLifeIds = SanitizeLives(copy.ConfirmedDestroyedLifeIds);
        copy.ArtifactEventIds = SanitizeLives(copy.ArtifactEventIds);
        foreach (string life in copy.ObservedEnemyLifeIds) observedLives.Add(life);
        foreach (string life in copy.ConfirmedDestroyedLifeIds) destroyedLives.Add(life);
        foreach (string eventId in copy.ArtifactEventIds) artifactEvents.Add(eventId);
        copy.CompletedOperationIds = copy.CompletedOperationIds ?? new List<long>();
        if (copy.CompletedOperationIds.Count > config.EventLimit)
            copy.CompletedOperationIds.RemoveRange(0, copy.CompletedOperationIds.Count - config.EventLimit);
        foreach (long id in copy.CompletedOperationIds) if (id > 0) completedIds.Add(id);
        foreach (var plan in copy.ActiveOperations) copy.NextOperationId = Math.Max(copy.NextOperationId, plan.OperationId);
        foreach (var plan in copy.CompletedOperations) copy.NextOperationId = Math.Max(copy.NextOperationId, plan.OperationId);
        foreach (long id in copy.CompletedOperationIds) copy.NextOperationId = Math.Max(copy.NextOperationId, id);
        if (copy.LearningProfile != null) { Profile = copy.LearningProfile; Profile.RebuildIndex(); }
        copy.LearningProfile = Profile; state = copy;
        EnsureOnePrimary(); features.Invalidate();
    }

    bool ValidPlan(AIOperationPlan plan) => plan != null && plan.OperationId > 0
        && Enum.IsDefined(typeof(AIOperationGoal), plan.PrimaryGoal) && Enum.IsDefined(typeof(AIOperationStatus), plan.Status)
        && plan.StartContext != null && plan.CurrentContext != null && plan.Steps != null && plan.Steps.Count <= config.StepLimit
        && plan.AssignedUnitLifeIds != null && plan.AssignedUnitLifeIds.Count <= config.AssignmentLimit
        && plan.AssignedUnitRoles != null && plan.OriginalAssignedUnitLifeIds != null && plan.UtilizedUnitLifeIds != null
        && plan.ActionIds != null && plan.ActionEvidence != null && plan.CreditedActionIds != null
        && plan.Revisions != null && plan.Revisions.Count <= config.RevisionLimit;

    List<string> SanitizeLives(List<string> source)
    {
        var result = new List<string>(); var unique = new HashSet<string>();
        if (source != null) foreach (string life in source)
            if (!string.IsNullOrEmpty(life) && life.Length <= 128 && result.Count < config.EventLimit && unique.Add(life)) result.Add(life);
        return result;
    }

    void GenerateCandidates(AIBoardState board, int turn, AIPersonality personality, AIPlayerModel.Profile player)
    {
        candidates.Clear(); var facts = features.Capture(board, null, turn);
        bool emergency = facts.OwnCrystalThreatened || strategy == TurnStrategy.CrystalDefense;
        if (emergency)
        {
            var ownCrystal = board.OwnCrystalParent?.GetComponentInChildren<Status>();
            AddCandidate(board, turn, AIOperationGoal.DefendOwnCrystal, 0, 90, ownCrystal, personality, player);
            if (facts.VisibleEnemyUnits > 0)
                AddCandidate(board, turn, AIOperationGoal.EliminateEnemyForce, 1, 65, FirstObservedTroop(board, turn), personality, player);
            return;
        }
        if (facts.EconomyState != EconomicState.Healthy || board.Governor?.Mode == StrategicMode.EconomicRecovery)
            AddCandidate(board, turn, AIOperationGoal.EconomicRecovery, 2, 80, null, personality, player);
        if (strategy != TurnStrategy.ScoutSearch && strategy != TurnStrategy.EconomyBuild && strategy != TurnStrategy.RetreatRegroup)
        {
            Status bestTarget = null; float bestValue = float.NegativeInfinity;
            foreach (var target in features.ObservedUnits(board, turn))
            {
                float value = target.kind == Kind.SubCrystal ? 75 : target.kind == Kind.Crystal ? 70
                    : target.type != Type.Unit ? 40 : target.kind == Kind.King ? 60 : 30;
                value -= AIOperationFeatureExtractor.Distance(board.EnemyCrystalPos, target.transform.position);
                if (value > bestValue) { bestValue = value; bestTarget = target; }
            }
            if (bestTarget != null)
            {
                var goal = bestTarget.kind == Kind.SubCrystal ? AIOperationGoal.DestroySubCrystal
                    : bestTarget.kind == Kind.Crystal ? AIOperationGoal.DestroyEnemyCrystal
                    : bestTarget.type == Type.Unit ? AIOperationGoal.EliminateEnemyForce : AIOperationGoal.BreakEnemyDefense;
                AddCandidate(board, turn, goal, 3, bestValue + 15, bestTarget, personality, player);
                if (facts.VisibleEnemyUnits > 0)
                {
                    AddCandidate(board, turn, AIOperationGoal.FlankEnemyForce, 3, bestValue + 10, bestTarget, personality, player);
                    AddCandidate(board, turn, AIOperationGoal.SurroundEnemyForce, 3, bestValue + 8, bestTarget, personality, player);
                    if (legacyPlan?.Goal == AIPlanGoal.EasternFeint && legacyPlan.Active)
                        AddCandidate(board, turn, AIOperationGoal.CreateDiversion, 3, bestValue + 13, bestTarget, personality, player);
                }
            }
        }
        // Dungeon objects are queried through the board's observed-only view. Reward type or
        // current control from unseen dungeons can never enter operation planning.
        foreach (var dungeon in board.ObservedDungeons())
        {
            if (dungeon.ClaimingTeam == board.ActorTeam && dungeon.SharedRemainingTurns > 0)
            {
                var candidate = AddCandidate(board, turn, AIOperationGoal.ObtainArtifact, 3, 55, null, personality, player);
                if (candidate != null)
                {
                    candidate.Plan.HasTargetPosition = true; candidate.Plan.TargetX = dungeon.Position.x;
                    candidate.Plan.TargetZ = dungeon.Position.z; candidate.Plan.TargetCategory = "Dungeon";
                    RefreshCandidateContext(candidate, board, turn);
                }
            }
        }
        if (HasScout())
        {
            AddCandidate(board, turn, facts.VisibleEnemyUnits == 0 ? AIOperationGoal.ScoutRegion
                : AIOperationGoal.LocateEnemyMainForce, facts.VisibleEnemyUnits == 0 ? 3 : 4,
                strategy == TurnStrategy.ScoutSearch ? 70 : 45, null, personality, player);
        }
        candidates.Sort(AIOperationCandidate.Compare);
        if (config.EnableDebug)
            foreach (var candidate in candidates)
                DevelopmentLog.Log($"[AI Operation] P{candidate.Priority} Goal={candidate.Plan.PrimaryGoal} Pattern={candidate.Plan.PatternKey} Base={candidate.BaseScore:F2} Experience={candidate.Experience:F2} PlayerModel={candidate.PlayerModel:F2} Personality={candidate.Personality:F2} Risk={candidate.Risk:F2} Final={candidate.FinalScore:F2}");
    }

    AIOperationCandidate AddCandidate(AIBoardState board, int turn, AIOperationGoal goal, int priority,
        float baseScore, Status target, AIPersonality personality, AIPlayerModel.Profile player)
    {
        if (candidates.Count >= 16) return null;
        var plan = new AIOperationPlan { PrimaryGoal = goal, OriginalGoal = goal, StrategicPriority = priority,
            BattleId = state.BattleId,
            StartTurn = turn, LastProgressTurn = turn, ExpectedEndTurn = turn + Mathf.Max(1, config.DefaultExpectedDuration) - 1,
            MaximumEndTurn = turn + Mathf.Max(config.DefaultExpectedDuration, config.DefaultMaximumDuration) - 1,
            Status = AIOperationStatus.Proposed, StrategicReason = Reason(goal),
            TargetNewTiles = Mathf.Max(1, config.DefaultScoutTargetTiles), TargetTerritoryGain = Mathf.Max(1, config.DefaultTerritoryGain),
            TargetPowerReduction = AIOperationConfig.Unit(config.DefaultTargetPowerReduction),
            ExpectedApBudget = Mathf.Max(1, AIOperationConfig.Finite(config.DefaultExpectedApBudget, 40)) };
        if (target != null)
        {
            plan.TargetLifeId = target.ReflectionLifeId; plan.TargetCategory = AIOperationFeatureExtractor.Category(target);
            plan.HasTargetPosition = true; plan.TargetX = Mathf.RoundToInt(target.transform.position.x);
            plan.TargetZ = Mathf.RoundToInt(target.transform.position.z);
        }
        else if (goal == AIOperationGoal.ScoutRegion || goal == AIOperationGoal.LocateEnemyMainForce)
        {
            foreach (var unit in ownArmy)
                if (unit.kind == Kind.Scout && board.Exploration.TryGetAssignedTarget(unit, out var point))
                { plan.HasTargetPosition = true; plan.TargetX = Mathf.RoundToInt(point.x); plan.TargetZ = Mathf.RoundToInt(point.z); break; }
            plan.TargetCategory = "Region";
        }
        else plan.TargetCategory = "Economy";
        plan.Steps = CreateSteps(goal, turn);
        plan.PatternKey = Pattern(plan.Steps);
        var candidate = new AIOperationCandidate { Plan = plan, Priority = priority, BaseScore = baseScore,
            Personality = PersonalityBonus(goal, personality), PlayerModel = PlayerBonus(goal, player) };
        RefreshCandidateContext(candidate, board, turn);
        candidates.Add(candidate); return candidate;
    }

    void RefreshCandidateContext(AIOperationCandidate candidate, AIBoardState board, int turn)
    {
        candidate.Plan.StartContext = features.Capture(board, candidate.Plan, turn);
        candidate.Plan.CurrentContext = candidate.Plan.StartContext.Copy();
        candidate.Plan.ContextKey = AIOperationFeatureExtractor.ContextKey(candidate.Plan, candidate.Plan.StartContext);
        candidate.Experience = Profile.GetModifier(candidate.Plan.ContextKey, candidate.Plan.PrimaryGoal,
            candidate.Plan.PatternKey, config);
        float knownRisk = candidate.Plan.StartContext.EnemyStrengthKnown
            ? candidate.Plan.StartContext.EnemyObservedPower / Mathf.Max(1, candidate.Plan.StartContext.OwnMilitaryPower) : 0;
        candidate.Risk = candidate.Priority <= 2 ? 0 : -Mathf.Min(5, knownRisk * 2);
    }

    AIOperationCandidate SelectCandidate()
    {
        AIOperationCandidate best = null;
        foreach (var candidate in candidates)
        {
            bool duplicate = false;
            foreach (var existing in state.ActiveOperations)
                if (Running(existing) && existing.PrimaryGoal == candidate.Plan.PrimaryGoal
                    && existing.TargetLifeId == candidate.Plan.TargetLifeId) { duplicate = true; break; }
            if (duplicate) continue;
            if (best == null || AIOperationCandidate.Compare(candidate, best) < 0) best = candidate;
        }
        return best;
    }

    void Start(AIOperationPlan plan, AIBoardState board, int turn, bool primary)
    {
        if (state.ActiveOperations.Count >= config.ConcurrentLimit)
        {
            AIOperationPlan leastImportant = null;
            foreach (var active in state.ActiveOperations)
                if (!active.IsPrimary && (leastImportant == null || active.StrategicPriority > leastImportant.StrategicPriority)) leastImportant = active;
            if (leastImportant == null) return;
            Complete(leastImportant, board, turn, AIOperationAbortReason.StrategicPriorityChanged); RemoveFinished();
        }
        if (state.NextOperationId >= long.MaxValue - 1) return;
        plan.OperationId = ++state.NextOperationId; plan.BattleId = state.BattleId;
        plan.IsPrimary = primary; plan.Status = AIOperationStatus.Preparing;
        state.ActiveOperations.Add(plan); Assign(plan, board, turn);
        plan.StartContext = features.Capture(board, plan, turn);
        plan.OriginalStartContext = plan.StartContext.Copy();
        plan.CurrentContext = plan.StartContext.Copy();
        plan.ContextKey = AIOperationFeatureExtractor.ContextKey(plan, plan.StartContext);
        if (config.EnableDebug) DevelopmentLog.Log($"[AI Operation] 開始 #{plan.OperationId} Goal={plan.PrimaryGoal} P{plan.StrategicPriority}");
    }

    void AddAuxiliaryScout(AIBoardState board, int turn)
    {
        if (state.ActiveOperations.Count >= config.ConcurrentLimit || !HasScout()) return;
        foreach (var plan in state.ActiveOperations)
            if (Running(plan) && (plan.PrimaryGoal == AIOperationGoal.ScoutRegion || plan.PrimaryGoal == AIOperationGoal.LocateEnemyMainForce)) return;
        foreach (var candidate in candidates)
            if (candidate.Plan.PrimaryGoal == AIOperationGoal.ScoutRegion || candidate.Plan.PrimaryGoal == AIOperationGoal.LocateEnemyMainForce)
            { Start(candidate.Plan, board, turn, false); break; }
    }

    void Assign(AIOperationPlan plan, AIBoardState board, int turn)
    {
        if (!Running(plan) || plan.Status == AIOperationStatus.Suspended) return;
        // Half of the combat force at most, with two scouts at most. The remainder retain individual
        // tactical, reserve, emergency and economic decisions in the existing commander.
        bool scout = plan.PrimaryGoal == AIOperationGoal.ScoutRegion || plan.PrimaryGoal == AIOperationGoal.LocateEnemyMainForce;
        int quota = plan.PrimaryGoal == AIOperationGoal.EconomicRecovery ? 0
            : scout ? Mathf.Min(2, config.AssignmentLimit) : Mathf.Min(config.AssignmentLimit, Mathf.Max(1, (ownArmy.Count + 1) / 2));
        for (int i = plan.AssignedUnitLifeIds.Count - 1; i >= 0; i--)
            if (!features.TryOwn(plan.AssignedUnitLifeIds[i], out var current) || current == null || !current.IsAlive
                || assignments.TryGetValue(plan.AssignedUnitLifeIds[i], out var owner) && owner != plan)
                plan.AssignedUnitLifeIds.RemoveAt(i);
        foreach (var actor in ownArmy)
        {
            if (plan.AssignedUnitLifeIds.Count >= quota) break;
            string life = actor.ReflectionLifeId;
            if (assignments.ContainsKey(life) || actor.HP < actor.MaxHP * .4f || scout != (actor.kind == Kind.Scout)) continue;
            plan.AssignedUnitLifeIds.Add(life);
            if (!plan.OriginalAssignedUnitLifeIds.Contains(life)) plan.OriginalAssignedUnitLifeIds.Add(life);
            string role = AIActionFeatureExtractor.Role(actor.kind);
            if (!plan.AssignedUnitRoles.Contains(role)) plan.AssignedUnitRoles.Add(role);
            assignments[life] = plan;
        }
        foreach (string life in plan.AssignedUnitLifeIds) if (!assignments.ContainsKey(life)) assignments[life] = plan;
        if (plan.Status == AIOperationStatus.Preparing && (quota == 0 || plan.AssignedUnitLifeIds.Count > 0)) plan.Status = AIOperationStatus.Active;
    }

    void Reevaluate(AIOperationPlan plan, AIBoardState board, int turn)
    {
        var facts = plan.CurrentContext;
        ConfirmTargetForce(plan);
        ConfirmDefenseResolved(plan);
        var evaluation = scorer.Evaluate(plan, plan.StartContext, facts, plan.ActionEvidence, true);
        plan.ProvisionalScore = evaluation.Breakdown;
        if (evaluation.PrimaryGoalAchieved) { Complete(plan, board, turn, AIOperationAbortReason.None); return; }
        bool emergency = facts.OwnCrystalThreatened || strategy == TurnStrategy.CrystalDefense;
        if (emergency && plan.StrategicPriority > 1)
        {
            Suspend(plan, turn, AIOperationAbortReason.CrystalEmergency);
            return;
        }
        if (facts.EconomyState == EconomicState.Collapse && plan.StrategicPriority > 2)
        {
            Suspend(plan, turn, AIOperationAbortReason.EconomicEmergency);
            return;
        }
        if (plan.Status == AIOperationStatus.Suspended)
        {
            int paused = Mathf.Max(0, turn - plan.SuspendedOwnTurn);
            plan.ExpectedEndTurn += paused; plan.MaximumEndTurn += paused; plan.LastProgressTurn += paused;
            plan.SuspendedOwnTurn = -1;
            plan.Status = AIOperationStatus.Active; plan.StrategicAbort = false; plan.AbortReason = AIOperationAbortReason.None;
            Revise(plan, turn, "緊急対応が解消したため保持していた作戦を再開", false);
        }
        int aliveAssigned = 0;
        foreach (string life in plan.OriginalAssignedUnitLifeIds) if (features.TryOwn(life, out var actor) && actor != null && actor.IsAlive) aliveAssigned++;
        if (plan.OriginalAssignedUnitLifeIds.Count >= 2 && aliveAssigned * 2 < plan.OriginalAssignedUnitLifeIds.Count)
        { Complete(plan, board, turn, AIOperationAbortReason.ExcessiveLoss); return; }
        if (turn > plan.MaximumEndTurn)
        { Complete(plan, board, turn, AIOperationAbortReason.Timeout); return; }
        if (turn - plan.LastProgressTurn >= Mathf.Max(1, config.ReplanAfterStalledTurns)
            && (plan.Revisions.Count == 0 || plan.Revisions[plan.Revisions.Count - 1].Turn < turn - 1))
            Revise(plan, turn, "観測上の進展が停滞したため偵察と接近手順を再評価", true);
    }

    /// <summary>Explicit goal change retains the operation identity and original purpose in revisions.</summary>
    public bool ReviseGoal(long operationId, AIOperationGoal goal, int ownTurn, string reason)
    {
        var plan = FindActive(operationId);
        if (plan == null || !Enum.IsDefined(typeof(AIOperationGoal), goal) || goal == AIOperationGoal.None) return false;
        var revision = new AIOperationRevision { Turn = ownTurn, OldGoal = plan.PrimaryGoal, NewGoal = goal,
            Reason = reason ?? "観測状況に基づく目的変更", OldSteps = AIOperationRevision.CopySteps(plan.Steps),
            OldStartContext = plan.StartContext.Copy() };
        if (plan.OriginalStartContext == null) plan.OriginalStartContext = plan.StartContext.Copy();
        foreach (var record in plan.ActionEvidence)
            plan.GoalStartedAfterActionId = Math.Max(plan.GoalStartedAfterActionId, record.ActionId);
        plan.StartContext = plan.CurrentContext.Copy();
        plan.StartContext.Turn = ownTurn;
        plan.PrimaryGoal = goal; plan.Steps = CreateSteps(goal, ownTurn); plan.CurrentStep = 0;
        plan.ExpectedEndTurn = Math.Max(plan.ExpectedEndTurn, ownTurn + Mathf.Max(1, config.DefaultExpectedDuration) - 1);
        plan.MaximumEndTurn = Math.Max(plan.MaximumEndTurn, ownTurn + Mathf.Max(1, config.DefaultMaximumDuration) - 1);
        plan.PatternKey = Pattern(plan.Steps); plan.ContextKey = AIOperationFeatureExtractor.ContextKey(plan, plan.StartContext);
        revision.NewSteps = AIOperationRevision.CopySteps(plan.Steps); revision.NewStartContext = plan.StartContext.Copy();
        AddRevision(plan, revision);
        return true;
    }

    void Revise(AIOperationPlan plan, int turn, string reason, bool scoutFirst)
    {
        var revision = new AIOperationRevision { Turn = turn, Reason = reason,
            OldGoal = plan.PrimaryGoal, NewGoal = plan.PrimaryGoal, OldSteps = AIOperationRevision.CopySteps(plan.Steps) };
        var next = new List<AIOperationStep>();
        int nextId = 1;
        foreach (var old in plan.Steps) nextId = Mathf.Max(nextId, old.StepId + 1);
        foreach (var step in plan.Steps) if (step.Completed) next.Add(step.Copy());
        if (scoutFirst && plan.PrimaryGoal != AIOperationGoal.EconomicRecovery && next.Count < config.StepLimit)
            next.Add(new AIOperationStep { StepId = nextId++, Type = AIOperationStepType.Scout,
                Purpose = "不足する視界を確認し、無理な突入を避ける", Required = false, StartTurn = turn });
        foreach (var step in plan.Steps)
            if (!step.Completed && next.Count < config.StepLimit)
            { var copy = step.Copy(); copy.StepId = nextId++; copy.StartTurn = turn; next.Add(copy); }
        plan.Steps = next; plan.CurrentStep = 0;
        while (plan.CurrentStep < next.Count && next[plan.CurrentStep].Completed) plan.CurrentStep++;
        plan.PatternKey = Pattern(plan.Steps); revision.NewSteps = AIOperationRevision.CopySteps(next);
        AddRevision(plan, revision); plan.Status = AIOperationStatus.Active;
    }

    void AddRevision(AIOperationPlan plan, AIOperationRevision revision)
    {
        if (plan.Revisions.Count >= config.RevisionLimit) plan.Revisions.RemoveAt(0);
        plan.Revisions.Add(revision);
    }

    void Suspend(AIOperationPlan plan, int turn, AIOperationAbortReason reason)
    {
        if (plan.Status != AIOperationStatus.Suspended) plan.SuspendedOwnTurn = turn;
        plan.Status = AIOperationStatus.Suspended; plan.IsPrimary = false;
        plan.StrategicAbort = true; plan.AbortReason = reason;
        foreach (string life in plan.AssignedUnitLifeIds)
            if (assignments.TryGetValue(life, out var owner) && owner == plan) assignments.Remove(life);
    }

    void Complete(AIOperationPlan plan, AIBoardState board, int turn, AIOperationAbortReason abort)
    {
        if (completedIds.Contains(plan.OperationId)) return;
        plan.EndContext = features.Capture(board, plan, turn); plan.ActualEndTurn = turn;
        plan.AbortReason = abort;
        plan.StrategicAbort = abort == AIOperationAbortReason.CrystalEmergency || abort == AIOperationAbortReason.EconomicEmergency
            || abort == AIOperationAbortReason.StrategicPriorityChanged || abort == AIOperationAbortReason.ObjectiveDestroyedByOther;
        if (abort != AIOperationAbortReason.None) plan.Status = AIOperationStatus.Aborted;
        var result = scorer.Evaluate(plan, plan.StartContext, plan.EndContext, plan.ActionEvidence);
        plan.PrimaryGoalAchieved = result.PrimaryGoalAchieved; plan.Score = result.Breakdown;
        plan.LearningReward = result.LearningReward; plan.Rank = result.Rank;
        plan.SuccessReasons = result.SuccessReasons; plan.FailureReasons = result.FailureReasons;
        if (abort == AIOperationAbortReason.None)
            plan.Status = result.PrimaryGoalAchieved ? AIOperationStatus.Success
                : result.Rank == AIOperationRank.PartialSuccess ? AIOperationStatus.PartialSuccess : AIOperationStatus.Failure;
        Profile.Learn(plan, result, config);
        if (result.PrimaryGoalAchieved && result.LearningReward > 0) AllocateCredits(plan, result);
        completedIds.Add(plan.OperationId); state.CompletedOperationIds.Add(plan.OperationId);
        if (state.CompletedOperationIds.Count > config.EventLimit)
        { completedIds.Remove(state.CompletedOperationIds[0]); state.CompletedOperationIds.RemoveAt(0); }
        if (state.CompletedOperations.Count >= config.CompletedLimit) state.CompletedOperations.RemoveAt(0);
        state.CompletedOperations.Add(plan);
        if (config.EnableDebug) DevelopmentLog.Log($"[AI Operation] 完了 #{plan.OperationId} Goal={plan.PrimaryGoal} Primary={plan.PrimaryGoalAchieved} Final={result.FinalScore:F1}/100 Learning={result.LearningReward:F2} Status={plan.Status}");
    }

    void AllocateCredits(AIOperationPlan plan, AIOperationEvaluationResult result)
    {
        // Positive operation results never repair a negative action. Credit is paid only for a
        // directly verified objective event, or preparation causally connected to that event.
        AIActionRecord achievement = null;
        foreach (var record in plan.ActionEvidence)
            if (record.ExecutionSucceeded && record.Outcome != null
                && (record.Outcome.KilledLifeIds?.Contains(plan.TargetLifeId ?? "") == true
                    || plan.PrimaryGoal == AIOperationGoal.ObtainArtifact && record.Outcome.ArtifactsAcquired > 0
                    || plan.PrimaryGoal == AIOperationGoal.ScoutRegion && record.Outcome.NewTilesRevealed > 0
                    || plan.PrimaryGoal == AIOperationGoal.EconomicRecovery
                        && record.Outcome.EconomyBefore != EconomicState.Healthy && record.Outcome.EconomyAfter == EconomicState.Healthy))
                achievement = record;
        if (achievement == null) return;
        int achievementIndex = plan.ActionEvidence.IndexOf(achievement);
        for (int i = 0; i <= achievementIndex; i++)
        {
            var record = plan.ActionEvidence[i];
            if (!record.ExecutionSucceeded || record.Reward < 0 || record.OperationContribution <= 0
                || record.ActionId <= plan.GoalStartedAfterActionId
                || plan.CreditedActionIds.Contains(record.ActionId)) continue;
            bool causal = record == achievement;
            if (!causal && achievementIndex - i <= 5)
            {
                bool sameActorApproach = record.ActorLifeId == achievement.ActorLifeId
                    && record.Outcome?.DistanceToObjectiveDelta > 0;
                bool discoveredTarget = record.Outcome?.NewEnemyLifeIds?.Contains(achievement.TargetLifeId ?? "") == true;
                bool supportedActor = record.Before?.SupportTargetLifeIds?.Contains(achievement.ActorLifeId ?? "") == true
                    && record.Outcome != null && (record.Outcome.EffectChanged || record.Outcome.ShieldGranted
                        || record.Outcome.HealingDone > 0 || record.Outcome.ApRecovered > 0);
                causal = sameActorApproach || discoveredTarget || supportedActor;
            }
            if (!causal) continue;
            float decay = Mathf.Pow(AIOperationConfig.Unit(config.OperationCreditDecay), achievementIndex - i);
            float delta = Mathf.Min(Mathf.Clamp(AIOperationConfig.Finite(config.MaxOperationCreditPerAction), 0, 1),
                result.LearningReward * AIOperationConfig.Unit(record.OperationContribution) * decay);
            if (delta <= 0) continue;
            plan.CreditedActionIds.Add(record.ActionId);
            credits.Add(new AIOperationActionCredit { OperationId = plan.OperationId, ActionId = record.ActionId,
                Delta = delta, Contribution = AIOperationConfig.Unit(record.OperationContribution) });
        }
    }

    float Contribution(AIActionRecord record, AIOperationPlan plan)
    {
        if (!record.ExecutionSucceeded || record.Outcome == null || record.Reward < 0) return 0;
        var outcome = record.Outcome;
        if (outcome.KilledLifeIds?.Contains(plan.TargetLifeId ?? "") == true
            || plan.PrimaryGoal == AIOperationGoal.ObtainArtifact && outcome.ArtifactsAcquired > 0)
            return AIOperationConfig.Unit(config.DirectGoalContribution);
        if (outcome.NewTilesRevealed > 0 || outcome.NewEnemyLifeIds?.Count > 0)
            return AIOperationConfig.Unit(config.ScoutContribution);
        if (outcome.EffectChanged || outcome.ShieldGranted || outcome.HealingDone > 0 || outcome.ApRecovered > 0)
            return AIOperationConfig.Unit(config.SupportContribution);
        if (outcome.DistanceToObjectiveDelta > 0) return AIOperationConfig.Unit(config.PreparationContribution);
        if (plan.PrimaryGoal == AIOperationGoal.EconomicRecovery
            && outcome.EconomyBefore != EconomicState.Healthy && outcome.EconomyAfter == EconomicState.Healthy
            && (record.ActionType == AIActionType.Build || record.ActionType == AIActionType.Upgrade))
            return AIOperationConfig.Unit(config.DirectGoalContribution);
        return 0;
    }

    void ConfirmStep(AIOperationPlan plan, AIOperationStep step, AIActionRecord record, int turn)
    {
        if (step == null || step.Completed || record.Outcome == null) return;
        var result = record.Outcome;
        bool done = step.Type == AIOperationStepType.Scout && (result.NewTilesRevealed > 0 || result.NewEnemyLifeIds?.Count > 0)
            || step.Type == AIOperationStepType.Approach && result.DistanceToObjectiveDelta > 0
            || step.Type == AIOperationStepType.Support && (result.EffectChanged || result.HealingDone > 0 || result.ShieldGranted)
            || step.Type == AIOperationStepType.Attack && (result.DamageDealt > 0 || result.EnemyKills > 0)
            || step.Type == AIOperationStepType.DestroyObjective && result.KilledLifeIds?.Contains(plan.TargetLifeId ?? "") == true
            || step.Type == AIOperationStepType.AcquireArtifact && result.ArtifactsAcquired > 0
            || step.Type == AIOperationStepType.Diversion && plan.CurrentContext.DiversionConfirmed
            || step.Type == AIOperationStepType.Flank && plan.CurrentContext.FlankConfirmed
            || step.Type == AIOperationStepType.Surround && plan.CurrentContext.SurroundConfirmed
            || step.Type == AIOperationStepType.Defense && !plan.CurrentContext.OwnCrystalThreatened;
        if (!done) return;
        step.Completed = true; step.EndTurn = turn;
        while (plan.CurrentStep < plan.Steps.Count && plan.Steps[plan.CurrentStep].Completed) plan.CurrentStep++;
        plan.LastProgressTurn = turn;
    }

    AIOperationPlan Match(AIAction action, AIBoardState board)
    {
        if (action.Unit != null && assignments.TryGetValue(action.Unit.ReflectionLifeId, out var plan) && Running(plan)) return plan;
        var primary = PrimaryOperation;
        if (primary == null || primary.Status == AIOperationStatus.Suspended) return null;
        if ((action.ActionType == AIActionType.Build || action.ActionType == AIActionType.Upgrade)
            && primary.PrimaryGoal == AIOperationGoal.EconomicRecovery) return primary;
        if (action.ActionType == AIActionType.SubCrystal && primary.PrimaryGoal == AIOperationGoal.ObtainArtifact) return primary;
        if (action.ActionType == AIActionType.Summon && primary.Status == AIOperationStatus.Preparing
            && action.SummonKind != Kind.King) return primary;
        return null;
    }

    Vector3 MovementAim(AIOperationPlan plan, Status actor)
    {
        // Reuse the existing plan's observed waypoints instead of creating competing target locks.
        if (legacyPlan != null && legacyPlan.Active && !legacyPlan.Suspended)
        {
            var step = CurrentStep(plan)?.Type;
            if (step == AIOperationStepType.Diversion && actor.kind == Kind.Scout) return legacyPlan.DemonstrationPoint;
            if (step == AIOperationStepType.Flank) return legacyPlan.FlankPoint;
            if (step == AIOperationStepType.Retreat || step == AIOperationStepType.Regroup) return legacyPlan.RallyPoint;
        }
        return new Vector3(plan.TargetX, 0, plan.TargetZ);
    }

    void SeedObservations(AIBoardState board, int turn)
    { foreach (var enemy in features.ObservedUnits(board, turn)) if (observedLives.Count < config.EventLimit) observedLives.Add(enemy.ReflectionLifeId); }
    void RemoveFinished()
    {
        for (int i = state.ActiveOperations.Count - 1; i >= 0; i--)
            if (!Running(state.ActiveOperations[i])) state.ActiveOperations.RemoveAt(i);
        state.ActiveOperations.Sort((first, second) => first.StrategicPriority == second.StrategicPriority
            ? first.OperationId.CompareTo(second.OperationId) : first.StrategicPriority.CompareTo(second.StrategicPriority));
        assignments.Clear();
        foreach (var plan in state.ActiveOperations)
            if (plan.Status != AIOperationStatus.Suspended)
                foreach (string life in plan.AssignedUnitLifeIds) if (!assignments.ContainsKey(life)) assignments[life] = plan;
        EnsureOnePrimary();
    }
    void PromoteExisting()
    {
        AIOperationPlan best = null;
        foreach (var plan in state.ActiveOperations)
            if (Running(plan) && plan.Status != AIOperationStatus.Suspended
                && (best == null || plan.StrategicPriority < best.StrategicPriority)) best = plan;
        if (best != null) best.IsPrimary = true;
    }
    void EnsureOnePrimary()
    {
        var best = PrimaryOperation;
        foreach (var plan in state.ActiveOperations)
            if (plan.IsPrimary && plan != best) plan.IsPrimary = false;
        if (best == null) PromoteExisting();
    }
    AIOperationPlan FindActive(long id)
    { foreach (var plan in state.ActiveOperations) if (plan.OperationId == id) return plan; return null; }
    void ConfirmTargetForce(AIOperationPlan plan)
    {
        if (plan.PrimaryGoal == AIOperationGoal.BreakEnemyDefense && plan.TargetCategory == "Building"
            && plan.CurrentContext.ConfirmedTargetDestroyed) plan.CurrentContext.TargetForceConfirmedEliminated = true;
        if (plan.PrimaryGoal != AIOperationGoal.EliminateEnemyForce) return;
        int targets = 0;
        foreach (var observed in plan.StartContext.ObservedEnemies)
        {
            if (observed.Power <= 0) continue;
            targets++;
            if (!destroyedLives.Contains(observed.LifeId)) return;
        }
        plan.CurrentContext.TargetForceConfirmedEliminated = targets > 0;
    }
    void ConfirmDefenseResolved(AIOperationPlan plan)
    {
        if (plan.PrimaryGoal != AIOperationGoal.DefendOwnCrystal && plan.PrimaryGoal != AIOperationGoal.DefendSubCrystal) return;
        var facts = plan.CurrentContext;
        facts.EnemyThreatResolved = false;
        if (facts.OwnCrystalThreatened || facts.OwnCrystalDestroyed) return;
        int threats = 0;
        foreach (var initial in plan.StartContext.ObservedEnemies)
        {
            if (!initial.ThreatensObjective) continue;
            threats++;
            if (destroyedLives.Contains(initial.LifeId)) continue;
            AIOperationObservedUnit current = null;
            foreach (var now in facts.ObservedEnemies) if (now.LifeId == initial.LifeId) { current = now; break; }
            if (current == null || current.ThreatensObjective) return;
        }
        facts.EnemyThreatResolved = threats > 0;
    }
    void ConfirmTacticalGoal(AIOperationPlan plan, AIActionRecord record, AIBoardState board)
    {
        if (record.Outcome.FormationKills > 0 && record.Outcome.KilledLifeIds.Contains(plan.TargetLifeId ?? ""))
            plan.CurrentContext.SurroundConfirmed = true;
        if (board == null || record.Outcome.DamageDealt <= 0 || record.TargetLifeId != plan.TargetLifeId) return;
        features.Prepare(board, state.LastStartedOwnTurn);
        if (!features.TryObserved(plan.TargetLifeId, out var target) || !features.TryOwn(record.ActorLifeId, out var actor)) return;
        float dx = actor.transform.position.x - target.transform.position.x;
        float dz = actor.transform.position.z - target.transform.position.z;
        bool attackedSide = Mathf.Abs(dx) > Mathf.Abs(dz) || dz * MovePatterns.DirZ(target.direction) < 0;
        if (attackedSide) plan.CurrentContext.FlankConfirmed = true;
        foreach (var partner in ownArmy)
        {
            if (partner == actor || !partner.IsAlive || AIOperationFeatureExtractor.Distance(partner.transform.position, target.transform.position) > 2) continue;
            var first = actor.transform.position - target.transform.position;
            var second = partner.transform.position - target.transform.position;
            if (first.x * second.x + first.z * second.z < 0)
            { plan.CurrentContext.SurroundConfirmed = true; break; }
        }
    }
    static bool Running(AIOperationPlan plan) => plan != null && (plan.Status == AIOperationStatus.Proposed
        || plan.Status == AIOperationStatus.Preparing || plan.Status == AIOperationStatus.Active
        || plan.Status == AIOperationStatus.Suspended || plan.Status == AIOperationStatus.Replanning);
    bool HasScout() { foreach (var actor in ownArmy) if (actor.kind == Kind.Scout) return true; return false; }
    Status FirstObservedTroop(AIBoardState board, int turn)
    { foreach (var target in features.ObservedUnits(board, turn)) if (target.type == Type.Unit) return target; return null; }
    static bool IsMovement(AIActionType type) => type == AIActionType.Move || type == AIActionType.Support
        || type == AIActionType.Surround || type == AIActionType.Retreat || type == AIActionType.DefenseRepos;
    static AIOperationStep CurrentStep(AIOperationPlan plan) => plan.CurrentStep >= 0 && plan.CurrentStep < plan.Steps.Count ? plan.Steps[plan.CurrentStep] : null;
    static AIOperationStep FindStep(AIOperationPlan plan, int id)
    { foreach (var step in plan.Steps) if (step.StepId == id) return step; return null; }
    static float ResourceSpend(AIActionOutcome outcome) => Mathf.Max(0, -outcome.WoodDelta) + Mathf.Max(0, -outcome.StoneDelta)
        + Mathf.Max(0, -outcome.IronDelta) + Mathf.Max(0, -outcome.MagicOreDelta) + Mathf.Max(0, -outcome.BreadDelta)
        + Mathf.Max(0, -outcome.WaterDelta) + Mathf.Max(0, -outcome.WheatDelta);

    List<AIOperationStep> CreateSteps(AIOperationGoal goal, int turn)
    {
        AIOperationStepType[] types;
        switch (goal)
        {
            case AIOperationGoal.DefendOwnCrystal: case AIOperationGoal.DefendSubCrystal:
                types = new[] { AIOperationStepType.Defense, AIOperationStepType.Support, AIOperationStepType.Hold }; break;
            case AIOperationGoal.EconomicRecovery:
                types = new[] { AIOperationStepType.Support, AIOperationStepType.Hold }; break;
            case AIOperationGoal.ScoutRegion: case AIOperationGoal.LocateEnemyMainForce:
                types = new[] { AIOperationStepType.Scout, AIOperationStepType.Approach, AIOperationStepType.Regroup }; break;
            case AIOperationGoal.FlankEnemyForce:
                types = new[] { AIOperationStepType.Scout, AIOperationStepType.Flank, AIOperationStepType.Attack }; break;
            case AIOperationGoal.SurroundEnemyForce:
                types = new[] { AIOperationStepType.Approach, AIOperationStepType.Surround, AIOperationStepType.Attack }; break;
            case AIOperationGoal.CreateDiversion:
                types = new[] { AIOperationStepType.Scout, AIOperationStepType.Diversion, AIOperationStepType.Flank, AIOperationStepType.Exploit }; break;
            case AIOperationGoal.ObtainArtifact:
                types = new[] { AIOperationStepType.Scout, AIOperationStepType.Approach, AIOperationStepType.Hold, AIOperationStepType.AcquireArtifact }; break;
            default:
                types = new[] { AIOperationStepType.Scout, AIOperationStepType.Approach, AIOperationStepType.Attack, AIOperationStepType.DestroyObjective }; break;
        }
        var result = new List<AIOperationStep>();
        foreach (var type in types)
        {
            if (result.Count >= config.StepLimit) break;
            result.Add(new AIOperationStep { StepId = result.Count + 1, Type = type, Purpose = type.ToString(),
                Required = type == AIOperationStepType.DestroyObjective || type == AIOperationStepType.Defense
                    || type == AIOperationStepType.AcquireArtifact || type == AIOperationStepType.Scout, StartTurn = turn });
        }
        return result;
    }
    static string Pattern(List<AIOperationStep> steps)
    {
        var names = new string[steps.Count];
        for (int i = 0; i < steps.Count; i++) names[i] = steps[i].Type.ToString();
        return string.Join(">", names);
    }
    static string Reason(AIOperationGoal goal)
    {
        switch (goal)
        {
            case AIOperationGoal.DefendOwnCrystal: return "観測された脅威から自軍の敗北条件を守る";
            case AIOperationGoal.DestroySubCrystal: return "発見済み敵拠点を破壊し前線を弱める";
            case AIOperationGoal.DestroyEnemyCrystal: return "発見済み敵メインクリスタルの破壊を目指す";
            case AIOperationGoal.EconomicRecovery: return "不足資源の生産能力を整えて経済を回復する";
            case AIOperationGoal.ScoutRegion: return "保持中の探索目標へ進み未確認領域の視界を増やす";
            case AIOperationGoal.LocateEnemyMainForce: return "最後の観測情報を参考に敵部隊の所在を確かめる";
            case AIOperationGoal.CreateDiversion: return "観測した敵防衛部隊の移動と別経路の開放を確認する";
            case AIOperationGoal.ObtainArtifact: return "発見済みダンジョンの支配を維持して報酬を確保する";
            default: return "観測済み目標への接近と局地戦の改善を目指す";
        }
    }
    static float PersonalityBonus(AIOperationGoal goal, AIPersonality personality)
    {
        if (personality == null) return 0;
        float value = goal == AIOperationGoal.EconomicRecovery ? personality.DevelopRate
            : goal == AIOperationGoal.DefendOwnCrystal ? personality.DefenseRate
            : goal == AIOperationGoal.ScoutRegion || goal == AIOperationGoal.LocateEnemyMainForce ? personality.CautionRate
            : goal == AIOperationGoal.FlankEnemyForce || goal == AIOperationGoal.CreateDiversion ? personality.TacticsRate : personality.CommandRate;
        return Mathf.Clamp(value * 2, 0, 2);
    }
    static float PlayerBonus(AIOperationGoal goal, AIPlayerModel.Profile profile)
    {
        if (profile == null || profile.Matches < 1) return 0;
        float value = goal == AIOperationGoal.FlankEnemyForce || goal == AIOperationGoal.CreateDiversion
            || goal == AIOperationGoal.ScoutRegion ? profile.Turtle
            : goal == AIOperationGoal.DefendOwnCrystal ? profile.Aggression : 0;
        return Mathf.Clamp(AIOperationConfig.Finite(value) * 2, 0, 2);
    }
}

/// <summary>Development diagnostics for candidates; strategic priority is compared before any modifiers.</summary>
public sealed class AIOperationCandidate
{
    public AIOperationPlan Plan;
    public int Priority;
    public float BaseScore, Experience, PlayerModel, Personality, Risk;
    public float FinalScore => BaseScore + Experience + PlayerModel + Personality + Risk;
    public static int Compare(AIOperationCandidate first, AIOperationCandidate second)
    {
        int priority = first.Priority.CompareTo(second.Priority);
        if (priority != 0) return priority;
        int score = second.FinalScore.CompareTo(first.FinalScore);
        return score != 0 ? score : first.Plan.PrimaryGoal.CompareTo(second.Plan.PrimaryGoal);
    }
}
