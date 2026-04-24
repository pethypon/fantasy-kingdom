// =====================================================================
//  FacilityData.Production — 生産建物データ定義
//  畑 / パン屋 / 伐採所 / 採石場 / 鉱山 / 井戸 / 兵舎 / 住宅 / 倉庫
//  （製材所・石材加工所・精錬所は廃止、鉱山が直接鉄を産出）
// =====================================================================
public static partial class FacilityData
{
    // ---- 畑 ----
    private static void BuildField()
    {
        var kind = FacilityKind.Field;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Water = 2 },
                Output = new ProductionBundle { Wheat = 5 },
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Water = 2 },
                Output = new ProductionBundle { Wheat = 7 },
                UpgradeCost = new ResourceCost(wood: 50, stone: 20, water: 50),
                UpgradeAP = 4,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Water = 2 },
                Output = new ProductionBundle { Wheat = 10 },
                UpgradeCost = new ResourceCost(wood: 55, stone: 40, iron: 5, water: 90),
                UpgradeAP = 5,
            },
        };
        Register(kind, "畑", 3, new ResourceCost(wood: 20, water: 10, citizen: 1), 3, levels);
    }

    // ---- パン屋 ----
    private static void BuildBakery()
    {
        var kind = FacilityKind.Bakery;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Wheat = 3, Water = 3 },
                Output = new ProductionBundle { Bread = 5 },
                BonusOutput1 = new ProductionBundle { Bread = 1 },
                BonusChance1 = 0.20f,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Wheat = 3, Water = 3 },
                Output = new ProductionBundle { Bread = 7 },
                BonusOutput1 = new ProductionBundle { Bread = 2 },
                BonusChance1 = 0.20f,
                UpgradeCost = new ResourceCost(wood: 70, stone: 35, water: 10),
                UpgradeAP = 6,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Wheat = 3, Water = 2 },
                Output = new ProductionBundle { Bread = 10 },
                BonusOutput1 = new ProductionBundle { Bread = 2 },
                BonusChance1 = 0.30f,
                UpgradeCost = new ResourceCost(wood: 90, stone: 45, iron: 20, water: 20),
                UpgradeAP = 7,
            },
        };
        Register(kind, "パン屋", 5, new ResourceCost(wood: 40, stone: 25, water: 5, citizen: 1), 3, levels);
    }

    // ---- 伐採所 ----
    private static void BuildLoggingCamp()
    {
        var kind = FacilityKind.LoggingCamp;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Wood = 10 },
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Wood = 14 },
                UpgradeCost = new ResourceCost(wood: 35, water: 40),
                UpgradeAP = 5,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Wood = 18 },
                UpgradeCost = new ResourceCost(wood: 35, iron: 5, water: 60),
                UpgradeAP = 6,
            },
        };
        Register(kind, "伐採所", 4, new ResourceCost(wood: 35, water: 20, citizen: 1), 3, levels);
    }

    // ---- 採石場 ----
    private static void BuildQuarry()
    {
        var kind = FacilityKind.Quarry;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Stone = 10 },
                BonusOutput1 = new ProductionBundle { Stone = 2 },
                BonusChance1 = 0.20f,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Stone = 14 },
                BonusOutput1 = new ProductionBundle { Stone = 3 },
                BonusChance1 = 0.30f,
                UpgradeCost = new ResourceCost(wood: 20, stone: 20),
                UpgradeAP = 6,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Stone = 18 },
                BonusOutput1 = new ProductionBundle { Stone = 4 },
                BonusChance1 = 0.30f,
                UpgradeCost = new ResourceCost(wood: 60, stone: 60, iron: 5),
                UpgradeAP = 7,
            },
        };
        Register(kind, "採石場", 4, new ResourceCost(wood: 25, stone: 45, citizen: 1), 3, levels);
    }

    // ---- 鉱山（直接鉄を産出、魔法鉱石は確率） ----
    private static void BuildMine()
    {
        var kind = FacilityKind.Mine;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Iron = 3 },
                BonusOutput1 = new ProductionBundle { MagicOre = 1 },
                BonusChance1 = 0.35f,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Iron = 5 },
                BonusOutput1 = new ProductionBundle { MagicOre = 1 },
                BonusChance1 = 0.55f,
                UpgradeCost = new ResourceCost(wood: 60, stone: 60, iron: 30),
                UpgradeAP = 7,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Iron = 7 },
                BonusOutput1 = new ProductionBundle { MagicOre = 2 },
                BonusChance1 = 0.70f,
                UpgradeCost = new ResourceCost(wood: 80, stone: 80, iron: 40),
                UpgradeAP = 7,
            },
        };
        Register(kind, "鉱山", 7, new ResourceCost(wood: 70, stone: 110, citizen: 1), 3, levels);
    }

    // ---- 井戸 ----
    private static void BuildWell()
    {
        var kind = FacilityKind.Well;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Water = 8 },
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Water = 12 },
                UpgradeCost = new ResourceCost(wood: 20, stone: 30),
                UpgradeAP = 4,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Water = 16 },
                UpgradeCost = new ResourceCost(wood: 30, stone: 50, iron: 10),
                UpgradeAP = 5,
            },
        };
        Register(kind, "井戸", 3, new ResourceCost(wood: 20, stone: 30), 3, levels);
    }

    // ---- 兵舎 ----
    private static void BuildBarracks()
    {
        var kind = FacilityKind.Barracks;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 5,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 8,
                UpgradeCost = new ResourceCost(wood: 140, stone: 140, iron: 40, water: 50),
                UpgradeAP = 12,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 10,
                UpgradeCost = new ResourceCost(wood: 140, stone: 140, iron: 50, water: 50),
                UpgradeAP = 15,
            },
        };
        Register(kind, "兵舎", 10, new ResourceCost(wood: 140, stone: 140, iron: 30, water: 50, citizen: 3), 3, levels);
    }

    // ---- 住宅 ----
    private static void BuildHouse()
    {
        var kind = FacilityKind.House;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 2,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 3,
                UpgradeCost = new ResourceCost(wood: 85, stone: 85, iron: 30, water: 10),
                UpgradeAP = 8,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 4,
                UpgradeCost = new ResourceCost(wood: 85, stone: 85, iron: 40, water: 10),
                UpgradeAP = 10,
            },
        };
        Register(kind, "家", 7, new ResourceCost(wood: 85, stone: 85, water: 10), 3, levels);
    }

    // ---- 高級住宅 ----
    private static void BuildLuxuryHouse()
    {
        var kind = FacilityKind.LuxuryHouse;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 120, DEF = 0, ATK = 0,
                SpecialValue = 3,
            },
            new FacilityLevelData
            {
                HP = 120, DEF = 0, ATK = 0,
                SpecialValue = 4,
                UpgradeCost = new ResourceCost(wood: 120, stone: 120, iron: 40, water: 20),
                UpgradeAP = 10,
            },
            new FacilityLevelData
            {
                HP = 120, DEF = 0, ATK = 0,
                SpecialValue = 6,
                UpgradeCost = new ResourceCost(wood: 120, stone: 120, iron: 60, magicOre: 5, water: 20),
                UpgradeAP = 12,
            },
        };
        Register(kind, "高級住宅", 10,
            new ResourceCost(wood: 140, stone: 140, iron: 20, water: 20, citizen: 2),
            3, levels);
    }

    // ---- 倉庫 ----
    private static void BuildWarehouse()
    {
        var kind = FacilityKind.Warehouse;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 25,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 50,
                UpgradeCost = new ResourceCost(wood: 60, stone: 60),
                UpgradeAP = 8,
            },
            new FacilityLevelData
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 100,
                UpgradeCost = new ResourceCost(wood: 60, stone: 60, iron: 20),
                UpgradeAP = 10,
            },
        };
        Register(kind, "倉庫", 7, new ResourceCost(wood: 60, stone: 60), 3, levels);
    }
}
