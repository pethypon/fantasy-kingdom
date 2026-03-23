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
        this.unitset = unitset;
        this.buildsystem = turngenerater.buildsystem;
        this.summonsystem = turngenerater.summonsystem;
    }

    public void Entry()
    {
        unitclick.UC(this, turngenerater, attackpoint);
        attackmode = AttackMode.None;

        // タイマーのコールバック接続
        if (turngenerater.timerSystem != null && !timerWired)
        {
            timerWired = true;
            turngenerater.timerSystem.OnTurnTimeExpired += OnTurnTimeExpired;
            turngenerater.timerSystem.OnTotalTimeExpired += OnTotalTimeExpired;
        }

        Debug.Log("プレイヤーターン開始");

        // InputHintUI を更新
        if (turngenerater.inputHintUI != null)
            turngenerater.inputHintUI.SetHints(InputHintUI.Hints.PlayerMove);
    }

    public void Update()
    {
        if (BuildMode)
        {
            // 建築モード用ヒントに切り替え
            if (turngenerater.inputHintUI != null)
                turngenerater.inputHintUI.SetHints(InputHintUI.Hints.BuildMode);
            HandleBuildMode();
            HandleTurnEnd();
            return;
        }

        if (SummonMode)
        {
            // 召喚モード用ヒントに切り替え
            if (turngenerater.inputHintUI != null)
                turngenerater.inputHintUI.SetHints(InputHintUI.Hints.SummonMode);
            HandleSummonMode();
            HandleTurnEnd();
            return;
        }

        // 通常モード用ヒントに戻す（Build/Summonから戻った時用）
        if (turngenerater.inputHintUI != null)
            turngenerater.inputHintUI.SetHints(InputHintUI.Hints.PlayerMove);

        HandleLeftClick();
        HandleRightClick();
        HandleTurnEnd();
        HandleAttackModeSelect();
        HandleDirectionToggle();
        HandleCameraFocus();
        HandleUnitCycle();
    }

    public void Exit()
    {
        if (buildsystem != null && buildsystem.IsActive)
            buildsystem.CancelBuildMode();
        if (summonsystem != null && summonsystem.IsActive)
            summonsystem.CancelSummonMode();

        // タイマーコールバック解除
        if (turngenerater.timerSystem != null && timerWired)
        {
            turngenerater.timerSystem.OnTurnTimeExpired -= OnTurnTimeExpired;
            turngenerater.timerSystem.OnTotalTimeExpired -= OnTotalTimeExpired;
            timerWired = false;
        }
    }

    public void Reset()
    {
        turngenerater.SelectUnit = null;
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
        if (turngenerater.LeftClickDown)
        {
            if (buildsystem.TryPlace())
            {
                RefreshVision();
            }
        }

        // 右クリック: 建築モード解除
        if (turngenerater.RightClickDown)
        {
            buildsystem.CancelBuildMode();
        }
    }

    // ---- 召喚モード処理 ----
    private void HandleSummonMode()
    {
        if (summonsystem == null) return;

        summonsystem.UpdateCursor();

        if (turngenerater.LeftClickDown)
        {
            if (summonsystem.TryPlace())
            {
                RefreshVision();
            }
        }

        if (turngenerater.RightClickDown)
        {
            summonsystem.CancelSummonMode();
        }
    }

    // ---- Q/Eキー：ユニット方向反転 (N↔S) ----
    private void HandleDirectionToggle()
    {
        if (!turngenerater.ToggleNSDown) return;
        if (turngenerater.SelectUnit == null) return;

        Status unit = turngenerater.SelectUnit;
        unit.direction = (unit.direction == Direction.N) ? Direction.S : Direction.N;

        // 方向変更後、移動範囲を再計算（方向依存パターンのため）
        if (MenuSwitch)
        {
            turngenerater.movegenerater.MoveReset();
            turngenerater.movegenerater.MoveCore(unit, unit.transform.position);
        }

        // 視界も再計算
        RefreshVision();

        string dirName = unit.direction == Direction.N ? "北" : "南";
        ToastMessageUI.Show($"向き変更: {dirName}", ToastMessageUI.MessageType.Info);
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
            // Click2開始
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

        // タイマー停止
        if (turngenerater.timerSystem != null)
            turngenerater.timerSystem.StopTurn();

        turngenerater.movegenerater.MoveReset();
        RefreshVision();
        Reset();

        if (turngenerater.unitPanelUI != null)
            turngenerater.unitPanelUI.Hide();

        // Player の資源獲得（ターン終了時）
        if (turngenerater.economysystem != null)
            turngenerater.economysystem.ProcessTurn(Team.Player);

        // Player の攻撃建築物による自動攻撃
        if (turngenerater.buildingAttackSystem != null)
            turngenerater.buildingAttackSystem.ProcessAttacks(Team.Player);

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

    // ---- タイマー自動ターン終了 ----
    private void OnTurnTimeExpired()
    {
        ToastMessageUI.Show("ターン制限時間終了", ToastMessageUI.MessageType.Warning);
        ForceEndTurn();
    }

    // ---- 持ち時間切れ → ゲーム終了 ----
    private void OnTotalTimeExpired(GameResult result)
    {
        ToastMessageUI.Show("持ち時間終了", ToastMessageUI.MessageType.Error);
        turngenerater.movegenerater.MoveReset();
        Reset();
        turngenerater.ChangeState(new GameEndState(turngenerater, result));
    }

    // ---- 強制ターン終了（タイマー切れ） ----
    private void ForceEndTurn()
    {
        turngenerater.movegenerater.MoveReset();
        RefreshVision();
        Reset();

        if (turngenerater.unitPanelUI != null)
            turngenerater.unitPanelUI.Hide();

        if (turngenerater.economysystem != null)
            turngenerater.economysystem.ProcessTurn(Team.Player);

        if (turngenerater.buildingAttackSystem != null)
            turngenerater.buildingAttackSystem.ProcessAttacks(Team.Player);

        turngenerater.ChangeState(new EnemyStart(
            turngenerater, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
    }

    // ---- カメラフォーカス（Cキー）: 選択ユニットにカメラを向ける ----
    private void HandleCameraFocus()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.cKey.wasPressedThisFrame) return;

        Status target = turngenerater.SelectUnit;
        if (target == null) return;

        Vector3 pos = turngenerater.CameraObject.position;
        pos.x = target.transform.position.x;
        pos.z = target.transform.position.z;
        turngenerater.CameraObject.position = pos;
    }

    // ---- ユニット巡回（Tabキー）: 味方ユニットを順に選択＆カメラ追従 ----
    private void HandleUnitCycle()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.tabKey.wasPressedThisFrame) return;
        if (unitset == null) return;

        // 行動可能な味方ユニットを収集
        var playerParent = unitset.PlayerUnit;
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
        turngenerater.movegenerater.MoveReset();
        Obj = next;
        ObjP = next.transform.position;
        turngenerater.movegenerater.MoveCore(next, ObjP);
        turngenerater.SelectUnit = next;
        turngenerater.OldCell = next.transform.position;
        MenuSwitch = true;

        if (turngenerater.unitPanelUI != null)
            turngenerater.unitPanelUI.Show(next);

        // カメラ追従
        Vector3 camPos = turngenerater.CameraObject.position;
        camPos.x = next.transform.position.x;
        camPos.z = next.transform.position.z;
        turngenerater.CameraObject.position = camPos;
    }

    // ---- 視界更新（VisionPoint のショートハンド） ----
    private void RefreshVision()
    {
        visiongenerater.VisionPoint(mapcreate, movegenerater, crystalsystem);
    }
}
