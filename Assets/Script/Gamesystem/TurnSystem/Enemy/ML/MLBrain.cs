using System;
using UnityEngine;

// =====================================================================
//  MLBrain — 純C#ニューラルネットワーク
//  盤面特徴ベクトルから行動価値を予測する。
//  構造: Input(64) → Hidden1(128,ReLU) → Hidden2(64,ReLU) → Output(1,Tanh)
//
//  脅威度20以降で有効化。戦うたびに学習し、より強くなる。
//  Xavier初期化 + Adam最適化 + 経験再生で安定学習。
// =====================================================================
public class MLBrain
{
    // ---- ネットワーク構成 ----
    public const int InputSize  = 64;   // MLFeatureExtractor が出力する特徴数
    const int Hidden1Size = 128;
    const int Hidden2Size = 64;
    const int OutputSize  = 1;

    // ---- 重み・バイアス ----
    // Layer 1: Input → Hidden1
    public float[,] W1;   // [InputSize, Hidden1Size]
    public float[]  B1;   // [Hidden1Size]
    // Layer 2: Hidden1 → Hidden2
    public float[,] W2;   // [Hidden1Size, Hidden2Size]
    public float[]  B2;   // [Hidden2Size]
    // Layer 3: Hidden2 → Output
    public float[,] W3;   // [Hidden2Size, OutputSize]
    public float[]  B3;   // [OutputSize]

    // ---- Adam最適化器の状態 ----
    // (M = 1st moment, V = 2nd moment for each parameter)
    public float[,] MW1, VW1, MW2, VW2, MW3, VW3;
    public float[]  MB1, VB1, MB2, VB2, MB3, VB3;
    public int AdamStep;

    // ---- 中間値キャッシュ（Forward時に保存、Backward時に使用） ----
    float[] _lastInput;
    float[] _z1, _a1;   // Hidden1 pre/post activation
    float[] _z2, _a2;   // Hidden2 pre/post activation
    float[] _z3;         // Output pre activation
    float   _lastOutput;

    // ---- ハイパーパラメータ ----
    public float LearningRate = 0.001f;
    public float AdamBeta1 = 0.9f;
    public float AdamBeta2 = 0.999f;
    public float AdamEpsilon = 1e-8f;
    public float L2Lambda = 1e-4f;       // L2正則化
    public float GradClipNorm = 5.0f;    // 勾配クリッピング

    // ---- 乱数 ----
    System.Random _rng;

    // ---- 統計 ----
    public int TotalTrainingSteps { get; private set; }
    public float LastLoss { get; private set; }

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
        W1 = XavierInit(InputSize, Hidden1Size);
        B1 = new float[Hidden1Size];
        W2 = XavierInit(Hidden1Size, Hidden2Size);
        B2 = new float[Hidden2Size];
        W3 = XavierInit(Hidden2Size, OutputSize);
        B3 = new float[OutputSize];
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

    void InitializeAdam()
    {
        MW1 = new float[InputSize, Hidden1Size];
        VW1 = new float[InputSize, Hidden1Size];
        MW2 = new float[Hidden1Size, Hidden2Size];
        VW2 = new float[Hidden1Size, Hidden2Size];
        MW3 = new float[Hidden2Size, OutputSize];
        VW3 = new float[Hidden2Size, OutputSize];
        MB1 = new float[Hidden1Size];
        VB1 = new float[Hidden1Size];
        MB2 = new float[Hidden2Size];
        VB2 = new float[Hidden2Size];
        MB3 = new float[OutputSize];
        VB3 = new float[OutputSize];
        AdamStep = 0;
    }

