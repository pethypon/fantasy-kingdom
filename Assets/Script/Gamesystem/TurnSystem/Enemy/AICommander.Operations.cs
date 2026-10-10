using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Optional operation boundary. Failure here cannot stop legal actions, reflection or turn completion.</summary>
public partial class AICommander
{
    AIOperationManager operations;
    AIOperationPersistence operationRepository;
    AIActionReflectionSystem operationReflectionSource;
    AIActionFeatureExtractor operationFallbackExtractor;
    AIActionRecord operationFallbackRecord;
    AIAction operationPendingAction;
    readonly HashSet<string> operationPendingKills = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> operationPendingFormationLifeIds = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<string> operationPendingArtifacts = new HashSet<string>(StringComparer.Ordinal);
    int operationPendingFormationKills, operationPendingDamageDealt, operationPendingDamageTaken;
    long operationFallbackActionId = 1L << 40;
    bool operationUnavailable, operationWarningLogged, operationTurnStarted, operationActionOpen;
    string requestedOperationEnd;
    int currentOperationOwnTurn;
    string operationDiaryText, operationDiaryPath;

    public AIOperationManager Operations => operations;
    public string OperationProfilePath => operationRepository?.ProfilePath;

    AIOperationManager EnsureOperations()
    {
        if (operationUnavailable) return null;
        try
        {
            var config = AIOperationConfig.Active;
            if (!config.Enabled || _threatLevel.Level < Math.Max(1, config.MinimumThreatLevel)) return null;
            if (operations == null)
            {
                string profileId = _actorTeam == Team.Enemy ? "EnemyFaction_Default" : "PlayerDeveloper";
                operationRepository = new AIOperationPersistence(reflection?.StorageDirectory, profileId, config);
                var profile = CanLearn && config.EnablePersistentLearning ? operationRepository.Load()
                    : new AIOperationLearningProfile { ProfileId = profileId };
                operations = new AIOperationManager(config, profile);
                operations.BeginBattle(_actorTeam, reflection?.BattleId ?? "FK_Operation_" + Guid.NewGuid().ToString("N"));
            }
            AttachOperationObserver();
            return operations;
        }
        catch (Exception error) { OperationFailed(error); return null; }
    }

    void AttachOperationObserver()
    {
        if (operationReflectionSource == reflection) return;
        if (operationReflectionSource != null)
        {
            operationReflectionSource.ActionCompleted -= ObserveOperationAction;
            operationReflectionSource.BeforeDiary -= FlushOperationDiary;
        }
        operationReflectionSource = reflection;
        if (reflection != null)
        {
            reflection.ActionCompleted += ObserveOperationAction;
            reflection.BeforeDiary += FlushOperationDiary;
        }
    }
    void OperationFailed(Exception error)
    {
        operationUnavailable = true; operationActionOpen = false; operationFallbackRecord = null;
        if (operationWarningLogged) return;
        operationWarningLogged = true;
        Debug.LogWarning("[AI作戦] 作戦の記録を停止し、通常のAI処理を続行します: " + error.Message);
    }

    void BeginOperationTurn()
    {
        var manager = EnsureOperations();
        if (manager == null || _board == null || manager.State.Ended) return;
        try
        {
            if (manager.State.LastStartedGlobalTurn == _turnCount) return;
            currentOperationOwnTurn = manager.State.OwnTurns + 1;
            manager.State.LastStartedGlobalTurn = _turnCount;
            _advanced.Plans.Update(_board);
            manager.BeginTurn(_board, _currentStrategy, currentOperationOwnTurn, _advanced.Plans.Current,
                _personality, _playerModel?.Snapshot);
            operationTurnStarted = true;
        }
        catch (Exception error) { OperationFailed(error); }
    }
    void RefreshOperationPlanning()
    {
        if (operations == null || operationUnavailable || !operationTurnStarted) return;
        try { operations.Refresh(_board, _currentStrategy, _advanced.Plans.Current); }
        catch (Exception error) { OperationFailed(error); }
    }
    float OperationActionBonus(AIAction action)
    {
        if (operations == null || operationUnavailable || !AIOperationConfig.Active.Enabled) return 0;
        try { return operations.ActionBonus(action, _board); }
        catch (Exception error) { OperationFailed(error); return 0; }
    }
    void EndOperationTurn()
    {
        if (!operationTurnStarted || operationUnavailable || operations == null) return;
        operationTurnStarted = false;
        try
        {
            operations.EndTurn(_board, currentOperationOwnTurn);
            DrainOperationCredits();
            if ((reflection == null || reflectionUnavailable || !reflection.Enabled)
                && operations.State.OwnTurns % Math.Max(1, AIReflectionConfig.Active.DiaryIntervalOwnTurns) == 0)
                FlushOperationDiary("INTERVAL");
        }
        catch (Exception error) { OperationFailed(error); }
    }

