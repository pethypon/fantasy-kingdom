using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 建築物の制作コスト・ステータス・生産レシピ・強化データを静的に定義する。
/// GameReference 準拠。
/// </summary>
public static class FacilityData
{
    // ==================================================================
    //  リソースコスト（建築・強化時消費用、8種）
    // ==================================================================
    public struct ResourceCost
    {
        public int Wood;
        public int Stone;
        public int Iron;
        public int MagicOre;
        public int Water;
        public int Plank;
        public int CutStone;
        public int Citizen;

        public ResourceCost(int wood = 0, int stone = 0, int iron = 0,
                            int magicOre = 0, int water = 0,
                            int plank = 0, int cutStone = 0, int citizen = 0)
        {
            Wood = wood; Stone = stone; Iron = iron; MagicOre = magicOre;
            Water = water; Plank = plank; CutStone = cutStone; Citizen = citizen;
        }
    }

    // ==================================================================
    //  生産バンドル（毎ターン生産用、全12資源対応）
    // ==================================================================
    public struct ProductionBundle
    {
        public int Wood;
        public int Stone;
        public int Coal;
        public int IronOre;
        public int Iron;
        public int MagicOre;
        public int Wheat;
        public int Bread;
        public int Water;
        public int Plank;
        public int CutStone;
        public int Citizen;

        public bool IsEmpty =>
            Wood == 0 && Stone == 0 && Coal == 0 && IronOre == 0 &&
            Iron == 0 && MagicOre == 0 && Wheat == 0 && Bread == 0 &&
            Water == 0 && Plank == 0 && CutStone == 0 && Citizen == 0;
    }

    // ==================================================================
    //  レベル別データ
    // ==================================================================
    public struct FacilityLevelData
    {
        // ステータス
        public int HP;
        public int DEF;
        public int ATK;

        // 生産（毎ターン）
        public ProductionBundle Input;           // 確定消費
        public ProductionBundle Output;          // 確定産出
        public ProductionBundle BonusOutput1;    // 確率産出1
        public float BonusChance1;               // 確率1 (0.0-1.0)
        public ProductionBundle BonusOutput2;    // 確率産出2
        public float BonusChance2;               // 確率2 (0.0-1.0)

        // 維持費（毎ターン、攻撃型建築物のみ）
        public ProductionBundle Maintenance;

        // 強化コスト（このレベルへの強化に必要なコスト。Lv1は使わない）
        public ResourceCost UpgradeCost;
        public int UpgradeAP;

        // 特殊値（House=収容数, Warehouse=容量, Barracks=経験値%）
        public int SpecialValue;

        public bool HasProduction => !Output.IsEmpty || BonusChance1 > 0 || BonusChance2 > 0;
    }

    // ==================================================================
    //  建築物基本情報（Lv1データ + ビルドコスト）
    // ==================================================================
    public struct FacilityInfo
    {
        public int APCost;
        public int HP;
        public int DEF;
        public int ATK;
        public ResourceCost BuildCost;
        public string DisplayName;
        public int MaxLevel;
    }

    // ==================================================================
    //  基本情報テーブル（後方互換: Lv1 ステータス + ビルドコスト）
    // ==================================================================
    public static readonly Dictionary<FacilityKind, FacilityInfo> Table;

    // ==================================================================
    //  レベルデータテーブル
    // ==================================================================
    private static readonly Dictionary<FacilityKind, FacilityLevelData[]> _levels;

    /// <summary>
    /// 指定施設・レベルのデータを取得する。level は 1-based。
    /// </summary>
    public static FacilityLevelData GetLevel(FacilityKind kind, int level)
    {
        if (!_levels.TryGetValue(kind, out var arr)) return default;
        int idx = Mathf.Clamp(level - 1, 0, arr.Length - 1);
        return arr[idx];
    }

    /// <summary>
    /// 指定施設の最大レベルを返す。
    /// </summary>
    public static int GetMaxLevel(FacilityKind kind)
    {
        if (Table.TryGetValue(kind, out var info)) return info.MaxLevel;
        return 1;
    }

    // ==================================================================
    //  静的コンストラクタ — 全データ構築
    // ==================================================================
    static FacilityData()
    {
        _levels = new Dictionary<FacilityKind, FacilityLevelData[]>();
        Table = new Dictionary<FacilityKind, FacilityInfo>();

        // ---- 生産建物 ----
        BuildField();
        BuildBakery();
        BuildLoggingCamp();
        BuildLumberMill();
        BuildQuarry();
        BuildStoneWorks();
        BuildMine();
        BuildSmelter();
        BuildWell();
        BuildBarracks();
        BuildHouse();
        BuildWarehouse();

        // ---- 壁 ----
        BuildWoodWall();
        BuildStoneWall();

        // ---- サブクリスタル ----
        BuildSubCrystal();

        // ---- 攻撃型建築物 ----
        BuildMortar();
        BuildCannon();
        BuildRestraintTrap();
        BuildSpikeTrap();
        BuildHeroSword();
    }

    // ==================================================================
    //  生産建物
    // ==================================================================

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
        Register(kind, "製材所", 6, new ResourceCost(wood: 70, stone: 40, cutStone: 15, citizen: 1), 3, levels);
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
        Register(kind, "石材加工所", 6, new ResourceCost(wood: 40, stone: 70, plank: 15, citizen: 1), 3, levels);
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

