#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>Controlled policy contracts, distinct from ordinary-game telemetry.</summary>
public static class AIAdaptivePolicyTests
{
    static int checks;
    static void Check(string id, bool ok)
    { if (!ok) throw new InvalidOperationException("[AIAdaptive] FAIL " + id); checks++; Debug.Log("[AIAdaptive] PASS " + id); }
    static AIActionContextSnapshot Snapshot() => new AIActionContextSnapshot
    { CurrentThreatLevel = 15, HasObjective = true, OwnUnitCount = 5, EconomyState = EconomicState.Healthy };
    static AIActionRecord Result(AIAdaptiveRule rule, float reward) => new AIActionRecord
    {
        ActionType = rule == AIAdaptiveRule.Siege ? AIActionType.Attack : AIActionType.Move,
        AdaptiveRule = (int)rule, Reward = reward, ExecutionSucceeded = true,
        Before = Snapshot(), After = Snapshot(), Outcome = new AIActionOutcome { DistanceToObjectiveDelta = reward > 0 ? 1 : 0 }
    };
    public static void Run()
    {
        checks = 0;
        var config = ScriptableObject.CreateInstance<AIReflectionConfig>();
        config.EnableFileDiary = false; config.EnableConsoleDiary = false;
        try
        {
            var policy = new AIAdaptiveDecisionPolicy(config);
            var march = new AIAction { ActionType = AIActionType.Move, StrategicPriority = 3 };
            var attack = new AIAction { ActionType = AIActionType.Attack, StrategicPriority = 3 };
            // A controlled observed siege outcome lowers that policy; productive marching raises its policy.
            for (int turn = 0; turn < 3; turn++)
            { policy.Observe(Result(AIAdaptiveRule.Advance, 1)); policy.Observe(Result(AIAdaptiveRule.Combat, -1)); policy.EndTurn(Snapshot()); }
            float marchBonus = policy.Modifier(march, 15), attackBonus = policy.Modifier(attack, 15);
            Check("successful-advance-learned", marchBonus > 0);
            Check("unproductive-combat-deprioritized", attackBonus < 0);
            Check("ordinary-candidate-order-changes", 15 < 20 && 15 + marchBonus > 20 + attackBonus);
            Check("score-cap", Mathf.Abs(marchBonus) <= 18 && Mathf.Abs(attackBonus) <= 18);
            march.StrategicPriority = 2;
            Check("survival-and-essential-tiers-immutable", policy.Modifier(march, 15) == 0);
            march.StrategicPriority = 3;
            Check("low-difficulty-unchanged", policy.Modifier(march, 9) == 0);
            config.EnableAdaptivePolicy = false;
            Check("disable-is-neutral", policy.Modifier(march, 15) == 0);
            config.EnableAdaptivePolicy = true;
            Check("faction-state-isolated", new AIAdaptiveDecisionPolicy(config).Modifier(march, 15) == 0);
            var saved = policy.Capture();
            saved.Values[0] = float.NaN; saved.Samples[0] = int.MaxValue;
            var restored = new AIAdaptiveDecisionPolicy(config); restored.Restore(saved);
            Check("corrupt-values-bounded", restored.Capture().Values[0] == 0 && restored.Capture().Samples[0] == 1000000);
            Check("save-copy-isolated", policy.Capture().Samples[0] == 0);
            policy.Observe(Result(AIAdaptiveRule.Advance, .5f));
            restored.Restore(JsonUtility.FromJson<AIAdaptivePolicyState>(JsonUtility.ToJson(policy.Capture())));
            policy.EndTurn(Snapshot()); restored.EndTurn(Snapshot());
            Check("mid-turn-save-resume-preserves-outcome", Mathf.Approximately(policy.Modifier(march, 15), restored.Modifier(march, 15)));
            restored.Restore(null);
            for (int i = 0; i < 3; i++) restored.EndTurn(Snapshot());
            Check("known-objective-stall-adapts", restored.Modifier(march, 15) > 0 && restored.Capture().StalledTurns == 3);
            var emergency = Snapshot(); emergency.EmergencyDefense = true; restored.EndTurn(emergency);
            Check("emergency-cancels-stall-pressure", restored.Capture().StalledTurns == 0);
            var empty = Snapshot(); empty.HasObjective = false;
            for (int i = 0; i < 12; i++) restored.EndTurn(empty);
            Check("no-hidden-goal-manufactured", restored.Capture().StalledTurns == 0);
            // Integration: real reflection begin/complete/end feeds the policy and its save snapshot.
            var system = new AIActionReflectionSystem(Team.Player, config, persistent: false);
            system.BeginBattle("adaptive-contract", 15);
            for (int turn = 1; turn <= 3; turn++)
            {
                system.BeginTurn(turn);
                var before = Snapshot(); before.Turn = turn; before.ActorHP = 35; before.ActorHpRatio = 1;
                before.ActorRole = "Infantry"; before.DistanceToObjective = 5; before.ObjectiveIdentity = "observed-territory";
                before.ObjectiveDistanceMetric = AIObjectiveDistanceMetric.ChebyshevXZ;
                var after = before.Copy(); after.ActorX = 1; after.DistanceToObjective = 4;
                var token = system.BeginAction(march, before); system.CompleteAction(token, true, after);
                system.EndTurn(turn, after);
            }
            Check("reflection-execution-updates-policy", system.CaptureAdaptivePolicy().Samples[(int)AIAdaptiveRule.Advance] == 3);
            var resume = system.CaptureBattleState();
            var other = new AIActionReflectionSystem(Team.Player, config, persistent: false);
            other.BeginBattle(resume: resume);
            Check("reflection-battle-save-resume", other.CaptureAdaptivePolicy().Revision == system.CaptureAdaptivePolicy().Revision);
            Debug.Log("[AIAdaptive] RESULT checks=" + checks);
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }
}
#endif
