using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMove : StateCore
{
    public enum AttackMode
    {
        None,
        Normal,
        Skill
    }

    public AttackMode attackmode;
    private readonly GameContext _ctx;

    public bool MenuSwitch;
    private BuildSystem buildsystem;
    private SummonSystem summonsystem;
    public Status Obj;
    public Vector3 ObjP;
    public Status MP;
    public RaycastHit hit;
    public Vector3 oldcell;
    public Vector3 newcell;

    /// <summary>建築モード中かどうか（BuildSystem.IsActive を参照）</summary>
    public bool BuildMode => buildsystem != null && buildsystem.IsActive;

    /// <summary>召喚モード中かどうか（SummonSystem.IsActive を参照）</summary>
    public bool SummonMode => summonsystem != null && summonsystem.IsActive;

    private bool timerWired;
    private int unitCycleIndex = -1;
    private string _lastHintKey = "";

    public PlayerMove(GameContext ctx)
    {
        _ctx = ctx;
        MenuSwitch = false;
        buildsystem = ctx.TurnGen.buildsystem;
        summonsystem = ctx.TurnGen.summonsystem;
    }

    public void Entry()
    {
        _ctx.UnitClick.UC(this, _ctx.TurnGen, _ctx.AttackPoint);
        attackmode = AttackMode.None;

        // タイマーのコールバック接続
        if (_ctx.TurnGen.timerSystem != null && !timerWired)
        {
            timerWired = true;
            _ctx.TurnGen.timerSystem.OnTurnTimeExpired += OnTurnTimeExpired;
            _ctx.TurnGen.timerSystem.OnTotalTimeExpired += OnTotalTimeExpired;
        }

        Debug.Log("プレイヤーターン開始");

        // InputHintUI を更新
        if (_ctx.TurnGen.inputHintUI != null)
            _ctx.TurnGen.inputHintUI.SetHints(InputHintUI.Hints.PlayerMove);
    }

    public void Update()
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
        if (_ctx.TurnGen.inputHintUI != null)
            _ctx.TurnGen.inputHintUI.SetHints(hints);
    }

    public void Exit()
    {
        if (buildsystem != null && buildsystem.IsActive)
            buildsystem.CancelBuildMode();
        if (summonsystem != null && summonsystem.IsActive)
            summonsystem.CancelSummonMode();

        // タイマーコールバック解除
        if (_ctx.TurnGen.timerSystem != null && timerWired)
        {
            _ctx.TurnGen.timerSystem.OnTurnTimeExpired -= OnTurnTimeExpired;
            _ctx.TurnGen.timerSystem.OnTotalTimeExpired -= OnTotalTimeExpired;
            timerWired = false;
        }
    }

    public void Reset()
    {
        _ctx.TurnGen.SelectUnit = null;
        Obj = null;
        MP = null;
        MenuSwitch = false;
        if (buildsystem != null && buildsystem.IsActive)
            buildsystem.CancelBuildMode();
        if (summonsystem != null && summonsystem.IsActive)
            summonsystem.CancelSummonMode();
    }

    // ---- 建築モード処理 ----
    private void HandleBuildMode()
    {
        if (buildsystem == null) return;

        buildsystem.UpdateCursor();

        // 左クリック: 設置試行
        if (_ctx.TurnGen.LeftClickDown)
        {
            if (buildsystem.TryPlace())
            {
                _ctx.RefreshVision();
            }
        }

        // 右クリック: 建築モード解除
        if (_ctx.TurnGen.RightClickDown)
        {
            buildsystem.CancelBuildMode();
        }
    }

    // ---- 召喚モード処理 ----
    private void HandleSummonMode()
    {
        if (summonsystem == null) return;

        summonsystem.UpdateCursor();

        if (_ctx.TurnGen.LeftClickDown)
        {
            if (summonsystem.TryPlace())
            {
                _ctx.RefreshVision();
            }
        }

        if (_ctx.TurnGen.RightClickDown)
        {
            summonsystem.CancelSummonMode();
        }
    }

    // ---- Q/Eキー：ユニット方向反転 (N↔S) ----
    private void HandleDirectionToggle()
    {
        if (!_ctx.TurnGen.ToggleNSDown) return;
        if (_ctx.TurnGen.SelectUnit == null) return;

        Status unit = _ctx.TurnGen.SelectUnit;
        unit.direction = (unit.direction == Direction.N) ? Direction.S : Direction.N;

        // 方向変更後、移動範囲を再計算（方向依存パターンのため）
        if (MenuSwitch)
        {
            _ctx.MoveGen.MoveReset();
            _ctx.MoveGen.MoveCore(unit, unit.transform.position);
        }

        // 視界も再計算
        _ctx.RefreshVision();

        string dirName = unit.direction == Direction.N ? "北" : "南";
        ToastMessageUI.Show($"向き変更: {dirName}", ToastMessageUI.MessageType.Info);
    }

    // ---- 左クリック ----
    private void HandleLeftClick()
    {
        if (!_ctx.TurnGen.LeftClickDown) return;

        if (!MenuSwitch)
        {
            _ctx.UnitClick.Click1();
        }
        else
        {
            _ctx.UnitClick.Click2();
            _ctx.RefreshVision();
        }
    }

    // ---- 右クリック ----
    private void HandleRightClick()
    {
        if (!_ctx.TurnGen.RightClickDown) return;

        _ctx.MoveGen.MoveReset();
        _ctx.RefreshVision();
        Reset();

        if (_ctx.TurnGen.unitPanelUI != null)
            _ctx.TurnGen.unitPanelUI.Hide();
    }

    // ---- ターン終了（通常＋タイマー強制の共通処理） ----
    private void EndPlayerTurn()
    {
        // タイマー停止
        if (_ctx.TurnGen.timerSystem != null)
            _ctx.TurnGen.timerSystem.StopTurn();

        _ctx.MoveGen.MoveReset();
        _ctx.RefreshVision();
        Reset();

        if (_ctx.TurnGen.unitPanelUI != null)
            _ctx.TurnGen.unitPanelUI.Hide();

        // Player の Special Ability + 資源獲得＋建築物攻撃
        _ctx.ProcessTurnEnd(Team.Player);

        _ctx.TurnGen.ChangeState(new EnemyStart(_ctx));
    }

    // ---- ターン終了キー ----
    private void HandleTurnEnd()
    {
        if (!_ctx.TurnGen.TurnEndDown) return;
        EndPlayerTurn();
    }

    // ---- 攻撃モード選択（メニュー表示のみ） ----
    private void HandleAttackModeSelect()
    {
        if (!MenuSwitch) return;

        if (_ctx.TurnGen.SelectNormalDown)
        {
            StartAttack(AttackMode.Normal);
        }
        else if (_ctx.TurnGen.SelectSkillDown)
        {
            StartAttack(AttackMode.Skill);
        }
    }

    // ---- 攻撃ステートへ遷移 ----
    private void StartAttack(AttackMode mode)
    {
        _ctx.MoveGen.MoveReset();
        MP = null;
        attackmode = mode;
        _ctx.TurnGen.ChangeState(new PlayerAttack(_ctx, this, attackmode));
    }

    // ---- タイマー自動ターン終了 ----
    private void OnTurnTimeExpired()
    {
        ToastMessageUI.Show("ターン制限時間終了", ToastMessageUI.MessageType.Warning);
        EndPlayerTurn();
    }

    // ---- 持ち時間切れ → ゲーム終了 ----
    private void OnTotalTimeExpired(GameResult result)
    {
        ToastMessageUI.Show("持ち時間終了", ToastMessageUI.MessageType.Error);
        _ctx.MoveGen.MoveReset();
        Reset();
        _ctx.TurnGen.ChangeState(new GameEndState(_ctx.TurnGen, result));
    }

    // ---- カメラフォーカス（Cキー）: 選択ユニットにカメラを向ける ----
    private void HandleCameraFocus()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.cKey.wasPressedThisFrame) return;

        Status target = _ctx.TurnGen.SelectUnit;
        if (target == null) return;

        Vector3 pos = _ctx.TurnGen.CameraObject.position;
        pos.x = target.transform.position.x;
        pos.z = target.transform.position.z;
        _ctx.TurnGen.CameraObject.position = pos;
    }

    // ---- ユニット巡回（Tabキー）: 味方ユニットを順に選択＆カメラ追従 ----
    private void HandleUnitCycle()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.tabKey.wasPressedThisFrame) return;
        if (_ctx.UnitSetting == null) return;

        // 行動可能な味方ユニットを収集
        var playerParent = _ctx.UnitSetting.PlayerUnit;
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

        // 次のユニットに切り替え
        unitCycleIndex = (unitCycleIndex + 1) % units.Count;
        Status next = units[unitCycleIndex];

        // 既存選択をリセットして新ユニットを選択
        _ctx.MoveGen.MoveReset();
        Obj = next;
        ObjP = next.transform.position;
        _ctx.MoveGen.MoveCore(next, ObjP);
        _ctx.TurnGen.SelectUnit = next;
        _ctx.TurnGen.OldCell = next.transform.position;
        MenuSwitch = true;

        if (_ctx.TurnGen.unitPanelUI != null)
            _ctx.TurnGen.unitPanelUI.Show(next);

        // カメラ追従
        Vector3 camPos = _ctx.TurnGen.CameraObject.position;
        camPos.x = next.transform.position.x;
        camPos.z = next.transform.position.z;
        _ctx.TurnGen.CameraObject.position = camPos;
    }

    // ---- 移動取り消し（Zキー） ----
    private void HandleUndo()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.zKey.wasPressedThisFrame) return;
        if (_ctx.TurnGen.moveUndoSystem == null) return;
        if (!_ctx.TurnGen.moveUndoSystem.CanUndo) return;

        _ctx.MoveGen.MoveReset();
        _ctx.TurnGen.moveUndoSystem.Undo(_ctx.TurnGen, this, _ctx.VisionGen, _ctx.MapCreate, _ctx.CrystalSystem);

        if (_ctx.TurnGen.unitPanelUI != null)
            _ctx.TurnGen.unitPanelUI.Hide();
    }
}
