using UnityEngine;
using UnityEngine.InputSystem;

public class TurnGenerater : MonoBehaviour
{
    public Status SelectUnit;
    public Vector3 OldCell;
    public Vector3 NewCell;

    [Header("�ێ�����X�e�[�g")]
    [SerializeField] StateCore StateManager;

    [Header("�^�[���Ǘ�")]
    [SerializeField] public int Turn = 0;

    [Header("���j�b�g�X�e�[�^�X")]
    [SerializeField] public Status status;

    [Header("���[�u")]
    [SerializeField] public MoveGererater movegenerater;

    [Header("���j�b�g�N���b�N")]
    [SerializeField] public UnitClick unitclick;

    [Header("�}�b�v�N���G�C�g")]
    [SerializeField] public MapCreate mapcreate;

    [Header("�A�^�b�N�|�C���g")]
    [SerializeField] public AttackPointt attackpoint;

    [Header("�o�g���V�X�e��")]
    [SerializeField] public BattleSystem battlesystem;

    [Header("�N���X�^���V�X�e��")]
    [SerializeField] public CrystalSystem crystalsystem;

    [Header("���E�V�X�e��")]
    [SerializeField] public VisionGenerater visiongenerater;   // �� public �ɕύX

    [Header("AP�V�X�e��")]
    [SerializeField] public APSystem apsystem;

    [Header("���j�b�g�z�u")]
    [SerializeField] public UnitSetting unitset;

    [Header("UI")]
    [SerializeField] public UnitPanelUI unitPanelUI;

    [Header("�Q�[���A�N�V�����̕ۑ��ꏊ")]
    public Vector2 MoveInput;
    public float ScrollInput;
    public bool LeftClickDown;
    public bool RightClickDown;
    public bool TurnEndDown;
    public bool SelectNormalDown;
    public bool SelectSkillDown;
    public bool ToggleNSDown;
    private GameAction gameaction;

    [Header("�J�����i�������Ώہj")]
    [SerializeField] public Transform CameraObject;

    // ������ �X�e�[�g�؂�ւ� ��������������������������������������������������������������������������������������������
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
