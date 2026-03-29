using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MLIntegration — ARC Raiders式 機械学習AI 統合コントローラ（全面書き直し版）
//
//  全サブシステムを統合し、AICommanderに対する単一のインターフェースを提供。
//
//  ┌─────────────────────────────────────────────────┐
//  │                  MLIntegration                   │
//  │                                                  │
//  │  ┌──────────────┐  ┌───────────────────────┐    │
//  │  │ PlayerProfiler│→│ CounterStrategyEngine  │    │
//  │  │(癖を分析)     │  │(カウンター方針を導出)   │    │
//  │  └──────────────┘  └───────────────────────┘    │
//  │                                                  │
//  │  ┌──────────────────┐  ┌────────────────────┐   │
//  │  │BehaviorPredictor │  │RealtimeAdaptation  │   │
//  │  │(次の行動を予測)    │  │(試合中リアルタイム適応)│   │
//  │  └──────────────────┘  └────────────────────┘   │
//  │                                                  │
//  │  ┌──────────────────────────────────────────┐   │
//  │  │ MLBrain (3ヘッドNN: 戦略/戦術/経済)       │   │
//  │  │ + MLReplayBuffer + MLTrainer             │   │
//  │  └──────────────────────────────────────────┘   │
//  └─────────────────────────────────────────────────┘
//
//  学習の流れ:
//  1. プレイヤーターン → PlayerProfiler がプレイヤー行動を観測
//                      → BehaviorPredictor が予測→学習
//                      → RealtimeAdaptation が脅威を記録
//  2. AIターン開始     → CounterStrategy がカウンター方針決定
//                      → BehaviorPredictor が次ターンの行動予測
//                      → RealtimeAdaptation が適応修正計算
//  3. 行動評価時       → MLBrain + Counter + Realtime + Prediction のスコア統合
//  4. 行動実行後       → ReplayBuffer に記録
//  5. 試合終了         → MLTrainer で逆伝播学習
//                      → PlayerProfiler のプロファイル更新
//                      → MLPersistence で重み保存
// =====================================================================
public class MLIntegration
{
    // ---- コアML ----
    readonly MLBrain _brain;
    readonly MLReplayBuffer _buffer;
    readonly MLTrainer _trainer;

    // ---- ARC Raiders式サブシステム ----
    readonly PlayerProfiler _profiler;
    readonly CounterStrategyEngine _counterStrategy;
    readonly RealtimeAdaptation _realtimeAdapt;
    readonly BehaviorPredictor _behaviorPredictor;

    // ---- 状態 ----
    bool _isActive;
    int _threatLevel;
    TurnStrategy _currentStrategy;
    MajorPersonality _personality;

    // ---- MLスコアの影響力 ----
    float _mlScoreWeight = 1.0f;

    // ---- 統計 ----
    public bool IsActive => _isActive;
    public int TotalTrainingSteps => _brain.TotalTrainingSteps;
    public int TotalMatchesTrained => _trainer.TotalMatchesTrained;
    public float AverageLoss => _trainer.AverageLoss;
    public float LastLoss => _brain.LastLoss;
    public int BufferSize => _buffer.CurrentSize;
    public int ParameterCount => _brain.ParameterCount;

    // ---- サブシステムアクセサ ----
    public PlayerProfiler Profiler => _profiler;
    public CounterStrategyEngine CounterStrategy => _counterStrategy;
    public RealtimeAdaptation RealtimeAdapt => _realtimeAdapt;
    public BehaviorPredictor BehaviorPred => _behaviorPredictor;
    public MLBrain Brain => _brain;

