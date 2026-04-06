using UnityEngine;

// =====================================================================
//  AIDeterministicRandom — シード管理された乱数ラッパー
//  同条件で同傾向を再現可能にする（デバッグ容易性のため）
//  仕様: 同一シードで同一初期盤面なら同一傾向の行動列を再現できる
// =====================================================================
public class AIDeterministicRandom
{
    System.Random _rng;
    int _seed;

    public int Seed => _seed;

    public AIDeterministicRandom(int seed)
    {
        _seed = seed;
        _rng = new System.Random(seed);
        Debug.Log($"[AIDeterministicRandom] シード={seed}");
    }

    /// <summary>試合開始時にシードを設定（再現性のため外部から指定可能）</summary>
    public void Reset(int seed)
    {
        _seed = seed;
        _rng = new System.Random(seed);
    }

    /// <summary>ターン開始時にターン番号でサブシードを生成（ターン単位の再現性）</summary>
    public void SetTurnSeed(int turn)
    {
        _rng = new System.Random(_seed ^ (turn * 31));
    }

    /// <summary>0以上の整数乱数</summary>
    public int Next()
    {
        return _rng.Next();
    }

    /// <summary>0.0〜1.0のfloat乱数</summary>
    public float NextFloat()
    {
        return (float)_rng.NextDouble();
    }

    /// <summary>min以上max未満の整数乱数</summary>
    public int Range(int min, int max)
    {
        if (min >= max) return min;
        return _rng.Next(min, max);
    }

    /// <summary>0.0〜max のfloat乱数</summary>
    public float Range(float min, float max)
    {
        if (min >= max) return min;
        return min + (float)_rng.NextDouble() * (max - min);
    }
}
