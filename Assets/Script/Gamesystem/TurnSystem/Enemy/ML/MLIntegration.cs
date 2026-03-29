using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MLIntegration — ARC Raiders式 機械学習AI 統合コントローラ（v3: 完全版）
//
//  v3 追加機能:
//  - 探索戦略（ε-greedy + UCB-like不確実性ボーナス）
//  - 自己対戦シミュレーション（試合終了時に仮想対局で追加学習）
//  - カウンター戦略の有効性追跡
//  - プロファイル更新の適応的頻度
//  - 全サブシステムのバグ修正連携
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

    // ---- 探索パラメータ（ε-greedy + UCB） ----
    float _explorationRate = 0.15f;         // ε: ランダム探索確率
    float _explorationDecay = 0.995f;       // εの減衰率（試合ごと）
    float _minExplorationRate = 0.02f;      // εの下限
    float _uncertaintyBonus = 5f;           // UCB的不確実性ボーナス係数
    System.Random _exploreRng;

    // ---- 自己対戦シミュレーション ----
    int _selfPlayStepsPerMatch = 50;        // 試合後の仮想学習ステップ数

    // ---- 統計 ----
    public bool IsActive => _isActive;
    public int TotalTrainingSteps => _brain.TotalTrainingSteps;
    public int TotalMatchesTrained => _trainer.TotalMatchesTrained;
    public float AverageLoss => _trainer.AverageLoss;
    public float LastLoss => _brain.LastLoss;
    public int BufferSize => _buffer.CurrentSize;
    public int ParameterCount => _brain.ParameterCount;
    public float ExplorationRate => _explorationRate;

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

        _exploreRng = seed >= 0 ? new System.Random(seed + 777) : new System.Random();

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

            // 学習が進むほど探索を減らす
            int matchesTrained = _trainer.TotalMatchesTrained;
            for (int i = 0; i < matchesTrained; i++)
                _explorationRate = Mathf.Max(_minExplorationRate,
                    _explorationRate * _explorationDecay);

            Debug.Log("=== [MLIntegration] ARC Raiders式 ML AI v3 ========================");
            Debug.Log($"[MLIntegration] 脅威度={threatLevel}  重み読込={loaded}");
            Debug.Log($"[MLIntegration] パラメータ={_brain.ParameterCount}  " +
                      $"バッファ={_buffer.CurrentSize}/{_buffer.Capacity}");
            Debug.Log($"[MLIntegration] ヘッド重み: 戦略={_brain.HeadWeights[0]:F2} " +
                      $"戦術={_brain.HeadWeights[1]:F2} 経済={_brain.HeadWeights[2]:F2}");
            Debug.Log($"[MLIntegration] 探索率={_explorationRate:F3}  " +
                      $"カリキュラム={_trainer.CurrentPhase}");
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
    //  プレイヤーターンの観測
    // ================================================================

    /// <summary>プレイヤーの攻撃を観測</summary>
    public void ObservePlayerAttack(Status attacker, Status target, int damage,
        Vector3 enemyCrystalPos, int turn)
    {
        if (!_isActive) return;
        _profiler.ObserveAttack(attacker, target, enemyCrystalPos, turn);
        _realtimeAdapt.OnPlayerAttackedUnit(target, attacker, damage, turn);

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

    /// <summary>プレイヤーの建築/召喚を観測</summary>
    public void ObservePlayerBuild(Vector3 position, int turn)
    {
        if (!_isActive) return;
        _behaviorPredictor.ObserveAction(turn, 2, position, Kind.Crystal);
        _behaviorPredictor.Learn(2, position);
    }

    /// <summary>プレイヤーのクリスタル攻撃を観測</summary>
    public void ObservePlayerCrystalAttack(int damage, int maxHP, int turn)
    {
        if (!_isActive) return;
        _realtimeAdapt.OnPlayerAttackedCrystal(damage, maxHP, turn);
    }

    /// <summary>プレイヤーの陣形を観測</summary>
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

        // 適応的プロファイル更新: 序盤は毎ターン、安定後は3ターンごと
        bool shouldUpdate = (_profiler.Profile.Confidence < 0.3f)
            ? true : (turn % 3 == 0);
        if (shouldUpdate)
            _profiler.UpdateProfile();

        // カウンター方針決定
        _counterStrategy.DecideCounterPlan(_profiler.Profile, board, turn);

        // リアルタイム適応計算
        _realtimeAdapt.ComputeAdaptation(board, turn);

        // 行動予測
        float[] boardFeatures = MLFeatureExtractor.ExtractBoardFeatures(board);
        float[] profileFeatures = _profiler.ToFeatureVector();
        var prediction = _behaviorPredictor.Predict(boardFeatures, profileFeatures);

        // シーケンスパターン検知のログ
        string freqSeq = _realtimeAdapt.GetMostFrequentSequence();

        if (prediction.Confidence > 0.3f || freqSeq.Length > 0)
        {
            Debug.Log($"[MLIntegration] 行動予測: " +
                      $"攻撃={prediction.AttackProbability:F2} " +
                      $"移動={prediction.MoveProbability:F2} " +
                      $"建築={prediction.BuildProbability:F2} " +
                      $"信頼度={prediction.Confidence:F2}" +
                      (freqSeq.Length > 0 ? $"  頻出パターン={freqSeq}" : ""));
        }
    }

    // ================================================================
    //  行動評価 — 全サブシステムのスコアを統合 + 探索
    // ================================================================
    public void EvaluateActions(List<AIAction> actions, AIBoardState board)
    {
        if (!_isActive || actions == null || actions.Count == 0) return;

        float[] profileFeatures = _profiler.ToFeatureVector();

        // ---- ε-greedy探索: 確率εでランダムにスコアを撹乱 ----
        bool isExploring = _exploreRng.NextDouble() < _explorationRate;

        foreach (var action in actions)
        {
            float totalMLBonus = 0f;

            // ---- 1. MLBrain (3ヘッドNN) ----
            float[] features = BuildFullInput(board, action, profileFeatures);
            float mlValue = _brain.Predict(features);
            totalMLBonus += mlValue * 30f * _mlScoreWeight;

            // ---- 2. UCB的不確実性ボーナス ----
            // 学習初期ほど不確実性が高い → 未知の行動を積極的に試す
            float uncertainty = 1f / (1f + _brain.TotalTrainingSteps * 0.001f);
            float ucbBonus = _uncertaintyBonus * uncertainty *
                (float)(_exploreRng.NextDouble() * 2.0 - 1.0);
            totalMLBonus += ucbBonus;

            // ---- 3. カウンター戦略ボーナス ----
            float counterBonus = _counterStrategy.GetCounterBonus(action, board);
            totalMLBonus += counterBonus;

            // ---- 4. リアルタイム適応ボーナス ----
            float adaptiveBonus = _realtimeAdapt.GetAdaptiveBonus(action, board);
            totalMLBonus += adaptiveBonus;

            // ---- 5. 行動予測ベースボーナス ----
            float predictiveBonus = _behaviorPredictor.GetPredictiveBonus(action, board);
            totalMLBonus += predictiveBonus;

            // ---- 探索モード: スコアにノイズを追加 ----
            if (isExploring)
            {
                float noise = (float)(_exploreRng.NextDouble() * 20.0 - 10.0);
                totalMLBonus += noise;
            }

            action.Score += totalMLBonus;
        }
    }

    // ================================================================
    //  入力ベクトル構築 (80次元: 盤面64 + プロファイル16)
    // ================================================================
    float[] BuildFullInput(AIBoardState board, AIAction action, float[] profileFeatures)
    {
        float[] actionFeatures = MLFeatureExtractor.ExtractActionFeatures(board, action);

        var input = new float[MLBrain.InputSize];

        int copyLen = Mathf.Min(64, actionFeatures.Length);
        System.Array.Copy(actionFeatures, 0, input, 0, copyLen);

        if (profileFeatures != null)
        {
            int pLen = Mathf.Min(16, profileFeatures.Length);
            System.Array.Copy(profileFeatures, 0, input, 64, pLen);
        }

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
    //  試合終了時の学習 + 自己対戦シミュレーション
    // ================================================================
    public void OnMatchEnd(bool playerWon, MatchAnalysis analysis)
    {
        if (!_isActive) return;

        float terminalReward = playerWon ? -0.8f : 0.8f;
        _buffer.FinalizeMatch(terminalReward);
        _trainer.TrainOnMatchEnd(playerWon, analysis);

        // カウンター戦略の有効性を記録
        _counterStrategy.RecordPlanEffectiveness(!playerWon);

        _profiler.OnMatchEnd();
        _realtimeAdapt.Reset();

        // ---- 自己対戦シミュレーション ----
        // バッファ内の経験を摂動させて仮想的な追加学習を行う
        // これにより、実際の対戦なしでも学習量を増やせる
        RunSelfPlaySimulation();

        // 探索率の減衰
        _explorationRate = Mathf.Max(_minExplorationRate,
            _explorationRate * _explorationDecay);

        // 保存
        MLPersistence.SaveWeights(_brain, _threatLevel,
            _trainer.TotalMatchesTrained, _trainer.AverageLoss);
        MLPersistence.SaveBuffer(_buffer);
        MLPersistence.SaveBehaviorPredictor(_behaviorPredictor);
        MLPersistence.SavePlayerProfile(_profiler);

        Debug.Log($"[MLIntegration] 試合終了学習完了: {(playerWon ? "AI敗北" : "AI勝利")}  " +
                  $"カウンター={_counterStrategy.CurrentPlan}+{_counterStrategy.SecondaryPlan}  " +
                  $"予測精度={_behaviorPredictor.Accuracy:F2}  " +
                  $"探索率={_explorationRate:F3}  フェーズ={_trainer.CurrentPhase}  " +
                  $"バッファ={_buffer.CurrentSize}  損失={_trainer.LastEpochLoss:F4}");
    }

    // ================================================================
    //  自己対戦シミュレーション
    //  バッファ内の既存経験に小さなノイズを加えて追加学習。
    //  ARC Raidersの「対戦していない時も学習する」を再現。
    // ================================================================
    void RunSelfPlaySimulation()
    {
        if (_buffer.CurrentSize < 32) return;

        int steps = Mathf.Min(_selfPlayStepsPerMatch, _buffer.CurrentSize / 2);
        float startTime = Time.realtimeSinceStartup;

        for (int i = 0; i < steps; i++)
        {
            // バッファからランダムサンプル
            var batch = _buffer.SampleBatch(1, _exploreRng);
            if (batch.Count == 0) continue;

            var exp = batch[0];
            float[] input = new float[MLBrain.InputSize];
            if (exp.Features != null)
            {
                int len = Mathf.Min(exp.Features.Length, MLBrain.InputSize);
                System.Array.Copy(exp.Features, 0, input, 0, len);
            }

            // 入力にノイズを追加（データ拡張）
            for (int j = 0; j < input.Length; j++)
                input[j] += (float)(_exploreRng.NextDouble() * 0.1 - 0.05);

            // 順伝播 + 逆伝播
            _brain.Forward(input);

            // ターゲットもわずかに摂動（過学習防止）
            float noise = (float)(_exploreRng.NextDouble() * 0.1 - 0.05);
            float t = Mathf.Clamp(exp.TDTarget + noise, -1f, 1f);
            float[] targets = new float[] {
                Mathf.Clamp(t * 0.9f, -1f, 1f),   // Strategy（やや保守的）
                Mathf.Clamp(t * 1.1f, -1f, 1f),   // Tactics（やや積極的）
                Mathf.Clamp(t * 0.8f, -1f, 1f)    // Economy（さらに保守的）
            };
            _brain.Backward(targets);
        }

        float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
        if (steps > 0)
            Debug.Log($"[MLIntegration] 自己対戦シミュレーション: {steps}ステップ  所要={elapsed:F1}ms");
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
    //  即時報酬計算（報酬シグナルを強化）
    // ================================================================
    float CalcImmediateReward(AIAction action, bool success)
    {
        if (!success) return -0.15f;
        float reward = 0f;
        switch (action.ActionType)
        {
            case AIActionType.Attack:
                if (action.Unit != null && action.TargetUnit != null)
                {
                    int dmg = Mathf.Max(0, 1 + (action.Unit.ATK / 6) +
                        ((action.Unit.ATK / 2) - (action.TargetUnit.DEF / 4)));
                    reward += Mathf.Clamp(dmg * 0.03f, 0f, 0.4f);
                    if (dmg >= action.TargetUnit.HP) reward += 0.4f; // キル報酬を強化
                }
                break;
            case AIActionType.SkillUse:    reward += 0.15f; break;
            case AIActionType.Build:       reward += 0.2f; break;
            case AIActionType.Summon:      reward += 0.15f; break;
            case AIActionType.Retreat:
                if (action.Unit != null && action.Unit.MaxHP > 0 &&
                    (float)action.Unit.HP / action.Unit.MaxHP < 0.3f)
                    reward += 0.25f; // 瀕死撤退の報酬を強化
                else reward += 0.05f;
                break;
            case AIActionType.Support:     reward += 0.12f; break;
            case AIActionType.Surround:    reward += 0.18f; break;
            case AIActionType.Move:        reward += 0.03f; break;
            case AIActionType.DefenseRepos:reward += 0.12f; break;
            case AIActionType.SubCrystal:  reward += 0.15f; break;
            case AIActionType.Wait:        reward -= 0.05f; break;
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
               $"探索率={_explorationRate:F3}  " +
               $"フェーズ={_trainer.CurrentPhase}  " +
               $"カウンター={_counterStrategy.CurrentPlan}+{_counterStrategy.SecondaryPlan}  " +
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
