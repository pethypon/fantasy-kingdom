using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ゲーム全体の中央ハブ。ステートマシン駆動、入力管理、サブシステム参照を保持する。
/// </summary>
public class TurnGenerater : MonoBehaviour
{
    // ================================================================
    //  ステート管理
    // ================================================================
    [SerializeField] StateCore StateManager;
    public int Turn = 0;

    // ================================================================
    //  選択中ユニット
    // ================================================================
    public Status SelectUnit;
    public Vector3 OldCell;
    public Vector3 NewCell;

    // ================================================================
    //  マップ・ユニット基盤
    // ================================================================
    [Header("マップ・ユニット基盤")]
    public MapCreate mapcreate;
    public CrystalSystem crystalsystem;
    public UnitSetting unitset;
    public Status status;

    // ================================================================
    //  コアゲームシステム
    // ================================================================
    [Header("コアゲームシステム")]
    public MoveGererater movegenerater;
    public AttackPointt attackpoint;
    public BattleSystem battlesystem;
    public VisionGenerater visiongenerater;
    public UnitClick unitclick;
    public SkillSystem skillsystem;

    // ================================================================
    //  経済・建築・召喚
    // ================================================================
    [Header("経済・建築・召喚")]
    public APSystem apsystem;
    public BuildSystem buildsystem;
    public SummonSystem summonsystem;
    public EconomySystem economysystem;
    public BuildingAttackSystem buildingAttackSystem;
    public SubCrystalSystem subCrystalSystem;

    // ================================================================
    //  タイマー・AI
    // ================================================================
    [Header("タイマー・AI")]
    public TimerSystem timerSystem;
    [HideInInspector] public AICommander aiCommander;

    // ================================================================
    //  UI
    // ================================================================
    [Header("UI")]
    public UnitPanelUI unitPanelUI;
    [HideInInspector] public DamagePreviewUI damagePreviewUI;
    [HideInInspector] public InputHintUI inputHintUI;
    [HideInInspector] public MoveUndoSystem moveUndoSystem;

    // ================================================================
    //  カメラ・入力
    // ================================================================
    [Header("カメラ・入力")]
    public Transform CameraObject;

    // 入力バッファ（毎フレーム ReadInputs() で更新）
    [HideInInspector] public Vector2 MoveInput;
    [HideInInspector] public float ScrollInput;
    [HideInInspector] public bool LeftClickDown;
    [HideInInspector] public bool RightClickDown;
    [HideInInspector] public bool TurnEndDown;
    [HideInInspector] public bool SelectNormalDown;
    [HideInInspector] public bool SelectSkillDown;
    [HideInInspector] public bool ToggleNSDown;
    private GameAction gameaction;

    // ================================================================
    //  ステート切り替え
    // ================================================================
    public void ChangeState(StateCore next)
    {
        StateManager?.Exit();
        StateManager = next;
        StateManager?.Entry();
    }

    public void Awake()
    {
        gameaction = new GameAction();
    }

    public void StartFirstTurn()
    {
        ChangeState(new PlayerStart(this, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
    }

    void Update()
    {
        ReadInputs();
        UpdateCamera();
        StateManager?.Update();
    }

    // ---- カメラ操作（全ステートで常時有効） ----
    private void UpdateCamera()
    {
        // WASD移動
        if (MoveInput != Vector2.zero && CameraObject != null)
        {
            Vector3 moveDir = new Vector3(MoveInput.x, 0f, MoveInput.y).normalized;
            CameraObject.Translate(moveDir * GameConstants.CameraMoveSpeed * Time.deltaTime, Space.World);

            Vector3 pos = CameraObject.position;
            pos.x = Mathf.Clamp(pos.x, 0f, mapcreate.maxX - 10);
            pos.z = Mathf.Clamp(pos.z, 0f, mapcreate.maxZ - 10);
            CameraObject.position = pos;
        }

        // スクロールズーム
        if (ScrollInput != 0f && Camera.main != null)
        {
            float fov = Camera.main.fieldOfView - ScrollInput * GameConstants.CameraScrollSpeed;
            Camera.main.fieldOfView = Mathf.Clamp(fov, GameConstants.CameraFOVMin, GameConstants.CameraFOVMax);
        }
    }

    private void ReadInputs()
    {
        MoveInput = gameaction.GamePlay.Move.ReadValue<Vector2>();
        ScrollInput = gameaction.GamePlay.Scroll.ReadValue<float>();
        LeftClickDown = gameaction.GamePlay.LeftClick.WasPressedThisFrame();
        RightClickDown = gameaction.GamePlay.RightClick.WasPressedThisFrame();
        TurnEndDown = gameaction.GamePlay.TurnEnd.WasPressedThisFrame();
        SelectNormalDown = gameaction.GamePlay.SelectNormal.WasPressedThisFrame();
        SelectSkillDown = gameaction.GamePlay.SelectSkill.WasPressedThisFrame();
        ToggleNSDown = gameaction.GamePlay.ToggleNS.WasPressedThisFrame();
    }

    public void OnEnable() => gameaction.Enable();
    public void OnDisable() => gameaction.Disable();
    public void OnDestroy() => gameaction.Dispose();
}
