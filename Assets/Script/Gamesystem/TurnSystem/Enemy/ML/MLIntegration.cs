using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MLIntegration — 機械学習AIと既存AIシステムの統合層
//
//  脅威度20以降で有効化。
//  ・行動評価時: MLBrainの出力をスコアに加算
//  ・行動実行後: 経験をReplayBufferに記録
//  ・試合終了時: MLTrainerで学習 + 重み保存
//  ・ゲーム起動時: 保存済み重みの読込
//
//  AICommanderから呼ばれるインターフェース。
// =====================================================================
public class MLIntegration
{
    // ---- コンポーネント ----
    readonly MLBrain _brain;
    readonly MLReplayBuffer _buffer;
    readonly MLTrainer _trainer;

    // ---- 状態 ----
    bool _isActive;
    int _threatLevel;
    TurnStrategy _currentStrategy;
    MajorPersonality _personality;

    // ---- 即時報酬の追跡 ----
    float _turnDamageDealt;
    float _turnDamageReceived;
    float _turnUnitsLost;
    float _turnUnitsKilled;
    float _turnBuildingsBuilt;

    // ---- MLスコアの影響力（脅威度に応じてスケール） ----
    float _mlScoreWeight = 1.0f;

    // ---- 統計 ----
    public bool IsActive => _isActive;
    public int TotalTrainingSteps => _brain.TotalTrainingSteps;
    public int TotalMatchesTrained => _trainer.TotalMatchesTrained;
    public float AverageLoss => _trainer.AverageLoss;
    public float LastLoss => _brain.LastLoss;
    public int BufferSize => _buffer.CurrentSize;
    public int ParameterCount => _brain.ParameterCount;

    // ================================================================
    //  初期化
    // ================================================================
    public MLIntegration(int threatLevel, MajorPersonality personality, int seed = -1)
    {
        _brain = new MLBrain(seed);
        _buffer = new MLReplayBuffer(5000);
        _trainer = new MLTrainer(_brain, _buffer, seed);

        _threatLevel = threatLevel;
        _personality = personality;
        _isActive = threatLevel >= AIThreatLevel.NormalEnd; // 脅威度20以上で有効

        if (_isActive)
        {
            // 保存済み重みの読込
            bool loaded = MLPersistence.LoadWeights(_brain);
            MLPersistence.LoadBuffer(_buffer);

            // 脅威度に応じた学習パラメータ設定
            _trainer.ConfigureForThreatLevel(threatLevel);
            UpdateMLScoreWeight(threatLevel);

            Debug.Log("=== [MLIntegration] ==============================");
            Debug.Log($"[MLIntegration] 機械学習AI有効化  脅威度={threatLevel}");
            Debug.Log($"[MLIntegration] 重み読込={loaded}  パラメータ数={_brain.ParameterCount}");
            Debug.Log($"[MLIntegration] バッファ={_buffer.CurrentSize}/{_buffer.Capacity}  " +
                      $"学習済試合={_trainer.TotalMatchesTrained}");
            Debug.Log($"[MLIntegration] MLスコア重み={_mlScoreWeight:F2}  " +
                      $"学習率={_brain.LearningRate}");
            Debug.Log("=== [MLIntegration] ==============================");
        }
        else
        {
            Debug.Log($"[MLIntegration] 脅威度{threatLevel} < 20 → ML無効");
        }
    }

    // ================================================================
    //  脅威度によるMLスコアの影響力調整
    //  低脅威度: 既存AIメイン、MLは補助
    //  高脅威度: MLの影響力が大きくな���
    // ================================================================
    void UpdateMLScoreWeight(int threatLevel)
    {
        if (threatLevel <= 25)
            _mlScoreWeight = 0.3f;     // 控えめ（既存AIの補助）
        else if (threatLevel <= 35)
            _mlScoreWeight = 0.5f;     // 中程度
        else if (threatLevel <= 50)
            _mlScoreWeight = 0.8f;     // ML主導に移行
        else if (threatLevel <= 70)
            _mlScoreWeight = 1.2f;     // MLが主要な判断力
        else
            _mlScoreWeight = 1.5f;     // ML最大影響力
    }

