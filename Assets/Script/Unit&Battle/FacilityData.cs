using System.Collections.Generic;

/// <summary>
/// 建築物の制作コスト・ステータス・生産レシピを静的に定義する。
/// </summary>
public static class FacilityData
{
    // ---- リソースコスト（建築時消費用、8種） ----
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

    // ---- 生産バンドル（毎ターン生産用、全12種対応） ----
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

    // ---- 生産レシピ（毎ターン消費→産出） ----
    public struct ProductionRecipe
    {
        public ProductionBundle Input;
        public ProductionBundle Output;

        public bool HasProduction => !Output.IsEmpty;
    }

    // ---- 建築物情報 ----
    public struct FacilityInfo
    {
        public int APCost;
        public int HP;
        public int DEF;
        public int ATK;
        public ResourceCost BuildCost;
        public string DisplayName;
        public ProductionRecipe Production;

        public FacilityInfo(string displayName, int ap, int hp, int def, int atk,
                            ResourceCost cost, ProductionRecipe production = default)
        {
            DisplayName = displayName;
            APCost = ap; HP = hp; DEF = def; ATK = atk;
            BuildCost = cost;
            Production = production;
        }
    }

    // ---- 全建築物テーブル ----
    public static readonly Dictionary<FacilityKind, FacilityInfo> Table =
        new Dictionary<FacilityKind, FacilityInfo>
    {
        // ---- 生産建物 ----
        // 畑: 水5消費 → 小麦8生産
        { FacilityKind.Field,       new FacilityInfo("畑",       3, 100, 0, 0,
            new ResourceCost(wood: 20, water: 10, citizen: 1),
            new ProductionRecipe {
                Input  = new ProductionBundle { Water = 5 },
                Output = new ProductionBundle { Wheat = 8 }
            }) },
        // パン屋: 小麦6消費 → パン4生産
        { FacilityKind.Bakery,      new FacilityInfo("パン屋",    5, 100, 0, 0,
            new ResourceCost(wood: 40, stone: 25, water: 5, citizen: 1),
            new ProductionRecipe {
                Input  = new ProductionBundle { Wheat = 6 },
                Output = new ProductionBundle { Bread = 4 }
            }) },
        // 伐採所: → 木材10生産
        { FacilityKind.LoggingCamp, new FacilityInfo("伐採所",    4, 100, 0, 0,
            new ResourceCost(wood: 35, water: 20, citizen: 1),
            new ProductionRecipe {
                Output = new ProductionBundle { Wood = 10 }
            }) },
        // 製材所: 木材8消費 → 板材5生産
        { FacilityKind.LumberMill,  new FacilityInfo("製材所",    6, 100, 0, 0,
            new ResourceCost(wood: 70, stone: 40, cutStone: 15, citizen: 1),
            new ProductionRecipe {
                Input  = new ProductionBundle { Wood = 8 },
                Output = new ProductionBundle { Plank = 5 }
            }) },
        // 採石場: → 石材10生産
        { FacilityKind.Quarry,      new FacilityInfo("採石場",    4, 100, 0, 0,
            new ResourceCost(wood: 25, stone: 45, citizen: 1),
            new ProductionRecipe {
                Output = new ProductionBundle { Stone = 10 }
            }) },
        // 石材加工所: 石材8消費 → 切石5生産
        { FacilityKind.StoneWorks,  new FacilityInfo("石材加工所", 6, 100, 0, 0,
            new ResourceCost(wood: 40, stone: 70, plank: 15, citizen: 1),
            new ProductionRecipe {
                Input  = new ProductionBundle { Stone = 8 },
                Output = new ProductionBundle { CutStone = 5 }
            }) },
        // 鉱山: → 鉄鉱石4, 石炭3生産
        { FacilityKind.Mine,        new FacilityInfo("鉱山",     7, 100, 0, 0,
            new ResourceCost(wood: 50, stone: 90, plank: 15, cutStone: 15, citizen: 1),
            new ProductionRecipe {
                Output = new ProductionBundle { IronOre = 4, Coal = 3 }
            }) },
        // 精錬所: 鉄鉱石4, 石炭2消費 → 鉄3生産
        { FacilityKind.Smelter,     new FacilityInfo("精錬所",    7, 100, 0, 0,
            new ResourceCost(wood: 20, stone: 30, cutStone: 50, citizen: 1),
            new ProductionRecipe {
                Input  = new ProductionBundle { IronOre = 4, Coal = 2 },
                Output = new ProductionBundle { Iron = 3 }
            }) },
        // 井戸: → 水8生産
        { FacilityKind.Well,        new FacilityInfo("井戸",     3, 100, 0, 0,
            new ResourceCost(wood: 20, stone: 30),
            new ProductionRecipe {
                Output = new ProductionBundle { Water = 8 }
            }) },
        // 兵舎: AP+2ボーナス（生産なし、EconomySystem で処理）
        { FacilityKind.Barracks,    new FacilityInfo("兵舎",     10, 100, 0, 0,
            new ResourceCost(wood: 100, stone: 100, iron: 30, water: 50, plank: 40, cutStone: 40, citizen: 3)) },
        // 家: パン2消費 → 市民1生産
        { FacilityKind.House,       new FacilityInfo("家",       7, 100, 0, 0,
            new ResourceCost(wood: 60, stone: 60, water: 10, plank: 25, cutStone: 25),
            new ProductionRecipe {
                Input  = new ProductionBundle { Bread = 2 },
                Output = new ProductionBundle { Citizen = 1 }
            }) },
        // 倉庫: 生産なし（将来的に資源上限増加）
        { FacilityKind.Warehouse,   new FacilityInfo("倉庫",     7, 100, 0, 0,
            new ResourceCost(plank: 60, cutStone: 60)) },

        // ---- 壁 ----
        { FacilityKind.WoodWall,    new FacilityInfo("木壁",     2, 250, 5, 0,
            new ResourceCost(wood: 15, plank: 10)) },
        { FacilityKind.StoneWall,   new FacilityInfo("石壁",     3, 550, 12, 0,
            new ResourceCost(stone: 25, cutStone: 15)) },

        // ---- 攻撃型建築物 ----
        { FacilityKind.Mortar,      new FacilityInfo("臼砲",     10, 220, 0, 30,
            new ResourceCost(wood: 30, stone: 90, iron: 20, water: 20, plank: 20, cutStone: 40, citizen: 1)) },
        { FacilityKind.Cannon,      new FacilityInfo("大砲",     10, 240, 0, 45,
            new ResourceCost(wood: 20, stone: 60, iron: 60, water: 10, plank: 30, cutStone: 40, citizen: 1)) },
        { FacilityKind.RestraintTrap, new FacilityInfo("拘束罠",  5, 120, 0, 0,
            new ResourceCost(wood: 10, stone: 20, iron: 10, magicOre: 5, plank: 10)) },
        { FacilityKind.SpikeTrap,   new FacilityInfo("トゲ罠",   5, 120, 0, 20,
            new ResourceCost(wood: 15, stone: 10, iron: 20, plank: 15, cutStone: 10)) },
        { FacilityKind.HeroSword,   new FacilityInfo("英雄の剣",  12, 1, 0, 200,
            new ResourceCost(iron: 150, magicOre: 80, plank: 50, cutStone: 50)) },
    };

    // ---- リソース充足チェック（建築コスト用） ----
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

    // ---- リソース充足チェック（生産入力用） ----
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

    // ---- リソース消費（建築コスト用） ----
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

    // ---- 生産バンドル消費 ----
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

    // ---- 生産バンドル加算 ----
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

    // ---- 壁かどうか ----
    public static bool IsWall(FacilityKind kind)
    {
        return kind == FacilityKind.WoodWall || kind == FacilityKind.StoneWall;
    }

    // ---- FacilityKind → Kind 変換（壁のみ） ----
    public static Kind ToUnitKind(FacilityKind facility)
    {
        switch (facility)
        {
            case FacilityKind.WoodWall:  return Kind.WoodWall;
            case FacilityKind.StoneWall: return Kind.StoneWall;
            default:                     return Kind.None;
        }
    }
}
