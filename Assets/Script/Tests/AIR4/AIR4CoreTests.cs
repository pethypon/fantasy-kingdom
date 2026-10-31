#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
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
        AIReflectionObjectiveDistanceTests.Run();
        AIHealerRoleRecruitmentTests.Run();
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
        OptionalInlineCompatibility();
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
        Check("SV02/pre-roundtrip-valid", AICommander.ValidCognitiveState(blank, Team.Enemy, "test", 1));
        string json = JsonUtility.ToJson(blank);
        var roundtrip = JsonUtility.FromJson<AICognitiveBattleState>(json);
        Check("SV02/value-roundtrip", AICommander.ValidCognitiveState(roundtrip, Team.Enemy, "test", 1));
        var restoredPlan = RestoredState(Restore(json, Team.Enemy));
        Check("SV02/active-plan-survives-absent-hypotheses", restoredPlan != null && restoredPlan.Hypotheses == null
            && restoredPlan.ActivePlan.PlanId == "test-plan" && restoredPlan.ActivePlan.OwnerTeam == Team.Enemy
            && restoredPlan.ActivePlan.BattleId == "test" && restoredPlan.ActivePlan.KnowledgeIds[0] == "SL-04"
            && restoredPlan.ActivePlan.Steps[0].BoundActionKey == "not-replayed");
        var planner = new AICognitivePlanner(); planner.RestoreIntent(roundtrip.ActivePlan);
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

    static void OptionalInlineCompatibility()
    {
        foreach (var side in new[] { Team.Player, Team.Enemy })
        {
            var empty = new AICognitiveBattleState { Owner = side, BattleId = "test", NextEventId = 7 };
            string json = JsonUtility.ToJson(empty);
            var materialized = JsonUtility.FromJson<AICognitiveBattleState>(json);
            var plan = materialized.ActivePlan; var hypotheses = materialized.Hypotheses;
            Check("SV02/null-optional-roundtrip/" + side, AICommander.ValidCognitiveState(materialized, side, "test", 1));
            Check("SV02/validation-is-read-only/" + side, ReferenceEquals(plan, materialized.ActivePlan)
                && ReferenceEquals(hypotheses, materialized.Hypotheses));
            var commander = Restore(json, side); var restored = RestoredState(commander);
            Check("SV02/empty-inline-extensions-restore-as-absent/" + side, restored != null
                && restored.ActivePlan == null && restored.Hypotheses == null
                && restored.Owner == side && restored.BattleId == "test" && restored.NextEventId == 7);
            var capture = typeof(AICommander).GetMethod("CaptureCognitiveState", BindingFlags.Instance | BindingFlags.NonPublic);
            var twice = RestoredState(Restore((string)capture.Invoke(commander, null), side));
            Check("SV02/absent-extensions-survive-resave/" + side, twice != null && twice.ActivePlan == null
                && twice.Hypotheses == null && twice.Owner == side && twice.BattleId == "test" && twice.NextEventId == 7);
        }
        var scoped = new AICognitiveBattleState { Owner = Team.Enemy, BattleId = "test", Hypotheses =
            new AIHypothesisBattleState { BattleId = "test", ActorTeam = Team.Enemy, LastOwnTurn = 1, LastGlobalTurn = 1 } };
        scoped.Hypotheses.ProcessedEventIds.Add("actual-event");
        var preserved = RestoredState(Restore(JsonUtility.ToJson(scoped), Team.Enemy));
        Check("SV02/scoped-hypothesis-data-is-preserved", preserved != null && preserved.ActivePlan == null
            && preserved.Hypotheses != null && preserved.Hypotheses.ActorTeam == Team.Enemy
            && preserved.Hypotheses.BattleId == "test" && preserved.Hypotheses.LastOwnTurn == 1
            && preserved.Hypotheses.LastGlobalTurn == 1 && preserved.Hypotheses.ProcessedEventIds.Count == 1
            && preserved.Hypotheses.ProcessedEventIds[0] == "actual-event");
        scoped.Hypotheses.ActorTeam = Team.Player;
        Check("SV05/foreign-hypothesis-owner-is-not-normalized", RestoredState(Restore(JsonUtility.ToJson(scoped), Team.Enemy)) == null);
        scoped.Hypotheses.ActorTeam = Team.Enemy; scoped.Hypotheses.BattleId = "foreign";
        Check("SV05/foreign-hypothesis-battle-is-not-normalized", RestoredState(Restore(JsonUtility.ToJson(scoped), Team.Enemy)) == null);
        CheckUnscopedData<AIHypothesisBattleState>((state, value) => state.Hypotheses = value);
        CheckUnscopedData<AICognitivePlan>((state, value) => state.ActivePlan = value);
        string future = "{\"Version\":2,\"Owner\":1,\"BattleId\":\"test\",\"NextEventId\":3,\"FutureOpaque\":\"retained\"}";
        var futureCommander = Restore(future, Team.Enemy);
        Check("SV07/future-json-is-preserved-before-normalization", RestoredState(futureCommander) == null
            && (string)typeof(AICommander).GetField("futureCognitiveJson", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(futureCommander) == future);
    }

    // Guard the format boundary independently of the hand-written empty-shape predicate:
    // any data in any serialized field must prevent an unscoped DTO from being discarded as absence.
    static void CheckUnscopedData<T>(Action<AICognitiveBattleState, T> assign) where T : new()
    {
        foreach (var field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = new T(); object original = field.GetValue(value); object changed;
            if (field.FieldType == typeof(string)) changed = "unscoped-data";
            else if (field.FieldType == typeof(int)) changed = (int)original + 1;
            else if (field.FieldType == typeof(long)) changed = (long)original + 1;
            else if (field.FieldType == typeof(float)) changed = 1e-10f;
            else if (field.FieldType == typeof(bool)) changed = !(bool)original;
            else if (field.FieldType == typeof(Vector3)) changed = new Vector3(1e-10f, 0, 0);
            else if (field.FieldType.IsEnum) changed = Enum.ToObject(field.FieldType, 1);
            else if (typeof(IList).IsAssignableFrom(field.FieldType))
            {
                var list = (IList)Activator.CreateInstance(field.FieldType);
                var element = field.FieldType.GetGenericArguments()[0];
                list.Add(element == typeof(string) ? "unscoped-data" : Activator.CreateInstance(element));
                changed = list;
            }
            else throw new InvalidOperationException("Add an optional DTO guard case for " + field.Name);
            field.SetValue(value, changed);
            var state = new AICognitiveBattleState { Owner = Team.Enemy, BattleId = "test" }; assign(state, value);
            Check("SV06/unscoped-data/" + typeof(T).Name + "/" + field.Name,
                !AICommander.ValidCognitiveState(state, Team.Enemy, "test", 1)
                && RestoredState(Restore(JsonUtility.ToJson(state), Team.Enemy)) == null);
            if (typeof(IList).IsAssignableFrom(field.FieldType))
            {
                field.SetValue(value, null);
                Check("SV06/null-optional-collection/" + typeof(T).Name + "/" + field.Name,
                    !AICommander.ValidCognitiveState(state, Team.Enemy, "test", 1));
            }
        }
    }

    static AICommander Restore(string json, Team side)
    {
        // Exercise the production read path without initializing a commander, profile, board or live actors.
        var commander = (AICommander)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(AICommander));
        typeof(AICommander).GetField("_actorTeam", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(commander, side);
        typeof(AICommander).GetMethod("RestoreCognitiveState", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(commander, new object[] { json, "test", 1, 1 });
        return commander;
    }
    static AICognitiveBattleState RestoredState(AICommander commander)
        => (AICognitiveBattleState)typeof(AICommander).GetField("restoredCognitiveState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(commander);
}
#endif
