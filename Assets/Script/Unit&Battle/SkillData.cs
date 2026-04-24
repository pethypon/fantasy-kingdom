using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  スキルのターゲット種別
// =====================================================================
public enum SkillTarget
{
    Self,           // 自身
    EnemySingle,    // 敵1体
    AllySingle,     // 味方1体
    EnemyOrBuilding,// 敵1体 or 建物
    DirectionLine,  // 直線先
    DesignatedTile, // 指定マス
    SelfArea,       // 自身中心範囲
    AdjacentCenter, // 隣接中心
    LowHPEnemy,     // 低HP敵1体
    FlyingEnemy,    // 飛行/高所敵1体
    DesignatedRow   // 指定列
}

// =====================================================================
//  スキル範囲の形状
// =====================================================================
public enum SkillAreaShape
{
    Single,         // 単体
    Cross1,         // 十字1マス
    Cross2,         // 十字2マス
    Line3,          // 直線3マス
    Line4,          // 直線4マス
    Line5,          // 直線5マス
    Line7,          // 直線7マス
    Area2x2,        // 2x2
    Area3x3,        // 3x3
    Area5x5,        // 5x5
    Surround1,      // 周囲1マス
    Surround2,      // 周囲2マス
    SingleChain,    // 単体+隣接1体
    SingleDouble,   // 単体2連撃
    Landing1        // 着弾1マス
}

// =====================================================================
//  スキルデータ定義
// =====================================================================
public class SkillData
{
    public int Id;
    public string Name;
    public SkillRarity Rarity;
    public int APCost;
    public SkillTarget Target;
    public SkillAreaShape Area;
    public float Multiplier;        // スキル倍率
    public float SecondMultiplier;  // 2段目倍率（2連撃 or チェイン用）
    public int FixedHeal;           // 固定回復量
    public int FixedDamage;         // 固定ダメージ（自傷等）
    public StatusEffectType InflictDebuff;
    public BuffType GrantBuff;
    public float DebuffChance;      // 付与率（1.0 = 100%）
    public bool BuffToSelf;         // 攻撃時に自身にバフ
    public string SpecialEffect;    // 特殊効果の識別子

    // =====================================================================
    //  全スキルテーブル
    // =====================================================================
    public static readonly Dictionary<int, SkillData> Table = new Dictionary<int, SkillData>();

    // =====================================================================
    //  レアリティ別IDリスト（ランダム配布用）
    // =====================================================================
    public static readonly List<int> NormalIds     = new List<int>();
    public static readonly List<int> RareIds       = new List<int>();
    public static readonly List<int> SuperRareIds  = new List<int>();
    public static readonly List<int> LegendaryIds  = new List<int>();

    static SkillData()
    {
        InitAllSkills();
    }

    static void Register(SkillData s)
    {
        Table[s.Id] = s;
        switch (s.Rarity)
        {
            case SkillRarity.Normal:    NormalIds.Add(s.Id);    break;
            case SkillRarity.Rare:      RareIds.Add(s.Id);     break;
            case SkillRarity.SuperRare: SuperRareIds.Add(s.Id); break;
            case SkillRarity.Legendary: LegendaryIds.Add(s.Id); break;
        }
    }

