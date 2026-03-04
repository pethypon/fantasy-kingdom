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
<<<<<<< HEAD
<<<<<<< HEAD
    WoodWall,   // ƒtƒF[ƒY6‚Åg—p
    StoneWall,  // ƒtƒF[ƒY6‚Åg—p
=======
    WoodWall,
    StoneWall,
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
=======
>>>>>>> parent of d903d2d (2)
    None
}
public enum Type
{
    Unit,
    Building,
<<<<<<< HEAD
<<<<<<< HEAD
    Wall,           // ƒtƒF[ƒY6‚Åg—p
=======
    Wall,
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
=======
>>>>>>> parent of d903d2d (2)
    MovePoint,
    AttackPoint
}
public enum State
{
    Normal,
    Stun       // Phase 2: Crossbow 10%ã‚¹ã‚¿ãƒ³
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
    [Header("ç¨®é¡")]
    [SerializeField] public Kind kind;
<<<<<<< HEAD
<<<<<<< HEAD
=======

>>>>>>> parent of d903d2d (2)
    [Header("ƒ`[ƒ€")]
    [SerializeField] public Team team;

    [Header("‹î‚Ìƒ^ƒCƒv")]
    [SerializeField] public Type type;

    [Header("ó‘Ô")]
    [SerializeField] public State state;

    [Header("ƒXƒLƒ‹")]
    [SerializeField] public Skill skill;

    [Header("‹î‚ÌŒü‚«")]
    [SerializeField] public Direction direction;

    [Header("ƒpƒbƒVƒuƒXƒLƒ‹")]
    [SerializeField] public PassiveSkill passiveskill;

    [Header("ƒXƒe[ƒ^ƒX")]
    [SerializeField] public int HP;
    [SerializeField] public int ATK;
    [SerializeField] public int DEF;

    [Header("‹î‚Ì‹ŠE")]
    [SerializeField] public HashSet<Vector3Int> VisionCell;

    [Header("”æ˜J")]
    [SerializeField] public int Fatigue = 0;
}
<<<<<<< HEAD

// „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
//  ˆÈ‰º‚Ìenum‚ÍV‹Kƒtƒ@ƒCƒ‹‚ğì‚ç‚¸‚±‚±‚É’Ç‹L‚·‚éiİŒvŒ´‘¥1j
// „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ

// Œš•¨‚Ìí•ÊiFacilityData ‚Æ EconomySystem ‚Åg—pj
=======
    [Header("ãƒãƒ¼ãƒ ")]
    [SerializeField] public Team team;
    [Header("é§’ã®ã‚¿ã‚¤ãƒ—")]
    [SerializeField] public Type type;
    [Header("çŠ¶æ…‹")]
    [SerializeField] public State state;
    [Header("ã‚¹ã‚­ãƒ«")]
    [SerializeField] public Skill skill;
    [Header("é§’ã®å‘ã")]
    [SerializeField] public Direction direction;
    [Header("ãƒ‘ãƒƒã‚·ãƒ–ã‚¹ã‚­ãƒ«")]
    [SerializeField] public PassiveSkill passiveskill;
    [Header("ã‚¹ãƒ†ãƒ¼ã‚¿ã‚¹")]
    [SerializeField] public int HP;
    [SerializeField] public int ATK;
    [SerializeField] public int DEF;
    [Header("ãƒ¬ãƒ™ãƒ«")]
    public int Level = 1;
    [Header("é§’ã®è¦–ç•Œ")]
    [SerializeField] public HashSet<Vector3Int> VisionCell;
    [Header("ç–²åŠ´")]
    [SerializeField] public int Fatigue = 0;
}

// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
//  ä»¥ä¸‹ã®enumã¯æ–°è¦ãƒ•ã‚¡ã‚¤ãƒ«ã‚’ä½œã‚‰ãšã“ã“ã«è¿½è¨˜ã™ã‚‹ï¼ˆè¨­è¨ˆåŸå‰‡1ï¼‰
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

// å»ºç‰©ã®ç¨®åˆ¥ï¼ˆFacilityData ã¨ EconomySystem ã§ä½¿ç”¨ï¼‰
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
public enum FacilityKind
{
    Field, Bakery, LoggingCamp, LumberMill,
    Quarry, StoneWorks, Mine, Smelter,
    Barracks, House, Well, Warehouse,
    WoodWall, StoneWall,
    Mortar, Cannon, RestraintTrap, SpikeTrap, HeroSword
}

<<<<<<< HEAD
// ‘Œ¹‚Ìí•ÊiFacilityData ‚Æ EconomySystem ‚Åg—pj
=======
// è³‡æºã®ç¨®åˆ¥ï¼ˆFacilityData ã¨ EconomySystem ã§ä½¿ç”¨ï¼‰
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
public enum ResourceKind
{
    Wood, Stone, IronOre, Iron, MagicOre, Coal,
    Wheat, Bread, Water, Plank, CutStone, Citizen, None
}
=======
>>>>>>> parent of d903d2d (2)
