using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStart : StateCore
{
    private TurnGenerater turngenerater;
    private UnitClick unitclick;
    public AttackPointt attackpoint;
    public BattleSystem battlesystem;
    private int playerap;
    private int playerplusap;
    private int playerminusap;
    private int turn;

    public PlayerStart(TurnGenerater turngenerater,UnitClick unitclick, AttackPointt attackpoint, BattleSystem battlesystem)
    {
        this.turngenerater = turngenerater;
        this.unitclick = unitclick;
        this.attackpoint = attackpoint;
        this.battlesystem = battlesystem;
    }
    public void Entry()
    {
        playerap = turngenerater.PlayerAP;
        playerplusap = turngenerater.PlayerPlusAP;
        playerminusap = turngenerater.PlayerMinusAP;
        playerap = playerap + playerplusap - playerminusap;
        turngenerater.PlayerAP = playerap;
        turn = turngenerater.Turn;
        turn += 1;
        turngenerater.Turn = turn;

        //‚Â‚¬‚ÌSutate‚É‚·‚é
        turngenerater.ChangeState(new PlayerMove(turngenerater,unitclick, attackpoint,battlesystem));
    }

    public void Update()
    {
        
    }

    public void Exit()
    {

    }
}