    void BeginOperationAction(AIAction action, AIActionReflectionSystem source, AIActionToken token)
    {
        if (operations == null || operationUnavailable || !operationTurnStarted) return;
        try
        {
            AttachOperationObserver();
            operationPendingKills.Clear(); operationPendingArtifacts.Clear(); operationPendingFormationLifeIds.Clear();
            operationPendingFormationKills = operationPendingDamageDealt = operationPendingDamageTaken = 0;
            operationActionOpen = true; operationPendingAction = action;
            var record = source?.GetPendingRecord(token);
            if (record == null)
            {
                operationFallbackExtractor ??= new AIActionFeatureExtractor();
                var before = operationFallbackExtractor.Capture(action, _board, _turnCount, _currentStrategy, _threatLevel.Level, 0);
                record = new AIActionRecord { ActionId = ++operationFallbackActionId, BattleId = operations.State.BattleId,
                    Turn = _turnCount, ActionType = action.ActionType, ActorRole = before.ActorRole,
                    TargetCategory = before.TargetCategory, Before = before,
                    ActorLifeId = action.Unit != null ? action.Unit.ReflectionLifeId : null,
                    TargetLifeId = before.KnownTargetLifeId,
                    BaseScore = selectedBaseScore, FinalScore = selectedFinalScore,
                    ContextKey = AIActionFeatureExtractor.ContextKey(before), ActionKey = AIActionFeatureExtractor.ActionKey(action, before) };
                operationFallbackRecord = record;
            }
            // Retain the before snapshot if reflection fails before publishing its completed record.
            // The normal callback clears this reference, so successful reflection is never observed twice.
            operationFallbackRecord = record;
            operations.TagAction(record, action);
        }
        catch (Exception error) { OperationFailed(error); }
    }
    void CompleteOperationAction(bool success)
    {
        if (!operationActionOpen || operationUnavailable || operations == null) return;
        if (operationFallbackRecord == null) { operationActionOpen = false; FinishRequestedOperationBattle(); return; }
        try
        {
            var record = operationFallbackRecord; operationFallbackRecord = null;
            // Exceptional fallback must not overwrite an action record that reflection may already have learned.
            if (reflectionUnavailable) record = JsonUtility.FromJson<AIActionRecord>(JsonUtility.ToJson(record));
            operationFallbackExtractor ??= new AIActionFeatureExtractor();
            record.After = operationFallbackExtractor.Capture(operationPendingAction, _board, _turnCount, _currentStrategy, _threatLevel.Level, 0);
            record.ExecutionSucceeded = success; record.Outcome = AIActionOutcome.Difference(record.Before, record.After);
            record.Outcome.KilledLifeIds.AddRange(operationPendingKills);
            record.Outcome.ArtifactEventIds.AddRange(operationPendingArtifacts);
            record.Outcome.EnemyKills = operationPendingKills.Count;
            record.Outcome.FormationKills = operationPendingFormationKills;
            record.Outcome.ArtifactsAcquired = operationPendingArtifacts.Count;
            record.Outcome.DamageDealt = Math.Max(record.Outcome.DamageDealt, operationPendingDamageDealt);
            record.Outcome.DamageTaken = Math.Max(record.Outcome.DamageTaken, operationPendingDamageTaken);
            record.MeaningfulProgress = AIFailureAnalyzer.HasMeaningfulProgress(record);
            record.FailureReason = AIFailureAnalyzer.Analyze(record);
            record.Reward = AIActionRewardEvaluator.Evaluate(record, AIReflectionConfig.Active);
            ObserveOperationAction(record);
        }
        catch (Exception error) { OperationFailed(error); }
    }
    void ObserveOperationAction(AIActionRecord record)
    {
        if (operations == null || operationUnavailable) return;
        try
        {
            operations.ObserveAction(record, _board);
            operationActionOpen = false; operationFallbackRecord = null;
            DrainOperationCredits();
            FinishRequestedOperationBattle();
        }
        catch (Exception error) { OperationFailed(error); }
    }
    void DrainOperationCredits()
    {
        foreach (var credit in operations.DrainCredits())
            if (reflection != null && !reflectionUnavailable)
                reflection.ApplyOperationCredit(credit.OperationId, credit.ActionId, credit.Delta,
                    credit.Contribution, AIOperationConfig.Active.MaxOperationCreditPerAction);
    }

