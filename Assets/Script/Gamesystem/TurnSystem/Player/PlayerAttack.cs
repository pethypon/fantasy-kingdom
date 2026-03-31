using UnityEngine;

public class PlayerAttack : StateCore
{
    private readonly GameContext _ctx;
    public PlayerMove move;
    public PlayerMove.AttackMode attackmode;
    public Status Attack;

    public bool AttackSetting;
    public bool AttackSuccess;

    public PlayerAttack(
        GameContext ctx,
        PlayerMove move,
        PlayerMove.AttackMode attackmode)
    {
        _ctx = ctx;
        this.move = move;
        this.attackmode = attackmode;
    }

    public void Entry()
    {
        _ctx.AttackPoint.AttackPointCall(move.Obj, move.ObjP, move);
        AttackSuccess = false;

        // DamagePreviewUI を有効化
        if (_ctx.TurnGen.damagePreviewUI != null)
            _ctx.TurnGen.damagePreviewUI.Activate();

        // InputHintUI を攻撃モード用に更新
        if (_ctx.TurnGen.inputHintUI != null)
            _ctx.TurnGen.inputHintUI.SetHints(InputHintUI.Hints.PlayerAttack);
        // 攻撃範囲内に敵がいない場合は即座にPlayerMoveへ戻る
        if (_ctx.AttackPoint.AttackP == null || _ctx.AttackPoint.AttackP.Count == 0)
        {
            ToastMessageUI.Show("攻撃範囲内に対象がいません", ToastMessageUI.MessageType.Warning);
            _ctx.AttackPoint.AtkpDestroy();
            _ctx.TurnGen.ChangeState(new PlayerMove(_ctx));
        }

    }

    public void Update()
    {
        if (AttackSuccess)
        {
            HandleAttackSuccess();
            return;
        }

        HandleAttackClick();
        HandleCancelAttack();
    }

    public void Exit()
    {
        if (_ctx.TurnGen.damagePreviewUI != null)
            _ctx.TurnGen.damagePreviewUI.Hide();
    }

    public void Reset()
    {
        _ctx.AttackPoint.obj = null;
        _ctx.UnitClick.ATKC = null;
        _ctx.BattleSystem.target = null;
        _ctx.BattleSystem.AttackSide = null;
        AttackSuccess = false;
        _ctx.AttackPoint.AtkpDestroy();
    }

    // ==== 攻撃成功時：PlayerMove へ戻る ====
    private void HandleAttackSuccess()
    {
        Reset();
        _ctx.TurnGen.ChangeState(new PlayerMove(_ctx));
    }

    // ==== 攻撃クリック（左クリック） ====
    private void HandleAttackClick()
    {
        if (!_ctx.TurnGen.LeftClickDown) return;

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
                if (_ctx.TurnGen.apsystem.GetAP(Team.Player) < skill.APCost)
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
                    _ctx.TurnGen.skillsystem.ExecuteSkill(move.Obj, move.Obj, skill);
                }

                _ctx.TurnGen.apsystem.ConsumeSkill(Team.Player, skill.APCost, move.Obj);
                AttackSuccess = true;
                return;
            }
        }

        _ctx.UnitClick.AttackClick(_ctx.BattleSystem, this, _ctx.AttackPoint, move);
    }

    // ==== 自身中心の範囲スキル実行 ====
    private void ExecuteSelfAreaSkill(Status caster, SkillData skill)
    {
        if (_ctx.TurnGen.skillsystem == null) return;

        Vector3Int center = new Vector3Int(
            Mathf.RoundToInt(caster.transform.position.x),
            0,
            Mathf.RoundToInt(caster.transform.position.z));

        var positions = SkillSystem.GetAreaPositions(skill.Area, center, caster.direction);
        var posSet = new System.Collections.Generic.HashSet<Vector3Int>(positions);

        // 味方ユニットを収集
        var allies = new System.Collections.Generic.List<Status>();
        Transform parent = _ctx.UnitSetting.PlayerUnit;
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
            }
            Debug.Log($"[SkillSystem] ラストシグナル: 範囲内味方 {allies.Count}体に攻勢付与");
        }
        else
        {
            _ctx.TurnGen.skillsystem.ExecuteAreaSupportSkill(caster, skill, allies);
        }
    }

    // ==== 攻撃キャンセル（右クリック）→ PlayerMove へ戻る ====
    private void HandleCancelAttack()
    {
        if (!_ctx.TurnGen.RightClickDown) return;
        Reset();
        _ctx.TurnGen.ChangeState(new PlayerMove(_ctx));
    }
}