    static void InitAllSkills()
    {
        // ============== ノーマル (1-20) ==============

        Register(new SkillData {
            Id = 1, Name = "パワーストライク", Rarity = SkillRarity.Normal,
            APCost = 4, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single,
            Multiplier = 1.25f
        });

        Register(new SkillData {
            Id = 2, Name = "シールドブレイク", Rarity = SkillRarity.Normal,
            APCost = 4, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single,
            Multiplier = 1.00f, InflictDebuff = StatusEffectType.ArmorBreak, DebuffChance = 1f
        });

        Register(new SkillData {
            Id = 3, Name = "ガードスタンス", Rarity = SkillRarity.Normal,
            APCost = 3, Target = SkillTarget.Self, Area = SkillAreaShape.Single,
            GrantBuff = BuffType.Defensive
        });

        Register(new SkillData {
            Id = 4, Name = "フォーカス", Rarity = SkillRarity.Normal,
            APCost = 3, Target = SkillTarget.Self, Area = SkillAreaShape.Single,
            GrantBuff = BuffType.Offensive
        });

        Register(new SkillData {
            Id = 6, Name = "ハムストリング", Rarity = SkillRarity.Normal,
            APCost = 4, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single,
            Multiplier = 0.90f, InflictDebuff = StatusEffectType.Chill, DebuffChance = 1f
        });

        Register(new SkillData {
            Id = 8, Name = "ピアシングショット", Rarity = SkillRarity.Normal,
            APCost = 6, Target = SkillTarget.DirectionLine, Area = SkillAreaShape.Line3,
            Multiplier = 0.90f
        });

        Register(new SkillData {
            Id = 10, Name = "ヒールライト", Rarity = SkillRarity.Normal,
            APCost = 4, Target = SkillTarget.AllySingle, Area = SkillAreaShape.Single,
            FixedHeal = 10
        });

        Register(new SkillData {
            Id = 13, Name = "マークショット", Rarity = SkillRarity.Normal,
            APCost = 4, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single,
            Multiplier = 0.80f, InflictDebuff = StatusEffectType.Mark, DebuffChance = 1f
        });

        Register(new SkillData {
            Id = 17, Name = "スマッシュ", Rarity = SkillRarity.Normal,
            APCost = 5, Target = SkillTarget.EnemyOrBuilding, Area = SkillAreaShape.Single,
            Multiplier = 1.15f, SpecialEffect = "SmashBuilding"
        });

        Register(new SkillData {
            Id = 18, Name = "ブラストシード", Rarity = SkillRarity.Normal,
            APCost = 6, Target = SkillTarget.DesignatedTile, Area = SkillAreaShape.Area2x2,
            Multiplier = 0.75f
        });

        Register(new SkillData {
            Id = 19, Name = "リフレクトガード", Rarity = SkillRarity.Normal,
            APCost = 4, Target = SkillTarget.Self, Area = SkillAreaShape.Single,
            GrantBuff = BuffType.Reflect
        });

        Register(new SkillData {
            Id = 20, Name = "スタンブロウ", Rarity = SkillRarity.Normal,
            APCost = 6, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single,
            Multiplier = 0.75f, InflictDebuff = StatusEffectType.Stun, DebuffChance = 0.20f
        });

        // ============== レア (21-35) ==============

        Register(new SkillData {
            Id = 21, Name = "ヘビースラッシュ", Rarity = SkillRarity.Rare,
            APCost = 6, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single,
            Multiplier = 1.50f
        });

        Register(new SkillData {
            Id = 22, Name = "ブレイクランス", Rarity = SkillRarity.Rare,
            APCost = 7, Target = SkillTarget.DirectionLine, Area = SkillAreaShape.Line4,
            Multiplier = 1.10f, InflictDebuff = StatusEffectType.ArmorBreak, DebuffChance = 1f
        });

        Register(new SkillData {
            Id = 24, Name = "ラピッドファイア", Rarity = SkillRarity.Rare,
            APCost = 7, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.SingleDouble,
            Multiplier = 0.80f, SecondMultiplier = 0.80f
        });

        Register(new SkillData {
            Id = 25, Name = "フレイムバースト", Rarity = SkillRarity.Rare,
            APCost = 8, Target = SkillTarget.DesignatedTile, Area = SkillAreaShape.Area3x3,
            Multiplier = 0.90f, InflictDebuff = StatusEffectType.Poison, DebuffChance = 0.25f,
            SpecialEffect = "FlamePoison"
        });

        Register(new SkillData {
            Id = 26, Name = "セイクリッドヒール", Rarity = SkillRarity.Rare,
            APCost = 5, Target = SkillTarget.AllySingle, Area = SkillAreaShape.Single,
            FixedHeal = 18
        });

        Register(new SkillData {
            Id = 30, Name = "チェインショット", Rarity = SkillRarity.Rare,
            APCost = 6, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.SingleChain,
            Multiplier = 1.00f, SecondMultiplier = 0.60f
        });

        Register(new SkillData {
            Id = 32, Name = "シャドウラッシュ", Rarity = SkillRarity.Rare,
            APCost = 6, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single,
            Multiplier = 1.35f, SpecialEffect = "ShadowRush"
        });

        Register(new SkillData {
            Id = 33, Name = "スカイハント", Rarity = SkillRarity.Rare,
            APCost = 6, Target = SkillTarget.FlyingEnemy, Area = SkillAreaShape.Single,
            Multiplier = 1.40f
        });

        Register(new SkillData {
            Id = 35, Name = "マナシールド", Rarity = SkillRarity.Rare,
            APCost = 4, Target = SkillTarget.Self, Area = SkillAreaShape.Single,
            GrantBuff = BuffType.Barrier
        });

        // ============== スーパーレア (36-45) ==============

        Register(new SkillData {
            Id = 36, Name = "グランドスラム", Rarity = SkillRarity.SuperRare,
            APCost = 9, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single,
            Multiplier = 1.90f
        });

        Register(new SkillData {
            Id = 38, Name = "メテオシャード", Rarity = SkillRarity.SuperRare,
            APCost = 10, Target = SkillTarget.DesignatedTile, Area = SkillAreaShape.Area3x3,
            Multiplier = 1.25f
        });

        Register(new SkillData {
            Id = 40, Name = "ブラッドサクリファイス", Rarity = SkillRarity.SuperRare,
            APCost = 6, Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single,
            Multiplier = 2.00f, SpecialEffect = "BloodSacrifice"
        });

        Register(new SkillData {
            Id = 42, Name = "フリーズバインド", Rarity = SkillRarity.SuperRare,
            APCost = 8, Target = SkillTarget.DesignatedTile, Area = SkillAreaShape.Area2x2,
            Multiplier = 0.90f, InflictDebuff = StatusEffectType.Freeze, DebuffChance = 0.35f
        });

        Register(new SkillData {
            Id = 44, Name = "デスサイト", Rarity = SkillRarity.SuperRare,
            APCost = 7, Target = SkillTarget.LowHPEnemy, Area = SkillAreaShape.Single,
            Multiplier = 1.60f, SpecialEffect = "DeathSight"
        });

        Register(new SkillData {
            Id = 45, Name = "シージブレイカー", Rarity = SkillRarity.SuperRare,
            APCost = 8, Target = SkillTarget.EnemyOrBuilding, Area = SkillAreaShape.Single,
            Multiplier = 1.35f, SpecialEffect = "SiegeBreaker"
        });

        // ============== レジェンダリー (46-50) ==============

        Register(new SkillData {
            Id = 46, Name = "ジャッジメント", Rarity = SkillRarity.Legendary,
            APCost = 11, Target = SkillTarget.DesignatedTile, Area = SkillAreaShape.Area3x3,
            Multiplier = 1.60f, InflictDebuff = StatusEffectType.Mark, DebuffChance = 1f,
            SpecialEffect = "JudgementMark"
        });

        Register(new SkillData {
            Id = 48, Name = "ワールドエッジ", Rarity = SkillRarity.Legendary,
            APCost = 12, Target = SkillTarget.DirectionLine, Area = SkillAreaShape.Line7,
            Multiplier = 1.35f
        });

        Register(new SkillData {
            Id = 50, Name = "カタストロフ", Rarity = SkillRarity.Legendary,
            APCost = 15, Target = SkillTarget.DesignatedTile, Area = SkillAreaShape.Area5x5,
            Multiplier = 1.20f, FixedDamage = 20, SpecialEffect = "Catastrophe"
        });
    }

