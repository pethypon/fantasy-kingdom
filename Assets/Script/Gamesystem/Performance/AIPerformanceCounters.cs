using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

public enum AIPerformanceStage
{
    BoardSnapshot, CandidateGeneration, ActionEvaluation, Search, Selection, Execution,
    Reflection, DelayedCredit, ArmyUtilization, Operation, OperationScoring, OperationLearning,
    Diary, Persistence, Presentation, UI, Count
}

public struct AIRuntimeCounts
{
    public int SharedMemory, PositionHistoryUnits, PositionHistoryEntries, ReflectionActions, ReflectionKnowledge;
    public int OperationActions, OperationKnowledge, CompletedOperations, DelayedCredit, DiaryEntries, DiaryVisibleLines, PresentationQueue;
}
public struct AITurnPerformanceSample
{
    public Team Team;
    public int OwnTurn;
    public double CpuMilliseconds;
    public long AllocationBytes, ManagedBytes, NativeBytes, FileWrites, UIRebuilds;
    public bool AllocationAvailable;
    public AIRuntimeCounts Counts;
}
public struct AIPerformanceSummary
{
    public long Count, AllocationBytes;
    public double AverageMilliseconds, P95Milliseconds, MaximumMilliseconds;
}

/// <summary>
/// Responsibility: read-only, bounded CPU/allocation/retention diagnostics, not wall-clock AI budget.
/// Owner: runtime diagnostics. Lifetime: application, reset on subsystem registration.
/// Maximum retained items: two faction rings of at most 2,000 turns; 256 samples per stage.
/// Cleanup: rings overwrite oldest samples. Failure: disable instrumentation once; AI continues.
/// Profiler scopes must end before yielding to the next frame.
/// </summary>
public static class AIPerformanceCounters
{
    const int StageSamples = 256;
    sealed class StageData
    {
        public readonly double[] Samples = new double[StageSamples];
        public int Position, Retained;
        public long Count, Bytes;
        public double Sum, Maximum;
    }
    sealed class TurnData
    {
        public AITurnPerformanceSample[] Samples;
        public int Position, Retained;
        public bool Open;
        public AITurnPerformanceSample Current;
        public long StartedWrites, StartedUIRebuilds;
        public long StartedAllocationValue;
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static readonly ProfilerMarker[] markers = CreateMarkers();
    static readonly StageData[] stages = CreateStages();
#else
    static readonly ProfilerMarker[] markers = Array.Empty<ProfilerMarker>();
    static readonly StageData[] stages = Array.Empty<StageData>();
#endif
    static readonly TurnData[] turns = { new TurnData(), new TurnData() };
    static bool failed, warningLogged;
    static ProfilerRecorder allocationBytes;
    public static long FileWriteCount { get; private set; }
    public static long FileReadCount { get; private set; }
    public static long DiaryUIRebuildCount { get; private set; }
    public static int DiaryVisibleLines { get; private set; }
    public static int PresentationQueueCount { get; private set; }
    public static bool Enabled
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            try { return !failed && AIPerformanceConfig.Active.EnableInstrumentation; }
            catch (Exception e) { Fail(e); return false; }
#else
            return false;
#endif
        }
    }
    static ProfilerMarker[] CreateMarkers()
    {
        var result = new ProfilerMarker[(int)AIPerformanceStage.Count];
        for (int i = 0; i < result.Length; i++) result[i] = new ProfilerMarker("AI." + (AIPerformanceStage)i);
        return result;
    }
    static StageData[] CreateStages()
    {
        var result = new StageData[(int)AIPerformanceStage.Count];
        for (int i = 0; i < result.Length; i++) result[i] = new StageData();
        return result;
    }
    static int Side(Team team) => team == Team.Player ? 0 : 1;
    public static Scope Measure(AIPerformanceStage stage) => new Scope(stage, Enabled);
    public struct Scope : IDisposable
    {
        readonly int stage;
        readonly long ticks, bytes;
        readonly bool enabled;
        internal Scope(AIPerformanceStage value, bool enabled)
        {
            stage = (int)value; this.enabled = enabled && stage >= 0 && stage < stages.Length;
            ticks = 0; bytes = -1;
            if (!this.enabled) return;
            try { bytes = ReadAllocationBytes(); ticks = Stopwatch.GetTimestamp(); markers[stage].Begin(); }
            catch (Exception e) { Fail(e); this.enabled = false; }
        }
        public void Dispose()
        {
            if (!enabled) return;
            try
            {
                markers[stage].End();
                var data = stages[stage];
                double ms = (Stopwatch.GetTimestamp() - ticks) * (1000d / Stopwatch.Frequency);
                data.Samples[data.Position] = ms; data.Position = (data.Position + 1) % StageSamples;
                data.Retained = Math.Min(StageSamples, data.Retained + 1); data.Count++; data.Sum += ms;
                data.Maximum = Math.Max(data.Maximum, ms);
                long afterBytes = ReadAllocationBytes();
                if (bytes < 0 || afterBytes < bytes) data.Bytes = -1;
                else if (data.Bytes >= 0) data.Bytes += afterBytes - bytes;
            }
            catch (Exception e) { Fail(e); }
        }
    }
    /// <summary>Starts a faction-local CPU sample; does not touch AITurnBudget or game state.</summary>
    public static void BeginTurn(Team team, int ownTurn)
    {
        if (!Enabled) return;
        try
        {
            var data = turns[Side(team)];
            if (data.Samples == null) data.Samples = new AITurnPerformanceSample[AIPerformanceConfig.Active.TurnSampleLimit];
            bool canReadAllocation = ReadAllocationBytes() >= 0;
            data.Current = new AITurnPerformanceSample { Team = team, OwnTurn = ownTurn, FileWrites = FileWriteCount, UIRebuilds = DiaryUIRebuildCount, AllocationAvailable = canReadAllocation, AllocationBytes = canReadAllocation ? 0 : -1 };
            data.StartedWrites = FileWriteCount; data.StartedUIRebuilds = DiaryUIRebuildCount;
            data.Open = true;
        }
        catch (Exception e) { Fail(e); }
    }
    public static void RecordTurnSlice(Team team, double milliseconds, long allocationBytes)
    {
        if (!Enabled) return;
        var data = turns[Side(team)]; if (!data.Open) return;
        data.Current.CpuMilliseconds += Math.Max(0, milliseconds);
        if (allocationBytes < 0) { data.Current.AllocationAvailable = false; data.Current.AllocationBytes = -1; }
        else if (data.Current.AllocationAvailable) data.Current.AllocationBytes += allocationBytes;
    }
    /// <summary>Samples the byte counter at slice boundaries. This includes other-thread work during the same interval.</summary>
    public static void BeginAllocationSlice(Team team)
    {
        if (!Enabled || !Profiler.enabled) return;
        try
        {
            turns[Side(team)].StartedAllocationValue = ReadAllocationBytes();
        }
        catch (Exception e) { Fail(e); }
    }
    public static long EndAllocationSlice(Team team)
    {
        if (!Enabled || !Profiler.enabled) return -1;
        try
        {
            var data = turns[Side(team)]; long current = ReadAllocationBytes();
            return data.StartedAllocationValue < 0 || current < data.StartedAllocationValue ? -1 : current - data.StartedAllocationValue;
        }
        catch (Exception e) { Fail(e); return 0; }
    }
    static long ReadAllocationBytes()
    {
        if (!Profiler.enabled) return -1;
        if (!allocationBytes.Valid) allocationBytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 0);
        return allocationBytes.CurrentValue;
    }
    /// <summary>Finalizes counters even when an iterator is disposed early. No collection clone is made.</summary>
    public static void EndTurn(Team team, AIRuntimeCounts counts)
    {
        if (!Enabled) return;
        try
        {
            var data = turns[Side(team)]; if (!data.Open) return;
            data.Current.Counts = counts;
            data.Current.ManagedBytes = Profiler.GetMonoUsedSizeLong();
            data.Current.NativeBytes = Math.Max(0, Profiler.GetTotalAllocatedMemoryLong() - data.Current.ManagedBytes);
            data.Current.FileWrites = FileWriteCount - data.StartedWrites;
            data.Current.UIRebuilds = DiaryUIRebuildCount - data.StartedUIRebuilds;
            data.Samples[data.Position] = data.Current; data.Position = (data.Position + 1) % data.Samples.Length;
            data.Retained = Math.Min(data.Samples.Length, data.Retained + 1); data.Open = false;
        }
        catch (Exception e) { Fail(e); }
    }
    public static int RetainedTurns(Team team) => turns[Side(team)].Retained;
    /// <summary>Includes the existing owner's turn-final reflection/diary work, without moving that work into the AI iterator.</summary>
    public static void RecordCompletedTurnTail(Team team, double milliseconds, long allocationBytes, AIRuntimeCounts counts)
    {
        if (!Enabled) return;
        try
        {
            var data = turns[Side(team)]; if (data.Retained == 0 || data.Open) return;
            int i = (data.Position - 1 + data.Samples.Length) % data.Samples.Length;
            var sample = data.Samples[i]; sample.CpuMilliseconds += Math.Max(0, milliseconds);
            if (allocationBytes < 0) { sample.AllocationAvailable = false; sample.AllocationBytes = -1; }
            else if (sample.AllocationAvailable) sample.AllocationBytes += allocationBytes;
            sample.Counts = counts;
            sample.ManagedBytes = Profiler.GetMonoUsedSizeLong();
            sample.NativeBytes = Math.Max(0, Profiler.GetTotalAllocatedMemoryLong() - sample.ManagedBytes);
            sample.FileWrites = FileWriteCount - data.StartedWrites; sample.UIRebuilds = DiaryUIRebuildCount - data.StartedUIRebuilds;
            data.Samples[i] = sample;
        }
        catch (Exception e) { Fail(e); }
    }
    public static AITurnPerformanceSample GetRecentTurn(Team team, int newestOffset = 0)
    {
        var data = turns[Side(team)];
        if (newestOffset < 0 || newestOffset >= data.Retained) return default;
        return data.Samples[(data.Position - 1 - newestOffset + data.Samples.Length) % data.Samples.Length];
    }
    /// <summary>Diagnostic query only. Sorting is on a bounded copy, outside the AI hot path.</summary>
    public static AIPerformanceSummary Summary(AIPerformanceStage stage)
    {
        if ((int)stage < 0 || (int)stage >= stages.Length) return default;
        var data = stages[(int)stage]; var sample = new double[data.Retained]; Array.Copy(data.Samples, sample, sample.Length); Array.Sort(sample);
        return new AIPerformanceSummary { Count = data.Count, AllocationBytes = data.Bytes, AverageMilliseconds = data.Count == 0 ? 0 : data.Sum / data.Count,
            MaximumMilliseconds = data.Maximum, P95Milliseconds = sample.Length == 0 ? 0 : sample[Math.Min(sample.Length - 1, (int)Math.Ceiling(sample.Length * .95) - 1)] };
    }
    public static void MarkFileWrite() { if (Enabled) FileWriteCount++; }
    public static void MarkFileRead() { if (Enabled) FileReadCount++; }
    public static void MarkDiaryUIRebuild() { if (Enabled) DiaryUIRebuildCount++; }
    public static void SetDiaryVisibleLines(int lines) => DiaryVisibleLines = Math.Max(0, lines);
    public static void SetPresentationQueueCount(int count) => PresentationQueueCount = Math.Max(0, count);
    static void Fail(Exception e)
    {
        failed = true;
        if (warningLogged) return; warningLogged = true;
        UnityEngine.Debug.LogWarning("[AI計測] 計測を停止しゲームを続行します: " + e.Message);
    }
    /// <summary>Testable failure boundary; diagnostics cannot propagate into gameplay.</summary>
    public static void DisableInstrumentation(string reason) => Fail(new InvalidOperationException(reason));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        failed = warningLogged = false; FileWriteCount = FileReadCount = DiaryUIRebuildCount = 0; DiaryVisibleLines = PresentationQueueCount = 0;
        foreach (var data in stages) { data.Position = data.Retained = 0; data.Count = data.Bytes = 0; data.Sum = data.Maximum = 0; Array.Clear(data.Samples, 0, data.Samples.Length); }
        allocationBytes.Dispose(); allocationBytes = default;
        foreach (var data in turns) { data.Position = data.Retained = 0; data.Open = false; data.Current = default; if (data.Samples != null) Array.Clear(data.Samples, 0, data.Samples.Length); }
    }
}
