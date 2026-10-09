using UnityEngine;

[CreateAssetMenu(menuName = "Fantasy Kingdom/駒と建物/駒の設定")]
public class UnitData : ScriptableObject
{
    [Header("駒の登録情報")]
    [Tooltip("保存・ロードに使う識別子。登録後は変更しないでください。")]
    public string definitionId;
    public string displayName;
    public string category;
    public GameObject prefab;
    public BoardActionProfile actionProfile;
    public Team defaultTeam = Team.Player;
    public bool availableToPlayer = true;
    public bool availableToEnemy = true;
    [Header("固有の能力（有効にすると既存の兵種設定より優先）")]
    public bool useAuthoredAbilities;
    public PassiveSkill authoredPassive;
    public int authoredSkillId = -1;
    public SpecialAbility authoredSpecialAbility;

    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : KindNameJP.Get(kind);
    public bool AvailableFor(Team team) => team == Team.Player ? availableToPlayer
        : team == Team.Enemy ? availableToEnemy : team == defaultTeam;

    [Header("種類")]
    public Kind kind;

    [Header("基礎ステータス（Lv1の値）")]
    public int baseHP;
    public int baseATK;
    public int baseDEF;

    [Header("成長率（Lv1基礎値から固定加算・最低+2/Lv）")]
    public float hpGrowth;
    public float atkGrowth;
    public float defGrowth;

    [Header("維持費（Lv1から発生・Lv10ごとに増加）")]
    [Tooltip("召喚体など維持費を免除する駒。王・異形の王は常に免除。通常の生産駒は有効にしません。")]
    public bool upkeepExempt;
    public int upkeepWood;
    public int upkeepStone;
    [HideInInspector] public int upkeepIron; // Legacy serialized data; standard Iron upkeep now uses GetUpkeep.
    public int upkeepMagic;
    public int upkeepWater;
    [HideInInspector] public int upkeepBread; // Legacy serialized data; standard Bread upkeep now uses GetUpkeep.

    [Header("維持費スケーリング")]
    [Tooltip("falseの場合、魔法鉱石の維持費はLv10ごとに増えない")]
    public bool upkeepMagicScales = true;

    [Header("制作コスト（木/石/鉄/魔/水/パン/市民/AP）")]
    public int costWood;
    public int costStone;
    public int costIron;
    public int costMagic;
    public int costWater;
    public int costBread;
    public int costCitizen;
    public int costAP;

    public bool IsValidForAuthoring => baseHP > 0 && baseATK >= 0 && baseDEF >= 0
        && ValidGrowth(hpGrowth) && ValidGrowth(atkGrowth) && ValidGrowth(defGrowth)
        && upkeepWood >= 0 && upkeepStone >= 0 && upkeepIron >= 0 && upkeepMagic >= 0 && upkeepWater >= 0 && upkeepBread >= 0
        && costWood >= 0 && costStone >= 0 && costIron >= 0 && costMagic >= 0 && costWater >= 0 && costBread >= 0 && costCitizen >= 0 && costAP >= 0;
    static bool ValidGrowth(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;

    // ─────────────────────────────────────────────────────────────────
    //  維持費計算（Lv1から、Lv10ごと。ゲーム本体とAI予測の共有API）
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lv1〜9=1、Lv10〜19=2、Lv20〜29=3。無効なレベルはゲームの有効範囲へ補正する。
    /// </summary>
    public static int UpkeepMultiplier(int level)
    {
        return 1 + Mathf.Clamp(level, 1, GameConstants.MaxUnitLevel) / 10;
    }

    /// <summary>
    /// 指定レベルでの維持費を ProductionBundle として返す。
    /// </summary>
    public FacilityData.ProductionBundle GetUpkeep(int level)
    {
        if (upkeepExempt || kind == Kind.King || kind == Kind.Boss) return default;
        int mult = UpkeepMultiplier(level);
        return new FacilityData.ProductionBundle
        {
            Wood     = ScaledUpkeep(upkeepWood, mult),
            Stone    = ScaledUpkeep(upkeepStone, mult),
            Iron     = mult, // Standard army maintenance: one Iron, plus one per ten levels.
            MagicOre = ScaledUpkeep(upkeepMagic, upkeepMagicScales ? mult : 1),
            Water    = ScaledUpkeep(upkeepWater, mult),
            Bread    = mult,
        };
    }

    static int ScaledUpkeep(int value, int multiplier)
        => (int)System.Math.Min(int.MaxValue, (long)Mathf.Max(0, value) * multiplier);

    // ─────────────────────────────────────────────────────────────────
    //  成長計算（GameReference準拠の線形式）
    //  staticにする理由：UnitDataインスタンスなしでUI等から呼べるようにするため
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Round once at Lv1; every subsequent level receives this same gain.</summary>
    public static int CalcGrowthPerLevel(int baseStat, float growth)
    {
        if (float.IsNaN(growth) || float.IsInfinity(growth) || growth < 0) growth = 0;
        float rawGain = baseStat * growth;
        if (rawGain >= int.MaxValue) return int.MaxValue;
        return Mathf.Max(2, Mathf.FloorToInt(rawGain + 0.5f));
    }

    public static int CalcStat(int baseStat, float growth, int level)
    {
        level = Mathf.Clamp(level, 1, GameConstants.MaxUnitLevel);
        return (int)System.Math.Min(int.MaxValue, (long)baseStat + (long)CalcGrowthPerLevel(baseStat, growth) * (level - 1));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Status への一括適用
    //  「自分のデータをStatusにどう適用するか」はUnitData自身が知っているべき
    //  ゲーム開始時・駒の生成時・レベルアップ後の3タイミングからこのメソッドを呼ぶ
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Status に Level・ATK・HP・DEF をセットする。
    /// このメソッド以外でステータスの計算式を書かない（原則4）。
    /// </summary>
    public void ApplyToStatus(Status status, int level)
    {
        level = Mathf.Clamp(level, 1, GameConstants.MaxUnitLevel);
        status.GrowthData = this;
        status.unitDefinitionId = definitionId ?? string.Empty;
        status.Level = level;
        status.ATK = CalcStat(baseATK, atkGrowth, level);
        status.HP = CalcStat(baseHP, hpGrowth, level);
        status.MaxHP = status.HP;
        status.DEF = CalcStat(baseDEF, defGrowth, level);
    }

    /// <summary>Called when spawning, never during growth or damage updates.</summary>
    public void InitializeAbilities(Status status)
    {
        if (status == null) return;
        if (useAuthoredAbilities)
        {
            status.passiveskill = authoredPassive;
            status.AssignedSkillId = SkillData.Table.ContainsKey(authoredSkillId) ? authoredSkillId : -1;
            status.specialAbility = authoredSpecialAbility;
            return;
        }
        if (status.kind == Kind.Boss) status.passiveskill = PassiveSkill.StrangeKingAura;
        SkillData.AssignFixedSkill(status);
        SpecialAbilityData.AssignRandom(status);
    }
}
