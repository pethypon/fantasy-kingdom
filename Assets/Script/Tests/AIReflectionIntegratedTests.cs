#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

/// <summary>Integrated specification regressions; value fixtures and a unique temporary persistence directory.</summary>
public static class AIReflectionIntegratedTests
{
    static int passed;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(string label, bool result)
    {
        if (!result) throw new InvalidOperationException("[AIReflectionIntegrated] FAIL " + label);
        passed++; UnityEngine.Debug.Log("[AIReflectionIntegrated] PASS " + label);
    }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
    static T RoundTrip<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
    static AIActionContextSnapshot Facts(int turn = 1) => new AIActionContextSnapshot
    {
        Turn = turn, ActionType = AIActionType.Move, Strategy = "Balanced", MatchPhase = "Early",
        ActorRole = "Infantry", ActorHP = 100, ActorHpRatio = 1, ActorAp = 30,
        TargetCategory = "Infantry", TargetObserved = true, TargetHP = 100, KnownTargetLifeId = "enemy-a",
        LocalAllyPower = 10, LocalEnemyPower = 10, OwnCrystalHP = 15000, OwnCrystalHpRatio = 1,
        OwnUnitCount = 10, VisibleAllyCount = 10, VisibleEnemyCount = 1, VisibleEnemyUnitCount = 1,
        EconomyState = EconomicState.Healthy, Bread = 100, Iron = 100, Wood = 100, Stone = 100,
        DistanceToTarget = 1, OwnCrystalDistance = 5, CurrentThreatLevel = 15
    };
    static AIActionRecord Record(AIActionType type, long id = 1, AIActionContextSnapshot before = null, AIActionContextSnapshot after = null)
    {
        before = before ?? Facts(); after = after ?? before.Copy(); before.ActionType = after.ActionType = type;
        return new AIActionRecord { ActionId = id, BattleId = "Integrated", Turn = before.Turn,
            ActionType = type, ActorRole = before.ActorRole, TargetCategory = before.TargetCategory,
            ActorLifeId = "actor-a", TargetLifeId = before.KnownTargetLifeId, Before = before, After = after,
            Outcome = AIActionOutcome.Difference(before, after), ExecutionSucceeded = true,
            ContextKey = AIActionFeatureExtractor.ContextKey(before),
            ActionKey = AIActionFeatureExtractor.ActionKey(new AIAction { ActionType = type }, before) };
    }
    static float Evaluate(AIActionRecord record, AIReflectionConfig config)
    {
        record.MeaningfulProgress = AIFailureAnalyzer.HasMeaningfulProgress(record, config);
        record.FailureReason = AIFailureAnalyzer.Analyze(record, null, config);
        record.StrategicOutcome = record.FailureReason != AIFailureReason.None ? AIStrategicOutcome.Failure
            : record.MeaningfulProgress ? AIStrategicOutcome.Progress : AIStrategicOutcome.Neutral;
        record.Reward = AIActionRewardEvaluator.Evaluate(record, config); return record.Reward;
    }
    static AIActionReflectionSystem SystemFor(AIReflectionConfig config, string id)
    {
        var system = new AIActionReflectionSystem(Team.Enemy, config, persistent: false);
        system.BeginBattle("Integrated_" + id, 15, "Growth"); return system;
    }

