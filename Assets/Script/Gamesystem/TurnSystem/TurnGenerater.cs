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

    // ================================================================
    //  入力・カメラ（専用クラスに委譲）
    // ================================================================
    private InputReader _inputReader;
    private CameraController _cameraController;

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
        var gameAction = new GameAction();
        _inputReader = new InputReader(gameAction, Context);
        _cameraController = new CameraController(Context, Systems);
    }

    public void StartFirstTurn()
    {
        ChangeState(new PlayerStart(this));
    }

    void Update()
    {
        _inputReader.ReadInputs();
        _cameraController.UpdateCamera();
        _stateManager?.Update();
    }

    public void OnEnable() => _inputReader.Enable();
    public void OnDisable() => _inputReader.Disable();
    public void OnDestroy() => _inputReader.Dispose();
}
