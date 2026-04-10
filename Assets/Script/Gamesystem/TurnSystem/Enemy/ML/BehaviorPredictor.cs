using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  BehaviorPredictor — プレイヤーの次の行動を予測するニューラルネットワーク
//
//  ARC Raiders式: 「プレイヤーが次に何をするか」を予測する。
//  入力: 盤面状態 + プレイヤープロファイル + 直近の行動系列
//  出力: 予測分布 (攻撃確率, 移動確率, 建築確率, 防衛確率, etc.)
//       + 予測ターゲット位置 + 予測タイミング
//
//  学習: プレイヤーの実際の行動を教師データとして逐次学習。
//  戦えば戦うほど予測精度が上がり、先手を打てるようになる。
// =====================================================================
public class BehaviorPredictor
{
    // ---- ネットワーク構成 ----
    // 入力: 盤面特徴(32) + プロファイル(16) + 直近行動(16) = 64
    // 出力: 行動確率分布(6) + ターゲット方向(4) + タイミング(2) = 12
    const int InputSize = 64;
    const int HiddenSize = 48;
    const int OutputSize = 12;

    // ---- 重み ----
    float[,] _w1;  // [InputSize, HiddenSize]
    float[] _b1;   // [HiddenSize]
    float[,] _w2;  // [HiddenSize, OutputSize]
    float[] _b2;   // [OutputSize]

    // ---- 学習用キャッシュ ----
    float[] _lastInput;
    float[] _a1;    // Hidden activation
    float[] _lastOutput;
    float _learningRate = 0.005f;

    // ---- モメンタムSGD ----
    float _momentum = 0.9f;
    float[,] _vw1;  // モメンタム: w1
    float[] _vb1;   // モメンタム: b1
    float[,] _vw2;  // モメンタム: w2
    float[] _vb2;   // モメンタム: b2

    // ---- タイミング学習用 ----
    int _lastAttackTurn = -1;
    int _turnsSinceLastAttack = 0;

    // ---- 直近の行動系列 ----
    List<ObservedAction> _recentActions = new List<ObservedAction>();
    const int MaxRecentActions = 8;

    struct ObservedAction
    {
        public int Turn;
        public int ActionCode;    // 0=攻撃, 1=移動, 2=建築, 3=スキル, 4=待機, 5=不明
        public Vector3Int Position;
        public Kind UnitKind;
    }

    // ---- 予測結果 ----
    public struct Prediction
    {
        public float AttackProbability;
        public float MoveProbability;
        public float BuildProbability;
        public float SkillProbability;
        public float DefendProbability;
        public float WaitProbability;
        public Vector3 PredictedDirection;    // 予測移動方向
        public float PredictedDistance;        // 予測移動距離
        public float PredictedTiming;         // 次の攻撃まで何ターンか
        public float Confidence;              // 予測の自信度
    }

    Prediction _lastPrediction;
    public Prediction LastPrediction => _lastPrediction;

    // ---- 統計 ----
    int _totalPredictions;
    int _correctPredictions;
    float _runningAccuracy;
    public float Accuracy => _totalPredictions > 0 ? _runningAccuracy : 0f;
    public int TotalPredictions => _totalPredictions;

    // ---- 乱数 ----
    System.Random _rng;

    // ---- 高速化用の再利用バッファ ----
    readonly float[] _forwardA1 = new float[HiddenSize];
    readonly float[] _forwardOutput = new float[OutputSize];
    readonly float[] _buildInputBuf = new float[InputSize];

    public BehaviorPredictor(int seed = -1)
    {
        _rng = seed >= 0 ? new System.Random(seed) : new System.Random();
        InitializeWeights();
    }

    void InitializeWeights()
    {
        float scale1 = Mathf.Sqrt(2f / (InputSize + HiddenSize));
        float scale2 = Mathf.Sqrt(2f / (HiddenSize + OutputSize));

        _w1 = new float[InputSize, HiddenSize];
        _b1 = new float[HiddenSize];
        _w2 = new float[HiddenSize, OutputSize];
        _b2 = new float[OutputSize];

        for (int i = 0; i < InputSize; i++)
            for (int j = 0; j < HiddenSize; j++)
                _w1[i, j] = (float)NextGaussian() * scale1;
        for (int i = 0; i < HiddenSize; i++)
            for (int j = 0; j < OutputSize; j++)
                _w2[i, j] = (float)NextGaussian() * scale2;

        // モメンタムバッファ初期化
        _vw1 = new float[InputSize, HiddenSize];
        _vb1 = new float[HiddenSize];
        _vw2 = new float[HiddenSize, OutputSize];
        _vb2 = new float[OutputSize];
    }

