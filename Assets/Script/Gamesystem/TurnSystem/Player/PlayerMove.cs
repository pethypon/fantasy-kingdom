using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class PlayerMove : StateCore
{
    public enum AttackMode
    {
        None,
        Normal,
        Skill
    }
    public AttackMode attackmode;
    public AttackPointt attackpoint;
    private TurnGenerater turngenerater;
    private UnitClick unitclick;
    private MapCreate mapcreate;
    public PlayerAttack playerattack;
    public BattleSystem battlesystem;
    
    public bool MenuSwitch;
    public Status Obj;
    public Vector3 ObjP;
    public Status MP;
    private int maxx;
    private int maxz;
    public float speed = 10f;
    public float scrollspeed = 20f;
    public RaycastHit hit;
    public Vector3 oldcell;
    public Vector3 newcell;

    public PlayerMove(TurnGenerater turngenerater, UnitClick unitclick ,AttackPointt attackpoint,BattleSystem battlesystem)
    {
        this.turngenerater = turngenerater;
        mapcreate = turngenerater.mapcreate;
        MenuSwitch = false;
        maxx = turngenerater.mapcreate.maxX;
        maxz = turngenerater.mapcreate.maxZ;
        this.unitclick = unitclick;
        this.attackpoint = attackpoint;
        this.battlesystem = battlesystem;


    }
    public void Entry()
    {
        unitclick.UC(this,turngenerater,attackpoint);
        attackmode = AttackMode.None;
        Debug.Log("プレイヤーターン開始");
    }

    public void Update()
    {
        float ma = Input.GetAxis("Horizontal");
        float mb = Input.GetAxis("Vertical");
        Vector3 Move = new Vector3(ma, 0, mb).normalized;
        turngenerater.CameraObject.Translate(Move * speed * Time.deltaTime, Space.World);
        Vector3 camerapos = turngenerater.CameraObject.position;
        float cma = Mathf.Clamp(camerapos.x, 0f, maxx - 10);
        float cmb = Mathf.Clamp(camerapos.z, 0f, maxz - 10);
        camerapos.x = cma;
        camerapos.z = cmb;
        turngenerater.CameraObject.position = camerapos;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
        {
            float camerascroll = Camera.main.fieldOfView - scroll * scrollspeed;
            Camera.main.fieldOfView = Mathf.Clamp(camerascroll, 30f, 90f);
        }
        

        //左クリック処理
        if (Input.GetMouseButtonDown(0))
        {
            if (MenuSwitch == false)
            {
                unitclick.Click1();
            }
            else
            {
                Debug.Log("Click2始動");
                unitclick.Click2();
            }

        }

        //右クリック処理
        if (Input.GetMouseButtonDown(1))
        {
            turngenerater.movegenerater.MoveReset();
            Reset();
        }

        if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            turngenerater.movegenerater.MoveReset();
            Reset();
            turngenerater.ChangeState(new EnemyStart(turngenerater, unitclick,attackpoint, battlesystem));
        }

        if(MenuSwitch == true)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKey(KeyCode.Keypad1))
            {
                turngenerater.movegenerater.MoveReset();
                MP = null;
                attackmode = AttackMode.Normal;
                turngenerater.ChangeState(new PlayerAttack(mapcreate, this, attackmode,attackpoint,turngenerater,unitclick,battlesystem));
            }

            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKey(KeyCode.Keypad2))
            {
                turngenerater.movegenerater.MoveReset();
                MP = null;
                attackmode = AttackMode.Skill;
                turngenerater.ChangeState(new PlayerAttack(mapcreate, this, attackmode,attackpoint,turngenerater,unitclick,battlesystem));
            }
        }
        
    }
    
    public void Exit()
    {

    }

    public void Reset()
    {
        turngenerater.SelectUnit = null;
        Obj = null;
        MP = null;
        MenuSwitch = false;
    }
}
