using System.Collections.Generic;
using UnityEngine;

public enum Team
{
    Player,
    Enemy,
    Obstacle,
    None
}

public enum Kind
{
    Crystal,
    King,
    Knight,
    Archer,
    Magic,
    Assassin,
    Scout,
    Priest,
    Guardian,
    Crossbow,
    Magicsniper,
    Bomber,
    Boss,       // BOSS駒（大きい性格を持つ指揮官）
    SubCrystal, // サブクリスタル（領地拡張用建築物）
    WoodWall,   // フェーズ6で使用
    StoneWall,  // フェーズ6で使用
    None
}

public enum Type
{
    Unit,
    Building,
    Wall,           // フェーズ6で使用
    MovePoint,
    AttackPoint
}

public enum State
{
    Normal
}

public enum Direction
{
    N,
    S
}

public enum Skill
{
    None,
    // ノーマル (1-20)
    PowerStrike,        // パワーストライク
    ShieldBreak,        // シールドブレイク
    GuardStance,        // ガードスタンス
    Focus,              // フォーカス
    QuickStep,          // クイックステップ
    Hamstring,          // ハムストリング
    WideSwing,          // ワイドスイング
    PiercingShot,       // ピアシングショット
    ArcShot,            // アークショット
    HealLight,          // ヒールライト
    BlessUp,            // ブレスアップ
    Protect,            // プロテクト
    MarkShot,           // マークショット
    Suppression,        // サプレッション
    Tracking,           // トラッキング
    Recover,            // リカバー
    Smash,              // スマッシュ
    BlastSeed,          // ブラストシード
    ReflectGuard,       // リフレクトガード
    StunBlow,           // スタンブロウ
    // レア (21-35)
    HeavySlash,         // ヘビースラッシュ
    BreakLance,         // ブレイクランス
    SmokeEdge,          // スモークエッジ
    RapidFire,          // ラピッドファイア
    FlameBurst,         // フレイムバースト
    SacredHeal,         // セイクリッドヒール
    FieldAid,           // フィールドエイド
    WarCry,             // ウォークライ
    IronWall,           // アイアンウォール
    ChainShot,          // チェインショット
    SilenceMark,        // サイレンスマーク
    ShadowRush,         // シャドウラッシュ
    SkyHunt,            // スカイハント
    GroundBreak,        // グラウンドブレイク
    ManaShield,         // マナシールド
    // スーパーレア (36-45)
    GrandSlam,          // グランドスラム
    PenetrateRain,      // ペネトレイトレイン
    MeteorShard,        // メテオシャード
    DivineCircle,       // ディバインサークル
    BloodSacrifice,     // ブラッドサクリファイス
    PhantomDrive,       // ファントムドライブ
    FreezeBind,         // フリーズバインド
    BastionCall,        // バスティオンコール
    DeathSight,         // デスサイト
    SiegeBreaker,       // シージブレイカー
    // レジェンダリー (46-50)
    Judgement,          // ジャッジメント
    PhoenixHeal,        // フェニックスヒール
    WorldEdge,          // ワールドエッジ
    LastSignal,         // ラストシグナル
    Catastrophe         // カタストロフ
}

public enum PassiveSkill
{
    None,
    Impregnable,
    HunterEyes,
    Destroyer,
    Assassination,
    Sniper,
    /// <summary>
    /// 異形の王専用: 被ダメージ-40% + 受けたダメージの20%をHP回復（吸血）+
    /// 自身HP50%以下時にATK+25%。クリスタル級の耐久と脅威度を実現する。
    /// </summary>
    StrangeKingAura,
}

/// <summary>
/// 強敵（ワイルドボス）のアーキタイプ。縄張り内行動・固有AIのディスパッチに使う。
/// </summary>
public enum WildBossArchetype
{
    None,
    GhostKing,      // テレポート+視界外攻撃（2ターン毎）
    Dragon,         // 炎ブレス（4ターン毎）+バフ/前方攻撃
    RebelKnight,    // 反撃付与（2ターン毎）+親衛騎士召喚
    ThunderMagus,   // 雷クリスタル設置+5ターン毎に一斉起爆
}

