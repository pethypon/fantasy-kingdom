using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class EnemyStart : StateCore
{
    private TurnGenerater turngenerater;
    public UnitClick unitclick;
    public AttackPointt attackpoint;
    public BattleSystem battlesystem;

    public EnemyStart(TurnGenerater turngenerater, UnitClick untclick, AttackPointt attackpoint, BattleSystem battlesystem)
    {
        this.turngenerater = turngenerater;
        this.unitclick = untclick;
        this.attackpoint = attackpoint;
        this.battlesystem = battlesystem;
    }
    public void Entry()
    {
        Debug.Log("EnemyStart“Ë“ü");
        turngenerater.ChangeState(new EnemyMove(turngenerater,unitclick,attackpoint, battlesystem));
    }

    public void Update()
    {

    }

    public void Exit()
    {

    }
}
