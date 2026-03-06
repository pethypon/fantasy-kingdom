using UnityEngine;

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
    public VisionGenerater visiongenerater;
    public MoveGererater movegenerater;
    public CrystalSystem crystalsystem;
    public UnitSetting unitset;

    public bool MenuSwitch;
    public Status Obj;
    public Vector3 ObjP;
    public Status MP;
    private int maxx;
    private int maxz;
    public float speed = 10f;
    public float scrollspeed = 5f;
    public RaycastHit hit;
    public Vector3 oldcell;
    public Vector3 newcell;

    public PlayerMove(
        TurnGenerater turngenerater,
        UnitClick unitclick,
        AttackPointt attackpoint,
        BattleSystem battlesystem,
        VisionGenerater visiongenerater,
        MoveGererater movegenerater,
        MapCreate mapcreate,
        CrystalSystem crystalsystem,
        UnitSetting unitset)
    {
        this.turngenerater = turngenerater;
        this.unitclick = unitclick;
        this.attackpoint = attackpoint;
        this.battlesystem = battlesystem;
        this.visiongenerater = visiongenerater;
        this.movegenerater = movegenerater;
        this.mapcreate = mapcreate;
        this.crystalsystem = crystalsystem;
        MenuSwitch = false;
        maxx = turngenerater.mapcreate.maxX;
        maxz = turngenerater.mapcreate.maxZ;
        this.unitset = unitset;
    }

    public void Entry()
    {
        unitclick.UC(this, turngenerater, attackpoint);
        attackmode = AttackMode.None;
        Debug.Log("プレイヤーターン開始");
    }

    public void Update()
    {
        UpdateCameraMove();
        UpdateCameraZoom();
        HandleLeftClick();
        HandleRightClick();
        HandleTurnEnd();
        HandleAttackModeSelect();
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

    // ---- カメラ移動 ----
    private void UpdateCameraMove()
    {
        Vector2 input = turngenerater.MoveInput;
        Vector3 moveDir = new Vector3(input.x, 0f, input.y).normalized;
        turngenerater.CameraObject.Translate(moveDir * speed * Time.deltaTime, Space.World);

        Vector3 pos = turngenerater.CameraObject.position;
        pos.x = Mathf.Clamp(pos.x, 0f, maxx - 10);
        pos.z = Mathf.Clamp(pos.z, 0f, maxz - 10);
        turngenerater.CameraObject.position = pos;
    }

    // ---- カメラズーム（FOV） ----
    private void UpdateCameraZoom()
    {
        float scroll = turngenerater.ScrollInput;
        if (scroll == 0f) return;

        float fov = Camera.main.fieldOfView - scroll * scrollspeed;
        Camera.main.fieldOfView = Mathf.Clamp(fov, 30f, 90f);
    }

    // ---- 左クリック ----
    private void HandleLeftClick()
    {
        if (!turngenerater.LeftClickDown) return;

        if (!MenuSwitch)
        {
            unitclick.Click1();
        }
        else
        {
            Debug.Log("Click2開始");
            unitclick.Click2();
            RefreshVision();
        }
    }

    // ---- 右クリック ----
    private void HandleRightClick()
    {
        if (!turngenerater.RightClickDown) return;

        turngenerater.movegenerater.MoveReset();
        RefreshVision();
        Reset();

        if (turngenerater.unitPanelUI != null)
            turngenerater.unitPanelUI.Hide();
    }

    // ---- ターン終了 ----
    private void HandleTurnEnd()
    {
        if (!turngenerater.TurnEndDown) return;

        turngenerater.movegenerater.MoveReset();
        RefreshVision();
        Reset();

        if (turngenerater.unitPanelUI != null)
            turngenerater.unitPanelUI.Hide();

        turngenerater.ChangeState(new EnemyStart(
            turngenerater, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
    }

    // ---- 攻撃モード選択（メニュー表示のみ） ----
    private void HandleAttackModeSelect()
    {
        if (!MenuSwitch) return;

        if (turngenerater.SelectNormalDown)
        {
            StartAttack(AttackMode.Normal);
        }
        else if (turngenerater.SelectSkillDown)
        {
            StartAttack(AttackMode.Skill);
        }
    }

    // ---- 攻撃ステートへ遷移 ----
    private void StartAttack(AttackMode mode)
    {
        turngenerater.movegenerater.MoveReset();
        MP = null;
        attackmode = mode;
        turngenerater.ChangeState(new PlayerAttack(
            mapcreate, this, attackmode, attackpoint, turngenerater,
            unitclick, battlesystem, visiongenerater, movegenerater, crystalsystem, unitset));
    }

    // ---- 視界更新（VisionPoint のショートハンド） ----
    private void RefreshVision()
    {
        visiongenerater.VisionPoint(mapcreate, movegenerater, crystalsystem);
    }
}
