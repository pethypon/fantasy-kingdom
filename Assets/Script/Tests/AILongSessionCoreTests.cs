#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;

/// <summary>Bounded display overflow and instrumentation-failure checks; no AI heuristic is mocked or changed.</summary>
public static class AILongSessionCoreTests
{
    static int passed;
    static void Check(string name, bool value)
    { if (!value) throw new InvalidOperationException("[LongSessionCore] FAIL " + name); passed++; }
    public static void RunAll()
    {
        passed = 0;
        var config = ScriptableObject.CreateInstance<AIPerformanceConfig>();
        var oldLevel = AIPerformanceConfig.Active.LogLevel;
        try
        {
            config.PresentationMaxQueue = 8; config.PresentationHighWaterMark = 6; config.PresentationLowWaterMark = 2;
            int critical = 0, emitted = 0;
            var queue = new AIPresentationQueue(e => { emitted++; if (e.Critical) critical++; }, config);
            for (int i = 0; i < 7; i++) queue.Enqueue(new AIPresentationEvent { Kind = AIPresentationKind.Move, ActorRuntimeId = i + 1 });
            Check("high water switches display only", queue.CatchUp && queue.Count == 7);
            queue.Drain(3); Check("hysteresis keeps catchup between boundaries", queue.CatchUp && queue.Count == 4);
            queue.Drain(2); Check("low water restores normal", !queue.CatchUp && queue.Count == 2);
            for (int i = 0; i < 100; i++) queue.Enqueue(new AIPresentationEvent { Kind = AIPresentationKind.Damage, Text = "event " + i });
            Check("backlog never exceeds configured limit", queue.Count <= 8);
            queue.Drain(1000); Check("critical overflow delivery preserves every event", critical == 100);
            Check("only low priority entries can be dropped", queue.DroppedLowPriority == 2 && emitted == 105);
            foreach (AIPresentationKind kind in Enum.GetValues(typeof(AIPresentationKind)))
                if (kind >= AIPresentationKind.Damage)
                { queue.Enqueue(new AIPresentationEvent { Kind = kind }); queue.Drain(1); }
            Check("all critical categories are delivered", critical == 111);
            for (int i = 0; i < 7; i++) queue.Enqueue(new AIPresentationEvent { Kind = AIPresentationKind.Move, ActorRuntimeId = i + 1 });
            queue.Enqueue(new AIPresentationEvent { Kind = AIPresentationKind.Move, ActorRuntimeId = 7 });
            Check("same actor move display coalesces under pressure", queue.Count == 7);
            queue.Clear(); Check("scene cleanup clears display state", queue.Count == 0 && !queue.CatchUp);
            AIPerformanceConfig.Active.LogLevel = AILogLevel.Off;
            int formats = 0;
            for (int i = 0; i < 100; i++) if (DevelopmentLog.IsEnabled()) { formats++; DevelopmentLog.Log("unused " + i); }
            Check("disabled logs do not format hot-path strings", formats == 0);
            AIPerformanceConfig.Active.LogLevel = AILogLevel.Summary;
            Check("Summary excludes candidate details", !DevelopmentLog.IsEnabled(AILogLevel.Verbose) && !DevelopmentLog.IsEnabled(AILogLevel.Trace));
            Check("Summary allows one turn summary", DevelopmentLog.IsEnabled(AILogLevel.Summary));
            config.PresentationHighWaterMark = 10000; config.PresentationLowWaterMark = 20000;
            Check("malformed water marks remain ordered", config.HighWater <= config.QueueLimit && config.LowWater < config.HighWater);
            config.MaxVisibleDiaryEvents = int.MaxValue; config.MaxDiaryVisibleCharacters = int.MaxValue;
            Check("malformed diary budgets remain bounded", config.DiaryEventLimit == 500 && config.DiaryCharacterLimit == 60000);
            for (int i = 0; i < 2100; i++)
            { AIPerformanceCounters.BeginTurn(Team.Enemy, i); AIPerformanceCounters.RecordTurnSlice(Team.Enemy, 1, -1); AIPerformanceCounters.EndTurn(Team.Enemy, default); }
            Check("turn sample ring cannot grow with session duration", AIPerformanceCounters.RetainedTurns(Team.Enemy) <= 2000);
            Check("unsupported allocation is not reported as zero", AIPerformanceCounters.GetRecentTurn(Team.Enemy).AllocationBytes == -1);
            AIPerformanceCounters.DisableInstrumentation("intentional isolated counter failure");
            Check("instrumentation failure disables itself", !AIPerformanceCounters.Enabled);
            using (AIPerformanceCounters.Measure(AIPerformanceStage.Search)) { Check("AI code continues after failed diagnostics", true); }
            Check("failed counter methods remain nonthrowing", true);
        }
        finally { AIPerformanceConfig.Active.LogLevel = oldLevel; UnityEngine.Object.DestroyImmediate(config); AIPerformanceCounters.Reset(); }
        Debug.Log("[LongSessionCore] " + passed + " presentation/log/counter checks passed");
    }
}
#endif
