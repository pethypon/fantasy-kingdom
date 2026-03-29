using System;
using UnityEngine;

// =====================================================================
//  MLBrain — マルチヘッドニューラルネットワーク（書き直し版）
//
//  旧: Input(64)→Hidden→Output(1) の単一評価
//  新: Input(80)→SharedHidden(128)→3つの専門ヘッド
//       ├─ StrategyHead(32→1): 戦略的価値（長期的な勝率予測）
//       ├─ TacticsHead(32→1):  戦術的価値（即時の有利不利）
//       └─ EconomyHead(32→1):  経済的価値（資源・生産効率）
//
//  3つのヘッドの重み付き合計が最終スコア。
//  脅威度に応じて各ヘッドの重みが動的に変化する。
//  プレイヤープロファイル16次元も入力に含む(64盤面+16プロファイル=80)。
//
//  Xavier初期化 + Adam最適化 + 勾配クリッピング + L2正則化。
// =====================================================================
public class MLBrain
{
    // ---- ネットワーク構成 ----
    public const int InputSize  = 80;   // 盤面64 + プロファイル16
    const int SharedHiddenSize = 128;
    const int HeadHiddenSize = 32;
    const int HeadOutputSize = 1;
    public const int NumHeads = 3;      // Strategy, Tactics, Economy

    // ---- 共有層: Input → SharedHidden ----
    public float[,] WShared;    // [InputSize, SharedHiddenSize]
    public float[]  BShared;    // [SharedHiddenSize]

    // ---- ヘッド別パラメータ [head][...] ----
    // Head Hidden: SharedHidden → HeadHidden
    public float[][,] WHead;    // [NumHeads][SharedHiddenSize, HeadHiddenSize]
    public float[][] BHead;     // [NumHeads][HeadHiddenSize]
    // Head Output: HeadHidden → Output
    public float[][,] WOut;     // [NumHeads][HeadHiddenSize, HeadOutputSize]
    public float[][] BOut;      // [NumHeads][HeadOutputSize]

    // ---- Adam状態 ----
    public float[,] MWShared, VWShared;
    public float[] MBShared, VBShared;
    public float[][,] MWHead, VWHead, MWOut, VWOut;
    public float[][] MBHead, VBHead, MBOut, VBOut;
    public int AdamStep;

    // ---- 中間値キャッシュ ----
    float[] _lastInput;
    float[] _zShared, _aShared;
    float[][] _zHead, _aHead;  // [NumHeads][HeadHiddenSize]
    float[][] _zOut;            // [NumHeads][HeadOutputSize]
    float[] _headOutputs;       // [NumHeads]

    // ---- ハイパーパラメータ ----
    public float LearningRate = 0.001f;
    public float AdamBeta1 = 0.9f;
    public float AdamBeta2 = 0.999f;
    public float AdamEpsilon = 1e-8f;
    public float L2Lambda = 1e-4f;
    public float GradClipNorm = 5.0f;

    // ---- Dropout（過学習防止） ----
    public float DropoutRate = 0.15f;       // 共有層のDropout率
    public float HeadDropoutRate = 0.1f;    // ヘッド層のDropout率
    bool _isTraining = false;               // 学習モードフラグ
    float[] _dropoutMaskShared;             // 共有層のDropoutマスク
    float[][] _dropoutMaskHead;             // ヘッド層のDropoutマスク

    // ---- ヘッド重み（脅威度で動的に変化） ----
    public float[] HeadWeights = { 0.4f, 0.4f, 0.2f }; // Strategy, Tactics, Economy

    // ---- 乱数 ----
    System.Random _rng;

    // ---- 統計 ----
    public int TotalTrainingSteps { get; private set; }
    public float LastLoss { get; private set; }
    public float[] LastHeadOutputs => _headOutputs;

    public MLBrain(int seed = -1)
    {
        _rng = seed >= 0 ? new System.Random(seed) : new System.Random();
        InitializeWeights();
        InitializeAdam();
    }

