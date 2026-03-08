using System.Collections;
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
    WoodWall,   // �t�F�[�Y6�Ŏg�p
    StoneWall,  // �t�F�[�Y6�Ŏg�p
    None
}

public enum Type
{
    Unit,
    Building,
    Wall,           // �t�F�[�Y6�Ŏg�p
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
    [Header("���")]
    [SerializeField] public Kind kind;
    [Header("�`�[��")]
    [SerializeField] public Team team;
    [Header("��̃^�C�v")]
    [SerializeField] public Type type;
    [Header("���")]
    [SerializeField] public State state;
    [Header("�X�L��")]
    [SerializeField] public Skill skill;
    [Header("��̌���")]
    [SerializeField] public Direction direction;
    [Header("�p�b�V�u�X�L��")]
    [SerializeField] public PassiveSkill passiveskill;
    [Header("�X�e�[�^�X")]
    [SerializeField] public int HP;
    [SerializeField] public int ATK;
    [SerializeField] public int DEF;
    [Header("���x��")]
    public int Level = 1;
    [Header("��̎��E")]
    [SerializeField] public HashSet<Vector3Int> VisionCell;
    [Header("��J")]
    [SerializeField] public int Fatigue = 0;

    [Header("建築物の種類")]
    public FacilityKind facilityKind;
}

// ������������������������������������������������������������������������������������������������������������������������������������������
//  �ȉ���enum�͐V�K�t�@�C������炸�����ɒǋL����i�݌v����1�j
// ������������������������������������������������������������������������������������������������������������������������������������������

// �����̎�ʁiFacilityData �� EconomySystem �Ŏg�p�j
public enum FacilityKind
{
    Field, Bakery, LoggingCamp, LumberMill,
    Quarry, StoneWorks, Mine, Smelter,
    Barracks, House, Well, Warehouse,
    WoodWall, StoneWall,
    Mortar, Cannon, RestraintTrap, SpikeTrap, HeroSword
}

// �����̎�ʁiFacilityData �� EconomySystem �Ŏg�p�j
public enum ResourceKind
{
    Wood, Stone, IronOre, Iron, MagicOre, Coal,
    Wheat, Bread, Water, Plank, CutStone, Citizen, None
}
