// =====================================================================
//  AIActionEvaluator.Economy — 経済/建築まわりの共通ヘルパー
// =====================================================================
public static partial class AIActionEvaluator
{
    internal static float CalcUpgradeBaseScore(AIAction action, AIBoardState board)
    {
        var building = action?.Unit != null ? action.Unit : action?.TargetUnit;
        if (building == null || board == null || !building.IsAlive || building.team != board.ActorTeam)
            return -100f;
        int level = UnityEngine.Mathf.Max(1, building.Level);
        if (level >= FacilityData.GetMaxLevel(building)) return -100f;
        var current = FacilityData.GetLevel(building, level);
        var next = FacilityData.GetLevel(building, level + 1);
        var demand = board.ProductionDemand;
        float score = 0f;
        bool useful = false;
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
        {
            var resource = demand.Get((ResourceKind)i);
            float gain = EconomyHelper.NetProduction(next, i) - EconomyHelper.NetProduction(current, i);
            if (gain > AIEconomySettings.Active.Epsilon && resource.ProductionDeficit > AIEconomySettings.Active.Epsilon)
            {
                useful = true;
                float coverage = UnityEngine.Mathf.Clamp01(gain / resource.ProductionDeficit);
                score += AIEconomySettings.Active.needWeight * coverage
                    * UnityEngine.Mathf.Max(0.2f, resource.Urgency01);
            }
            else if (gain < 0 && resource.ProductionDeficit > AIEconomySettings.Active.Epsilon)
                score -= AIEconomySettings.Active.needWeight * UnityEngine.Mathf.Clamp01(-gain / resource.ProductionDeficit);
        }
        if (!useful && board.Governor?.BasicResources.IsFoundationAction(action) != true) return -100f;
        // Pressure, investment cost and payback are applied once by the Governor to every action path.
        return 15f + score;
    }

    /// <summary>基礎経済施設5種(Well,LoggingCamp,Quarry,Field,House)の設置済み種類数</summary>
    internal static int CalcCoreEconomyCount(AIBoardState board)
        => EconomyHelper.CalcCoreEconomyCount(board);

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

    /// <summary>初期施設の有無の確認用。必要供給量は ProductionDemand で評価する。</summary>
    internal static bool IsMissingCoreFacility(FacilityKind facility, AIBoardState board)
        => EconomyHelper.IsMissingCoreFacility(facility, board);

    internal static bool IsProcessingFacility(FacilityKind facility)
        => EconomyHelper.IsProcessingFacility(facility);
}

// =====================================================================
//  EconomyHelper — 経済判定ユーティリティ（複数クラスで共用）
// =====================================================================
public static class EconomyHelper
{
    public static int CountEconBuildings(AIBoardState board)
    {
        return board.GetBuildingCount(FacilityKind.Well)
             + board.GetBuildingCount(FacilityKind.LoggingCamp)
             + board.GetBuildingCount(FacilityKind.Quarry)
             + board.GetBuildingCount(FacilityKind.Field)
             + board.GetBuildingCount(FacilityKind.Mine);
    }

    public static int CountProcessingBuildings(AIBoardState board)
    {
        return board.GetBuildingCount(FacilityKind.Bakery);
    }

    /// <summary>施設数ではなく、継続供給・支払い予測・安全在庫の不足がないことを確認する。</summary>
    public static bool IsEconomySufficient(AIBoardState board)
    {
        if (board == null || board.EnemyResources == null) return false;
        board.Governor?.Evaluate(board);
        if (AIBasicResourceSettings.Active.enabled && board.Governor?.BasicResources.HasShortage == true)
            return false;
        var demand = board.ProductionDemand;
        var forecast = board.Governor != null ? board.Governor.Economy : demand.Forecast;
        int reserveTurns = board.Governor != null ? board.Governor.BreadReserveTurns : AIEconomySettings.Active.BreadReserveTurns;
        return !demand.HasMandatoryPaymentFailure && !demand.HasCriticalDeficit && !demand.HasWarningDeficit
            && !forecast.BreadPaymentFailed && !forecast.UpkeepPaymentFailed && !forecast.ImportantProductionStopped
            && forecast.BreadCoverageTurns >= reserveTurns;
    }

    public static ProductionBuildAssessment AssessProductionBuild(AIAction action, AIBoardState board)
        => board.Governor != null ? board.Governor.BuildAssessment(action, board) : board.ProductionDemand.EvaluateBuild(action);

    public static bool ImprovesProductionDemand(AIAction action, AIBoardState board)
    {
        if (action == null || board == null) return false;
        if (action.ActionType == AIActionType.Build) return AssessProductionBuild(action, board).ImprovesDeficit;
        if (action.ActionType != AIActionType.Upgrade) return false;
        var building = action.Unit != null ? action.Unit : action.TargetUnit;
        if (building == null || !building.IsAlive || building.team != board.ActorTeam) return false;
        int level = UnityEngine.Mathf.Max(1, building.Level);
        if (level >= FacilityData.GetMaxLevel(building)) return false;
        var current = FacilityData.GetLevel(building, level);
        var next = FacilityData.GetLevel(building, level + 1);
        var demand = board.ProductionDemand;
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
            if (NetProduction(next, i) - NetProduction(current, i) > AIEconomySettings.Active.Epsilon
                && demand.Get((ResourceKind)i).ProductionDeficit > AIEconomySettings.Active.Epsilon) return true;
        return false;
    }

    internal static float NetProduction(FacilityData.FacilityLevelData recipe, int resource)
        => StrategicEconomyForecast.GuaranteedOutput(recipe, resource)
            - StrategicEconomyForecast.Amount(recipe.Input, resource)
            - StrategicEconomyForecast.Amount(recipe.Maintenance, resource);

    public static bool IsMissingCoreFacility(FacilityKind facility, AIBoardState board)
    {
        switch (facility)
        {
            case FacilityKind.Well:
            case FacilityKind.LoggingCamp:
            case FacilityKind.Quarry:
            case FacilityKind.Field:
            case FacilityKind.Bakery:
            case FacilityKind.Mine:
            case FacilityKind.House:
                return board.GetBuildingCount(facility) == 0;
            default:
                return false;
        }
    }

    /// <summary>基礎経済施設5種(Well,LoggingCamp,Quarry,Field,House)の設置済み種類数</summary>
    public static int CalcCoreEconomyCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Well) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Quarry) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Field) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.House) > 0 ? 1 : 0);
    }

    public static bool IsProcessingFacility(FacilityKind facility)
    {
        return facility == FacilityKind.Bakery;
    }
}