    // ================================================================
    //  Xavier初期化
    // ================================================================
    void InitializeWeights()
    {
        WShared = XavierInit(InputSize, SharedHiddenSize);
        BShared = new float[SharedHiddenSize];

        WHead = new float[NumHeads][,];
        BHead = new float[NumHeads][];
        WOut = new float[NumHeads][,];
        BOut = new float[NumHeads][];

        for (int h = 0; h < NumHeads; h++)
        {
            WHead[h] = XavierInit(SharedHiddenSize, HeadHiddenSize);
            BHead[h] = new float[HeadHiddenSize];
            WOut[h] = XavierInit(HeadHiddenSize, HeadOutputSize);
            BOut[h] = new float[HeadOutputSize];
        }
    }

    void InitializeAdam()
    {
        MWShared = new float[InputSize, SharedHiddenSize];
        VWShared = new float[InputSize, SharedHiddenSize];
        MBShared = new float[SharedHiddenSize];
        VBShared = new float[SharedHiddenSize];

        MWHead = new float[NumHeads][,];
        VWHead = new float[NumHeads][,];
        MWOut = new float[NumHeads][,];
        VWOut = new float[NumHeads][,];
        MBHead = new float[NumHeads][];
        VBHead = new float[NumHeads][];
        MBOut = new float[NumHeads][];
        VBOut = new float[NumHeads][];

        for (int h = 0; h < NumHeads; h++)
        {
            MWHead[h] = new float[SharedHiddenSize, HeadHiddenSize];
            VWHead[h] = new float[SharedHiddenSize, HeadHiddenSize];
            MWOut[h] = new float[HeadHiddenSize, HeadOutputSize];
            VWOut[h] = new float[HeadHiddenSize, HeadOutputSize];
            MBHead[h] = new float[HeadHiddenSize];
            VBHead[h] = new float[HeadHiddenSize];
            MBOut[h] = new float[HeadOutputSize];
            VBOut[h] = new float[HeadOutputSize];
        }
        AdamStep = 0;
    }

    float[,] XavierInit(int fanIn, int fanOut)
    {
        float scale = Mathf.Sqrt(2f / (fanIn + fanOut));
        var w = new float[fanIn, fanOut];
        for (int i = 0; i < fanIn; i++)
            for (int j = 0; j < fanOut; j++)
                w[i, j] = (float)NextGaussian() * scale;
        return w;
    }

    // ================================================================
    //  Forward — 順伝播（学習用、キャッシュ保持）
    //  戻り値: 3ヘッドの重み付き合計 (-1 ～ +1)
    // ================================================================
    public float Forward(float[] input)
    {
        _isTraining = true;
        if (input.Length != InputSize)
            throw new ArgumentException($"入力サイズ不一致: 期待{InputSize}, 受取{input.Length}");

        _lastInput = input;

        // 共有隠れ層 (ReLU + Dropout)
        _zShared = new float[SharedHiddenSize];
        _aShared = new float[SharedHiddenSize];
        _dropoutMaskShared = new float[SharedHiddenSize];
        float dropScale = 1f / (1f - DropoutRate); // Inverted Dropout
        for (int j = 0; j < SharedHiddenSize; j++)
        {
            float sum = BShared[j];
            for (int i = 0; i < InputSize; i++)
                sum += input[i] * WShared[i, j];
            _zShared[j] = sum;
            float a = ReLU(sum);
            // Inverted Dropout: 学習時のみニューロンをランダムに無効化
            bool keep = _rng.NextDouble() >= DropoutRate;
            _dropoutMaskShared[j] = keep ? dropScale : 0f;
            _aShared[j] = a * _dropoutMaskShared[j];
        }

        // 各ヘッド
        _zHead = new float[NumHeads][];
        _aHead = new float[NumHeads][];
        _zOut = new float[NumHeads][];
        _headOutputs = new float[NumHeads];
        _dropoutMaskHead = new float[NumHeads][];
        float headDropScale = 1f / (1f - HeadDropoutRate);

        for (int h = 0; h < NumHeads; h++)
        {
            // Head Hidden (ReLU + Dropout)
            _zHead[h] = new float[HeadHiddenSize];
            _aHead[h] = new float[HeadHiddenSize];
            _dropoutMaskHead[h] = new float[HeadHiddenSize];
            for (int j = 0; j < HeadHiddenSize; j++)
            {
                float sum = BHead[h][j];
                for (int i = 0; i < SharedHiddenSize; i++)
                    sum += _aShared[i] * WHead[h][i, j];
                _zHead[h][j] = sum;
                float a = ReLU(sum);
                bool keep = _rng.NextDouble() >= HeadDropoutRate;
                _dropoutMaskHead[h][j] = keep ? headDropScale : 0f;
                _aHead[h][j] = a * _dropoutMaskHead[h][j];
            }

            // Head Output (Tanh)
            _zOut[h] = new float[HeadOutputSize];
            float outSum = BOut[h][0];
            for (int i = 0; i < HeadHiddenSize; i++)
                outSum += _aHead[h][i] * WOut[h][i, 0];
            _zOut[h][0] = outSum;
            _headOutputs[h] = Tanh(outSum);
        }

        // 重み付き合計
        float combined = 0f;
        for (int h = 0; h < NumHeads; h++)
            combined += _headOutputs[h] * HeadWeights[h];
        return Mathf.Clamp(combined, -1f, 1f);
    }

