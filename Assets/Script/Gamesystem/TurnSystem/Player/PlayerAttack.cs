using UnityEngine;

public partial class PlayerAttack : TurnState
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
        if (attackmode == PlayerMove.AttackMode.Skill && SpecialSkillRuntime.Definition(move.SelectedUnit)?.ability > UnitSpecialSkill.None)
            BeginFixedSkillTargeting();
        else
        {
            Systems.AttackGenerator.AttackPointCall(move.SelectedUnit, move.SelectedUnitPosition, move);
            if (attackmode == PlayerMove.AttackMode.Skill) BeginLegacySkillPreview();
        }
        AttackSuccess = false;

        // 攻撃モード中もタイマー切れで強制ターン終了できるよう購読する
        // （PlayerMove は Exit 時に購読解除するため、ここで引き継がないとタイマーが無効化される）
        if (Systems.TimerSystem != null)
        {
            Systems.TimerSystem.OnTurnTimeExpired += OnTurnTimeExpired;
            Systems.TimerSystem.OnTotalTimeExpired += OnTotalTimeExpired;
        }

        if (Systems.DamagePreviewUI != null && !fixedSkillMode)
            Systems.DamagePreviewUI.Activate();

        if (Systems.InputHintUI != null && !fixedSkillMode && !legacySkillPreview)
            Systems.InputHintUI.SetHints(InputHintUI.Hints.PlayerAttack);

        // 対象がいなくても攻撃モードを維持し、取消で選択へ戻れるようにする。
        if (Systems.AttackGenerator.AttackP == null || Systems.AttackGenerator.AttackP.Count == 0)
        {
            ToastMessageUI.Show("攻撃範囲内に対象がいません", ToastMessageUI.MessageType.Warning);
            return;
        }
    }

    public override void Update()
    {
        if (Context.TurnEndDown)
        {
            Reset();
            move.ExecuteTurnEnd();
            return;
        }
        if (AttackSuccess)
        {
            HandleAttackSuccess();
            return;
        }

        if (Context.SelectNormalDown || Context.SelectSkillDown)
        {
            var profile = BoardActionProfile.For(move.SelectedUnit);
            if (profile != null && (Context.SelectSkillDown ? !profile.CanUseSkills : !profile.CanAttack))
            { ToastMessageUI.Show("この駒の行動設定では使用できません", ToastMessageUI.MessageType.Warning); return; }
            if (Context.SelectSkillDown && BeginSpecialTargeting(move.SelectedUnit, Systems)) return;
            Reset();
            move.CurrentAttackMode = Context.SelectSkillDown ? PlayerMove.AttackMode.Skill : PlayerMove.AttackMode.Normal;
            Turn.ChangeState(new PlayerAttack(Turn, move, move.CurrentAttackMode));
            return;
        }
        if (fixedSkillMode) { HandleFixedSkillTargeting(); HandleCancelAttack(); return; }
        TickLegacySkillPreview();
        HandleAttackClick();
        HandleCancelAttack();
    }

    public override void Exit()
    {
        EndFixedSkillTargeting();
        if (Systems.TimerSystem != null)
        {
            Systems.TimerSystem.OnTurnTimeExpired -= OnTurnTimeExpired;
            Systems.TimerSystem.OnTotalTimeExpired -= OnTotalTimeExpired;
        }

        if (Systems.DamagePreviewUI != null)
            Systems.DamagePreviewUI.Hide();
    }

    // ---- タイマー自動ターン終了（攻撃モード中） ----
    private void OnTurnTimeExpired()
    {
        ToastMessageUI.Show("ターン制限時間終了", ToastMessageUI.MessageType.Warning);
        Reset();
        move.ExecuteTurnEnd();
    }

    private void OnTotalTimeExpired(GameResult result)
    {
        ToastMessageUI.Show("持ち時間終了", ToastMessageUI.MessageType.Error);
        Reset();
        Turn.ChangeState(new GameEndState(Turn, result));
    }

    public void Reset()
    {
        Systems.AttackGenerator.TargetUnit = null;
        Systems.UnitClick.AttackTarget = null;
        Systems.BattleSystem.SetTarget(null);
        AttackSuccess = false;
        Systems.AttackGenerator.AtkpDestroy();
    }

    private void HandleAttackSuccess()
    {
        var prevUnit = move.SelectedUnit;
        Reset();

        // 攻撃成功後、生存しているユニットを再選択状態で PlayerMove へ戻す。
        // 同一ターン中にさらに移動・スキルを使えるよう移動範囲とパネルを復元する。
        if (prevUnit != null && prevUnit.IsAlive)
        {
            move.SelectedUnit = prevUnit;
            move.SelectedUnitPosition = prevUnit.transform.position;
            move.MenuSwitch = true;
        }
        else move.Reset();

        Turn.ChangeState(move);

        if (prevUnit != null && prevUnit.IsAlive)
        {
            Systems.MoveGenerator.MoveCore(prevUnit, prevUnit.transform.position);
            Systems.UnitPanelUI?.Show(prevUnit);
        }
    }

    private void HandleAttackClick()
    {
        if (!Context.LeftClickDown) return;
        var profile = BoardActionProfile.For(move.SelectedUnit);
        if (profile != null && (attackmode == PlayerMove.AttackMode.Skill ? !profile.CanUseSkills : !profile.CanAttack)) return;

        if (attackmode == PlayerMove.AttackMode.Skill && BeginSpecialTargeting(move.SelectedUnit, Systems)) return;

        Systems.UnitClick.AttackClick(Systems.BattleSystem, this, Systems.AttackGenerator, move);
    }

    private void HandleCancelAttack()
    {
        if (!Context.RightClickDown) return;
        Reset();
        Turn.ChangeState(move);
        if (move.SelectedUnit != null && move.SelectedUnit.IsAlive)
        {
            Systems.MoveGenerator.MoveCore(move.SelectedUnit, move.SelectedUnit.transform.position);
            Systems.UnitPanelUI?.Show(move.SelectedUnit);
        }
    }
}