    // ================================================================
    //  ターン開始時
    // ================================================================
    public void OnTurnStart(TurnStrategy strategy, int threatLevel)
    {
        if (!_isActive) return;

        _currentStrategy = strategy;
        _threatLevel = threatLevel;
        _turnDamageDealt = 0f;
        _turnDamageReceived = 0f;
        _turnUnitsLost = 0f;
        _turnUnitsKilled = 0f;
        _turnBuildingsBuilt = 0f;

        UpdateMLScoreWeight(threatLevel);
    }

    // ================================================================
    //  行動評価 — 各AIActionにMLスコアを加算
    //  AIActionEvaluator.EvaluateAll() の後に呼ばれる
    // ================================================================
    public void EvaluateActions(List<AIAction> actions, AIBoardState board)
    {
        if (!_isActive || actions == null || actions.Count == 0) return;

        foreach (var action in actions)
        {
            float[] features = MLFeatureExtractor.ExtractActionFeatures(board, action);

            // メタ特徴を埋める
            features[62] = EncodeStrategy(_currentStrategy);
            features[63] = EncodePersonality(_personality);

            // MLBrainで予測 (-1 ～ +1)
            float mlValue = _brain.Predict(features);

            // スコアに変換して加算
            // MLの出力範囲 (-1,+1) を既存スコアと同等のスケールに変換
            float mlScore = mlValue * 30f * _mlScoreWeight;

            action.Score += mlScore;
        }
    }

    // ================================================================
    //  行動実行後の記録
    // ================================================================
    public void RecordAction(AIAction action, AIBoardState board, bool success, int turn)
    {
        if (!_isActive) return;

        float[] features = MLFeatureExtractor.ExtractActionFeatures(board, action);
        features[62] = EncodeStrategy(_currentStrategy);
        features[63] = EncodePersonality(_personality);

        // 即時報酬を計算
        float reward = CalcImmediateReward(action, success);

        _buffer.RecordStep(features, reward, turn);
    }

    // ================================================================
    //  即時報酬計算
    //  行動の直接的な価値を報酬信号として返す
    // ================================================================
    float CalcImmediateReward(AIAction action, bool success)
    {
        if (!success) return -0.1f;

        float reward = 0f;

        switch (action.ActionType)
        {
            case AIActionType.Attack:
                // ダメージを与えた (推定)
                if (action.Unit != null && action.TargetUnit != null)
                {
                    int dmg = Mathf.Max(0, 1 + (action.Unit.ATK / 6) +
                        ((action.Unit.ATK / 2) - (action.TargetUnit.DEF / 4)));
                    reward += Mathf.Clamp(dmg * 0.02f, 0f, 0.3f);

                    // 撃破ボーナス
                    if (dmg >= action.TargetUnit.HP)
                        reward += 0.3f;

                    // 高価値ターゲット
                    float targetValue = AIBoardEvaluator.GetPieceValuePublic(action.TargetUnit);
                    reward += Mathf.Clamp(targetValue * 0.001f, 0f, 0.2f);
                }
                break;

            case AIActionType.SkillUse:
                reward += 0.1f;
                if (action.Skill != null && action.Skill.Multiplier > 1.5f)
                    reward += 0.1f;
                break;

            case AIActionType.Build:
                reward += 0.15f;
                _turnBuildingsBuilt++;
                break;

            case AIActionType.Summon:
                reward += 0.1f;
                break;

            case AIActionType.Retreat:
                // 瀕死駒の撤退は良い判断
                if (action.Unit != null && action.Unit.MaxHP > 0 &&
                    (float)action.Unit.HP / action.Unit.MaxHP < 0.3f)
                    reward += 0.15f;
                else
                    reward += 0.05f;
                break;

            case AIActionType.Support:
                reward += 0.1f;
                break;

            case AIActionType.Surround:
                reward += 0.12f;
                break;

            case AIActionType.Move:
                reward += 0.02f; // 移動自体は中立
                break;

            case AIActionType.DefenseRepos:
                reward += 0.08f;
                break;

            case AIActionType.SubCrystal:
                reward += 0.12f;
                break;

            case AIActionType.Wait:
                reward -= 0.02f;
                break;
        }

        return Mathf.Clamp(reward, -0.5f, 0.5f);
    }

