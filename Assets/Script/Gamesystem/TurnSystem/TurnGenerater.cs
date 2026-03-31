using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ゲーム全体の中央ハブ。ステートマシン駆動、入力管理、サブシステム参照を保持する。
/// GameSystems / GameContext を内部保持し、各ステートへ統一的にアクセスを提供する。
/// </summary>
public class TurnGenerater : MonoBehaviour
{
    // ================================================================
    //  集約コンテナ（新アーキテクチャ）
    // ================================================================
    public GameSystems Systems { get; private set; } = new GameSystems();
    public GameContext Context { get; private set; } = new GameContext();

    // ================================================================
    //  ステート管理
    // ================================================================
    private StateCore _stateManager;
    public StateCore CurrentState => _stateManager;

    // ================================================================
    //  後方互換プロパティ（既存コードとの橋渡し）
    //  各フィールドは GameSystems / GameContext に委譲する。
    // ================================================================

    // --- ターン ---
    public int Turn
    {
        get => Context.Turn;
        set => Context.Turn = value;
    }

    // --- 選択ユニット ---
    public Status SelectUnit
    {
        get => Context.SelectUnit;
        set => Context.SelectUnit = value;
    }
    public Vector3 OldCell
    {
        get => Context.OldCell;
        set => Context.OldCell = value;
    }
    public Vector3 NewCell
    {
        get => Context.NewCell;
        set => Context.NewCell = value;
    }

    // --- マップ・ユニット基盤 ---
    [Header("マップ・ユニット基盤")]
    public MapCreate mapcreate
    {
        get => Systems.MapCreate;
        set => Systems.MapCreate = value;
    }
    public CrystalSystem crystalsystem
    {
        get => Systems.CrystalSystem;
        set => Systems.CrystalSystem = value;
    }
    public UnitSetting unitset
    {
        get => Systems.UnitSetting;
        set => Systems.UnitSetting = value;
    }
    public Status status; // 参照用（単一インスタンス）

    // --- コアゲームシステム ---
    public MoveGererater movegenerater
    {
        get => Systems.MoveGenerator;
        set => Systems.MoveGenerator = value;
    }
    public AttackPointt attackpoint
    {
        get => Systems.AttackPoint;
        set => Systems.AttackPoint = value;
    }
    public BattleSystem battlesystem
    {
        get => Systems.BattleSystem;
        set => Systems.BattleSystem = value;
    }
    public VisionGenerater visiongenerater
    {
        get => Systems.VisionGenerator;
        set => Systems.VisionGenerator = value;
    }
    public UnitClick unitclick
    {
        get => Systems.UnitClick;
        set => Systems.UnitClick = value;
    }
    public SkillSystem skillsystem
    {
        get => Systems.SkillSystem;
        set => Systems.SkillSystem = value;
    }

    // --- 経済・建築・召喚 ---
    public APSystem apsystem
    {
        get => Systems.APSystem;
        set => Systems.APSystem = value;
    }
    public BuildSystem buildsystem
    {
        get => Systems.BuildSystem;
        set => Systems.BuildSystem = value;
    }
    public SummonSystem summonsystem
    {
        get => Systems.SummonSystem;
        set => Systems.SummonSystem = value;
    }
    public EconomySystem economysystem
    {
        get => Systems.EconomySystem;
        set => Systems.EconomySystem = value;
    }
    public BuildingAttackSystem buildingAttackSystem
    {
        get => Systems.BuildingAttackSystem;
        set => Systems.BuildingAttackSystem = value;
    }
    public SubCrystalSystem subCrystalSystem
    {
        get => Systems.SubCrystalSystem;
        set => Systems.SubCrystalSystem = value;
    }

    // --- タイマー・AI ---
    public TimerSystem timerSystem
    {
        get => Systems.TimerSystem;
        set => Systems.TimerSystem = value;
    }
    [HideInInspector]
    public AICommander aiCommander
    {
        get => Systems.AICommander;
        set => Systems.AICommander = value;
    }

    // --- UI ---
    public UnitPanelUI unitPanelUI
    {
        get => Systems.UnitPanelUI;
        set => Systems.UnitPanelUI = value;
    }
    [HideInInspector]
    public DamagePreviewUI damagePreviewUI
    {
        get => Systems.DamagePreviewUI;
        set => Systems.DamagePreviewUI = value;
    }
    [HideInInspector]
    public InputHintUI inputHintUI
    {
        get => Systems.InputHintUI;
        set => Systems.InputHintUI = value;
    }
    [HideInInspector]
    public MoveUndoSystem moveUndoSystem
    {
        get => Systems.MoveUndoSystem;
        set => Systems.MoveUndoSystem = value;
    }

