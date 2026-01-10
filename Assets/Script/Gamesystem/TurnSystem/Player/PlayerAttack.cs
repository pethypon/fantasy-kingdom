using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttack : StateCore
{
    public MapCreate mapcreate;
    public PlayerMove move;
    public AttackPointt attackpoint;
    public PlayerMove.AttackMode attackmode;
    public PlayerAttack(MapCreate mapcreate,PlayerMove move,PlayerMove.AttackMode attackmode,AttackPointt attackpoint)
    {
        this.mapcreate = mapcreate;
        this.move = move;
        this.attackmode = attackmode;
        this.attackpoint = attackpoint;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void Entry()
    {
        attackpoint.AttackPointCall(move.Obj,move.ObjP,move);
    }

    // Update is called once per frame
    public void Update()
    {
        
    }

    public void Exit()
    {

    }
}
