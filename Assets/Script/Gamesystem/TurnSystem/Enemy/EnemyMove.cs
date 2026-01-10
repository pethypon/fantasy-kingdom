using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyMove : StateCore
{
    public TurnGenerater turngenerater;
    public UnitClick unitclick;
    public AttackPointt attackpoint;
    public EnemyMove(TurnGenerater turngenerater, UnitClick unitclick, AttackPointt attackpoint)
    {
        this.turngenerater = turngenerater;
        this.unitclick = unitclick;
        this.attackpoint = attackpoint;
    }
    public void Entry()
    {
        turngenerater.ChangeState(new PlayerStart(turngenerater,unitclick,attackpoint));
        Debug.Log("“G‚Ìƒ^[ƒ“I—¹");
    }

    public void Update()
    {

    }

    public void Exit()
    {

    }
}
