using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum Team
{
    Player,
    Enemy,
    None

}
public enum Kind
{
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
    None
}
public enum Type
{
    Unit,
    Building,
    MovePoint
}
public enum State
{
    Normal
}
public enum Equipment
{
    Head,
    Body,
    Arm,
    Waist,
    Leg

}

public enum Skill
{
    None
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

    [Header("ステータス")]
    [SerializeField] public int HP;
    [SerializeField] public int ATK;
    [SerializeField] public int DEF;
}
