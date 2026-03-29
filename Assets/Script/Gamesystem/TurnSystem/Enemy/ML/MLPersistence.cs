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
    const int MaxBackups = 3;

    // ================================================================
    //  MLBrain重み保存データ (マルチヘッド対応)
    // ================================================================
    [Serializable]
    public class MLWeightsData
    {
        public int Version = 2;
        public string SaveDate;
        public int ThreatLevel;
        public int TotalTrainingSteps;
        public int TotalMatchesTrained;
        public float AverageLoss;
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
    public class MLBufferData
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
    public class PredictorData
    {
        public int Version = 1;
        public float[] Weights;
        public float RunningAccuracy;
        public int TotalPredictions;
        public int CorrectPredictions;
    }

    [Serializable]
    public class ProfileData
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
    public static void SaveWeights(MLBrain brain, int threatLevel, int totalMatchesTrained, float avgLoss)
    {
        try
        {
            string dir = GetMLDir();
            RotateBackups(dir, WeightsFile);

            var data = new MLWeightsData
            {
                SaveDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ThreatLevel = threatLevel,
                TotalTrainingSteps = brain.TotalTrainingSteps,
                TotalMatchesTrained = totalMatchesTrained,
                AverageLoss = avgLoss,
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

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(Path.Combine(dir, WeightsFile), json);
            Debug.Log($"[MLPersistence] 重み保存完了 (v2 マルチヘッド)  脅威度={threatLevel}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MLPersistence] 重み保存失敗: {e.Message}");
        }
    }

    public static bool LoadWeights(MLBrain brain)
    {
        try
        {
            string path = Path.Combine(GetMLDir(), WeightsFile);
            if (!File.Exists(path))
            {
                Debug.Log("[MLPersistence] 重みファイルなし → 新規初期化");
                return false;
            }

            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<MLWeightsData>(json);
            if (data == null || data.WShared_flat == null || data.Heads == null) return false;

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
            brain.LearningRate = data.LearningRate;
            if (data.HeadWeights != null && data.HeadWeights.Length == MLBrain.NumHeads)
                Array.Copy(data.HeadWeights, brain.HeadWeights, MLBrain.NumHeads);

            Debug.Log($"[MLPersistence] 重み読込完了 (v2)  脅威度={data.ThreatLevel}  " +
                      $"学習ステップ={data.TotalTrainingSteps}  保存日={data.SaveDate}");
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
            File.WriteAllText(Path.Combine(dir, BufferFile), JsonUtility.ToJson(data));
            Debug.Log($"[MLPersistence] バッファ保存: {recent.Count}件");
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] バッファ保存失敗: {e.Message}"); }
    }

    public static void LoadBuffer(MLReplayBuffer buffer)
    {
        try
        {
            string path = Path.Combine(GetMLDir(), BufferFile);
            if (!File.Exists(path)) return;
            var data = JsonUtility.FromJson<MLBufferData>(File.ReadAllText(path));
            if (data?.Experiences == null) return;
            var exps = new List<MLReplayBuffer.Experience>();
            foreach (var ed in data.Experiences)
                exps.Add(new MLReplayBuffer.Experience
                {
                    Features = ed.Features, Reward = ed.Reward,
                    TDTarget = ed.TDTarget, Priority = ed.Priority,
                    TurnNumber = ed.TurnNumber
                });
            buffer.LoadExperiences(exps);
            Debug.Log($"[MLPersistence] バッファ読込: {exps.Count}件");
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
            File.WriteAllText(Path.Combine(GetMLDir(), PredictorFile), JsonUtility.ToJson(data));
            Debug.Log($"[MLPersistence] 予測器保存: 精度={pred.Accuracy:F2}  予測数={pred.TotalPredictions}");
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] 予測器保存失敗: {e.Message}"); }
    }

    public static void LoadBehaviorPredictor(BehaviorPredictor pred)
    {
        try
        {
            string path = Path.Combine(GetMLDir(), PredictorFile);
            if (!File.Exists(path)) return;
            var data = JsonUtility.FromJson<PredictorData>(File.ReadAllText(path));
            if (data == null) return;
            pred.LoadWeightsFlat(data.Weights);
            pred.SaveRunningAccuracy = data.RunningAccuracy;
            pred.SaveTotalPredictions = data.TotalPredictions;
            pred.SaveCorrectPredictions = data.CorrectPredictions;
            Debug.Log($"[MLPersistence] 予測器読込: 精度={pred.Accuracy:F2}");
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
            File.WriteAllText(Path.Combine(GetMLDir(), ProfileFile), JsonUtility.ToJson(data, true));
            Debug.Log($"[MLPersistence] プロファイル保存: 攻撃性={p.AggressionScore:F2} " +
                      $"信頼度={p.Confidence:F2}  観測試合={p.MatchesObserved}");
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] プロファイル保存失敗: {e.Message}"); }
    }

    public static void LoadPlayerProfile(PlayerProfiler profiler)
    {
        try
        {
            string path = Path.Combine(GetMLDir(), ProfileFile);
            if (!File.Exists(path)) return;
            var data = JsonUtility.FromJson<ProfileData>(File.ReadAllText(path));
            if (data == null) return;

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
            Debug.Log($"[MLPersistence] プロファイル読込: 攻撃性={p.AggressionScore:F2}  " +
                      $"観測試合={p.MatchesObserved}");
        }
        catch (Exception e) { Debug.LogError($"[MLPersistence] プロファイル読込失敗: {e.Message}"); }
    }

    // ================================================================
    //  ユーティリティ
    // ================================================================
    static string GetMLDir()
    {
        string dir = Path.Combine(Application.persistentDataPath, MLDir);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    static void RotateBackups(string dir, string fileName)
    {
        string basePath = Path.Combine(dir, fileName);
        if (!File.Exists(basePath)) return;
        string oldest = Path.Combine(dir, $"{fileName}.bak{MaxBackups}");
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int i = MaxBackups - 1; i >= 1; i--)
        {
            string from = Path.Combine(dir, $"{fileName}.bak{i}");
            string to = Path.Combine(dir, $"{fileName}.bak{i + 1}");
            if (File.Exists(from)) File.Move(from, to);
        }
        File.Copy(basePath, Path.Combine(dir, $"{fileName}.bak1"), true);
    }

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
