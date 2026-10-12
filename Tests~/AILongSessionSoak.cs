#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
using Debug = UnityEngine.Debug;

/// <summary>
/// Isolated, fixed-workload endurance: actual Commander, actions, vision, reflection and operation run
/// continuously for 500 own turns. This is deliberately distinct from an ordinary battle which can end early.
/// No learning state is reset, no AI search/candidate capability is reduced, no forced GC is used.
/// </summary>
[InitializeOnLoad]
public static class AILongSessionSoak
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Running = "AILongSessionSoak.Running";
    static int Turns = 500;
    static EconomyR2Tests.Fixture fixture;
    static GameObject extra;
    static TurnGenerator hub;
    static AICommander commander;
    static GameSystems live;
    static VisionGenerator vision;
    static IEnumerator steps;
    static readonly double[] ms = new double[1000];
    static readonly long[] bytes = new long[1000], managed = new long[1000], native = new long[1000];
    static readonly StringBuilder trace = new StringBuilder(65536), csv = new StringBuilder(65536);
    static readonly Dictionary<string, string> identities = new Dictionary<string, string>();
    static int ownTurn, lastFrame = -1, actions, logCount;
    static bool initialized;
    static bool originalProfiler;
    static double started, cpu;
    static long allocated;
    static AIReflectionConfig reflectionConfig;
    static AIActionReflectionSystem reflection;
    static string label;
    static AILogLevelHolder logHolder;
    static byte[] calibrationBuffer;
    static ProfilerRecorder frameAllocation;
    static bool allocationCalibrated;
    // This reflection-based optional holder keeps the identical harness compilable against the before checkout.
    sealed class AILogLevelHolder { public FieldInfo Field; public object Config, Original; }
    static AILongSessionSoak() { EditorApplication.update += Tick; }
    public static void Run()
        => RunWithTurns(500);
    public static void Run1000()
        => RunWithTurns(1000);
    static void RunWithTurns(int targetTurns)
    {
        if (Application.dataPath.IndexOf("UnityValidation", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException("Use isolated UnityValidation only");
        initialized = false; ownTurn = actions = logCount = 0; cpu = 0; allocated = 0;
        trace.Clear(); csv.Clear(); identities.Clear(); label = Environment.GetEnvironmentVariable("FK_SOAK_LABEL") ?? "after";
        SessionState.SetString("AILongSessionSoak.Label", label);
        SessionState.SetInt("AILongSessionSoak.Turns", targetTurns);
        csv.AppendLine("turn,cpuMs,allocBytes,managedBytes,nativeBytes,positionKeys,positionEntries,sharedMemory,reflectionRecords,operationCompleted,logs");
        SessionState.SetBool(Running, true); started = EditorApplication.timeSinceStartup;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Running, false) || EditorApplication.isCompiling || !EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 1800) throw new TimeoutException("500-turn soak exceeded 30 minutes");
            if (!initialized) { Initialize(); return; }
            if (lastFrame == Time.frameCount) return; lastFrame = Time.frameCount;
            if (!allocationCalibrated)
            {
                long value = frameAllocation.LastValue;
                if (value < calibrationBuffer.Length) throw new InvalidOperationException("Frame allocation byte-counter calibration failed: " + value);
                Debug.Log("[LongSessionSoak] GC frame byte calibration=" + value); allocationCalibrated = true;
            }
            if (steps == null) StartTurn();
            allocated += frameAllocation.LastValue; // Complete frame bytes: includes UI/editor work, not claimed as AI-exclusive bytes.
            long ticks = Stopwatch.GetTimestamp();
            bool more;
            try { AITurnBudget.Resume(); more = steps.MoveNext(); }
            finally
            {
                AITurnBudget.Pause(); cpu += (Stopwatch.GetTimestamp() - ticks) * (1000d / Stopwatch.Frequency);
            }
            if (more) return;
            (steps as IDisposable)?.Dispose(); steps = null;
            ticks = Stopwatch.GetTimestamp();
            commander.EndReflectionTurn();
            cpu += (Stopwatch.GetTimestamp() - ticks) * (1000d / Stopwatch.Frequency);
            Record(); if (ownTurn >= Turns) Finish();
        }
        catch (Exception e) { Fail(e); }
    }
    static void Initialize()
    {
        // Entering play mode reloads static fields; keep a usable CSV header after that reload.
        if (csv.Length == 0) csv.AppendLine("turn,cpuMs,allocBytes,managedBytes,nativeBytes,positionKeys,positionEntries,sharedMemory,reflectionRecords,operationCompleted,logs");
        label = SessionState.GetString("AILongSessionSoak.Label", "after");
        Turns = SessionState.GetInt("AILongSessionSoak.Turns", 500);
        originalProfiler = Profiler.enabled; Profiler.maxUsedMemory = 16 * 1024 * 1024;
        Profiler.enabled = false; // Do not include the Editor Profiler's recording history in the game retention plateau.
        var game = UnityEngine.Object.FindFirstObjectByType<GameGenerator>();
        if (game == null || TitleScreenUI.Instance == null) return;
        UnityEngine.Random.InitState(56192);
        typeof(GameGenerator).GetField("_waitingForTitle", Private).SetValue(game, false);
        UnityEngine.Object.DestroyImmediate(TitleScreenUI.Instance.gameObject);
        typeof(GameGenerator).GetMethod("StartGameInit", Private).Invoke(game, new object[] { -1 });
        var main = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>(); main.enabled = false; main.Systems.TimerSystem.StopTurn(); live = main.Systems;
        fixture = new EconomyR2Tests.Fixture(live);
        extra = new GameObject("Long-session isolated AI hub"); extra.SetActive(false);
        hub = extra.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch(); vision = extra.AddComponent<VisionGenerator>(); vision.Awake();
        hub.Systems.MapCreate = live.MapCreate; hub.Systems.MoveGenerator = live.MoveGenerator; hub.Systems.AttackGenerator = live.AttackGenerator;
        hub.Systems.UnitSetting = fixture.Units; hub.Systems.FactionState = fixture.State; hub.Systems.CrystalSystem = fixture.Crystals;
        hub.Systems.BuildSystem = fixture.Builder; hub.Systems.VisionGenerator = vision; hub.Systems.TerritorySystem = live.TerritorySystem;
        typeof(VisionGenerator).GetField("PlayerUnit", Private).SetValue(vision, fixture.Units.PlayerUnit);
        typeof(VisionGenerator).GetField("EnemyUnit", Private).SetValue(vision, fixture.Units.EnemyUnit);
        typeof(VisionGenerator).GetField("territorysystem", Private).SetValue(vision, live.TerritorySystem);
        var resources = fixture.Resources;
        resources.Wood = resources.Stone = resources.Iron = resources.MagicOre = resources.Bread = resources.Wheat = resources.Water = 0;
        resources.Citizen = 0;
        var cells = fixture.AdjacentCells();
        for (int i = 0; i < 4; i++)
        {
            var unit = fixture.Unit(); unit.name = "SoakScout" + i; unit.kind = unit.GrowthData.kind = Kind.Scout;
            unit.Level = 1; unit.direction = Direction.N; unit.transform.position = cells[0] + Vector3.forward * i;
            unit.RestoreReflectionLifeId("long-session-scout-" + i);
            identities[unit.ReflectionLifeId] = unit.name;
        }
        vision.MarkVisionDirty(); vision.VisionPoint(live.MapCreate, live.MoveGenerator, fixture.Crystals);
        AIConfig.Mode = AIMode.PureRule;
        commander = new AICommander(hub, live.MoveGenerator, live.AttackGenerator, live.BattleSystem, vision, fixture.AP,
            fixture.Units, fixture.Crystals, live.MapCreate, MajorPersonality.Combat, fixture.Builder, null, fixture.State,
            null, null, 15, 56192, Team.Enemy);
        commander.TurnThinkingBudgetMs = 100000; // identical before/after; avoids hardware-dependent deadline parity differences
        commander.SearchSliceBudgetMs = 3;
        // Legacy constructors do not seed traits from randomSeed. Control that independent input
        // on the existing personality object so this oracle compares the same board and policy.
        typeof(AIPersonality).GetProperty("Traits").SetValue(commander.Personality,
            new AIPersonality(MajorPersonality.Combat, 56192).Traits);
        reflectionConfig = ScriptableObject.CreateInstance<AIReflectionConfig>(); reflectionConfig.EnableConsoleDiary = reflectionConfig.EnableFileDiary = false;
        reflection = new AIActionReflectionSystem(Team.Enemy, reflectionConfig, persistent: false);
        reflection.BeginBattle("LongSession_FixedSeed56192", 15, "Combat");
        typeof(AICommander).GetField("reflection", Private).SetValue(commander, reflection);
        reflection.ActionCompleted += Action;
        // Sample old Console verbosity in before; default Summary/Off hot paths are measured separately after.
        var configType = typeof(AICommander).Assembly.GetType("AIPerformanceConfig");
        if (configType != null)
        {
            var config = configType.GetProperty("Active").GetValue(null); var field = configType.GetField("LogLevel");
            logHolder = new AILogLevelHolder { Field = field, Config = config, Original = field.GetValue(config) };
            field.SetValue(config, Enum.Parse(field.FieldType, "Off"));
        }
        Application.logMessageReceived += Log; initialized = true;
        frameAllocation = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        calibrationBuffer = new byte[65536]; calibrationBuffer[0] = 1;
        Debug.Log("[LongSessionSoak] START label=" + label + " ownTurns=" + Turns + " seed=56192 actualCommander=True fixture=fixed_workload capabilityUnchanged=True");
    }
    static void StartTurn()
    {
        ownTurn++; hub.Context.Turn = ownTurn; fixture.State.GetNation(Team.Enemy).TurnsAlive = ownTurn;
        fixture.State.SetAP(Team.Enemy, 6); cpu = 0; allocated = 0;
        foreach (var unit in fixture.Units.EnemyUnit.GetComponentsInChildren<Status>())
        { unit.HasMovedThisTurn = false; }
        steps = commander.ExecuteTurnSteps();
    }
    static void Action(AIActionRecord record)
    {
        actions++;
        string actor = identities.TryGetValue(record.ActorLifeId ?? "", out string id) ? id : record.ActorRole;
        trace.Append(ownTurn).Append('|').Append(record.ActionType).Append('|').Append(actor).Append('|')
            .Append(record.TargetLifeId != null && identities.TryGetValue(record.TargetLifeId, out id) ? id : "none")
            .Append('|').Append(record.TargetCategory).Append('|').Append(record.Before?.TargetX ?? 0).Append(',')
            .Append(record.Before?.TargetY ?? 0).Append('|').Append(record.ExecutionSucceeded).AppendLine();
    }
    static void Record()
    {
        ms[ownTurn - 1] = cpu; bytes[ownTurn - 1] = allocated;
        managed[ownTurn - 1] = Profiler.GetMonoUsedSizeLong(); native[ownTurn - 1] = Math.Max(0, Profiler.GetTotalAllocatedMemoryLong() - managed[ownTurn - 1]);
        var positions = (Dictionary<Status, List<Vector3Int>>)typeof(AICommander).GetField("_unitPositionHistory", Private).GetValue(commander);
        int entries = 0; foreach (var list in positions.Values) entries += list.Count;
        var memory = (Dictionary<int, AIBoardState.LastKnownInfo>)typeof(AICommander).GetField("_sharedMemory", Private).GetValue(commander);
        // The bounded battle DTO copy is intentionally outside measured AI work.
        var state = reflection.CaptureBattleState();
        int completed = commander.Operations?.State.CompletedOperations.Count ?? 0;
        csv.Append(ownTurn).Append(',').Append(cpu.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(allocated)
            .Append(',').Append(managed[ownTurn - 1]).Append(',').Append(native[ownTurn - 1]).Append(',').Append(positions.Count)
            .Append(',').Append(entries).Append(',').Append(memory.Count).Append(',').Append(state.RecentRecords.Count).Append(',').Append(completed).Append(',').Append(logCount).AppendLine();
        if (positions.Count > 4 || entries > 16 || state.RecentRecords.Count > Mathf.Clamp(reflectionConfig.MaxRecentRecords, 16, 1024) || completed > AIOperationConfig.Active.CompletedLimit)
            throw new InvalidOperationException("Collection cap exceeded");
        if (ownTurn % 50 == 0) Debug.Log($"[LongSessionSoak] PROGRESS label={label} ownTurn={ownTurn} actions={actions} cpuMs={cpu:F3} alloc={allocated} managed={managed[ownTurn - 1]} native={native[ownTurn - 1]} logs={logCount}");
    }
    static void Log(string text, string stack, LogType type) { if (!text.StartsWith("[LongSessionSoak]", StringComparison.Ordinal)) logCount++; }
    static double Mean(double[] source, int start, int count) { double sum = 0; for (int i = start; i < start + count; i++) sum += source[i]; return sum / count; }
    static double Mean(long[] source, int start, int count) { double sum = 0; for (int i = start; i < start + count; i++) sum += source[i]; return sum / count; }
    static void Finish()
    {
        string root = "C:/Users/pethi/Documents/Codex/2026-09-19/ko/work";
        File.WriteAllText(Path.Combine(root, "long-session-" + label + ".csv"), csv.ToString());
        File.WriteAllText(Path.Combine(root, "long-session-" + label + "-trace.txt"), trace.ToString());
        double early = Mean(ms, 50, 50), late = Mean(ms, Turns - 50, 50), ratio = late / Math.Max(.001, early);
        Debug.Log($"[LongSessionSoak] COMPLETED label={label} ownTurns={ownTurn} actions={actions} earlyMeanMs={early:F4} lateMeanMs={late:F4} ratio={ratio:F4} earlyAlloc={Mean(bytes, 50, 50):F0} lateAlloc={Mean(bytes, Turns - 50, 50):F0} earlyManaged={Mean(managed, 50, 50):F0} lateManaged={Mean(managed, Turns - 50, 50):F0} earlyNative={Mean(native, 50, 50):F0} lateNative={Mean(native, Turns - 50, 50):F0} logs={logCount}");
        var counters = typeof(AICommander).Assembly.GetType("AIPerformanceCounters");
        var stageType = typeof(AICommander).Assembly.GetType("AIPerformanceStage");
        if (counters != null && stageType != null)
        {
            var summary = counters.GetMethod("Summary");
            foreach (var name in Enum.GetNames(stageType))
            {
                if (name == "Count") continue;
                object value = summary.Invoke(null, new[] { Enum.Parse(stageType, name) }); var type = value.GetType();
                Debug.Log("[LongSessionStage] " + name + " count=" + type.GetField("Count").GetValue(value)
                    + " meanMs=" + type.GetField("AverageMilliseconds").GetValue(value) + " p95Ms=" + type.GetField("P95Milliseconds").GetValue(value)
                    + " maxMs=" + type.GetField("MaximumMilliseconds").GetValue(value));
            }
        }
        Cleanup(); SessionState.SetBool(Running, false); EditorApplication.Exit(0);
    }
    static void Cleanup()
    {
        frameAllocation.Dispose(); Profiler.enabled = originalProfiler;
        Application.logMessageReceived -= Log; if (reflection != null) reflection.ActionCompleted -= Action;
        (steps as IDisposable)?.Dispose(); steps = null;
        if (logHolder != null) logHolder.Field.SetValue(logHolder.Config, logHolder.Original);
        fixture?.Dispose(); fixture = null;
        if (extra != null) UnityEngine.Object.DestroyImmediate(extra);
        if (reflectionConfig != null) UnityEngine.Object.DestroyImmediate(reflectionConfig);
    }
    static void Fail(Exception e)
    { Debug.LogError("[LongSessionSoak] FAIL " + e); Cleanup(); SessionState.SetBool(Running, false); EditorApplication.Exit(1); }
}
#endif