// =====================================================================
//  状態異常（デバフ）
// =====================================================================
public enum StatusEffectType
{
    None,
    Stun,           // スタン: 行動不可 (1T)
    Mark,           // マーク: 被ダメ+10% (1T)
    ArmorBreak,     // 破甲: DEF-15% (1T)
    Weaken,         // 弱体: ATK-15% (1T)
    Poison,         // 毒: ターン終了時固定8ダメ + 回復-40% (2T) ※Bleed/Curse統合
    Chill,          // 冷気: 移動AP+2 + ATK-10% (1T) ※Slow統合
    Freeze,         // 凍結: 移動不可 + 被ダメ+10% (1T) ※Bind統合
    Seal,           // 封技: スキル倍率-20% (1T)
    Blind,          // 視界封じ: 視界を前方1マスに制限 (1T) ※重複不可
    NarrowVision    // 視界縮小: 視界を前方1マスに制限 (1T、強敵専用) ※重複不可
}

// =====================================================================
//  バフ
// =====================================================================
public enum BuffType
{
    None,
    Offensive,      // 攻勢: ATK+15% (1T)
    Defensive,      // 守勢: DEF+20% (1T)
    Haste,          // 加速: AP+2 (即時)
    Insight,        // 看破: 視界+1 (1T)
    Barrier,        // 障壁: 次ダメ-30% (1T)
    Reflect         // 反射: 被弾時固定5-10ダメ返し (1T)
}

// =====================================================================
//  アクティブエフェクト（状態異常・バフの実体）
// =====================================================================
[System.Serializable]
public class ActiveEffect
{
    public StatusEffectType debuffType;
    public BuffType buffType;
    public int remainingTurns;

    public bool IsDebuff => debuffType != StatusEffectType.None;
    public bool IsBuff   => buffType   != BuffType.None;

    public ActiveEffect(StatusEffectType debuff, int turns)
    {
        debuffType = debuff;
        buffType = BuffType.None;
        remainingTurns = turns;
    }

    public ActiveEffect(BuffType buff, int turns)
    {
        debuffType = StatusEffectType.None;
        buffType = buff;
        remainingTurns = turns;
    }
}

// =====================================================================
//  Special Ability（ランダム配布パッシブ）
// =====================================================================
public enum SpecialAbility
{
    None,
    // ノーマル (1-15)
    SwiftPosture,       // 迅速体勢: 未移動なら最初の移動コスト-1
    InterceptPosture,   // 迎撃姿勢: HP100%時、最初の被ダメ-10%
    PierceEnhance,      // 刺突強化: 単体攻撃の与ダメ+10%
    SiegeAdapt,         // 迫撃適応: 範囲攻撃で2体以上巻き込み時、与ダメ+10%
    WatchEye,           // 監視眼: 視界+1
    Tenacity,           // 粘り腰: HP50%以下の時、被ダメ-10%
    PursuitInstinct,    // 追撃本能: HP50%以下の敵への与ダメ+10%
    WallFamiliar,       // 防壁慣れ: 建物隣接マスにいる時、DEF+10%
    FocusMaintain,      // 集中維持: そのターン未移動なら攻撃与ダメ+10%
    FirstAid,           // 応急処置: ターン終了時、周囲1マスに味方なしなら最大HP3%回復
    PoisonCoat,         // 毒塗り: 単体攻撃命中時10%で毒付与
    FrostBlade,         // 冷気刃: 攻撃命中時10%で冷気付与
    Foresight,          // 見切り: 視界内の敵からの被ダメ-5%
    PressureShot,       // 圧迫射: 攻撃命中時10%で弱体付与
    Efficiency,         // 省力化: 最初に使うスキルのAP消費-1（最低1）

    // レア (16-25)
    Desperation,        // 背水: HP30%以下でATK+20%
    IronWalkDefense,    // 鉄壁歩法: 移動後、そのターン被ダメ-15%
    HeightAdapt,        // 高所順応: 自分が高い位置にいる時、与ダメ+15%
    LowHunt,            // 低所狩り: 自分が高い位置にいる時、15%で鈍足付与
    MagicResist,        // 耐魔皮膜: 魔法/遠距離から被ダメ-15%
    Breach,             // 破勢: 攻撃命中時15%で破甲付与
    VenomBlade,         // 猛毒刃: 単体攻撃命中時15%で毒付与
    FrostPressure,      // 氷結圧: 攻撃命中時10%で冷気、5%で凍結付与
    SupportSpread,      // 支援波及: バフ/回復を与えた時、周囲1マス別味方に50%波及
    SurvivalInstinct,   // 生還本能: 1戦闘に1回、致死ダメージをHP1で耐える