    // ================================================================
    //  Predict — 推論専用（高速版、キャッシュ不要）
    // ================================================================
    public float Predict(float[] input)
    {
        if (input.Length != InputSize) return 0f;

        // 共有隠れ層
        var aShared = new float[SharedHiddenSize];
        for (int j = 0; j < SharedHiddenSize; j++)
        {
            float sum = BShared[j];
            for (int i = 0; i < InputSize; i++)
                sum += input[i] * WShared[i, j];
            aShared[j] = ReLU(sum);
        }

        float combined = 0f;
        for (int h = 0; h < NumHeads; h++)
        {
            // Head Hidden
            var aHead = new float[HeadHiddenSize];
            for (int j = 0; j < HeadHiddenSize; j++)
            {
                float sum = BHead[h][j];
                for (int i = 0; i < SharedHiddenSize; i++)
                    sum += aShared[i] * WHead[h][i, j];
                aHead[j] = ReLU(sum);
            }

            // Head Output
            float outSum = BOut[h][0];
            for (int i = 0; i < HeadHiddenSize; i++)
                outSum += aHead[i] * WOut[h][i, 0];
            combined += Tanh(outSum) * HeadWeights[h];
        }

        return Mathf.Clamp(combined, -1f, 1f);
    }

    // ================================================================
    //  PredictPerHead — ヘッド別の出力を取得
    // ================================================================
    public float[] PredictPerHead(float[] input)
    {
        if (input.Length != InputSize) return new float[NumHeads];

        var aShared = new float[SharedHiddenSize];
        for (int j = 0; j < SharedHiddenSize; j++)
        {
            float sum = BShared[j];
            for (int i = 0; i < InputSize; i++)
                sum += input[i] * WShared[i, j];
            aShared[j] = ReLU(sum);
        }

        var outputs = new float[NumHeads];
        for (int h = 0; h < NumHeads; h++)
        {
            var aHead = new float[HeadHiddenSize];
            for (int j = 0; j < HeadHiddenSize; j++)
            {
                float sum = BHead[h][j];
                for (int i = 0; i < SharedHiddenSize; i++)
                    sum += aShared[i] * WHead[h][i, j];
                aHead[j] = ReLU(sum);
            }

            float outSum = BOut[h][0];
            for (int i = 0; i < HeadHiddenSize; i++)
                outSum += aHead[i] * WOut[h][i, 0];
            outputs[h] = Tanh(outSum);
        }
        return outputs;
    }

