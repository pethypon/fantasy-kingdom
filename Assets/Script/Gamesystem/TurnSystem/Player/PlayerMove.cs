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
    private BuildSystem buildsystem;
    private SummonSystem summonsystem;
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

    /// <summary>建築モード中かどうか（BuildSystem.IsActive を参照）</summary>
    public bool BuildMode => buildsystem != null && buildsystem.IsActive;

    /// <summary>召喚モード中かどうか（SummonSystem.IsActive を参照）</summary>
    public bool SummonMode => summonsystem != null && summonsystem.IsActive;

    private bool timerWired;

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
    }

    public void Update()
    {
        UpdateCameraMove();
        UpdateCameraZoom();

        if (BuildMode)
        {
            HandleBuildMode();
            HandleTurnEnd();
            return;
        }

        if (SummonMode)
        {
            HandleSummonMode();
            HandleTurnEnd();
            return;
        }

        HandleLeftClick();
        HandleRightClick();
        HandleTurnEnd();
        HandleAttackModeSelect();
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
        Debug.Log("[PlayerMove] 1ターン制限時間終了 → 自動ターン切り替え");
        ForceEndTurn();
    }

    // ---- 持ち時間切れ → ゲーム終了 ----
    private void OnTotalTimeExpired(GameResult result)
    {
        Debug.Log($"[PlayerMove] 持ち時間終了 → ゲーム終了: {result}");
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

    // ---- 視界更新（VisionPoint のショートハンド） ----
    private void RefreshVision()
    {
        visiongenerater.VisionPoint(mapcreate, movegenerater, crystalsystem);
    }
}
