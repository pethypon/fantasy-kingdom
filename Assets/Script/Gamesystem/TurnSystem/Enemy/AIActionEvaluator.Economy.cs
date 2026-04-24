// =====================================================================
//  AIActionEvaluator.Economy — 経済/建築まわりの共通ヘルパー
// =====================================================================
public static partial class AIActionEvaluator
{
    /// <summary>基礎経済施設5種(Well,LoggingCamp,Quarry,Field,House)の設置済み種類数</summary>
    internal static int CalcCoreEconomyCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Well) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Quarry) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Field) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.House) > 0 ? 1 : 0);
    }

    /// <summary>原料生産施設5種(Well,LoggingCamp,Quarry,Field,Mine)の設置済み種類数</summary>
    internal static int CalcRawFacilityCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Well) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Quarry) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Field) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Mine) > 0 ? 1 : 0);
    }

    /// <summary>加工施設(Bakery)の設置済み種類数</summary>
    internal static int CalcProcessingFacilityCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Bakery) > 0 ? 1 : 0);
    }

    /// <summary>資源量に応じた緊急度ボーナス(枯渇→最大, 少量→中, やや不足→小)</summary>
    internal static float ResourceEmergencyBonus(int amount, float depleted, float low, float moderate,
        int lowThreshold = 20, int moderateThreshold = 50)
    {
        if (amount <= 0)                 return depleted;
        if (amount <= lowThreshold)      return low;
        if (amount <= moderateThreshold) return moderate;
        return 0f;
    }

    /// <summary>指定施設が基礎経済5種の中でまだ建っていないものかどうか</summary>
    internal static bool IsMissingCoreFacility(FacilityKind facility, AIBoardState board)
    {
        switch (facility)
        {
            case FacilityKind.Well:        return board.GetBuildingCount(FacilityKind.Well) == 0;
            case FacilityKind.LoggingCamp: return board.GetBuildingCount(FacilityKind.LoggingCamp) == 0;
            case FacilityKind.Quarry:      return board.GetBuildingCount(FacilityKind.Quarry) == 0;
            case FacilityKind.Field:       return board.GetBuildingCount(FacilityKind.Field) == 0;
            case FacilityKind.House:       return board.GetBuildingCount(FacilityKind.House) == 0;
            case FacilityKind.Bakery:      return board.GetBuildingCount(FacilityKind.Bakery) == 0;
            case FacilityKind.Mine:        return board.GetBuildingCount(FacilityKind.Mine) == 0;
            default: return false;
        }
    }

    internal static bool IsProcessingFacility(FacilityKind facility)
    {
        return facility == FacilityKind.Bakery;
    }
}
