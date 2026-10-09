#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Reflection regressions use value snapshots and an isolated temporary directory only.</summary>
public static class AIReflectionCoreTests
{
    static int passed;
    static void Check(string label, bool result)
    {
        if (!result) throw new InvalidOperationException("[AIReflectionTests] FAIL " + label);
        passed++; Debug.Log("[AIReflectionTests] PASS " + label);
    }
    static bool Near(float first, float second) => Mathf.Abs(first - second) < .0001f;
    static AIActionContextSnapshot State(int turn = 1, int x = 0, EconomicState economy = EconomicState.Warning)
        => new AIActionContextSnapshot { Turn = turn, ActionType = AIActionType.Move,
            ActorRole = "Infantry", TargetCategory = "Infantry", ActorHP = 20, ActorHpRatio = 1,
            ActorAp = 30, ActorX = x, LocalAllyPower = 10, LocalEnemyPower = 10,
            OwnCrystalHP = 15000, OwnCrystalHpRatio = 1, OwnUnitCount = 1,
            VisibleEnemyUnitCount = 1, TargetObserved = true, TargetHP = 20,
            Bread = 30, Iron = 30, Wood = 30, Stone = 30, Water = 30,
            EconomyState = economy, CurrentThreatLevel = 15, DistanceToTarget = 1 };
    static AIActionRecord Record(AIActionType type, int from, int to, bool meaningful = false, int actor = 10)
    {
        var before = State(x: from); before.ActionType = type;
        var after = State(x: to); after.ActionType = type;
        return new AIActionRecord { ActionId = to + 100, BattleId = "Fixture", Turn = 1,
            ActionType = type, ActorRole = "Infantry", TargetCategory = "Infantry", ActorRuntimeId = actor,
            Before = before, After = after, Outcome = AIActionOutcome.Difference(before, after),
            ExecutionSucceeded = true, MeaningfulProgress = meaningful,
            ContextKey = AIActionFeatureExtractor.ContextKey(before),
            ActionKey = AIActionFeatureExtractor.ActionKey(new AIAction { ActionType = type }, before) };
    }
    static AIActionReflectionSystem SystemFor(AIReflectionConfig config, string battle = "Fixture", string directory = null, bool persistent = false)
    {
        var system = new AIActionReflectionSystem(Team.Enemy, config, directory, persistent);
        system.BeginBattle(battle, 15, "Growth"); system.BeginTurn(1);
        return system;
    }

