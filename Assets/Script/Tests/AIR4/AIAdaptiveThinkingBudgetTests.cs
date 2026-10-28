#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

/// <summary>Registered policy/timer contracts. Not a match, performance benchmark or AI-behaviour audit.</summary>
public static class AIAdaptiveThinkingBudgetTests
{
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static int passed;
    static void Check(string description, bool value)
    {
        if (!value) throw new InvalidOperationException("[AIAdaptiveBudget] FAIL " + description);
        passed++; Debug.Log("[AIAdaptiveBudget] PASS " + description);
    }
    public static void Run()
    {
        passed = 0;
        var config = ScriptableObject.CreateInstance<AIThinkingBudgetConfig>();
        var host = new GameObject("Adaptive budget isolated timer"); host.SetActive(false);
        var clock = typeof(AITurnBudget).GetField("clock", PrivateStatic);
        var wall = typeof(AITurnBudget).GetField("wallClock", PrivateStatic);
        var limit = typeof(AITurnBudget).GetField("limit", PrivateStatic);
        var savedClock = clock.GetValue(null); var savedWall = wall.GetValue(null); var savedLimit = limit.GetValue(null);
        try
        {
            foreach (var side in new[] { Team.Player, Team.Enemy })
            {
                var normal = config.Decide(new AIThinkingBudgetFacts(false, false, false), side, 21);
                var combat = config.Decide(new AIThinkingBudgetFacts(false, false, true), side, 21);
                var coordinated = config.Decide(new AIThinkingBudgetFacts(false, true, true), side, 21);
                var critical = config.Decide(new AIThinkingBudgetFacts(true, true, true), side, 21);
                Check(side + " ordinary context uses the adjustable 2.5-second proposal", normal.Milliseconds == 2500
                    && normal.Category == AIThinkingBudgetCategory.Normal && normal.Team == side && normal.Turn == 21);
                Check(side + " observed combat uses the 5-second proposal", combat.Milliseconds == 5000
                    && combat.Category == AIThinkingBudgetCategory.Combat);
                Check(side + " coordinated combat uses the 6-second proposal", coordinated.Milliseconds == 6000
                    && coordinated.Category == AIThinkingBudgetCategory.Coordination);
                Check(side + " highest applicable category wins once instead of adding four budgets", critical.Milliseconds == 7500
                    && critical.Category == AIThinkingBudgetCategory.Critical && critical.CompletionDeadlineMilliseconds == 8000);
                var limited = config.Decide(new AIThinkingBudgetFacts(true, true, true), side, 22, 3000);
                Check(side + " a shorter official clock remains a hard rule without extension", limited.RequestedMilliseconds == 7500
                    && limited.Milliseconds == 3000 && limited.CompletionDeadlineMilliseconds == 3000 && limited.OfficialTimerLimited);
                var exhausted = config.Decide(new AIThinkingBudgetFacts(true, false, false), side, 22, 0);
                Check(side + " an exhausted clock cannot receive a thinking or finishing-time gift", exhausted.Milliseconds == 0
                    && exhausted.CompletionDeadlineMilliseconds == 0 && exhausted.OfficialTimerLimited);
            }
            LegacyPreferenceSurvivesAdaptiveSelection(config);
            config.NormalSeconds = -100; config.CombatSeconds = 100;
            config.CoordinationSeconds = float.NaN; config.CriticalSeconds = float.PositiveInfinity;
            Check("invalid normal/combat settings stay in their required ranges",
                config.Decide(default, Team.Enemy, 1).Milliseconds == 2000
                && config.Decide(new AIThinkingBudgetFacts(false, false, true), Team.Enemy, 1).Milliseconds == 6000);
            Check("nonfinite coordination/critical settings use finite proposal defaults",
                config.Decide(new AIThinkingBudgetFacts(false, true, false), Team.Enemy, 1).Milliseconds == 6000
                && config.Decide(new AIThinkingBudgetFacts(true, false, false), Team.Enemy, 1).Milliseconds == 7500);
            Check("a malformed official timer never grants unlimited time",
                config.Decide(default, Team.Enemy, 1, double.NaN).Milliseconds == 0
                && config.Decide(default, Team.Enemy, 1, double.NegativeInfinity).Milliseconds == 0);
            config.Enabled = false;
            Check("compatibility budget remains finite and bounded", config.Decide(default, Team.Enemy, 1,
                double.PositiveInfinity, double.PositiveInfinity).Milliseconds == 3000
                && config.Decide(default, Team.Enemy, 1, double.PositiveInfinity, 100000).Milliseconds == 8000);

            AITurnBudget.Begin(8000);
            var runningClock = clock.GetValue(null); var runningWall = wall.GetValue(null);
            double setupElapsed = AITurnBudget.ElapsedMs;
            AITurnBudget.SetLimitWithoutRestart(2500);
            Check("committing the selected budget retains setup CPU/wall clocks", ReferenceEquals(runningClock, clock.GetValue(null))
                && ReferenceEquals(runningWall, wall.GetValue(null)) && AITurnBudget.ElapsedMs >= setupElapsed && AITurnBudget.LimitMs == 2500);
            AITurnBudget.SetLimitWithoutRestart(0);
            Check("an already-exhausted selected budget expires immediately", AITurnBudget.Expired);
            AITurnBudget.Begin(double.PositiveInfinity);
            Check("generic budget cannot become unbounded from nonfinite input", AITurnBudget.Expired);

            var timer = host.AddComponent<TimerSystem>(); timer.TurnTimeLimit = 180; timer.PlayerTotalTime = 60;
            timer.StartTurn(Team.Player);
            Check("timer policy reads the current side without rewriting the formal limit", timer.AvailableAIThinkingMilliseconds(Team.Player) == 60000
                && timer.TurnTimeLimit == 180 && timer.TurnTimeRemaining == 180
                && double.IsPositiveInfinity(timer.AvailableAIThinkingMilliseconds(Team.Enemy)));
            timer.RestoreTurnTimeRemaining(2);
            Check("short remaining player time is not relaxed for automation", timer.AvailableAIThinkingMilliseconds(Team.Player) == 2000
                && timer.PlayerTimeRemaining == 60 && timer.TurnTimeRemaining == 2);
            int expirations = 0; timer.OnTurnTimeExpired += () => expirations++;
            var advance = typeof(TimerSystem).GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic);
            advance.Invoke(timer, new object[] { 3f }); advance.Invoke(timer, new object[] { 3f });
            Check("formal timer completion remains one-shot", expirations == 1 && !timer.IsRunning && timer.TurnTimeRemaining == 0);
        }
        finally
        {
            clock.SetValue(null, savedClock); wall.SetValue(null, savedWall); limit.SetValue(null, savedLimit);
            UnityEngine.Object.DestroyImmediate(config); UnityEngine.Object.DestroyImmediate(host);
        }
        Debug.Log("[AIAdaptiveBudget] ALL PASSED " + passed);
    }

    static void LegacyPreferenceSurvivesAdaptiveSelection(AIThinkingBudgetConfig config)
    {
        // Property/policy fixture only: no commander initialization, gameplay turn,
        // observed actors or execution. Exercise the actual budget commit methods.
        var commander = (AICommander)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(AICommander));
        var active = typeof(AIThinkingBudgetConfig).GetField("active", PrivateStatic);
        var savedActive = active.GetValue(null);
        var begin = typeof(AICommander).GetMethod("BeginThinkingBudgetSelection", BindingFlags.Instance | BindingFlags.NonPublic);
        var resolve = typeof(AICommander).GetMethod("ResolveTurnThinkingBudget", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            active.SetValue(null, config); config.Enabled = true;
            commander.TurnThinkingBudgetMs = 3000;
            begin.Invoke(commander, null); resolve.Invoke(commander, new object[] { null });
            Check("adaptive selection reports its active decision", commander.TurnThinkingBudgetMs == 2500);
            config.Enabled = false;
            begin.Invoke(commander, null); resolve.Invoke(commander, new object[] { null });
            Check("disabling adaptation restores configured legacy time rather than the previous adaptive result",
                commander.TurnThinkingBudgetMs == 3000);
            commander.TurnThinkingBudgetMs = 1800;
            Check("a preference change does not alter an already-frozen turn", commander.TurnThinkingBudgetMs == 3000);
            begin.Invoke(commander, null); resolve.Invoke(commander, new object[] { null });
            Check("an explicit legacy preference applies at the next turn boundary", commander.TurnThinkingBudgetMs == 1800);
        }
        finally { active.SetValue(null, savedActive); config.Enabled = true; }
    }
}
#endif
