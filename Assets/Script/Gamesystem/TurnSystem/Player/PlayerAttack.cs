using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAttack : StateCore
{
    public MapCreate mapcreate;
    public PlayerMove move;
    public AttackPointt attackpoint;
    public TurnGenerater turngenerater;
    public PlayerAttack playerattack;
    public PlayerMove.AttackMode attackmode;
    public BattleSystem battlesystem;
    public UnitClick unitclick;
    public Status Attack;
    public float Speed;
    public float Scrollspeed;
    public int Maxx;
    public int Maxz;
    public bool AttackSetting;
    public bool AttackSuccess;

    public PlayerAttack(MapCreate mapcreate,PlayerMove move,PlayerMove.AttackMode attackmode,AttackPointt attackpoint,TurnGenerater turngenerater,UnitClick unitclick,BattleSystem battlesystem)
    {
        this.mapcreate = mapcreate;
        this.move = move;
        this.attackmode = attackmode;
        this.attackpoint = attackpoint;
        this.turngenerater = turngenerater;
        this.unitclick = unitclick;
        this.battlesystem = battlesystem;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void Entry()
    {
        Speed = move.speed;
        Scrollspeed = move.scrollspeed;
        Maxx = mapcreate.maxX;
        Maxz = mapcreate.maxZ;
        attackpoint.AttackPointCall(move.Obj, move.ObjP, move);

        if(AttackSuccess == true)
        {
            AttackSuccess = false;
        }
    }

    // Update is called once per frame
    public void Update()
    {
        
        if (AttackSuccess == true)
        {
            attackpoint.AtkpDestroy();
            Reset();
            turngenerater.ChangeState(new PlayerMove(turngenerater, unitclick, attackpoint, battlesystem));
        }
        else 
        {
            float ma = Input.GetAxis("Horizontal");
            float mb = Input.GetAxis("Vertical");
            Vector3 Move = new Vector3(ma, 0, mb).normalized;
            turngenerater.CameraObject.Translate(Move * Speed * Time.deltaTime, Space.World);
            Vector3 camerapos = turngenerater.CameraObject.position;
            float cma = Mathf.Clamp(camerapos.x, 0f, Maxx - 10);
            float cmb = Mathf.Clamp(camerapos.z, 0f, Maxz - 10);
            camerapos.x = cma;
            camerapos.z = cmb;
            turngenerater.CameraObject.position = camerapos;
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                float camerascroll = Camera.main.fieldOfView - scroll * Scrollspeed;
                Camera.main.fieldOfView = Mathf.Clamp(camerascroll, 30f, 90f);
            }

            if (Input.GetMouseButtonDown(0))
            {
                unitclick.AttackClick(battlesystem,this);

                
            }

            if (Input.GetMouseButtonDown(1))
            {
                attackpoint.AtkpDestroy();
                Reset();
                turngenerater.ChangeState(new PlayerMove(turngenerater, unitclick, attackpoint, battlesystem));
            }
        }

    }

    public void Exit()
    {

    }
    public void Reset()
    {
        attackpoint.obj = null;
        unitclick.ATKC = null;
        battlesystem.target = null;
        battlesystem.AttackSide = null;
        AttackSuccess = false;
        attackpoint.AtkpDestroy();
    }
}
