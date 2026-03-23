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
        this.visiongenerater = visiongenerater; // 旧コードで未代入だったバグ修正
        this.movegenerater = movegenerater;
        this.crystalsystem = crystalsystem;
        this.unitset = unitset;
    }

    public void Entry()
    {
        Speed = move.speed;
        Scrollspeed = move.scrollspeed;
        Maxx = mapcreate.maxX;
        Maxz = mapcreate.maxZ;
        attackpoint.AttackPointCall(move.Obj, move.ObjP, move);
        AttackSuccess = false;

        // DamagePreviewUI を有効化
        if (turngenerater.damagePreviewUI != null)
            turngenerater.damagePreviewUI.Activate();

        // InputHintUI を攻撃モード用に更新
        if (turngenerater.inputHintUI != null)
            turngenerater.inputHintUI.SetHints(InputHintUI.Hints.PlayerAttack);
        // 攻撃範囲内に敵がいない場合は即座にPlayerMoveへ戻る
        if (attackpoint.AttackP == null || attackpoint.AttackP.Count == 0)
        {
            ToastMessageUI.Show("攻撃範囲内に対象がいません", ToastMessageUI.MessageType.Warning);
            attackpoint.AtkpDestroy();
            turngenerater.ChangeState(new PlayerMove(
                turngenerater, unitclick, attackpoint, battlesystem,
                visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
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
        if (turngenerater.damagePreviewUI != null)
            turngenerater.damagePreviewUI.Hide();
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

    // ==== 攻撃成功時：PlayerMove へ戻る ====
    private void HandleAttackSuccess()
    {
        Reset();
        turngenerater.ChangeState(new PlayerMove(
            turngenerater, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem,unitset
            ));
    }

    // ==== カメラ移動 ====
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

    // ==== カメラズーム（FOV） ====
    private void UpdateCameraZoom()
    {
        float scroll = turngenerater.ScrollInput;
        if (scroll == 0f) return;

        float fov = Camera.main.fieldOfView - scroll * Scrollspeed;
        Camera.main.fieldOfView = Mathf.Clamp(fov, GameConstants.CameraFOVMin, GameConstants.CameraFOVMax);
    }

    // ==== 攻撃クリック（左クリック） ====
    private void HandleAttackClick()
    {
        if (!turngenerater.LeftClickDown) return;

        // スキルモードで自身対象スキルの場合は即実行
        if (attackmode == PlayerMove.AttackMode.Skill
            && move.Obj != null
            && move.Obj.AssignedSkillId >= 0
            && SkillData.Table.ContainsKey(move.Obj.AssignedSkillId))
        {
            SkillData skill = SkillData.Table[move.Obj.AssignedSkillId];
            if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea)
            {
                // APチェック
                if (turngenerater.apsystem.GetAP(Team.Player) < skill.APCost)
                {
                    ToastMessageUI.Show("AP不足：スキルを使用できません", ToastMessageUI.MessageType.Warning);
                    return;
                }

                if (skill.Target == SkillTarget.SelfArea)
                {
                    // 範囲支援スキル: 周囲の味方にバフ/回復
                    ExecuteSelfAreaSkill(move.Obj, skill);
                }
                else
                {
                    turngenerater.skillsystem.ExecuteSkill(move.Obj, move.Obj, skill);
                }

                turngenerater.apsystem.ConsumeSkill(Team.Player, skill.APCost, move.Obj);
                AttackSuccess = true;
                return;
            }
        }

        unitclick.AttackClick(battlesystem, this, attackpoint, move);
    }

    // ==== 自身中心の範囲スキル実行 ====
    private void ExecuteSelfAreaSkill(Status caster, SkillData skill)
    {
        if (turngenerater.skillsystem == null) return;

        Vector3Int center = new Vector3Int(
            Mathf.RoundToInt(caster.transform.position.x),
            0,
            Mathf.RoundToInt(caster.transform.position.z));

        var positions = SkillSystem.GetAreaPositions(skill.Area, center, caster.direction);
        var posSet = new System.Collections.Generic.HashSet<Vector3Int>(positions);

        // 味方ユニットを収集
        var allies = new System.Collections.Generic.List<Status>();
        Transform parent = turngenerater.unitset.PlayerUnit;
        foreach (Status s in parent.GetComponentsInChildren<Status>())
        {
            if (s.type != Type.Unit) continue;
            Vector3Int cell = new Vector3Int(
                Mathf.RoundToInt(s.transform.position.x), 0,
                Mathf.RoundToInt(s.transform.position.z));
            if (posSet.Contains(cell))
                allies.Add(s);
        }

        // ラストシグナル特殊処理
        if (skill.SpecialEffect == "LastSignal")
        {
            foreach (Status ally in allies)
            {
                StatusEffectSystem.ApplyBuff(ally, BuffType.Offensive);
                // AP+2 は FactionState 経由
            }
            Debug.Log($"[SkillSystem] ラストシグナル: 範囲内味方 {allies.Count}体に攻勢付与");
        }
        else
        {
            turngenerater.skillsystem.ExecuteAreaSupportSkill(caster, skill, allies);
        }
    }

    // ==== 攻撃キャンセル（右クリック）→ PlayerMove へ戻る ====
    private void HandleCancelAttack()
    {
        if (!turngenerater.RightClickDown) return;
        Reset();
        turngenerater.ChangeState(new PlayerMove(
            turngenerater, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
    }
}