    void RequestOperationBattleEnd(string result)
    {
        if (operations == null || operationUnavailable || operations.State.Ended) return;
        requestedOperationEnd = result;
        FinishRequestedOperationBattle();
    }
    void FinishRequestedOperationBattle()
    {
        if (requestedOperationEnd == null || operationActionOpen || operations == null || operationUnavailable) return;
        try
        {
            string result = requestedOperationEnd; requestedOperationEnd = null;
            operations.EndBattle(_board, result);
            operationTurnStarted = false;
            DrainOperationCredits();
            if (reflection == null || reflectionUnavailable || !reflection.Enabled) FlushOperationDiary(result);
        }
        catch (Exception error) { OperationFailed(error); }
    }

    void ObserveOperationDamage(int damage, bool dealt, string killedLifeId, bool formation)
    {
        if (operations == null || operationUnavailable) return;
        try
        {
            if (operationActionOpen)
            {
                if (dealt) operationPendingDamageDealt = (int)Math.Min(int.MaxValue, (long)operationPendingDamageDealt + Math.Max(0, damage));
                else operationPendingDamageTaken = (int)Math.Min(int.MaxValue, (long)operationPendingDamageTaken + Math.Max(0, damage));
                if (dealt && killedLifeId != null)
                {
                    operationPendingKills.Add(killedLifeId);
                    if (formation && operationPendingFormationLifeIds.Add(killedLifeId)) operationPendingFormationKills++;
                }
            }
            else if (dealt && killedLifeId != null) operations.NotifyTargetDestroyed(killedLifeId);
        }
        catch (Exception error) { OperationFailed(error); }
    }
    void ObserveOperationArtifact(string eventId)
    {
        if (operations == null || operationUnavailable || string.IsNullOrEmpty(eventId)) return;
        try
        {
            if (operationActionOpen) operationPendingArtifacts.Add(eventId);
            else operations.NotifyArtifactAcquired(eventId);
        }
        catch (Exception error) { OperationFailed(error); }
    }
    void FlushOperationDiary(string result)
    {
        if (operations == null || operationUnavailable) return;
        try
        {
            var snapshot = AIOperationConfig.Active.Enabled ? operations.Snapshot(false) : null;
            reflection?.SetOperationDiary(snapshot);
            if (snapshot != null && (reflection == null || reflectionUnavailable || !reflection.Enabled))
            {
                var text = new StringBuilder(2048);
                AIOperationDiary.Append(text, snapshot, AIOperationConfig.Active);
                operationDiaryText = text.ToString();
                if (CanLearn && AIReflectionConfig.Active.EnableFileDiary)
                {
                    string directory = Path.Combine(reflection?.StorageDirectory
                        ?? Path.Combine(Application.persistentDataPath, "FantasyKingdom", "AI"), "Diary");
                    Directory.CreateDirectory(directory);
                    operationDiaryPath = Path.Combine(directory, "AI_OperationDiary_" + _actorTeam + "_"
                        + AIReflectionSaveRepository.SafeFileName(snapshot.BattleId) + "_Turn" + snapshot.OwnTurns + ".txt");
                    File.WriteAllText(operationDiaryPath, operationDiaryText, new UTF8Encoding(false));
                }
            }
            if (CanLearn && AIOperationConfig.Active.EnablePersistentLearning)
                operationRepository.Save(operations.Profile);
        }
        catch (Exception error) { OperationFailed(error); }
    }
    public AIOperationBattleState CaptureOperationState()
    {
        if (operations == null || operationUnavailable) return null;
        try { return operations.Snapshot(); }
        catch (Exception error) { OperationFailed(error); return null; }
    }
    public void RestoreOperationState(AIOperationBattleState saved)
    {
        if (saved == null) return;
        var manager = EnsureOperations(); if (manager == null) return;
        try
        {
            if (saved.Faction != _actorTeam) return;
            manager.Restore(saved);
            currentOperationOwnTurn = manager.State.LastStartedOwnTurn;
            operationTurnStarted = !manager.State.Ended && manager.State.LastStartedOwnTurn > manager.State.LastEndedOwnTurn;
            foreach (var plan in manager.State.ActiveOperations)
                foreach (long id in plan.ActionIds) operationFallbackActionId = Math.Max(operationFallbackActionId, id);
        }
        catch (Exception error) { OperationFailed(error); }
    }
}
