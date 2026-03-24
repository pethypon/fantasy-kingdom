/// <summary>
/// AI評価関数で使用する定数を一元管理。
/// 調整時にはここだけ変更すればよい。
/// </summary>
public static class AIConstants
{
    // =====================================================================
    //  駒価値 (PieceValue)
    // =====================================================================
    public const float PV_Crystal      = 200f;
    public const float PV_King         = 100f;
    public const float PV_Boss         = 80f;
    public const float PV_Magicsniper  = 35f;
    public const float PV_Priest       = 35f;
    public const float PV_Bomber       = 32f;
    public const float PV_Magic        = 30f;
    public const float PV_Guardian     = 30f;
    public const float PV_Archer       = 28f;
    public const float PV_Crossbow     = 28f;
    public const float PV_Assassin     = 26f;
    public const float PV_Knight       = 25f;
    public const float PV_Scout        = 18f;
    public const float PV_Default      = 20f;

    // =====================================================================
    //  攻撃射程推定
    // =====================================================================
    public const float AR_Archer       = 3f;
    public const float AR_Magic        = 2f;
    public const float AR_Crossbow     = 2f;
    public const float AR_Magicsniper  = 4f;
    public const float AR_Bomber       = 3f;
    public const float AR_Default      = 1.5f;

    // =====================================================================
    //  SimBoardEvaluator 重み
    // =====================================================================
    public const float W_MATERIAL         = 1.0f;
    public const float W_CRYSTAL_SAFETY   = 1.8f;
    public const float W_KING_SAFETY      = 1.2f;
    public const float W_FRONTLINE        = 0.5f;
    public const float W_POSITIONAL       = 0.7f;
    public const float W_MOBILITY         = 0.4f;
    public const float W_ECONOMY          = 0.7f;
    public const float W_THREAT           = 0.9f;
    public const float W_TEMPO            = 0.3f;
    public const float W_RESOURCE_PROJ    = 0.5f;
    public const float W_TERRITORY        = 0.4f;
    public const float W_COORDINATION     = 0.6f;
    public const float W_VISION           = 0.35f;

    // =====================================================================
    //  クリスタル安全度
    // =====================================================================
    public const float CS_HP_Weight        = 50f;
    public const float CS_Shield_Per_Turn  = 8f;
    public const float CS_Guard_Per_Unit   = 6f;
    public const float CS_Guard_Max        = 30f;
    public const float CS_Threat_Per_Unit  = 18f;
    public const float CS_Lost_Penalty     = 500f;

    // =====================================================================
    //  King安全度
    // =====================================================================
    public const float KS_HP_Weight        = 30f;
    public const float KS_Threat_Per_Enemy = 12f;
    public const float KS_Guard_Per_Ally   = 4f;
    public const float KS_Guard_Max        = 16f;
    public const float KS_Death_Penalty    = 300f;

    // =====================================================================
    //  建築関連
    // =====================================================================
    public const float BUILD_Core_Type_Value   = 5f;
    public const float BUILD_Process_Value     = 4f;
    public const float BUILD_Barracks_Value    = 6f;
    public const float BUILD_House_AP_Value    = 5f;
    public const float BUILD_Per_Building      = 3f;
    public const float BUILD_Max_Total         = 30f;

    // =====================================================================
    //  建築シナジーボーナス（原料→加工チェーンが揃っている場合）
    // =====================================================================
    public const float SYNERGY_LogLumber    = 8f;  // LoggingCamp + LumberMill
    public const float SYNERGY_QuarryStone  = 8f;  // Quarry + StoneWorks
    public const float SYNERGY_MineSmelter  = 10f; // Mine + Smelter
    public const float SYNERGY_FieldBakery  = 7f;  // Field + Bakery

    // =====================================================================
    //  資源投影（1ターンあたりの推定収入価値）
    // =====================================================================
    public const float RP_Well           = 2.0f;
    public const float RP_LoggingCamp    = 2.5f;
    public const float RP_Quarry         = 2.5f;
    public const float RP_Field          = 2.0f;
    public const float RP_Mine           = 3.0f;
    public const float RP_LumberMill     = 3.5f;
    public const float RP_StoneWorks     = 3.5f;
    public const float RP_Smelter        = 4.0f;
    public const float RP_Bakery         = 3.0f;
    public const float RP_Projection_Turns = 3f;  // 何ターン先の収入まで考慮するか

    // =====================================================================
    //  領土/視界
    // =====================================================================
    public const float TERRITORY_Per_Cell  = 0.3f;
    public const float TERRITORY_Max       = 25f;
    public const float VISION_Per_Cell     = 0.15f;
    public const float VISION_Max          = 20f;
    public const float VISION_Scout_Bonus  = 3f;  // Scoutによる視界拡大ボーナス

