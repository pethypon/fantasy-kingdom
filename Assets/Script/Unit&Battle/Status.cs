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
    Normal,
    Invincible  // 無敵シールド（メインクリスタル用）
}

public enum Direction
{
    N,
    S
}

public enum Skill
{
    None
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

    [Header("建築物の種類")]
    public FacilityKind facilityKind;
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
