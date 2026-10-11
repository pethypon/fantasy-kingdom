using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// =====================================================================
//  MLPersistence — ML全サブシステムの永続化（書き直し版）
//  保存対象: MLBrain(3ヘッド), ReplayBuffer, BehaviorPredictor, PlayerProfile
// =====================================================================
public static class MLPersistence
{
    const string MLDir = "ml";
    const string WeightsFile = "ml_weights_v2.json";
    const string BufferFile = "ml_buffer.json";
    const string PredictorFile = "ml_predictor.json";
    const string ProfileFile = "ml_profile.json";
    const int MaximumFileBytes = 64 * 1024 * 1024;
    static string testStorageRoot = null;
    [Serializable] public class MLMetadata
    {
        public int SchemaVersion;
        public string GameVersion, CreatedVersion, LastSavedVersion;
    }

    // ================================================================
    //  MLBrain重み保存データ (マルチヘッド対応)
    // ================================================================
    [Serializable]
    public class MLWeightsData : MLMetadata
    {
        public int Version = 2;
        public string SaveDate;
        public int ThreatLevel;
        public int TotalTrainingSteps;
        public int TotalMatchesTrained;
        public float AverageLoss;
        public int LossCount;
        public float LossSum;
        public float LearningRate;
        public int AdamStep;
        public float[] HeadWeights;

        // 共有層
        public float[] WShared_flat;
        public float[] BShared;
        public float[] MWShared_flat, VWShared_flat;
        public float[] MBShared, VBShared;

        // ヘッド別 (直列化)
        public List<HeadData> Heads;
    }

    [Serializable]
    public class HeadData
    {
        public float[] WHead_flat;
        public float[] BHead;
        public float[] WOut_flat;
        public float[] BOut;
        public float[] MWHead_flat, VWHead_flat;
        public float[] MWOut_flat, VWOut_flat;
        public float[] MBHead, VBHead;
        public float[] MBOut, VBOut;
    }

    [Serializable]
    public class MLBufferData : MLMetadata
    {
        public int Version = 1;
        public int TotalExperiences;
        public List<ExperienceData> Experiences = new List<ExperienceData>();
    }

    [Serializable]
    public class ExperienceData
    {
        public float[] Features;
        public float Reward;
        public float TDTarget;
        public float Priority;
        public int TurnNumber;
    }

    [Serializable]
    public class PredictorData : MLMetadata
    {
        public int Version = 1;
        public float[] Weights;
        public float RunningAccuracy;
        public int TotalPredictions;
        public int CorrectPredictions;
    }

    [Serializable]
    public class ProfileData : MLMetadata
    {
        public int Version = 1;
        public int TotalMatches;
        public float AggressionScore;
        public float RushTendency;
        public float TurtleTendency;
        public float FlankPreference;
        public float EconomyFocus;
        public float SkillReliance;
        public float PreferredAttackRange;
        public float TargetWeakestTendency;
        public float TargetHighValueTendency;
        public float CrystalFocusTendency;
        public float AverageAttackStartTurn;
        public float FormationSpread;
        public float TurnPressurePattern;
        public int TotalObservations;
        public int MatchesObserved;
    }