    // ================================================================
    //  Backward — 逆伝播 + Adam更新
    //  targets: 各ヘッドの教師信号 float[NumHeads]
    // ================================================================
    public float Backward(float[] targets)
    {
        if (targets.Length != NumHeads)
            throw new ArgumentException($"教師信号サイズ不一致: 期待{NumHeads}, 受取{targets.Length}");

        AdamStep++;
        TotalTrainingSteps++;

        float totalLoss = 0f;

        // 共有層への勾配を蓄積
        var dAShared = new float[SharedHiddenSize];

        // 各ヘッドの勾配を一旦蓄積（グローバル勾配クリッピング用）
        var allDWHead = new float[NumHeads][,];
        var allDBHead = new float[NumHeads][];
        var allDWOut = new float[NumHeads][,];
        var allDBOut = new float[NumHeads][];

        for (int h = 0; h < NumHeads; h++)
        {
            float error = _headOutputs[h] - targets[h];
            totalLoss += error * error;

            // Output勾配
            float dOutZ = error * TanhDeriv(_zOut[h][0]);

            // WOut勾配
            var dWOut = new float[HeadHiddenSize, HeadOutputSize];
            var dBOut = new float[HeadOutputSize];
            dBOut[0] = dOutZ;
            var dAHead = new float[HeadHiddenSize];
            for (int i = 0; i < HeadHiddenSize; i++)
            {
                dWOut[i, 0] = _aHead[h][i] * dOutZ + L2Lambda * WOut[h][i, 0];
                dAHead[i] = WOut[h][i, 0] * dOutZ;
            }

            // Head Hidden勾配 (Dropoutマスク反映)
            var dZHead = new float[HeadHiddenSize];
            for (int j = 0; j < HeadHiddenSize; j++)
            {
                float mask = (_dropoutMaskHead != null && _dropoutMaskHead[h] != null)
                    ? _dropoutMaskHead[h][j] : 1f;
                dZHead[j] = dAHead[j] * ReLUDeriv(_zHead[h][j]) * (mask > 0f ? 1f : 0f);
            }

            var dWHead = new float[SharedHiddenSize, HeadHiddenSize];
            var dBHead = new float[HeadHiddenSize];
            for (int j = 0; j < HeadHiddenSize; j++)
            {
                dBHead[j] = dZHead[j];
                for (int i = 0; i < SharedHiddenSize; i++)
                {
                    dWHead[i, j] = _aShared[i] * dZHead[j] + L2Lambda * WHead[h][i, j];
                    dAShared[i] += WHead[h][i, j] * dZHead[j];
                }
            }

            allDWHead[h] = dWHead;
            allDBHead[h] = dBHead;
            allDWOut[h] = dWOut;
            allDBOut[h] = dBOut;
        }

        // 共有層勾配 (Dropoutマスク反映)
        var dZShared = new float[SharedHiddenSize];
        for (int j = 0; j < SharedHiddenSize; j++)
        {
            float mask = (_dropoutMaskShared != null) ? _dropoutMaskShared[j] : 1f;
            dZShared[j] = dAShared[j] * ReLUDeriv(_zShared[j]) * (mask > 0f ? 1f : 0f);
        }

        var dWShared = new float[InputSize, SharedHiddenSize];
        var dBSharedArr = new float[SharedHiddenSize];
        for (int j = 0; j < SharedHiddenSize; j++)
        {
            dBSharedArr[j] = dZShared[j];
            for (int i = 0; i < InputSize; i++)
                dWShared[i, j] = _lastInput[i] * dZShared[j] + L2Lambda * WShared[i, j];
        }

        // ---- グローバル勾配クリッピング（全層の勾配ノルムを計算） ----
        float normSq = MatNormSq(dWShared) + VecNormSq(dBSharedArr);
        for (int h = 0; h < NumHeads; h++)
        {
            normSq += MatNormSq(allDWHead[h]) + VecNormSq(allDBHead[h]);
            normSq += MatNormSq(allDWOut[h]) + VecNormSq(allDBOut[h]);
        }
        float norm = Mathf.Sqrt(normSq);
        if (norm > GradClipNorm)
        {
            float scale = GradClipNorm / norm;
            ScaleMat(dWShared, scale);
            ScaleVec(dBSharedArr, scale);
            for (int h = 0; h < NumHeads; h++)
            {
                ScaleMat(allDWHead[h], scale);
                ScaleVec(allDBHead[h], scale);
                ScaleMat(allDWOut[h], scale);
                ScaleVec(allDBOut[h], scale);
            }
        }

        // Adam更新 (全層)
        AdamUpdate(WShared, dWShared, MWShared, VWShared);
        AdamUpdateBias(BShared, dBSharedArr, MBShared, VBShared);
        for (int h = 0; h < NumHeads; h++)
        {
            AdamUpdate(WHead[h], allDWHead[h], MWHead[h], VWHead[h]);
            AdamUpdate(WOut[h], allDWOut[h], MWOut[h], VWOut[h]);
            AdamUpdateBias(BHead[h], allDBHead[h], MBHead[h], VBHead[h]);
            AdamUpdateBias(BOut[h], allDBOut[h], MBOut[h], VBOut[h]);
        }

        LastLoss = totalLoss / NumHeads;
        _isTraining = false;
        return LastLoss;
    }

