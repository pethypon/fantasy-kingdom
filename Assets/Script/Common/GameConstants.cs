using UnityEngine;

/// <summary>
/// ゲーム全体で使用するマジックナンバーを一元管理する定数クラス。
/// Y座標オフセット、シールド関連、ダメージ計算係数など。
/// </summary>
public static class GameConstants
{
    // =====================================================================
    //  Y座標オフセット
    // =====================================================================
    /// <summary>MovePoint の Y オフセット（タイル上面からの下げ幅）</summary>
    public const float MovePointYOffset = 0.47f;
    /// <summary>AttackPoint の Y オフセット</summary>
    public const float AttackPointYOffset = 0.17f;

    // =====================================================================
    //  ダメージ計算式の係数
    // =====================================================================
    /// <summary>基本ダメージの固定加算値</summary>
    public const float DamageBase = 1f;
    /// <summary>ATK÷この値が基本ダメージに加算される</summary>
    public const float DamageATKDivisor = 6f;
    /// <summary>ATK÷この値が攻撃側の実効攻撃力</summary>
    public const float DamageATKHalf = 2f;
    /// <summary>DEF÷この値が防御側の実効防御力</summary>
    public const float DamageDEFQuarter = 4f;

    // =====================================================================
    //  クリスタルシールド
    // =====================================================================
    /// <summary>シールド発動条件: HP割合がこの値未満</summary>
    public const float CrystalShieldThreshold = 0.5f;
    /// <summary>シールド持続ターン数</summary>
    public const int CrystalShieldDuration = 5;

    // =====================================================================
    //  DoT（継続ダメージ）
    // =====================================================================
    /// <summary>毒の毎ターンダメージ</summary>
    public const int PoisonDamagePerTurn = 8;
    /// <summary>出血の毎ターンダメージ</summary>
    public const int BleedDamagePerTurn = 6;

    // =====================================================================
    //  回復修飾
    // =====================================================================
    /// <summary>毒状態での回復減少率</summary>
    public const float PoisonHealReduction = 0.75f;
    /// <summary>呪傷状態での回復減少率</summary>
    public const float CurseHealReduction = 0.50f;

    // =====================================================================
    //  AP関連
    // =====================================================================
    /// <summary>移動の基本APコスト</summary>
    public const int BaseMoveAPCost = 3;
    /// <summary>攻撃の基本APコスト</summary>
    public const int BaseAttackAPCost = 2;

    // =====================================================================
    //  Raycast
    // =====================================================================
    /// <summary>ユニット選択等の最大レイ距離</summary>
    public const float DefaultRayDistance = 100f;
    /// <summary>MovePoint検出用の最大レイ距離</summary>
    public const float MovePointRayDistance = 50f;

    // =====================================================================
    //  カメラ
    // =====================================================================
    /// <summary>カメラFOV最小値</summary>
    public const float CameraFOVMin = 30f;
    /// <summary>カメラFOV最大値</summary>
    public const float CameraFOVMax = 90f;

    // =====================================================================
    //  グリッド計算ヘルパー
    // =====================================================================
    /// <summary>ワールド座標をグリッドセル座標(Y=0)に変換する</summary>
    public static Vector3 ToCell(Vector3 v)
    {
        return new Vector3(Mathf.RoundToInt(v.x), 0f, Mathf.RoundToInt(v.z));
    }

    /// <summary>Vector3Int同士の距離（Floatベース）</summary>
    public static float Distance(Vector3Int a, Vector3Int b)
    {
        return Vector3.Distance((Vector3)a, (Vector3)b);
    }
}
