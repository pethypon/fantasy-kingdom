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
    Sniper
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
    Slow,           // 鈍足: 移動AP+2 (1T)
    Blind,          // 盲目: 視界-1 (1T)
    Poison,         // 毒: ターン終了時固定8ダメ + 回復-25% (2T)
    Chill,          // 冷気: 移動AP+2 + ATK-10% (1T)
    Freeze,         // 凍結: 移動不可 + 被ダメ+10% (1T)
    Seal,           // 封技: スキル倍率-20% (1T)
    Curse,          // 呪傷: 回復-50% (2T)
    Bind,           // 束縛: 移動不可、攻撃可 (1T)
    Bleed           // 出血: ターン終了時固定6ダメ (2T)
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
    [SerializeField] public Kind kind;
    [Header("チーム")]
    [SerializeField] public Team team;
    [Header("駒のタイプ")]
    [SerializeField] public Type type;
    [Header("状態")]
    [SerializeField] public State state;
    [Header("スキル")]
    [SerializeField] public Skill skill;
    [Header("駒の向き")]
    [SerializeField] public Direction direction;
    [Header("パッシブスキル")]
    [SerializeField] public PassiveSkill passiveskill;
    [Header("ステータス")]
    [SerializeField] public int HP;
    [SerializeField] public int ATK;
    [SerializeField] public int DEF;
    [Header("レベル")]
    public int Level = 1;
    [Header("駒の視界")]
    [SerializeField] public HashSet<Vector3Int> VisionCell;
    [Header("疲労")]
    [SerializeField] public int Fatigue = 0;

    [Header("シールド（無敵バフ）")]
    [SerializeField] public int ShieldTurns = 0;
    [HideInInspector] public bool ShieldActivated = false;
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

    /// <summary>BOSS駒かどうか（Kind.Boss または Kind.King かつ Enemy チームで判定）</summary>
    public bool IsBoss => kind == Kind.Boss;
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
    Field, Bakery, LoggingCamp, LumberMill,
    Quarry, StoneWorks, Mine, Smelter,
    Barracks, House, Well, Warehouse,
    WoodWall, StoneWall,
    Mortar, Cannon, RestraintTrap, SpikeTrap, HeroSword,
    SubCrystal
}

// 資源の種別（FacilityData と EconomySystem で使用）
public enum ResourceKind
{
    Wood, Stone, IronOre, Iron, MagicOre, Coal,
    Wheat, Bread, Water, Plank, CutStone, Citizen, None
}