    // ================================================================
    //  行動の観測（プレイヤーが行動するたびに呼ぶ）
    // ================================================================
    public void ObserveAction(int turn, int actionCode, Vector3 position, Kind unitKind)
    {
        _recentActions.Add(new ObservedAction
        {
            Turn = turn,
            ActionCode = actionCode,
            Position = GridHelper.ToGridXZ(position),
            UnitKind = unitKind
        });

        // 最大数を超えたら古いものを削除
        while (_recentActions.Count > MaxRecentActions)
            _recentActions.RemoveAt(0);
    }

    // ================================================================
    //  予測（AIターン開始時に呼ぶ）
    //  入力: 盤面特徴 + プロファイル + 直近行動
    //  出力: 次のプレイヤーターンの行動予測
    // ================================================================
    public Prediction Predict(float[] boardFeatures, float[] profileFeatures)
    {
        float[] input = BuildInput(boardFeatures, profileFeatures);
        float[] output = Forward(input);

        // 出力を予測に変換
        // [0-5]: 行動確率 (Softmax)
        float[] actionProbs = Softmax(output, 0, 6);

        _lastPrediction = new Prediction
        {
            AttackProbability = actionProbs[0],
            MoveProbability = actionProbs[1],
            BuildProbability = actionProbs[2],
            SkillProbability = actionProbs[3],
            DefendProbability = actionProbs[4],
            WaitProbability = actionProbs[5],
            PredictedDirection = new Vector3(
                Mathf.Clamp(output[6], -1f, 1f),
                0,
                Mathf.Clamp(output[7], -1f, 1f)).normalized,
            PredictedDistance = Mathf.Clamp(output[8] * 10f, 0f, 20f),
            PredictedTiming = Mathf.Clamp(output[9] * 5f, 0f, 10f),
            Confidence = CalculateConfidence(actionProbs)
        };

        _totalPredictions++;
        return _lastPrediction;
    }

    // ================================================================
    //  学習（プレイヤーの実際の行動と予測を比較して重みを更新）
    // ================================================================
    public void Learn(int actualActionCode, Vector3 actualPosition)
    {
        if (_lastInput == null) return;

        // 教師信号を構築
        float[] target = new float[OutputSize];

        // 行動のone-hot
        for (int i = 0; i < 6; i++)
            target[i] = (i == actualActionCode) ? 1f : 0f;

        // 方向（直前の行動位置からの相対方向）
        if (_recentActions.Count >= 2)
        {
            var last = _recentActions[_recentActions.Count - 1];
            var prev = _recentActions[_recentActions.Count - 2];
            Vector3 dir = (new Vector3(last.Position.x, 0, last.Position.z) -
                          new Vector3(prev.Position.x, 0, prev.Position.z)).normalized;
            target[6] = dir.x;
            target[7] = dir.z;
        }

        // 距離（直前位置からの移動距離を正規化）
        if (_recentActions.Count >= 2)
        {
            var prev = _recentActions[_recentActions.Count - 2];
            Vector3 prevPos = new Vector3(prev.Position.x, 0, prev.Position.z);
            target[8] = Mathf.Clamp01(Vector3.Distance(actualPosition, prevPos) / 10f);
        }
        else
        {
            target[8] = 0.5f; // 初回はデフォルト値
        }

        // タイミング（攻撃間隔を学習）
        if (actualActionCode == 0) // 攻撃行動
        {
            if (_lastAttackTurn >= 0)
                _turnsSinceLastAttack = _recentActions.Count > 0
                    ? _recentActions[_recentActions.Count - 1].Turn - _lastAttackTurn : 0;
            _lastAttackTurn = _recentActions.Count > 0
                ? _recentActions[_recentActions.Count - 1].Turn : 0;
        }
        target[9] = Mathf.Clamp01(_turnsSinceLastAttack / 10f);

        // 正解判定
        int predictedAction = 0;
        float maxProb = 0f;
        float[] probs = { _lastPrediction.AttackProbability, _lastPrediction.MoveProbability,
                          _lastPrediction.BuildProbability, _lastPrediction.SkillProbability,
                          _lastPrediction.DefendProbability, _lastPrediction.WaitProbability };
        for (int i = 0; i < 6; i++)
        {
            if (probs[i] > maxProb) { maxProb = probs[i]; predictedAction = i; }
        }

        if (predictedAction == actualActionCode)
        {
            _correctPredictions++;
        }

        // 指数移動平均で精度を追跡
        float correct = (predictedAction == actualActionCode) ? 1f : 0f;
        _runningAccuracy = _runningAccuracy * 0.95f + correct * 0.05f;

        // 逆伝播
        Backward(target);
    }