    // =====================================================================
    //  連携攻撃ボーナス
    // =====================================================================
    public const float COORD_Dual_Threat   = 8f;   // 2体以上で同一ターゲットを脅かす
    public const float COORD_Triple_Threat  = 15f;  // 3体以上
    public const float COORD_Kill_Threat    = 12f;  // 連携で確殺可能

    // =====================================================================
    //  QuickScore パラメータ
    // =====================================================================
    public const float QS_Attack_DmgMult     = 2.0f;
    public const float QS_Kill_Crystal       = 200f;
    public const float QS_Kill_King          = 100f;
    public const float QS_Kill_Unit          = 50f;
    public const float QS_Skill_DmgMult      = 1.8f;
    public const float QS_Skill_Debuff       = 5f;
    public const float QS_Move_DistMult      = 2.0f;
    public const float QS_Move_AttackRange   = 5f;
    public const float QS_Build_Base         = 10f;
    public const float QS_Summon_Base        = 15f;
    public const float QS_Upgrade_Base       = 12f;
    public const float QS_AP_Penalty         = 0.3f;

    // =====================================================================
    //  脅威度帯 (AIThreatLevel に定義、ここは参照用コメント)
    //  1-10: チュートリアル帯 (3手先), 11-20: ノーマル帯 (5手先),
    //  21-30: ハード帯 (8手先), 31-100: やりこみ帯 (10/15/20手先)
    // =====================================================================

    // =====================================================================
    //  ゲームフェーズ閾値
    // =====================================================================
    /// <summary>序盤の終わりターン数</summary>
    public const int TurnEarlyEnd = 5;
    /// <summary>中盤の終わりターン数</summary>
    public const int TurnMidEnd = 12;
    /// <summary>生産施設優先度ブースト開始ターン</summary>
    public const int TurnProductionBoost = 20;
    /// <summary>全建築大幅ブースト開始ターン</summary>
    public const int TurnLateBuildBoost = 30;

    // =====================================================================
    //  ゲーム終了 極値
    // =====================================================================
    public const float TERMINAL_Win  = 10000f;
    public const float TERMINAL_Lose = -10000f;
    public const float TERMINAL_King_Win  = 8000f;
    public const float TERMINAL_King_Lose = -8000f;

    // =====================================================================
    //  ヘルパー: 駒価値取得
    // =====================================================================
    public static float GetPieceValue(Kind kind)
    {
        switch (kind)
        {
            case Kind.Crystal:      return PV_Crystal;
            case Kind.King:         return PV_King;
            case Kind.Boss:         return PV_Boss;
            case Kind.Magicsniper:  return PV_Magicsniper;
            case Kind.Priest:       return PV_Priest;
            case Kind.Bomber:       return PV_Bomber;
            case Kind.Magic:        return PV_Magic;
            case Kind.Guardian:     return PV_Guardian;
            case Kind.Archer:       return PV_Archer;
            case Kind.Crossbow:     return PV_Crossbow;
            case Kind.Assassin:     return PV_Assassin;
            case Kind.Knight:       return PV_Knight;
            case Kind.Scout:        return PV_Scout;
            default:                return PV_Default;
        }
    }

    /// <summary>攻撃射程推定</summary>
    public static float GetAttackRange(Kind kind)
    {
        switch (kind)
        {
            case Kind.Archer:       return AR_Archer;
            case Kind.Magic:        return AR_Magic;
            case Kind.Crossbow:     return AR_Crossbow;
            case Kind.Magicsniper:  return AR_Magicsniper;
            case Kind.Bomber:       return AR_Bomber;
            default:                return AR_Default;
        }
    }

    /// <summary>建物1棟あたりの推定資源収入価値</summary>
    public static float GetResourceProjection(FacilityKind fk)
    {
        switch (fk)
        {
            case FacilityKind.Well:        return RP_Well;
            case FacilityKind.LoggingCamp: return RP_LoggingCamp;
            case FacilityKind.Quarry:      return RP_Quarry;
            case FacilityKind.Field:       return RP_Field;
            case FacilityKind.Mine:        return RP_Mine;
            case FacilityKind.LumberMill:  return RP_LumberMill;
            case FacilityKind.StoneWorks:  return RP_StoneWorks;
            case FacilityKind.Smelter:     return RP_Smelter;
            case FacilityKind.Bakery:      return RP_Bakery;
            default: return 0f;
        }
    }
}
