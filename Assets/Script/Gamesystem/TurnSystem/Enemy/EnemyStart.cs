using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class EnemyStart : StateCore
{
    private TurnGenerater turngenerater;
    public UnitClick unitclick;
    public AttackPointt attackpoint;

    public EnemyStart(TurnGenerater turngenerater, UnitClick untclick, AttackPointt attackpoint)
    {
        this.turngenerater = turngenerater;
        this.unitclick = untclick;
        this.attackpoint = attackpoint;
    }
    public void Entry()
    {
        Debug.Log("EnemyStart“Ë“ü");
        turngenerater.ChangeState(new EnemyMove(turngenerater,unitclick,attackpoint));
    }

    public void Update()
    {

    }

    public void Exit()
    {

    }
}
