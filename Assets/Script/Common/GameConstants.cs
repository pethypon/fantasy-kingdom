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
    //  ステータス効果修飾値（StatusEffectSystem / SimUnit 共用）
    // =====================================================================
    /// <summary>弱体(Weaken): ATK低下率</summary>
    public const float WeakenATKReduction = 0.15f;
    /// <summary>冷気(Chill): ATK低下率</summary>
    public const float ChillATKReduction = 0.10f;
    /// <summary>攻勢(Offensive): ATK増加率</summary>
    public const float OffensiveATKBonus = 0.15f;
    /// <summary>破甲(ArmorBreak): DEF低下率</summary>
    public const float ArmorBreakDEFReduction = 0.15f;
    /// <summary>守勢(Defensive): DEF増加率</summary>
    public const float DefensiveDEFBonus = 0.20f;
    /// <summary>マーク(Mark): 被ダメ増加率</summary>
    public const float MarkIncomingDamageIncrease = 0.10f;
    /// <summary>凍結(Freeze): 被ダメ増加率</summary>
    public const float FreezeIncomingDamageIncrease = 0.10f;
    /// <summary>障壁(Barrier): 被ダメ軽減率</summary>
    public const float BarrierDamageReduction = 0.30f;
    /// <summary>封技(Seal): スキル倍率低下値</summary>
    public const float SealSkillReduction = 0.20f;
    /// <summary>鈍足(Slow)/冷気(Chill): 移動AP追加コスト</summary>
    public const int DebuffMoveAPBonus = 2;

    // =====================================================================
    //  AP関連
    // =====================================================================
    /// <summary>移動の基本APコスト</summary>
    public const int BaseMoveAPCost = 3;
    /// <summary>攻撃の基本APコスト</summary>
    public const int BaseAttackAPCost = 2;

    // =====================================================================
    //  パッシブスキル
    // =====================================================================
    /// <summary>Knight: 視界内の攻撃を受けた時のダメージ倍率（20%軽減）</summary>
    public const float KnightVisionDamageReduction = 0.8f;
    /// <summary>Knight: 視界外から攻撃された時のダメージ倍率（10%増加）</summary>
    public const float KnightOutOfVisionDamageIncrease = 1.1f;
    /// <summary>Assassin: 視界外から攻撃した時のダメージ倍率</summary>
    public const float AssassinShadowstrikeDamage = 1.25f;
    /// <summary>Archer: 距離ボーナス（1マスあたり）</summary>
    public const float ArcherDistanceBonusPerTile = 0.25f;
    /// <summary>Archer: 距離ボーナス最大値（3マス=0.75倍）</summary>
    public const float ArcherDistanceBonusMax = 0.75f;
    /// <summary>Archer: 飛行ユニットへのダメージ倍率</summary>
    public const float ArcherFlyingBonus = 1.25f;
    /// <summary>Magician: 建物へのダメージ倍率</summary>
    public const float MagicianBuildingBonus = 1.15f;
    /// <summary>Guardian: 建物へのダメージ倍率</summary>
    public const float GuardianBuildingBonus = 2.0f;
    /// <summary>Crossbow: スタン付与確率</summary>
    public const float CrossbowStunChance = 0.10f;
    /// <summary>MagicSniper: 自傷ダメージ（最大HPの割合）</summary>
    public const float MagicSniperSelfDamageRatio = 0.20f;
    /// <summary>Priest: 隣接味方の回復量（最大HPの割合）</summary>
    public const float PriestHealRatio = 0.05f;
    /// <summary>パッシブ倍率の上限</summary>
    public const float PassiveMultiplierMax = 2.0f;

    // =====================================================================
    //  スキル特殊効果の追加ダメージ倍率
    // =====================================================================
    /// <summary>シャドウラッシュ: 視界外攻撃時の追加ダメージ倍率</summary>
    public const float ShadowRushBonusMultiplier = 0.20f;
    /// <summary>ブラッドサクリファイス: 自傷ダメージ割合</summary>
    public const float BloodSacrificeRatio = 0.10f;
    /// <summary>デスサイト: HP50%以下の敵への追加ダメージ倍率</summary>
    public const float DeathSightBonusMultiplier = 0.30f;
    /// <summary>シージブレイカー: 建物への追加ダメージ倍率</summary>
    public const float SiegeBreakerBonusMultiplier = 0.40f;
    /// <summary>カタストロフ: 使用者への固定ダメージ</summary>
    public const int CatastropheSelfDamage = 20;
    /// <summary>フレイムポイズン: 出血の確率</summary>
    public const float FlamePoisonBleedChance = 0.25f;
    /// <summary>HP50%以下の閾値（デスサイト・LowHPEnemy スキル共通）</summary>
    public const float LowHPThreshold = 0.50f;

    // =====================================================================
    //  初期資源
    // =====================================================================
    public const int InitWood = 200;
    public const int InitStone = 200;
    public const int InitWater = 50;
    public const int InitPlank = 50;
    public const int InitCutStone = 50;
    public const int InitBread = 100;
    public const int InitCitizen = 5;
    public const int InitIron = 30;
    public const int InitMagicOre = 15;
    public const int InitIronOre = 10;
    public const int InitCoal = 10;
    public const int InitialSubCrystals = 2;

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
    /// <summary>カメラ移動速度</summary>
    public const float CameraMoveSpeed = 10f;
    /// <summary>カメラズーム速度</summary>
    public const float CameraScrollSpeed = 5f;

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
