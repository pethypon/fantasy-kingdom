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
    public VisionGenerater visiongenerater;
    public MoveGererater movegenerater;
    public CrystalSystem crystalsystem;
    public UnitSetting unitset;

    public float Speed;
    public float Scrollspeed;
    public int Maxx;
    public int Maxz;
    public bool AttackSetting;
    public bool AttackSuccess;

    public PlayerAttack(
        MapCreate mapcreate,
        PlayerMove move,
        PlayerMove.AttackMode attackmode,
        AttackPointt attackpoint,
        TurnGenerater turngenerater,
        UnitClick unitclick,
        BattleSystem battlesystem,
        VisionGenerater visiongenerater,
        MoveGererater movegenerater,
        CrystalSystem crystalsystem,
        UnitSetting unitset)
    {
        this.mapcreate = mapcreate;
        this.move = move;
        this.attackmode = attackmode;
        this.attackpoint = attackpoint;
        this.turngenerater = turngenerater;
        this.unitclick = unitclick;
        this.battlesystem = battlesystem;
        this.visiongenerater = visiongenerater; // ���R�[�h�Ŗ�����������o�O���C��
        this.movegenerater = movegenerater;
        this.crystalsystem = crystalsystem;
        this.unitset = unitset;
    }

    public void Entry()
    {
        // 前回の攻撃ポイントが残っていた場合に備えて先に削除
        attackpoint.AtkpDestroy();

        Speed = move.speed;
        Scrollspeed = move.scrollspeed;
        Maxx = mapcreate.maxX;
        Maxz = mapcreate.maxZ;
        attackpoint.AttackPointCall(move.Obj, move.ObjP, move);
        AttackSuccess = false;

        // 攻撃可能な対象が存在しない場合は PlayerMove に戻る
        if (attackpoint.AttackP == null || attackpoint.AttackP.Count == 0)
        {
            Debug.Log("[PlayerAttack] 攻撃可能な対象がありません");
            turngenerater.ChangeState(new PlayerMove(
                turngenerater, unitclick, attackpoint, battlesystem,
                visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
            return;
        }
    }

    public void Update()
    {
        if (AttackSuccess)
        {
            HandleAttackSuccess();
            return;
        }

        UpdateCameraMove();
        UpdateCameraZoom();
        HandleAttackClick();
        HandleCancelAttack();
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

    // ������ �U���������FPlayerMove �֖߂� ����������������������������������������������������
    private void HandleAttackSuccess()
    {
        Reset();
        turngenerater.ChangeState(new PlayerMove(
            turngenerater, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem,unitset
            ));
    }

    // ������ �J�����ړ� ��������������������������������������������������������������������������������������������
    private void UpdateCameraMove()
    {
        Vector2 input = turngenerater.MoveInput;
        Vector3 moveDir = new Vector3(input.x, 0f, input.y).normalized;
        turngenerater.CameraObject.Translate(moveDir * Speed * Time.deltaTime, Space.World);

        Vector3 pos = turngenerater.CameraObject.position;
        pos.x = Mathf.Clamp(pos.x, 0f, Maxx - 10);
        pos.z = Mathf.Clamp(pos.z, 0f, Maxz - 10);
        turngenerater.CameraObject.position = pos;
    }

    // ������ �J�����Y�[���iFOV�j ��������������������������������������������������������������������������
    private void UpdateCameraZoom()
    {
        float scroll = turngenerater.ScrollInput;
        if (scroll == 0f) return;

        float fov = Camera.main.fieldOfView - scroll * Scrollspeed;
        Camera.main.fieldOfView = Mathf.Clamp(fov, 30f, 90f);
    }

    // ������ �U���N���b�N�i���N���b�N�j ������������������������������������������������������������
    private void HandleAttackClick()
    {
        if (!turngenerater.LeftClickDown) return;
        unitclick.AttackClick(battlesystem, this, attackpoint, move);
    }

    // ������ �U���L�����Z���i�E�N���b�N�j�� PlayerMove �֖߂� ����������������
    private void HandleCancelAttack()
    {
        if (!turngenerater.RightClickDown) return;
        Reset();
        turngenerater.ChangeState(new PlayerMove(
            turngenerater, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
    }
}