    // ================================================================
    //  Forward — 順伝播
    //  入力: 特徴ベクトル float[InputSize]
    //  出力: 行動価値 float (-1 ～ +1)
    // ================================================================
    public float Forward(float[] input)
    {
        if (input.Length != InputSize)
            throw new ArgumentException($"入力サイズ不一致: 期待{InputSize}, 受取{input.Length}");

        _lastInput = input;

        // Layer 1: Input → Hidden1 (ReLU)
        _z1 = new float[Hidden1Size];
        _a1 = new float[Hidden1Size];
        for (int j = 0; j < Hidden1Size; j++)
        {
            float sum = B1[j];
            for (int i = 0; i < InputSize; i++)
                sum += input[i] * W1[i, j];
            _z1[j] = sum;
            _a1[j] = ReLU(sum);
        }

        // Layer 2: Hidden1 → Hidden2 (ReLU)
        _z2 = new float[Hidden2Size];
        _a2 = new float[Hidden2Size];
        for (int j = 0; j < Hidden2Size; j++)
        {
            float sum = B2[j];
            for (int i = 0; i < Hidden1Size; i++)
                sum += _a1[i] * W2[i, j];
            _z2[j] = sum;
            _a2[j] = ReLU(sum);
        }

        // Layer 3: Hidden2 → Output (Tanh)
        _z3 = new float[OutputSize];
        float outSum = B3[0];
        for (int i = 0; i < Hidden2Size; i++)
            outSum += _a2[i] * W3[i, 0];
        _z3[0] = outSum;
        _lastOutput = Tanh(outSum);

        return _lastOutput;
    }

    // ================================================================
    //  Backward — 逆伝播 + Adam更新
    //  target: 教師信号 (-1 ～ +1)
    //  損失関数: MSE = (output - target)^2
    // ================================================================
    public float Backward(float target)
    {
        AdamStep++;
        TotalTrainingSteps++;

        float error = _lastOutput - target;
        LastLoss = error * error;

        // Output層の勾配 (Tanh微分)
        float dOut = error * TanhDeriv(_z3[0]);

        // Layer 3 勾配
        var dW3 = new float[Hidden2Size, OutputSize];
        var dB3 = new float[OutputSize];
        dB3[0] = dOut;
        var dA2 = new float[Hidden2Size];
        for (int i = 0; i < Hidden2Size; i++)
        {
            dW3[i, 0] = _a2[i] * dOut + L2Lambda * W3[i, 0];
            dA2[i] = W3[i, 0] * dOut;
        }

        // Layer 2 勾配 (ReLU微分)
        var dZ2 = new float[Hidden2Size];
        for (int j = 0; j < Hidden2Size; j++)
            dZ2[j] = dA2[j] * ReLUDeriv(_z2[j]);

        var dW2 = new float[Hidden1Size, Hidden2Size];
        var dB2 = new float[Hidden2Size];
        var dA1 = new float[Hidden1Size];
        for (int j = 0; j < Hidden2Size; j++)
        {
            dB2[j] = dZ2[j];
            for (int i = 0; i < Hidden1Size; i++)
            {
                dW2[i, j] = _a1[i] * dZ2[j] + L2Lambda * W2[i, j];
                dA1[i] += W2[i, j] * dZ2[j];
            }
        }

        // Layer 1 勾配 (ReLU微分)
        var dZ1 = new float[Hidden1Size];
        for (int j = 0; j < Hidden1Size; j++)
            dZ1[j] = dA1[j] * ReLUDeriv(_z1[j]);

        var dW1 = new float[InputSize, Hidden1Size];
        var dB1 = new float[Hidden1Size];
        for (int j = 0; j < Hidden1Size; j++)
        {
            dB1[j] = dZ1[j];
            for (int i = 0; i < InputSize; i++)
                dW1[i, j] = _lastInput[i] * dZ1[j] + L2Lambda * W1[i, j];
        }

        // 勾配クリッピング
        ClipGradients(dW1, dW2, dW3, dB1, dB2, dB3);

        // Adam更新
        AdamUpdate(W1, dW1, MW1, VW1);
        AdamUpdate(W2, dW2, MW2, VW2);
        AdamUpdate(W3, dW3, MW3, VW3);
        AdamUpdateBias(B1, dB1, MB1, VB1);
        AdamUpdateBias(B2, dB2, MB2, VB2);
        AdamUpdateBias(B3, dB3, MB3, VB3);

        return LastLoss;
    }

