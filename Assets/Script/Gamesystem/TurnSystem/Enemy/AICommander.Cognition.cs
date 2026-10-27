using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Optional bounded R4 boundary. Disabled/shadow paths never modify formal candidates or existing rewards.</summary>
public partial class AICommander
{
    AIWorldModelBuilder cognitiveWorldBuilder;
    AIProblemDetector cognitiveProblems;
    AIHypothesisManager cognitiveHypotheses;
    AICognitivePlanner cognitivePlanner;
    AIStrategyKnowledgeMatcher cognitiveMatcher;
    readonly List<AIKnowledgeMatch> cognitiveMatches = new List<AIKnowledgeMatch>(12);
    readonly Dictionary<AIAction, AICognitiveActionAssessment> cognitiveAssessments = new Dictionary<AIAction, AICognitiveActionAssessment>(64);
    readonly Dictionary<AIAction, IReadOnlyList<AIKnowledgeMatch>> cognitiveActionKnowledge = new Dictionary<AIAction, IReadOnlyList<AIKnowledgeMatch>>(64);
    IReadOnlyList<AIProblem> currentCognitiveProblems = Array.Empty<AIProblem>();
    IReadOnlyList<AIHypothesis> currentCognitiveHypotheses = Array.Empty<AIHypothesis>();
    AIObservedWorldModel cognitiveWorld;
    AICognitiveTraceBuffer cognitiveTraces;
    AICausalExperienceMemory cognitiveExperience;
    AICausalExperiencePersistence cognitiveRepository;
    bool cognitiveUnavailable, cognitiveWarning, cognitivePending, cognitiveReleaseRequested;
    long cognitiveEventId;
    long cognitiveObservedActionId = -1;
    string cognitiveBattleId;
    AICognitivePlan pendingCognitivePlan;
    string pendingCognitiveAction;
    AIActionFeatureExtractor cognitiveFallbackExtractor;
    AIActionRecord cognitiveFallbackRecord;
    readonly List<CognitiveAttribution> cognitiveAttributions = new List<CognitiveAttribution>(32);
    sealed class CognitiveAttribution
    {
        public long OperationId;
        public string PlanId, Operators, Context;
        public AIOperationGoal Goal;
        public readonly List<string> Knowledge = new List<string>();
        public int ConfirmedActions;
        public float Predicted, Observed;
    }
    public AICognitivePlan CurrentCognitivePlan => cognitivePlanner?.Current;
    public AICognitiveDecisionTrace[] CaptureCognitiveTraces() => cognitiveTraces?.Snapshot() ?? Array.Empty<AICognitiveDecisionTrace>();
    void CognitiveFailed(Exception error)
    {
        cognitiveUnavailable = true;
        cognitivePending = false; pendingCognitivePlan = null; pendingCognitiveAction = null;
        cognitiveFallbackRecord = null; cognitiveAssessments.Clear(); cognitiveActionKnowledge.Clear();
        if (!cognitiveWarning)
        { cognitiveWarning = true; Debug.LogWarning("[R4戦略] 通常AIへ戻します: " + error.GetType().Name + ": " + error.Message); }
    }
    void EnsureCognition(AICognitiveAIConfig config)
    {
        cognitiveWorldBuilder ??= new AIWorldModelBuilder();
        cognitiveProblems ??= new AIProblemDetector(); cognitiveHypotheses ??= new AIHypothesisManager();
        cognitivePlanner ??= new AICognitivePlanner(); cognitiveMatcher ??= new AIStrategyKnowledgeMatcher();
        cognitiveTraces ??= new AICognitiveTraceBuffer(config.MaxTraceEntries);
        cognitiveBattleId ??= operations?.State?.BattleId ?? reflection?.BattleId ?? "R4_" + _actorTeam + "_" + RandomSeed;
        ApplyRestoredCognition();
        if (cognitiveExperience == null && config.EnableGeneralizedExperience)
        {
            cognitiveExperience = new AICausalExperienceMemory(_actorTeam, config.MaxCrossMatchEntries);
            if (CanLearn)
            {
                cognitiveRepository = new AICausalExperiencePersistence(Path.Combine(Application.persistentDataPath,
                    "FantasyKingdom", "AI"), _actorTeam, config.MaxCrossMatchEntries);
                var saved = cognitiveRepository.Load(); if (saved != null) cognitiveExperience.Restore(saved);
            }
        }
    }
    IEnumerator PrepareCognitionSteps(List<AIAction> actions)
    {
        var config = AICognitiveAIConfig.Active;
        if (!config.Enabled || !config.EnableWorldModel || cognitiveUnavailable || _board == null || AITurnBudget.RemainingMs < 5) yield break;
        IEnumerator work = null;
        try
        {
            config.Validate(); EnsureCognition(config);
            cognitiveWorld = cognitiveWorldBuilder.Build(_board, cognitiveBattleId, _turnCount);
            // Legacy boards fall back on the AI side; map generation/save data are never normalized by R4.
            if (cognitiveWorld.Width != 40 || cognitiveWorld.Depth != 40)
            { AddCognitiveTrace(config, "unsupported_legacy_board"); yield break; }
            cognitiveAssessments.Clear();
            cognitiveActionKnowledge.Clear();
            if (config.EnableHypothesis)
            {
                currentCognitiveProblems = cognitiveProblems.Detect(cognitiveWorld);
                currentCognitiveHypotheses = cognitiveHypotheses.Update(cognitiveWorld, currentCognitiveProblems);
            }
        }
        catch (Exception error) { CognitiveFailed(error); }
        if (cognitiveUnavailable || cognitiveWorld == null) yield break;
        yield return null;
        if (AITurnBudget.RemainingMs < 5 || _turnGen == null || _turnGen.IsGameOver) yield break;
        try
        {
            cognitiveMatches.Clear();
            if (config.EnableKnowledge)
            {
                var context = AIKnowledgeContextBuilder.Build(cognitiveWorld, actions, _threatLevel.Level, AssessCognitive);
                cognitiveMatcher.Match(context, cognitiveMatches, config.MaxKnowledgeMatches);
            }
            if (config.EnableAutonomousPlanning)
                work = cognitivePlanner.RefreshSteps(cognitiveWorld, actions, cognitiveMatches, AICognitivePlannerLimits.From(config),
                    _governor.Mode == StrategicMode.EmergencyDefense, _governor.Mode == StrategicMode.EconomicRecovery, AssessCognitive,
                    config.EnableGeneralizedExperience ? CalibrateCognitivePlan : null, KnowledgeForCognitiveAction);
        }
        catch (Exception error) { CognitiveFailed(error); }
        if (work == null) yield break;
        try
        {
            while (!AITurnBudget.Expired)
            {
                bool running;
                try { running = work.MoveNext(); }
                catch (Exception error) { CognitiveFailed(error); break; }
                if (!running) break;
                yield return null;
                if (_turnGen == null || _turnGen.IsGameOver) yield break;
            }
        }
        finally { (work as IDisposable)?.Dispose(); }
        try
        {
            var current = cognitivePlanner?.Current;
            if (current != null)
            {
                foreach (var problem in currentCognitiveProblems)
                    if ((problem.OperationId == 0 || problem.OperationId == current.ParentOperationId)
                        && !current.OriginProblemIds.Contains(problem.ProblemId) && current.OriginProblemIds.Count < 16)
                        current.OriginProblemIds.Add(problem.ProblemId);
                foreach (var hypothesis in currentCognitiveHypotheses)
                    if (current.OriginProblemIds.Contains(hypothesis.ProblemId) && !current.HypothesisIds.Contains(hypothesis.HypothesisId)
                        && current.HypothesisIds.Count < 32) current.HypothesisIds.Add(hypothesis.HypothesisId);
            }
            AddCognitiveTrace(config, AITurnBudget.Expired ? "budget_exhausted" : null);
            if (config.MayInfluenceActions && !cognitiveUnavailable
                && _governor.Mode != StrategicMode.EmergencyDefense && _governor.Mode != StrategicMode.EconomicRecovery)
                operations?.SubmitCognitivePlans(cognitivePlanner.Candidates, _board);
        }
        catch (Exception error) { CognitiveFailed(error); }
    }
    AICognitiveActionAssessment AssessCognitive(AIAction action)
    {
        if (action == null) return default;
        if (cognitiveAssessments.TryGetValue(action, out var cached)) return cached;
        var assessment = _governor.AssessCognitiveAction(action, _board);
        // Reconnaissance needs a concrete unresolved decision, not just empty map cells.
        bool unansweredRoute = false;
        foreach (var hypothesis in currentCognitiveHypotheses)
            if (hypothesis.CauseCode == AIHypothesisCause.UnknownRoute && hypothesis.Outcome == AIHypothesisOutcome.Untested)
            { unansweredRoute = true; break; }
        if (unansweredRoute && assessment.HardSafe && action.Unit != null && action.Unit.kind == Kind.Scout
            && action.ActionType == AIActionType.Move)
        {
            int expectedNew = _board.EstimateNewVisionCells(action.TargetPos);
            assessment.ExpectedInformationGain = Mathf.Max(0, AIInformationValueEvaluator.Evaluate(
                expectedNew, .5f, action.APCost, assessment.PredictedIncomingDamage, 1, 0, expectedNew > 0));
        }
        if (cognitiveAssessments.Count < 64) cognitiveAssessments[action] = assessment;
        return assessment;
    }
    IReadOnlyList<AIKnowledgeMatch> KnowledgeForCognitiveAction(AIAction action)
    {
        if (action == null || cognitiveWorld == null) return Array.Empty<AIKnowledgeMatch>();
        if (cognitiveActionKnowledge.TryGetValue(action, out var known)) return known;
        var refined = new List<AIKnowledgeMatch>(12);
        AICognitiveFormalKnowledge.MatchesForAction(cognitiveWorld, action, _board, AssessCognitive(action), cognitiveMatches, refined);
        if (cognitiveActionKnowledge.Count < 64) cognitiveActionKnowledge[action] = refined;
        return refined;
    }
    void ApplyCognitiveSelectionHint(List<AIAction> actions)
    {
        var config = AICognitiveAIConfig.Active;
        if (!config.MayInfluenceActions || cognitiveUnavailable || cognitivePlanner == null || _board == null
            || _governor.Mode == StrategicMode.EmergencyDefense || _governor.Mode == StrategicMode.EconomicRecovery) return;
        if (cognitivePlanner.BindPreferred(_board.Generation, actions, out var action))
        {
            var proof = AssessCognitive(action);
            if (!proof.Legal || !proof.HardSafe || action.StrategicPriority < 2) return;
            // Only ordinary strategy bands are aligned. Safety/economy bands 0/1 remain protected.
            action.StrategicPriority = Math.Min(action.StrategicPriority, proof.PreparationProven ? 2 : 3);
        }
    }
    float CognitiveActionBonus(AIAction action)
        => AICognitiveAIConfig.Active.MayInfluenceActions && !cognitiveUnavailable && cognitivePlanner?.Current != null
            && cognitivePlanner.Current.Status == AICognitivePlanStatus.Active
            && cognitivePlanner.Current.BoardGenerationAtValidation == _board?.Generation
            && cognitivePlanner.Current.NextActionKey == AICognitiveActionKey.For(action) ? .5f : 0;
    void BeginCognitiveExecution(AIAction action, AIActionRecord record)
    {
        if (cognitiveUnavailable || cognitivePlanner == null || !AICognitiveAIConfig.Active.Enabled) return;
        try
        {
            string key = AICognitiveActionKey.For(action);
            if (cognitiveEventId < long.MaxValue) cognitiveEventId++;
            if (record == null)
            {
                cognitiveFallbackExtractor ??= new AIActionFeatureExtractor();
                record = new AIActionRecord { ActionId = cognitiveEventId, BattleId = cognitiveBattleId,
                    Turn = _turnCount, ActionType = action.ActionType,
                    Before = cognitiveFallbackExtractor.Capture(action, _board, _turnCount, _currentStrategy, _threatLevel.Level, 0),
                    Outcome = new AIActionOutcome() };
                cognitiveFallbackRecord = record;
            }
            var trace = cognitiveTraces?.Latest;
            if (trace != null) { trace.ChosenAIActionKey = key; trace.ActionLegal = string.IsNullOrEmpty(action.StrategicRejectReason);
                trace.LegalityStatus = "FORMAL_CANDIDATE_PENDING_EXECUTOR"; trace.ExecutionAttempted = true; }
            cognitiveObservedActionId = record?.ActionId ?? -1;
            if (!AICognitiveAIConfig.Active.MayInfluenceActions || cognitivePlanner.Current?.NextActionKey != key) return;
            pendingCognitivePlan = JsonUtility.FromJson<AICognitivePlan>(JsonUtility.ToJson(cognitivePlanner.Current));
            pendingCognitiveAction = key; cognitivePending = true;
            if (record != null) { record.CognitivePlanId = pendingCognitivePlan.PlanId; record.CognitiveStepId = pendingCognitivePlan.Steps[0].StepId; }
        }
        catch (Exception error) { CognitiveFailed(error); }
    }
    void CompleteCognitiveFallback(AIAction action, bool success)
    {
        if (cognitiveFallbackRecord == null || cognitiveUnavailable) return;
        try
        {
            var record = cognitiveFallbackRecord; cognitiveFallbackRecord = null;
            var events = record.Outcome;
            record.After = cognitiveFallbackExtractor.Capture(action, _board, _turnCount, _currentStrategy, _threatLevel.Level, 0);
            record.ExecutionSucceeded = success; record.Outcome = AIActionOutcome.Difference(record.Before, record.After);
            record.Outcome.DamageDealt = Math.Max(record.Outcome.DamageDealt, events.DamageDealt);
            record.Outcome.DamageTaken = Math.Max(record.Outcome.DamageTaken, events.DamageTaken);
            record.Outcome.KilledLifeIds.AddRange(events.KilledLifeIds); record.Outcome.EnemyKills = events.KilledLifeIds.Count;
            record.MeaningfulProgress = AIFailureAnalyzer.HasMeaningfulProgress(record);
            CompleteCognitiveExecution(record);
            if (cognitiveReleaseRequested && !cognitivePending) ReleaseCognition();
        }
        catch (Exception error) { CognitiveFailed(error); }
    }
    void ObserveCognitiveFallbackDamage(int amount, bool dealt, string killedLife)
    {
        if (cognitiveFallbackRecord?.Outcome == null) return;
        var outcome = cognitiveFallbackRecord.Outcome;
        if (dealt) outcome.DamageDealt = (int)Math.Min(int.MaxValue, (long)outcome.DamageDealt + Math.Max(0, amount));
        else outcome.DamageTaken = (int)Math.Min(int.MaxValue, (long)outcome.DamageTaken + Math.Max(0, amount));
        if (dealt && !string.IsNullOrEmpty(killedLife) && outcome.KilledLifeIds.Count < 16 && !outcome.KilledLifeIds.Contains(killedLife))
            outcome.KilledLifeIds.Add(killedLife);
    }
    void CompleteCognitiveExecution(AIActionRecord record)
    {
        if (record == null) return;
        var executedTrace = cognitiveTraces?.Latest;
        if (executedTrace != null && record.ActionId == cognitiveObservedActionId)
        {
            executedTrace.ExecutionSucceeded = record.ExecutionSucceeded; executedTrace.MatchedOperationId = record.OperationId;
            executedTrace.LegalityStatus = record.ExecutionSucceeded ? "EXECUTOR_ACCEPTED" : "EXECUTION_FAILED_LEGALITY_UNCONFIRMED";
            executedTrace.ObservedDamage = record.Outcome?.DamageDealt ?? 0; executedTrace.ObservedLoss = record.Outcome?.DamageTaken ?? 0;
            executedTrace.ObservedHealing = record.Outcome?.HealingDone ?? 0; executedTrace.ObservedNewCells = record.Outcome?.NewTilesRevealed ?? 0;
        }
        if (record.ActionId != cognitiveObservedActionId || cognitiveUnavailable || !cognitivePending || pendingCognitivePlan == null) return;
        try
        {
            cognitivePending = false;
            var after = cognitiveWorldBuilder.Build(_board, cognitiveBattleId, _turnCount);
            ObserveCognitiveHypotheses(record, after);
            cognitivePlanner.ObserveExecution(pendingCognitiveAction, record.ExecutionSucceeded, after, record.MeaningfulProgress);
            if (record.ExecutionSucceeded && record.OperationId > 0 && record.OperationId == pendingCognitivePlan.ParentOperationId
                && pendingCognitivePlan.KnowledgeIds.Count > 0)
            {
                if (cognitiveAttributions.Count >= 32) cognitiveAttributions.RemoveAt(0);
                var attribution = new CognitiveAttribution { OperationId = record.OperationId, PlanId = pendingCognitivePlan.PlanId,
                    Goal = pendingCognitivePlan.Goal, Operators = OperatorSignature(pendingCognitivePlan), Context = CognitiveContext(cognitiveWorld),
                    ConfirmedActions = 1, Predicted = pendingCognitivePlan.GoalProgress,
                    Observed = record.MeaningfulProgress ? 1 : 0 };
                attribution.Knowledge.AddRange(pendingCognitivePlan.KnowledgeIds); cognitiveAttributions.Add(attribution);
            }
            pendingCognitivePlan = null; pendingCognitiveAction = null;
            if (cognitiveReleaseRequested) ReleaseCognition();
        }
        catch (Exception error) { CognitiveFailed(error); }
    }
    void ObserveCognitiveHypotheses(AIActionRecord record, AIObservedWorldModel after)
    {
        if (record.Outcome == null || cognitiveHypotheses == null || pendingCognitivePlan == null) return;
        string op = pendingCognitivePlan.Steps[0].OperatorId;
        foreach (var hypothesis in currentCognitiveHypotheses)
        {
            if (!pendingCognitivePlan.HypothesisIds.Contains(hypothesis.HypothesisId) || hypothesis.TestActionType != op) continue;
            var outcome = AIHypothesisOutcome.Inconclusive;
            if (record.ExecutionSucceeded && op == "SCOUT_FOR_ANSWER")
            {
                if (record.Outcome.NewTilesRevealed > 0 && record.Outcome.DistanceToObjectiveDelta > 0)
                    outcome = AIHypothesisOutcome.Supported;
                else if (record.Outcome.NewTilesRevealed == 0) outcome = AIHypothesisOutcome.Refuted;
            }
            // A failed command alone cannot prove why a route/formation/response failed.
            cognitiveHypotheses.ObserveOutcome(cognitiveBattleId + ":" + cognitiveEventId, hypothesis.HypothesisId,
                outcome, after.GlobalTurn, "executed:" + record.ActionId);
        }
    }
    void AddCognitiveTrace(AICognitiveAIConfig config, string fallback)
    {
        if (!config.EnableDecisionDiagnostics || cognitiveWorld == null) return;
        var plan = cognitivePlanner?.Current;
        var trace = new AICognitiveDecisionTrace { SourceSHA = FKBuildInfo.Current.Commit,
            BattleId = cognitiveWorld.BattleId, ActorTeam = _actorTeam, GlobalTurn = _board.TurnCount, OwnTurn = _turnCount,
            BoardGeneration = cognitiveWorld.BoardGeneration, KnownCount = cognitiveWorld.Facts.Count,
            BeliefCount = cognitiveWorld.BeliefRegions.Count, UnknownCount = cognitiveWorld.UnknownCount,
            Shadow = !config.MayInfluenceActions, SelectedPlanId = plan?.PlanId, MatchedOperationId = plan?.ParentOperationId ?? 0,
            ReplanReason = cognitivePlanner?.ReplanReason, FallbackReason = fallback,
            ElapsedMilliseconds = AITurnBudget.ElapsedMs, BudgetRemainingMilliseconds = AITurnBudget.RemainingMs };
        for (int i = 0; i < cognitiveWorld.ObservedFactIds.Count && i < 64; i++) trace.ObservedFactIds.Add(cognitiveWorld.ObservedFactIds[i]);
        if (plan != null) trace.KnowledgeIds.AddRange(plan.KnowledgeIds);
        foreach (var problem in currentCognitiveProblems) trace.ProblemIds.Add(problem.ProblemId);
        foreach (var hypothesis in currentCognitiveHypotheses) trace.HypothesisIds.Add(hypothesis.HypothesisId);
        if (cognitivePlanner != null) foreach (var candidate in cognitivePlanner.Candidates)
        { trace.CandidatePlanIds.Add(candidate.PlanId); if (candidate.RejectionReason != null) trace.RejectedPlanReasons.Add(candidate.RejectionReason); }
        cognitiveTraces.Add(trace);
    }
    static string OperatorSignature(AICognitivePlan plan)
    {
        var values = new List<string>(6); foreach (var step in plan.Steps) if (step != null) values.Add(step.OperatorId);
        return string.Join(",", values);
    }
    static string CognitiveContext(AIObservedWorldModel world)
    {
        int own = 0, observed = 0;
        foreach (var unit in world.OwnUnits) if (!unit.IsBuilding && !unit.IsCrystal) own++;
        foreach (var unit in world.ObservedOpponents) if (!unit.IsBuilding && !unit.IsCrystal) observed++;
        return "F" + Math.Min(3, own / Math.Max(1, observed)) + "-V" + (observed > 0 ? "Known" : "Unknown")
            + "-E" + (int)world.OwnEconomy.State;
    }
    float CalibrateCognitivePlan(AICognitivePlan plan)
    {
        if (cognitiveExperience == null || cognitiveWorld == null || plan.KnowledgeIds.Count == 0) return 0;
        float total = 0; string operators = OperatorSignature(plan), context = CognitiveContext(cognitiveWorld);
        foreach (string knowledge in plan.KnowledgeIds)
            total += cognitiveExperience.Calibration(knowledge, operators, context, plan.Goal, AICognitiveAIConfig.Active.MinMeaningfulSamples);
        return Mathf.Clamp(total / plan.KnowledgeIds.Count, -.1f, .1f);
    }
    void EndCognitiveTurn()
    {
        cognitiveAssessments.Clear(); cognitiveActionKnowledge.Clear();
        if (!AICognitiveAIConfig.Active.Enabled) return;
        if (cognitiveExperience == null || cognitiveUnavailable) return;
        try
        {
            if (operations != null) foreach (var operation in operations.State.CompletedOperations)
                for (int i = cognitiveAttributions.Count - 1; i >= 0; i--)
                {
                    var sample = cognitiveAttributions[i]; if (sample.OperationId != operation.OperationId) continue;
                    foreach (string knowledge in sample.Knowledge)
                        cognitiveExperience.Record(cognitiveBattleId + ":" + operation.OperationId + ":" + knowledge,
                            knowledge, sample.Operators, sample.Context, sample.Goal,
                            operation.PrimaryGoalAchieved ? 1 : operation.StrategicAbort ? 0 : -1,
                            Mathf.Abs(sample.Predicted - sample.Observed), operation.StrategicAbort, operation.AbortReason.ToString());
                    cognitiveAttributions.RemoveAt(i);
                }
            if (CanLearn && AICognitiveAIConfig.Active.EnableGeneralizedExperience && cognitiveExperience.Dirty
                && cognitiveRepository?.Save(cognitiveExperience.Profile) == true) cognitiveExperience.MarkSaved();
        }
        catch (Exception error) { CognitiveFailed(error); }
    }
    void ReleaseCognition()
    {
        if (cognitivePending || cognitiveFallbackRecord != null) { cognitiveReleaseRequested = true; return; }
        EndCognitiveTurn(); cognitiveWorldBuilder?.Clear(); cognitiveProblems?.Clear(); cognitiveHypotheses?.Clear();
        cognitivePlanner?.Clear(); cognitiveWorld = null; cognitiveMatches.Clear(); cognitiveAttributions.Clear();
        cognitiveAssessments.Clear(); currentCognitiveProblems = Array.Empty<AIProblem>(); currentCognitiveHypotheses = Array.Empty<AIHypothesis>();
        cognitiveActionKnowledge.Clear();
        pendingCognitivePlan = null; pendingCognitiveAction = null; cognitiveReleaseRequested = false;
        cognitiveFallbackRecord = null;
    }
}