    public static void RunAll()
    {
        passed = 0;
        var config = ScriptableObject.CreateInstance<AIReflectionConfig>();
        config.EnableConsoleDiary = false; config.EnableFileDiary = false;
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FantasyKingdomReflectionTests_" + Guid.NewGuid().ToString("N")));
        try
        {
            Rewards(config);
            ResourceStability(config);
            LearningAndPriority(config);
            RepetitionAndOutcomes(config);
            DiaryAndResume(config, directory);
            Persistence(config, directory);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(config);
            // The target is a literal unique test directory under the verified temporary root.
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (directory.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(directory).StartsWith("FantasyKingdomReflectionTests_", StringComparison.Ordinal)
                && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        Debug.Log("[AIReflectionTests] " + passed + " pure reflection checks passed");
    }

    static void Rewards(AIReflectionConfig config)
    {
        var attack = new AIAction { ActionType = AIActionType.Attack };
        var before = State(); before.ActionType = AIActionType.Attack;
        var after = before.Copy(); after.TargetHP = 0;
        var system = SystemFor(config);
        var token = system.BeginAction(attack, before);
        system.OnEnemyKilled(101, false, token);
        system.OnEnemyKilled(101, false, token);
        system.CompleteAction(token, true, after);
        Check("119 authoritative normal kill is exactly +1", Near(system.LastRecord.Reward, 1) && Near(system.BattleReward, 1));
        int samples = system.Profile.Find(system.LastRecord.ContextKey, system.LastRecord.ActionKey).Samples;
        system.CompleteAction(token, true, after);
        Check("104 duplicate completion cannot add reward or a sample", Near(system.BattleReward, 1)
            && system.Profile.Find(system.LastRecord.ContextKey, system.LastRecord.ActionKey).Samples == samples);
        system.OnEnemyKilled(101, true, token);
        system.OnEnemyKilled(101, true, token);
        Check("120 normal kill upgraded to formation is +2 rather than +3", Near(system.LastRecord.Reward, 2)
            && Near(system.BattleReward, 2) && system.LastRecord.Outcome.EnemyKills == 1 && system.LastRecord.Outcome.FormationKills == 1);

        system = SystemFor(config, "Formation"); token = system.BeginAction(attack, before);
        system.OnEnemyKilled(102, true, token); system.OnEnemyKilled(102, false, token);
        system.CompleteAction(token, true, after);
        Check("120 formation event received first cannot be downgraded or duplicated", Near(system.LastRecord.Reward, 2));
        system = SystemFor(config, "Prediction"); token = system.BeginAction(attack, before);
        system.CompleteAction(token, true, after);
        Check("13 predicted zero HP without a death event earns no kill reward", Near(system.LastRecord.Reward, 0));
        system = SystemFor(config, "ActualDamageAfterTargetLost"); token = system.BeginAction(attack, before);
        system.SetSelectedScores(token, 10, 2, 12);
        system.OnDamageDealt(7, token); system.OnDamageDealt(-99, token); system.OnDamageTaken(3, token);
        var lost = before.Copy(); lost.TargetObserved = false; lost.TargetHP = -1; lost.ActorHP = 17;
        system.CompleteAction(token, true, lost);
        Check("102 authoritative damage survives lost target without reading its hidden HP",
            system.LastRecord.Outcome.DamageDealt == 7 && system.LastRecord.MeaningfulProgress && !system.LastRecord.After.TargetObserved);
        Check("107 actual counterdamage and snapshot difference are not counted twice", system.LastRecord.Outcome.DamageTaken == 3);
        Check("39 base, learned and final selected scores remain separately traceable",
            Near(system.LastRecord.BaseScore, 10) && Near(system.LastRecord.LearnedModifier, 2) && Near(system.LastRecord.FinalScore, 12));

        system = SystemFor(config, "Artifact");
        token = system.BeginAction(new AIAction { ActionType = AIActionType.Move }, State());
        var discovery = State(x: 1); discovery.ExploredTiles = 1;
        system.OnArtifactAcquired("dungeon-artifact-1", token); system.OnArtifactAcquired("dungeon-artifact-1", token);
        system.CompleteAction(token, true, discovery);
        Check("121 artifact event is exactly +1 after duplicate notification", Near(system.LastRecord.Reward, 1) && Near(system.BattleReward, 1));
        system.OnArtifactAcquired("dungeon-artifact-1", token);
        Check("105 artifact duplicate after completion is also ignored", Near(system.BattleReward, 1));

        system = SystemFor(config, "Victory");
        token = system.BeginAction(new AIAction { ActionType = AIActionType.Wait }, State());
        system.CompleteAction(token, true, State()); var last = system.LastRecord;
        system.EndBattle(true, State(7)); system.EndBattle(true, State(7));
        Check("123 victory +100 belongs to the battle and is idempotent", Near(system.BattleReward, 100)
            && Near(last.Reward, 0) && system.Profile.Wins == 1 && system.Profile.BattlesPlayed == 1);
        system = SystemFor(config, "Defeat"); system.EndBattle(false, State(7));
        Check("19 defeat has no invented negative 100 reward", Near(system.BattleReward, 0) && system.Profile.Losses == 1);
        system = SystemFor(config, "VictoryDuringExecution"); token = system.BeginAction(attack, before);
        system.OnEnemyKilled(103, false, token); system.EndBattle(true, after);
        Check("106 battle end waits for an executing action outcome", system.HasPendingActions && system.DiaryCount == 0);
        system.CompleteAction(token, true, after);
        Check("106 executing finishing action is recorded before victory diary", !system.HasPendingActions
            && Near(system.LastRecord.Reward, 1) && Near(system.BattleReward, 101) && system.DiaryCount == 1);
    }

    static void ResourceStability(AIReflectionConfig config)
    {
        var system = SystemFor(config, "Stable");
        for (int i = 0; i < 5; i++)
        {
            var before = State(x: i, economy: EconomicState.Healthy);
            var after = State(x: i + 1, economy: EconomicState.Healthy);
            var token = system.BeginAction(new AIAction { ActionType = AIActionType.Move }, before);
            system.CompleteAction(token, true, after);
            Check("122 unrelated stable-economy move receives no stable reward " + i, Near(system.LastRecord.Reward, 0));
        }
        system.EndTurn(1, State(economy: EconomicState.Healthy)); system.EndTurn(1, State(economy: EconomicState.Healthy));
        Check("122 five actions and duplicate EndTurn receive only one stable reward", Near(system.TurnReward, 1) && Near(system.BattleReward, 1));
        system.BeginTurn(2); system.EndTurn(2, State(2, economy: EconomicState.Healthy));
        Check("122 another own turn may receive one stable reward", Near(system.BattleReward, 2));
        system = SystemFor(config, "EconomicRecovery");
        var prior = State(); prior.OwnBuildingCount = 0;
        var recovered = State(economy: EconomicState.Healthy); recovered.OwnBuildingCount = 1;
        var build = system.BeginAction(new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.Bakery }, prior);
        system.CompleteAction(build, true, recovered); var record = system.LastRecord;
        system.EndTurn(1, recovered);
        Check("17.1 identifiable economic construction receives stable reward once", Near(record.Reward, 1)
            && Near(system.BattleReward, 1) && record.MeaningfulProgress);
    }

    static void LearningAndPriority(AIReflectionConfig config)
    {
        var system = SystemFor(config, "Learning");
        var before = State(); var action = new AIAction { ActionType = AIActionType.Move, Score = 10, StrategicPriority = 4 };
        string context = AIActionFeatureExtractor.ContextKey(before), key = AIActionFeatureExtractor.ActionKey(action, before);
        system.Profile.Learn(new AIActionRecord { ContextKey = context, ActionKey = key, Reward = 10,
            StrategicOutcome = AIStrategicOutcome.Progress, BattleId = "PreviousBattle", Turn = 1 }, config);
        float modifier = system.GetLearnedModifier(action, before);
        Check("125 first successful sample supplies bounded learned +2", Near(modifier, 2));
        action.Score += modifier;
        var rival = new AIAction { Score = 11, StrategicPriority = 4 };
        var actions = new List<AIAction> { rival, action }; actions.Sort(AIAction.ComparePriorityThenScore);
        Check("125 modifier affects the same tier comparison", ReferenceEquals(actions[0], action));
        var defense = new AIAction { Score = -100, StrategicPriority = 0 };
        actions.Add(defense); actions.Sort(AIAction.ComparePriorityThenScore);
        Check("126 critical defense priority survives a better learned score", ReferenceEquals(actions[0], defense));
        var movedMap = before.Copy(); movedMap.ActorX = 101; movedMap.ActorY = 307;
        movedMap.TargetX = 102; movedMap.TargetY = 307;
        Check("108 learning context is independent of runtime ID and absolute map coordinates",
            AIActionFeatureExtractor.ContextKey(movedMap) == context);
        var changed = before.Copy(); changed.EconomyState = EconomicState.Collapse;
        Check("149 changed economic context does not reuse unrelated learned preference", Near(system.GetLearnedModifier(action, changed), 0));
        config.ApplyLearningToSelection = false;
        Check("116 learn-only mode produces zero selection modifier", Near(system.GetLearnedModifier(action, before), 0));
        config.ApplyLearningToSelection = true; config.EnableReflection = false;
        Check("124 reflection off supplies zero modifier", Near(system.GetLearnedModifier(action, before), 0));
        var disabledToken = system.BeginAction(action, before); system.CompleteAction(disabledToken, true, before);
        Check("124 reflection off performs no action recording", !disabledToken.IsValid && system.LastRecord == null);
        config.EnableReflection = true;

        var profile = new AIActionLearningProfile(); config.MaxKnowledgeEntries = 16;
        var important = new AIActionRecord { ContextKey = "important", ActionKey = "defend", Reward = 10,
            StrategicOutcome = AIStrategicOutcome.Progress, BattleId = "PreviousBattle", Turn = 1 };
        for (int i = 0; i < 50; i++) profile.Learn(important, config);
        for (int i = 0; i < 40; i++) profile.Learn(new AIActionRecord { ContextKey = "weak" + i, ActionKey = "move", Reward = 0,
            StrategicOutcome = AIStrategicOutcome.Neutral, BattleId = "PreviousBattle", Turn = i }, config);
        Check("57 knowledge entry capacity is bounded", profile.Entries.Count == 16);
        Check("57 pruning retains repeatedly useful knowledge", profile.Find("important", "defend") != null);
        for (int i = 0; i < 200; i++) profile.Learn(important, config);
        Check("34 learned value never grows past config clamp", profile.Find("important", "defend").LearnedValue <= config.MaxLearnedValue);
        config.MaxKnowledgeEntries = 2048;
    }

    static void RepetitionAndOutcomes(AIReflectionConfig config)
    {
        var repeat = new AIRepeatActionDetector();
        Check("128 initial A to B is not an oscillation", !repeat.Evaluate(Record(AIActionType.Move, 0, 1)));
        Check("128 B to A alone is not enough to infer a pointless cycle", !repeat.Evaluate(Record(AIActionType.Move, 1, 0)));
        var third = Record(AIActionType.Move, 0, 1);
        Check("128 A B A B no-progress cycle is detected", repeat.Evaluate(third) && third.IsOscillation);
        third.FailureReason = AIFailureAnalyzer.Analyze(third);
        Check("128 cycle receives configured negative reward", third.FailureReason == AIFailureReason.RepeatedNoProgress
            && AIActionRewardEvaluator.Evaluate(third, config) < 0);
        repeat.Clear();
        for (int i = 0; i < 3; i++)
        {
            var attack = Record(AIActionType.Attack, 0, 0); attack.After.TargetHP = attack.Before.TargetHP - 3;
            attack.Outcome = AIActionOutcome.Difference(attack.Before, attack.After);
            attack.MeaningfulProgress = AIFailureAnalyzer.HasMeaningfulProgress(attack);
            Check("129 repeated damaging attack is meaningful " + i, attack.MeaningfulProgress && !repeat.Evaluate(attack));
        }
        var shield = Record(AIActionType.Attack, 0, 0); shield.Before.TargetShieldTurns = 2;
        shield.Outcome = AIActionOutcome.Difference(shield.Before, shield.After);
        Check("22 shield reduction is meaningful", AIFailureAnalyzer.HasMeaningfulProgress(shield));
        var heal = Record(AIActionType.SkillUse, 0, 0); heal.After.TargetHP += 4;
        heal.Outcome = AIActionOutcome.Difference(heal.Before, heal.After);
        Check("22 effective healing is meaningful", AIFailureAnalyzer.HasMeaningfulProgress(heal));
        var scout = Record(AIActionType.Move, 0, 1); scout.After.ExploredTiles = 3;
        scout.Outcome = AIActionOutcome.Difference(scout.Before, scout.After);
        Check("22 new explored cells prevent meaningless move penalty", AIFailureAnalyzer.HasMeaningfulProgress(scout));
        var retreat = Record(AIActionType.Retreat, 0, 1); retreat.Before.IncomingDamage = 20; retreat.After.IncomingDamage = 0;
        retreat.Outcome = AIActionOutcome.Difference(retreat.Before, retreat.After);
        Check("22 leaving enemy range is meaningful retreat", AIFailureAnalyzer.HasMeaningfulProgress(retreat));
        var loss = Record(AIActionType.Attack, 0, 0); loss.After.ActorHP = 0; loss.After.OwnUnitCount = 0;
        loss.Outcome = AIActionOutcome.Difference(loss.Before, loss.After);
        Check("107 actor killed by counterattack is included in outcome", loss.Outcome.DamageTaken == 20 && loss.Outcome.OwnLosses == 1
            && AIFailureAnalyzer.Analyze(loss) == AIFailureReason.UnitLost);
        var overextended = Record(AIActionType.Move, 0, 1); overextended.After.LocalEnemyPower = 30;
        overextended.Outcome = AIActionOutcome.Difference(overextended.Before, overextended.After);
        Check("103 movement execution success can still be strategic failure", overextended.ExecutionSucceeded
            && AIFailureAnalyzer.Analyze(overextended) == AIFailureReason.Overextended);
        repeat.Clear();
        for (int i = 0; i < 3; i++)
        {
            var wait = Record(AIActionType.Wait, 0, 0); repeat.Evaluate(wait); wait.FailureReason = AIFailureAnalyzer.Analyze(wait);
            Check("23 repeated quiet defense wait remains neutral " + i, !wait.IsRepeatedAction
                && wait.FailureReason == AIFailureReason.None && Near(AIActionRewardEvaluator.Evaluate(wait, config), 0));
        }
    }

    static void DiaryAndResume(AIReflectionConfig config, string directory)
    {
        config.EnableFileDiary = true;
        var system = SystemFor(config, "Diary:/invalid*name", directory, true);
        for (int turn = 1; turn <= 14; turn++) { system.BeginTurn(turn); system.EndTurn(turn, State(turn)); }
        Check("130 diary is absent after 14 own turns", system.DiaryCount == 0);
        system.BeginTurn(15); system.EndTurn(15, State(15)); system.EndTurn(15, State(15));
        Check("130 turn 15 diary appears once", system.DiaryCount == 1);
        for (int turn = 16; turn <= 30; turn++) { system.BeginTurn(turn); system.EndTurn(turn, State(turn)); }
        Check("130 turn 30 generates the second diary", system.DiaryCount == 2);
        Check("50 diary text files use sanitized filenames", Directory.GetFiles(Path.Combine(directory, "Diary"), "*.txt").Length == 2);
        system = SystemFor(config, "EarlyVictory", directory, true); system.BeginTurn(7); system.EndBattle(true, State(7));
        Check("131 early victory writes immediately before turn 15", system.DiaryCount == 1
            && File.Exists(Path.Combine(directory, "Diary", "AI_Diary_Enemy_EarlyVictory_VICTORY.txt")));
        system = SystemFor(config, "EarlyDefeat", directory, true); system.BeginTurn(7); system.EndBattle(false, State(7));
        Check("131 early defeat writes immediately before turn 15", system.DiaryCount == 1
            && File.Exists(Path.Combine(directory, "Diary", "AI_Diary_Enemy_EarlyDefeat_DEFEAT.txt")));
        config.EnableFileDiary = false;
        system = SystemFor(config, "PersistedBattleOnce", directory, true); system.EndBattle(true, State(7));
        int battles = system.Profile.BattlesPlayed, wins = system.Profile.Wins;
        var duplicateBattle = SystemFor(config, "PersistedBattleOnce", directory, true); duplicateBattle.EndBattle(true, State(7));
        Check("105 reloaded finished battle cannot duplicate cross-battle statistics",
            duplicateBattle.Profile.BattlesPlayed == battles && duplicateBattle.Profile.Wins == wins);

        system = SystemFor(config, "SaveContinuation");
        var token = system.BeginAction(new AIAction { ActionType = AIActionType.Attack }, State());
        var defeated = State(); defeated.TargetHP = 0;
        system.OnEnemyKilled(301, true, token); system.CompleteAction(token, true, defeated);
        for (int own = 1; own <= 14; own++) { system.BeginTurn(own * 2); system.EndTurn(own * 2, State(own * 2)); }
        var saved = JsonUtility.FromJson<AIReflectionBattleState>(JsonUtility.ToJson(system.CaptureBattleState()));
        var restored = new AIActionReflectionSystem(Team.Enemy, config, null, false);
        restored.BeginBattle("NewIdMustBeIgnored", 15, "Growth", saved);
        restored.OnEnemyKilled(301, true); restored.BeginTurn(30); restored.EndTurn(30, State(30));
        Check("58 battle ID and deduplicated events survive save/load", restored.BattleId == "SaveContinuation"
            && Near(restored.BattleReward, 2));
        Check("45 restored diary follows 15 own turns rather than global turn 30", restored.DiaryCount == 1);
        var next = restored.BeginAction(new AIAction { ActionType = AIActionType.Wait }, State(30));
        Check("104 action tokens continue uniquely after resume", next.Id > token.Id);
        restored.CompleteAction(next, true, State(30));
    }

    static void Persistence(AIReflectionConfig config, string directory)
    {
        var repository = new AIReflectionSaveRepository(directory, "Regression", config);
        var first = repository.Load();
        Check("109 absent JSON starts at zero samples", first.SchemaVersion == 1 && first.Entries.Count == 0);
        var record = Record(AIActionType.Move, 0, 1); record.Reward = 1; record.StrategicOutcome = AIStrategicOutcome.Progress;
        first.Learn(record, config); Check("54 initial atomic save succeeds", repository.Save(first));
        string saved = File.ReadAllText(repository.ProfilePath);
        File.WriteAllText(repository.ProfilePath + ".tmp", "interrupted incomplete write");
        Check("54 interrupted temp write leaves valid profile unchanged", repository.Load().Entries.Count == 1 && File.ReadAllText(repository.ProfilePath) == saved);
        first.Learn(record, config); Check("54 replacement save retains backup", repository.Save(first)
            && File.Exists(repository.ProfilePath + ".bak") && !File.Exists(repository.ProfilePath + ".tmp"));
        File.WriteAllText(repository.ProfilePath, "{ invalid learning json");
        var recovered = repository.Load();
        Check("132 corrupted primary uses an intact previous backup", recovered.Entries.Count == 1 && recovered.Entries[0].Samples == 1);
        Check("55 corrupt file is archived", Directory.GetFiles(Path.GetDirectoryName(repository.ProfilePath), "*.corrupt.*").Length > 0);
        var isolated = new AIReflectionSaveRepository(directory, "NoBackup", config);
        Directory.CreateDirectory(Path.GetDirectoryName(isolated.ProfilePath)); File.WriteAllText(isolated.ProfilePath, "{ invalid learning json");
        int warnings = 0;
        Application.LogCallback callback = (message, stack, type) => { if (type == LogType.Warning && message.Contains("AI自己評価")) warnings++; };
        Application.logMessageReceived += callback;
        try
        {
            var fallback = isolated.Load();
            Check("132 corrupt profile without backup returns default and warns", fallback.Entries.Count == 0 && warnings > 0);
        }
        finally { Application.logMessageReceived -= callback; }
        Check("110 deleting learning data preserves empty baseline operation", isolated.Load().Entries.Count == 0);
        var invalid = new AIActionLearningProfile { ProfileId = "NoBackup", SchemaVersion = 999 };
        Check("56 unknown schema cannot overwrite a valid learning file", !isolated.Save(invalid));
        var crossBattle = repository.Load();
        Check("108 saved experience survives a new learning profile load", crossBattle.Find(record.ContextKey, record.ActionKey)?.Samples == 1);
        string blockedRoot = Path.Combine(directory, "FileInsteadOfDirectory"); File.WriteAllText(blockedRoot, "fixture");
        var blockedSystem = SystemFor(config, "PersistenceUnavailable", blockedRoot, true);
        blockedSystem.EndBattle(true, State(7));
        Check("60/113 save failure cannot stop the AI battle result", Near(blockedSystem.BattleReward, 100)
            && blockedSystem.Profile.Wins == 1 && blockedSystem.DiaryCount == 1);
    }
}
#endif
