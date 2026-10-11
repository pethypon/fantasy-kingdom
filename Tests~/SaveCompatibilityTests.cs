#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Frozen old formats and fault injection use isolated OS-temp storage only.</summary>
public static class SaveCompatibilityTests
{
    static int passed;
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(string label, bool ok)
    { if (!ok) throw new InvalidOperationException("[SaveCompatibility] FAIL " + label); passed++; Debug.Log("[SaveCompatibility] PASS " + label); }
    static SaveSystem.GameSaveData Current()
    {
        return new SaveSystem.GameSaveData
        {
            Turn = 8, ThreatLevel = 4, MapWidth = 35, MapDepth = 35,
            PCPx = 2, PCPy = 1, PCPz = 2, ECPx = 30, ECPy = 1, ECPz = 30,
            HasExplicitMapSnapshot = true,
            PlayerAP = new SaveSystem.APSaveData { Current = 12, Reset = 30 },
            EnemyAP = new SaveSystem.APSaveData { Current = 8, Reset = 30 },
            Timer = new SaveSystem.TimerSaveData { TurnTimeRemaining = 43, TurnTimeLimit = 180, PlayerTotalTime = 36000, EnemyTotalTime = 35000 },
            AI = new SaveSystem.AISaveData { HasDeterministicSeed = true, RngSeed = 56192, AITurnCount = 7 }
        };
    }
    static IDisposable Isolated(string root) => (IDisposable)typeof(SaveSystem).GetMethod("UseTestStorageRoot", Private).Invoke(null, new object[] { root });
    public static void RunAll()
    {
        passed = 0;
        string root = Path.Combine(Path.GetTempPath(), "FK_SaveCompatibility_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (Isolated(root))
            {
                GoldenFiles(root); RoundTrips(root); InvalidData(); AtomicFaults(root); Profiles(root);
                LearningRepositories(root); MachineLearning(root); PlayerModel(root);
            }
        }
        finally
        {
            // Exact verified test root, never a user profile directory.
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
        Debug.Log("[SaveCompatibility] " + passed + " migration/atomic/recovery/future/schema checks passed");
    }
    static void GoldenFiles(string root)
    {
        Check("complete migration chain", SaveSystem.ValidateMigrationChain(out _));
        string fixtures = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tests~", "Data", "Saves"));
        string path = Path.Combine(root, "saves", "save_0.json"); Directory.CreateDirectory(Path.GetDirectoryName(path));
        string[] names = { "legacy", "base", "growth", "crystal", "reflection", "exploration", "operations", "checkpoint" };
        for (int version = 0; version <= 7; version++)
        {
            string original = File.ReadAllText(Path.Combine(fixtures, "v" + version + "_" + names[version] + ".json"));
            File.WriteAllText(path, original);
            Check("frozen v" + version + " migrates", SaveSystem.TryLoadGame(0, out var data, out string reason));
            Check("v" + version + " current schema and checkpoint", data.Version == SaveSystem.CurrentSaveVersion && data.SchemaVersion == SaveSystem.CurrentSaveVersion && data.Checkpoint == "player_turn");
            Check("v" + version + " crystal upgrade exactly once", data.Units[0].HP == 5700 && data.Units[0].MaxHP == 6000);
            Check("v" + version + " killed crystal stays killed", data.Units[1].HP == 0 && data.Units[1].MaxHP == 6000);
            Check("v" + version + " legacy missing maxHP repaired", data.Units[2].MaxHP == 20);
            Check("v" + version + " no invented knowledge or deterministic seed", data.AI.Reflection == null && data.AI.Operations == null && !data.AI.HasDeterministicSeed);
            Check("v" + version + " source remains unchanged", File.ReadAllText(path) == original);
            typeof(SaveSystem).GetMethod("RunMigrations", Private).Invoke(null, new object[] { data });
            Check("v" + version + " migration is idempotent", data.Units[0].MaxHP == 6000 && data.Units[0].HP == 5700);
            Check("v" + version + " save current", SaveSystem.TrySaveGame(0, data, out reason));
            Check("v" + version + " restart reload keeps phase", SaveSystem.TryLoadGame(0, out var reloaded, out reason)
                && reloaded.Turn == 8 && reloaded.PlayerNationExtra.TurnsAlive == (version >= 4 ? 23 : 7));
            File.Delete(path); if (File.Exists(path + ".previous")) File.Delete(path + ".previous"); if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
    }
    static void RoundTrips(string root)
    {
        var data = Current(); string path = Path.Combine(root, "saves", "save_0.json");
        Check("newest save succeeds", SaveSystem.TrySaveGame(0, data, out _));
        Check("caller snapshot is not mutated", data.SaveDate == null && data.GameVersion == null);
        Check("actual seed and zero map seed round trip", SaveSystem.TryLoadGame(0, out var loaded, out _)
            && loaded.AI.RngSeed == 56192 && loaded.AI.HasDeterministicSeed && loaded.MapSeedX == 0 && loaded.HasExplicitMapSnapshot);
        Check("turn/AP/timer round trip", loaded.Turn == 8 && loaded.PlayerAP.Current == 12 && loaded.Timer.TurnTimeRemaining == 43);
        Check("metadata recorded", !string.IsNullOrEmpty(loaded.GameVersion) && loaded.CreatedVersion == loaded.LastSavedVersion);
        data.Turn = 9; Check("second save", SaveSystem.TrySaveGame(0, data, out _));
        data.Turn = 10; Check("third save", SaveSystem.TrySaveGame(0, data, out _));
        Check("three generations exist", File.Exists(path) && File.Exists(path + ".previous") && File.Exists(path + ".bak"));
        File.WriteAllText(path, "{broken");
        Check("broken primary recovers previous", SaveSystem.TryLoadGame(0, out loaded, out string reason) && loaded.Turn == 9 && reason.Length > 0);
        File.WriteAllText(path + ".previous", "{broken");
        Check("two broken generations recover backup", SaveSystem.TryLoadGame(0, out loaded, out reason) && loaded.Turn == 8 && reason.Length > 0);
        File.WriteAllText(path + ".bak", "{broken");
        Check("all generations broken explicitly fail", !SaveSystem.TryLoadGame(0, out loaded, out reason) && loaded == null && reason.Length > 0);
        File.Delete(path); File.Delete(path + ".previous"); File.Delete(path + ".bak");
        Check("fresh slot after isolated broken test", SaveSystem.TrySaveGame(0, data, out _));
        string future = "{\"Version\":99,\"SchemaVersion\":99,\"FuturePayload\":\"keep\"}";
        File.WriteAllText(path, future);
        Check("future format fails load", !SaveSystem.TryLoadGame(0, out _, out reason) && reason.Length > 0);
        Check("future file fails overwrite", !SaveSystem.TrySaveGame(0, data, out reason) && File.ReadAllText(path) == future);
        data.Version = 99; Check("future DTO refuses downgrade", !SaveSystem.TrySaveGame(1, data, out reason) && !SaveSystem.HasSaveData(1));
    }
    static void InvalidData()
    {
        Check("invalid slot read", !SaveSystem.TryLoadGame(-1, out _, out _));
        Check("invalid slot save", !SaveSystem.TrySaveGame(3, Current(), out _));
        var data = Current(); data.PlayerResources.Wood = -1; Check("negative resource rejected", !SaveSystem.ValidateGameData(data, out _));
        data = Current(); data.MapSeedX = float.NaN; Check("nonfinite seed rejected", !SaveSystem.ValidateGameData(data, out _));
        data = Current(); data.MapWidth = int.MaxValue; Check("oversized map rejected", !SaveSystem.ValidateGameData(data, out _));
        data = Current(); data.Timer.TurnTimeRemaining = float.PositiveInfinity; Check("nonfinite timer rejected", !SaveSystem.ValidateGameData(data, out _));
        data = Current(); data.Checkpoint = "enemy_action"; Check("unsupported checkpoint rejected", !SaveSystem.ValidateGameData(data, out _));
        data = Current(); data.AI.RandomAlgorithm = "future-random"; Check("unknown RNG algorithm rejected", !SaveSystem.ValidateGameData(data, out _));
        data = Current(); data.SchemaVersion = 6; Check("schema aliases must agree", !SaveSystem.ValidateGameData(data, out _));
    }
    static AtomicSaveStorage.Validation SmallJson(string json, out string reason)
    {
        reason = "invalid";
        if (json == "{\"v\":99}") { reason = "future"; return AtomicSaveStorage.Validation.FutureSchema; }
        if (json == "{\"v\":1}" || json == "{\"v\":2}") { reason = ""; return AtomicSaveStorage.Validation.Valid; }
        return AtomicSaveStorage.Validation.Invalid;
    }
    static void AtomicFaults(string root)
    {
        string path = Path.Combine(root, "atomic.json");
        Check("atomic first creation", AtomicSaveStorage.TryWrite(path, "{\"v\":1}", 1024, SmallJson, out _));
        var injection = typeof(AtomicSaveStorage).GetField("ReplaceForTests", Private);
        injection.SetValue(null, (Action<string, string, string>)((a, b, c) => throw new PlatformNotSupportedException("Injected unsupported replace")));
        try
        {
            Check("unsupported replacement reports failure", !AtomicSaveStorage.TryWrite(path, "{\"v\":2}", 1024, SmallJson, out _));
            Check("unsupported replacement preserves exact old primary", File.ReadAllText(path) == "{\"v\":1}");
            Check("failed write leaves no temporary official save", !File.Exists(path + ".tmp"));
        }
        finally { injection.SetValue(null, null); }
        File.WriteAllText(path + ".previous", "{\"v\":99}");
        Check("future previous generation also protected", !AtomicSaveStorage.TryWrite(path, "{\"v\":2}", 1024, SmallJson, out _)
            && File.ReadAllText(path + ".previous") == "{\"v\":99}");
        Check("bounded read refuses oversize", !AtomicSaveStorage.ReadBounded(path, 2, out _, out _));
        Check("invalid new JSON never touches current", !AtomicSaveStorage.TryWrite(path, "{broken", 1024, SmallJson, out _) && File.ReadAllText(path) == "{\"v\":1}");
    }
    static void Profiles(string root)
    {
        Check("profile atomic creation", SaveSystem.TrySaveProfile(new SaveSystem.ProfileData { ThreatLevel = 15, TotalWins = 5 }, out _));
        Check("profile counts preserved", SaveSystem.TryLoadProfile(out var profile, out _) && profile.ThreatLevel == 15 && profile.TotalWins == 5);
        string path = Path.Combine(root, "saves", "profile.json"); string future = "{\"SchemaVersion\":99,\"ThreatLevel\":70}";
        File.WriteAllText(path, future);
        Check("future profile load protected", !SaveSystem.TryLoadProfile(out _, out _));
        Check("future profile write protected", !SaveSystem.TrySaveProfile(new SaveSystem.ProfileData(), out _) && File.ReadAllText(path) == future);
    }
    static void LearningRepositories(string root)
    {
        var reflectionConfig = ScriptableObject.CreateInstance<AIReflectionConfig>();
        var operationConfig = ScriptableObject.CreateInstance<AIOperationConfig>(); operationConfig.EnablePersistentLearning = true;
        try
        {
            var cleanReflection = new AIReflectionSaveRepository(root, "MetadataReflection", reflectionConfig);
            var reflectionProfile = new AIActionLearningProfile { ProfileId = "MetadataReflection" };
            Check("reflection metadata saved without mutating learning snapshot", cleanReflection.Save(reflectionProfile)
                && reflectionProfile.GameVersion == null && !string.IsNullOrEmpty(cleanReflection.Load().GameVersion));
            var cleanOperation = new AIOperationPersistence(root, "MetadataOperation", operationConfig);
            var operationProfile = new AIOperationLearningProfile { ProfileId = "MetadataOperation" };
            Check("operation metadata saved without mutating learning snapshot", cleanOperation.Save(operationProfile)
                && operationProfile.GameVersion == null && !string.IsNullOrEmpty(cleanOperation.Load().GameVersion));
            var reflection = new AIReflectionSaveRepository(root, "FutureReflection", reflectionConfig);
            Directory.CreateDirectory(Path.GetDirectoryName(reflection.ProfilePath));
            string future = "{\"SchemaVersion\":99,\"ProfileId\":\"FutureReflection\",\"FutureKnowledge\":\"preserve\"}";
            File.WriteAllText(reflection.ProfilePath, future);
            Check("reflection future read gives transient empty baseline", reflection.Load().Entries.Count == 0);
            Check("reflection future file is not archived", File.ReadAllText(reflection.ProfilePath) == future
                && !File.Exists(reflection.ProfilePath + ".corrupt.latest"));
            Check("reflection future cannot be overwritten by fresh profile", !reflection.Save(new AIActionLearningProfile { ProfileId = "FutureReflection" })
                && File.ReadAllText(reflection.ProfilePath) == future);
            var operation = new AIOperationPersistence(root, "FutureOperation", operationConfig);
            Directory.CreateDirectory(Path.GetDirectoryName(operation.ProfilePath));
            future = "{\"SchemaVersion\":99,\"ProfileId\":\"FutureOperation\",\"FutureKnowledge\":\"preserve\"}";
            File.WriteAllText(operation.ProfilePath, future);
            Check("operation future read gives transient empty baseline", operation.Load().Entries.Count == 0);
            Check("operation future file is not archived", File.ReadAllText(operation.ProfilePath) == future
                && !File.Exists(operation.ProfilePath + ".corrupt.latest"));
            Check("operation future cannot be overwritten by fresh profile", !operation.Save(new AIOperationLearningProfile { ProfileId = "FutureOperation" })
                && File.ReadAllText(operation.ProfilePath) == future);
        }
        finally { UnityEngine.Object.DestroyImmediate(reflectionConfig); UnityEngine.Object.DestroyImmediate(operationConfig); }
    }
    static void MachineLearning(string root)
    {
        using var scope = (IDisposable)typeof(MLPersistence).GetMethod("UseTestStorageRoot", Private).Invoke(null, new object[] { root });
        var brain = new MLBrain(42); brain.RestoreTrainingSteps(123);
        var trainer = new MLTrainer(brain, new MLReplayBuffer()); trainer.RestoreStatistics(7, .1f, 10, 1);
        MLPersistence.SaveWeights(brain, 20, 7, .1f, trainer);
        string path = Path.Combine(root, "ml", "ml_weights_v2.json");
        Check("ML weights created atomically with metadata", File.Exists(path)
            && JsonUtility.FromJson<MLPersistence.MLWeightsData>(File.ReadAllText(path)).SchemaVersion == 2);
        var loaded = new MLBrain(101);
        Check("ML exact weight round trip", MLPersistence.LoadWeights(loaded, out var statistics) && loaded.WShared[0, 0] == brain.WShared[0, 0]);
        var restoredTrainer = new MLTrainer(loaded, new MLReplayBuffer()); restoredTrainer.RestoreStatistics(statistics.Matches, statistics.AverageLoss, statistics.LossCount, statistics.LossSum);
        Check("ML training steps/matches/loss aggregate survive restart", loaded.TotalTrainingSteps == 123 && restoredTrainer.TotalMatchesTrained == 7
            && restoredTrainer.AverageLoss == .1f && restoredTrainer.LossCount == 10 && restoredTrainer.LossSum == 1);
        var broken = JsonUtility.FromJson<MLPersistence.MLWeightsData>(File.ReadAllText(path)); broken.Heads[0].BHead = new float[0];
        File.WriteAllText(path, JsonUtility.ToJson(broken));
        var intact = new MLBrain(909); float before = intact.WShared[0, 0];
        Check("ML invalid head shape rejected before any shared weights change", !MLPersistence.LoadWeights(intact) && intact.WShared[0, 0] == before);
        string future = "{\"Version\":99,\"SchemaVersion\":99,\"FutureWeights\":\"keep\"}";
        File.WriteAllText(path, future);
        Check("ML future weight read rejected", !MLPersistence.LoadWeights(intact));
        MLPersistence.SaveWeights(brain, 20, 8, .1f);
        Check("ML future weight write refused", File.ReadAllText(path) == future);
        var predictor = new BehaviorPredictor(42); MLPersistence.SaveBehaviorPredictor(predictor);
        var predictorCopy = new BehaviorPredictor(303); MLPersistence.LoadBehaviorPredictor(predictorCopy);
        Check("predictor exact weights round trip", predictorCopy.GetWeightsFlat()[0] == predictor.GetWeightsFlat()[0]);
        string predictorPath = Path.Combine(root, "ml", "ml_predictor.json"); File.WriteAllText(predictorPath, "{\"Version\":99}");
        MLPersistence.SaveBehaviorPredictor(predictor);
        Check("predictor future format protected", File.ReadAllText(predictorPath) == "{\"Version\":99}");
        var profiler = new PlayerProfiler(); MLPersistence.SavePlayerProfile(profiler);
        string profilePath = Path.Combine(root, "ml", "ml_profile.json"); File.WriteAllText(profilePath, "{\"Version\":99}");
        MLPersistence.SavePlayerProfile(profiler);
        Check("ML player profile future format protected", File.ReadAllText(profilePath) == "{\"Version\":99}");
        var buffer = new MLReplayBuffer(5000); MLPersistence.SaveBuffer(buffer);
        string bufferPath = Path.Combine(root, "ml", "ml_buffer.json"); File.WriteAllText(bufferPath, "{\"Version\":99}");
        MLPersistence.SaveBuffer(buffer);
        Check("ML buffer future format protected", File.ReadAllText(bufferPath) == "{\"Version\":99}");
    }
    static void PlayerModel(string root)
    {
        string path = Path.Combine(root, "PlayerModel.json"), future = "{\"Version\":99,\"Matches\":400}";
        File.WriteAllText(path, future); var model = new AIPlayerModel(path);
        typeof(AIPlayerModel).GetField("observations", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(model, 1);
        Check("tendency model future write refused after actual observation count", !model.CompleteMatch() && File.ReadAllText(path) == future);
        Check("tendency model future file is not archived", !File.Exists(path + ".corrupt.latest"));
    }
}
#endif
