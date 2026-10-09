using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Observes finalized actions without replacing the AI. Mutable event ledgers live only in a battle;
/// cross-battle experience uses role/context buckets. Disk writes occur only at diary/battle boundaries.
/// </summary>
public sealed partial class AIActionReflectionSystem
{
    sealed class PendingAction
    {
        public AIAction Action;
        public AIActionRecord Record;
        public int Kills, FormationKills, Artifacts;
        public int DamageDealt, DamageTaken;
    }
    readonly AIReflectionConfig config;
    readonly AIReflectionSaveRepository repository;
    readonly AIDiaryWriter diary;
    readonly AIActionFeatureExtractor extractor;
    readonly AIRepeatActionDetector repeat = new AIRepeatActionDetector();
    readonly Dictionary<long, PendingAction> pending = new Dictionary<long, PendingAction>();
    readonly Dictionary<long, AIActionRecord> completed = new Dictionary<long, AIActionRecord>();
    readonly Dictionary<string, AIReflectionRewardEvent> kills = new Dictionary<string, AIReflectionRewardEvent>();
    readonly HashSet<string> artifacts = new HashSet<string>();
    readonly HashSet<int> stableTurns = new HashSet<int>();
    AIReflectionBattleState state;
    bool persistenceAllowed, endRequested;
    string pendingResult;
    AIActionContextSnapshot requestedEndSnapshot;
    TurnStrategy strategy;
    long latestToken;

    public Team Faction { get; }
    public AIActionLearningProfile Profile { get; private set; }
    public AIActionRecord LastRecord { get; private set; }
    public float BattleReward => state?.BattleReward ?? 0;
    public float TurnReward => state?.TurnReward ?? 0;
    public int DiaryCount => state?.DiaryCount ?? 0;
    public string BattleId => state?.BattleId;
    public bool HasPendingActions => pending.Count > 0;
    public bool Enabled => config.EnableReflection;
    public string LatestDiaryText => diary.LastText;
    public string LastDiaryPath => diary.LastPath;
    public string ProfilePath => repository.ProfilePath;
    public string StorageDirectory { get; }

