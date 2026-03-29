using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MLTrainer — マルチヘッド対応学習エンジン（書き直し版）
//
//  3ヘッド (Strategy, Tactics, Economy) に対して別々の教師信号を生成。
//  敗因に応じてヘッド別に補正をかけることで、弱点を重点的に改善する。
// =====================================================================
public class MLTrainer
{
    readonly MLBrain _brain;
    readonly MLReplayBuffer _buffer;
    System.Random _rng;

    int _batchSize = 32;
    int _epochsPerMatch = 5;
    float _baseLearningRate = 0.001f;

    public int TotalMatchesTrained { get; private set; }
    public float LastEpochLoss { get; private set; }
    public float AverageLoss { get; private set; }
    float _lossSum = 0f;
    int _lossCount = 0;

    public MLTrainer(MLBrain brain, MLReplayBuffer buffer, int seed = -1)
    {
        _brain = brain;
        _buffer = buffer;
        _rng = seed >= 0 ? new System.Random(seed) : new System.Random();
    }

    public void ConfigureForThreatLevel(int threatLevel)
    {
        if (threatLevel <= 30)
        {
            _batchSize = 16; _epochsPerMatch = 3; _baseLearningRate = 0.002f;
        }
        else if (threatLevel <= 50)
        {
            _batchSize = 32; _epochsPerMatch = 5; _baseLearningRate = 0.001f;
        }
        else if (threatLevel <= 70)
        {
            _batchSize = 48; _epochsPerMatch = 8; _baseLearningRate = 0.0005f;
        }
        else
        {
            _batchSize = 64; _epochsPerMatch = 10; _baseLearningRate = 0.0003f;
        }
        _brain.LearningRate = _baseLearningRate;
    }

    // ================================================================
    //  試合終了時の学習（マルチヘッド版）
    // ================================================================
    public void TrainOnMatchEnd(bool playerWon, MatchAnalysis analysis)
    {
        if (_buffer.CurrentSize < _batchSize)
        {
            Debug.Log($"[MLTrainer] バッファ不足 ({_buffer.CurrentSize}/{_batchSize}) → 学習スキップ");
            return;
        }

        float startTime = Time.realtimeSinceStartup;
        float totalLoss = 0f;
        int totalSteps = 0;

        for (int epoch = 0; epoch < _epochsPerMatch; epoch++)
        {
            var batch = _buffer.SampleBatch(_batchSize, _rng);
            float epochLoss = 0f;

            foreach (var exp in batch)
            {
                // Forward (入力サイズ調整: バッファ内の特徴が64次元の場合は80次元にパディング)
                float[] input = PadInput(exp.Features);
                _brain.Forward(input);

                // 3ヘッド分の教師信号を生成
                float baseTarget = exp.TDTarget;
                if (playerWon)
                    baseTarget = Mathf.Clamp(baseTarget - 0.1f, -1f, 1f);
                else
                    baseTarget = Mathf.Clamp(baseTarget + 0.1f, -1f, 1f);

                float[] targets = new float[MLBrain.NumHeads];
                targets[0] = baseTarget; // Strategy
                targets[1] = baseTarget; // Tactics
                targets[2] = baseTarget; // Economy

                // 敗因に応じたヘッド別補正
                if (playerWon && analysis != null)
                    ApplyFailureCorrection(targets, exp, analysis);

                float loss = _brain.Backward(targets);
                epochLoss += loss;
                totalSteps++;
            }

            epochLoss /= batch.Count;
            totalLoss += epochLoss;
        }

        LastEpochLoss = totalLoss / _epochsPerMatch;
        _lossSum += LastEpochLoss;
        _lossCount++;
        AverageLoss = _lossSum / _lossCount;
        TotalMatchesTrained++;

        float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
        Debug.Log($"[MLTrainer] 学習完了: {_epochsPerMatch}ep×{_batchSize}batch  " +
                  $"損失={LastEpochLoss:F4}  平均={AverageLoss:F4}  " +
                  $"累計試合={TotalMatchesTrained}  所要={elapsed:F1}ms");
    }

    // ================================================================
    //  敗因別のヘッド教師信号補正
    // ================================================================
    void ApplyFailureCorrection(float[] targets, MLReplayBuffer.Experience exp, MatchAnalysis analysis)
    {
        switch (analysis.PrimaryFailure)
        {
            case FailureReason.CrystalDestroyed:
            case FailureReason.DefenseBreached:
                // 防衛崩壊 → 戦略ヘッドを強く下方修正、経済ヘッドも下方
                targets[0] = Mathf.Clamp(targets[0] - 0.2f, -1f, 1f); // Strategy
                targets[2] = Mathf.Clamp(targets[2] - 0.1f, -1f, 1f); // Economy
                // 防衛行動だった場合は逆に上方修正（防衛行動は正しかった）
                if (exp.Features.Length > 30 && exp.Features[30] < -0.1f)
                {
                    targets[0] = Mathf.Clamp(targets[0] + 0.3f, -1f, 1f);
                    targets[1] = Mathf.Clamp(targets[1] + 0.2f, -1f, 1f);
                }
                break;

            case FailureReason.EconomyCollapse:
                // 経済崩壊 → 経済ヘッドを強く下方修正
                targets[2] = Mathf.Clamp(targets[2] - 0.3f, -1f, 1f); // Economy
                // 建築行動は上方修正
                if (exp.Features.Length > 30 && exp.Features[30] < -0.5f)
                    targets[2] = Mathf.Clamp(targets[2] + 0.4f, -1f, 1f);
                break;

            case FailureReason.UnitWipeout:
                // 全滅 → 戦術ヘッドを強く下方修正
                targets[1] = Mathf.Clamp(targets[1] - 0.25f, -1f, 1f); // Tactics
                // 孤立行動を罰する
                if (exp.Features.Length > 51 && exp.Features[51] > 0.5f)
                    targets[1] = Mathf.Clamp(targets[1] - 0.2f, -1f, 1f);
                break;

            case FailureReason.OverextensionPunished:
                // 攻め急ぎ → 戦略と戦術の両方を修正
                targets[0] = Mathf.Clamp(targets[0] - 0.15f, -1f, 1f);
                targets[1] = Mathf.Clamp(targets[1] - 0.2f, -1f, 1f);
                if (exp.Features.Length > 51 && exp.Features[51] > 0.3f)
                    targets[1] = Mathf.Clamp(targets[1] - 0.15f, -1f, 1f);
                break;
        }
    }

    // ================================================================
    //  入力パディング（旧64次元 → 新80次元）
    // ================================================================
    static float[] PadInput(float[] features)
    {
        if (features == null) return new float[MLBrain.InputSize];
        if (features.Length >= MLBrain.InputSize) return features;

        var padded = new float[MLBrain.InputSize];
        System.Array.Copy(features, 0, padded, 0, features.Length);
        return padded;
    }

    // ================================================================
    //  オンライン学習（脅威度50以上）
    // ================================================================
    public void OnlineLearningStep(float[] features, float reward)
    {
        float[] input = PadInput(features);
        _brain.Forward(input);
        float t = Mathf.Clamp(reward, -1f, 1f);
        _brain.Backward(new float[] { t, t, t });
    }
}
