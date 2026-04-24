// =====================================================================
//  FacilityData.Walls — 壁 + サブクリスタル データ定義
// =====================================================================
public static partial class FacilityData
{
    // ---- 木壁 ----
    private static void BuildWoodWall()
    {
        var kind = FacilityKind.WoodWall;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData { HP = 250, DEF = 5, ATK = 0 },
            new FacilityLevelData
            {
                HP = 420, DEF = 9, ATK = 0,
                UpgradeCost = new ResourceCost(wood: 35),
                UpgradeAP = 3,
            },
            new FacilityLevelData
            {
                HP = 650, DEF = 14, ATK = 0,
                UpgradeCost = new ResourceCost(wood: 50, stone: 15),
                UpgradeAP = 4,
            },
        };
        Register(kind, "木壁", 2, new ResourceCost(wood: 25), 3, levels);
    }

    // ---- 石壁 ----
    private static void BuildStoneWall()
    {
        var kind = FacilityKind.StoneWall;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData { HP = 550, DEF = 12, ATK = 0 },
            new FacilityLevelData
            {
                HP = 900, DEF = 18, ATK = 0,
                UpgradeCost = new ResourceCost(stone: 60),
                UpgradeAP = 4,
            },
            new FacilityLevelData
            {
                HP = 1400, DEF = 26, ATK = 0,
                UpgradeCost = new ResourceCost(wood: 5, stone: 85),
                UpgradeAP = 5,
            },
        };
        Register(kind, "石壁", 3, new ResourceCost(stone: 40), 3, levels);
    }

    // ---- サブクリスタル ----
    private static void BuildSubCrystal()
    {
        var kind = FacilityKind.SubCrystal;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 5000, DEF = 0, ATK = 0,
            },
        };
        Register(kind, "サブクリスタル", 0, new ResourceCost(), 1, levels);
    }
}
