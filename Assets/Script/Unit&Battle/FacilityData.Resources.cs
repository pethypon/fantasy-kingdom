// =====================================================================
//  FacilityData.Resources — リソース充足/消費/加算/強化
// =====================================================================
public static partial class FacilityData
{
    // ==================================================================
    //  リソース充足チェック（建築コスト用）
    // ==================================================================
    public static bool CanAfford(FactionState.ResourceData res, ResourceCost cost)
    {
        return res.Wood     >= cost.Wood
            && res.Stone    >= cost.Stone
            && res.Iron     >= cost.Iron
            && res.MagicOre >= cost.MagicOre
            && res.Water    >= cost.Water
            && res.Citizen  >= cost.Citizen;
    }

    // ==================================================================
    //  リソース充足チェック（生産入力・維持費用）
    // ==================================================================
    public static bool CanAffordProduction(FactionState.ResourceData res, ProductionBundle cost)
    {
        return res.Wood     >= cost.Wood
            && res.Stone    >= cost.Stone
            && res.Iron     >= cost.Iron
            && res.MagicOre >= cost.MagicOre
            && res.Wheat    >= cost.Wheat
            && res.Bread    >= cost.Bread
            && res.Water    >= cost.Water
            && res.Citizen  >= cost.Citizen;
    }

    // ==================================================================
    //  リソース消費（建築コスト用）
    // ==================================================================
    public static void Consume(FactionState.ResourceData res, ResourceCost cost)
    {
        res.Wood     -= cost.Wood;
        res.Stone    -= cost.Stone;
        res.Iron     -= cost.Iron;
        res.MagicOre -= cost.MagicOre;
        res.Water    -= cost.Water;
        res.Citizen  -= cost.Citizen;
    }

    // ==================================================================
    //  生産バンドル消費
    // ==================================================================
    public static void ConsumeProduction(FactionState.ResourceData res, ProductionBundle cost)
    {
        res.Wood     -= cost.Wood;
        res.Stone    -= cost.Stone;
        res.Iron     -= cost.Iron;
        res.MagicOre -= cost.MagicOre;
        res.Wheat    -= cost.Wheat;
        res.Bread    -= cost.Bread;
        res.Water    -= cost.Water;
        res.Citizen  -= cost.Citizen;
    }

    // ==================================================================
    //  生産バンドル加算
    // ==================================================================
    public static void AddProduction(FactionState.ResourceData res, ProductionBundle output)
    {
        res.Wood     += output.Wood;
        res.Stone    += output.Stone;
        res.Iron     += output.Iron;
        res.MagicOre += output.MagicOre;
        res.Wheat    += output.Wheat;
        res.Bread    += output.Bread;
        res.Water    += output.Water;
        res.Citizen  += output.Citizen;
    }

    // ==================================================================
    //  強化可否チェック
    // ==================================================================
    public static bool CanUpgrade(FactionState.ResourceData res, int currentAP,
                                  FacilityKind kind, int currentLevel)
    {
        int max = GetMaxLevel(kind);
        if (currentLevel >= max) return false;

        var next = GetLevel(kind, currentLevel + 1);
        if (currentAP < next.UpgradeAP) return false;
        return CanAfford(res, next.UpgradeCost);
    }

    // ==================================================================
    //  強化コスト消費
    // ==================================================================
    public static void ConsumeUpgrade(FactionState.ResourceData res, FactionState.APData apData,
                                      FacilityKind kind, int targetLevel)
    {
        var data = GetLevel(kind, targetLevel);
        Consume(res, data.UpgradeCost);
        apData.Current -= data.UpgradeAP;
    }
}