    // ================================================================
    //  ヘッド重みの動的調整（脅威度に応じて）
    // ================================================================
    public void SetHeadWeightsForThreat(int threatLevel)
    {
        if (threatLevel <= 30)
        {
            // 序盤: 戦術重視（目先の戦闘）
            HeadWeights[0] = 0.25f; // Strategy
            HeadWeights[1] = 0.55f; // Tactics
            HeadWeights[2] = 0.20f; // Economy
        }
        else if (threatLevel <= 50)
        {
            // 中盤: バランス
            HeadWeights[0] = 0.35f;
            HeadWeights[1] = 0.40f;
            HeadWeights[2] = 0.25f;
        }
        else if (threatLevel <= 70)
        {
            // 上級: 戦略重視（長期的な判断）
            HeadWeights[0] = 0.45f;
            HeadWeights[1] = 0.30f;
            HeadWeights[2] = 0.25f;
        }
        else
        {
            // 最上級: 全て高精度
            HeadWeights[0] = 0.40f;
            HeadWeights[1] = 0.35f;
            HeadWeights[2] = 0.25f;
        }
    }

    // ================================================================
    //  Adam Optimizer
    // ================================================================
    void AdamUpdate(float[,] w, float[,] dw, float[,] m, float[,] v)
    {
        float bc1 = 1f - Mathf.Pow(AdamBeta1, AdamStep);
        float bc2 = 1f - Mathf.Pow(AdamBeta2, AdamStep);
        int rows = w.GetLength(0), cols = w.GetLength(1);
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                m[i, j] = AdamBeta1 * m[i, j] + (1f - AdamBeta1) * dw[i, j];
                v[i, j] = AdamBeta2 * v[i, j] + (1f - AdamBeta2) * dw[i, j] * dw[i, j];
                float mHat = m[i, j] / bc1;
                float vHat = v[i, j] / bc2;
                w[i, j] -= LearningRate * mHat / (Mathf.Sqrt(vHat) + AdamEpsilon);
            }
    }

    void AdamUpdateBias(float[] b, float[] db, float[] m, float[] v)
    {
        float bc1 = 1f - Mathf.Pow(AdamBeta1, AdamStep);
        float bc2 = 1f - Mathf.Pow(AdamBeta2, AdamStep);
        for (int i = 0; i < b.Length; i++)
        {
            m[i] = AdamBeta1 * m[i] + (1f - AdamBeta1) * db[i];
            v[i] = AdamBeta2 * v[i] + (1f - AdamBeta2) * db[i] * db[i];
            float mHat = m[i] / bc1;
            float vHat = v[i] / bc2;
            b[i] -= LearningRate * mHat / (Mathf.Sqrt(vHat) + AdamEpsilon);
        }
    }

    // ================================================================
    //  活性化関数
    // ================================================================
    static float ReLU(float x) => x > 0 ? x : 0;
    static float ReLUDeriv(float x) => x > 0 ? 1f : 0f;
    static float Tanh(float x) => (float)Math.Tanh(x);
    static float TanhDeriv(float x) { float t = Tanh(x); return 1f - t * t; }

    // ================================================================
    //  数学ユーティリティ
    // ================================================================
    double NextGaussian()
    {
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = 1.0 - _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    static float MatNormSq(float[,] m)
    {
        float sum = 0f;
        int r = m.GetLength(0), c = m.GetLength(1);
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
                sum += m[i, j] * m[i, j];
        return sum;
    }

    static float VecNormSq(float[] v)
    {
        float sum = 0f;
        for (int i = 0; i < v.Length; i++) sum += v[i] * v[i];
        return sum;
    }

    static void ScaleMat(float[,] m, float s)
    {
        int r = m.GetLength(0), c = m.GetLength(1);
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++) m[i, j] *= s;
    }

    static void ScaleVec(float[] v, float s)
    {
        for (int i = 0; i < v.Length; i++) v[i] *= s;
    }

    public int ParameterCount =>
        InputSize * SharedHiddenSize + SharedHiddenSize +
        NumHeads * (SharedHiddenSize * HeadHiddenSize + HeadHiddenSize +
                    HeadHiddenSize * HeadOutputSize + HeadOutputSize);
}