    // ================================================================
    //  Adam Optimizer
    // ================================================================
    void AdamUpdate(float[,] w, float[,] dw, float[,] m, float[,] v)
    {
        float bc1 = 1f - Mathf.Pow(AdamBeta1, AdamStep);
        float bc2 = 1f - Mathf.Pow(AdamBeta2, AdamStep);
        int rows = w.GetLength(0);
        int cols = w.GetLength(1);

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                m[i, j] = AdamBeta1 * m[i, j] + (1f - AdamBeta1) * dw[i, j];
                v[i, j] = AdamBeta2 * v[i, j] + (1f - AdamBeta2) * dw[i, j] * dw[i, j];
                float mHat = m[i, j] / bc1;
                float vHat = v[i, j] / bc2;
                w[i, j] -= LearningRate * mHat / (Mathf.Sqrt(vHat) + AdamEpsilon);
            }
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
    //  勾配クリッピング (Gradient Norm Clipping)
    // ================================================================
    void ClipGradients(float[,] dW1, float[,] dW2, float[,] dW3,
        float[] dB1, float[] dB2, float[] dB3)
    {
        float norm = 0f;
        norm += MatNormSq(dW1);
        norm += MatNormSq(dW2);
        norm += MatNormSq(dW3);
        norm += VecNormSq(dB1);
        norm += VecNormSq(dB2);
        norm += VecNormSq(dB3);
        norm = Mathf.Sqrt(norm);

        if (norm > GradClipNorm)
        {
            float scale = GradClipNorm / norm;
            ScaleMat(dW1, scale);
            ScaleMat(dW2, scale);
            ScaleMat(dW3, scale);
            ScaleVec(dB1, scale);
            ScaleVec(dB2, scale);
            ScaleVec(dB3, scale);
        }
    }

    // ================================================================
    //  推論専用（キャッシュ不要・高速版）
    // ================================================================
    public float Predict(float[] input)
    {
        // Layer 1
        var a1 = new float[Hidden1Size];
        for (int j = 0; j < Hidden1Size; j++)
        {
            float sum = B1[j];
            for (int i = 0; i < InputSize; i++)
                sum += input[i] * W1[i, j];
            a1[j] = ReLU(sum);
        }

        // Layer 2
        var a2 = new float[Hidden2Size];
        for (int j = 0; j < Hidden2Size; j++)
        {
            float sum = B2[j];
            for (int i = 0; i < Hidden1Size; i++)
                sum += a1[i] * W2[i, j];
            a2[j] = ReLU(sum);
        }

        // Layer 3
        float outSum = B3[0];
        for (int i = 0; i < Hidden2Size; i++)
            outSum += a2[i] * W3[i, 0];

        return Tanh(outSum);
    }

    // ================================================================
    //  活性化関数
    // ================================================================
    static float ReLU(float x) => x > 0 ? x : 0;
    static float ReLUDeriv(float x) => x > 0 ? 1f : 0f;
    static float Tanh(float x) => (float)Math.Tanh(x);
    static float TanhDeriv(float x)
    {
        float t = Tanh(x);
        return 1f - t * t;
    }

    // ================================================================
    //  数学ユーティリティ
    // ================================================================
    double NextGaussian()
    {
        // Box-Muller変換
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
        for (int i = 0; i < v.Length; i++)
            sum += v[i] * v[i];
        return sum;
    }

    static void ScaleMat(float[,] m, float s)
    {
        int r = m.GetLength(0), c = m.GetLength(1);
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
                m[i, j] *= s;
    }

    static void ScaleVec(float[] v, float s)
    {
        for (int i = 0; i < v.Length; i++)
            v[i] *= s;
    }

    // ================================================================
    //  パラメータ数
    // ================================================================
    public int ParameterCount =>
        InputSize * Hidden1Size + Hidden1Size +
        Hidden1Size * Hidden2Size + Hidden2Size +
        Hidden2Size * OutputSize + OutputSize;
}