    // ================================================================
    //  初期化
    // ================================================================
    public MLIntegration(int threatLevel, MajorPersonality personality, int seed = -1)
    {
        _brain = new MLBrain(seed);
        _buffer = new MLReplayBuffer(5000);
        _trainer = new MLTrainer(_brain, _buffer, seed);

        _profiler = new PlayerProfiler();
        _counterStrategy = new CounterStrategyEngine();
        _realtimeAdapt = new RealtimeAdaptation();
        _behaviorPredictor = new BehaviorPredictor(seed);

        _threatLevel = threatLevel;
        _personality = personality;
        _isActive = threatLevel >= AIThreatLevel.NormalEnd;

        if (_isActive)
        {
            bool loaded = MLPersistence.LoadWeights(_brain);
            MLPersistence.LoadBuffer(_buffer);
            MLPersistence.LoadBehaviorPredictor(_behaviorPredictor);
            MLPersistence.LoadPlayerProfile(_profiler);

            _trainer.ConfigureForThreatLevel(threatLevel);
            _brain.SetHeadWeightsForThreat(threatLevel);
            UpdateMLScoreWeight(threatLevel);

            Debug.Log("=== [MLIntegration] ARC Raiders式 ML AI ==============================");
            Debug.Log($"[MLIntegration] 脅威度={threatLevel}  重み読込={loaded}");
            Debug.Log($"[MLIntegration] パラメータ={_brain.ParameterCount}  " +
                      $"バッファ={_buffer.CurrentSize}/{_buffer.Capacity}");
            Debug.Log($"[MLIntegration] ヘッド重み: 戦略={_brain.HeadWeights[0]:F2} " +
                      $"戦術={_brain.HeadWeights[1]:F2} 経済={_brain.HeadWeights[2]:F2}");
            Debug.Log($"[MLIntegration] プロファイル: 攻撃性={_profiler.Profile.AggressionScore:F2} " +
                      $"信頼度={_profiler.Profile.Confidence:F2} " +
                      $"観測試合={_profiler.Profile.MatchesObserved}");
            Debug.Log($"[MLIntegration] 予測器: 精度={_behaviorPredictor.Accuracy:F2} " +
                      $"予測数={_behaviorPredictor.TotalPredictions}");
            Debug.Log("=== [MLIntegration] ==============================================");
        }
    }

    void UpdateMLScoreWeight(int threatLevel)
    {
        if (threatLevel <= 25) _mlScoreWeight = 0.3f;
        else if (threatLevel <= 35) _mlScoreWeight = 0.5f;
        else if (threatLevel <= 50) _mlScoreWeight = 0.8f;
        else if (threatLevel <= 70) _mlScoreWeight = 1.2f;
        else _mlScoreWeight = 1.5f;
    }

    // ================================================================
    //  プレイヤーターンの観測（EnemyStart/PlayerMoveから呼ばれる）
    // ================================================================

    /// <summary>プレイヤーの攻撃を観測</summary>
    public void ObservePlayerAttack(Status attacker, Status target, int damage,
        Vector3 enemyCrystalPos, int turn)
    {
        if (!_isActive) return;
        _profiler.ObserveAttack(attacker, target, enemyCrystalPos, turn);
        _realtimeAdapt.OnPlayerAttackedUnit(target, attacker, damage, turn);

        // BehaviorPredictorに観測記録 + 学習
        if (attacker != null)
        {
            _behaviorPredictor.ObserveAction(turn, 0, attacker.transform.position, attacker.kind);
            _behaviorPredictor.Learn(0, attacker.transform.position);
        }
    }

    /// <summary>プレイヤーの移動を観測</summary>
    public void ObservePlayerMove(Status unit, Vector3 from, Vector3 to,
        Vector3 enemyCrystalPos, int turn)
    {
        if (!_isActive) return;
        _profiler.ObserveMove(unit, from, to, enemyCrystalPos, turn);

        float distClosed = Vector3.Distance(from, enemyCrystalPos) -
                           Vector3.Distance(to, enemyCrystalPos);
        _realtimeAdapt.OnPlayerAdvanced(distClosed, turn);

        if (unit != null)
        {
            _behaviorPredictor.ObserveAction(turn, 1, to, unit.kind);
            _behaviorPredictor.Learn(1, to);
        }
    }

    /// <summary>プレイヤーのスキル使用を観測</summary>
    public void ObservePlayerSkill(Status attacker, int turn)
    {
        if (!_isActive) return;
        _realtimeAdapt.OnPlayerUsedSkill(attacker, turn);
        if (attacker != null)
        {
            _behaviorPredictor.ObserveAction(turn, 3, attacker.transform.position, attacker.kind);
            _behaviorPredictor.Learn(3, attacker.transform.position);
        }
    }

    /// <summary>プレイヤーのクリスタル攻撃を観測</summary>
    public void ObservePlayerCrystalAttack(int damage, int maxHP, int turn)
    {
        if (!_isActive) return;
        _realtimeAdapt.OnPlayerAttackedCrystal(damage, maxHP, turn);
    }

    /// <summary>プレイヤーの陣形を観測（ターン開始時に全駒位置を記録）</summary>
    public void ObservePlayerFormation(List<Status> playerUnits, Vector3 enemyCrystalPos, int turn)
    {
        if (!_isActive) return;
        _profiler.ObserveFormation(playerUnits, enemyCrystalPos, turn);
    }