    // スーパーレア (26-30)
    ShadowCross,        // 影渡り: 視界外から攻撃時ダメージ倍率+0.25
    ArtilleryControl,   // 砲撃管制: 範囲攻撃が3体以上巻き込み時マーク付与
    GuardianZone,       // 守護圏: 周囲1マスの味方の被ダメ-10%
    SniperCorrection,   // 狙撃補正: 3マス以上離れた敵への与ダメ+20%
    HolyReaction,       // 聖域反応: 周囲1マス味方が状態異常時、ターン終了時その味方HP5%回復

    // レジェンダリー (31-35)
    BattlefieldDomination, // 戦場支配: 視界内の敵全員に被ダメ+10%
    IndomitableWill,    // 不滅の意志: 毎ターン最初の状態異常無効+被ダメ-10%
    ThunderChain,       // 雷印連鎖: マーク状態の敵に攻撃命中時20%でスタン付与
    IcePrison,          // 氷牢結界: 冷気状態の敵を攻撃時10%で凍結付与
    PurifyHalo          // 浄化光輪: ターン開始時、周囲1マス味方1体の状態異常解除+守勢付与
}

public enum SpecialAbilityRarity
{
    Normal,
    Rare,
    SuperRare,
    Legendary
}

// =====================================================================
//  スキルレアリティ
// =====================================================================
public enum SkillRarity
{
    Normal,      // 出現率50%
    Rare,        // 出現率20%
    SuperRare,   // 出現率15%
    Legendary    // 出現率5%
}

public class Status : MonoBehaviour
{
    [Header("種類")]
    public Kind kind;
    [Header("チーム")]
    public Team team;
    [Header("駒のタイプ")]
    public Type type;
    [Header("状態")]
    public State state;
    [Header("スキル")]
    public Skill skill;
    [Header("駒の向き")]
    public Direction direction;
    [Header("パッシブスキル")]
    public PassiveSkill passiveskill;
    [Header("強敵（縄張り付き中立ボス）")]
    [Tooltip("trueなら縄張り外では被弾無効、縄張り外からも出ない")]
    public bool isWildBoss;
    [Tooltip("強敵の縄張り中心（Y=0）")]
    public Vector3Int wildBossTerritoryCenter;
    [Tooltip("強敵の縄張り半径（チェビシェフ距離）")]
    public int wildBossTerritoryRadius = 3;
    [Tooltip("強敵アーキタイプ（固有AI・行動周期）")]
    public WildBossArchetype wildBossArchetype;
    [Tooltip("強敵の所持AP（毎ターン最大値までリフィル）")]
    public int wildBossAP;
    [Tooltip("強敵の最大AP")]
    public int wildBossMaxAP;
    [Tooltip("強敵の行動カウンタ（ターン毎の周期判定用）")]
    public int wildBossTurnCounter;
    [Tooltip("反撃バフの残ターン（反逆の騎士王用）")]
    public int wildBossCounterTurns;
    [Tooltip("攻撃バフの残ターン（ドラゴン用）")]
    public int wildBossAtkBuffTurns;
    [Tooltip("強敵がPhase2に移行済みか（反逆の騎士王: HP50%以下で発動）")]
    public bool wildBossPhase2Active;
    [Tooltip("強敵の脅威度（25=通常/75=激怒）。HP50%以下で75へ昇格")]
    public int wildBossCurrentThreat = 25;
    [Header("ステータス")]
    public int HP;
    public int ATK;
    public int DEF;
    [Header("レベル")]
    public int Level = 1;
    [Header("駒の視界")]
    public HashSet<Vector3Int> VisionCell = new HashSet<Vector3Int>();
    [Header("疲労")]
    public int Fatigue = 0;

    [Header("維持費未払いターン数")]
    public int UpkeepUnpaidTurns = 0;

