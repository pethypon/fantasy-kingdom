using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ゲーム全体の中央ハブ。ステートマシン駆動と入力管理のみを担当する。
/// GameSystems / GameContext を公開し、各ステートやシステムが直接アクセスする。
/// 後方互換の委譲プロパティは廃止し、責務を最小化した。
/// </summary>
public class TurnGenerator : MonoBehaviour
{
    // ================================================================
    //  集約コンテナ
    // ================================================================
    public GameSystems Systems { get; private set; } = new GameSystems();
    public GameContext Context { get; private set; } = new GameContext();

    // ================================================================
    //  ステート管理
    // ================================================================
    private StateCore _stateManager;
    public StateCore CurrentState => _stateManager;

    public void ChangeState(StateCore next)
    {
        _stateManager?.Exit();
        _stateManager = next;
        _stateManager?.Entry();
    }

    // ================================================================
    //  ライフサイクル
    // ================================================================
    private GameAction gameaction;

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

    public void OnEnable() => gameaction.Enable();
    public void OnDisable() => gameaction.Disable();
    public void OnDestroy() => gameaction.Dispose();

    // ================================================================
    //  入力読み取り
    // ================================================================
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

    // ================================================================
    //  カメラ操作（全ステートで常時有効）
    // ================================================================
    private void UpdateCamera()
    {
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

        float scroll = Context.ScrollInput;
        if (scroll != 0f && Camera.main != null)
        {
            float fov = Camera.main.fieldOfView - scroll * GameConstants.CameraScrollSpeed;
            Camera.main.fieldOfView = Mathf.Clamp(fov, GameConstants.CameraFOVMin, GameConstants.CameraFOVMax);
        }
    }
}
