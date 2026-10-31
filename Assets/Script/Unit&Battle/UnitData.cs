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
    [Header("駒固有能力（兵種に固定するスキル・アビリティ）")]
    public SpecialSkillDefinition specialSkill;
    [Tooltip("攻撃名ではなく属性で地形相性を判定します。未指定は属性なし。")]
    public CombatAttackTags attackTags;

    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : KindNameJP.Get(kind);
    public bool AvailableFor(Team team) => kind != Kind.Engineer && (team == Team.Player ? availableToPlayer
        : team == Team.Enemy ? availableToEnemy : team == defaultTeam);

    [Header("種類")]
    public Kind kind;

    [Header("基礎ステータス（固定倍率適用前）")]
    public int baseHP;
    public int baseATK;
    [HideInInspector] public int baseDEF; // Historical serialized data; unused by R1 combat.
    [Min(-1), InspectorName("初期バリア（-1は職種設定）")] public int baseBarrier = -1;

    [HideInInspector] public float hpGrowth;
    [HideInInspector] public float atkGrowth;
    [HideInInspector] public float defGrowth;

    [Header("基本維持費（実際の毎ターン支払額はこの5倍）")]
    [Tooltip("召喚体など維持費を免除する駒。王・異形の王は常に免除。通常の生産駒は有効にしません。")]
    public bool upkeepExempt;
    [Tooltip("有効時は鉄・パンもこの駒の設定値を使います。無効時は従来の共通維持費を使用。")]
    public bool useAuthoredUpkeep;
    public int upkeepWood;
    public int upkeepStone;
    [HideInInspector] public int upkeepIron; // Legacy serialized data; standard Iron upkeep now uses GetUpkeep.
    public int upkeepMagic;
    public int upkeepWater;
    [HideInInspector] public int upkeepBread; // Legacy serialized data; standard Bread upkeep now uses GetUpkeep.

    [HideInInspector] public bool upkeepMagicScales = true; // Legacy asset compatibility only.

    [Header("制作コスト（木/石/鉄/魔/水/パン/市民/AP）")]
    public int costWood;
    public int costStone;
    public int costIron;
    public int costMagic;
    public int costWater;
    public int costBread;
    public int costCitizen;
    public int costAP;

    public bool IsValidForAuthoring => kind != Kind.Engineer && baseHP > 0 && baseATK >= 0 && baseDEF >= 0 && baseBarrier >= -1
        && upkeepWood >= 0 && upkeepStone >= 0 && upkeepIron >= 0 && upkeepMagic >= 0 && upkeepWater >= 0 && upkeepBread >= 0
        && costWood >= 0 && costStone >= 0 && costIron >= 0 && costMagic >= 0 && costWater >= 0 && costBread >= 0 && costCitizen >= 0 && costAP >= 0;

    // ─────────────────────────────────────────────────────────────────
    //  固定維持費（旧基礎値+追加維持費の合計を5倍。本体・UI・AIの共有API）
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Legacy caller compatibility. Unit levels never affect upkeep.
    /// </summary>
    public static int UpkeepMultiplier(int level) => FixedUnitRules.UpkeepFactor;

    /// <summary>
    /// Legacy caller compatibility. Returns the same fixed upkeep for every argument.
    /// </summary>
    public FacilityData.ProductionBundle GetUpkeep(int level) => GetFixedUpkeep();
    public FacilityData.ProductionBundle GetFixedUpkeep()
    {
        if (upkeepExempt || kind == Kind.King || kind == Kind.Boss) return default;
        var extra = GameAuthoringRules.Active?.unitExtraUpkeep ?? default;
        return new FacilityData.ProductionBundle
        {
            Wood     = FixedUnitRules.MultiplyUpkeep(upkeepWood, extra.Wood),
            Stone    = FixedUnitRules.MultiplyUpkeep(upkeepStone, extra.Stone),
            Iron     = FixedUnitRules.MultiplyUpkeep(useAuthoredUpkeep ? upkeepIron : 1, extra.Iron),
            MagicOre = FixedUnitRules.MultiplyUpkeep(upkeepMagic, extra.MagicOre),
            Wheat    = FixedUnitRules.MultiplyUpkeep(extra.Wheat),
            Citizen  = FixedUnitRules.MultiplyUpkeep(extra.Citizen),
            Water    = FixedUnitRules.MultiplyUpkeep(upkeepWater, extra.Water),
            Bread    = FixedUnitRules.MultiplyUpkeep(useAuthoredUpkeep ? upkeepBread : 1, extra.Bread),
        };
    }

    // ─────────────────────────────────────────────────────────────────
    //  旧呼び出し互換（成長は無効。旧保存の読取計算はFixedUnitRulesのみ）
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Legacy API: no level growth is applied.</summary>
    public static int CalcGrowthPerLevel(int baseStat, float growth)
        => 0; // Inert compatibility API; legacy save interpretation lives in FixedUnitRules.

    public static int CalcStat(int baseStat, float growth, int level)
        => FixedUnitRules.ScaleBaseStat(baseStat);

    public int FixedMaxHP => Mathf.Max(1, FixedUnitRules.BaseStat(kind, baseHP));
    public int FixedATK => FixedUnitRules.BaseStat(kind, baseATK);
    public int FixedDEF => FixedUnitRules.BaseStat(kind, baseDEF);

    // ─────────────────────────────────────────────────────────────────
    //  Status への一括適用
    //  「自分のデータをStatusにどう適用するか」はUnitData自身が知っているべき
    //  ゲーム開始時・新規生成時に定義の生値から固定能力を適用する
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Status に Level・ATK・HP・DEF をセットする。
    /// このメソッド以外でステータスの計算式を書かない（原則4）。
    /// </summary>
    public void ApplyToStatus(Status status, int level)
    {
        status.GrowthData = this;
        status.unitDefinitionId = definitionId ?? string.Empty;
        status.Level = 1;
        status.Experience = 0;
        status.FixedStatsMigrationDeferred = false;
        status.LegacyUnitStats = null;
        status.ATK = FixedATK;
        status.HP = FixedMaxHP;
        status.MaxHP = status.HP;
        status.ResetBarrier();
        status.DEF = FixedDEF;
        StatusBalanceRefresh.RecordDefinitionBaseline(status);
    }

    /// <summary>Called when spawning, never during growth or damage updates.</summary>
    public void InitializeAbilities(Status status)
    {
        if (status == null) return;
        status.Special = new SpecialState();
        status.NaturalRecovery = new NaturalRecoveryState();
        status.ResetBarrier();
        SpecialSkillRuntime.InvalidateRules();
        ApplyAbilityIdentity(status);
    }
    /// <summary>Fixed definition wins over historical random assignments, without resetting durable state.</summary>
    public void ApplyAbilityIdentity(Status status)
    {
        if (status == null) return;
        if (specialSkill != null && specialSkill.ability != UnitSpecialSkill.None)
        {
            status.AssignedSkillId = -1; status.specialAbility = SpecialAbility.None;
            status.passiveskill = PassiveSkill.None; return;
        }
        if (useAuthoredAbilities)
        {
            status.passiveskill = authoredPassive;
            status.AssignedSkillId = SkillData.Table.ContainsKey(authoredSkillId) ? authoredSkillId : -1;
            status.specialAbility = authoredSpecialAbility;
            return;
        }
        if (status.kind == Kind.Boss) status.passiveskill = PassiveSkill.StrangeKingAura;
        SkillData.AssignFixedSkill(status);
        status.specialAbility = SpecialAbility.None;
    }
    void OnValidate() => GameContentRevision.NotifyChanged();
}
