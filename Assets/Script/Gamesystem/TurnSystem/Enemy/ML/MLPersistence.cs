using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// =====================================================================
//  MLPersistence — ML重みの永続化
//  Application.persistentDataPath/ml/ に JSON 形式で保存。
//  試合終了時に自動保存、ゲーム開始時に自動読込。
//  世代管理: 最新3世代のバックアップを保持。
// =====================================================================
public static class MLPersistence
{
    const string MLDir = "ml";
    const string WeightsFile = "ml_weights.json";
    const string BufferFile = "ml_buffer.json";
    const int MaxBackups = 3;

    // ================================================================
    //  保存データ構造
    // ================================================================
    [Serializable]
    public class MLWeightsData
    {
        public int Version = 1;
        public string SaveDate;
        public int ThreatLevel;
        public int TotalTrainingSteps;
        public int TotalMatchesTrained;
        public float AverageLoss;
        public float LearningRate;
        public int AdamStep;

        // 重み（1次元配列に直列化）
        public float[] W1_flat;
        public float[] B1;
        public float[] W2_flat;
        public float[] B2;
        public float[] W3_flat;
        public float[] B3;

        // Adam状態（復元用）
        public float[] MW1_flat, VW1_flat;
        public float[] MW2_flat, VW2_flat;
        public float[] MW3_flat, VW3_flat;
        public float[] MB1, VB1;
        public float[] MB2, VB2;
        public float[] MB3, VB3;
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

    // ================================================================
    //  保存
    // ================================================================
    public static void SaveWeights(MLBrain brain, int threatLevel, int totalMatchesTrained, float avgLoss)
    {
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, MLDir);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // バックアップ管理
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

                W1_flat = Flatten(brain.W1),
                B1 = (float[])brain.B1.Clone(),
                W2_flat = Flatten(brain.W2),
                B2 = (float[])brain.B2.Clone(),
                W3_flat = Flatten(brain.W3),
                B3 = (float[])brain.B3.Clone(),

                MW1_flat = Flatten(brain.MW1),
                VW1_flat = Flatten(brain.VW1),
                MW2_flat = Flatten(brain.MW2),
                VW2_flat = Flatten(brain.VW2),
                MW3_flat = Flatten(brain.MW3),
                VW3_flat = Flatten(brain.VW3),
                MB1 = (float[])brain.MB1.Clone(),
                VB1 = (float[])brain.VB1.Clone(),
                MB2 = (float[])brain.MB2.Clone(),
                VB2 = (float[])brain.VB2.Clone(),
                MB3 = (float[])brain.MB3.Clone(),
                VB3 = (float[])brain.VB3.Clone(),
            };

            string json = JsonUtility.ToJson(data, true);
            string path = Path.Combine(dir, WeightsFile);
            File.WriteAllText(path, json);

            Debug.Log($"[MLPersistence] 重み保存完了: {path}  " +
                      $"脅威度={threatLevel}  学習ステップ={brain.TotalTrainingSteps}");
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
            string path = Path.Combine(Application.persistentDataPath, MLDir, WeightsFile);
            if (!File.Exists(path))
            {
                Debug.Log("[MLPersistence] 重みファイルなし → 新規初期化");
                return false;
            }

            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<MLWeightsData>(json);

            if (data == null || data.W1_flat == null)
            {
                Debug.LogWarning("[MLPersistence] 重みデータ破損 → 新規初期化");
                return false;
            }

            // 重みの復元
            Unflatten(data.W1_flat, brain.W1);
            Array.Copy(data.B1, brain.B1, brain.B1.Length);
            Unflatten(data.W2_flat, brain.W2);
            Array.Copy(data.B2, brain.B2, brain.B2.Length);
            Unflatten(data.W3_flat, brain.W3);
            Array.Copy(data.B3, brain.B3, brain.B3.Length);

            // Adam状態の復元
            if (data.MW1_flat != null) Unflatten(data.MW1_flat, brain.MW1);
            if (data.VW1_flat != null) Unflatten(data.VW1_flat, brain.VW1);
            if (data.MW2_flat != null) Unflatten(data.MW2_flat, brain.MW2);
            if (data.VW2_flat != null) Unflatten(data.VW2_flat, brain.VW2);
            if (data.MW3_flat != null) Unflatten(data.MW3_flat, brain.MW3);
            if (data.VW3_flat != null) Unflatten(data.VW3_flat, brain.VW3);
            if (data.MB1 != null) Array.Copy(data.MB1, brain.MB1, brain.MB1.Length);
            if (data.VB1 != null) Array.Copy(data.VB1, brain.VB1, brain.VB1.Length);
            if (data.MB2 != null) Array.Copy(data.MB2, brain.MB2, brain.MB2.Length);
            if (data.VB2 != null) Array.Copy(data.VB2, brain.VB2, brain.VB2.Length);
            if (data.MB3 != null) Array.Copy(data.MB3, brain.MB3, brain.MB3.Length);
            if (data.VB3 != null) Array.Copy(data.VB3, brain.VB3, brain.VB3.Length);