    // ==================================================================
    //  壁
    // ==================================================================

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
                UpgradeCost = new ResourceCost(wood: 20, plank: 15),
                UpgradeAP = 3,
            },
            new FacilityLevelData
            {
                HP = 650, DEF = 14, ATK = 0,
                UpgradeCost = new ResourceCost(wood: 30, stone: 5, plank: 20, cutStone: 5),
                UpgradeAP = 4,
            },
        };
        Register(kind, "木壁", 2, new ResourceCost(wood: 15, plank: 10), 3, levels);
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
                UpgradeCost = new ResourceCost(stone: 35, cutStone: 25),
                UpgradeAP = 4,
            },
            new FacilityLevelData
            {
                HP = 1400, DEF = 26, ATK = 0,
                UpgradeCost = new ResourceCost(stone: 50, plank: 5, cutStone: 35),
                UpgradeAP = 5,
            },
        };
        Register(kind, "石壁", 3, new ResourceCost(stone: 25, cutStone: 15), 3, levels);
    }

    // ==================================================================
    //  サブクリスタル
    // ==================================================================

    private static void BuildSubCrystal()
    {
        var kind = FacilityKind.SubCrystal;
        var levels = new FacilityLevelData[]
        {
            new FacilityLevelData // Lv1: HP5000, 領地拡張用
            {
                HP = 5000, DEF = 0, ATK = 0,
            },
        };
        // AP0, リソースコスト0（サブクリスタル資源を1消費する別ロジック）
        Register(kind, "サブクリスタル", 0, new ResourceCost(), 1, levels);
    }

    // ==================================================================
    //  攻撃型建築物
    // ==================================================================

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

    // ==================================================================
    //  登録ヘルパー
    // ==================================================================
    private static void Register(FacilityKind kind, string displayName, int buildAP,
                                 ResourceCost buildCost, int maxLevel, FacilityLevelData[] levels)
    {
        _levels[kind] = levels;

        var lv1 = levels[0];
        Table[kind] = new FacilityInfo
        {
            DisplayName = displayName,
            APCost = buildAP,
            BuildCost = buildCost,
            MaxLevel = maxLevel,
            HP = lv1.HP,
            DEF = lv1.DEF,
            ATK = lv1.ATK,
        };
    }

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
            && res.Plank    >= cost.Plank
            && res.CutStone >= cost.CutStone
            && res.Citizen  >= cost.Citizen;
    }

    // ==================================================================
    //  リソース充足チェック（生産入力・維持費用）
    // ==================================================================
    public static bool CanAffordProduction(FactionState.ResourceData res, ProductionBundle cost)
    {
        return res.Wood     >= cost.Wood
            && res.Stone    >= cost.Stone
            && res.Coal     >= cost.Coal
            && res.IronOre  >= cost.IronOre
            && res.Iron     >= cost.Iron
            && res.MagicOre >= cost.MagicOre
            && res.Wheat    >= cost.Wheat
            && res.Bread    >= cost.Bread
            && res.Water    >= cost.Water
            && res.Plank    >= cost.Plank
            && res.CutStone >= cost.CutStone
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
        res.Plank    -= cost.Plank;
        res.CutStone -= cost.CutStone;
        res.Citizen  -= cost.Citizen;
    }

    // ==================================================================
    //  生産バンドル消費
    // ==================================================================
    public static void ConsumeProduction(FactionState.ResourceData res, ProductionBundle cost)
    {
        res.Wood     -= cost.Wood;
        res.Stone    -= cost.Stone;
        res.Coal     -= cost.Coal;
        res.IronOre  -= cost.IronOre;
        res.Iron     -= cost.Iron;
        res.MagicOre -= cost.MagicOre;
        res.Wheat    -= cost.Wheat;
        res.Bread    -= cost.Bread;
        res.Water    -= cost.Water;
        res.Plank    -= cost.Plank;
        res.CutStone -= cost.CutStone;
        res.Citizen  -= cost.Citizen;
    }

    // ==================================================================
    //  生産バンドル加算
    // ==================================================================
    public static void AddProduction(FactionState.ResourceData res, ProductionBundle output)
    {
        res.Wood     += output.Wood;
        res.Stone    += output.Stone;
        res.Coal     += output.Coal;
        res.IronOre  += output.IronOre;
        res.Iron     += output.Iron;
        res.MagicOre += output.MagicOre;
        res.Wheat    += output.Wheat;
        res.Bread    += output.Bread;
        res.Water    += output.Water;
        res.Plank    += output.Plank;
        res.CutStone += output.CutStone;
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

    // ==================================================================
    //  壁かどうか
    // ==================================================================
    public static bool IsWall(FacilityKind kind)
    {
        return kind == FacilityKind.WoodWall || kind == FacilityKind.StoneWall;
    }

    // ==================================================================
    //  攻撃型建築物かどうか
    // ==================================================================
    public static bool IsOffensive(FacilityKind kind)
    {
        return kind == FacilityKind.Mortar
            || kind == FacilityKind.Cannon
            || kind == FacilityKind.RestraintTrap
            || kind == FacilityKind.SpikeTrap
            || kind == FacilityKind.HeroSword;
    }

    // ==================================================================
    //  サブクリスタルかどうか
    // ==================================================================
    public static bool IsSubCrystal(FacilityKind kind)
    {
        return kind == FacilityKind.SubCrystal;
    }

    // ==================================================================
    //  FacilityKind → Kind 変換（壁・サブクリスタル）
    // ==================================================================
    public static Kind ToUnitKind(FacilityKind facility)
    {
        switch (facility)
        {
            case FacilityKind.WoodWall:   return Kind.WoodWall;
            case FacilityKind.StoneWall:  return Kind.StoneWall;
            case FacilityKind.SubCrystal: return Kind.SubCrystal;
            default:                      return Kind.None;
        }
    }
}
