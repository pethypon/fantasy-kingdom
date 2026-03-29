using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MLTrainer — マルチヘッド対応学習エンジン（v3: バグ修正+カリキュラム学習）
//
//  修正点:
//  - 全層グローバル勾配クリッピングはMLBrain側で実施
//  - PER優先度更新を実際に呼び出し
//  - ヘッド別に異なる教師信号を生成（行動タイプに基づく）
//  - カリキュラム学習フェーズ対応
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

    // ---- カリキュラム学習 ----
    public enum CurriculumPhase { Foundation, Intermediate, Advanced, Expert }
    CurriculumPhase _phase = CurriculumPhase.Foundation;
    int _consecutiveWins = 0;
    int _consecutiveLosses = 0;
    float _recentWinRate = 0.5f;
    public CurriculumPhase CurrentPhase => _phase;

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

        // カリキュラムフェーズに応じて学習率を微調整
        float phaseMultiplier = 1f;
        switch (_phase)
        {
            case CurriculumPhase.Foundation:   phaseMultiplier = 1.2f; break;
            case CurriculumPhase.Intermediate: phaseMultiplier = 1.0f; break;
            case CurriculumPhase.Advanced:     phaseMultiplier = 0.8f; break;
            case CurriculumPhase.Expert:       phaseMultiplier = 0.5f; break;
        }
        _brain.LearningRate = _baseLearningRate * phaseMultiplier;
    }

    // ================================================================
    //  試合終了時の学習（マルチヘッド版 + PER更新）
    // ================================================================
    public void TrainOnMatchEnd(bool playerWon, MatchAnalysis analysis)
    {
        // カリキュラム更新
        UpdateCurriculum(playerWon);

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
            List<int> batchIndices;
            var batch = _buffer.SampleBatchWithIndices(_batchSize, _rng, out batchIndices);
            float epochLoss = 0f;

            for (int b = 0; b < batch.Count; b++)
            {
                var exp = batch[b];

                // Forward (入力サイズ調整)
                float[] input = PadInput(exp.Features);
                float predicted = _brain.Forward(input);

                // 3ヘッド分の教師信号を生成（ヘッド別に異なるターゲット）
                float[] targets = BuildHeadTargets(exp, playerWon, analysis);

                float loss = _brain.Backward(targets);
                epochLoss += loss;
                totalSteps++;

                // ---- PER優先度更新（学習後のTD誤差に基づく） ----
                float tdError = Mathf.Abs(predicted - exp.TDTarget);
                _buffer.UpdatePriority(batchIndices[b], tdError);
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
                  $"累計試合={TotalMatchesTrained}  フェーズ={_phase}  所要={elapsed:F1}ms");
    }

    // ================================================================
    //  ヘッド別教師信号の構築
    //  行動の特徴に基づいて3ヘッドに異なるターゲットを設定
    // ================================================================
    float[] BuildHeadTargets(MLReplayBuffer.Experience exp, bool playerWon, MatchAnalysis analysis)
    {
        float baseTarget = exp.TDTarget;

        // 勝敗による全体的な補正
        // playerWon=true → AI敗北 → ターゲットを下げる（この状態/行動は悪かった）
        // playerWon=false → AI勝利 → ターゲットを上げる（この状態/行動は良かった）
        if (playerWon)
            baseTarget = Mathf.Clamp(baseTarget - 0.1f, -1f, 1f);
        else
            baseTarget = Mathf.Clamp(baseTarget + 0.1f, -1f, 1f);

        float[] targets = new float[MLBrain.NumHeads];

        // ---- ヘッド別の差分ターゲット ----
        // 行動コンテキストから各ヘッドのターゲットを微調整
        float actionFeature = (exp.Features != null && exp.Features.Length > 30) ? exp.Features[30] : 0f;
        float isolationFeature = (exp.Features != null && exp.Features.Length > 51) ? exp.Features[51] : 0f;

        // Strategy (長期勝率): 全体報酬を反映、防衛行動を重視
        targets[0] = baseTarget;
        if (actionFeature < -0.3f) // 防衛的行動
            targets[0] = Mathf.Clamp(targets[0] + (playerWon ? -0.05f : 0.1f), -1f, 1f);

        // Tactics (即時戦術): 短期報酬を重視、孤立ペナルティ
        targets[1] = Mathf.Clamp(baseTarget * 0.7f + exp.Reward * 0.3f, -1f, 1f);
        if (isolationFeature > 0.5f)
            targets[1] = Mathf.Clamp(targets[1] - 0.1f, -1f, 1f);

        // Economy (経済効率): 建築・召喚行動に反応、ターン後半ほど重要
        targets[2] = baseTarget;
        float turnRatio = (exp.Features != null && exp.Features.Length > 61)
            ? Mathf.Clamp01(exp.Features[61]) : 0.5f;
        targets[2] = Mathf.Clamp(targets[2] * (0.8f + turnRatio * 0.4f), -1f, 1f);

        // 敗因に応じたヘッド別補正
        if (playerWon && analysis != null)
            ApplyFailureCorrection(targets, exp, analysis);

        return targets;
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
                targets[0] = Mathf.Clamp(targets[0] - 0.2f, -1f, 1f);
                targets[2] = Mathf.Clamp(targets[2] - 0.1f, -1f, 1f);
                // 防衛行動だった場合は逆に上方修正
                if (exp.Features != null && exp.Features.Length > 30 && exp.Features[30] < -0.1f)
                {
                    targets[0] = Mathf.Clamp(targets[0] + 0.3f, -1f, 1f);
                    targets[1] = Mathf.Clamp(targets[1] + 0.2f, -1f, 1f);
                }
                break;

            case FailureReason.EconomyCollapse:
                targets[2] = Mathf.Clamp(targets[2] - 0.3f, -1f, 1f);
                if (exp.Features != null && exp.Features.Length > 30 && exp.Features[30] < -0.5f)
                    targets[2] = Mathf.Clamp(targets[2] + 0.4f, -1f, 1f);
                break;

            case FailureReason.UnitWipeout:
                targets[1] = Mathf.Clamp(targets[1] - 0.25f, -1f, 1f);
                if (exp.Features != null && exp.Features.Length > 51 && exp.Features[51] > 0.5f)
                    targets[1] = Mathf.Clamp(targets[1] - 0.2f, -1f, 1f);
                break;

            case FailureReason.OverextensionPunished:
                targets[0] = Mathf.Clamp(targets[0] - 0.15f, -1f, 1f);
                targets[1] = Mathf.Clamp(targets[1] - 0.2f, -1f, 1f);
                if (exp.Features != null && exp.Features.Length > 51 && exp.Features[51] > 0.3f)
                    targets[1] = Mathf.Clamp(targets[1] - 0.15f, -1f, 1f);
                break;
        }
    }

    // ================================================================
    //  カリキュラム学習: 勝率に基づいてフェーズを進行
    // ================================================================
    void UpdateCurriculum(bool playerWon)
    {
        // 勝率の指数移動平均
        float won = playerWon ? 0f : 1f; // AI視点: playerWon=true → AI負け
        _recentWinRate = _recentWinRate * 0.9f + won * 0.1f;

        if (playerWon)
        {
            _consecutiveWins = 0;
            _consecutiveLosses++;
        }
        else
        {
            _consecutiveWins++;
            _consecutiveLosses = 0;
        }

        // フェーズ昇格条件: AI勝率が安定して高い
        CurriculumPhase newPhase = _phase;
        switch (_phase)
        {
            case CurriculumPhase.Foundation:
                if (_recentWinRate > 0.4f && TotalMatchesTrained >= 3)
                    newPhase = CurriculumPhase.Intermediate;
                break;
            case CurriculumPhase.Intermediate:
                if (_recentWinRate > 0.5f && TotalMatchesTrained >= 8)
                    newPhase = CurriculumPhase.Advanced;
                break;
            case CurriculumPhase.Advanced:
                if (_recentWinRate > 0.55f && TotalMatchesTrained >= 15)
                    newPhase = CurriculumPhase.Expert;
                break;
        }

        // フェーズ降格条件: 連敗が続く
        if (_consecutiveLosses >= 5 && _phase != CurriculumPhase.Foundation)
        {
            int p = (int)_phase;
            newPhase = (CurriculumPhase)Mathf.Max(0, p - 1);
            _consecutiveLosses = 0;
        }

        if (newPhase != _phase)
        {
            Debug.Log($"[MLTrainer] カリキュラムフェーズ変更: {_phase} → {newPhase}  " +
                      $"勝率={_recentWinRate:F2}");
            _phase = newPhase;
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
        // ヘッド別ターゲット: 戦術ヘッドに即時報酬を強く反映
        float[] targets = new float[] { t * 0.5f, t, t * 0.3f };
        _brain.Backward(targets);
    }
}
