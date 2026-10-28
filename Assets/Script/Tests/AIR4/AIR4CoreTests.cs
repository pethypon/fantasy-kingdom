#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>Explicit manual entry point. Importing scripts never executes tests or starts a game.</summary>
public static class AIR4CoreTests
{
    [UnityEditor.MenuItem("Fantasy Kingdom/AI/R1・R3・R4 契約テストを実行")]
    public static void RunAll()
    {
        if (!CanRun()) throw new InvalidOperationException("R1・R3・R4の契約テストは、再生を停止してから実行してください。");
        AIR4WorldModelTests.Run();
        AIR4KnowledgeTests.Run();
        AIR4PlannerTests.Run();
        AIR4SearchKeyTests.Run();
        AIR4CausalExperienceTests.Run();
        AIR4SaveTests.Run();
        AIR4DiagnosticTests.Run();
        AIR4LookaheadTests.Run();
        AIR4SquadPlanTests.Run();
        AIIntegratedR1ConfigTests.Run();
        AIIntegratedR1TimeTests.Run();
        AIIntegratedR1SquadTests.Run();
        AIIntegratedR1RecruitmentTests.Run();
        AIR3ScoutContinuityTests.Run();
        AIR3RecoveryEconomyStateTests.Run();
        AIR3MaterialCapacityTests.Run();
        AIPlaytestExplorationTests.Run();
        AIAdaptiveThinkingBudgetTests.Run();
        IntegratedCrystalRulesTests.Run(null);
        Debug.Log("[R4契約テスト] 登録済みの単体チェックを終了。実戦・152項目全体の合格を意味しません。");
    }
    [UnityEditor.MenuItem("Fantasy Kingdom/AI/R1・R3・R4 契約テストを実行", true)]
    static bool CanRun() => !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
        && !UnityEditor.EditorApplication.isCompiling && !UnityEditor.EditorApplication.isUpdating;
}

public static class AIR4SaveTests
{
    static void Check(string name, bool result)
    { if (!result) throw new Exception("[R4Save] FAIL " + name); Debug.Log("[R4Save] PASS " + name); }
    public static void Run()
    {
        var blank = new AICognitiveBattleState { Owner = Team.Enemy, BattleId = "test", NextEventId = 3 };
        Check("SV01/empty-extension", AICommander.ValidCognitiveState(blank, Team.Enemy, "test", 1));
        Check("SV05/foreign-owner", !AICommander.ValidCognitiveState(blank, Team.Player, "test", 1));
        Check("SV05/foreign-battle", !AICommander.ValidCognitiveState(blank, Team.Enemy, "another", 1));
        blank.Version = 2;
        Check("SV07/future-state-not-used", !AICommander.ValidCognitiveState(blank, Team.Enemy, "test", 1));
        blank.Version = 1; blank.NextEventId = -1;
        Check("SV06/negative-event", !AICommander.ValidCognitiveState(blank, Team.Enemy, "test", 1));
        blank.NextEventId = 0;
        blank.ActivePlan = new AICognitivePlan { OwnerTeam = Team.Enemy, BattleId = "test", PlanId = "test-plan", Signature = "test-plan",
            Goal = AIOperationGoal.DestroyEnemyCrystal, ObjectiveRef = "test-objective", PrimaryMethod = "ATTACK",
            CreatedTurn = 1, LastValidatedTurn = 1, BoardGenerationAtValidation = 1, StrategicPriorityBand = 3, Confidence = .5f };
        blank.ActivePlan.KnowledgeIds.Add("SL-04"); blank.ActivePlan.KnowledgeVersions.Add(1);
        blank.ActivePlan.Steps.Add(new AIPlanOperator { StepId = 0, OperatorId = "ATTACK", ExpectedCost = 3, BoundActionKey = "not-replayed" });
        Check("SV02/value-roundtrip", AICommander.ValidCognitiveState(JsonUtility.FromJson<AICognitiveBattleState>(JsonUtility.ToJson(blank)), Team.Enemy, "test", 1));
        var planner = new AICognitivePlanner(); planner.RestoreIntent(blank.ActivePlan);
        Check("SV03/no-reserved-action-replay", planner.Current != null && planner.Current.Steps.Count == 0 && planner.Current.Goal == blank.ActivePlan.Goal);
        var intent = new AICognitiveBattleState { Owner = Team.Enemy, BattleId = "test", ActivePlan = planner.Current };
        Check("SV03/restored-intent-can-be-saved-before-replanning", AICommander.ValidCognitiveState(intent, Team.Enemy, "test", 1));
        blank.ActivePlan.Confidence = float.NaN;
        Check("SV06/nonfinite", !AICommander.ValidCognitiveState(blank, Team.Enemy, "test", 1));
        blank.ActivePlan.Confidence = .5f; blank.ActivePlan.KnowledgeIds[0] = "UN-99";
        Check("SV06/unknown-knowledge", !AICommander.ValidCognitiveState(blank, Team.Enemy, "test", 1));
        blank.ActivePlan.KnowledgeIds[0] = "SL-04"; blank.ActivePlan.Steps[0].Dependencies = null;
        Check("SV06/null-dependency", !AICommander.ValidCognitiveState(blank, Team.Enemy, "test", 1));
    }
}
#endif
