using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MLReplayBuffer — 経験再生バッファ
//  試合中の (状態, 行動, 報酬) を記録し、試合終了時に学習に使う。
//  リングバッファで古い経験を自動破棄。
//  優先度付き経験再生 (PER) で重要な経験を重点的に学習。
// =====================================================================
public class MLReplayBuffer
{
    // ---- 経験データ ----
    public struct Experience
    {
        public float[] Features;       // 状態+行動の特徴ベクトル
        public float Reward;           // 即時報酬
        public float TDTarget;         // TD目標値（試合終了時に計算）
        public float Priority;         // 優先度（|TD誤差|）
        public int TurnNumber;         // どのターンの経験か
    }

    // ---- バッファ ----
    Experience[] _buffer;
    int _head = 0;
    int _count = 0;
    readonly int _capacity;

    // ---- 1ターン分の一時記録 ----
    List<TurnRecord> _currentMatchRecords = new List<TurnRecord>();

    public struct TurnRecord
    {
        public float[] Features;
        public float ImmediateReward;
        public int Turn;
    }

    // ---- 設定 ----
    public float DiscountFactor = 0.97f;     // 割引率 γ
    public float PriorityAlpha = 0.6f;       // 優先度の指数
    public float PriorityEpsilon = 0.01f;    // 最低優先度

    // ---- 統計 ----
    public int TotalExperiences { get; private set; }
    public int CurrentSize => _count;
    public int Capacity => _capacity;

    public MLReplayBuffer(int capacity = 5000)
    {
        _capacity = capacity;
        _buffer = new Experience[capacity];
    }

    // ================================================================
    //  試合中の記録
    // ================================================================

    /// <summary>行動実行時に呼ぶ。特徴と即時報酬を記録。</summary>
    public void RecordStep(float[] features, float immediateReward, int turn)
    {
        _currentMatchRecords.Add(new TurnRecord
        {
            Features = (float[])features.Clone(),
            ImmediateReward = immediateReward,
            Turn = turn
        });
    }

    /// <summary>試合中の記録数</summary>
    public int CurrentMatchRecordCount => _currentMatchRecords.Count;

    // ================================================================
    //  試合終了時の処理
    //  終端報酬を逆伝播して各ステップのTD目標値を計算
    // ================================================================

    /// <summary>
    /// 試合結果を反映して全記録をバッファに格納。
    /// terminalReward: 勝利=+1.0, 敗北=-1.0, 引分=0.0
    /// </summary>
    public void FinalizeMatch(float terminalReward)
    {
        if (_currentMatchRecords.Count == 0) return;

        int n = _currentMatchRecords.Count;

        // 逆順に割引報酬を計算 (Monte Carlo return)
        float[] returns = new float[n];
        float G = terminalReward;

        for (int i = n - 1; i >= 0; i--)
        {
            G = _currentMatchRecords[i].ImmediateReward + DiscountFactor * G;
            returns[i] = Mathf.Clamp(G, -1f, 1f);
        }

        // バッファに格納
        for (int i = 0; i < n; i++)
        {
            var record = _currentMatchRecords[i];
            var exp = new Experience
            {
                Features = record.Features,
                Reward = record.ImmediateReward,
                TDTarget = returns[i],
                Priority = Mathf.Abs(returns[i]) + PriorityEpsilon,
                TurnNumber = record.Turn
            };

            _buffer[_head] = exp;
            _head = (_head + 1) % _capacity;
            _count = Mathf.Min(_count + 1, _capacity);
            TotalExperiences++;
        }

        Debug.Log($"[MLReplayBuffer] 試合記録追加: {n}ステップ  " +
                  $"終端報酬={terminalReward:F2}  バッファ={_count}/{_capacity}");

        _currentMatchRecords.Clear();
    }

    /// <summary>試合中の記録を破棄（異常終了時）</summary>
    public void DiscardCurrentMatch()
    {
        _currentMatchRecords.Clear();
    }

    // ================================================================
    //  サンプリング（優先度付き経験再生）
    // ================================================================

    /// <summary>
    /// 優先度に基づいてbatchSize個の経験をサンプリングする。
    /// 高い|TD誤差|を持つ経験ほど選ばれやすい。
    /// </summary>
    public List<Experience> SampleBatch(int batchSize, System.Random rng)
    {
        if (_count == 0) return new List<Experience>();

        batchSize = Mathf.Min(batchSize, _count);

        // 優先度の合計を計算
        float totalPriority = 0f;
        for (int i = 0; i < _count; i++)
        {
            totalPriority += Mathf.Pow(_buffer[i].Priority, PriorityAlpha);
        }

        var batch = new List<Experience>(batchSize);
        var selected = new HashSet<int>();

        for (int b = 0; b < batchSize; b++)
        {
            float target = (float)(rng.NextDouble() * totalPriority);
            float cumulative = 0f;
            int idx = 0;

            for (int i = 0; i < _count; i++)
            {
                cumulative += Mathf.Pow(_buffer[i].Priority, PriorityAlpha);
                if (cumulative >= target)
                {
                    idx = i;
                    break;
                }
            }

            // 重複回避（できなければそのまま追加）
            if (selected.Contains(idx) && _count > batchSize)
            {
                idx = (idx + 1) % _count;
                int tries = 0;
                while (selected.Contains(idx) && tries < _count)
                {
                    idx = (idx + 1) % _count;
                    tries++;
                }
            }

            selected.Add(idx);
            batch.Add(_buffer[idx]);
        }

        return batch;
    }

    /// <summary>経験の優先度を更新（学習後にTD誤差に基づいて更新）</summary>
    public void UpdatePriority(int bufferIndex, float tdError)
    {
        if (bufferIndex >= 0 && bufferIndex < _count)
        {
            _buffer[bufferIndex].Priority = Mathf.Abs(tdError) + PriorityEpsilon;
        }
    }

    // ================================================================
    //  シリアライズ（最新N件のみ保存）
    // ================================================================

    public List<Experience> GetRecentExperiences(int maxCount)
    {
        var result = new List<Experience>();
        int start = _count < _capacity ? 0 : _head;
        int count = Mathf.Min(maxCount, _count);
        int offset = _count - count;

        for (int i = 0; i < count; i++)
        {
            int idx = (start + offset + i) % _capacity;
            result.Add(_buffer[idx]);
        }

        return result;
    }

    public void LoadExperiences(List<Experience> experiences)
    {
        _head = 0;
        _count = 0;
        foreach (var exp in experiences)
        {
            _buffer[_head] = exp;
            _head = (_head + 1) % _capacity;
            _count = Mathf.Min(_count + 1, _capacity);
        }
    }
}
