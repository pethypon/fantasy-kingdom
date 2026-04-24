// =====================================================================
//  FacilityData.Offensive — 攻撃型建築物データ定義
//  迫撃砲 / 大砲 / 拘束罠 / トゲ罠 / 英雄の剣
// =====================================================================
public static partial class FacilityData
{
    // ---- 迫撃砲 ----
    private static void BuildMortar()
    {
        var kind = FacilityKind.Mortar;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 220, DEF = 0, ATK = 30,
                Maintenance = new ProductionBundle { Stone = 2, Iron = 2, Water = 1 },
            },
            new FacilityLevelData
            {
                HP = 280, DEF = 0, ATK = 40,
                Maintenance = new ProductionBundle { Stone = 3, Iron = 3, Water = 1 },
                UpgradeCost = new ResourceCost(wood: 60, stone: 140, iron: 25, water: 20),
                UpgradeAP = 8,
            },
            new FacilityLevelData
            {
                HP = 360, DEF = 0, ATK = 55,
                Maintenance = new ProductionBundle { Stone = 4, Iron = 4, Water = 2 },
                UpgradeCost = new ResourceCost(wood: 80, stone: 180, iron: 35, magicOre: 5, water: 25),
                UpgradeAP = 9,
            },
            new FacilityLevelData
            {
                HP = 460, DEF = 0, ATK = 70,
                Maintenance = new ProductionBundle { Stone = 5, Iron = 5, Water = 2 },
                UpgradeCost = new ResourceCost(wood: 110, stone: 230, iron: 45, magicOre: 10, water: 30),
                UpgradeAP = 10,
            },
            new FacilityLevelData
            {
                HP = 600, DEF = 0, ATK = 90,
                Maintenance = new ProductionBundle { Stone = 7, Iron = 7, MagicOre = 1, Water = 3 },
                UpgradeCost = new ResourceCost(wood: 160, stone: 300, iron: 60, magicOre: 20, water: 40),
                UpgradeAP = 12,
            },
        };
        Register(kind, "臼砲", 10, new ResourceCost(wood: 70, stone: 170, iron: 20, water: 20, citizen: 1), 5, levels);
    }

    // ---- 大砲 ----
    private static void BuildCannon()
    {
        var kind = FacilityKind.Cannon;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 240, DEF = 0, ATK = 45,
                Maintenance = new ProductionBundle { Stone = 1, Iron = 4, Water = 1 },
            },
            new FacilityLevelData
            {
                HP = 310, DEF = 0, ATK = 60,
                Maintenance = new ProductionBundle { Stone = 2, Iron = 5, Water = 1 },
                UpgradeCost = new ResourceCost(wood: 90, stone: 170, iron: 70, water: 15),
                UpgradeAP = 9,
            },
            new FacilityLevelData
            {
                HP = 400, DEF = 0, ATK = 75,
                Maintenance = new ProductionBundle { Stone = 3, Iron = 7, Water = 2 },
                UpgradeCost = new ResourceCost(wood: 115, stone: 220, iron: 90, magicOre: 5, water: 20),
                UpgradeAP = 10,
            },
            new FacilityLevelData
            {
                HP = 520, DEF = 0, ATK = 90,
                Maintenance = new ProductionBundle { Stone = 4, Iron = 9, Water = 2 },
                UpgradeCost = new ResourceCost(wood: 150, stone: 280, iron: 110, magicOre: 10, water: 25),
                UpgradeAP = 12,
            },
            new FacilityLevelData
            {
                HP = 700, DEF = 0, ATK = 100,
                Maintenance = new ProductionBundle { Stone = 6, Iron = 12, MagicOre = 1, Water = 3 },
                UpgradeCost = new ResourceCost(wood: 200, stone: 360, iron: 140, magicOre: 20, water: 35),
                UpgradeAP = 15,
            },
        };
        Register(kind, "大砲", 10, new ResourceCost(wood: 50, stone: 140, iron: 60, water: 10, citizen: 1), 5, levels);
    }

    // ---- 拘束罠 ----
    private static void BuildRestraintTrap()
    {
        var kind = FacilityKind.RestraintTrap;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 120, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 1, MagicOre = 1 },
            },
            new FacilityLevelData
            {
                HP = 160, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 1, MagicOre = 2 },
                UpgradeCost = new ResourceCost(wood: 20, stone: 50, iron: 15, magicOre: 5),
                UpgradeAP = 5,
            },
            new FacilityLevelData
            {
                HP = 210, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 2, MagicOre = 2 },
                UpgradeCost = new ResourceCost(wood: 30, stone: 80, iron: 20, magicOre: 10),
                UpgradeAP = 6,
            },
            new FacilityLevelData
            {
                HP = 270, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 2, MagicOre = 3 },
                UpgradeCost = new ResourceCost(wood: 40, stone: 120, iron: 25, magicOre: 15),
                UpgradeAP = 7,
            },
            new FacilityLevelData
            {
                HP = 340, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 3, MagicOre = 4 },
                UpgradeCost = new ResourceCost(wood: 50, stone: 160, iron: 35, magicOre: 20),
                UpgradeAP = 8,
            },
        };
        Register(kind, "拘束罠", 5, new ResourceCost(wood: 20, stone: 40, iron: 10, magicOre: 5), 5, levels);
    }

    // ---- トゲ罠 ----
    private static void BuildSpikeTrap()
    {
        var kind = FacilityKind.SpikeTrap;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 120, DEF = 0, ATK = 20,
                Maintenance = new ProductionBundle { Iron = 1 },
            },
            new FacilityLevelData
            {
                HP = 130, DEF = 0, ATK = 35,
                Maintenance = new ProductionBundle { Iron = 2 },
                UpgradeCost = new ResourceCost(wood: 30, stone: 45, iron: 25),
                UpgradeAP = 5,
            },
            new FacilityLevelData
            {
                HP = 150, DEF = 0, ATK = 50,
                Maintenance = new ProductionBundle { Iron = 2 },
                UpgradeCost = new ResourceCost(wood: 40, stone: 60, iron: 30),
                UpgradeAP = 6,
            },
            new FacilityLevelData
            {
                HP = 170, DEF = 0, ATK = 60,
                Maintenance = new ProductionBundle { Iron = 3 },
                UpgradeCost = new ResourceCost(wood: 50, stone: 75, iron: 40),
                UpgradeAP = 7,
            },
            new FacilityLevelData
            {
                HP = 200, DEF = 0, ATK = 80,
                Maintenance = new ProductionBundle { Iron = 4 },
                UpgradeCost = new ResourceCost(wood: 60, stone: 90, iron: 55),
                UpgradeAP = 8,
            },
        };
        Register(kind, "トゲ罠", 5, new ResourceCost(wood: 30, stone: 30, iron: 20), 5, levels);
    }

    // ---- 勇者の剣 ----
    private static void BuildHeroSword()
    {
        var kind = FacilityKind.HeroSword;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData
            {
                HP = 1, DEF = 0, ATK = 200,
            },
        };
        Register(kind, "英雄の剣", 12, new ResourceCost(wood: 100, stone: 100, iron: 200, magicOre: 80), 1, levels);
    }
}
