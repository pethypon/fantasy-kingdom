using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 建築物の制作コスト・ステータス・生産レシピ・強化データを静的に定義する。
/// GameReference 準拠。
///
/// 実装は以下の partial ファイルに分離されている:
///   - FacilityData.Resources.cs   リソース充足/消費/加算/強化チェック
///   - FacilityData.Production.cs  生産建物 (畑/パン屋/伐採所/…)
///   - FacilityData.Walls.cs       壁 / サブクリスタル
///   - FacilityData.Offensive.cs   攻撃型建築物 (大砲/罠/英雄の剣)
/// </summary>
public static partial class FacilityData
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
    //  施設カテゴリ判定
    // ==================================================================
    public static bool IsWall(FacilityKind kind)
    {
        return kind == FacilityKind.WoodWall || kind == FacilityKind.StoneWall;
    }

    public static bool IsOffensive(FacilityKind kind)
    {
        return kind == FacilityKind.Mortar
            || kind == FacilityKind.Cannon
            || kind == FacilityKind.RestraintTrap
            || kind == FacilityKind.SpikeTrap
            || kind == FacilityKind.HeroSword;
    }

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