    // ================================================================
    //  戦闘結果の通知（AICommanderから呼ぶ）
    // ================================================================
    public void NotifyDamageDealt(float damage)
    {
        _turnDamageDealt += damage;
    }

    public void NotifyDamageReceived(float damage)
    {
        _turnDamageReceived += damage;
    }

    public void NotifyUnitKilled()
    {
        _turnUnitsKilled++;
    }

    public void NotifyUnitLost()
    {
        _turnUnitsLost++;
    }

    // ================================================================
    //  試合終了時の学習
    // ================================================================
    public void OnMatchEnd(bool playerWon, MatchAnalysis analysis)
    {
        if (!_isActive) return;

        // 終端報酬
        float terminalReward = playerWon ? -0.8f : 0.8f;

        // 経験バッファの終端処理
        _buffer.FinalizeMatch(terminalReward);

        // 学習実行
        _trainer.TrainOnMatchEnd(playerWon, analysis);

        // 重み保存
        MLPersistence.SaveWeights(_brain, _threatLevel,
            _trainer.TotalMatchesTrained, _trainer.AverageLoss);
        MLPersistence.SaveBuffer(_buffer);

        Debug.Log($"[MLIntegration] 試合終了学習: {(playerWon ? "AI敗北" : "AI勝利")}  " +
                  $"バッファ={_buffer.CurrentSize}  損失={_trainer.LastEpochLoss:F4}");
    }

    // ================================================================
    //  オンライン学習（脅威度50以上、ターン中に即座に学習）
    // ================================================================
    public void OnlineLearn(float[] features, float reward)
    {
        if (!_isActive || _threatLevel < 50) return;
        _trainer.OnlineLearningStep(features, reward);
    }

    // ================================================================
    //  盤面評価（3手先探索の末端評価にML価値を加算）
    // ================================================================
    public float EvaluateBoard(AIBoardState board)
    {
        if (!_isActive) return 0f;

        float[] features = MLFeatureExtractor.ExtractBoardFeatures(board);
        features[62] = EncodeStrategy(_currentStrategy);
        features[63] = EncodePersonality(_personality);

        return _brain.Predict(features) * 20f * _mlScoreWeight;
    }

    // ================================================================
    //  脅威度更新
    // ================================================================
    public void UpdateThreatLevel(int newLevel)
    {
        _threatLevel = newLevel;
        _isActive = newLevel >= AIThreatLevel.NormalEnd;
        if (_isActive)
        {
            _trainer.ConfigureForThreatLevel(newLevel);
            UpdateMLScoreWeight(newLevel);
        }
    }

    // ================================================================
    //  エンコーディングヘルパー
    // ================================================================
    static float EncodeStrategy(TurnStrategy strategy)
    {
        switch (strategy)
        {
            case TurnStrategy.Assault:        return 1.0f;
            case TurnStrategy.ContactEngage:  return 0.6f;
            case TurnStrategy.ScoutSearch:    return 0.2f;
            case TurnStrategy.Balanced:       return 0.0f;
            case TurnStrategy.EconomyBuild:   return -0.4f;
            case TurnStrategy.RetreatRegroup: return -0.7f;
            case TurnStrategy.CrystalDefense: return -1.0f;
            default: return 0f;
        }
    }

    static float EncodePersonality(MajorPersonality p)
    {
        switch (p)
        {
            case MajorPersonality.Combat:   return 1.0f;
            case MajorPersonality.Adaptive: return 0.3f;
            case MajorPersonality.Growth:   return -0.3f;
            case MajorPersonality.Intellect:return -1.0f;
            default: return 0f;
        }
    }

    // ================================================================
    //  デバッグ情報
    // ================================================================
    public string GetDebugInfo()
    {
        if (!_isActive) return "ML: 無効";
        return $"ML: 有効  重み={_mlScoreWeight:F2}  " +
               $"パラメ={_brain.ParameterCount}  " +
               $"バッファ={_buffer.CurrentSize}/{_buffer.Capacity}  " +
               $"学習試合={_trainer.TotalMatchesTrained}  " +
               $"平均損失={_trainer.AverageLoss:F4}";
    }
}
