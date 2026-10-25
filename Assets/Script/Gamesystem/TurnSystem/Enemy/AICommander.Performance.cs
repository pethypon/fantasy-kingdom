using System;
using System.Collections;
using System.Diagnostics;

/// <summary>
/// Responsibility: observe AI CPU slices and retained data, without changing decisions or budgets.
/// Lifetime/Owner: one battle / the containing commander. Maximum retained items: scalar counters only.
/// Cleanup timing: iterator disposal; counters belong to the performance service.
/// Failure behavior: instrumentation disables itself for this commander; the AI continues.
/// </summary>
public partial class AICommander
{
    int runtimeOwnTurns;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    bool performanceUnavailable;
#endif
    bool maintenanceUnavailable;

    /// <summary>Executes the normal decision iterator while excluding cooperative frame waits from CPU/GC samples.</summary>
    public IEnumerator ExecuteTurnSteps()
    {
        var steps = ExecuteTurnCoreSteps();
        int ownTurn = ++runtimeOwnTurns;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool measured = TryBeginTurnMeasurement(ownTurn);
#endif
        try
        {
            while (true)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                long started = 0;
                if (measured)
                {
                    try
                    {
                        AIPerformanceCounters.BeginAllocationSlice(_actorTeam);
                        started = Stopwatch.GetTimestamp();
                    }
                    catch (Exception) { performanceUnavailable = true; measured = false; }
                }
#endif
                bool running;
                try { running = steps.MoveNext(); }
                finally
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (measured)
                    {
                        try
                        {
                            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
                            long bytes = AIPerformanceCounters.EndAllocationSlice(_actorTeam);
                            AIPerformanceCounters.RecordTurnSlice(_actorTeam, elapsed, bytes);
                        }
                        catch (Exception) { performanceUnavailable = true; measured = false; }
                    }
#endif
                }
                if (!running) break;
                yield return steps.Current;
            }
        }
        finally
        {
            try { (steps as IDisposable)?.Dispose(); }
            finally
            {
                MaintainRuntimeHistory(ownTurn);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (measured)
                    try { AIPerformanceCounters.EndTurn(_actorTeam, GetRuntimeCounts()); }
                    catch (Exception) { performanceUnavailable = true; }
#endif
                if (_turnGen != null && _turnGen.IsGameOver) ReleaseRuntimeReferences();
            }
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    bool TryBeginTurnMeasurement(int ownTurn)
    {
        if (performanceUnavailable) return false;
        try
        {
            if (!AIPerformanceCounters.Enabled) return false;
            AIPerformanceCounters.BeginTurn(_actorTeam, ownTurn);
            return true;
        }
        catch (Exception) { performanceUnavailable = true; return false; }
    }
#endif

    void MaintainRuntimeHistory(int ownTurn)
    {
        if (maintenanceUnavailable) return;
        try
        {
            int interval = Math.Max(1, AIPerformanceConfig.Active.MaintenanceIntervalOwnTurns);
            if (ownTurn % interval == 0) CleanupDeadUnitHistory();
        }
        catch (Exception)
        {
            // Optional housekeeping must not interrupt a legal action or prevent turn completion.
            maintenanceUnavailable = true;
        }
    }

    /// <summary>Releases transient own-unit references after battle learning/diary finalization or scene disposal.</summary>
    public void ReleaseRuntimeReferences()
    {
        ReleaseObservedMemoryProfiles();
        _governor.ReleasePreservationState();
        _unitPositionHistory.Clear();
        _actedUnits.Clear();
        turnMovementHistory.Clear();
        selectionBuffer.Clear();
        runtimeMaintenance.Clear();
    }

    /// <summary>Reads collection sizes only. Does not clone saves, load profiles, or initialize optional AI systems.</summary>
    public AIRuntimeCounts GetRuntimeCounts()
    {
        int historyEntries = 0, operationActions = 0;
        foreach (var pair in _unitPositionHistory) historyEntries += pair.Value?.Count ?? 0;
        if (operations != null)
        {
            var active = operations.ActiveOperations;
            for (int i = 0; i < active.Count; i++)
            {
                var operation = active[i];
                operationActions += operation?.ActionEvidence?.Count ?? 0;
            }
        }
        return new AIRuntimeCounts
        {
            SharedMemory = _sharedMemory.Count,
            PositionHistoryUnits = _unitPositionHistory.Count,
            PositionHistoryEntries = historyEntries,
            ReflectionActions = reflection?.ActionRecordCount ?? 0,
            ReflectionKnowledge = reflection?.KnowledgeCount ?? 0,
            DelayedCredit = reflection?.DelayedCreditCount ?? 0,
            DiaryEntries = reflection?.DiaryEntryCount ?? 0,
            OperationActions = operationActions,
            OperationKnowledge = operations?.Profile?.Entries?.Count ?? 0,
            CompletedOperations = operations?.State?.CompletedOperations?.Count ?? 0,
            DiaryVisibleLines = AIPerformanceCounters.DiaryVisibleLines,
            PresentationQueue = AIPerformanceCounters.PresentationQueueCount
        };
    }
}
