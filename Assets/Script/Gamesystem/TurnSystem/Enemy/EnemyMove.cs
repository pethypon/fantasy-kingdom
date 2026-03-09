using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyMove : StateCore
{
    public TurnGenerater turngenerater;
    public UnitClick unitclick;
    public AttackPointt attackpoint;
    public BattleSystem battlesystem;
    public VisionGenerater visiongenerater;
    public MoveGererater movegenerater;
    public MapCreate mapcreate;
    public CrystalSystem crystalsystem;
    public UnitSetting unitset;

    public EnemyMove(TurnGenerater turngenerater, UnitClick unitclick, AttackPointt attackpoint, BattleSystem battlesystem, VisionGenerater visiongenerater, MoveGererater movegenerater, MapCreate mapcreate, CrystalSystem crystalsystem, UnitSetting unitset)
    {
        this.turngenerater = turngenerater;
        this.unitclick = unitclick;
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
        visiongenerater.VisionPoint(mapcreate, movegenerater, crystalsystem);

        // Enemy の資源獲得（ターン終了時）
        if (turngenerater.economysystem != null)
            turngenerater.economysystem.ProcessTurn(Team.Enemy);

        turngenerater.ChangeState(new PlayerStart(turngenerater,unitclick,attackpoint,battlesystem, visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
        Debug.Log("敵のターン終了");
    }

    public void Update()
    {

    }

    public void Exit()
    {
        visiongenerater.VisionPoint(mapcreate, movegenerater, crystalsystem);
    }
}