    // ================================================================
    //  予測の行動評価への変換
    //  予測結果に基づいてAIの行動スコアを修正する
    // ================================================================
    public float GetPredictiveBonus(AIAction action, AIBoardState board)
    {
        if (_totalPredictions < 5) return 0f; // データ不足

        float bonus = 0f;
        float conf = _lastPrediction.Confidence;

        // プレイヤーが攻撃してきそう → 防衛を強化
        if (_lastPrediction.AttackProbability > 0.5f)
        {
            if (action.ActionType == AIActionType.DefenseRepos) bonus += 12f * conf;
            if (action.ActionType == AIActionType.Retreat) bonus += 8f * conf;
            if (action.ActionType == AIActionType.Support) bonus += 6f * conf;

            // 予測攻撃方向の防衛を強化
            if (action.ActionType == AIActionType.Move && action.Unit != null)
            {
                Vector3 predicted = _lastPrediction.PredictedDirection;
                Vector3 toTarget = (action.TargetPos - board.EnemyCrystalPos).normalized;
                float alignment = Vector3.Dot(predicted, toTarget);
                if (alignment > 0.5f) // 予測攻撃方向に向かう → 迎撃位置
                    bonus += 10f * conf * alignment;
            }
        }

        // プレイヤーが建築しそう → 速攻チャンス
        if (_lastPrediction.BuildProbability > 0.5f)
        {
            if (action.ActionType == AIActionType.Attack) bonus += 8f * conf;
            if (action.ActionType == AIActionType.Move)
            {
                // 前進を促す
                if (board.CanUsePlayerCrystalAsTarget() && action.Unit != null)
                {
                    float curDist = Vector3.Distance(action.Unit.transform.position, board.PlayerCrystalPos);
                    float newDist = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
                    if (newDist < curDist)
                        bonus += 5f * conf;
                }
            }
        }

        // プレイヤーが防衛しそう → 奇襲 or 経済投資
        if (_lastPrediction.DefendProbability > 0.4f)
        {
            if (action.ActionType == AIActionType.Surround) bonus += 8f * conf;
            if (action.ActionType == AIActionType.Build) bonus += 5f * conf;
        }

        return bonus;
    }

    // ================================================================
    //  順伝播
    // ================================================================
    float[] Forward(float[] input)
    {
        _lastInput = input;

        // Hidden layer (ReLU) — 事前確保バッファ再利用
        _a1 = _forwardA1;
        for (int j = 0; j < HiddenSize; j++)
        {
            float sum = _b1[j];
            for (int i = 0; i < InputSize; i++)
                sum += input[i] * _w1[i, j];
            _a1[j] = sum > 0 ? sum : 0; // ReLU
        }

        // Output layer (linear) — 事前確保バッファ再利用
        _lastOutput = _forwardOutput;
        for (int j = 0; j < OutputSize; j++)
        {
            float sum = _b2[j];
            for (int i = 0; i < HiddenSize; i++)
                sum += _a1[i] * _w2[i, j];
            _lastOutput[j] = sum;
        }

        return _lastOutput;
    }

    // ================================================================
    //  逆伝播（モメンタムSGD + 勾配クリッピング）
    // ================================================================
    void Backward(float[] target)
    {
        // Output層のエラー
        float[] dOut = new float[OutputSize];
        for (int j = 0; j < OutputSize; j++)
            dOut[j] = _lastOutput[j] - target[j];

        // 勾配ノルム計算（クリッピング用）
        float gradNorm = 0f;
        for (int j = 0; j < OutputSize; j++) gradNorm += dOut[j] * dOut[j];
        gradNorm = Mathf.Sqrt(gradNorm);
        float clipScale = (gradNorm > 3f) ? 3f / gradNorm : 1f;
        for (int j = 0; j < OutputSize; j++) dOut[j] *= clipScale;

        // Hidden → Output の勾配 (モメンタムSGD)
        float[] dA1 = new float[HiddenSize];
        for (int j = 0; j < OutputSize; j++)
        {
            _vb2[j] = _momentum * _vb2[j] + _learningRate * dOut[j];
            _b2[j] -= _vb2[j];
            for (int i = 0; i < HiddenSize; i++)
            {
                dA1[i] += _w2[i, j] * dOut[j];
                float grad = _a1[i] * dOut[j];
                _vw2[i, j] = _momentum * _vw2[i, j] + _learningRate * grad;
                _w2[i, j] -= _vw2[i, j];
            }
        }

        // Input → Hidden の勾配 (ReLU + モメンタム)
        for (int j = 0; j < HiddenSize; j++)
        {
            float dz = _a1[j] > 0 ? dA1[j] : 0;
            _vb1[j] = _momentum * _vb1[j] + _learningRate * dz;
            _b1[j] -= _vb1[j];
            for (int i = 0; i < InputSize; i++)
            {
                float grad = _lastInput[i] * dz;
                _vw1[i, j] = _momentum * _vw1[i, j] + _learningRate * grad;
                _w1[i, j] -= _vw1[i, j];
            }
        }
    }

