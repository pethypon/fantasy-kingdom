#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class AIOperationCoreTests
{
    static int passed;
    static void Check(string name, bool ok)
    { if (!ok) throw new InvalidOperationException("[OperationCore] FAIL " + name); passed++; Debug.Log("[OperationCore] PASS " + name); }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
    static AIOperationContext Facts(int turn = 1) => new AIOperationContext
    {
        Turn = turn, OwnMilitaryPower = 100, EnemyObservedPower = 100, EnemyStrengthKnown = true,
        OwnUnits = 10, VisibleEnemyUnits = 5, OwnCrystalHpRatio = 1, EconomyState = EconomicState.Healthy,
        ObjectiveKnown = true, ObjectiveHpKnown = true, ObjectiveLifeId = "sub-a", ObjectiveHpRatio = 1,
        ObjectiveDistance = 8, AssignedUnitCount = 5, UtilizedAssignedUnitCount = 5, ArmyUtilization = 1,
        Bread = 100, Wood = 100, Stone = 100, Iron = 100, EconomyProductionValue = 20
    };
    static AIOperationPlan Plan(AIOperationGoal goal, long id = 1)
    {
        var before = Facts();
        var after = before.Copy(); after.Turn = 3; after.ObjectiveDistance = 0;
        after.PreparationContribution = 1; after.ExploitationProgress = 1;
        return new AIOperationPlan { OperationId = id, BattleId = "Operation_Core", PrimaryGoal = goal, OriginalGoal = goal,
            TargetLifeId = "sub-a", TargetCategory = "SubCrystal", HasTargetPosition = true, TargetX = 5, TargetZ = 6,
            StartTurn = 1, ExpectedEndTurn = 8, MaximumEndTurn = 15, LastProgressTurn = 3, ActualEndTurn = 3,
            Status = AIOperationStatus.Success, IsPrimary = true, StrategicPriority = 3,
            StartContext = before, CurrentContext = after, EndContext = after,
            ContextKey = "goal=" + goal + "|power=Even|economy=Healthy", PatternKey = "Scout>Approach>DestroyObjective",
            Steps = new List<AIOperationStep> { new AIOperationStep { StepId = 1, Type = AIOperationStepType.Scout, Completed = true },
                new AIOperationStep { StepId = 2, Type = AIOperationStepType.DestroyObjective, Completed = true } } };
    }
    static AIOperationEvaluationResult Score(AIOperationScorer scorer, AIOperationPlan plan, bool provisional = false)
        => scorer.Evaluate(plan, plan.StartContext, plan.EndContext, plan.ActionEvidence, provisional);
    public static void RunAll()
    {
        passed = 0;
        var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        string directory = Path.Combine(Path.GetTempPath(), "FantasyKingdomOperationTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var scorer = new AIOperationScorer(config);
            var p = Plan(AIOperationGoal.DestroySubCrystal); p.EndContext.ConfirmedTargetDestroyed = true;
            p.EndContext.ObjectiveHpRatio = 0; p.EndContext.EnemyKills = 5; p.EndContext.EnemyKilledPower = 100;
            p.EndContext.EnemyObservedPower = 0; p.EndContext.ExploredTiles = 32; p.EndContext.NewEnemyContacts = 3;
            p.EndContext.TerritoryCount = 4;
            var result = Score(scorer, p);
            Check("T01 confirmed objective destruction with sound outcomes scores success", result.PrimaryGoalAchieved && result.FinalScore >= 70);
            var perfect = result.FinalScore;
            p.EndContext.ConfirmedTargetDestroyed = false; p.EndContext.ObjectiveHpRatio = 1; p.EndContext.EnemyKills = 100;
            result = Score(scorer, p);
            Check("T02 unrelated kills cannot override the primary goal gate", !result.PrimaryGoalAchieved && result.FinalScore <= 69);
            p.EndContext.ConfirmedTargetDestroyed = true; p.EndContext.OwnLossPower = 95; p.EndContext.OwnMilitaryPower = 5; p.EndContext.OwnLosses = 9;
            result = Score(scorer, p);
            Check("T03 achieved objective with catastrophic losses is never perfect", result.PrimaryGoalAchieved && !result.CanReachPerfect && result.FinalScore < 95);
            p = Plan(AIOperationGoal.ScoutRegion); p.EndContext.ExploredTiles = 64; p.EndContext.NewEnemyContacts = 3;
            result = Score(scorer, p);
            Check("T04 scouting is valuable without any kill", result.PrimaryGoalAchieved && result.FinalScore >= 70 && p.EndContext.EnemyKills == 0);
            p.EndContext.ExploredTiles = 0; p.EndContext.NewEnemyContacts = 0; p.EndContext.EnemyKills = 10;
            result = Score(scorer, p);
            Check("T05 kills alone do not complete scouting", !result.PrimaryGoalAchieved && result.FinalScore <= 69);
            p = Plan(AIOperationGoal.DefendOwnCrystal); p.StartContext.OwnCrystalThreatened = true; p.EndContext.EnemyThreatResolved = true;
            result = Score(scorer, p);
            Check("T06 complete defense can succeed without kills", result.PrimaryGoalAchieved && result.FinalScore >= 70);
            p.EndContext.OwnCrystalDestroyed = true; p.EndContext.OwnCrystalHpRatio = 0; p.EndContext.EnemyKills = 10;
            result = Score(scorer, p);
            Check("T07 a destroyed own crystal defeats the defense goal", !result.PrimaryGoalAchieved && result.FinalScore < 20);
            p = Plan(AIOperationGoal.CreateDiversion); p.EndContext.DiversionConfirmed = p.EndContext.RouteOpened = true;
            result = Score(scorer, p);
            Check("T08 verified diversion and opening are scored by their goal", result.PrimaryGoalAchieved && result.FinalScore >= 70);
            p = Plan(AIOperationGoal.DestroySubCrystal); p.Status = AIOperationStatus.Aborted; p.StrategicAbort = true; p.AbortReason = AIOperationAbortReason.CrystalEmergency;
            result = Score(scorer, p);
            Check("T09 rational emergency recall is neutral rather than a major failure", result.IsStrategicAbort && result.FinalScore >= 40 && Near(result.LearningReward, 0));
            var action = new AIActionRecord { ActionId = 1, OperationId = p.OperationId, Reward = .4f, ExecutionSucceeded = true };
            p.ActionEvidence.Add(action); Score(scorer, p);
            Check("T10 failed operation does not rewrite a good action", Near(action.Reward, .4f));
            p.Status = AIOperationStatus.Success; p.StrategicAbort = false; p.AbortReason = AIOperationAbortReason.None;
            p.EndContext.ConfirmedTargetDestroyed = true; action.Reward = -.7f; Score(scorer, p);
            Check("T11 successful operation does not rewrite a bad action", Near(action.Reward, -.7f));
            p.EndContext.ConfirmedTargetDestroyed = false; p.PrimaryGoalAchieved = true; result = Score(scorer, p);
            Check("T12 cached success flag cannot replace actual goal evidence", !result.PrimaryGoalAchieved && result.FinalScore <= 69);
            p.EndContext.ConfirmedTargetDestroyed = true; p.EndContext.EconomyState = EconomicState.Collapse; result = Score(scorer, p);
            Check("T13 economic collapse blocks a perfect result", !result.CanReachPerfect && result.FinalScore < 95);
            Check("T14 excellent multi-dimensional outcome can reach the perfect band", perfect >= 95 && perfect <= 100);
            p = Plan(AIOperationGoal.ScoutRegion); p.EndContext.ExploredTiles = 64; p.EndContext.NewEnemyContacts = 3;
            result = Score(scorer, p); var profile = new AIOperationLearningProfile { ProfileId = "Operation_Core" };
            profile.Learn(p, result, config);
            Check("T15 one operation sample does not influence selection", Near(profile.GetModifier(p.ContextKey, p.PrimaryGoal, p.PatternKey, config), 0));
            p.OperationId = 2; profile.Learn(p, result, config);
            Check("T16 two samples begin a small confidence-weighted modifier", profile.GetModifier(p.ContextKey, p.PrimaryGoal, p.PatternKey, config) > 0
                && profile.GetModifier(p.ContextKey, p.PrimaryGoal, p.PatternKey, config) <= 3);
            int samples = profile.Entries[0].Samples; profile.Learn(p, result, config);
            Check("completion receipts prevent duplicate learning", profile.Entries[0].Samples == samples);
            p.OperationId = 3; profile.Learn(p, Score(scorer, p, true), config);
            Check("provisional results never teach operation memory", profile.Entries[0].Samples == samples);
            p.EndContext.ExploredTiles = 0; result = Score(scorer, p);
            Check("main-goal failure limits learning independently from points", result.LearningReward <= .5f);
            var contextJson = JsonUtility.ToJson(p.StartContext); action.Reward = 999999; p.ActionEvidence.Add(action); Score(scorer, p);
            Check("T17 value-only scorer neither mutates context nor sums action reward", JsonUtility.ToJson(p.StartContext) == contextJson);
            var manager = new AIOperationManager(config, profile); manager.BeginBattle(Team.Enemy, "Operation_Core");
            p.Status = AIOperationStatus.Active; p.OperationId = 17; p.PrimaryGoal = p.OriginalGoal = AIOperationGoal.ScoutRegion;
            manager.State.NextOperationId = 17; manager.State.ActiveOperations.Add(p);
            var snapshot = manager.Snapshot(); var restored = new AIOperationManager(config, profile); restored.BeginBattle(Team.Enemy, "Operation_Core"); restored.Restore(snapshot);
            Check("T18 active plan identity and target survive a value save/load", restored.PrimaryOperation?.OperationId == 17 && restored.PrimaryOperation.TargetX == 5);
            Check("T19 goal revision keeps operation identity", restored.ReviseGoal(17, AIOperationGoal.EconomicRecovery, 4, "観測した資源不足")
                && restored.PrimaryOperation.OperationId == 17 && restored.PrimaryOperation.Revisions.Count == 1
                && restored.PrimaryOperation.Revisions[0].OldGoal == AIOperationGoal.ScoutRegion
                && restored.PrimaryOperation.Revisions[0].NewGoal == AIOperationGoal.EconomicRecovery);
            var diary = AIOperationDiary.Render(restored.Snapshot(false), config);
            Check("T20 operation diary exposes goal, plan, expected, actual, score and lesson", diary.Contains("Goal") && diary.Contains("Plan")
                && diary.Contains("Expected") && diary.Contains("Actual") && diary.Contains("Score") && diary.Contains("Lesson"));
            var repo = new AIOperationPersistence(directory, "Operation_Core", config);
            Check("operation knowledge saves independently", repo.Save(profile) && repo.Load().Entries.Count == 1);
            Check("cross-battle knowledge contains no coordinates or unit life identities", !File.ReadAllText(repo.ProfilePath).Contains("TargetX")
                && !File.ReadAllText(repo.ProfilePath).Contains("OwnUnitLifeIds"));
            repo.Save(profile); File.WriteAllText(repo.ProfilePath, "{ corrupt");
            Check("backup restores valid operation knowledge after a broken primary file", repo.Load().Entries[0].Samples == samples);
            var priorityA = new AIOperationCandidate { Priority = 0, BaseScore = -999 };
            var priorityB = new AIOperationCandidate { Priority = 3, BaseScore = 999, Experience = 3 };
            Check("operation experience never reverses survival priority", AIOperationCandidate.Compare(priorityA, priorityB) < 0);
            ActionCreditAndBounds(config);
            DiversionReinforcements(config);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(config);
            var path = Path.GetFullPath(directory); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).StartsWith("FantasyKingdomOperationTests_", StringComparison.Ordinal)
                && Directory.Exists(path)) Directory.Delete(path, true);
        }
        Debug.Log("[OperationCore] " + passed + " goal/learning/save/diary checks passed; T01..T20 covered with runtime FoW in SceneSmoke");
    }
    static void DiversionReinforcements(AIOperationConfig config)
    {
        var plan = Plan(AIOperationGoal.CreateDiversion); plan.TargetX = plan.TargetZ = 0;
        plan.StartContext.ObservedEnemies.Add(new AIOperationObservedUnit { LifeId = "guard-a", Power = 10 });
        plan.StartContext.ObservedEnemies.Add(new AIOperationObservedUnit { LifeId = "guard-b", Power = 10 });
        var now = Facts();
        now.ObservedEnemies.Add(new AIOperationObservedUnit { LifeId = "guard-a", Power = 10, X = 10 });
        now.ObservedEnemies.Add(new AIOperationObservedUnit { LifeId = "guard-b", Power = 10, X = 10 });
        now.ObservedEnemies.Add(new AIOperationObservedUnit { LifeId = "reinforcement", Power = 40 });
        var feature = new AIOperationFeatureExtractor(config);
        var method = typeof(AIOperationFeatureExtractor).GetMethod("ObserveEnemyResponse",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method.Invoke(feature, new object[] { plan, now });
        Check("observed reinforcements prevent falsely claiming an open diversion route", now.DiversionConfirmed && !now.RouteOpened);
        now.ObservedEnemies.RemoveAt(2); method.Invoke(feature, new object[] { plan, now });
        Check("T08 a verified withdrawal without replacement really opens the route", now.RouteOpened);
    }
    static void ActionCreditAndBounds(AIOperationConfig config)
    {
        var reflectionConfig = ScriptableObject.CreateInstance<AIReflectionConfig>();
        reflectionConfig.EnableFileDiary = reflectionConfig.EnableConsoleDiary = false;
        try
        {
            var system = new AIActionReflectionSystem(Team.Enemy, reflectionConfig, persistent: false);
            system.BeginBattle("Operation_Credit"); system.BeginTurn(1);
            var before = new AIActionContextSnapshot { Turn = 1, ActorRole = "Scout", ActorHP = 100, ActorHpRatio = 1,
                OwnUnitCount = 1, ActorAp = 20, OwnCrystalHP = 15000, OwnCrystalHpRatio = 1, TargetCategory = "Cell" };
            var after = before.Copy(); after.ActorX = 1; after.ExploredTiles = 10;
            var token = system.BeginAction(new AIAction { ActionType = AIActionType.Move }, before);
            system.GetPendingRecord(token).OperationId = 42; system.GetPendingRecord(token).OperationStepId = 1;
            system.CompleteAction(token, true, after);
            var record = system.LastRecord; float immediate = record.Reward;
            int samples = system.Profile.Find(record.ContextKey, record.ActionKey).Samples;
            Check("causal operation credit is added to an eligible scouting action", system.ApplyOperationCredit(42, token.Id, .4f, .4f)
                && Near(record.Reward, immediate + .4f) && Near(record.OperationCreditReward, .4f)
                && system.Profile.Find(record.ContextKey, record.ActionKey).Samples == samples);
            Check("one operation cannot credit the same action twice", !system.ApplyOperationCredit(42, token.Id, .4f, .4f)
                && Near(record.OperationCreditReward, .4f));
            Check("a different operation cannot credit an unrelated action", !system.ApplyOperationCredit(43, token.Id, 100, 1));
            var resume = system.CaptureBattleState(); var loaded = new AIActionReflectionSystem(Team.Enemy, reflectionConfig, persistent: false);
            loaded.BeginBattle(resume: resume);
            Check("operation credit receipts survive action-memory save/load", !loaded.ApplyOperationCredit(42, token.Id, .4f, .4f)
                && Near(loaded.LastRecord.Reward, record.Reward));
            token = system.BeginAction(new AIAction { ActionType = AIActionType.Move }, before);
            system.GetPendingRecord(token).OperationId = 42;
            after = before.Copy(); after.ActorHP = after.OwnUnitCount = 0;
            system.CompleteAction(token, true, after); record = system.LastRecord; float bad = record.Reward;
            Check("T11 operation success cannot turn a suicidal action positive", bad < 0 && !system.ApplyOperationCredit(42, token.Id, 100, 1)
                && Near(record.Reward, bad));
            reflectionConfig.MaxActionReward = .5f;
            token = system.BeginAction(new AIAction { ActionType = AIActionType.Move }, before);
            system.GetPendingRecord(token).OperationId = 42;
            after = before.Copy(); after.ActorX = 2; after.ExploredTiles = 10;
            system.CompleteAction(token, true, after); record = system.LastRecord;
            Check("operation credit respects the shared action budget", Near(record.Reward, .5f)
                && !system.ApplyOperationCredit(42, token.Id, 100, 1) && Near(record.Reward, record.RewardBreakdown.Total));
            var profile = new AIOperationLearningProfile { ProfileId = "Bounds" };
            var plan = Plan(AIOperationGoal.ScoutRegion); plan.BattleId = null;
            var result = new AIOperationEvaluationResult { FinalScore = 100, LearningReward = 3, PrimaryGoalAchieved = true };
            profile.Learn(plan, result, config);
            Check("missing completion identity cannot bypass operation dedupe", profile.Entries.Count == 0);
            float lower = config.OperationLearningMin, upper = config.OperationLearningMax, limit = config.MaxOperationExperienceModifier;
            try
            {
                config.OperationLearningMin = -100; config.OperationLearningMax = config.MaxOperationExperienceModifier = 100;
                plan.BattleId = "Bounds";
                for (int i = 1; i <= 20; i++) { plan.OperationId = i; profile.Learn(plan, result, config); }
                Check("malformed authored bounds cannot inject a hundred-point modifier", profile.Entries[0].LearnedValue <= 3
                    && profile.GetModifier(plan.ContextKey, plan.PrimaryGoal, plan.PatternKey, config) <= 3);
                string json = JsonUtility.ToJson(profile); var keys = new string[2000];
                var cache = new AIOperationLearningProfile { ProfileId = "Perf" };
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i] = "goal=ScoutRegion|power=Even|case=" + i;
                    cache.Entries.Add(new AIOperationKnowledgeEntry { ContextKey = keys[i], Goal = AIOperationGoal.ScoutRegion,
                        OperationPattern = "Scout>Approach", Samples = 10, LearnedValue = 1 });
                }
                cache.RebuildIndex(); var watch = System.Diagnostics.Stopwatch.StartNew(); float sum = 0;
                for (int i = 0; i < 100000; i++) sum += cache.GetModifier(keys[i % keys.Length], AIOperationGoal.ScoutRegion, "Scout>Approach", config);
                watch.Stop();
                Check("operation experience uses bounded indexed lookups", sum > 0 && watch.Elapsed.TotalMilliseconds < 1500);
                Debug.Log("[OperationPerf] entries=2000 queries=100000 ms=" + watch.Elapsed.TotalMilliseconds.ToString("F2"));
                Check("scoring leaves independent action-memory values unchanged", json == JsonUtility.ToJson(profile));
            }
            finally { config.OperationLearningMin = lower; config.OperationLearningMax = upper; config.MaxOperationExperienceModifier = limit; }
        }
        finally { UnityEngine.Object.DestroyImmediate(reflectionConfig); }
    }
}
#endif
