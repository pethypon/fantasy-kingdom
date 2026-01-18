using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyMove : StateCore
{
    public TurnGenerater turngenerater;
    public UnitClick unitclick;
    public AttackPointt attackpoint;
    public BattleSystem battlesystem;
    public EnemyMove(TurnGenerater turngenerater, UnitClick unitclick, AttackPointt attackpoint, BattleSystem battlesystem)
    {
        this.turngenerater = turngenerater;
        this.unitclick = unitclick;
        this.attackpoint = attackpoint;
        this.battlesystem = battlesystem;
    }
    public void Entry()
    {
        turngenerater.ChangeState(new PlayerStart(turngenerater,unitclick,attackpoint,battlesystem));
        Debug.Log("“G‚Ìƒ^[ƒ“I—¹");
    }

    public void Update()
    {

    }

    public void Exit()
    {

    }
}