    // ================================================================
    //  AIターン開始時
    // ================================================================
    public void OnTurnStart(TurnStrategy strategy, int threatLevel, AIBoardState board, int turn)
    {
        if (!_isActive) return;

        _currentStrategy = strategy;
        _threatLevel = threatLevel;
        UpdateMLScoreWeight(threatLevel);
        _brain.SetHeadWeightsForThreat(threatLevel);

        // プロファイル更新（5ターンごと）
        if (turn % 5 == 0)
            _profiler.UpdateProfile();

        // カウンター方針決定
        _counterStrategy.DecideCounterPlan(_profiler.Profile, board, turn);

        // リアルタイム適応計算
        _realtimeAdapt.ComputeAdaptation(board, turn);

        // 行動予測
        float[] boardFeatures = MLFeatureExtractor.ExtractBoardFeatures(board);
        float[] profileFeatures = _profiler.ToFeatureVector();
        var prediction = _behaviorPredictor.Predict(boardFeatures, profileFeatures);

        if (prediction.Confidence > 0.3f)
        {
            Debug.Log($"[MLIntegration] 行動予測: " +
                      $"攻撃={prediction.AttackProbability:F2} " +
                      $"移動={prediction.MoveProbability:F2} " +
                      $"建築={prediction.BuildProbability:F2} " +
                      $"信頼度={prediction.Confidence:F2}");
        }
    }

    // ================================================================
    //  行動評価 — 全サブシステムのスコアを統合
    // ================================================================
    public void EvaluateActions(List<AIAction> actions, AIBoardState board)
    {
        if (!_isActive || actions == null || actions.Count == 0) return;

        float[] profileFeatures = _profiler.ToFeatureVector();

        foreach (var action in actions)
        {
            float totalMLBonus = 0f;

            // ---- 1. MLBrain (3ヘッドNN) ----
            float[] features = BuildFullInput(board, action, profileFeatures);
            float mlValue = _brain.Predict(features);
            totalMLBonus += mlValue * 30f * _mlScoreWeight;

            // ---- 2. カウンター戦略ボーナス ----
            float counterBonus = _counterStrategy.GetCounterBonus(action, board);
            totalMLBonus += counterBonus;

            // ---- 3. リアルタイム適応ボーナス ----
            float adaptiveBonus = _realtimeAdapt.GetAdaptiveBonus(action, board);
            totalMLBonus += adaptiveBonus;

            // ---- 4. 行動予測ベースボーナス ----
            float predictiveBonus = _behaviorPredictor.GetPredictiveBonus(action, board);
            totalMLBonus += predictiveBonus;

            action.Score += totalMLBonus;
        }
    }

    // ================================================================
    //  入力ベクトル構築 (80次元: 盤面64 + プロファイル16)
    // ================================================================
    float[] BuildFullInput(AIBoardState board, AIAction action, float[] profileFeatures)
    {
        float[] actionFeatures = MLFeatureExtractor.ExtractActionFeatures(board, action);

        // 80次元配列を構築
        var input = new float[MLBrain.InputSize];

        // 盤面+行動特徴 (64次元)
        int copyLen = Mathf.Min(64, actionFeatures.Length);
        System.Array.Copy(actionFeatures, 0, input, 0, copyLen);

        // プロファイル特徴 (16次元)
        if (profileFeatures != null)
        {
            int pLen = Mathf.Min(16, profileFeatures.Length);
            System.Array.Copy(profileFeatures, 0, input, 64, pLen);
        }

        // メタ特徴を上書き
        if (actionFeatures.Length > 62)
        {
            input[62] = EncodeStrategy(_currentStrategy);
            input[63] = EncodePersonality(_personality);
        }

        return input;
    }

    // ================================================================
    //  行動実行後の記録
    // ================================================================
    public void RecordAction(AIAction action, AIBoardState board, bool success, int turn)
    {
        if (!_isActive) return;

        float[] profileFeatures = _profiler.ToFeatureVector();
        float[] features = BuildFullInput(board, action, profileFeatures);
        float reward = CalcImmediateReward(action, success);
        _buffer.RecordStep(features, reward, turn);
    }