    [Header("累積経験値")]
    public int Experience = 0;

    [Header("シールド（無敵バフ）")]
    public int ShieldTurns = 0;
    [HideInInspector] public bool ShieldActivated = false;
    /// <summary>シールドが一度でも発動したか（クリスタル反撃の3体拡大判定用・永続）</summary>
    [HideInInspector] public bool ShieldEverActivated = false;
    [HideInInspector] public int MaxHP;

    [Header("建築物の種類")]
    public FacilityKind facilityKind;

    // =====================================================================
    //  状態異常・バフ・スキル
    // =====================================================================
    [Header("状態異常・バフ")]
    public List<ActiveEffect> ActiveEffects = new List<ActiveEffect>();

    [Header("割り当てスキル")]
    public int AssignedSkillId = -1; // SkillData.Id（-1 = なし）

    [Header("スキルクールダウン")]
    public int SkillCooldown = 0; // 0 = 使用可能、1以上 = 使用不可（ターン毎に-1）

    // =====================================================================
    //  Special Ability（ランダム配布パッシブ）
    // =====================================================================
    [Header("Special Ability")]
    public SpecialAbility specialAbility = SpecialAbility.None;

    // ---- Special Ability 用トラッキングフラグ ----
    [HideInInspector] public bool HasMovedThisTurn = false;
    [HideInInspector] public bool SurvivalInstinctUsed = false;
    [HideInInspector] public bool FirstSkillUsedThisTurn = false;
    [HideInInspector] public bool DebuffNullifiedThisTurn = false;
    [HideInInspector] public bool InterceptUsedThisTurn = false;

    /// <summary>BOSS駒かどうか（Kind.Boss または Kind.King かつ Enemy チームで判定）</summary>
    public bool IsBoss => kind == Kind.Boss;

    // ================================================================
    //  状態管理ヘルパー
    // ================================================================

    /// <summary>ターン開始時にリセットすべきフラグを全て初期化する</summary>
    public void ResetTurnFlags()
    {
        HasMovedThisTurn = false;
        FirstSkillUsedThisTurn = false;
        DebuffNullifiedThisTurn = false;
        InterceptUsedThisTurn = false;
    }

    /// <summary>戦闘開始時にリセットすべきフラグを初期化する</summary>
    public void ResetBattleFlags()
    {
        SurvivalInstinctUsed = false;
    }

    /// <summary>ダメージを適用する（0未満にならない）。強敵の無敵判定もここで行う。</summary>
    public int ApplyDamage(int damage)
    {
        damage = UnityEngine.Mathf.Max(0, damage);
        if (isWildBoss && IsWildBossInvulnerable())
        {
            UnityEngine.Debug.Log($"[WildBoss] 縄張り外のため無敵: ダメージ{damage}を無効化");
            return 0;
        }
        HP = UnityEngine.Mathf.Max(0, HP - damage);
        return damage;
    }

    /// <summary>
    /// 強敵の無敵判定: 縄張り内にPlayer/Enemyの駒がいれば解除（= false）、
    /// いなければ無敵（= true）。
    /// </summary>
    private bool IsWildBossInvulnerable()
    {
        var reg = UnitRegistry.Instance;
        if (reg == null) return true;
        return !HasAnyUnitInTerritory(reg.PlayerUnits)
            && !HasAnyUnitInTerritory(reg.EnemyUnits);
    }

