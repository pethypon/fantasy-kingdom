using System;
using UnityEngine;

/// <summary>Small integration boundary: reflection can fail without stopping legal AI execution.</summary>
public partial class AICommander
{
    AIActionReflectionSystem reflection;
    bool reflectionUnavailable, reflectionWarningLogged;
    bool reflectionTurnStarted;
    bool boardReadyAfterAction;
    float selectedBaseScore, selectedLearnedModifier, selectedFinalScore;
    public AIActionReflectionSystem Reflection => EnsureReflection();
    public string LatestDiaryText => reflection != null && !reflectionUnavailable && reflection.Enabled
        ? reflection.LatestDiaryText : operationDiaryText;
    public string LatestDiaryPath => reflection != null && !reflectionUnavailable && reflection.Enabled
        ? reflection.LastDiaryPath : operationDiaryPath;
    public string ReflectionProfilePath => reflection?.ProfilePath;
    public string ReflectionStorageDirectory => reflection?.StorageDirectory;
    string observedOperationDiary;
    long operationDiaryRevision;
    public long DiaryRevision
    {
        get
        {
            if (reflection != null && !reflectionUnavailable && reflection.Enabled) return reflection.DiaryRevision;
            if (!ReferenceEquals(observedOperationDiary, operationDiaryText))
            { observedOperationDiary = operationDiaryText; if (operationDiaryRevision < long.MaxValue) operationDiaryRevision++; }
            return operationDiaryRevision;
        }
    }

    /// <summary>Presentation reads existing memory only; opening the diary never calls EnsureReflection.</summary>
    public AIDiarySnapshot CaptureDiarySnapshot(int pageIndex = 0, int maximumEvents = 100)
        => reflection != null && !reflectionUnavailable && reflection.Enabled
            ? reflection.CaptureDiarySnapshot(pageIndex, maximumEvents)
            : new AIDiarySnapshot(DiaryRevision, _actorTeam, operationDiaryText, null, pageIndex, maximumEvents);