    public AIActionReflectionSystem(Team team, AIReflectionConfig config = null, string storageRoot = null, bool persistent = true)
    {
        Faction = team;
        this.config = config != null ? config : AIReflectionConfig.Active;
        extractor = new AIActionFeatureExtractor(this.config);
        economyRewards = new AIEconomyRewardTracker(this.config);
        delayedCredits = new AIDelayedCreditAssigner(this.config);
        armyUtilization = new AIArmyUtilizationAnalyzer(this.config);
        // Developer player control always starts in an isolated memory scope.
        persistenceAllowed = persistent && team == Team.Enemy;
        StorageDirectory = storageRoot ?? Path.Combine(Application.persistentDataPath, "FantasyKingdom", "AI");
        string profileId = team == Team.Enemy ? "EnemyFaction_Default" : "PlayerDeveloper";
        repository = new AIReflectionSaveRepository(StorageDirectory, profileId, this.config);
        diary = new AIDiaryWriter(StorageDirectory, this.config, persistenceAllowed);
        Profile = persistenceAllowed && this.config.EnableReflection ? repository.Load()
            : new AIActionLearningProfile { ProfileId = profileId };
    }
    public void SetPersistenceAllowed(bool allowed)
    {
        persistenceAllowed &= allowed;
        diary.SetPersistenceAllowed(persistenceAllowed);
        if (state != null) state.PersistentLearningAllowed = persistenceAllowed;
    }
    public void BeginBattle(string battleId = null, int threatLevel = 1, string personality = "", AIReflectionBattleState resume = null)
    {
        pending.Clear(); completed.Clear(); kills.Clear(); artifacts.Clear(); stableTurns.Clear();
        repeat.Clear(); LastRecord = null; latestToken = 0; endRequested = false; requestedEndSnapshot = null;
        var restored = resume == null ? null : Clone(resume);
        if (restored != null && AIReflectionMigration.UpgradeBattle(restored, config) && ValidResume(restored))
        {
            state = restored;
            SetPersistenceAllowed(state.PersistentLearningAllowed);
            if (state.BattleSummary == null) state.BattleSummary = new AIReflectionInterval();
            if (state.LearningProfile != null && repository.IsValid(state.LearningProfile)
                && state.LearningProfile.Sequence > Profile.Sequence) Profile = state.LearningProfile;
            state.LearningProfile = null;
            if (Enum.TryParse(state.LastSnapshot?.Strategy, out TurnStrategy restoredStrategy)) strategy = restoredStrategy;
            foreach (var item in state.Kills) if (item != null && !string.IsNullOrEmpty(item.Id)) kills[item.Id] = item;
            foreach (var item in state.Artifacts) if (item != null && !string.IsNullOrEmpty(item.Id)) artifacts.Add(item.Id);
            foreach (int turn in state.StableTurns) stableTurns.Add(turn);
            foreach (var record in state.RecentRecords)
                if (record != null) { completed[record.ActionId] = record; LastRecord = record; }
            repeat.Restore(state.RecentRecords);
        }
        else state = new AIReflectionBattleState { BattleId = string.IsNullOrEmpty(battleId)
            ? "FK_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Faction + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)
            : battleId, Faction = Faction, ThreatLevel = Mathf.Clamp(threatLevel, 1, 100), Personality = personality ?? "",
            PersistentLearningAllowed = persistenceAllowed };
        RestoreEvaluationState();
    }
    public void BeginTurn(int turnNumber, AIBoardState board = null, TurnStrategy strategy = TurnStrategy.Balanced, int threatLevel = 1)
    {
        if (!Enabled) return;
        EnsureBattle();
        if (state.Ended || state.LastStartedTurn == turnNumber) return;
        state.LastStartedTurn = turnNumber; state.ThreatLevel = Mathf.Clamp(threatLevel, 1, 100);
        this.strategy = strategy; state.TurnReward = 0;
        int threatBand = state.ThreatLevel <= 9 ? 0 : state.ThreatLevel <= 30 ? 1 : 2;
        AIReflectionStrategyUse usage = null;
        foreach (var item in state.StrategyUse)
            if (item.Strategy == strategy && item.ThreatBand == threatBand) { usage = item; break; }
        if (usage == null && state.StrategyUse.Count < 32)
        {
            usage = new AIReflectionStrategyUse { Strategy = strategy, ThreatBand = threatBand };
            state.StrategyUse.Add(usage);
        }
        if (usage != null) usage.OwnTurns = Math.Min(1000000, usage.OwnTurns + 1);
        state.LastSnapshot = Capture(null, board);
        SeedObservedContacts(state.LastSnapshot);
        economyRewards.Observe(state.LastSnapshot, state.OwnTurns + 1);
        if (state.Interval.FirstOwnTurn < 0) state.Interval.FirstOwnTurn = state.OwnTurns + 1;
        if (state.BattleSummary.FirstOwnTurn < 0) state.BattleSummary.FirstOwnTurn = state.OwnTurns + 1;
        if (state.Interval.Before == null) state.Interval.Before = state.LastSnapshot;
        if (state.BattleSummary.Before == null) state.BattleSummary.Before = state.LastSnapshot;
    }
    public AIActionToken BeginAction(AIAction action, AIBoardState board)
        => Enabled ? BeginAction(action, Capture(action, board)) : default;
    public AIActionToken BeginAction(AIAction action, AIActionContextSnapshot before)
    {
        if (!Enabled || action == null || before == null) return default;
        EnsureBattle();
        if (state.Ended || pending.Count >= 16) return default;
        long id = ++state.NextActionId;
        before = before.Copy(); before.ActionType = action.ActionType;
        SeedObservedContacts(before);
        economyRewards.Observe(before, state.OwnTurns + 1);
        string context = AIActionFeatureExtractor.ContextKey(before);
        string actionKey = AIActionFeatureExtractor.ActionKey(action, before);
        float modifier = GetLearnedModifier(action, before);
        var record = new AIActionRecord { ActionId = id, BattleId = state.BattleId, Turn = before.Turn,
            ActionType = action.ActionType, ActorRole = before.ActorRole, TargetCategory = before.TargetCategory,
            Before = before, ContextKey = context, ActionKey = actionKey,
            BaseScore = action.Score, LearnedModifier = modifier, FinalScore = action.Score + modifier,
            ActorRuntimeId = action.Unit != null ? action.Unit.GetInstanceID() : 0,
            ActorLifeId = action.Unit != null ? action.Unit.ReflectionLifeId : null,
            TargetLifeId = before.KnownTargetLifeId };
        record.DecisionTrace = AIReflectionDecisionTrace.Build(action, record);
        pending[id] = new PendingAction { Action = action, Record = record };
        latestToken = id;
        return new AIActionToken(id);
    }
    public void SetSelectedScores(AIActionToken token, float baseScore, float learnedModifier, float finalScore)
    {
        if (!pending.TryGetValue(token.Id, out var action)) return;
        action.Record.BaseScore = AIReflectionConfig.Finite(baseScore);
        action.Record.LearnedModifier = AIReflectionConfig.Finite(learnedModifier);
        action.Record.FinalScore = AIReflectionConfig.Finite(finalScore);
        if (action.Record.DecisionTrace != null)
        {
            action.Record.DecisionTrace.BaseScore = action.Record.BaseScore;
            action.Record.DecisionTrace.LearnedModifier = action.Record.LearnedModifier;
            action.Record.DecisionTrace.FinalScore = action.Record.FinalScore;
        }
    }
    public void CompleteAction(AIActionToken token, bool success, AIBoardState board, string executionFailure = null)
    {
        if (!Enabled || !pending.TryGetValue(token.Id, out var action)) return;
        CompleteAction(token, success, Capture(action.Action, board), executionFailure);
    }
    public void CompleteAction(AIActionToken token, bool success, AIActionContextSnapshot after, string executionFailure = null)
    {
        if (!Enabled || after == null || !pending.TryGetValue(token.Id, out var action)) return;
        pending.Remove(token.Id);
        var record = action.Record;
        record.After = after.Copy(); record.ExecutionSucceeded = success;
        record.Outcome = AIActionOutcome.Difference(record.Before, record.After);
        record.Outcome.EnemyKills = action.Kills; record.Outcome.FormationKills = action.FormationKills;
        record.Outcome.ArtifactsAcquired = action.Artifacts;
        record.Outcome.DamageDealt = Math.Max(record.Outcome.DamageDealt, action.DamageDealt);
        record.Outcome.DamageTaken = Math.Max(record.Outcome.DamageTaken, action.DamageTaken);
        FilterNewlyObservedEnemies(record);
        record.MeaningfulProgress = AIFailureAnalyzer.HasMeaningfulProgress(record, config);
        repeat.Evaluate(record);
        record.FailureReason = AIFailureAnalyzer.Analyze(record, executionFailure, config);
        record.StrategicOutcome = record.FailureReason != AIFailureReason.None ? AIStrategicOutcome.Failure
            : record.MeaningfulProgress ? AIStrategicOutcome.Progress : AIStrategicOutcome.Neutral;
        record.Reward = AIActionRewardEvaluator.Evaluate(record, config);
        economyRewards.ObserveAction(record, state.OwnTurns + 1);
        float change = Profile.Learn(record, config);
        record.LearningValueChange = change;
        AIReflectionDecisionTrace.UpdateOutcome(record);
        state.BattleReward += record.Reward; state.TotalOwnLosses += record.Outcome.OwnLosses;
        state.Interval.Observe(record, change, config.EntryLimit);
        state.BattleSummary.Observe(record, change, config.EntryLimit);
        state.LastSnapshot = record.After;
        Remember(record);
        ApplyOutcomeCredits(record);
        delayedCredits.Remember(record);
        LogReward(record);
        if (pending.Count == 0) latestToken = 0;
        if (endRequested && pending.Count == 0) FinalizeBattle(pendingResult, record.After);
    }
    public float GetLearnedModifier(AIAction action, AIBoardState board)
    {
        if (!Enabled || !config.ApplyLearningToSelection || action == null || Profile.Entries.Count == 0) return 0;
        extractor.GetSelectionKeys(action, board, state?.LastStartedTurn ?? board?.TurnCount ?? 0, strategy,
            state?.ThreatLevel ?? board?.ReconThreatLevel ?? 1, state?.TotalArtifacts ?? 0, out var context, out var key);
        return LearnedModifier(action, context, key);
    }
    public float GetLearnedModifier(AIAction action, AIActionContextSnapshot context)
    {
        if (!Enabled || !config.ApplyLearningToSelection || action == null || context == null) return 0;
        return LearnedModifier(action, AIActionFeatureExtractor.ContextKey(context), AIActionFeatureExtractor.ActionKey(action, context));
    }
    float LearnedModifier(AIAction action, string context, string key)
    {
        var entry = Profile.Find(context, key);
        float modifier = Profile.GetModifier(context, key, config);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (config.EnableCandidateDebug)
            Debug.Log("[AI自己評価候補] priority=" + action.StrategicPriority + " base=" + action.Score.ToString("F2")
                + " learned=" + modifier.ToString("F2") + " final=" + (action.Score + modifier).ToString("F2")
                + " samples=" + (entry != null ? entry.Samples.ToString() : "旧形式を含めて参照") + " context=" + context);
#endif
        return modifier;
    }
    public void OnDamageDealt(int damage, AIActionToken token = default)
    {
        if (Enabled && pending.TryGetValue(token.IsValid ? token.Id : latestToken, out var action))
            action.DamageDealt = (int)Math.Min(int.MaxValue, (long)action.DamageDealt + Math.Max(0, damage));
    }
    public void OnDamageTaken(int damage, AIActionToken token = default)
    {
        if (Enabled && pending.TryGetValue(token.IsValid ? token.Id : latestToken, out var action))
            action.DamageTaken = (int)Math.Min(int.MaxValue, (long)action.DamageTaken + Math.Max(0, damage));
    }
    public void OnEnemyKilled(int killedUnitId, bool formation = false, AIActionToken token = default)
        => OnEnemyKilled(killedUnitId.ToString(), formation, token);
    public void OnEnemyKilled(string killEventId, bool formation = false, AIActionToken token = default)
    {
        if (!Enabled || string.IsNullOrEmpty(killEventId) || killEventId.Length > 128) return;
        EnsureBattle(); if (state.Ended) return;
        string id = killEventId;
        if (kills.TryGetValue(id, out var existing))
        {
            if (!formation || existing.Formation) return;
            existing.Formation = true; state.TotalFormationKills++;
            if (pending.TryGetValue(existing.ActionId, out var open)) open.FormationKills++;
            else if (completed.TryGetValue(existing.ActionId, out var record))
            {
                UpgradeFormationReward(record);
            }
            else
            {
                state.BattleReward++; state.Interval.Reward++; state.Interval.NormalKills = Math.Max(0, state.Interval.NormalKills - 1); state.Interval.FormationKills++;
                state.BattleSummary.Reward++; state.BattleSummary.NormalKills = Math.Max(0, state.BattleSummary.NormalKills - 1); state.BattleSummary.FormationKills++;
            }
            return;
        }
        if (kills.Count >= config.EventLimit) return;
        long actionId = token.IsValid ? token.Id : latestToken;
        var item = new AIReflectionRewardEvent { Id = id, ActionId = actionId, Formation = formation };
        kills.Add(id, item); state.Kills.Add(item); state.TotalKills++;
        if (formation) state.TotalFormationKills++;
        if (pending.TryGetValue(actionId, out var current)) { current.Kills++; if (formation) current.FormationKills++; }
        else AddUnattributedReward(formation ? 2 : 1, formation ? 0 : 1, formation ? 1 : 0, 0);
    }
    public void OnArtifactAcquired(string artifactId, AIActionToken token = default)
    {
        if (!Enabled || string.IsNullOrEmpty(artifactId) || artifactId.Length > 128) return;
        EnsureBattle(); if (state.Ended || artifacts.Count >= config.EventLimit || !artifacts.Add(artifactId)) return;
        long actionId = token.IsValid ? token.Id : latestToken;
        state.Artifacts.Add(new AIReflectionRewardEvent { Id = artifactId, ActionId = actionId }); state.TotalArtifacts++;
        if (pending.TryGetValue(actionId, out var current)) current.Artifacts++;
        else AddUnattributedReward(1, 0, 0, 1);
    }
    public void EndTurn(int turnNumber, AIBoardState board)
        => EndTurn(turnNumber, board, false);
    public void EndTurn(int turnNumber, AIBoardState board, bool terminalTurn)
    {
        EnsureBattle();
        var evidence = board == null ? null : armyUtilization.Capture(board, state.RecentRecords, strategy, state.OwnTurns + 1);
        EndEvaluationTurn(turnNumber, Capture(null, board), terminalTurn, evidence);
    }
    public void EndTurn(int turnNumber, AIActionContextSnapshot snapshot)
        => EndEvaluationTurn(turnNumber, snapshot, false, null);
    public void EndTurn(int turnNumber, AIActionContextSnapshot snapshot, bool terminalTurn)
        => EndEvaluationTurn(turnNumber, snapshot, terminalTurn, null);
    public void EndBattle(bool victory, AIBoardState board) => RequestBattleEnd(victory ? "VICTORY" : "DEFEAT", Capture(null, board));
    public void EndBattle(bool victory, AIActionContextSnapshot snapshot) => RequestBattleEnd(victory ? "VICTORY" : "DEFEAT", snapshot);
    public void EndDrawBattle(AIBoardState board) => RequestBattleEnd("DRAW", Capture(null, board));
    public void EndDrawBattle(AIActionContextSnapshot snapshot) => RequestBattleEnd("DRAW", snapshot);
    void RequestBattleEnd(string result, AIActionContextSnapshot snapshot)
    {
        if (!Enabled) return;
        EnsureBattle(); if (state.Ended || endRequested) return;
        endRequested = true; pendingResult = result; requestedEndSnapshot = snapshot?.Copy();
        if (pending.Count == 0) FinalizeBattle(result, requestedEndSnapshot);
    }
    void FinalizeBattle(string result, AIActionContextSnapshot snapshot)
    {
        if (state.Ended) return;
        CountTerminalOwnTurn();
        state.Ended = true; state.Result = result; state.Victory = result == "VICTORY";
        state.LastSnapshot = snapshot ?? state.LastSnapshot; state.Interval.After = state.LastSnapshot;
        state.BattleSummary.After = state.LastSnapshot;
        float reward = result == "VICTORY" ? AIReflectionConfig.VictoryReward
            : result == "DEFEAT" ? AIReflectionConfig.Finite(config.DefeatReward) : 0;
        state.BattleReward += reward; state.Interval.Reward += reward; state.Interval.VictoryReward += reward;
        state.BattleSummary.Reward += reward; state.BattleSummary.VictoryReward += reward;
        if (!Profile.CompletedBattleIds.Contains(state.BattleId))
        {
            Profile.RecordStrategyResult(state, result, reward);
            Profile.BattlesPlayed = Math.Min(1000000, Profile.BattlesPlayed + 1);
            if (result == "VICTORY") Profile.Wins = Math.Min(1000000, Profile.Wins + 1);
            if (result == "DEFEAT") Profile.Losses = Math.Min(1000000, Profile.Losses + 1);
            Profile.CompletedBattleIds.Add(state.BattleId);
            if (Profile.CompletedBattleIds.Count > 128) Profile.CompletedBattleIds.RemoveAt(0);
        }
        OutputDiary(result);
        pending.Clear(); repeat.Clear();
    }
    void OutputDiary(string result)
    {
        bool sameTurn = state.LastDiaryOwnTurn == state.OwnTurns;
        if (sameTurn && (result == "INTERVAL" || state.LastDiaryResult != "INTERVAL")) return;
        if (sameTurn && state.LastDiaryInterval != null)
        {
            var combined = JsonUtility.FromJson<AIReflectionInterval>(JsonUtility.ToJson(state.LastDiaryInterval));
            combined.MergeFrom(state.Interval, config.EntryLimit);
            state.Interval = combined;
        }
        diary.Write(state, result);
        if (!sameTurn) state.DiaryCount++;
        state.LastDiaryOwnTurn = state.OwnTurns; state.LastDiaryResult = result;
        state.LastDiaryInterval = JsonUtility.FromJson<AIReflectionInterval>(JsonUtility.ToJson(state.Interval));
        if (persistenceAllowed) repository.Save(Profile);
        state.Interval = new AIReflectionInterval { Before = state.LastSnapshot, FirstOwnTurn = state.OwnTurns + 1 };
    }
    bool MarkStableTurn(int turn)
    {
        if (stableTurns.Contains(turn) || stableTurns.Count >= config.EventLimit) return false;
        stableTurns.Add(turn); state.StableTurns.Add(turn); state.TotalStableTurns++; state.Interval.StableTurns++;
        state.BattleSummary.StableTurns++;
        return true;
    }
    void AddUnattributedReward(float reward, int normal, int formation, int artifact)
    {
        state.BattleReward += reward; state.Interval.Reward += reward;
        state.Interval.NormalKills += normal; state.Interval.FormationKills += formation; state.Interval.Artifacts += artifact;
        state.BattleSummary.Reward += reward; state.BattleSummary.NormalKills += normal;
        state.BattleSummary.FormationKills += formation; state.BattleSummary.Artifacts += artifact;
    }
    AIActionContextSnapshot Capture(AIAction action, AIBoardState board)
        => extractor.Capture(action, board, state?.LastStartedTurn ?? board?.TurnCount ?? 0,
            strategy, state?.ThreatLevel ?? board?.ReconThreatLevel ?? 1, state?.TotalArtifacts ?? 0);
    void EnsureBattle() { if (state == null) BeginBattle(); }
    void Remember(AIActionRecord record)
    {
        completed[record.ActionId] = record; state.RecentRecords.Add(record); LastRecord = record;
        if (state.RecentRecords.Count > config.RecordLimit)
        {
            completed.Remove(state.RecentRecords[0].ActionId); state.RecentRecords.RemoveAt(0);
        }
    }
    public AIReflectionBattleState CaptureBattleState()
    {
        if (!Enabled || state == null) return null;
        CaptureEvaluationState();
        // This copy occurs only at the existing game-save boundary, never per action.
        state.LearningProfile = Profile;
        try { return Clone(state); }
        finally { state.LearningProfile = null; }
    }
    bool ValidResume(AIReflectionBattleState resume)
    {
        if (resume.SchemaVersion != 2 || resume.Faction != Faction || string.IsNullOrEmpty(resume.BattleId)
            || resume.BattleId.Length > 256 || resume.OwnTurns < 0 || resume.OwnTurns > 1000000
            || resume.NextActionId < 0 || resume.NextActionId > 1000000000000L
            || !Finite(resume.BattleReward) || !Finite(resume.TurnReward)
            || !ValidInterval(resume.Interval) || resume.BattleSummary != null && !ValidInterval(resume.BattleSummary)
            || resume.RecentRecords == null || resume.RecentRecords.Count > config.RecordLimit
            || resume.Kills == null || resume.Kills.Count > config.EventLimit
            || resume.Artifacts == null || resume.Artifacts.Count > config.EventLimit
            || resume.StableTurns == null || resume.StableTurns.Count > config.EventLimit
            || resume.StrategyUse == null || resume.StrategyUse.Count > 32) return false;
        foreach (var usage in resume.StrategyUse)
            if (usage == null || usage.OwnTurns < 0 || usage.OwnTurns > 1000000 || usage.ThreatBand < 0
                || usage.ThreatBand > 2 || !Enum.IsDefined(typeof(TurnStrategy), usage.Strategy)) return false;
        foreach (var record in resume.RecentRecords)
            if (record == null || record.Before == null || record.After == null || record.Outcome == null
                || record.ActionId <= 0 || record.ActionId > resume.NextActionId
                || !Finite(record.Reward) || !Finite(record.BaseScore) || !Finite(record.LearnedModifier)
                || string.IsNullOrEmpty(record.ContextKey) || record.ContextKey.Length > 512
                || string.IsNullOrEmpty(record.ActionKey) || record.ActionKey.Length > 256) return false;
        foreach (var reward in resume.Kills) if (!ValidEvent(reward)) return false;
        foreach (var reward in resume.Artifacts) if (!ValidEvent(reward)) return false;
        return true;
    }
    bool ValidInterval(AIReflectionInterval interval)
    {
        if (interval == null || !Finite(interval.Reward) || !Finite(interval.PenaltyReward)
            || interval.Patterns == null || interval.Patterns.Count > config.EntryLimit
            || interval.Failures == null || interval.Failures.Count > 32) return false;
        foreach (var pattern in interval.Patterns)
            if (pattern == null || pattern.Key == null || pattern.Key.Length > 800 || pattern.Samples < 0
                || !Finite(pattern.Reward) || !Finite(pattern.LearnedChange)) return false;
        foreach (var reason in interval.Failures) if (reason == null || reason.Count < 0) return false;
        return true;
    }
    static bool ValidEvent(AIReflectionRewardEvent reward) => reward != null && !string.IsNullOrEmpty(reward.Id)
        && reward.Id.Length <= 128 && reward.ActionId >= 0;
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static AIReflectionBattleState Clone(AIReflectionBattleState source)
        => JsonUtility.FromJson<AIReflectionBattleState>(JsonUtility.ToJson(source));
}
