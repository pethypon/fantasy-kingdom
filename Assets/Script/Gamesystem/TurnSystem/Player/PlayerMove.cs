using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMove : TurnState
{
    public enum AttackMode
    {
        None,
        Normal,
        Skill
    }

    public AttackMode attackmode;
    public bool MenuSwitch;
    public Status Obj;
    public Vector3 ObjP;
    public Status MP;
    public RaycastHit hit;
    public Vector3 oldcell;
    public Vector3 newcell;

    /// <summary>建築モード中かどうか</summary>
    public bool BuildMode => Systems.BuildSystem != null && Systems.BuildSystem.IsActive;

    /// <summary>召喚モード中かどうか</summary>
    public bool SummonMode => Systems.SummonSystem != null && Systems.SummonSystem.IsActive;

    private bool timerWired;
    private int unitCycleIndex = -1;
    private string _lastHintKey = "";

    public PlayerMove(TurnGenerater turn) : base(turn) { }

    public override void Entry()
    {
        Systems.UnitClick.UC(this, Turn, Systems.AttackPoint);
        attackmode = AttackMode.None;

        // タイマーのコールバック接続
        if (Systems.TimerSystem != null && !timerWired)
        {
            timerWired = true;
            Systems.TimerSystem.OnTurnTimeExpired += OnTurnTimeExpired;
            Systems.TimerSystem.OnTotalTimeExpired += OnTotalTimeExpired;
        }

        Debug.Log("プレイヤーターン開始");

        if (Systems.InputHintUI != null)
            Systems.InputHintUI.SetHints(InputHintUI.Hints.PlayerMove);
    }

    public override void Update()
    {
        if (BuildMode)
        {
            SetHintsOnce(InputHintUI.Hints.BuildMode);
            HandleBuildMode();
            HandleTurnEnd();
            return;
        }

        if (SummonMode)
        {
            SetHintsOnce(InputHintUI.Hints.SummonMode);
            HandleSummonMode();
            HandleTurnEnd();
            return;
        }

        SetHintsOnce(InputHintUI.Hints.PlayerMove);

        HandleLeftClick();
        HandleRightClick();
        HandleTurnEnd();
        HandleAttackModeSelect();
        HandleDirectionToggle();
        HandleCameraFocus();
        HandleUnitCycle();
        HandleUndo();
    }

    private void SetHintsOnce(string hints)
    {
        if (_lastHintKey == hints) return;
        _lastHintKey = hints;
        if (Systems.InputHintUI != null)
            Systems.InputHintUI.SetHints(hints);
    }

    public override void Exit()
    {
        if (Systems.BuildSystem != null && Systems.BuildSystem.IsActive)
            Systems.BuildSystem.CancelBuildMode();
        if (Systems.SummonSystem != null && Systems.SummonSystem.IsActive)
            Systems.SummonSystem.CancelSummonMode();

        if (Systems.TimerSystem != null && timerWired)
        {
            Systems.TimerSystem.OnTurnTimeExpired -= OnTurnTimeExpired;
            Systems.TimerSystem.OnTotalTimeExpired -= OnTotalTimeExpired;
            timerWired = false;
        }
    }

    public void Reset()
    {
        Context.SelectUnit = null;
        Obj = null;
        MP = null;
        MenuSwitch = false;
        if (Systems.BuildSystem != null && Systems.BuildSystem.IsActive)
            Systems.BuildSystem.CancelBuildMode();
        if (Systems.SummonSystem != null && Systems.SummonSystem.IsActive)
            Systems.SummonSystem.CancelSummonMode();
    }

    // ---- 建築モード処理 ----
    private void HandleBuildMode()
    {
        if (Systems.BuildSystem == null) return;

        Systems.BuildSystem.UpdateCursor();

        if (Context.LeftClickDown)
        {
            if (Systems.BuildSystem.TryPlace())
                RefreshVision();
        }

        if (Context.RightClickDown)
            Systems.BuildSystem.CancelBuildMode();
    }

    // ---- 召喚モード処理 ----
    private void HandleSummonMode()
    {
        if (Systems.SummonSystem == null) return;

        Systems.SummonSystem.UpdateCursor();

        if (Context.LeftClickDown)
        {
            if (Systems.SummonSystem.TryPlace())
                RefreshVision();
        }

        if (Context.RightClickDown)
            Systems.SummonSystem.CancelSummonMode();
    }

    // ---- Q/Eキー：ユニット方向反転 (N↔S) ----
    private void HandleDirectionToggle()
    {
        if (!Context.ToggleNSDown) return;
        if (Context.SelectUnit == null) return;

        Status unit = Context.SelectUnit;
        unit.direction = (unit.direction == Direction.N) ? Direction.S : Direction.N;

        if (MenuSwitch)
        {
            Systems.MoveGenerator.MoveReset();
            Systems.MoveGenerator.MoveCore(unit, unit.transform.position);
        }

        RefreshVision();

        string dirName = unit.direction == Direction.N ? "北" : "南";
        ToastMessageUI.Show($"向き変更: {dirName}", ToastMessageUI.MessageType.Info);
    }

    // ---- 左クリック ----
    private void HandleLeftClick()
    {
        if (!Context.LeftClickDown) return;

        if (!MenuSwitch)
        {
            Systems.UnitClick.Click1();
        }
        else
        {
            Systems.UnitClick.Click2();
            RefreshVision();
        }
    }

    // ---- 右クリック ----
    private void HandleRightClick()
    {
        if (!Context.RightClickDown) return;

        Systems.MoveGenerator.MoveReset();
        RefreshVision();
        Reset();

        if (Systems.UnitPanelUI != null)
            Systems.UnitPanelUI.Hide();
    }

    // ---- ターン終了 ----
    private void HandleTurnEnd()
    {
        if (!Context.TurnEndDown) return;

        if (Systems.TimerSystem != null)
            Systems.TimerSystem.StopTurn();

        Systems.MoveGenerator.MoveReset();
        RefreshVision();
        Reset();

        if (Systems.UnitPanelUI != null)
            Systems.UnitPanelUI.Hide();

        SpecialAbilitySystem.OnTurnEnd(Systems.UnitSetting.PlayerUnit);

        if (Systems.EconomySystem != null)
            Systems.EconomySystem.ProcessTurn(Team.Player);

        if (Systems.BuildingAttackSystem != null)
            Systems.BuildingAttackSystem.ProcessAttacks(Team.Player);

        Turn.ChangeState(new EnemyStart(Turn));
    }

    // ---- 攻撃モード選択 ----
    private void HandleAttackModeSelect()
    {
        if (!MenuSwitch) return;

        if (Context.SelectNormalDown)
            StartAttack(AttackMode.Normal);
        else if (Context.SelectSkillDown)
            StartAttack(AttackMode.Skill);
    }

    // ---- 攻撃ステートへ遷移 ----
    private void StartAttack(AttackMode mode)
    {
        Systems.MoveGenerator.MoveReset();
        MP = null;
        attackmode = mode;
        Turn.ChangeState(new PlayerAttack(Turn, this, attackmode));
    }

    // ---- タイマー自動ターン終了 ----
    private void OnTurnTimeExpired()
    {
        ToastMessageUI.Show("ターン制限時間終了", ToastMessageUI.MessageType.Warning);
        ForceEndTurn();
    }

    private void OnTotalTimeExpired(GameResult result)
    {
        ToastMessageUI.Show("持ち時間終了", ToastMessageUI.MessageType.Error);
        Systems.MoveGenerator.MoveReset();
        Reset();
        Turn.ChangeState(new GameEndState(Turn, result));
    }

    private void ForceEndTurn()
    {
        Systems.MoveGenerator.MoveReset();
        RefreshVision();
        Reset();

        if (Systems.UnitPanelUI != null)
            Systems.UnitPanelUI.Hide();

        SpecialAbilitySystem.OnTurnEnd(Systems.UnitSetting.PlayerUnit);

        if (Systems.EconomySystem != null)
            Systems.EconomySystem.ProcessTurn(Team.Player);

        if (Systems.BuildingAttackSystem != null)
            Systems.BuildingAttackSystem.ProcessAttacks(Team.Player);

        Turn.ChangeState(new EnemyStart(Turn));
    }

    // ---- カメラフォーカス（Cキー） ----
    private void HandleCameraFocus()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.cKey.wasPressedThisFrame) return;

        Status target = Context.SelectUnit;
        if (target == null || Context.CameraObject == null) return;

        Vector3 pos = Context.CameraObject.position;
        pos.x = target.transform.position.x;
        pos.z = target.transform.position.z;
        Context.CameraObject.position = pos;
    }

    // ---- ユニット巡回（Tabキー） ----
    private void HandleUnitCycle()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.tabKey.wasPressedThisFrame) return;
        if (Systems.UnitSetting == null) return;

        var playerParent = Systems.UnitSetting.PlayerUnit;
        if (playerParent == null) return;

        var units = new System.Collections.Generic.List<Status>();
        foreach (Status s in playerParent.GetComponentsInChildren<Status>())
        {
            if (!s.gameObject.activeSelf) continue;
            if (s.type != Type.Unit) continue;
            if (StatusEffectSystem.IsStunned(s)) continue;
            units.Add(s);
        }

        if (units.Count == 0) return;

        unitCycleIndex = (unitCycleIndex + 1) % units.Count;
        Status next = units[unitCycleIndex];

        Systems.MoveGenerator.MoveReset();
        Obj = next;
        ObjP = next.transform.position;
        Systems.MoveGenerator.MoveCore(next, ObjP);
        Context.SelectUnit = next;
        Context.OldCell = next.transform.position;
        MenuSwitch = true;

        if (Systems.UnitPanelUI != null)
            Systems.UnitPanelUI.Show(next);

        if (Context.CameraObject != null)
        {
            Vector3 camPos = Context.CameraObject.position;
            camPos.x = next.transform.position.x;
            camPos.z = next.transform.position.z;
            Context.CameraObject.position = camPos;
        }
    }

    // ---- 移動取り消し（Zキー） ----
    private void HandleUndo()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.zKey.wasPressedThisFrame) return;
        if (Systems.MoveUndoSystem == null) return;
        if (!Systems.MoveUndoSystem.CanUndo) return;

        Systems.MoveGenerator.MoveReset();
        Systems.MoveUndoSystem.Undo(Turn, this,
            Systems.VisionGenerator, Systems.MapCreate, Systems.CrystalSystem);

        if (Systems.UnitPanelUI != null)
            Systems.UnitPanelUI.Hide();
    }
}