    // --- カメラ・入力 ---
    [Header("カメラ・入力")]
    public Transform CameraObject
    {
        get => Context.CameraObject;
        set => Context.CameraObject = value;
    }

    // 入力バッファ（後方互換 — UIボタン等からの書き込みにも対応）
    [HideInInspector]
    public Vector2 MoveInput
    {
        get => Context.MoveInput;
        set => Context.MoveInput = value;
    }
    [HideInInspector]
    public float ScrollInput
    {
        get => Context.ScrollInput;
        set => Context.ScrollInput = value;
    }
    [HideInInspector]
    public bool LeftClickDown
    {
        get => Context.LeftClickDown;
        set => Context.LeftClickDown = value;
    }
    [HideInInspector]
    public bool RightClickDown
    {
        get => Context.RightClickDown;
        set => Context.RightClickDown = value;
    }
    [HideInInspector]
    public bool TurnEndDown
    {
        get => Context.TurnEndDown;
        set => Context.TurnEndDown = value;
    }
    [HideInInspector]
    public bool SelectNormalDown
    {
        get => Context.SelectNormalDown;
        set => Context.SelectNormalDown = value;
    }
    [HideInInspector]
    public bool SelectSkillDown
    {
        get => Context.SelectSkillDown;
        set => Context.SelectSkillDown = value;
    }
    [HideInInspector]
    public bool ToggleNSDown
    {
        get => Context.ToggleNSDown;
        set => Context.ToggleNSDown = value;
    }

    private GameAction gameaction;

    // ================================================================
    //  ステート切り替え
    // ================================================================
    public void ChangeState(StateCore next)
    {
        _stateManager?.Exit();
        _stateManager = next;
        _stateManager?.Entry();
    }

    public void Awake()
    {
        gameaction = new GameAction();
    }

    public void StartFirstTurn()
    {
        ChangeState(new PlayerStart(this));
    }

    void Update()
    {
        ReadInputs();
        UpdateCamera();
        _stateManager?.Update();
    }

    // ---- カメラ操作（全ステートで常時有効） ----
    private void UpdateCamera()
    {
        // WASD移動
        Vector2 move = Context.MoveInput;
        Transform cam = Context.CameraObject;
        if (move != Vector2.zero && cam != null)
        {
            Vector3 moveDir = new Vector3(move.x, 0f, move.y).normalized;
            cam.Translate(moveDir * GameConstants.CameraMoveSpeed * Time.deltaTime, Space.World);

            Vector3 pos = cam.position;
            var mc = Systems.MapCreate;
            if (mc != null)
            {
                pos.x = Mathf.Clamp(pos.x, 0f, mc.maxX - 10);
                pos.z = Mathf.Clamp(pos.z, 0f, mc.maxZ - 10);
            }
            cam.position = pos;
        }

        // スクロールズーム
        float scroll = Context.ScrollInput;
        if (scroll != 0f && Camera.main != null)
        {
            float fov = Camera.main.fieldOfView - scroll * GameConstants.CameraScrollSpeed;
            Camera.main.fieldOfView = Mathf.Clamp(fov, GameConstants.CameraFOVMin, GameConstants.CameraFOVMax);
        }
    }

    private void ReadInputs()
    {
        Context.MoveInput = gameaction.GamePlay.Move.ReadValue<Vector2>();
        Context.ScrollInput = gameaction.GamePlay.Scroll.ReadValue<float>();
        Context.LeftClickDown = gameaction.GamePlay.LeftClick.WasPressedThisFrame();
        Context.RightClickDown = gameaction.GamePlay.RightClick.WasPressedThisFrame();
        Context.TurnEndDown = gameaction.GamePlay.TurnEnd.WasPressedThisFrame();
        Context.SelectNormalDown = gameaction.GamePlay.SelectNormal.WasPressedThisFrame();
        Context.SelectSkillDown = gameaction.GamePlay.SelectSkill.WasPressedThisFrame();
        Context.ToggleNSDown = gameaction.GamePlay.ToggleNS.WasPressedThisFrame();
    }

    public void OnEnable() => gameaction.Enable();
    public void OnDisable() => gameaction.Disable();
    public void OnDestroy() => gameaction.Dispose();
}