    private bool HasAnyUnitInTerritory(System.Collections.Generic.IReadOnlyList<Status> list)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
        {
            var u = list[i];
            if (u == null || !u.IsAlive || u == this) continue;
            var g = u.GridPosition;
            int dx = UnityEngine.Mathf.Abs(g.x - wildBossTerritoryCenter.x);
            int dz = UnityEngine.Mathf.Abs(g.z - wildBossTerritoryCenter.z);
            if (UnityEngine.Mathf.Max(dx, dz) <= wildBossTerritoryRadius) return true;
        }
        return false;
    }

    /// <summary>回復を適用する（MaxHPを超えない）</summary>
    public int ApplyHeal(int amount)
    {
        amount = UnityEngine.Mathf.Max(0, amount);
        int actual = UnityEngine.Mathf.Min(amount, MaxHP - HP);
        HP += actual;
        return actual;
    }

    /// <summary>HP割合を返す（MaxHP が 0 の場合は 1.0）</summary>
    public float HPRatio => MaxHP > 0 ? (float)HP / MaxHP : 1f;

    /// <summary>生存しているかどうか</summary>
    public bool IsAlive => HP > 0 && gameObject.activeInHierarchy;

    /// <summary>グリッド座標（Y=0）を返す</summary>
    public UnityEngine.Vector3Int GridPosition => GridHelper.ToGridXZ(transform.position);

    /// <summary>
    /// 維持費未払いターン数に応じた ATK/DEF 低下率（0.0〜0.40）。
    /// 1-3: -10%、4-6: -25%、7-9: -40%。10以上は離脱処理側が担当。
    /// </summary>
    public float UpkeepPenaltyATKDEF
    {
        get
        {
            int t = UpkeepUnpaidTurns;
            if (t <= 0) return 0f;
            if (t <= 3) return GameConstants.UpkeepPenaltyStage1;
            if (t <= 6) return GameConstants.UpkeepPenaltyStage2;
            if (t <= 9) return GameConstants.UpkeepPenaltyStage3;
            return GameConstants.UpkeepPenaltyStage3;
        }
    }

    // =====================================================================
    //  経験値・レベルアップ
    // =====================================================================
    /// <summary>指定レベルに必要な累計XP（Lv1=0、Lv2=10、以後×1.15切り上げ）</summary>
    public static int XPRequiredForLevel(int level)
    {
        if (level <= 1) return 0;
        int required = GameConstants.XPRequiredLv2;
        int total = required;
        for (int lv = 3; lv <= level; lv++)
        {
            required = UnityEngine.Mathf.CeilToInt(required * GameConstants.XPLevelMultiplier);
            total += required;
        }
        return total;
    }

    /// <summary>XPを加算し、必要XPに達していればレベルアップする。</summary>
    public void GainExperience(int amount)
    {
        if (amount <= 0) return;
        Experience += amount;
        while (Level < 10 && Experience >= XPRequiredForLevel(Level + 1))
        {
            Level++;
            // シンプルなステータス成長（ATK/DEF/MaxHP +10%）
            ATK = UnityEngine.Mathf.RoundToInt(ATK * 1.10f);
            DEF = UnityEngine.Mathf.RoundToInt(DEF * 1.10f);
            int newMax = UnityEngine.Mathf.RoundToInt(MaxHP * 1.10f);
            int gained = newMax - MaxHP;
            MaxHP = newMax;
            HP += gained;
            UnityEngine.Debug.Log($"[Level] {kind} → Lv{Level} (XP:{Experience})");
            if (team == Team.Player && AchievementSystem.Instance != null)
                AchievementSystem.Instance.OnLevelUp(Level);
        }
    }

    /// <summary>
    /// 与ダメージから獲得XPを計算して加算する（兵舎XPボーナス込み）。
    /// 実獲得XP = floor(damage * (1 + barracksXPPercent/100))
    /// </summary>
    public void GainExperienceFromDamage(int damage, int barracksXPPercent)
    {
        if (damage <= 0) return;
        int actualXP = UnityEngine.Mathf.FloorToInt(damage * (1f + barracksXPPercent / 100f));
        GainExperience(actualXP);
    }

    /// <summary>
    /// レベル・経験値・ATK/DEF/MaxHP を Lv1 の基礎値にリセットする（駒撃破時に呼ばれる）。
    /// HP は 0 のまま維持（次回スポーン時に MaxHP 付与される想定）。
    ///
    /// 注: WildBoss (isWildBoss==true) は WildBossSystem.Profile で
    /// アーキタイプ別ステータス (HP=6200〜8200 等) を持つため、
    /// UnitStaticData.Boss (HP=20000) で上書きすると整合性が崩れる。
    /// WildBoss は撃破後に再活性化されない設計のため、リセット自体をスキップする。
    /// </summary>
    public void ResetToLv1()
    {
        if (isWildBoss) return; // WildBoss は専用 Profile を持つためリセット対象外
        if (!UnitStaticData.Table.TryGetValue(kind, out var info)) return;
        Level = 1;
        Experience = 0;
        ATK = info.BaseATK;
        DEF = info.BaseDEF;
        MaxHP = info.BaseHP;
        // HP は 0 のまま（撃破直後）
        UnityEngine.Debug.Log($"[Status] {kind} を Lv1 にリセット (ATK={ATK} DEF={DEF} MaxHP={MaxHP})");
    }

    /// <summary>
    /// HP が 0 になった駒の共通クリーンアップ処理。
    /// BattleSystem 経由でない攻撃 (WildBoss/BuildingAttack/Dungeon モンスター反撃 等)
    /// が ApplyDamage 後に呼び、ゴースト駒（HP=0 だが scene に残る）を防ぐ。
    /// 既に非アクティブなら何もしない（idempotent）。
    /// </summary>
    public void HandleDeathIfDead()
    {
        if (HP > 0 || type != Type.Unit) return;
        if (!gameObject.activeInHierarchy) return;

        // MoveGenerator の占有セル解除
        var moveGen = UnityEngine.Object.FindFirstObjectByType<MoveGenerator>();
        if (moveGen != null)
        {
            UnityEngine.Vector3 cellPos = moveGen.Cell(transform.position);
            moveGen.RemoveOccupied(cellPos);
        }

        ResetToLv1();
        gameObject.SetActive(false);
        UnityEngine.Debug.Log($"[Status] {team} {kind} が HP=0 で非表示化（HandleDeathIfDead）");
    }
}

