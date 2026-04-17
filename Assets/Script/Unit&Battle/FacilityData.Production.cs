// =====================================================================
//  FacilityData.Production — 生産建物データ定義
//  畑 / パン屋 / 伐採所 / 製材所 / 採石場 / 石材加工所 / 鉱山 /
//  精錬所 / 井戸 / 兵舎 / 住宅 / 倉庫
// =====================================================================
public static partial class FacilityData
{
    // ---- 畑 ----
    private static void BuildField()
    {
        var kind = FacilityKind.Field;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: 水2消費 → 小麦+5（パン屋1軒分の小麦を確保）
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Water = 2 },
                Output = new ProductionBundle { Wheat = 5 },
            },
            new FacilityLevelData // Lv2: 水2消費 → 小麦+7
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Water = 2 },
                Output = new ProductionBundle { Wheat = 7 },
                UpgradeCost = new ResourceCost(wood: 30, water: 50, plank: 10, cutStone: 10),
                UpgradeAP = 4,
            },
            new FacilityLevelData // Lv3: 水2消費 → 小麦+10
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Water = 2 },
                Output = new ProductionBundle { Wheat = 10 },
                UpgradeCost = new ResourceCost(wood: 15, iron: 5, water: 90, plank: 20, cutStone: 20),
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
            new FacilityLevelData // Lv1: 小麦3+水3 → パン5 & パン+1は20%（市民5人を維持可能）
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Wheat = 3, Water = 3 },
                Output = new ProductionBundle { Bread = 5 },
                BonusOutput1 = new ProductionBundle { Bread = 1 },
                BonusChance1 = 0.20f,
            },
            new FacilityLevelData // Lv2: 小麦3+水3 → パン7 & パン+2は20%
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Wheat = 3, Water = 3 },
                Output = new ProductionBundle { Bread = 7 },
                BonusOutput1 = new ProductionBundle { Bread = 2 },
                BonusChance1 = 0.20f,
                UpgradeCost = new ResourceCost(wood: 50, stone: 35, water: 10, plank: 10),
                UpgradeAP = 6,
            },
            new FacilityLevelData // Lv3: 小麦3+水2 → パン10 & パン+2は30%
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Wheat = 3, Water = 2 },
                Output = new ProductionBundle { Bread = 10 },
                BonusOutput1 = new ProductionBundle { Bread = 2 },
                BonusChance1 = 0.30f,
                UpgradeCost = new ResourceCost(wood: 60, stone: 45, iron: 20, water: 20, plank: 20),
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
            new FacilityLevelData // Lv1: 木+10（クリスタル収入なしを補填、唯一の木材源）
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Wood = 10 },
            },
            new FacilityLevelData // Lv2: 木+14
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Wood = 14 },
                UpgradeCost = new ResourceCost(wood: 35, water: 40),
                UpgradeAP = 5,
            },
            new FacilityLevelData // Lv3: 木+18
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Wood = 18 },
                UpgradeCost = new ResourceCost(wood: 35, iron: 5, water: 60),
                UpgradeAP = 6,
            },
        };
        Register(kind, "伐採所", 4, new ResourceCost(wood: 35, water: 20, citizen: 1), 3, levels);
    }

    // ---- 製材所 ----
    private static void BuildLumberMill()
    {
        var kind = FacilityKind.LumberMill;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: 木3 → 木板+3 & 木板+1は20%（住宅・兵舎に必要な板材を確保）
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Wood = 3 },
                Output = new ProductionBundle { Plank = 3 },
                BonusOutput1 = new ProductionBundle { Plank = 1 },
                BonusChance1 = 0.20f,
            },
            new FacilityLevelData // Lv2: 木3 → 木板+4 & 木板+2は20%
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Wood = 3 },
                Output = new ProductionBundle { Plank = 4 },
                BonusOutput1 = new ProductionBundle { Plank = 2 },
                BonusChance1 = 0.20f,
                UpgradeCost = new ResourceCost(wood: 20, stone: 20, plank: 10, cutStone: 30),
                UpgradeAP = 6,
            },
            new FacilityLevelData // Lv3: 木4 → 木板+6 & 木板+2は30%
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Wood = 4 },
                Output = new ProductionBundle { Plank = 6 },
                BonusOutput1 = new ProductionBundle { Plank = 2 },
                BonusChance1 = 0.30f,
                UpgradeCost = new ResourceCost(wood: 10, stone: 10, iron: 20, plank: 10, cutStone: 60),
                UpgradeAP = 6,
            },
        };
        Register(kind, "製材所", 6, new ResourceCost(wood: 70, stone: 40, citizen: 1), 3, levels);
    }

    // ---- 採石場 ----
    private static void BuildQuarry()
    {
        var kind = FacilityKind.Quarry;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: 石+10 & 石炭+2確定 & 石炭+1は10%
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Stone = 10, Coal = 2 },
                BonusOutput1 = new ProductionBundle { Coal = 1 },
                BonusChance1 = 0.10f,
            },
            new FacilityLevelData // Lv2: 石+14 & 石炭+3確定 & 石炭+1は20%
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Stone = 14, Coal = 3 },
                BonusOutput1 = new ProductionBundle { Coal = 1 },
                BonusChance1 = 0.20f,
                UpgradeCost = new ResourceCost(wood: 20, stone: 20),
                UpgradeAP = 6,
            },
            new FacilityLevelData // Lv3: 石+18 & 石炭+4確定 & 石炭+2は20%
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Stone = 18, Coal = 4 },
                BonusOutput1 = new ProductionBundle { Coal = 2 },
                BonusChance1 = 0.20f,
                UpgradeCost = new ResourceCost(wood: 40, stone: 40, iron: 5, plank: 10, cutStone: 10),
                UpgradeAP = 7,
            },
        };
        Register(kind, "採石場", 4, new ResourceCost(wood: 25, stone: 45, citizen: 1), 3, levels);
    }

    // ---- 石材加工所 ----
    private static void BuildStoneWorks()
    {
        var kind = FacilityKind.StoneWorks;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: 石3 → 石材3 & 石材+1は20%（溶鉱炉・兵舎に必要な切石を確保）
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Stone = 3 },
                Output = new ProductionBundle { CutStone = 3 },
                BonusOutput1 = new ProductionBundle { CutStone = 1 },
                BonusChance1 = 0.20f,
            },
            new FacilityLevelData // Lv2: 石4 → 石材4 & 石材+2は20%
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Stone = 4 },
                Output = new ProductionBundle { CutStone = 4 },
                BonusOutput1 = new ProductionBundle { CutStone = 2 },
                BonusChance1 = 0.20f,
                UpgradeCost = new ResourceCost(wood: 20, stone: 20, plank: 30, cutStone: 10),
                UpgradeAP = 6,
            },
            new FacilityLevelData // Lv3: 石4 → 石材6 & 石材+2は30%
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { Stone = 4 },
                Output = new ProductionBundle { CutStone = 6 },
                BonusOutput1 = new ProductionBundle { CutStone = 2 },
                BonusChance1 = 0.30f,
                UpgradeCost = new ResourceCost(wood: 10, stone: 10, iron: 20, plank: 60, cutStone: 10),
                UpgradeAP = 6,
            },
        };
        Register(kind, "石材加工所", 6, new ResourceCost(wood: 40, stone: 70, citizen: 1), 3, levels);
    }

    // ---- 鉱山 ----
    private static void BuildMine()
    {
        var kind = FacilityKind.Mine;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: 鉄鉱石+4, 石炭+2 & 魔法鉱石+1は50% & 追加+1は50%
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { IronOre = 4, Coal = 2 },
                BonusOutput1 = new ProductionBundle { MagicOre = 1 },
                BonusChance1 = 0.50f,
                BonusOutput2 = new ProductionBundle { MagicOre = 1 },
                BonusChance2 = 0.50f,
            },
            new FacilityLevelData // Lv2: 鉄鉱石+5, 石炭+3 & 魔法鉱石+1は70% & 追加+1は80%
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { IronOre = 5, Coal = 3 },
                BonusOutput1 = new ProductionBundle { MagicOre = 1 },
                BonusChance1 = 0.70f,
                BonusOutput2 = new ProductionBundle { MagicOre = 1 },
                BonusChance2 = 0.80f,
                UpgradeCost = new ResourceCost(wood: 40, stone: 40, iron: 30, plank: 25, cutStone: 25),
                UpgradeAP = 7,
            },
            new FacilityLevelData // Lv3: 鉄鉱石+6, 石炭+4 & 魔法鉱石+2は60% & 追加+1は80%
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { IronOre = 6, Coal = 4 },
                BonusOutput1 = new ProductionBundle { MagicOre = 2 },
                BonusChance1 = 0.60f,
                BonusOutput2 = new ProductionBundle { MagicOre = 1 },
                BonusChance2 = 0.80f,
                UpgradeCost = new ResourceCost(wood: 50, stone: 50, iron: 40, plank: 40, cutStone: 40),
                UpgradeAP = 7,
            },
        };
        Register(kind, "鉱山", 7, new ResourceCost(wood: 50, stone: 90, plank: 15, cutStone: 15, citizen: 1), 3, levels);
    }

    // ---- 精錬所 ----
    private static void BuildSmelter()
    {
        var kind = FacilityKind.Smelter;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: 鉄鉱石4+石炭2 → 鉄3 & 鉄+1は10%
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { IronOre = 4, Coal = 2 },
                Output = new ProductionBundle { Iron = 3 },
                BonusOutput1 = new ProductionBundle { Iron = 1 },
                BonusChance1 = 0.10f,
            },
            new FacilityLevelData // Lv2: 鉄鉱石5+石炭3 → 鉄5 & 鉄+1は20%
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { IronOre = 5, Coal = 3 },
                Output = new ProductionBundle { Iron = 5 },
                BonusOutput1 = new ProductionBundle { Iron = 1 },
                BonusChance1 = 0.20f,
                UpgradeCost = new ResourceCost(wood: 30, stone: 30, iron: 30, cutStone: 50),
                UpgradeAP = 8,
            },
            new FacilityLevelData // Lv3: 鉄鉱石5+石炭3 → 鉄7 & 鉄+2は20%
            {
                HP = 100, DEF = 0, ATK = 0,
                Input  = new ProductionBundle { IronOre = 5, Coal = 3 },
                Output = new ProductionBundle { Iron = 7 },
                BonusOutput1 = new ProductionBundle { Iron = 2 },
                BonusChance1 = 0.20f,
                UpgradeCost = new ResourceCost(wood: 40, stone: 40, iron: 30, cutStone: 60),
                UpgradeAP = 10,
            },
        };
        Register(kind, "精錬所", 7, new ResourceCost(wood: 20, stone: 30, cutStone: 35, citizen: 1), 3, levels);
    }

    // ---- 井戸 ----
    private static void BuildWell()
    {
        var kind = FacilityKind.Well;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: 水+8（クリスタル収入なしを補填）
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Water = 8 },
            },
            new FacilityLevelData // Lv2: 水+12
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Water = 12 },
                UpgradeCost = new ResourceCost(wood: 10, stone: 20, plank: 10, cutStone: 10),
                UpgradeAP = 4,
            },
            new FacilityLevelData // Lv3: 水+16
            {
                HP = 100, DEF = 0, ATK = 0,
                Output = new ProductionBundle { Water = 16 },
                UpgradeCost = new ResourceCost(stone: 20, iron: 10, plank: 30, cutStone: 30),
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
            new FacilityLevelData // Lv1: 経験値+5%
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 5,
            },
            new FacilityLevelData // Lv2: 経験値+8%
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 8,
                UpgradeCost = new ResourceCost(wood: 100, stone: 100, iron: 40, water: 50, plank: 40, cutStone: 40),
                UpgradeAP = 12,
            },
            new FacilityLevelData // Lv3: 経験値+10%
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 10,
                UpgradeCost = new ResourceCost(wood: 100, stone: 100, iron: 50, water: 50, plank: 40, cutStone: 40),
                UpgradeAP = 15,
            },
        };
        Register(kind, "兵舎", 10, new ResourceCost(wood: 100, stone: 100, iron: 30, water: 50, plank: 40, cutStone: 40, citizen: 3), 3, levels);
    }

    // ---- 住宅 ----
    private static void BuildHouse()
    {
        var kind = FacilityKind.House;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: 収容+2
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 2,
            },
            new FacilityLevelData // Lv2: 収容+3
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 3,
                UpgradeCost = new ResourceCost(wood: 60, stone: 60, iron: 30, water: 10, plank: 25, cutStone: 25),
                UpgradeAP = 8,
            },
            new FacilityLevelData // Lv3: 収容+4
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 4,
                UpgradeCost = new ResourceCost(wood: 60, stone: 60, iron: 40, water: 10, plank: 25, cutStone: 25),
                UpgradeAP = 10,
            },
        };
        Register(kind, "家", 7, new ResourceCost(wood: 60, stone: 60, water: 10, plank: 25, cutStone: 25), 3, levels);
    }

    // ---- 高級住宅（Houseの上位互換: +3/Lv1, +4/Lv2, +6/Lv3） ----
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
                UpgradeCost = new ResourceCost(wood: 80, stone: 80, iron: 40, water: 20, plank: 40, cutStone: 40),
                UpgradeAP = 10,
            },
            new FacilityLevelData
            {
                HP = 120, DEF = 0, ATK = 0,
                SpecialValue = 6,
                UpgradeCost = new ResourceCost(wood: 80, stone: 80, iron: 60, magicOre: 5, water: 20, plank: 40, cutStone: 40),
                UpgradeAP = 12,
            },
        };
        Register(kind, "高級住宅", 10,
            new ResourceCost(wood: 100, stone: 100, iron: 20, water: 20, plank: 40, cutStone: 40, citizen: 2),
            3, levels);
    }

    // ---- 倉庫 ----
    private static void BuildWarehouse()
    {
        var kind = FacilityKind.Warehouse;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: 資源容量+25
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 25,
            },
            new FacilityLevelData // Lv2: 資源容量+50
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 50,
                UpgradeCost = new ResourceCost(plank: 60, cutStone: 60),
                UpgradeAP = 8,
            },
            new FacilityLevelData // Lv3: 資源容量+100
            {
                HP = 100, DEF = 0, ATK = 0,
                SpecialValue = 100,
                UpgradeCost = new ResourceCost(iron: 20, plank: 60, cutStone: 60),
                UpgradeAP = 10,
            },
        };
        Register(kind, "倉庫", 7, new ResourceCost(plank: 60, cutStone: 60), 3, levels);
    }
}
