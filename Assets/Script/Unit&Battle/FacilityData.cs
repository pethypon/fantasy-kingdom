using System.Collections.Generic;

/// <summary>
/// 建築物の制作コスト・ステータスを静的に定義する。
/// Phase1: Lv1 の制作コストとステータスのみ。
/// </summary>
public static class FacilityData
{
    // ---- リソースコスト ----
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

    // ---- 建築物情報 ----
    public struct FacilityInfo
    {
        public int APCost;
        public int HP;
        public int DEF;
        public int ATK;
        public ResourceCost BuildCost;
        public string DisplayName;

        public FacilityInfo(string displayName, int ap, int hp, int def, int atk, ResourceCost cost)
        {
            DisplayName = displayName;
            APCost = ap; HP = hp; DEF = def; ATK = atk;
            BuildCost = cost;
        }
    }

    // ---- 全建築物テーブル ----
    public static readonly Dictionary<FacilityKind, FacilityInfo> Table =
        new Dictionary<FacilityKind, FacilityInfo>
    {
        // ---- 生産建物 ----
        { FacilityKind.Field,       new FacilityInfo("畑",       3, 100, 0, 0,
            new ResourceCost(wood: 20, water: 10, citizen: 1)) },
        { FacilityKind.Bakery,      new FacilityInfo("パン屋",    5, 100, 0, 0,
            new ResourceCost(wood: 40, stone: 25, water: 5, citizen: 1)) },
        { FacilityKind.LoggingCamp, new FacilityInfo("伐採所",    4, 100, 0, 0,
            new ResourceCost(wood: 35, water: 20, citizen: 1)) },
        { FacilityKind.LumberMill,  new FacilityInfo("製材所",    6, 100, 0, 0,
            new ResourceCost(wood: 70, stone: 40, cutStone: 15, citizen: 1)) },
        { FacilityKind.Quarry,      new FacilityInfo("採石場",    4, 100, 0, 0,
            new ResourceCost(wood: 25, stone: 45, citizen: 1)) },
        { FacilityKind.StoneWorks,  new FacilityInfo("石材加工所", 6, 100, 0, 0,
            new ResourceCost(wood: 40, stone: 70, plank: 15, citizen: 1)) },
        { FacilityKind.Mine,        new FacilityInfo("鉱山",     7, 100, 0, 0,
            new ResourceCost(wood: 50, stone: 90, plank: 15, cutStone: 15, citizen: 1)) },
        { FacilityKind.Smelter,     new FacilityInfo("精錬所",    7, 100, 0, 0,
            new ResourceCost(wood: 20, stone: 30, cutStone: 50, citizen: 1)) },
        { FacilityKind.Well,        new FacilityInfo("井戸",     3, 100, 0, 0,
            new ResourceCost(wood: 20, stone: 30)) },
        { FacilityKind.Barracks,    new FacilityInfo("兵舎",     10, 100, 0, 0,
            new ResourceCost(wood: 100, stone: 100, iron: 30, water: 50, plank: 40, cutStone: 40, citizen: 3)) },
        { FacilityKind.House,       new FacilityInfo("家",       7, 100, 0, 0,
            new ResourceCost(wood: 60, stone: 60, water: 10, plank: 25, cutStone: 25)) },
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

    // ---- リソース充足チェック ----
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

    // ---- リソース消費 ----
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
