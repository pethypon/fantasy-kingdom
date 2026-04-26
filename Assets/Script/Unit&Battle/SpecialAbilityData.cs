using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Special Ability のデータ定義とランダム配布ロジック。
/// 1駒につき1つ、重複なし、AP消費なしの常時発動パッシブ。
/// </summary>
public class SpecialAbilityInfo
{
    public SpecialAbility Id;
    public string NameJP;
    public SpecialAbilityRarity Rarity;
    public string Description;

    public SpecialAbilityInfo(SpecialAbility id, string nameJP, SpecialAbilityRarity rarity, string desc)
    {
        Id = id;
        NameJP = nameJP;
        Rarity = rarity;
        Description = desc;
    }
}

public static class SpecialAbilityData
{
    // =====================================================================
    //  全 Special Ability テーブル
    // =====================================================================
    public static readonly Dictionary<SpecialAbility, SpecialAbilityInfo> Table
        = new Dictionary<SpecialAbility, SpecialAbilityInfo>();

    // レアリティ別リスト（ランダム配布用）
    public static readonly List<SpecialAbility> NormalList     = new List<SpecialAbility>();
    public static readonly List<SpecialAbility> RareList       = new List<SpecialAbility>();
    public static readonly List<SpecialAbility> SuperRareList  = new List<SpecialAbility>();
    public static readonly List<SpecialAbility> LegendaryList  = new List<SpecialAbility>();

    static SpecialAbilityData()
    {
        InitAll();
    }

    static void Reg(SpecialAbilityInfo info)
    {
        Table[info.Id] = info;
        switch (info.Rarity)
        {
            case SpecialAbilityRarity.Normal:    NormalList.Add(info.Id);    break;
            case SpecialAbilityRarity.Rare:      RareList.Add(info.Id);     break;
            case SpecialAbilityRarity.SuperRare: SuperRareList.Add(info.Id); break;
            case SpecialAbilityRarity.Legendary: LegendaryList.Add(info.Id); break;
        }
    }

    static void InitAll()
    {
        // ============== ノーマル (8) ==============
        Reg(new SpecialAbilityInfo(SpecialAbility.SwiftPosture,     "迅速体勢", SpecialAbilityRarity.Normal,
            "ターン開始時、未移動ならそのターン最初の移動コスト-1"));
        Reg(new SpecialAbilityInfo(SpecialAbility.InterceptPosture, "迎撃姿勢", SpecialAbilityRarity.Normal,
            "HP100%の時、そのターン最初に受けるダメージ-10%"));
        Reg(new SpecialAbilityInfo(SpecialAbility.PierceEnhance,    "刺突強化", SpecialAbilityRarity.Normal,
            "単体攻撃時のみ与ダメージ+10%"));
        Reg(new SpecialAbilityInfo(SpecialAbility.WatchEye,         "監視眼", SpecialAbilityRarity.Normal,
            "視界+1"));
        Reg(new SpecialAbilityInfo(SpecialAbility.Tenacity,         "粘り腰", SpecialAbilityRarity.Normal,
            "HP50%以下の時、被ダメージ-10%"));
        Reg(new SpecialAbilityInfo(SpecialAbility.PoisonCoat,       "毒塗り", SpecialAbilityRarity.Normal,
            "単体攻撃命中時10%で毒付与"));
        Reg(new SpecialAbilityInfo(SpecialAbility.FrostBlade,       "冷気刃", SpecialAbilityRarity.Normal,
            "攻撃命中時10%で冷気付与"));
        Reg(new SpecialAbilityInfo(SpecialAbility.Efficiency,       "省力化", SpecialAbilityRarity.Normal,
            "最初に使うスキルのAP消費-1（最低1）"));

        // ============== レア (7) ==============
        Reg(new SpecialAbilityInfo(SpecialAbility.Desperation,      "背水", SpecialAbilityRarity.Rare,
            "HP30%以下でATK+20%"));
        Reg(new SpecialAbilityInfo(SpecialAbility.IronWalkDefense,  "鉄壁歩法", SpecialAbilityRarity.Rare,
            "移動後、そのターン被ダメージ-15%"));
        Reg(new SpecialAbilityInfo(SpecialAbility.HeightAdapt,      "高所順応", SpecialAbilityRarity.Rare,
            "自分が相手より高い位置にいる時、与ダメージ+15%"));
        Reg(new SpecialAbilityInfo(SpecialAbility.MagicResist,      "耐魔皮膜", SpecialAbilityRarity.Rare,
            "魔法/遠距離属性から受けるダメージ-15%"));
        Reg(new SpecialAbilityInfo(SpecialAbility.Breach,           "破勢", SpecialAbilityRarity.Rare,
            "攻撃命中時15%で破甲付与"));
        Reg(new SpecialAbilityInfo(SpecialAbility.VenomBlade,       "猛毒刃", SpecialAbilityRarity.Rare,
            "単体攻撃命中時15%で毒付与"));
        Reg(new SpecialAbilityInfo(SpecialAbility.SurvivalInstinct, "生還本能", SpecialAbilityRarity.Rare,
            "1戦闘に1回だけ、致死ダメージをHP1で耐える"));

        // ============== スーパーレア (3) ==============
        Reg(new SpecialAbilityInfo(SpecialAbility.ShadowCross,       "影渡り", SpecialAbilityRarity.SuperRare,
            "視界外から攻撃した時のダメージ倍率をさらに+0.25"));
        Reg(new SpecialAbilityInfo(SpecialAbility.GuardianZone,      "守護圏", SpecialAbilityRarity.SuperRare,
            "周囲1マスの味方が受けるダメージ-10%"));
        Reg(new SpecialAbilityInfo(SpecialAbility.SniperCorrection,  "狙撃補正", SpecialAbilityRarity.SuperRare,
            "3マス以上離れた敵への与ダメージ+20%"));

        // ============== レジェンダリー (2) ==============
        Reg(new SpecialAbilityInfo(SpecialAbility.BattlefieldDomination, "戦場支配", SpecialAbilityRarity.Legendary,
            "自身の視界内にいる敵全員に被ダメージ+10%"));
        Reg(new SpecialAbilityInfo(SpecialAbility.IndomitableWill,  "不滅の意志", SpecialAbilityRarity.Legendary,
            "毎ターン最初に受ける状態異常を無効化し、さらに被ダメージ-10%"));
    }

