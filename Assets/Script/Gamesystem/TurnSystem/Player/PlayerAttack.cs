using UnityEngine;

public class PlayerAttack : TurnState
{
    public PlayerMove move;
    public PlayerMove.AttackMode attackmode;

    public bool AttackSetting;
    public bool AttackSuccess;

    public PlayerAttack(TurnGenerator turn, PlayerMove move, PlayerMove.AttackMode attackmode)
        : base(turn)
    {
        this.move = move;
        this.attackmode = attackmode;
    }

    public override void Entry()
    {
        Systems.AttackGenerator.AttackPointCall(move.Obj, move.ObjP, move);
        AttackSuccess = false;

        if (Systems.DamagePreviewUI != null)
            Systems.DamagePreviewUI.Activate();

        if (Systems.InputHintUI != null)
            Systems.InputHintUI.SetHints(InputHintUI.Hints.PlayerAttack);

        // 攻撃範囲内に敵がいない場合は即座にPlayerMoveへ戻る
        if (Systems.AttackGenerator.AttackP == null || Systems.AttackGenerator.AttackP.Count == 0)
        {
            ToastMessageUI.Show("攻撃範囲内に対象がいません", ToastMessageUI.MessageType.Warning);
            Systems.AttackGenerator.AtkpDestroy();
            Turn.ChangeState(new PlayerMove(Turn));
        }
    }

    public override void Update()
    {
        if (AttackSuccess)
        {
            HandleAttackSuccess();
            return;
        }

        HandleAttackClick();
        HandleCancelAttack();
    }

    public override void Exit()
    {
        if (Systems.DamagePreviewUI != null)
            Systems.DamagePreviewUI.Hide();
    }

    public void Reset()
    {
        Systems.AttackGenerator.obj = null;
        Systems.UnitClick.ATKC = null;
        Systems.BattleSystem.target = null;
        Systems.BattleSystem.AttackSide = null;
        AttackSuccess = false;
        Systems.AttackGenerator.AtkpDestroy();
    }

    private void HandleAttackSuccess()
    {
        Reset();
        Turn.ChangeState(new PlayerMove(Turn));
    }

    private void HandleAttackClick()
    {
        if (!Context.LeftClickDown) return;

        // スキルモードで自身対象スキルの場合は即実行
        if (attackmode == PlayerMove.AttackMode.Skill
            && move.Obj != null
            && move.Obj.AssignedSkillId >= 0
            && SkillData.Table.ContainsKey(move.Obj.AssignedSkillId))
        {
            SkillData skill = SkillData.Table[move.Obj.AssignedSkillId];
            if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea)
            {
                if (Systems.APSystem.GetAP(Team.Player) < skill.APCost)
                {
                    ToastMessageUI.Show("AP不足：スキルを使用できません", ToastMessageUI.MessageType.Warning);
                    return;
                }

                if (skill.Target == SkillTarget.SelfArea)
                    ExecuteSelfAreaSkill(move.Obj, skill);
                else
                    Systems.SkillSystem.ExecuteSkill(move.Obj, move.Obj, skill);

                Systems.APSystem.ConsumeSkill(Team.Player, skill.APCost, move.Obj);
                AttackSuccess = true;
                return;
            }
        }

        Systems.UnitClick.AttackClick(Systems.BattleSystem, this, Systems.AttackGenerator, move);
    }

    private void ExecuteSelfAreaSkill(Status caster, SkillData skill)
    {
        if (Systems.SkillSystem == null) return;

        Vector3Int center = new Vector3Int(
            Mathf.RoundToInt(caster.transform.position.x),
            0,
            Mathf.RoundToInt(caster.transform.position.z));

        var positions = SkillSystem.GetAreaPositions(skill.Area, center, caster.direction);
        var posSet = new System.Collections.Generic.HashSet<Vector3Int>(positions);

        var allies = new System.Collections.Generic.List<Status>();
        Transform parent = Systems.UnitSetting.PlayerUnit;
        foreach (Status s in parent.GetComponentsInChildren<Status>())
        {
            if (s.type != Type.Unit) continue;
            Vector3Int cell = new Vector3Int(
                Mathf.RoundToInt(s.transform.position.x), 0,
                Mathf.RoundToInt(s.transform.position.z));
            if (posSet.Contains(cell))
                allies.Add(s);
        }

        if (skill.SpecialEffect == "LastSignal")
        {
            foreach (Status ally in allies)
                StatusEffectSystem.ApplyBuff(ally, BuffType.Offensive);
            Debug.Log($"[SkillSystem] ラストシグナル: 範囲内味方 {allies.Count}体に攻勢付与");
        }
        else
        {
            Systems.SkillSystem.ExecuteAreaSupportSkill(caster, skill, allies);
        }
    }

    private void HandleCancelAttack()
    {
        if (!Context.RightClickDown) return;
        Reset();
        Turn.ChangeState(new PlayerMove(Turn));
    }
}