    public static void RunAll()
    {
        passed = 0;
        var config = ScriptableObject.CreateInstance<AIReflectionConfig>();
        config.EnableConsoleDiary = false; config.EnableFileDiary = false;
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FantasyKingdomIntegratedReflection_" + Guid.NewGuid().ToString("N")));
        try
        {
            Economy(config);
            LearningAndPriority(config);
            ImmediateOutcomes(config);
            DelayedCredits(config);
            Army(config);
            DiaryAndTerminal(config);
            TerminalBoundaryCorrections(config, directory);
            PersistenceAndSafety(config, directory);
            ClampsAndPerformance(config);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(config);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (directory.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(directory).StartsWith("FantasyKingdomIntegratedReflection_", StringComparison.Ordinal)
                && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        UnityEngine.Debug.Log("[AIReflectionIntegrated] " + passed + " integrated checks passed");
    }

    static void Economy(AIReflectionConfig config)
    {
        var tracker = new AIEconomyRewardTracker(config); float reward = 0;
        for (int turn = 1; turn <= 10; turn++)
        { tracker.Observe(EconomicState.Healthy, turn); reward += tracker.EndTurn(turn).Reward; }
        Check("T01 Healthy maintenance earns no recovery reward", Near(reward, 0));
        tracker = new AIEconomyRewardTracker(config);
        tracker.Observe(EconomicState.Warning, 1); tracker.EndTurn(1);
        tracker.Observe(EconomicState.Healthy, 2); var recovery = tracker.EndTurn(2);
        Check("T02 Warning to Healthy rewards one episode exactly once", Near(recovery.Reward, 1)
            && recovery.EpisodeId == 1 && recovery.CandidateActionId == 0 && Near(tracker.EndTurn(2).Reward, 0));
        reward = 0;
        for (int turn = 3; turn <= 7; turn++) { tracker.Observe(EconomicState.Healthy, turn); reward += tracker.EndTurn(turn).Reward; }
        Check("T03 five further Healthy turns add zero", Near(reward, 0));
        reward = 0;
        for (int turn = 8; turn <= 15; turn++)
        { tracker.Observe(turn % 2 == 0 ? EconomicState.Warning : EconomicState.Healthy, turn); reward += tracker.EndTurn(turn).Reward; }
        Check("T04 single-turn unhealthy oscillation cannot farm recovery", Near(reward, 0));
        tracker.Observe(EconomicState.Crisis, 16); tracker.EndTurn(16);
        tracker.Observe(EconomicState.Warning, 17); tracker.EndTurn(17);
        tracker.Observe(EconomicState.Healthy, 18);
        var resumed = new AIEconomyRewardTracker(config); resumed.Restore(RoundTrip(tracker.Capture()));
        recovery = resumed.EndTurn(18);
        Check("T04 sustained deterioration re-arms once across save continuation", Near(recovery.Reward, 1)
            && recovery.EpisodeId == 2 && Near(resumed.EndTurn(18).Reward, 0));
        tracker = new AIEconomyRewardTracker(config);
        var before = Facts(); before.EconomyState = EconomicState.Warning;
        var after = before.Copy(); after.EconomyState = EconomicState.Healthy; after.OwnBuildingCount++;
        var build = Record(AIActionType.Build, 41, before, after); tracker.ObserveAction(build, 1);
        recovery = tracker.EndTurn(1);
        Check("economic recovery is attributed to a confirmed causal build", recovery.CandidateActionId == 41 && Near(recovery.Reward, 1));
        tracker = new AIEconomyRewardTracker(config); tracker.Observe(EconomicState.Warning, 1);
        tracker.Observe(EconomicState.Healthy, 1); tracker.ObserveAction(Record(AIActionType.Build, 42), 1);
        Check("a later unrelated build cannot claim natural recovery", tracker.EndTurn(1).CandidateActionId == 0);
        tracker = new AIEconomyRewardTracker(config);
        tracker.Restore(new AIEconomyRewardState { HasGrantedRecovery = true });
        tracker.Observe(EconomicState.Warning, 1); tracker.Observe(EconomicState.Healthy, 2);
        Check("legacy paid stability starts with conservative re-arming", Near(tracker.EndTurn(2).Reward, 0));
    }

    static void LearningAndPriority(AIReflectionConfig config)
    {
        var profile = new AIActionLearningProfile { ProfileId = "IntegratedLearning" };
        var record = Record(AIActionType.Move); record.Reward = 1; record.StrategicOutcome = AIStrategicOutcome.Progress;
        profile.Learn(record, config);
        Check("T05 first sample is recorded with zero selection modifier", profile.Find(record.ContextKey, record.ActionKey).Samples == 1
            && Near(profile.GetModifier(record.ContextKey, record.ActionKey, config), 0));
        profile.Learn(record, config); var entry = profile.Find(record.ContextKey, record.ActionKey);
        float modifier = profile.GetModifier(record.ContextKey, record.ActionKey, config);
        Check("T06 second sample enables confidence-scaled learning", modifier > 0
            && Near(modifier, entry.LearnedValue * 2f / config.FullConfidenceSamples));
        var defense = new AIAction { ActionType = AIActionType.DefenseRepos, Score = 10, StrategicPriority = 0 };
        var attack = new AIAction { ActionType = AIActionType.Attack, Score = 100 + modifier, StrategicPriority = 1 };
        var actions = new List<AIAction> { attack, defense }; actions.Sort(AIAction.ComparePriorityThenScore);
        Check("T07 existing priority comparison preserves Crystal defense", ReferenceEquals(actions[0], defense) && actions.Count == 2);
        var first = new AIAction { Score = 10 + modifier, StrategicPriority = 4 };
        var second = new AIAction { Score = 10, StrategicPriority = 4 };
        actions = new List<AIAction> { second, first }; actions.Sort(AIAction.ComparePriorityThenScore);
        Check("experience ranks candidates inside the same priority band", ReferenceEquals(actions[0], first));
        var otherMap = record.Before.Copy(); otherMap.ActorX = 123; otherMap.ActorY = 456; otherMap.TargetX = 321; otherMap.TargetY = 654;
        Check("T29 generalized context does not contain absolute coordinates", AIActionFeatureExtractor.ContextKey(otherMap) == record.ContextKey);
        string json = JsonUtility.ToJson(profile);
        Check("T29 cross-battle JSON contains no coordinates or Unity runtime identity",
            !json.Contains("\"ActorX\"") && !json.Contains("\"TargetY\"") && !json.Contains("InstanceID")
            && !json.Contains("ActorRuntimeId") && !json.Contains("ActorLifeId") && !json.Contains("UnityEngine"));
    }

    static void ImmediateOutcomes(AIReflectionConfig config)
    {
        var attack = new AIAction { ActionType = AIActionType.Attack };
        var before = Facts(); var after = before.Copy(); after.TargetHP = 0;
        var system = SystemFor(config, "NormalKill"); system.BeginTurn(1);
        var token = system.BeginAction(attack, before); system.OnEnemyKilled("enemy-a", false, token);
        system.OnEnemyKilled("enemy-a", false, token); system.CompleteAction(token, true, after);
        Check("T09 authoritative normal kill remains exactly +1", Near(system.LastRecord.Reward, 1) && Near(system.BattleReward, 1));
        system.OnEnemyKilled("enemy-a", true, token); system.OnEnemyKilled("enemy-a", true, token);
        Check("T08 formation promotion totals +2 rather than +3", Near(system.LastRecord.Reward, 2)
            && Near(system.BattleReward, 2) && system.LastRecord.Outcome.FormationKills == 1);
        system = SystemFor(config, "Artifact"); system.BeginTurn(1);
        token = system.BeginAction(new AIAction { ActionType = AIActionType.Move }, before);
        system.OnArtifactAcquired("artifact-a", token); system.OnArtifactAcquired("artifact-a", token);
        system.CompleteAction(token, true, before.Copy());
        Check("T10 duplicate Artifact notification rewards only +1", Near(system.LastRecord.Reward, 1));
        before = Facts(); after = before.Copy(); after.ActorHeight = 2; after.CanAttackObservedEnemy = true;
        var record = Record(AIActionType.Move, before: before, after: after); Evaluate(record, config);
        Check("T11 useful high ground rewards observed attack advantage", record.RewardBreakdown.Position > 0
            && record.SuccessReasons.Contains(AISuccessReason.HighGroundAdvantage));
        before = Facts(); before.LocalEnemyPower = 0; after = before.Copy(); after.ActorHeight = 2;
        record = Record(AIActionType.Move, before: before, after: after); Evaluate(record, config);
        Check("T12 height alone without threat or objective is neutral", Near(record.Reward, 0) && !record.MeaningfulProgress);
        before = Facts(); before.ActorHP = 30; before.ActorHpRatio = .3f; before.LocalAllyPower = 7;
        before.LocalEnemyPower = 20; before.IncomingDamage = 22; before.OwnCrystalDistance = 6;
        after = before.Copy(); after.IncomingDamage = 2; after.OwnCrystalDistance = 5; after.ActorX = 1;
        record = Record(AIActionType.Retreat, before: before, after: after); Evaluate(record, config);
        Check("T13 low-HP retreat to lower risk is successful", record.RewardBreakdown.Survival > 0
            && record.Reward > 0 && record.SuccessReasons.Contains(AISuccessReason.SuccessfulRetreat));
        before = Facts(); before.LocalAllyPower = 140; before.LocalEnemyPower = 80; before.HasObjective = true; before.DistanceToObjective = 5;
        after = before.Copy(); after.DistanceToObjective = 8; after.ActorX = -1;
        record = Record(AIActionType.Retreat, before: before, after: after); Evaluate(record, config);
        Check("T14 needless retreat from an objective does not reward movement", record.Reward <= 0);
        before = Facts(); before.ActorRole = "Scout"; before.ExploredTiles = 100; before.VisibleEnemyLifeIds.Clear();
        after = before.Copy(); after.ExploredTiles = 103; after.VisibleEnemyLifeIds.Add("new-enemy");
        record = Record(AIActionType.Move, before: before, after: after); Evaluate(record, config);
        Check("T15 Scout values genuinely new tiles and enemy identity", record.RewardBreakdown.Information > 0
            && record.Outcome.NewEnemyLifeIds.Count == 1);
        var reconSystem = SystemFor(config, "ContactDedupe"); reconSystem.BeginTurn(1);
        before = Facts(); before.ActorRole = "Scout"; before.TargetCategory = "Cell";
        before.TargetObserved = false; before.TargetHP = -1; before.KnownTargetLifeId = null; before.VisibleEnemyCount = 0;
        after = before.Copy(); after.VisibleEnemyLifeIds.Add("seen-once"); after.VisibleEnemyCount = 1;
        token = reconSystem.BeginAction(new AIAction { ActionType = AIActionType.Move }, before); reconSystem.CompleteAction(token, true, after);
        float firstDiscovery = reconSystem.LastRecord.RewardBreakdown.Information;
        var restoredRecon = SystemFor(config, "ContactResume"); restoredRecon.BeginBattle(resume: RoundTrip(reconSystem.CaptureBattleState()));
        token = restoredRecon.BeginAction(new AIAction { ActionType = AIActionType.Move }, before); restoredRecon.CompleteAction(token, true, after);
        Check("re-observing a known enemy after save continuation cannot farm Scout reward", firstDiscovery > 0
            && restoredRecon.LastRecord.Outcome.NewEnemyLifeIds.Count == 0 && Near(restoredRecon.LastRecord.RewardBreakdown.Information, 0));
        var repeats = new AIRepeatActionDetector();
        var a = Record(AIActionType.Move, 1); a.After.ActorX = 1; a.Outcome = AIActionOutcome.Difference(a.Before, a.After);
        Evaluate(a, config); repeats.Evaluate(a);
        Check("T16 first known-area move stays neutral", Near(a.Reward, 0) && !a.IsRepeatedAction);
        var b = Record(AIActionType.Move, 2); b.Before.ActorX = 1; b.Outcome = AIActionOutcome.Difference(b.Before, b.After);
        Evaluate(b, config); repeats.Evaluate(b);
        var again = Record(AIActionType.Move, 3); again.After.ActorX = 1; again.Outcome = AIActionOutcome.Difference(again.Before, again.After);
        Evaluate(again, config); repeats.Evaluate(again); Evaluate(again, config);
        Check("T16 repeated known-area oscillation incurs bounded penalty", again.IsOscillation && again.Reward < 0);
        before = Facts(); before.TargetIsAlly = true; before.KnownTargetLifeId = "ally-b"; after = before.Copy(); after.TargetShieldTurns = 3;
        record = Record(AIActionType.SkillUse, before: before, after: after); Evaluate(record, config);
        Check("T17 a zero-damage shield buff is preparation rather than NoDamage", record.Outcome.ShieldGranted
            && record.FailureReason == AIFailureReason.None && record.RewardBreakdown.Preparation > 0);
        before = Facts(); before.TargetEffectCount = 1; before.TargetEffectSignature = 10;
        after = before.Copy(); after.TargetEffectSignature = 11;
        record = Record(AIActionType.SkillUse, before: before, after: after); Evaluate(record, config);
        Check("T18 same-count debuff refresh is meaningful preparation", record.Outcome.EffectChanged
            && record.FailureReason != AIFailureReason.NoDamage && record.RewardBreakdown.Preparation > 0);
        before = Facts(); before.IsApBuff = true; before.ActionApCost = 2; before.ActorAp = 10;
        after = before.Copy(); after.ActorAp = 10;
        record = Record(AIActionType.SkillUse, before: before, after: after); Evaluate(record, config);
        Check("Haste recognizes actual recovered AP after paying its action cost", Near(record.Outcome.ApRecovered, 2)
            && record.MeaningfulProgress && record.FailureReason != AIFailureReason.NoDamage);
        before = Facts(); before.AreaEffects.Add(new AIReflectionEffectObservation { LifeId = "ally-b", Signature = 10, HP = 80, IsAlly = true });
        after = before.Copy(); after.AreaEffects[0].Signature = 11; after.AreaEffects[0].HP = 100;
        record = Record(AIActionType.SkillUse, before: before, after: after); Evaluate(record, config);
        Check("area buff and healing use common observed ally effect evidence", record.Outcome.EffectChanged
            && record.Outcome.HealingDone == 20 && record.RewardBreakdown.Preparation > 0 && record.FailureReason != AIFailureReason.NoDamage);
        after = before.Copy(); after.AreaEffects.Clear();
        Check("T28 a disappearing area target does not imply an effect change", !AIActionOutcome.Difference(before, after).EffectChanged);
        before = Facts(); before.TargetObserved = false; before.TargetHP = -1;
        after = before.Copy(); after.TargetHP = 7; after.TargetEffectCount = 99; after.TargetEffectSignature = 99;
        var hidden = AIActionOutcome.Difference(before, after);
        Check("T28 unobserved target fields cannot produce inferred damage or effects", hidden.DamageDealt == 0 && !hidden.EffectChanged);
        before = Facts(); before.IsRangedActor = true; after = before.Copy(); after.CanAttackObservedEnemy = true; after.OutsideObservedEnemyRange = true;
        record = Record(AIActionType.Move, before: before, after: after); Evaluate(record, config);
        Check("range advantage carries Position and Survival success evidence", record.RewardBreakdown.Position > 0
            && record.RewardBreakdown.Survival > 0 && record.SuccessReasons.Contains(AISuccessReason.GoodRangeManagement));
        before = Facts(); before.LocalAllyPower = 7; before.LocalEnemyPower = 10;
        after = before.Copy(); after.LocalAllyPower = 12;
        record = Record(AIActionType.Support, before: before, after: after); Evaluate(record, config);
        Check("local power improvement uses a true power ratio", record.RewardBreakdown.LocalPower > 0 && Near(record.Outcome.LocalPowerRatioDelta, .5f));
        before = Facts(); before.OwnCrystalThreatened = before.EmergencyDefense = true; before.HasObjective = true; before.DistanceToObjective = 10;
        after = before.Copy(); after.OwnCrystalThreatened = false; after.DistanceToObjective = 5;
        record = Record(AIActionType.DefenseRepos, before: before, after: after); Evaluate(record, config);
        Check("emergency threat relief earns Defense without offensive objective farming", record.RewardBreakdown.Defense > 0
            && Near(record.RewardBreakdown.Objective, 0));
        record = Record(AIActionType.Wait); Evaluate(record, config);
        Check("T27 a quiet wait is neutral and not a failure", Near(record.Reward, 0) && record.FailureReason == AIFailureReason.None);
    }

    static void DelayedCredits(AIReflectionConfig config)
    {
        var assigner = new AIDelayedCreditAssigner(config);
        var scout = Record(AIActionType.Move, 1); scout.ActorRole = "Scout"; scout.ActorLifeId = "scout-a";
        scout.MeaningfulProgress = scout.PreparationCandidate = true; scout.Outcome.NewEnemyLifeIds.Add("enemy-a");
        var flank = Record(AIActionType.Surround, 2); flank.ActorLifeId = "front-a"; flank.TargetLifeId = "enemy-a";
        flank.MeaningfulProgress = flank.PreparationCandidate = true;
        assigner.Remember(scout); assigner.Remember(flank);
        var outcome = new AIDelayedOutcomeEvent { EventId = "kill-a", KilledLifeId = "enemy-a", ActorLifeId = "killer-a", ActionId = 3, Reward = 2 };
        var credits = assigner.ApplyOutcome(outcome);
        Check("T19 discovered enemy and related surround receive decayed credit", credits.Count == 2
            && credits[0].ActionId == 2 && credits[0].Delta > credits[1].Delta && credits[1].ActionId == 1);
        var formation = Record(AIActionType.Attack, 3); formation.Outcome.EnemyKills = formation.Outcome.FormationKills = 1;
        Evaluate(formation, config);
        Check("T19 formation action retains its own exact +2", Near(formation.Reward, 2));
        Check("T21 repeated outcome cannot add delayed credit", assigner.ApplyOutcome(outcome).Count == 0);
        var restored = new AIDelayedCreditAssigner(config); restored.Restore(RoundTrip(assigner.Capture()));
        Check("T21 delayed event dedupe survives saving", restored.ApplyOutcome(outcome).Count == 0);
        var house = Record(AIActionType.Build, 4); house.TargetLifeId = "enemy-a"; house.MeaningfulProgress = house.PreparationCandidate = true;
        assigner = new AIDelayedCreditAssigner(config); assigner.Remember(house); outcome.ActionId = 5; outcome.EventId = "kill-house-test";
        Check("T20 unrelated House never earns preparation credit", assigner.ApplyOutcome(outcome).Count == 0);
        assigner = new AIDelayedCreditAssigner(config); assigner.Remember(scout);
        for (int i = 2; i <= 6; i++) assigner.Remember(Record(AIActionType.Wait, i));
        outcome.ActionId = 7; outcome.EventId = "outside-window";
        Check("delayed credit searches only the last five actions", assigner.ApplyOutcome(outcome).Count == 0 && assigner.Capture().RecentActions.Count == 5);
        assigner = new AIDelayedCreditAssigner(config); assigner.Remember(flank); float total = 0;
        for (int i = 0; i < 5; i++) { outcome.ActionId = 10 + i; outcome.EventId = "cap-" + i; foreach (var credit in assigner.ApplyOutcome(outcome)) total += credit.Delta; }
        Check("delayed credit is capped at +1 per preparation action", Near(total, config.MaxDelayedRewardPerAction));
        var buff = Record(AIActionType.SkillUse, 1); buff.ActorLifeId = "caster"; buff.TargetLifeId = "caster";
        buff.Before.TargetIsAlly = true; buff.Before.SupportTargetLifeIds.Add("killer-a"); buff.Outcome.EffectChanged = true;
        buff.MeaningfulProgress = true; assigner = new AIDelayedCreditAssigner(config); assigner.Remember(buff);
        outcome.ActionId = 2; outcome.EventId = "area-buff-kill";
        Check("area buff credit follows the actually supported ally life ID", assigner.ApplyOutcome(outcome).Count == 1);
        DelayedSystemContinuation(config);
        DelayedBudgetFeedback(config);
    }

    static void DelayedBudgetFeedback(AIReflectionConfig config)
    {
        float oldBudget = config.MaxActionReward; config.MaxActionReward = .5f;
        try
        {
            var system = SystemFor(config, "DelayedBudgetFeedback"); system.BeginTurn(1);
            var before = Facts(); before.ActorRole = "Scout"; before.TargetCategory = "Cell";
            before.TargetObserved = false; before.TargetHP = -1; before.KnownTargetLifeId = null;
            before.VisibleEnemyCount = before.VisibleEnemyUnitCount = 0; before.LocalEnemyPower = 0;
            var after = before.Copy(); after.ExploredTiles += 10; after.VisibleEnemyCount = after.VisibleEnemyUnitCount = 2;
            after.VisibleEnemyLifeIds.Add("budget-kill-a"); after.VisibleEnemyLifeIds.Add("budget-kill-b");
            var token = system.BeginAction(new AIAction { ActionType = AIActionType.Move }, before); system.CompleteAction(token, true, after);
            var preparation = system.LastRecord; float immediate = preparation.ImmediateReward;
            Check("delayed-budget fixture uses its complete action budget for immediate Scout information", Near(immediate, .5f)
                && Near(preparation.RewardBreakdown.Information, .5f) && Near(preparation.Reward, .5f));
            before = Facts(); before.KnownTargetLifeId = "budget-kill-a";
            before.VisibleEnemyLifeIds.Add("budget-kill-a"); before.VisibleEnemyLifeIds.Add("budget-kill-b");
            after = before.Copy(); after.TargetHP = 0;
            token = system.BeginAction(new AIAction { ActionType = AIActionType.Attack }, before);
            system.OnEnemyKilled("budget-kill-a", false, token); system.CompleteAction(token, true, after);
            var saved = system.CaptureBattleState();
            var evidence = saved.DelayedCreditState.RecentActions.Find(action => action.ActionId == preparation.ActionId);
            var entry = system.Profile.Find(preparation.ContextKey, preparation.ActionKey);
            Check("a causal kill with no free action budget does not consume delayed reward capacity", evidence != null
                && evidence.AppliedEventIds.Count == 1 && Near(evidence.GrantedReward, 0)
                && Near(preparation.RewardBreakdown.DelayedReward, 0) && Near(preparation.ImmediateReward, immediate)
                && Near(preparation.Reward, .5f) && entry.Samples == 1);
            config.MaxActionReward = Mathf.Max(oldBudget, 1.5f);
            before = Facts(); before.KnownTargetLifeId = "budget-kill-b"; before.VisibleEnemyLifeIds.Add("budget-kill-b");
            after = before.Copy(); after.TargetHP = 0;
            token = system.BeginAction(new AIAction { ActionType = AIActionType.Attack }, before);
            system.OnEnemyKilled("budget-kill-b", false, token); system.CompleteAction(token, true, after);
            saved = system.CaptureBattleState(); evidence = saved.DelayedCreditState.RecentActions.Find(action => action.ActionId == preparation.ActionId);
            float granted = preparation.RewardBreakdown.DelayedReward;
            Check("a different causal kill uses released capacity after raising the action budget", granted > 0
                && evidence.AppliedEventIds.Count == 2 && Near(evidence.GrantedReward, granted)
                && Near(preparation.ImmediateReward, immediate) && Near(preparation.RewardBreakdown.Information, .5f)
                && Near(preparation.Reward, immediate + granted) && Near(entry.CumulativeReward, preparation.Reward)
                && entry.Samples == 1);
            float battleReward = system.BattleReward;
            system.OnEnemyKilled("budget-kill-b", false, token);
            saved = system.CaptureBattleState(); evidence = saved.DelayedCreditState.RecentActions.Find(action => action.ActionId == preparation.ActionId);
            Check("duplicating the applied event preserves helper record and sample consistency", Near(system.BattleReward, battleReward)
                && Near(evidence.GrantedReward, granted) && Near(preparation.RewardBreakdown.DelayedReward, granted)
                && Near(preparation.Reward, preparation.RewardBreakdown.Total) && entry.Samples == 1);
        }
        finally { config.MaxActionReward = oldBudget; }
    }

    static void DelayedSystemContinuation(AIReflectionConfig config)
    {
        var system = SystemFor(config, "DelayedContinuation"); system.BeginTurn(1);
        var before = Facts(); before.ActorRole = "Scout"; before.KnownTargetLifeId = null; before.TargetObserved = false;
        before.TargetCategory = "Cell"; before.VisibleEnemyLifeIds.Clear();
        var after = before.Copy(); after.VisibleEnemyLifeIds.Add("discovered-a");
        var token = system.BeginAction(new AIAction { ActionType = AIActionType.Move }, before); system.CompleteAction(token, true, after);
        var preparation = system.LastRecord; float immediate = preparation.Reward;
        before = Facts(); before.KnownTargetLifeId = "discovered-a"; before.VisibleEnemyLifeIds.Add("discovered-a");
        after = before.Copy(); after.TargetHP = 0;
        token = system.BeginAction(new AIAction { ActionType = AIActionType.Attack }, before);
        system.OnEnemyKilled("discovered-a", true, token); system.CompleteAction(token, true, after);
        var entry = system.Profile.Find(preparation.ContextKey, preparation.ActionKey);
        Check("integrated delayed correction changes reward without adding a sample", preparation.Reward > immediate
            && preparation.RewardBreakdown.DelayedReward > 0 && entry.Samples == 1
            && Near(entry.CumulativeReward, preparation.Reward) && Near(preparation.Reward, preparation.RewardBreakdown.Total));
        var saved = RoundTrip(system.CaptureBattleState()); var resumed = SystemFor(config, "UnusedResumeId");
        resumed.BeginBattle(resume: saved); float earned = resumed.BattleReward; resumed.OnEnemyKilled("discovered-a", true);
        Check("integrated save continuation preserves delayed and kill event dedupe", Near(resumed.BattleReward, earned)
            && resumed.BattleId == system.BattleId && resumed.CaptureBattleState().DelayedCreditState.RecentActions.Count > 0);
        var nextPeriod = new AIReflectionInterval();
        nextPeriod.CorrectReward(preparation, .1f, .02f, new AIRewardBreakdown { DelayedReward = .1f }, 16);
        Check("old-period credit is recorded as a correction without a new action", nextPeriod.Actions == 0
            && nextPeriod.Patterns[0].Samples == 0 && Near(nextPeriod.Reward, .1f)
            && nextPeriod.TraceExamples[0].IsRewardCorrection && nextPeriod.TraceExamples[0].Turn == 16);
    }

    static AIArmyTurnEvidence StrongArmy() => new AIArmyTurnEvidence
    {
        UnitCount = 14, EnemyUnitCount = 8, OwnPower = 140, EnemyPower = 80, Upkeep = 14,
        EnemyStrengthKnown = true, HasObjective = true, AttackOpportunity = true, EconomyState = EconomicState.Healthy
    };
    static void Army(AIReflectionConfig config)
    {
        var analyzer = new AIArmyUtilizationAnalyzer(config); var evidence = StrongArmy(); evidence.ReserveNecessary = true;
        float reward = 0; AIArmyUtilizationResult result = null;
        for (int turn = 1; turn <= 4; turn++) { result = analyzer.EndTurn(turn, evidence); reward += result.Reward; }
        Check("T22 rational Reserve is never idle", Near(reward, 0) && !result.IdleArmy);
        analyzer = new AIArmyUtilizationAnalyzer(config); evidence = StrongArmy(); evidence.OwnPower = 60; evidence.EnemyPower = 120;
        evidence.ArmyNearBase = evidence.EnemyFar = true;
        for (int turn = 1; turn <= 10; turn++) result = analyzer.EndTurn(turn, evidence);
        Check("T23 weaker army defending its base is not unnecessary turtle", !result.UnnecessaryTurtle && Near(result.Reward, 0));
        analyzer = new AIArmyUtilizationAnalyzer(config); evidence = StrongArmy();
        for (int turn = 1; turn <= 10; turn++) result = analyzer.EndTurn(turn, evidence);
        Check("T24 true 140 versus 80 idle army detects missed opportunity", result.IdleArmy && result.MissedOpportunity
            && result.OwnPower == 140 && result.EnemyPower == 80 && result.Reward < 0 && result.Reward >= -1.0001f);
        Check("same own-turn analysis cannot duplicate the aggregate penalty", Near(analyzer.EndTurn(10, evidence).Reward, 0));
        var saved = RoundTrip(analyzer.Capture()); analyzer = new AIArmyUtilizationAnalyzer(config); analyzer.Restore(saved);
        Check("army clocks persist while ended-turn dedupe survives loading", Near(analyzer.EndTurn(10, evidence).Reward, 0)
            && analyzer.EndTurn(11, evidence).IdleArmy);
        analyzer = new AIArmyUtilizationAnalyzer(config); evidence = StrongArmy(); evidence.UnitCount = 5;
        evidence.EnemyUnitCount = 12; evidence.OwnPower = 50; evidence.EnemyPower = 120; evidence.Upkeep = 5;
        analyzer.EndTurn(1, evidence);
        for (int turn = 2; turn <= 6; turn++)
        { evidence.UnitCount++; evidence.OwnPower += 10; evidence.Upkeep++; evidence.EconomyState = EconomicState.Warning; result = analyzer.EndTurn(turn, evidence); }
        Check("T25 five needed reinforcements against twelve enemies are not overproduction", !result.ExcessiveProduction);
        analyzer = new AIArmyUtilizationAnalyzer(config); evidence = StrongArmy(); evidence.UnitCount = 10; evidence.Upkeep = 10;
        analyzer.EndTurn(1, evidence);
        for (int turn = 2; turn <= 6; turn++)
        { evidence.UnitCount++; evidence.Upkeep++; evidence.EconomyState = EconomicState.Warning; result = analyzer.EndTurn(turn, evidence); }
        Check("T26 growth plus low utilization upkeep and worsening economy detects excess", result.ExcessiveProduction
            && result.Breakdown.OverproductionPenalty < 0 && result.Reward >= -1.0001f);
        foreach (string missing in new[] { "Growth", "LowUse", "Upkeep", "WorseEconomy", "NoProgress" })
        {
            analyzer = new AIArmyUtilizationAnalyzer(config); evidence = StrongArmy(); evidence.UnitCount = 10; evidence.Upkeep = 10;
            if (missing == "LowUse") evidence.UtilizedUnitCount = evidence.UnitCount;
            if (missing == "NoProgress") evidence.ObjectiveProgress = true;
            analyzer.EndTurn(1, evidence);
            for (int turn = 2; turn <= 6; turn++)
            {
                if (missing != "Growth") evidence.UnitCount++;
                if (missing != "Upkeep") evidence.Upkeep++;
                if (missing != "WorseEconomy") evidence.EconomyState = EconomicState.Warning;
                if (missing == "LowUse") evidence.UtilizedUnitCount = evidence.UnitCount;
                result = analyzer.EndTurn(turn, evidence);
            }
            Check("T26 overproduction requires every causal signal: " + missing, !result.ExcessiveProduction);
        }
        foreach (string protection in new[] { "Defense", "AP", "Unknown", "Progress" })
        {
            analyzer = new AIArmyUtilizationAnalyzer(config); evidence = StrongArmy();
            evidence.DefenseValue = protection == "Defense"; evidence.PreserveAP = protection == "AP";
            evidence.EnemyStrengthKnown = protection != "Unknown"; evidence.ObjectiveProgress = protection == "Progress";
            for (int turn = 1; turn <= 10; turn++) result = analyzer.EndTurn(turn, evidence);
            Check("reasonable army activity or uncertainty blocks idle penalty: " + protection, Near(result.Reward, 0));
        }
    }

    static void DiaryAndTerminal(AIReflectionConfig config)
    {
        var system = SystemFor(config, "Diary15");
        for (int own = 1; own <= 15; own++) { system.BeginTurn(own * 2); system.EndTurn(own * 2, Facts(own * 2)); }
        Check("T30 diary follows fifteen own turns rather than global thirty", system.DiaryCount == 1
            && system.LatestDiaryText.Contains("自軍ターン: 1～15"));
        system.EndTurn(30, Facts(30)); Check("T30 duplicate EndTurn does not create a second diary", system.DiaryCount == 1);
        system = SystemFor(config, "Terminal15");
        for (int own = 1; own <= 15; own++) { system.BeginTurn(own); system.EndTurn(own, Facts(own), own == 15); }
        system.EndBattle(true, Facts(15));
        Check("T31 terminal turn fifteen writes one final diary", system.DiaryCount == 1
            && system.LatestDiaryText.Contains("結果: VICTORY") && system.LatestDiaryText.Contains("自軍ターン: 1～15"));
        system = SystemFor(config, "RegularThenTerminal15");
        for (int own = 1; own <= 15; own++) { system.BeginTurn(own); system.EndTurn(own, Facts(own)); }
        system.EndBattle(false, Facts(15));
        Check("T31 finalizing an already scheduled boundary replaces its entry", system.DiaryCount == 1 && system.LatestDiaryText.Contains("結果: DEFEAT"));
        system = SystemFor(config, "Terminal17");
        for (int own = 1; own <= 17; own++) { system.BeginTurn(own); system.EndTurn(own, Facts(own), own == 17); }
        system.EndBattle(false, Facts(17));
        Check("T32 turn seventeen final diary covers only own turns sixteen to seventeen", system.DiaryCount == 2
            && system.LatestDiaryText.Contains("自軍ターン: 16～17"));
        system = SystemFor(config, "Victory"); system.BeginTurn(1);
        var before = Facts(); var after = before.Copy(); after.TargetHP = 0;
        var token = system.BeginAction(new AIAction { ActionType = AIActionType.Attack }, before);
        system.OnEnemyKilled("victory-kill", false, token); system.CompleteAction(token, true, after);
        float actionReward = system.LastRecord.Reward, battleReward = system.BattleReward; system.EndBattle(true, after);
        Check("T33 victory adds +100 to the battle while leaving the last action unchanged", Near(system.BattleReward, battleReward + 100)
            && Near(system.LastRecord.Reward, actionReward) && Near(actionReward, 1));
        system = SystemFor(config, "LosingGoodAction"); system.BeginTurn(1);
        token = system.BeginAction(new AIAction { ActionType = AIActionType.Attack }, before);
        system.OnEnemyKilled("losing-kill", false, token); system.CompleteAction(token, true, after);
        system.EndBattle(false, after);
        Check("T34 a losing battle retains its successful action memory", system.LastRecord.Reward > 0
            && system.Profile.Find(system.LastRecord.ContextKey, system.LastRecord.ActionKey).LearnedValue > 0);
        system = SystemFor(config, "WinningBadAction"); system.BeginTurn(1);
        token = system.BeginAction(new AIAction { ActionType = AIActionType.Attack }, before); system.CompleteAction(token, true, before.Copy());
        float failureReward = system.LastRecord.Reward; system.EndBattle(true, before);
        Check("T35 victory does not turn failed actions into positive memory", failureReward < 0 && Near(system.LastRecord.Reward, failureReward)
            && system.Profile.Find(system.LastRecord.ContextKey, system.LastRecord.ActionKey).LearnedValue < 0);
        var record = Record(AIActionType.Move); record.Before.ActorHeight = 0; record.After.ActorHeight = 2; record.After.CanAttackObservedEnemy = true;
        record.Outcome = AIActionOutcome.Difference(record.Before, record.After); Evaluate(record, config);
        record.BaseScore = 32; record.LearnedModifier = .3f; record.FinalScore = 32.3f; record.LearningValueChange = .05f;
        record.DecisionTrace = AIReflectionDecisionTrace.Build(new AIAction { ActionType = AIActionType.Move, StrategicPriority = 3 }, record);
        var trace = record.DecisionTrace;
        Check("decision trace reports actual intent facts outcome and learning delta", !string.IsNullOrEmpty(trace.Intent)
            && !string.IsNullOrEmpty(trace.ExpectedOutcome) && !string.IsNullOrEmpty(trace.ActualOutcome)
            && !string.IsNullOrEmpty(trace.Lesson) && trace.DecisionFacts.Count > 0 && Near(trace.LearningValueChange, .05f));
        var firstInterval = new AIReflectionInterval(); firstInterval.Observe(record, .05f, 2048);
        var lastInterval = new AIReflectionInterval(); lastInterval.AddReward(new AIRewardBreakdown { Economy = 1 }, 15);
        lastInterval.MergeFrom(firstInterval, 2048);
        Check("same-boundary interval merging preserves action and reward statistics", lastInterval.Actions == 1
            && Near(lastInterval.Reward, record.Reward + 1) && Near(lastInterval.Breakdown.Total, lastInterval.Reward));
    }

    static void TerminalBoundaryCorrections(AIReflectionConfig config, string directory)
    {
        var economic = SystemFor(config, "TerminalEconomicRecovery"); economic.BeginTurn(1);
        var before = Facts(); before.ActorRole = "Builder"; before.TargetCategory = "Facility:Bakery";
        before.EconomyState = EconomicState.Warning;
        var after = before.Copy(); after.EconomyState = EconomicState.Healthy; after.OwnBuildingCount++;
        var token = economic.BeginAction(new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.Bakery }, before);
        economic.CompleteAction(token, true, after); economic.EndBattle(true, after);
        var terminal = economic.CaptureBattleState();
        Check("terminal Build recovery flushes one economy reward before EndTurn", Near(economic.LastRecord.RewardBreakdown.Economy, 1)
            && Near(economic.LastRecord.Reward, 1) && terminal.TotalStableTurns == 1 && terminal.OwnTurns == 1
            && Near(economic.BattleReward, 101));
        economic.EndBattle(true, after); economic.EndTurn(1, after);
        terminal = economic.CaptureBattleState();
        Check("terminal recovery and the own-turn count cannot flush twice", terminal.TotalStableTurns == 1 && terminal.OwnTurns == 1
            && Near(economic.BattleReward, 101));

        bool fileDiary = config.EnableFileDiary; config.EnableFileDiary = true;
        try
        {
            string storage = Path.Combine(directory, "LateFormationBoundary");
            var system = new AIActionReflectionSystem(Team.Enemy, config, storage, true);
            system.BeginBattle("IntegratedLateFormation", 15, "Growth");
            for (int turn = 1; turn <= 14; turn++) { system.BeginTurn(turn); system.EndTurn(turn, Facts(turn)); }
            system.BeginTurn(15); before = Facts(15); before.KnownTargetLifeId = "late-kill";
            after = before.Copy(); after.TargetHP = 0;
            token = system.BeginAction(new AIAction { ActionType = AIActionType.Attack }, before);
            system.OnEnemyKilled("late-kill", false, token); system.CompleteAction(token, true, after);
            system.EndTurn(15, after);
            var disk = JsonUtility.FromJson<AIActionLearningProfile>(File.ReadAllText(system.ProfilePath));
            long diskSequence = disk.Sequence;
            system.OnEnemyKilled("late-kill", true, token);
            var continuation = RoundTrip(system.CaptureBattleState());
            Check("a reward-only late formation correction advances profile sequence without Learn", system.Profile.Sequence > diskSequence
                && system.Profile.Find(system.LastRecord.ContextKey, system.LastRecord.ActionKey).Samples == 1
                && JsonUtility.FromJson<AIActionLearningProfile>(File.ReadAllText(system.ProfilePath)).Sequence == diskSequence);
            var resumed = new AIActionReflectionSystem(Team.Enemy, config, storage, true);
            long loadedDiskSequence = resumed.Profile.Sequence;
            resumed.BeginBattle(resume: continuation);
            var restoredEntry = resumed.Profile.Find(system.LastRecord.ContextKey, system.LastRecord.ActionKey);
            Check("an in-save corrected profile supersedes the older disk profile on resume", loadedDiskSequence == diskSequence
                && resumed.Profile.Sequence == continuation.LearningProfile.Sequence && resumed.Profile.Sequence > loadedDiskSequence
                && restoredEntry.Samples == 1 && Near(restoredEntry.CumulativeReward, 2));
            system.EndBattle(false, after);
            var completed = system.CaptureBattleState(); var interval = completed.LastDiaryInterval;
            Check("Turn15 normal to late formation ends with consistent final period statistics", completed.DiaryCount == 1
                && completed.OwnTurns == 15 && interval.Actions == 1 && interval.NormalKills == 0 && interval.FormationKills == 1
                && Near(interval.Reward, 2) && Near(interval.Breakdown.Combat, 2) && Near(completed.BattleReward, 2));
            string[] files = Directory.GetFiles(Path.Combine(storage, "Diary"), "*.txt");
            Check("Turn15 interval is replaced by one final file after late correction", files.Length == 1
                && File.ReadAllText(files[0]).Contains("結果: DEFEAT") && !File.ReadAllText(files[0]).Contains("結果: INTERVAL"));
        }
        finally { config.EnableFileDiary = fileDiary; }
    }

    static AIActionKnowledgeEntry Knowledge(string context, float value = .5f) => new AIActionKnowledgeEntry
    {
        ContextKey = context, ActionKey = "Move", LearnedValue = value, Samples = 3,
        CumulativeReward = 1.5f, AverageReward = .5f, MaxReward = .5f, MinReward = .5f, LastUseSequence = 1
    };
    static void PersistenceAndSafety(AIReflectionConfig config, string directory)
    {
        var repository = new AIReflectionSaveRepository(directory, "IntegratedLegacy", config);
        var legacy = new AIActionLearningProfile { ProfileId = "IntegratedLegacy", SchemaVersion = 1, Sequence = 1 };
        legacy.Entries.Add(Knowledge("legacy-context"));
        Directory.CreateDirectory(Path.GetDirectoryName(repository.LegacyProfilePath));
        File.WriteAllText(repository.LegacyProfilePath, JsonUtility.ToJson(legacy));
        var upgraded = repository.Load();
        Check("T36 version-one profile loads with knowledge preserved", upgraded.SchemaVersion == 2
            && upgraded.Find("legacy-context", "Move")?.Samples == 3 && Near(upgraded.Entries[0].LearnedValue, .5f));
        Check("T36 next save writes schema two and preserves the legacy source", repository.Save(upgraded)
            && RoundTrip(upgraded).SchemaVersion == 2 && File.Exists(repository.ProfilePath)
            && JsonUtility.FromJson<AIActionLearningProfile>(File.ReadAllText(repository.LegacyProfilePath)).SchemaVersion == 1);
        var partial = new AIActionLearningProfile { ProfileId = "Salvage", SchemaVersion = 1, Sequence = 1 };
        partial.Entries.Add(Knowledge("good")); partial.Entries.Add(Knowledge("nan", float.NaN));
        var invalidEnum = Knowledge("invalid-enum"); invalidEnum.FailureReasons.Add(new FailureReasonCount { Reason = (AIFailureReason)999, Count = 1 });
        partial.Entries.Add(invalidEnum); var negative = Knowledge("negative-samples"); negative.Samples = -1; partial.Entries.Add(negative);
        Check("T37 malformed entries are salvaged without discarding valid knowledge", AIReflectionMigration.TryUpgradeProfile(partial, config)
            && partial.Entries.Count == 1 && partial.Entries[0].ContextKey == "good");
        var battle = new AIReflectionBattleState { SchemaVersion = 1, BattleId = "LegacyBattle", Faction = Team.Enemy, TotalStableTurns = 2 };
        Check("battle schema migration initializes bounded helper continuation states", AIReflectionMigration.UpgradeBattle(battle, config)
            && battle.SchemaVersion == 2 && battle.EconomyRewardState.HasGrantedRecovery
            && battle.DelayedCreditState != null && battle.ArmyUtilizationState != null);
        var commander = (AICommander)FormatterServices.GetUninitializedObject(typeof(AICommander));
        var fail = typeof(AICommander).GetMethod("ReflectionFailed", Private);
        var modifier = typeof(AICommander).GetMethod("LearnedModifier", Private);
        var ensure = typeof(AICommander).GetMethod("EnsureReflection", Private);
        fail.Invoke(commander, new object[] { new IOException("isolated injected reflection failure") });
        Check("T38 reflection fault disables only its modifier boundary", Near((float)modifier.Invoke(commander,
            new object[] { new AIAction { ActionType = AIActionType.Move } }), 0) && ensure.Invoke(commander, null) == null);
        var copied = Facts(); copied.VisibleEnemyLifeIds.Add("enemy-a"); copied.SupportTargetLifeIds.Add("ally-a");
        copied.AreaEffects.Add(new AIReflectionEffectObservation { LifeId = "ally-a", Signature = 1, IsAlly = true });
        var snapshot = copied.Copy(); copied.VisibleEnemyLifeIds.Clear(); copied.SupportTargetLifeIds.Clear(); copied.AreaEffects[0].Signature = 2;
        Check("snapshot copies keep discovery support and effect evidence independent", snapshot.VisibleEnemyLifeIds.Count == 1
            && snapshot.SupportTargetLifeIds.Count == 1 && snapshot.AreaEffects[0].Signature == 1);
    }

    static void ClampsAndPerformance(AIReflectionConfig config)
    {
        var record = Record(AIActionType.Attack); record.Outcome.EnemyKills = 1; Evaluate(record, config);
        Check("T40 final action reward equals the complete breakdown", Near(record.Reward, record.RewardBreakdown.Total));
        var breakdown = new AIRewardBreakdown { Combat = 100, Artifact = 100, Survival = 100, Position = 100,
            LocalPower = 100, Objective = 100, Economy = 100, Information = 100, Defense = 100, Preparation = 100, DelayedReward = 100 };
        breakdown.Clamp(config);
        Check("T41 simultaneous positive dimensions stay inside the action clamp", breakdown.Total <= config.MaxActionReward + .0001f
            && Near(breakdown.Combat, 2) && Near(breakdown.Artifact, 1));
        breakdown = new AIRewardBreakdown { Survival = -100, Position = -100, LocalPower = -100,
            RepeatPenalty = -100, FailurePenalty = -100, IdleArmyPenalty = -100, OverproductionPenalty = -100, MissedOpportunityPenalty = -100 };
        breakdown.Clamp(config); Check("T41 simultaneous penalties respect the action floor", breakdown.Total >= config.MinActionReward - .0001f);
        var profile = new AIActionLearningProfile { ProfileId = "Clamp" }; record.Reward = 3;
        for (int i = 0; i < 500; i++) profile.Learn(record, config);
        float oldInfluence = config.LearningInfluenceMultiplier; config.LearningInfluenceMultiplier = 2;
        float modifier = profile.GetModifier(record.ContextKey, record.ActionKey, config); config.LearningInfluenceMultiplier = oldInfluence;
        Check("T42 accumulated experience and doubled influence stay bounded", modifier <= config.MaxLearnedValue && modifier >= config.MinLearnedValue);
        profile = new AIActionLearningProfile { ProfileId = "Perf" }; var keys = new string[2000];
        for (int i = 0; i < keys.Length; i++) { keys[i] = "context-" + i; profile.Entries.Add(Knowledge(keys[i])); }
        profile.RebuildIndex(); profile.GetModifier(keys[0], "Move", config);
        var watch = Stopwatch.StartNew(); float sum = 0;
        for (int i = 0; i < 100000; i++) sum += profile.GetModifier(keys[i % keys.Length], "Move", config);
        watch.Stop();
        var index = typeof(AIActionLearningProfile).GetField("index", Private).GetValue(profile);
        Check("T39 two thousand knowledge entries use a dictionary for bounded lookup", index is System.Collections.IDictionary
            && sum > 0 && watch.Elapsed.TotalMilliseconds < 1500);
        UnityEngine.Debug.Log("[AIReflectionIntegratedPerf] entries=2000 queries=100000 ms=" + watch.Elapsed.TotalMilliseconds.ToString("F2"));
    }
}
#endif