    // =====================================================================
    //  レアリティ抽選（出現率: N50% R20% SR15% L5% + 残10%でN）
    // =====================================================================
    public static SpecialAbility DrawRandom()
    {
        float roll = Random.Range(0f, 1f);
        List<SpecialAbility> pool;

        if (roll < 0.05f)
            pool = LegendaryList;
        else if (roll < 0.20f)
            pool = SuperRareList;
        else if (roll < 0.40f)
            pool = RareList;
        else
            pool = NormalList;

        if (pool.Count == 0) pool = NormalList;
        if (pool.Count == 0) return SpecialAbility.None;
        return pool[Random.Range(0, pool.Count)];
    }

    // =====================================================================
    //  ユニットに Special Ability をランダム配布（同名重複なしは呼び出し元で管理）
    // =====================================================================
    public static void AssignRandom(Status unit)
    {
        if (unit.type != Type.Unit) return;
        if (unit.kind == Kind.Crystal || unit.kind == Kind.SubCrystal) return;

        unit.specialAbility = DrawRandom();

        if (Table.TryGetValue(unit.specialAbility, out var info))
        {
            Debug.Log($"[SpecialAbility] {unit.team} {unit.kind} に '{info.NameJP}' ({info.Rarity}) を配布");
        }
    }

    // =====================================================================
    //  全ユニットに重複なしで配布
    // =====================================================================
    public static void AssignToAll(Transform unitParent)
    {
        if (unitParent == null) return;

        var assigned = new HashSet<SpecialAbility>();

        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s.type != Type.Unit) continue;
            if (s.kind == Kind.Crystal || s.kind == Kind.SubCrystal) continue;
            if (s.specialAbility != SpecialAbility.None) continue;

            // 重複なし: 最大100回試行
            SpecialAbility sa = SpecialAbility.None;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                sa = DrawRandom();
                if (!assigned.Contains(sa)) break;
            }

            assigned.Add(sa);
            s.specialAbility = sa;

            if (Table.TryGetValue(sa, out var info))
                Debug.Log($"[SpecialAbility] {s.team} {s.kind} に '{info.NameJP}' ({info.Rarity}) を配布");
        }
    }
}