    // ================================================================
    //  試合終了時の学習
    // ================================================================
    public void OnMatchEnd(bool playerWon, MatchAnalysis analysis)
    {
        if (!_isActive) return;

        float terminalReward = playerWon ? -0.8f : 0.8f;
        _buffer.FinalizeMatch(terminalReward);
        _trainer.TrainOnMatchEnd(playerWon, analysis);

        _profiler.OnMatchEnd();
        _realtimeAdapt.Reset();

        // 保存
        MLPersistence.SaveWeights(_brain, _threatLevel,
            _trainer.TotalMatchesTrained, _trainer.AverageLoss);
        MLPersistence.SaveBuffer(_buffer);
        MLPersistence.SaveBehaviorPredictor(_behaviorPredictor);
        MLPersistence.SavePlayerProfile(_profiler);

        Debug.Log($"[MLIntegration] 試合終了学習完了: {(playerWon ? "AI敗北" : "AI勝利")}  " +
                  $"カウンター方針={_counterStrategy.CurrentPlan}  " +
                  $"予測精度={_behaviorPredictor.Accuracy:F2}  " +
                  $"バッファ={_buffer.CurrentSize}  損失={_trainer.LastEpochLoss:F4}");
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
            _brain.SetHeadWeightsForThreat(newLevel);
            UpdateMLScoreWeight(newLevel);
        }
    }

    // ================================================================
    //  盤面評価（探索末端用）
    // ================================================================
    public float EvaluateBoard(AIBoardState board)
    {
        if (!_isActive) return 0f;
        float[] features = new float[MLBrain.InputSize];
        float[] boardF = MLFeatureExtractor.ExtractBoardFeatures(board);
        int len = Mathf.Min(64, boardF.Length);
        System.Array.Copy(boardF, 0, features, 0, len);
        float[] pf = _profiler.ToFeatureVector();
        int pLen = Mathf.Min(16, pf.Length);
        System.Array.Copy(pf, 0, features, 64, pLen);
        return _brain.Predict(features) * 20f * _mlScoreWeight;
    }

    // ================================================================
    //  即時報酬計算
    // ================================================================
    float CalcImmediateReward(AIAction action, bool success)
    {
        if (!success) return -0.1f;
        float reward = 0f;
        switch (action.ActionType)
        {
            case AIActionType.Attack:
                if (action.Unit != null && action.TargetUnit != null)
                {
                    int dmg = Mathf.Max(0, 1 + (action.Unit.ATK / 6) +
                        ((action.Unit.ATK / 2) - (action.TargetUnit.DEF / 4)));
                    reward += Mathf.Clamp(dmg * 0.02f, 0f, 0.3f);
                    if (dmg >= action.TargetUnit.HP) reward += 0.3f;
                }
                break;
            case AIActionType.SkillUse:    reward += 0.1f; break;
            case AIActionType.Build:       reward += 0.15f; break;
            case AIActionType.Summon:      reward += 0.1f; break;
            case AIActionType.Retreat:
                if (action.Unit != null && action.Unit.MaxHP > 0 &&
                    (float)action.Unit.HP / action.Unit.MaxHP < 0.3f)
                    reward += 0.15f;
                else reward += 0.05f;
                break;
            case AIActionType.Support:     reward += 0.1f; break;
            case AIActionType.Surround:    reward += 0.12f; break;
            case AIActionType.Move:        reward += 0.02f; break;
            case AIActionType.DefenseRepos:reward += 0.08f; break;
            case AIActionType.SubCrystal:  reward += 0.12f; break;
            case AIActionType.Wait:        reward -= 0.02f; break;
        }
        return Mathf.Clamp(reward, -0.5f, 0.5f);
    }

    // ================================================================
    //  デバッグ
    // ================================================================
    public string GetDebugInfo()
    {
        if (!_isActive) return "ML: 無効";
        return $"ML: 有効  重み={_mlScoreWeight:F2}  " +
               $"パラメ={_brain.ParameterCount}  " +
               $"バッファ={_buffer.CurrentSize}/{_buffer.Capacity}  " +
               $"学習試合={_trainer.TotalMatchesTrained}  " +
               $"カウンター={_counterStrategy.CurrentPlan}  " +
               $"予測精度={_behaviorPredictor.Accuracy:F2}  " +
               $"プロファイル信頼度={_profiler.Profile.Confidence:F2}";
    }

    // ================================================================
    //  エンコーディングヘルパー
    // ================================================================
    static float EncodeStrategy(TurnStrategy s)
    {
        switch (s)
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
}