    // ================================================================
    //  入力ベクトル構築
    // ================================================================
    float[] BuildInput(float[] boardFeatures, float[] profileFeatures)
    {
        // 事前確保バッファ再利用 — GCゼロ
        System.Array.Clear(_buildInputBuf, 0, InputSize);

        // 盤面特徴 (最大32次元)
        int bLen = Mathf.Min(32, boardFeatures != null ? boardFeatures.Length : 0);
        if (bLen > 0 && boardFeatures != null)
            System.Array.Copy(boardFeatures, 0, _buildInputBuf, 0, bLen);

        // プロファイル特徴 (最大16次元)
        int pLen = Mathf.Min(16, profileFeatures != null ? profileFeatures.Length : 0);
        if (pLen > 0 && profileFeatures != null)
            System.Array.Copy(profileFeatures, 0, _buildInputBuf, 32, pLen);

        // 直近行動系列 (最大16次元 = 8行動 × 2特徴)
        for (int i = 0; i < MaxRecentActions && i < _recentActions.Count; i++)
        {
            int baseIdx = 48 + i * 2;
            if (baseIdx + 1 < InputSize)
            {
                _buildInputBuf[baseIdx] = _recentActions[i].ActionCode / 5f * 2f - 1f;
                _buildInputBuf[baseIdx + 1] = EncodeKind(_recentActions[i].UnitKind);
            }
        }

        return _buildInputBuf;
    }

    // ================================================================
    //  ユーティリティ
    // ================================================================
    float[] Softmax(float[] values, int start, int count)
    {
        float[] result = new float[count];
        float max = float.MinValue;
        for (int i = 0; i < count; i++)
            if (values[start + i] > max) max = values[start + i];

        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            result[i] = Mathf.Exp(values[start + i] - max);
            sum += result[i];
        }
        if (sum > 0)
            for (int i = 0; i < count; i++)
                result[i] /= sum;
        return result;
    }

    float CalculateConfidence(float[] probs)
    {
        // 最大確率が高いほど自信あり（エントロピーの逆数的）
        float maxProb = 0f;
        foreach (float p in probs) if (p > maxProb) maxProb = p;

        // 精度によるスケーリング
        float accuracyFactor = Mathf.Clamp01(_runningAccuracy * 2f);
        return maxProb * accuracyFactor;
    }

    static float EncodeKind(Kind k)
    {
        switch (k)
        {
            case Kind.Knight:       return 0.0f;
            case Kind.Archer:       return 0.2f;
            case Kind.Magic:        return 0.4f;
            case Kind.Assassin:     return 0.6f;
            case Kind.Scout:        return -0.4f;
            case Kind.Priest:       return -0.6f;
            case Kind.Guardian:     return -0.2f;
            case Kind.Crossbow:     return 0.3f;
            case Kind.Magicsniper:  return 0.8f;
            case Kind.Bomber:       return 1.0f;
            case Kind.King:         return -0.8f;
            default: return 0f;
        }
    }

    double NextGaussian()
    {
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = 1.0 - _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    // ================================================================
    //  セーブ/ロード
    // ================================================================
    public float[] GetWeightsFlat()
    {
        int totalSize = InputSize * HiddenSize + HiddenSize + HiddenSize * OutputSize + OutputSize;
        var flat = new float[totalSize];
        int idx = 0;
        for (int i = 0; i < InputSize; i++)
            for (int j = 0; j < HiddenSize; j++)
                flat[idx++] = _w1[i, j];
        for (int j = 0; j < HiddenSize; j++)
            flat[idx++] = _b1[j];
        for (int i = 0; i < HiddenSize; i++)
            for (int j = 0; j < OutputSize; j++)
                flat[idx++] = _w2[i, j];
        for (int j = 0; j < OutputSize; j++)
            flat[idx++] = _b2[j];
        return flat;
    }

    public void LoadWeightsFlat(float[] flat)
    {
        if (flat == null) return;
        int expected = InputSize * HiddenSize + HiddenSize + HiddenSize * OutputSize + OutputSize;
        if (flat.Length != expected) return;

        int idx = 0;
        for (int i = 0; i < InputSize; i++)
            for (int j = 0; j < HiddenSize; j++)
                _w1[i, j] = flat[idx++];
        for (int j = 0; j < HiddenSize; j++)
            _b1[j] = flat[idx++];
        for (int i = 0; i < HiddenSize; i++)
            for (int j = 0; j < OutputSize; j++)
                _w2[i, j] = flat[idx++];
        for (int j = 0; j < OutputSize; j++)
            _b2[j] = flat[idx++];
    }

    public float SaveRunningAccuracy { get => _runningAccuracy; set => _runningAccuracy = value; }
    public int SaveTotalPredictions { get => _totalPredictions; set => _totalPredictions = value; }
    public int SaveCorrectPredictions { get => _correctPredictions; set => _correctPredictions = value; }
}
