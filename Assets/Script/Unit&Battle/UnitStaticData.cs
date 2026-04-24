using System.Collections.Generic;

/// <summary>
/// 全ユニットの基礎ステータス・制作コスト・維持費・成長率を静的に定義する。
/// FacilityData と同じパターン。Inspector 設定不要。
/// </summary>
public static class UnitStaticData
{
    // ==================================================================
    //  ユニット定義構造体
    // ==================================================================
    public struct UnitInfo
    {
        // 基礎ステータス（Lv1）
        public int BaseATK;
        public int BaseHP;
        public int BaseDEF;

        // 成長率（0.0〜1.0 / Lv）
        public float AtkGrowth;
        public float HpGrowth;
        public float DefGrowth;

        // 制作コスト（木/石/鉄/魔法鉱石/水/パン/市民/AP）
        public int CostWood;
        public int CostStone;
        public int CostIron;
        public int CostMagicOre;
        public int CostWater;
        public int CostBread;
        public int CostCitizen;
        public int CostAP;

        // 維持費（Lv5まで無料。Lv6〜発生。Lv15ごとに全項目+1）
        public int UpkeepWood;
        public int UpkeepStone;
        public int UpkeepIron;
        public int UpkeepMagicOre;
        public int UpkeepWater;
        public int UpkeepBread;

        // 維持費でLv15ごとに魔法鉱石が増えるか（false = 魔法鉱石は固定）
        public bool UpkeepMagicScales;

        // 表示名
        public string DisplayName;
    }

    // ==================================================================
    //  全ユニットテーブル
    // ==================================================================
    public static readonly Dictionary<Kind, UnitInfo> Table;

    static UnitStaticData()
    {
        Table = new Dictionary<Kind, UnitInfo>();

        // ---- キング（King） ----
        // 維持費なし、制作コストなし
        Table[Kind.King] = new UnitInfo
        {
            DisplayName = "キング",
            BaseATK = 20, BaseHP = 800, BaseDEF = 16,
            AtkGrowth = 0.25f, HpGrowth = 0.05f, DefGrowth = 0.20f,
        };

        // ---- ナイト（Knight） ----
        // 視界内にいる駒の攻撃を20%軽減、視界外からの攻撃は10%上昇
        Table[Kind.Knight] = new UnitInfo
        {
            DisplayName = "ナイト",
            BaseATK = 10, BaseHP = 32, BaseDEF = 13,
            AtkGrowth = 0.10f, HpGrowth = 0.20f, DefGrowth = 0.20f,
            CostWood = 3, CostIron = 10, CostBread = 5, CostCitizen = 1, CostAP = 3,
            UpkeepIron = 1, UpkeepBread = 1,
            UpkeepMagicScales = true,
        };

        // ---- アーチャー（Archer） ----
        // 距離ダメージボーナス: 1マス+0.25倍, 2マス+0.5倍, 3マス+0.75倍(最大)
        // 飛行ユニットに対して1.25倍
        Table[Kind.Archer] = new UnitInfo
        {
            DisplayName = "アーチャー",
            BaseATK = 15, BaseHP = 24, BaseDEF = 10,
            AtkGrowth = 0.20f, HpGrowth = 0.20f, DefGrowth = 0.10f,
            CostWood = 15, CostStone = 15, CostIron = 3, CostBread = 5, CostCitizen = 1, CostAP = 3,
            UpkeepStone = 1, UpkeepIron = 1, UpkeepBread = 1,
            UpkeepMagicScales = true,
        };

        // ---- マジシャン（Magic） ----
        // 建物に1.15倍、距離ダメージボーナス: 1マス+0.25倍, 2マス+0.5倍, 3マス+0.75倍(最大)
        Table[Kind.Magic] = new UnitInfo
        {
            DisplayName = "マジシャン",
            BaseATK = 11, BaseHP = 22, BaseDEF = 10,
            AtkGrowth = 0.25f, HpGrowth = 0.15f, DefGrowth = 0.10f,
            CostIron = 3, CostMagicOre = 10, CostWater = 20, CostBread = 8, CostCitizen = 1, CostAP = 4,
            UpkeepIron = 1, UpkeepMagicOre = 1, UpkeepBread = 1,
            UpkeepMagicScales = true,
        };

        // ---- アサシン（Assassin） ----
        // 段差のAPコスト無視、視界外から攻撃時1.25倍
        Table[Kind.Assassin] = new UnitInfo
        {
            DisplayName = "アサシン",
            BaseATK = 15, BaseHP = 16, BaseDEF = 10,
            AtkGrowth = 0.30f, HpGrowth = 0.10f, DefGrowth = 0.10f,
            CostIron = 15, CostMagicOre = 5, CostBread = 10, CostCitizen = 1, CostAP = 6,
            UpkeepMagicOre = 2, UpkeepBread = 2,
            UpkeepMagicScales = false, // 魔法鉱石は増えない
        };

        // ---- 斥候（Scout） ----
        // 視界が広い
        Table[Kind.Scout] = new UnitInfo
        {
            DisplayName = "斥候",
            BaseATK = 6, BaseHP = 20, BaseDEF = 9,
            AtkGrowth = 0.15f, HpGrowth = 0.25f, DefGrowth = 0.10f,
            CostWood = 20, CostWater = 30, CostBread = 8, CostCitizen = 1, CostAP = 4,
            UpkeepWood = 1, UpkeepStone = 1, UpkeepIron = 1, UpkeepBread = 1,
            UpkeepMagicScales = true,
        };

        // ---- 僧侶（Priest） ----
        // 隣接マスの自陣駒HPを最大HPの5%回復
        Table[Kind.Priest] = new UnitInfo
        {
            DisplayName = "僧侶",
            BaseATK = 3, BaseHP = 28, BaseDEF = 11,
            AtkGrowth = 0.20f, HpGrowth = 0.20f, DefGrowth = 0.10f,
            CostIron = 5, CostMagicOre = 20, CostWater = 50, CostBread = 10, CostCitizen = 1, CostAP = 6,
            UpkeepIron = 1, UpkeepMagicOre = 2, UpkeepBread = 2,
            UpkeepMagicScales = false, // 魔法鉱石は増えない
        };

        // ---- ガーディアン（Guardian） ----
        // 建物に対して2倍
        Table[Kind.Guardian] = new UnitInfo
        {
            DisplayName = "ガーディアン",
            BaseATK = 14, BaseHP = 42, BaseDEF = 20,
            AtkGrowth = 0.05f, HpGrowth = 0.35f, DefGrowth = 0.10f,
            CostStone = 50, CostMagicOre = 30, CostWater = 50, CostAP = 10,
            UpkeepStone = 3, UpkeepMagicOre = 1,
            UpkeepMagicScales = false, // Lv15ごとに石+1のみ（魔法鉱石は固定）
        };

        // ---- 28式クロスボウ（Crossbow） ----
        // 命中時10%でスタン（1ターン行動不可）
        Table[Kind.Crossbow] = new UnitInfo
        {
            DisplayName = "28式クロスボウ",
            BaseATK = 13, BaseHP = 17, BaseDEF = 16,
            AtkGrowth = 0.20f, HpGrowth = 0.15f, DefGrowth = 0.15f,
            CostWood = 60, CostStone = 40, CostIron = 30, CostBread = 10, CostCitizen = 1, CostAP = 8,
            UpkeepWood = 3, UpkeepIron = 3, UpkeepBread = 3,
            UpkeepMagicScales = true,
        };

        // ---- マジックスナイパー（Magicsniper） ----
        // 攻撃時に自身の最大HPの20%ダメージ、敵にマーキング（ダメージ10%UP）1ターン付与
        Table[Kind.Magicsniper] = new UnitInfo
        {
            DisplayName = "マジックスナイパー",
            BaseATK = 20, BaseHP = 14, BaseDEF = 10,
            AtkGrowth = 0.30f, HpGrowth = 0.10f, DefGrowth = 0.05f,
            CostIron = 20, CostMagicOre = 50, CostWater = 60, CostBread = 10, CostCitizen = 1, CostAP = 10,
            UpkeepIron = 1, UpkeepMagicOre = 3, UpkeepBread = 3,
            UpkeepMagicScales = false, // 魔法鉱石は増えない
        };

        // ---- ボンバー（Bomber） ----
        // 3x3 範囲攻撃
        Table[Kind.Bomber] = new UnitInfo
        {
            DisplayName = "ボンバー",
            BaseATK = 12, BaseHP = 20, BaseDEF = 11,
            AtkGrowth = 0.10f, HpGrowth = 0.25f, DefGrowth = 0.15f,
            CostStone = 20, CostIron = 30, CostWater = 50, CostBread = 10, CostCitizen = 1, CostAP = 8,
            UpkeepIron = 3, UpkeepBread = 3,
            UpkeepMagicScales = true,
        };

        // ---- BOSS（異形の王／指揮官） ----
        // キングと同等の初期ステータスだが、レベルアップ時の成長率がキングより高い。
        // クリスタル級の脅威となる指揮官ユニット（特殊パッシブ/スキルは別途データで付与）。
        Table[Kind.Boss] = new UnitInfo
        {
            DisplayName = "異形の王",
            BaseATK = 20, BaseHP = 800, BaseDEF = 16,
            AtkGrowth = 0.35f, HpGrowth = 0.10f, DefGrowth = 0.30f,
            CostIron = 30, CostMagicOre = 20, CostBread = 15, CostCitizen = 1, CostAP = 10,
            UpkeepIron = 3, UpkeepMagicOre = 2, UpkeepBread = 3,
            UpkeepMagicScales = true,
        };
    }