// =====================================================================
//  AI性格システム（大きい性格）
// =====================================================================
public enum MajorPersonality
{
    Combat,     // 戦闘型: 前線圧力・撃破重視
    Intellect,  // 知性型: 整形・防衛・長期重視
    Adaptive,   // 変動型: 局面に応じて揺れる
    Growth      // 成長型: 試合中学習で進化
}

// =====================================================================
//  AI性格システム（細かい性格6項目）— 合計300ポイント
// =====================================================================
[System.Serializable]
public class PersonalityTraits
{
    public int Caution;     // 慎重性
    public int Command;     // 指揮性
    public int Obsession;   // 執着性
    public int Defense;     // 防衛性
    public int Tactics;     // 戦術性
    public int Development; // 発展性

    public int Total => Caution + Command + Obsession + Defense + Tactics + Development;
}

// =====================================================================
//  AI候補行動タイプ
// =====================================================================
public enum AIActionType
{
    Move,           // 移動
    Attack,         // 攻撃
    SkillUse,       // スキル使用
    Retreat,        // 撤退
    Support,        // 援護配置
    Surround,       // 包囲移動
    Build,          // 建築
    Summon,         // 駒生成
    DefenseRepos,   // 防衛再配置
    SubCrystal,     // サブクリ展開
    Wait            // 待機
}

// =====================================================================
//  ターン方針（AICommander がターン冒頭で決定）
// =====================================================================
public enum TurnStrategy
{
    Assault,        // 攻勢: 前線突破・包囲・撃破を優先
    CrystalDefense, // 防衛: クリスタル守備を最優先
    RetreatRegroup, // 再編: 撤退→回復→次ターン反撃の準備
    EconomyBuild,   // 建築: 建設・召喚で基盤を固める
    Balanced,       // 均衡: 特に偏らず局面判断で行動
    ScoutSearch,    // 索敵: 未探索エリアへの偵察・情報収集を優先
    ContactEngage   // 初接敵: 敵を視認した直後、攻撃・スキル・交戦前進を優先
}

// =====================================================================
//  以下のenumは新規ファイルに出さずここに追記する（設計原則1）
// =====================================================================

// 建築の種別（FacilityData と EconomySystem で使用）
public enum FacilityKind
{
    Field, Bakery, LoggingCamp,
    Quarry, Mine,
    Barracks, House, LuxuryHouse, Well, Warehouse,
    WoodWall, StoneWall,
    Mortar, Cannon, RestraintTrap, SpikeTrap, HeroSword,
    SubCrystal
}

// 資源の種別（FacilityData と EconomySystem で使用）
public enum ResourceKind
{
    Wood, Stone, Iron, MagicOre,
    Wheat, Bread, Water, Citizen, None
}
