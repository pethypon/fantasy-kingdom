#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Source-only time-policy contracts. Importing this file never starts a game or executes a test.</summary>
public static class AIIntegratedR1TimeTests
{
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(string id, bool value)
    { if (!value) throw new InvalidOperationException("[R1Time] FAIL " + id); Debug.Log("[R1Time] PASS " + id); }

    public static void Run()
    {
        var clock = typeof(AITurnBudget).GetField("clock", PrivateStatic);
        var wall = typeof(AITurnBudget).GetField("wallClock", PrivateStatic);
        var limit = typeof(AITurnBudget).GetField("limit", PrivateStatic);
        var activeConfig = typeof(AIThinkingBudgetConfig).GetField("active", PrivateStatic);
        var oldClock = clock.GetValue(null); var oldWall = wall.GetValue(null); var oldLimit = limit.GetValue(null);
        var oldConfig = activeConfig.GetValue(null);
        var config = ScriptableObject.CreateInstance<AIThinkingBudgetConfig>();
        try
        {
            DeadlineBounds(config);
            activeConfig.SetValue(null, config);
            PreparedAttemptOwnership(config);
            MenuPausesBothClocks(clock, wall);
        }
        finally
        {
            clock.SetValue(null, oldClock); wall.SetValue(null, oldWall); limit.SetValue(null, oldLimit);
            activeConfig.SetValue(null, oldConfig); UnityEngine.Object.DestroyImmediate(config);
        }
    }

    static void DeadlineBounds(AIThinkingBudgetConfig config)
    {
        Check("RE-time-init-formal-window", AITurnBudget.ClampCompletionDeadline(8500, 2300) == 2300);
        Check("RE-time-zero-window", AITurnBudget.ClampCompletionDeadline(8500, 0) == 0);
        Check("RE-time-no-window-extension", AITurnBudget.ClampCompletionDeadline(8000, 7000) == 7000);
        Check("RE-time-extreme-deadline-bounded", AITurnBudget.ClampCompletionDeadline(double.MaxValue) == 8500);
        foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, -1d })
            Check("RE-time-invalid-formal-" + invalid, AITurnBudget.ClampCompletionDeadline(8000, invalid) == 0);
        foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1d })
            Check("RE-time-invalid-request-" + invalid, AITurnBudget.ClampCompletionDeadline(invalid) == 0);
        foreach (var side in new[] { Team.Player, Team.Enemy })
        {
            var decision = config.Decide(new AIThinkingBudgetFacts(true, true, true), side, 9);
            Check("RE-time-shared-adopted-completion-" + side,
                decision.Milliseconds == 7500 && AITurnBudget.ClampCompletionDeadline(decision.CompletionDeadlineMilliseconds) == 8000);
            var formal = config.Decide(new AIThinkingBudgetFacts(true, true, true), side, 9, 2000);
            Check("RE-time-formal-overrides-policy-" + side, formal.Milliseconds == 2000 && formal.CompletionDeadlineMilliseconds == 2000);
        }
    }

    static void PreparedAttemptOwnership(AIThinkingBudgetConfig config)
    {
        // Only the pure budget boundary is invoked. No board, Unity scene or turn execution is constructed.
        var commander = (AICommander)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(AICommander));
        var begin = typeof(AICommander).GetMethod("BeginThinkingBudgetSelection", PrivateInstance);
        var resolve = typeof(AICommander).GetMethod("ResolveTurnThinkingBudget", PrivateInstance);
        commander.TurnThinkingBudgetMs = 3000;
        long previous = commander.ThinkingBudgetSequence;
        begin.Invoke(commander, null);
        Check("RE-time-unresolved-attempt-not-adopted", !commander.TryGetPreparedThinkingBudget(previous, commander.ActorTeam, out _));
        resolve.Invoke(commander, new object[] { null });
        Check("RE-time-new-attempt-adopted", commander.TryGetPreparedThinkingBudget(previous, commander.ActorTeam, out var decision)
            && decision.Milliseconds == 2500 && commander.ThinkingBudgetSequence != previous);
        Team foreign = commander.ActorTeam == Team.Player ? Team.Enemy : Team.Player;
        Check("RE-time-foreign-faction-not-adopted", !commander.TryGetPreparedThinkingBudget(previous, foreign, out _));
        Check("RE-time-old-prepared-result-not-adopted", !commander.TryGetPreparedThinkingBudget(commander.ThinkingBudgetSequence,
            commander.ActorTeam, out _));
        config.NormalSeconds = 3;
        resolve.Invoke(commander, new object[] { null });
        Check("RE-time-budget-frozen-until-next-attempt", commander.ActiveThinkingBudget.Milliseconds == 2500);
        previous = commander.ThinkingBudgetSequence;
        begin.Invoke(commander, null);
        Check("RE-time-cancelled-before-classification-cannot-reuse-old-result",
            !commander.TryGetPreparedThinkingBudget(previous, commander.ActorTeam, out _));
        resolve.Invoke(commander, new object[] { null });
        Check("RE-time-next-attempt-uses-new-setting", commander.TryGetPreparedThinkingBudget(previous, commander.ActorTeam, out decision)
            && decision.Milliseconds == 3000);
    }

    static void MenuPausesBothClocks(FieldInfo clock, FieldInfo wall)
    {
        var controller = new DeveloperPlayerAIController(null);
        typeof(DeveloperPlayerAIController).GetField("steps", PrivateInstance).SetValue(controller, Empty());
        var elapsed = (Stopwatch)typeof(DeveloperPlayerAIController).GetField("elapsed", PrivateInstance).GetValue(controller);
        elapsed.Start(); AITurnBudget.Begin(2500);
        controller.PauseForMenu(); controller.PauseForMenu();
        Check("RE-time-menu-pauses-driver-and-shared-clocks", !elapsed.IsRunning
            && !((Stopwatch)clock.GetValue(null)).IsRunning && !((Stopwatch)wall.GetValue(null)).IsRunning);
        double stopped = elapsed.Elapsed.TotalMilliseconds;
        typeof(DeveloperPlayerAIController).GetMethod("Stop", PrivateInstance).Invoke(controller, null);
        Check("RE-time-cancel-leaves-driver-stopped", !elapsed.IsRunning && elapsed.Elapsed.TotalMilliseconds == stopped);
        controller.Dispose();
    }
    static IEnumerator Empty() { yield break; }
}
#endif
