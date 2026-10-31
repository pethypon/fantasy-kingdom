#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Value-only objective provenance checks, callable by NativeFocusedContracts.</summary>
public static class AIReflectionObjectiveDistanceTests
{
    static int passed;
    static void Check(string label, bool result)
    {
        if (!result) throw new InvalidOperationException("[AIReflectionObjectiveDistance] FAIL " + label);
        passed++; Debug.Log("[AIReflectionObjectiveDistance] PASS " + label);
    }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
    static T RoundTrip<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
    static AIActionContextSnapshot Facts(int turn = 1) => new AIActionContextSnapshot
    {
        Turn = turn, ActionType = AIActionType.Move, Strategy = "Balanced", MatchPhase = "Early",
        ActorRole = "Infantry", ActorHP = 100, ActorHpRatio = 1, ActorAp = 30,
        TargetCategory = "Infantry", TargetObserved = true, TargetHP = 100, KnownTargetLifeId = "enemy-a",
        LocalAllyPower = 10, LocalEnemyPower = 10, OwnCrystalHP = 15000, OwnCrystalHpRatio = 1,
        OwnUnitCount = 10, VisibleAllyCount = 10, VisibleEnemyCount = 1, VisibleEnemyUnitCount = 1,
        EconomyState = EconomicState.Healthy, Bread = 100, Iron = 100, Wood = 100, Stone = 100,
        DistanceToTarget = 1, OwnCrystalDistance = 5, CurrentThreatLevel = 15
    };
    static AIActionRecord Record(AIActionType type, long id = 1, AIActionContextSnapshot before = null, AIActionContextSnapshot after = null)
    {
        before = before ?? Facts(); after = after ?? before.Copy(); before.ActionType = after.ActionType = type;
        return new AIActionRecord { ActionId = id, BattleId = "Integrated", Turn = before.Turn,
            ActionType = type, ActorRole = before.ActorRole, TargetCategory = before.TargetCategory,
            ActorLifeId = "actor-a", TargetLifeId = before.KnownTargetLifeId, Before = before, After = after,
            Outcome = AIActionOutcome.Difference(before, after), ExecutionSucceeded = true,
            ContextKey = AIActionFeatureExtractor.ContextKey(before),
            ActionKey = AIActionFeatureExtractor.ActionKey(new AIAction { ActionType = type }, before) };
    }
    static float Evaluate(AIActionRecord record, AIReflectionConfig config)
    {
        record.MeaningfulProgress = AIFailureAnalyzer.HasMeaningfulProgress(record, config);
        record.FailureReason = AIFailureAnalyzer.Analyze(record, null, config);
        record.StrategicOutcome = record.FailureReason != AIFailureReason.None ? AIStrategicOutcome.Failure
            : record.MeaningfulProgress ? AIStrategicOutcome.Progress : AIStrategicOutcome.Neutral;
        record.Reward = AIActionRewardEvaluator.Evaluate(record, config); return record.Reward;
    }
    public static void Run()
    {
        passed = 0;
        var config = ScriptableObject.CreateInstance<AIReflectionConfig>();
        config.EnableConsoleDiary = config.EnableFileDiary = false;
        try { ObjectiveDistanceProvenance(config); }
        finally { UnityEngine.Object.DestroyImmediate(config); }
        Debug.Log("[AIReflectionObjectiveDistance] " + passed + " objective provenance checks passed");
    }
    static void ObjectiveDistanceProvenance(AIReflectionConfig config)
    {
        var before = Facts(); before.HasObjective = true; before.ObjectiveIdentity = "Exploration:7";
        before.ObjectiveX = 10; before.DistanceToObjective = 10;
        before.ObjectiveDistanceMetric = AIObjectiveDistanceMetric.ChebyshevXZ;
        var after = before.Copy(); after.ActorX = 2; after.DistanceToObjective = 8;
        var forward = Record(AIActionType.Move, before: before, after: after); Evaluate(forward, config);
        Check("same objective and fixed metric earn genuine objective progress", forward.Outcome.DistanceToObjectiveDelta == 2
            && forward.MeaningfulProgress && forward.RewardBreakdown.Objective > 0);
        var backward = Record(AIActionType.Move, before: after, after: before); Evaluate(backward, config);
        Check("reverse movement cannot also report positive progress to the same objective", backward.Outcome.DistanceToObjectiveDelta == -2
            && !backward.MeaningfulProgress && Near(backward.RewardBreakdown.Objective, 0));
        foreach (string mismatch in new[] { "Identity", "Coordinate", "Height", "Metric", "Legacy" })
        {
            var changed = after.Copy();
            if (mismatch == "Identity") changed.ObjectiveIdentity = "Exploration:8";
            if (mismatch == "Coordinate") changed.ObjectiveX++;
            if (mismatch == "Height") changed.ObjectiveHeight++;
            if (mismatch == "Metric") changed.ObjectiveDistanceMetric = AIObjectiveDistanceMetric.Unknown;
            if (mismatch == "Legacy") changed.ObjectiveIdentity = null;
            var record = Record(AIActionType.Move, before: before, after: changed);
            Check("incomparable objective distances are neutral: " + mismatch, record.Outcome.DistanceToObjectiveDelta == 0);
            // Persisted old outcomes may still claim positive progress; consumers verify snapshots.
            record.Outcome.DistanceToObjectiveDelta = 2; Evaluate(record, config);
            Check("old positive outcome cannot manufacture learning evidence: " + mismatch,
                !record.MeaningfulProgress && Near(record.RewardBreakdown.Objective, 0) && !record.PreparationCandidate);
        }
        var legacyBefore = JsonUtility.FromJson<AIActionContextSnapshot>("{\"HasObjective\":true,\"DistanceToObjective\":10}");
        var legacyAfter = JsonUtility.FromJson<AIActionContextSnapshot>("{\"HasObjective\":true,\"DistanceToObjective\":8}");
        Check("missing optional save fields remain unverified", legacyBefore.ObjectiveDistanceMetric == AIObjectiveDistanceMetric.Unknown
            && AIActionOutcome.Difference(legacyBefore, legacyAfter).DistanceToObjectiveDelta == 0);
        var copied = RoundTrip(before.Copy());
        Check("new objective provenance survives copying and JSON continuation", copied.ObjectiveIdentity == before.ObjectiveIdentity
            && AIActionOutcome.Difference(copied, after).DistanceToObjectiveDelta == 2);
        copied.ObjectiveIdentity = "Exploration:99"; copied.ObjectiveX = 999; copied.ObjectiveHeight = 4;
        Check("battle-local objective provenance never enters generalized knowledge keys",
            AIActionFeatureExtractor.ContextKey(copied) == AIActionFeatureExtractor.ContextKey(before)
            && AIActionFeatureExtractor.ActionKey(new AIAction { ActionType = AIActionType.Move }, copied)
                == AIActionFeatureExtractor.ActionKey(new AIAction { ActionType = AIActionType.Move }, before));
        var repeats = new AIRepeatActionDetector(); AIActionRecord last = null;
        for (int i = 0; i < 3; i++)
        {
            var from = before.Copy(); var to = after.Copy();
            if (i % 2 == 1) { from.ActorX = 2; to.ActorX = 0; }
            from.ObjectiveIdentity = "Exploration:" + (20 + i * 2);
            to.ObjectiveIdentity = "Exploration:" + (21 + i * 2);
            last = Record(AIActionType.Move, i + 1, from, to); Evaluate(last, config); repeats.Evaluate(last);
        }
        Check("retargeted no-discovery reversals reach the oscillation detector", last.IsOscillation && !last.MeaningfulProgress);
        var oldRecord = Record(AIActionType.Move, before: before, after: after);
        oldRecord.Before.ObjectiveIdentity = oldRecord.After.ObjectiveIdentity = null;
        oldRecord.Outcome.DistanceToObjectiveDelta = 2; oldRecord.MeaningfulProgress = oldRecord.PreparationCandidate = true;
        oldRecord.SuccessReasons.Add(AISuccessReason.ObjectiveProgress);
        var battle = new AIReflectionBattleState { BattleId = "LegacyObjective", Faction = Team.Enemy, NextActionId = 1 };
        oldRecord.Reward = .7f; battle.BattleReward = .7f;
        battle.LearningProfile = new AIActionLearningProfile { ProfileId = "PreservedObjectiveHistory" };
        battle.RecentRecords.Add(oldRecord);
        battle.DelayedCreditState = new AIDelayedCreditState();
        battle.DelayedCreditState.RecentActions.Add(new AIDelayedActionEvidence { ActionId = 1, ActorLifeId = "actor-a",
            Preparation = true, MeaningfulPosition = true, GrantedReward = .2f, AppliedEventIds = new List<string> { "already-paid" } });
        Check("legacy battle remains loadable while unproved objective evidence is withdrawn", AIReflectionMigration.UpgradeBattle(battle, config)
            && oldRecord.Outcome.DistanceToObjectiveDelta == 0 && !oldRecord.MeaningfulProgress && !oldRecord.PreparationCandidate
            && !oldRecord.SuccessReasons.Contains(AISuccessReason.ObjectiveProgress)
            && Near(oldRecord.Reward, .7f) && Near(battle.BattleReward, .7f) && battle.LearningProfile != null);
        var delayed = new AIDelayedCreditAssigner(config); delayed.Restore(battle.DelayedCreditState, battle.RecentRecords);
        var restored = delayed.Capture().RecentActions[0];
        Check("legacy causal credit preserves caps and dedupe without granting future objective credit", !restored.Preparation
            && !restored.MeaningfulPosition && Near(restored.GrantedReward, .2f) && restored.AppliedEventIds.Contains("already-paid")
            && delayed.ApplyOutcome(new AIDelayedOutcomeEvent { EventId = "future-kill", ActionId = 2, ActorLifeId = "actor-a" }).Count == 0);
        var otherSuccess = Record(AIActionType.Move, before: oldRecord.Before.Copy(), after: oldRecord.After.Copy());
        otherSuccess.After.TargetHP -= 7; otherSuccess.After.ExploredTiles++;
        otherSuccess.Outcome = AIActionOutcome.Difference(otherSuccess.Before, otherSuccess.After);
        otherSuccess.Outcome.DistanceToObjectiveDelta = 2;
        otherSuccess.MeaningfulProgress = otherSuccess.PreparationCandidate = true;
        battle.RecentRecords[0] = otherSuccess;
        Check("legacy objective withdrawal preserves independent damage and discovery", AIReflectionMigration.UpgradeBattle(battle, config)
            && otherSuccess.Outcome.DistanceToObjectiveDelta == 0 && otherSuccess.Outcome.DamageDealt == 7
            && otherSuccess.Outcome.NewTilesRevealed == 1 && otherSuccess.MeaningfulProgress);
        delayed.Restore(battle.DelayedCreditState, battle.RecentRecords); restored = delayed.Capture().RecentActions[0];
        Check("independent discovery cannot preserve a legacy false positioning claim", restored.Preparation
            && !restored.MeaningfulPosition
            && delayed.ApplyOutcome(new AIDelayedOutcomeEvent { EventId = "unrelated-kill", ActionId = 2, ActorLifeId = "actor-a" }).Count == 0);
    }

}
#endif
