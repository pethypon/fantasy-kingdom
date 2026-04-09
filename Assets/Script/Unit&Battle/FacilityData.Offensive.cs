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
            new FacilityLevelData // Lv1
            {
                HP = 220, DEF = 0, ATK = 30,
                Maintenance = new ProductionBundle { Stone = 2, Iron = 2, Water = 1 },
            },
            new FacilityLevelData // Lv2
            {
                HP = 280, DEF = 0, ATK = 40,
                Maintenance = new ProductionBundle { Stone = 3, Iron = 3, Water = 1 },
                UpgradeCost = new ResourceCost(wood: 20, stone: 70, iron: 25, water: 20, plank: 20, cutStone: 35),
                UpgradeAP = 8,
            },
            new FacilityLevelData // Lv3
            {
                HP = 360, DEF = 0, ATK = 55,
                Maintenance = new ProductionBundle { Stone = 4, Iron = 4, Water = 2 },
                UpgradeCost = new ResourceCost(wood: 20, stone: 80, iron: 35, magicOre: 5, water: 25, plank: 30, cutStone: 50),
                UpgradeAP = 9,
            },
            new FacilityLevelData // Lv4
            {
                HP = 460, DEF = 0, ATK = 70,
                Maintenance = new ProductionBundle { Stone = 5, Iron = 5, Water = 2 },
                UpgradeCost = new ResourceCost(wood: 30, stone: 90, iron: 45, magicOre: 10, water: 30, plank: 40, cutStone: 70),
                UpgradeAP = 10,
            },
            new FacilityLevelData // Lv5
            {
                HP = 600, DEF = 0, ATK = 90,
                Maintenance = new ProductionBundle { Stone = 7, Iron = 7, MagicOre = 1, Water = 3 },
                UpgradeCost = new ResourceCost(wood: 40, stone: 120, iron: 60, magicOre: 20, water: 40, plank: 60, cutStone: 90),
                UpgradeAP = 12,
            },
        };
        Register(kind, "臼砲", 10, new ResourceCost(wood: 30, stone: 90, iron: 20, water: 20, plank: 20, cutStone: 40, citizen: 1), 5, levels);
    }

    // ---- 大砲 ----
    private static void BuildCannon()
    {
        var kind = FacilityKind.Cannon;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1
            {
                HP = 240, DEF = 0, ATK = 45,
                Maintenance = new ProductionBundle { Stone = 1, Iron = 4, Water = 1 },
            },
            new FacilityLevelData // Lv2
            {
                HP = 310, DEF = 0, ATK = 60,
                Maintenance = new ProductionBundle { Stone = 2, Iron = 5, Water = 1 },
                UpgradeCost = new ResourceCost(wood: 20, stone: 70, iron: 70, water: 15, plank: 35, cutStone: 50),
                UpgradeAP = 9,
            },
            new FacilityLevelData // Lv3
            {
                HP = 400, DEF = 0, ATK = 75,
                Maintenance = new ProductionBundle { Stone = 3, Iron = 7, Water = 2 },
                UpgradeCost = new ResourceCost(wood: 25, stone: 80, iron: 90, magicOre: 5, water: 20, plank: 45, cutStone: 70),
                UpgradeAP = 10,
            },
            new FacilityLevelData // Lv4
            {
                HP = 520, DEF = 0, ATK = 90,
                Maintenance = new ProductionBundle { Stone = 4, Iron = 9, Water = 2 },
                UpgradeCost = new ResourceCost(wood: 30, stone: 100, iron: 110, magicOre: 10, water: 25, plank: 60, cutStone: 90),
                UpgradeAP = 12,
            },
            new FacilityLevelData // Lv5
            {
                HP = 700, DEF = 0, ATK = 100,
                Maintenance = new ProductionBundle { Stone = 6, Iron = 12, MagicOre = 1, Water = 3 },
                UpgradeCost = new ResourceCost(wood: 40, stone: 120, iron: 140, magicOre: 20, water: 35, plank: 80, cutStone: 120),
                UpgradeAP = 15,
            },
        };
        Register(kind, "大砲", 10, new ResourceCost(wood: 20, stone: 60, iron: 60, water: 10, plank: 30, cutStone: 40, citizen: 1), 5, levels);
    }

    // ---- 拘束罠 ----
    private static void BuildRestraintTrap()
    {
        var kind = FacilityKind.RestraintTrap;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1
            {
                HP = 120, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 1, MagicOre = 1 },
            },
            new FacilityLevelData // Lv2
            {
                HP = 160, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 1, MagicOre = 2 },
                UpgradeCost = new ResourceCost(wood: 10, stone: 20, iron: 15, magicOre: 5, plank: 10, cutStone: 10),
                UpgradeAP = 5,
            },
            new FacilityLevelData // Lv3
            {
                HP = 210, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 2, MagicOre = 2 },
                UpgradeCost = new ResourceCost(wood: 15, stone: 30, iron: 20, magicOre: 10, plank: 15, cutStone: 20),
                UpgradeAP = 6,
            },
            new FacilityLevelData // Lv4
            {
                HP = 270, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 2, MagicOre = 3 },
                UpgradeCost = new ResourceCost(wood: 20, stone: 40, iron: 25, magicOre: 15, plank: 20, cutStone: 30),
                UpgradeAP = 7,
            },
            new FacilityLevelData // Lv5
            {
                HP = 340, DEF = 0, ATK = 0,
                Maintenance = new ProductionBundle { Iron = 3, MagicOre = 4 },
                UpgradeCost = new ResourceCost(wood: 25, stone: 50, iron: 35, magicOre: 20, plank: 25, cutStone: 40),
                UpgradeAP = 8,
            },
        };
        Register(kind, "拘束罠", 5, new ResourceCost(wood: 10, stone: 20, iron: 10, magicOre: 5, plank: 10), 5, levels);
    }

    // ---- トゲ罠 ----
    private static void BuildSpikeTrap()
    {
        var kind = FacilityKind.SpikeTrap;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1
            {
                HP = 120, DEF = 0, ATK = 20,
                Maintenance = new ProductionBundle { Iron = 1 },
            },
            new FacilityLevelData // Lv2
            {
                HP = 130, DEF = 0, ATK = 35,
                Maintenance = new ProductionBundle { Iron = 2 },
                UpgradeCost = new ResourceCost(wood: 15, stone: 15, iron: 25, plank: 15, cutStone: 15),
                UpgradeAP = 5,
            },
            new FacilityLevelData // Lv3
            {
                HP = 150, DEF = 0, ATK = 50,
                Maintenance = new ProductionBundle { Iron = 2 },
                UpgradeCost = new ResourceCost(wood: 20, stone: 20, iron: 30, plank: 20, cutStone: 20),
                UpgradeAP = 6,
            },
            new FacilityLevelData // Lv4
            {
                HP = 170, DEF = 0, ATK = 60,
                Maintenance = new ProductionBundle { Iron = 3 },
                UpgradeCost = new ResourceCost(wood: 25, stone: 25, iron: 40, plank: 25, cutStone: 25),
                UpgradeAP = 7,
            },
            new FacilityLevelData // Lv5
            {
                HP = 200, DEF = 0, ATK = 80,
                Maintenance = new ProductionBundle { Iron = 4 },
                UpgradeCost = new ResourceCost(wood: 30, stone: 30, iron: 55, plank: 30, cutStone: 30),
                UpgradeAP = 8,
            },
        };
        Register(kind, "トゲ罠", 5, new ResourceCost(wood: 15, stone: 10, iron: 20, plank: 15, cutStone: 10), 5, levels);
    }

    // ---- 勇者の剣 ----
    private static void BuildHeroSword()
    {
        var kind = FacilityKind.HeroSword;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1（強化なし、攻撃したら壊れる）
            {
                HP = 1, DEF = 0, ATK = 200,
            },
        };
        Register(kind, "英雄の剣", 12, new ResourceCost(iron: 150, magicOre: 80, plank: 50, cutStone: 50), 1, levels);
    }
}