    // ================================================================
    //  MLBrain 保存/読込
    // ================================================================
    public static void SaveWeights(MLBrain brain, int threatLevel, int totalMatchesTrained, float avgLoss, MLTrainer trainer = null)
    {
        try
        {
            string dir = GetMLDir();


            var data = new MLWeightsData
            {
                SaveDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ThreatLevel = threatLevel,
                TotalTrainingSteps = brain.TotalTrainingSteps,
                TotalMatchesTrained = totalMatchesTrained,
                AverageLoss = avgLoss,
                LossCount = trainer?.LossCount ?? 0,
                LossSum = trainer?.LossSum ?? 0,
                LearningRate = brain.LearningRate,
                AdamStep = brain.AdamStep,
                HeadWeights = (float[])brain.HeadWeights.Clone(),

                WShared_flat = Flatten(brain.WShared),
                BShared = (float[])brain.BShared.Clone(),
                MWShared_flat = Flatten(brain.MWShared),
                VWShared_flat = Flatten(brain.VWShared),
                MBShared = (float[])brain.MBShared.Clone(),
                VBShared = (float[])brain.VBShared.Clone(),

                Heads = new List<HeadData>()
            };

            for (int h = 0; h < MLBrain.NumHeads; h++)
            {
                data.Heads.Add(new HeadData
                {
                    WHead_flat = Flatten(brain.WHead[h]),
                    BHead = (float[])brain.BHead[h].Clone(),
                    WOut_flat = Flatten(brain.WOut[h]),
                    BOut = (float[])brain.BOut[h].Clone(),
                    MWHead_flat = Flatten(brain.MWHead[h]),
                    VWHead_flat = Flatten(brain.VWHead[h]),
                    MWOut_flat = Flatten(brain.MWOut[h]),
                    VWOut_flat = Flatten(brain.VWOut[h]),
                    MBHead = (float[])brain.MBHead[h].Clone(),
                    VBHead = (float[])brain.VBHead[h].Clone(),
                    MBOut = (float[])brain.MBOut[h].Clone(),
                    VBOut = (float[])brain.VBOut[h].Clone(),
                });
            }

            Stamp(data, 2);
            if (!WriteData(WeightsFile, data, 2, d => ValidWeights(d, brain))) return;
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[MLPersistence] 重み保存完了 (v2 マルチヘッド)  脅威度={threatLevel}"); }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MLPersistence] 重み保存失敗: {e.Message}");
        }
    }

    public readonly struct TrainingStatistics
    {
        public readonly int TrainingSteps, Matches, LossCount;
        public readonly float AverageLoss, LossSum;
        public TrainingStatistics(MLWeightsData data)
        { TrainingSteps = data.TotalTrainingSteps; Matches = data.TotalMatchesTrained; AverageLoss = data.AverageLoss; LossCount = data.LossCount; LossSum = data.LossSum; }
    }
    public static bool LoadWeights(MLBrain brain) => LoadWeights(brain, out _);
    public static bool LoadWeights(MLBrain brain, out TrainingStatistics statistics)
    {
        statistics = default;
        try
        {
            if (!ReadData(WeightsFile, 2, d => ValidWeights(d, brain), out MLWeightsData data)) return false;

            Unflatten(data.WShared_flat, brain.WShared);
            Array.Copy(data.BShared, brain.BShared, brain.BShared.Length);
            if (data.MWShared_flat != null) Unflatten(data.MWShared_flat, brain.MWShared);
            if (data.VWShared_flat != null) Unflatten(data.VWShared_flat, brain.VWShared);
            if (data.MBShared != null) Array.Copy(data.MBShared, brain.MBShared, brain.MBShared.Length);
            if (data.VBShared != null) Array.Copy(data.VBShared, brain.VBShared, brain.VBShared.Length);

            for (int h = 0; h < MLBrain.NumHeads && h < data.Heads.Count; h++)
            {
                var hd = data.Heads[h];
                Unflatten(hd.WHead_flat, brain.WHead[h]);
                Array.Copy(hd.BHead, brain.BHead[h], brain.BHead[h].Length);
                Unflatten(hd.WOut_flat, brain.WOut[h]);
                Array.Copy(hd.BOut, brain.BOut[h], brain.BOut[h].Length);
                if (hd.MWHead_flat != null) Unflatten(hd.MWHead_flat, brain.MWHead[h]);
                if (hd.VWHead_flat != null) Unflatten(hd.VWHead_flat, brain.VWHead[h]);
                if (hd.MWOut_flat != null) Unflatten(hd.MWOut_flat, brain.MWOut[h]);
                if (hd.VWOut_flat != null) Unflatten(hd.VWOut_flat, brain.VWOut[h]);
                if (hd.MBHead != null) Array.Copy(hd.MBHead, brain.MBHead[h], brain.MBHead[h].Length);
                if (hd.VBHead != null) Array.Copy(hd.VBHead, brain.VBHead[h], brain.VBHead[h].Length);
                if (hd.MBOut != null) Array.Copy(hd.MBOut, brain.MBOut[h], brain.MBOut[h].Length);
                if (hd.VBOut != null) Array.Copy(hd.VBOut, brain.VBOut[h], brain.VBOut[h].Length);
            }

            brain.AdamStep = data.AdamStep;
            brain.RestoreTrainingSteps(data.TotalTrainingSteps);
            statistics = new TrainingStatistics(data);
            brain.LearningRate = data.LearningRate;
            if (data.HeadWeights != null && data.HeadWeights.Length == MLBrain.NumHeads)
                Array.Copy(data.HeadWeights, brain.HeadWeights, MLBrain.NumHeads);

            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[MLPersistence] 重み読込完了 (v2)  脅威度={data.ThreatLevel}  " +
                      $"学習ステップ={data.TotalTrainingSteps}  保存日={data.SaveDate}"); }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[MLPersistence] 重み読込失敗: {e.Message}");
            return false;
        }
    }

    // ================================================================
    //  ReplayBuffer 保存/読込
    // ================================================================
    public static void SaveBuffer(MLReplayBuffer buffer)
    {
        try
        {
            string dir = GetMLDir();
            var recent = buffer.GetRecentExperiences(1000);
            var data = new MLBufferData { TotalExperiences = buffer.TotalExperiences };
            foreach (var exp in recent)
            {
                data.Experiences.Add(new ExperienceData
                {
                    Features = exp.Features,
                    Reward = exp.Reward,
                    TDTarget = exp.TDTarget,
                    Priority = exp.Priority,
                    TurnNumber = exp.TurnNumber
                });
            }
            Stamp(data, 1);
            if (!WriteData(BufferFile, data, 1, d => ValidBuffer(d, buffer.Capacity))) return;
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[MLPersistence] バッファ保存: {recent.Count}件"); }
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] バッファ保存失敗: {e.Message}"); }
    }

    public static void LoadBuffer(MLReplayBuffer buffer)
    {
        try
        {
            if (!ReadData(BufferFile, 1, d => ValidBuffer(d, buffer.Capacity), out MLBufferData data)) return;
            var exps = new List<MLReplayBuffer.Experience>();
            foreach (var ed in data.Experiences)
                exps.Add(new MLReplayBuffer.Experience
                {
                    Features = ed.Features, Reward = ed.Reward,
                    TDTarget = ed.TDTarget, Priority = ed.Priority,
                    TurnNumber = ed.TurnNumber
                });
            buffer.LoadExperiences(exps);
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[MLPersistence] バッファ読込: {exps.Count}件"); }
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] バッファ読込失敗: {e.Message}"); }
    }

    // ================================================================
    //  BehaviorPredictor 保存/読込
    // ================================================================
    public static void SaveBehaviorPredictor(BehaviorPredictor pred)
    {
        try
        {
            var data = new PredictorData
            {
                Weights = pred.GetWeightsFlat(),
                RunningAccuracy = pred.SaveRunningAccuracy,
                TotalPredictions = pred.SaveTotalPredictions,
                CorrectPredictions = pred.SaveCorrectPredictions
            };
            Stamp(data, 1);
            if (!WriteData(PredictorFile, data, 1, ValidPredictor)) return;
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[MLPersistence] 予測器保存: 精度={pred.Accuracy:F2}  予測数={pred.TotalPredictions}"); }
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] 予測器保存失敗: {e.Message}"); }
    }

    public static void LoadBehaviorPredictor(BehaviorPredictor pred)
    {
        try
        {
            if (!ReadData(PredictorFile, 1, ValidPredictor, out PredictorData data)) return;
            pred.LoadWeightsFlat(data.Weights);
            pred.SaveRunningAccuracy = data.RunningAccuracy;
            pred.SaveTotalPredictions = data.TotalPredictions;
            pred.SaveCorrectPredictions = data.CorrectPredictions;
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[MLPersistence] 予測器読込: 精度={pred.Accuracy:F2}"); }
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] 予測器読込失敗: {e.Message}"); }
    }

    // ================================================================
    //  PlayerProfile 保存/読込
    // ================================================================
    public static void SavePlayerProfile(PlayerProfiler profiler)
    {
        try
        {
            var p = profiler.Profile;
            var data = new ProfileData
            {
                TotalMatches = profiler.SaveTotalMatches,
                AggressionScore = p.AggressionScore,
                RushTendency = p.RushTendency,
                TurtleTendency = p.TurtleTendency,
                FlankPreference = p.FlankPreference,
                EconomyFocus = p.EconomyFocus,
                SkillReliance = p.SkillReliance,
                PreferredAttackRange = p.PreferredAttackRange,
                TargetWeakestTendency = p.TargetWeakestTendency,
                TargetHighValueTendency = p.TargetHighValueTendency,
                CrystalFocusTendency = p.CrystalFocusTendency,
                AverageAttackStartTurn = p.AverageAttackStartTurn,
                FormationSpread = p.FormationSpread,
                TurnPressurePattern = p.TurnPressurePattern,
                TotalObservations = p.TotalObservations,
                MatchesObserved = p.MatchesObserved,
            };
            Stamp(data, 1);
            if (!WriteData(ProfileFile, data, 1, ValidPlayerProfile)) return;
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[MLPersistence] プロファイル保存: 攻撃性={p.AggressionScore:F2} " +
                      $"信頼度={p.Confidence:F2}  観測試合={p.MatchesObserved}"); }
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] プロファイル保存失敗: {e.Message}"); }
    }

    public static void LoadPlayerProfile(PlayerProfiler profiler)
    {
        try
        {
            if (!ReadData(ProfileFile, 1, ValidPlayerProfile, out ProfileData data)) return;

            profiler.SaveTotalMatches = data.TotalMatches;
            var p = profiler.GetProfileRef();
            p.AggressionScore = data.AggressionScore;
            p.RushTendency = data.RushTendency;
            p.TurtleTendency = data.TurtleTendency;
            p.FlankPreference = data.FlankPreference;
            p.EconomyFocus = data.EconomyFocus;
            p.SkillReliance = data.SkillReliance;
            p.PreferredAttackRange = data.PreferredAttackRange;
            p.TargetWeakestTendency = data.TargetWeakestTendency;
            p.TargetHighValueTendency = data.TargetHighValueTendency;
            p.CrystalFocusTendency = data.CrystalFocusTendency;
            p.AverageAttackStartTurn = data.AverageAttackStartTurn;
            p.FormationSpread = data.FormationSpread;
            p.TurnPressurePattern = data.TurnPressurePattern;
            p.TotalObservations = data.TotalObservations;
            p.MatchesObserved = data.MatchesObserved;
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[MLPersistence] プロファイル読込: 攻撃性={p.AggressionScore:F2}  " +
                      $"観測試合={p.MatchesObserved}"); }
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] プロファイル読込失敗: {e.Message}"); }
    }

    // ================================================================
    //  ユーティリティ
    // ================================================================
    static string GetMLDir()
    {
        string dir = Path.Combine(testStorageRoot ?? Application.persistentDataPath, MLDir);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    static void Stamp(MLMetadata data, int version)
    {
        data.SchemaVersion = version; data.GameVersion = data.LastSavedVersion = FKBuildInfo.Current.GameVersion;
        if (string.IsNullOrEmpty(data.CreatedVersion)) data.CreatedVersion = data.GameVersion;
    }
    [Serializable] sealed class Header { public int Version = 0, SchemaVersion = 0; }
    static AtomicSaveStorage.Validation ValidateJson<T>(string json, int version, Func<T, bool> valid, out string reason) where T : class
    {
        var header = JsonUtility.FromJson<Header>(json);
        if (header != null && (header.Version > version || header.SchemaVersion > version))
        { reason = "新しい形式の ML データは読み込み・上書きしません。"; return AtomicSaveStorage.Validation.FutureSchema; }
        var data = JsonUtility.FromJson<T>(json);
        if (header == null || header.Version != version || header.SchemaVersion != 0 && header.SchemaVersion != version || data == null || !valid(data))
        { reason = "ML データの形式、配列の大きさ、数値が不正です。"; return AtomicSaveStorage.Validation.Invalid; }
        reason = ""; return AtomicSaveStorage.Validation.Valid;
    }
    static bool ReadData<T>(string file, int version, Func<T, bool> valid, out T data) where T : class
    {
        data = null; string path = Path.Combine(GetMLDir(), file); bool future = false;
        AtomicSaveStorage.Validation Validate(string json, out string reason)
        {
            var result = ValidateJson(json, version, valid, out reason);
            future |= result == AtomicSaveStorage.Validation.FutureSchema; return result;
        }
        foreach (string candidate in new[] { path, path + ".bak1", path + ".bak2", path + ".bak3" })
        {
            if (!AtomicSaveStorage.HasAny(candidate)) continue;
            if (AtomicSaveStorage.TryRead(candidate, MaximumFileBytes, Validate, out string json, out string reason, out _))
            {
                AIPerformanceCounters.MarkFileRead(); data = JsonUtility.FromJson<T>(json);
                if (data is MLMetadata metadata && string.IsNullOrEmpty(metadata.CreatedVersion)) metadata.CreatedVersion = "legacy";
                return true;
            }
            Debug.LogWarning("[MLPersistence] " + reason);
            if (future) return false;
        }
        return false;
    }
    static bool WriteData<T>(string file, T data, int version, Func<T, bool> valid) where T : class
    {
        string path = Path.Combine(GetMLDir(), file);
        AtomicSaveStorage.Validation Validate(string json, out string reason) => ValidateJson(json, version, valid, out reason);
        // Keep old backup names readable and protect their future schemas before any generation is changed.
        for (int i = 1; i <= 3; i++)
        {
            string backup = path + ".bak" + i;
            if (!File.Exists(backup)) continue;
            if (!AtomicSaveStorage.ReadBounded(backup, MaximumFileBytes, out string old, out string reason)
                || Validate(old, out reason) == AtomicSaveStorage.Validation.FutureSchema)
            { Debug.LogWarning("[MLPersistence] " + reason); return false; }
        }
        bool saved = AtomicSaveStorage.TryWrite(path, JsonUtility.ToJson(data), MaximumFileBytes, Validate, out string failure);
        if (saved) AIPerformanceCounters.MarkFileWrite(); else Debug.LogWarning("[MLPersistence] " + failure);
        return saved;
    }
    static bool Vector(float[] values, int length, bool optional = false)
    {
        if (values == null) return optional;
        if (values.Length != length) return false;
        foreach (float value in values) if (float.IsNaN(value) || float.IsInfinity(value)) return false;
        return true;
    }
    static bool Number(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool ValidWeights(MLWeightsData d, MLBrain brain)
    {
        if (d == null || d.TotalTrainingSteps < 0 || d.TotalMatchesTrained < 0 || d.AdamStep < 0 || !Number(d.AverageLoss)
            || d.LossCount < 0 || !Number(d.LossSum)
            || !Number(d.LearningRate) || d.LearningRate < 0 || !Vector(d.WShared_flat, brain.WShared.Length)
            || !Vector(d.BShared, brain.BShared.Length) || !Vector(d.HeadWeights, MLBrain.NumHeads, true)
            || !Vector(d.MWShared_flat, brain.MWShared.Length, true) || !Vector(d.VWShared_flat, brain.VWShared.Length, true)
            || !Vector(d.MBShared, brain.MBShared.Length, true) || !Vector(d.VBShared, brain.VBShared.Length, true)
            || d.Heads == null || d.Heads.Count != MLBrain.NumHeads) return false;
        for (int h = 0; h < MLBrain.NumHeads; h++)
        {
            var hd = d.Heads[h];
            if (hd == null || !Vector(hd.WHead_flat, brain.WHead[h].Length) || !Vector(hd.BHead, brain.BHead[h].Length)
                || !Vector(hd.WOut_flat, brain.WOut[h].Length) || !Vector(hd.BOut, brain.BOut[h].Length)
                || !Vector(hd.MWHead_flat, brain.MWHead[h].Length, true) || !Vector(hd.VWHead_flat, brain.VWHead[h].Length, true)
                || !Vector(hd.MWOut_flat, brain.MWOut[h].Length, true) || !Vector(hd.VWOut_flat, brain.VWOut[h].Length, true)
                || !Vector(hd.MBHead, brain.MBHead[h].Length, true) || !Vector(hd.VBHead, brain.VBHead[h].Length, true)
                || !Vector(hd.MBOut, brain.MBOut[h].Length, true) || !Vector(hd.VBOut, brain.VBOut[h].Length, true)) return false;
        }
        return true;
    }
    static bool ValidBuffer(MLBufferData data, int capacity)
    {
        if (data == null || data.TotalExperiences < 0 || data.Experiences == null || data.Experiences.Count > capacity) return false;
        foreach (var e in data.Experiences)
            if (e == null || !Vector(e.Features, MLBrain.InputSize) || !Number(e.Reward) || !Number(e.TDTarget)
                || !Number(e.Priority) || e.Priority < 0 || e.TurnNumber < 0) return false;
        return true;
    }
    static bool ValidPredictor(PredictorData data) => data != null && Vector(data.Weights, 64 * 48 + 48 + 48 * 12 + 12)
        && Number(data.RunningAccuracy) && data.RunningAccuracy >= 0 && data.RunningAccuracy <= 1
        && data.TotalPredictions >= 0 && data.CorrectPredictions >= 0 && data.CorrectPredictions <= data.TotalPredictions;
    static bool ValidPlayerProfile(ProfileData data) => data != null && data.TotalMatches >= 0 && data.TotalObservations >= 0 && data.MatchesObserved >= 0
        && Number(data.AggressionScore) && Number(data.RushTendency) && Number(data.TurtleTendency) && Number(data.FlankPreference)
        && Number(data.EconomyFocus) && Number(data.SkillReliance) && Number(data.PreferredAttackRange) && Number(data.TargetWeakestTendency)
        && Number(data.TargetHighValueTendency) && Number(data.CrystalFocusTendency) && Number(data.AverageAttackStartTurn)
        && Number(data.FormationSpread) && Number(data.TurnPressurePattern);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    internal static IDisposable UseTestStorageRoot(string root)
    {
        string full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Test root must be temporary.");
        return new StorageScope(full);
    }
    sealed class StorageScope : IDisposable
    {
        readonly string previous; bool disposed;
        public StorageScope(string value) { previous = testStorageRoot; testStorageRoot = value; }
        public void Dispose() { if (!disposed) { testStorageRoot = previous; disposed = true; } }
    }
#endif

    static float[] Flatten(float[,] mat)
    {
        int r = mat.GetLength(0), c = mat.GetLength(1);
        var flat = new float[r * c];
        int idx = 0;
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
                flat[idx++] = mat[i, j];
        return flat;
    }

    static void Unflatten(float[] flat, float[,] mat)
    {
        int r = mat.GetLength(0), c = mat.GetLength(1);
        if (flat == null || flat.Length != r * c) return;
        int idx = 0;
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
                mat[i, j] = flat[idx++];
    }

    public static bool HasSavedWeights()
        => File.Exists(Path.Combine(Application.persistentDataPath, MLDir, WeightsFile));
}