    AIActionReflectionSystem EnsureReflection()
    {
        if (reflectionUnavailable) return null;
        try
        {
            if (reflection == null)
            {
                var config = AIReflectionConfig.Active;
                if (!config.EnableReflection) return null;
                reflection = new AIActionReflectionSystem(_actorTeam, config, persistent: CanLearn);
                reflection.BeginBattle(threatLevel: _threatLevel.Level, personality: _personality.Major.ToString());
            }
            reflection.SetPersistenceAllowed(CanLearn);
            return reflection;
        }
        catch (Exception error) { ReflectionFailed(error); return null; }
    }
    void ReflectionFailed(Exception error)
    {
        reflectionUnavailable = true;
        if (reflectionWarningLogged) return;
        reflectionWarningLogged = true;
        Debug.LogWarning("[AI自己評価] 記録を停止し、通常のAI処理を続行します: " + error.Message);
    }
    void BeginReflectionTurn()
    {
        boardReadyAfterAction = false;
        try
        {
            var system = EnsureReflection();
            if (system == null || !system.Enabled) return;
            system.BeginTurn(_turnCount, _board, _currentStrategy, _threatLevel.Level);
            reflectionTurnStarted = true;
        }
        catch (Exception error) { ReflectionFailed(error); }
    }
    public void EndReflectionTurn()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool measured = false;
        long started = 0;
        try
        {
            measured = !performanceUnavailable && AIPerformanceCounters.Enabled
                && (reflectionTurnStarted || operationTurnStarted);
            if (measured)
            {
                AIPerformanceCounters.BeginAllocationSlice(_actorTeam);
                started = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }
        catch (Exception) { performanceUnavailable = true; measured = false; }
        try { EndReflectionTurnCore(); }
        finally
        {
            if (measured)
                try
                {
                    double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                        * 1000d / System.Diagnostics.Stopwatch.Frequency;
                    long bytes = AIPerformanceCounters.EndAllocationSlice(_actorTeam);
                    AIPerformanceCounters.RecordCompletedTurnTail(_actorTeam, elapsed, bytes, GetRuntimeCounts());
                }
                catch (Exception) { performanceUnavailable = true; }
        }
#else
        EndReflectionTurnCore();
#endif
    }
    void EndReflectionTurnCore()
    {
        bool operationRefreshedBoard = false;
        if (operationTurnStarted && !operationUnavailable)
        {
            try { _board?.Refresh(); operationRefreshedBoard = _board != null; EndOperationTurn(); }
            catch (Exception error) { OperationFailed(error); }
        }
        if (!reflectionTurnStarted || reflectionUnavailable || reflection == null) return;
        reflectionTurnStarted = false;
        try
        {
            reflection.SetPersistenceAllowed(CanLearn);
            if (!operationRefreshedBoard) _board?.Refresh();
            reflection.EndTurn(_turnCount, _board, _turnGen != null && _turnGen.IsGameOver);
        }
        catch (Exception error) { ReflectionFailed(error); }
    }
    bool ExecuteReflectedAction(AIAction action)
    {
        var system = EnsureReflection();
        AIActionToken token = default;
        if (system != null && system.Enabled) try
        {
            token = system.BeginAction(action, _board);
            system.SetSelectedScores(token, selectedBaseScore, selectedLearnedModifier, selectedFinalScore);
        }
        catch (Exception error) { ReflectionFailed(error); }
        BeginOperationAction(action, system, token);
        int generation = _board.Generation;
        bool success = false;
        try { success = _actionExecutor.Execute(action, _board); return success; }
        finally
        {
            bool reflectionPending = token.IsValid && !reflectionUnavailable;
            bool explorationActive = ExplorationAISettings.Active.Enabled;
            if (reflectionPending || explorationActive || operationActionOpen)
            {
                try
                {
                    // Build/summon already refresh. Other actions need finalized observed facts.
                    if (_board.Generation == generation) _board.Refresh();
                    boardReadyAfterAction = true;
                }
                catch (Exception error) { ReflectionFailed(error); }
            }
            if (reflectionPending && !reflectionUnavailable)
            {
                try { system.CompleteAction(token, success, _board); }
                catch (Exception error) { ReflectionFailed(error); }
            }
            CompleteOperationAction(success);
            if (explorationActive) RecordExplorationAction(action, success);
            if (_turnGen != null && _turnGen.IsGameOver) EndExplorationTurn();
        }
    }
    float LearnedModifier(AIAction action)
    {
        if (reflectionUnavailable || reflection == null || !reflection.Enabled) return 0;
        try { return reflection.GetLearnedModifier(action, _board); }
        catch (Exception error) { ReflectionFailed(error); return 0; }
    }
    public AIReflectionBattleState CaptureReflectionState()
    {
        if (reflectionUnavailable || reflection == null) return null;
        try { return reflection.CaptureBattleState(); }
        catch (Exception error) { ReflectionFailed(error); return null; }
    }
    public void RestoreReflectionState(AIReflectionBattleState saved)
    {
        if (saved == null) return;
        try
        {
            EnsureReflection()?.BeginBattle(threatLevel: _threatLevel.Level,
                personality: _personality.Major.ToString(), resume: saved);
        }
        catch (Exception error) { ReflectionFailed(error); }
    }
    public void NotifyBattleEnd(bool victory)
    {
        RequestOperationBattleEnd(victory ? "VICTORY" : "DEFEAT");
        try
        {
            var system = EnsureReflection();
            if (system == null) return;
            RefreshReflectionBoard();
            system.EndBattle(victory, _board);
        }
        catch (Exception error) { ReflectionFailed(error); }
        finally { ReleaseRuntimeReferences(); }
    }
    public void NotifyDrawBattleEnd()
    {
        RequestOperationBattleEnd("DRAW");
        try
        {
            var system = EnsureReflection();
            if (system == null) return;
            RefreshReflectionBoard();
            system.EndDrawBattle(_board);
        }
        catch (Exception error) { ReflectionFailed(error); }
        finally { ReleaseRuntimeReferences(); }
    }
    public void NotifyActualDamage(int damage, bool dealt, string killedLifeId = null, bool formation = false)
    {
        ObserveOperationDamage(damage, dealt, killedLifeId, formation);
        try
        {
            var system = EnsureReflection();
            if (system == null) return;
            if (dealt) system.OnDamageDealt(damage); else system.OnDamageTaken(damage);
            if (dealt && killedLifeId != null) system.OnEnemyKilled(killedLifeId, formation);
        }
        catch (Exception error) { ReflectionFailed(error); }
    }
    public void NotifyArtifactAcquired(string eventId)
    {
        ObserveOperationArtifact(eventId);
        try { EnsureReflection()?.OnArtifactAcquired(eventId); }
        catch (Exception error) { ReflectionFailed(error); }
    }
    void RefreshReflectionBoard()
    {
        if (_board != null) { _board.Refresh(); return; }
        if (_moveGen == null || _unitSet == null || _crystalSystem == null || _apSystem == null) return;
        _board = new AIBoardState(_moveGen, _attackPoint, _apSystem, _unitSet, _crystalSystem,
            _visionGen, _buildSystem ?? _turnGen?.Systems?.BuildSystem,
            _summonSystem ?? _turnGen?.Systems?.SummonSystem, _factionState,
            _subCrystalSystem, _turnCount, _sharedMemory, _actorTeam)
        {
            MapCreate = _mapCreate, TerritorySystem = _turnGen?.Systems?.TerritorySystem,
            Governor = _governor, ReconThreatLevel = _threatLevel.Level
        };
    }
}