            brain.AdamStep = data.AdamStep;
            brain.LearningRate = data.LearningRate;

            Debug.Log($"[MLPersistence] 重み読込完了: 脅威度={data.ThreatLevel}  " +
                      $"学習ステップ={data.TotalTrainingSteps}  試合={data.TotalMatchesTrained}  " +
                      $"平均損失={data.AverageLoss:F4}  保存日={data.SaveDate}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[MLPersistence] 重み読込失敗: {e.Message}");
            return false;
        }
    }

    // ================================================================
    //  経験バッファの保存/読込（最新1000件のみ）
    // ================================================================
    public static void SaveBuffer(MLReplayBuffer buffer)
    {
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, MLDir);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var recent = buffer.GetRecentExperiences(1000);
            var data = new MLBufferData
            {
                TotalExperiences = buffer.TotalExperiences,
            };

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

            string json = JsonUtility.ToJson(data);
            string path = Path.Combine(dir, BufferFile);
            File.WriteAllText(path, json);

            Debug.Log($"[MLPersistence] バッファ保存完了: {recent.Count}件");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MLPersistence] バッファ保存失敗: {e.Message}");
        }
    }

    public static void LoadBuffer(MLReplayBuffer buffer)
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, MLDir, BufferFile);
            if (!File.Exists(path))
            {
                Debug.Log("[MLPersistence] バッファファイルなし → 空のまま");
                return;
            }

            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<MLBufferData>(json);

            if (data == null || data.Experiences == null) return;

            var experiences = new List<MLReplayBuffer.Experience>();
            foreach (var ed in data.Experiences)
            {
                experiences.Add(new MLReplayBuffer.Experience
                {
                    Features = ed.Features,
                    Reward = ed.Reward,
                    TDTarget = ed.TDTarget,
                    Priority = ed.Priority,
                    TurnNumber = ed.TurnNumber
                });
            }

            buffer.LoadExperiences(experiences);

            Debug.Log($"[MLPersistence] バッファ読込完了: {experiences.Count}件");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MLPersistence] バッファ読込失敗: {e.Message}");
        }
    }

    // ================================================================
    //  バックアップローテーション
    // ================================================================
    static void RotateBackups(string dir, string fileName)
    {
        string basePath = Path.Combine(dir, fileName);
        if (!File.Exists(basePath)) return;

        // 古いバックアップを削除
        string oldestBackup = Path.Combine(dir, $"{fileName}.bak{MaxBackups}");
        if (File.Exists(oldestBackup))
            File.Delete(oldestBackup);

        // バックアップをシフト
        for (int i = MaxBackups - 1; i >= 1; i--)
        {
            string from = Path.Combine(dir, $"{fileName}.bak{i}");
            string to = Path.Combine(dir, $"{fileName}.bak{i + 1}");
            if (File.Exists(from))
                File.Move(from, to);
        }

        // 現在のファイルを bak1 に
        string bak1 = Path.Combine(dir, $"{fileName}.bak1");
        File.Copy(basePath, bak1, true);
    }

    // ================================================================
    //  2D配列 ⇔ 1D配列変換
    // ================================================================
    static float[] Flatten(float[,] mat)
    {
        int rows = mat.GetLength(0);
        int cols = mat.GetLength(1);
        var flat = new float[rows * cols];
        int idx = 0;
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                flat[idx++] = mat[i, j];
        return flat;
    }

    static void Unflatten(float[] flat, float[,] mat)
    {
        int rows = mat.GetLength(0);
        int cols = mat.GetLength(1);
        int expected = rows * cols;
        if (flat.Length != expected)
        {
            Debug.LogWarning($"[MLPersistence] 配列サイズ不一致: 期待{expected}, 受取{flat.Length}");
            return;
        }
        int idx = 0;
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                mat[i, j] = flat[idx++];
    }

    // ================================================================
    //  重みファイルの存在確認
    // ================================================================
    public static bool HasSavedWeights()
    {
        string path = Path.Combine(Application.persistentDataPath, MLDir, WeightsFile);
        return File.Exists(path);
    }

    /// <summary>保存されている重みの脅威度を確認</summary>
    public static int GetSavedThreatLevel()
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, MLDir, WeightsFile);
            if (!File.Exists(path)) return 0;
            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<MLWeightsData>(json);
            return data?.ThreatLevel ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
