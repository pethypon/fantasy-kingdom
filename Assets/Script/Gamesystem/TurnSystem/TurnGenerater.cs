using UnityEngine;
using UnityEngine.InputSystem;

public class TurnGenerater : MonoBehaviour
{
    public Status SelectUnit;
    public Vector3 OldCell;
    public Vector3 NewCell;

<<<<<<< HEAD
    [Header("•Û‚·‚éƒXƒe[ƒg")]
    [SerializeField] StateCore StateManager;

    [Header("ƒ^[ƒ“ŠÇ—")]
    [SerializeField] public int Turn = 0;

    [Header("ƒ†ƒjƒbƒgƒXƒe[ƒ^ƒX")]
=======
    [Header("ä¿æŒã™ã‚‹ã‚¹ãƒ†ãƒ¼ãƒˆ")]
    [SerializeField] StateCore StateManager;

    [Header("ã‚¿ãƒ¼ãƒ³ç®¡ç†")]
    [SerializeField] public int Turn = 0;

    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆã‚¹ãƒ†ãƒ¼ã‚¿ã‚¹")]
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    [SerializeField] public Status status;

    [Header("ãƒ ãƒ¼ãƒ–")]
    [SerializeField] public MoveGererater movegenerater;

    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆã‚¯ãƒªãƒƒã‚¯")]
    [SerializeField] public UnitClick unitclick;

    [Header("ãƒãƒƒãƒ—ã‚¯ãƒªã‚¨ã‚¤ãƒˆ")]
    [SerializeField] public MapCreate mapcreate;

    [Header("ã‚¢ã‚¿ãƒƒã‚¯ãƒã‚¤ãƒ³ãƒˆ")]
    [SerializeField] public AttackPointt attackpoint;

    [Header("ãƒãƒˆãƒ«ã‚·ã‚¹ãƒ†ãƒ ")]
    [SerializeField] public BattleSystem battlesystem;

<<<<<<< HEAD
    [Header("ƒNƒŠƒXƒ^ƒ‹ƒVƒXƒeƒ€")]
    [SerializeField] public CrystalSystem crystalsystem;

    [Header("‹ŠEƒVƒXƒeƒ€")]
    [SerializeField] public VisionGenerater visiongenerater;   // © public ‚É•ÏX

    [Header("APƒVƒXƒeƒ€")]
    [SerializeField] public APSystem apsystem;

    [Header("ƒ†ƒjƒbƒg”z’u")]
    [SerializeField] public UnitSetting unitset;

    [Header("ƒQ[ƒ€ƒAƒNƒVƒ‡ƒ“‚Ì•Û‘¶êŠ")]
=======
    [Header("ã‚¯ãƒªã‚¹ã‚¿ãƒ«ã‚·ã‚¹ãƒ†ãƒ ")]
    [SerializeField] public CrystalSystem crystalsystem;

    [Header("è¦–ç•Œã‚·ã‚¹ãƒ†ãƒ ")]
    [SerializeField] public VisionGenerater visiongenerater;

    [Header("APã‚·ã‚¹ãƒ†ãƒ ")]
    [SerializeField] public APSystem apsystem;

    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆé…ç½®")]
    [SerializeField] public UnitSetting unitset;

    [Header("ã‚²ãƒ¼ãƒ ã‚¢ã‚¯ã‚·ãƒ§ãƒ³ã®ä¿å­˜å ´æ‰€")]
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public Vector2 MoveInput;
    public float ScrollInput;
    public bool LeftClickDown;
    public bool RightClickDown;
    public bool TurnEndDown;
    public bool SelectNormalDown;
    public bool SelectSkillDown;
    public bool ToggleNSDown;
    private GameAction gameaction;

<<<<<<< HEAD
    [Header("ƒJƒƒ‰i“®‚©‚·‘ÎÛj")]
    [SerializeField] public Transform CameraObject;

    // „Ÿ„Ÿ„Ÿ ƒXƒe[ƒgØ‚è‘Ö‚¦ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    [Header("ã‚«ãƒ¡ãƒ©ï¼ˆå‹•ã‹ã™å¯¾è±¡ï¼‰")]
    [SerializeField] public Transform CameraObject;

    // â”€â”€â”€ ã‚¹ãƒ†ãƒ¼ãƒˆåˆ‡ã‚Šæ›¿ãˆ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
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
