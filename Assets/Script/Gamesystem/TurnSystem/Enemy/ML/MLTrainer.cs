using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MLTrainer — 試合終了後の学習エンジン
//  経験再生バッファからミニバッチを取り出し、
//  ニューラルネットワークの重みを更新する。
//
//  学習タイミング:
//  ・Player勝利時 → 自分の弱点を学習（敗因分析）
//  ・Player敗北時 → 成功パターンを強化
//  ・脅威度に応じて学習率・バッチサイズ・エポック数がスケール
// =====================================================================
public class MLTrainer
{
    readonly MLBrain _brain;
    readonly MLReplayBuffer _buffer;
    System.Random _rng;

    // ---- 学習パラメータ（脅威度でスケール） ----
    int _batchSize = 32;
    int _epochsPerMatch = 5;
    float _baseLearningRate = 0.001f;

    // ---- 統計 ----
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

    // ================================================================
    //  脅威度に応じたパラメータ調整
    // ================================================================
    public void ConfigureForThreatLevel(int threatLevel)
    {
        // 脅威度が高いほど学習を深くする
        if (threatLevel <= 30)
        {
            _batchSize = 16;
            _epochsPerMatch = 3;
            _baseLearningRate = 0.002f;  // 初期は大胆に学習
        }
        else if (threatLevel <= 50)
        {
            _batchSize = 32;
            _epochsPerMatch = 5;
            _baseLearningRate = 0.001f;
        }
        else if (threatLevel <= 70)
        {
            _batchSize = 48;
            _epochsPerMatch = 8;
            _baseLearningRate = 0.0005f; // 収束に向けて慎重に
        }
        else
        {
            _batchSize = 64;
            _epochsPerMatch = 10;
            _baseLearningRate = 0.0003f; // 微調整
        }

        _brain.LearningRate = _baseLearningRate;
    }

    // ================================================================
    //  試合終了時の学習
    //  playerWon: true = AI敗北（弱点学習）, false = AI勝利（成功強化）
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
                // Forward
                _brain.Forward(exp.Features);

                // TD目標値をそのまま使用
                float target = exp.TDTarget;

                // 勝敗による教師信号の調整
                if (playerWon)
                {
                    // AI敗北: この行動は悪かった → 目標を下げる
                    target = Mathf.Clamp(target - 0.1f, -1f, 1f);
                }
                else
                {
                    // AI勝利: この行動は良かった → 目標を上げる
                    target = Mathf.Clamp(target + 0.1f, -1f, 1f);
                }

                // 敗因に応じた追加補正
                if (playerWon && analysis != null)
                {
                    target = ApplyFailureCorrection(target, exp, analysis);
                }

                // Backward
                float loss = _brain.Backward(target);
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

        Debug.Log($"[MLTrainer] 学習完了: {_epochsPerMatch}エポック×{_batchSize}バッチ  " +
                  $"損失={LastEpochLoss:F4}  平均損失={AverageLoss:F4}  " +
                  $"ステップ={totalSteps}  累計試合={TotalMatchesTrained}  " +
                  $"所要={elapsed:F1}ms");
    }

    // ================================================================
    //  敗因に応じた教師信号補正
    //  特定の敗因パターンを重点的に修正する
    // ================================================================
    float ApplyFailureCorrection(float target, MLReplayBuffer.Experience exp, MatchAnalysis analysis)
    {
        switch (analysis.PrimaryFailure)
        {
            case FailureReason.CrystalDestroyed:
            case FailureReason.DefenseBreached:
                // 防衛不足: 撤退・防衛行動の価値を上げる
                // 特徴30 = ActionType encode → 負の値が防衛系
                if (exp.Features[30] < -0.1f) // 防衛・建築系
                    target = Mathf.Clamp(target + 0.15f, -1f, 1f);
                // 攻撃系行動の価値を下げる（守りが足りなかった）
                if (exp.Features[30] > 0.5f)
                    target = Mathf.Clamp(target - 0.1f, -1f, 1f);
                break;

            case FailureReason.EconomyCollapse:
                // 経済崩壊: 建築行動の価値を上げる
                if (exp.Features[30] < -0.5f) // 建築系
                    target = Mathf.Clamp(target + 0.2f, -1f, 1f);
                break;

            case FailureReason.UnitWipeout:
                // 全滅: 撤退行動の価値を上げ、過度な攻撃を下げる
                if (exp.Features[30] < 0f) // 防衛・撤退系
                    target = Mathf.Clamp(target + 0.15f, -1f, 1f);
                if (exp.Features[51] > 0.5f) // 孤立度が高い行動
                    target = Mathf.Clamp(target - 0.2f, -1f, 1f);
                break;

            case FailureReason.OverextensionPunished:
                // 攻め急ぎ: 孤立した行動を強く否定
                if (exp.Features[51] > 0.3f) // 孤立度
                    target = Mathf.Clamp(target - 0.25f, -1f, 1f);
                break;
        }

        return target;
    }

    // ================================================================
    //  ターン中の即時学習（オンライン学習、脅威度50以上）
    //  行動の結果が分かった時点で即座に学習する
    // ================================================================
    public void OnlineLearningStep(float[] features, float reward)
    {
        _brain.Forward(features);
        float target = Mathf.Clamp(reward, -1f, 1f);
        _brain.Backward(target);
    }
}
