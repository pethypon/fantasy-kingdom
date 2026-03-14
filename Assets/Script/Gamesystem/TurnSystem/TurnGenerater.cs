using UnityEngine;
using UnityEngine.InputSystem;

public class TurnGenerater : MonoBehaviour
{
    public Status SelectUnit;
    public Vector3 OldCell;
    public Vector3 NewCell;

    [Header("保持するステート")]
    [SerializeField] StateCore StateManager;

    [Header("ターン管理")]
    [SerializeField] public int Turn = 0;

    [Header("ユニットステータス")]
    [SerializeField] public Status status;

    [Header("ムーブ")]
    [SerializeField] public MoveGererater movegenerater;

    [Header("ユニットクリック")]
    [SerializeField] public UnitClick unitclick;

    [Header("マップクリエイト")]
    [SerializeField] public MapCreate mapcreate;

    [Header("アタックポイント")]
    [SerializeField] public AttackPointt attackpoint;

    [Header("バトルシステム")]
    [SerializeField] public BattleSystem battlesystem;

    [Header("クリスタルシステム")]
    [SerializeField] public CrystalSystem crystalsystem;

    [Header("視界システム")]
    [SerializeField] public VisionGenerater visiongenerater;

    [Header("APシステム")]
    [SerializeField] public APSystem apsystem;

    [Header("建築システム")]
    [SerializeField] public BuildSystem buildsystem;

    [Header("召喚システム")]
    [SerializeField] public SummonSystem summonsystem;

    [Header("経済システム")]
    [SerializeField] public EconomySystem economysystem;

    [Header("建築物攻撃システム")]
    [SerializeField] public BuildingAttackSystem buildingAttackSystem;

    [Header("サブクリスタルシステム")]
    [SerializeField] public SubCrystalSystem subCrystalSystem;

    [Header("タイマーシステム")]
    [SerializeField] public TimerSystem timerSystem;

    [Header("ユニット配置")]
    [SerializeField] public UnitSetting unitset;

    [Header("UI")]
    [SerializeField] public UnitPanelUI unitPanelUI;

    [Header("ゲームアクションの保存場所")]
    public Vector2 MoveInput;
    public float ScrollInput;
    public bool LeftClickDown;
    public bool RightClickDown;
    public bool TurnEndDown;
    public bool SelectNormalDown;
    public bool SelectSkillDown;
    public bool ToggleNSDown;
    private GameAction gameaction;

    [Header("カメラ（操作対象）")]
    [SerializeField] public Transform CameraObject;

    // ---- ステート切り替え ----
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
        StateManager?.Update();
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