    // ==================================================================
    //  UnitData への適用
    // ==================================================================

    /// <summary>
    /// UnitStaticData のデータを UnitData ScriptableObject に書き込む。
    /// Inspector 未設定時のフォールバックとして使用。
    /// </summary>
    public static void ApplyTo(UnitData target, Kind kind)
    {
        if (!Table.TryGetValue(kind, out var info)) return;

        target.kind = kind;
        target.baseATK = info.BaseATK;
        target.baseHP = info.BaseHP;
        target.baseDEF = info.BaseDEF;
        target.atkGrowth = info.AtkGrowth;
        target.hpGrowth = info.HpGrowth;
        target.defGrowth = info.DefGrowth;

        target.costWood = info.CostWood;
        target.costStone = info.CostStone;
        target.costIron = info.CostIron;
        target.costMagic = info.CostMagicOre;
        target.costWater = info.CostWater;
        target.costBread = info.CostBread;
        target.costCitizen = info.CostCitizen;
        target.costAP = info.CostAP;

        target.upkeepWood = info.UpkeepWood;
        target.upkeepStone = info.UpkeepStone;
        target.upkeepIron = info.UpkeepIron;
        target.upkeepMagic = info.UpkeepMagicOre;
        target.upkeepWater = info.UpkeepWater;
        target.upkeepBread = info.UpkeepBread;
        target.upkeepMagicScales = info.UpkeepMagicScales;
    }

    // ==================================================================
    //  UnitData を生成（ランタイム用）
    // ==================================================================

    /// <summary>
    /// 指定 Kind の UnitData インスタンスをランタイム生成して返す。
    /// </summary>
    public static UnitData CreateUnitData(Kind kind)
    {
        var data = UnityEngine.ScriptableObject.CreateInstance<UnitData>();
        ApplyTo(data, kind);
        return data;
    }
}