    // =====================================================================
    //  レアリティ抽選（出現率: N50% R20% SR15% L5% + 残10%でN）
    // =====================================================================
    public static int DrawRandomSkillId()
    {
        float roll = Random.Range(0f, 1f);
        List<int> pool;

        if (roll < 0.05f)
            pool = LegendaryIds;
        else if (roll < 0.20f)
            pool = SuperRareIds;
        else if (roll < 0.40f)
            pool = RareIds;
        else
            pool = NormalIds;

        if (pool.Count == 0) pool = NormalIds;
        return pool[Random.Range(0, pool.Count)];
    }

    // =====================================================================
    //  召喚時の3択候補抽選
    //  - レアリティは従来の確率で1回決定 → その帯から重複なしで3枚抽選。
    //  - 枚数が不足する場合は隣接帯で補う（下位 → 上位の順に探索）。
    // =====================================================================
    public static List<int> DrawCandidateSkillIds(int count = 3)
    {
        float roll = Random.Range(0f, 1f);
        List<int> primary;
        if (roll < 0.05f) primary = LegendaryIds;
        else if (roll < 0.20f) primary = SuperRareIds;
        else if (roll < 0.40f) primary = RareIds;
        else primary = NormalIds;
        if (primary.Count == 0) primary = NormalIds;

        var picked = new List<int>();
        var pool = new List<int>(primary);
        while (picked.Count < count && pool.Count > 0)
        {
            int idx = Random.Range(0, pool.Count);
            picked.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        // 候補不足なら下位帯から補充
        List<List<int>> fallback = new List<List<int>> { NormalIds, RareIds, SuperRareIds, LegendaryIds };
        int fIdx = 0;
        while (picked.Count < count && fIdx < fallback.Count)
        {
            foreach (var id in fallback[fIdx])
                if (!picked.Contains(id)) { picked.Add(id); if (picked.Count >= count) break; }
            fIdx++;
        }
        return picked;
    }

    /// <summary>敵AI: 候補から最もスコアの高いスキルを選ぶ。</summary>
    public static int PickBestForAI(List<int> candidates)
    {
        int bestId = candidates[0];
        float bestScore = float.MinValue;
        foreach (var id in candidates)
        {
            if (!Table.TryGetValue(id, out var s)) continue;
            float score = s.Multiplier * 2f + s.FixedDamage * 0.5f + s.FixedHeal * 0.3f - s.APCost * 0.2f;
            if (s.InflictDebuff != StatusEffectType.None) score += 1.5f;
            if (s.GrantBuff != BuffType.None) score += 1.0f;
            score += (int)s.Rarity * 1.0f;
            if (score > bestScore) { bestScore = score; bestId = id; }
        }
        return bestId;
    }

    // =====================================================================
    //  ユニットにスキルを配布（敵AIは3択からベストを自動選択）
    //  プレイヤー向けには SkillChoiceUI.ShowOrCreate を使う OfferPlayerChoice を別途呼ぶ
    // =====================================================================
    public static void AssignRandomSkill(Status unit)
    {
        if (unit.type != Type.Unit) return;
        if (unit.kind == Kind.Crystal || unit.kind == Kind.SubCrystal) return;

        var candidates = DrawCandidateSkillIds(3);
        int picked = (unit.team == Team.Enemy || unit.team == Team.Obstacle)
            ? PickBestForAI(candidates)
            : candidates[Random.Range(0, candidates.Count)]; // 初期配置プレイヤー駒は即ランダム
        unit.AssignedSkillId = picked;
        var skill = Table[picked];
        Debug.Log($"[SkillData] {unit.team} {unit.kind} にスキル '{skill.Name}' ({skill.Rarity}) を配布");
    }

    /// <summary>
    /// プレイヤー召喚時の3択UI。ユニットに既にデフォルト割り当てがあっても上書きする。
    /// UI が出せない環境ではベストピックでフォールバック。
    /// </summary>
    public static void OfferPlayerChoice(Status unit)
    {
        if (unit == null) return;
        var candidates = DrawCandidateSkillIds(3);
        SkillChoiceUI.ShowOrCreate(candidates, picked =>
        {
            if (picked < 0) picked = PickBestForAI(candidates);
            unit.AssignedSkillId = picked;
            if (Table.TryGetValue(picked, out var s))
                Debug.Log($"[SkillData] プレイヤー選択: '{s.Name}' ({s.Rarity}) → {unit.kind}");
        });
    }

    /// <summary>全ユニットにスキルをランダム配布</summary>
    public static void AssignSkillsToAll(Transform unitParent)
    {
        if (unitParent == null) return;
        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s.type == Type.Unit && s.AssignedSkillId < 0)
                AssignRandomSkill(s);
        }
    }
}
