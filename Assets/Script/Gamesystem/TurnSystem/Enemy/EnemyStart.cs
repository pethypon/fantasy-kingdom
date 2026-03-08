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
    public VisionGenerater visiongenerater;
    public MoveGererater movegenerater;
    public MapCreate mapcreate;
    public CrystalSystem crystalsystem;
    public UnitSetting unitset;


    public EnemyStart(TurnGenerater turngenerater, UnitClick untclick, AttackPointt attackpoint,
        BattleSystem battlesystem, VisionGenerater visiongenerater, MoveGererater movegenerater,
        MapCreate mapcreate, CrystalSystem crystalsystem, UnitSetting unitset)
    {
        this.turngenerater = turngenerater;
        this.unitclick = untclick;
        this.attackpoint = attackpoint;
        this.battlesystem = battlesystem;
        this.visiongenerater = visiongenerater;
        this.movegenerater = movegenerater;
        this.mapcreate = mapcreate;
        this.crystalsystem = crystalsystem;
        this.unitset = unitset;
    }
    public void Entry()
    {
        Debug.Log("EnemyStart突入");
        // ターン開始時に AP と疲労をリセット
        turngenerater.apsystem.ResetAP(Team.Enemy);
        turngenerater.apsystem.ResetFatigue(unitset.EnemyUnit);
        turngenerater.ChangeState(new EnemyMove(turngenerater,unitclick,attackpoint, battlesystem,visiongenerater, movegenerater, mapcreate, crystalsystem,unitset));
    }

    public void Update()
    {

    }

    public void Exit()
    {

    }
}
