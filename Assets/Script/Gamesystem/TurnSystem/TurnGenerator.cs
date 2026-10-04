using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ゲーム全体の中央ハブ。ステートマシン駆動と AI モード切替のみを担当する。
/// 入力読み取りは TurnInputHandler、カメラ操作は TurnCameraController に委譲。
/// いずれも同一 GameObject 上の MonoBehaviour として GetComponent で取得する。
/// </summary>
public class TurnGenerator : MonoBehaviour
{
    // ================================================================
    //  集約コンテナ
    // ================================================================
    public GameSystems Systems { get; private set; } = new GameSystems();
    public GameContext Context { get; private set; } = new GameContext();

    // ================================================================
    //  AI モード切替（ML有効/無効）
    // ================================================================
    [Header("AI 設定")]
    [Tooltip("ML 補正を含めた AI にするか。OFF で純粋ルール決定論モード。")]
    [SerializeField] private bool useMLAssistedAI = true;

    // ================================================================
    //  ステート管理
    // ================================================================
    private StateCore _stateManager;
    public StateCore CurrentState => _stateManager;

    /// <summary>ゲームが決着済み（GameEndState）かどうか。死亡処理の二重発火防止に使う。</summary>
    public bool IsGameOver => _stateManager is GameEndState;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("開発者用 自動操作")]
    [SerializeField] private Key playerAIToggleKey = Key.F8;
    DeveloperPlayerAIController _playerAI;
    public DeveloperPlayerAIController PlayerAI => _playerAI;
    public bool DeveloperPlayerAIEnabled => _playerAI != null && _playerAI.Enabled;

#else
    public bool DeveloperPlayerAIEnabled => false;

#endif
    bool developerMatchUsed;
    public bool DeveloperPlayerAIWasUsed => developerMatchUsed;
    public void MarkDeveloperMatch(bool used = true) { developerMatchUsed |= used; }

    public void ChangeState(StateCore next)
    {
        if (next == null) throw new System.ArgumentNullException(nameof(next));
        // ゲーム終了後は一切の遷移を受け付けない。
        // ターン終了処理の途中で王/クリスタルが破壊された場合、続く
        // EnemyStart 等への遷移が GameEndState を上書きしてしまうのを防ぐ。
        // （リトライ/タイトルはシーン再読込で抜けるため復帰経路は不要）
        if (_stateManager is GameEndState)
        {
            Debug.Log($"[TurnGenerator] ゲーム終了済みのため {next?.GetType().Name} への遷移を無視");
            return;
        }

        // Entry/Exit の例外でステートマシンが止まらないよう保護する
        try { _stateManager?.Exit(); }
        catch (System.Exception e) { Debug.LogException(e); }

        // Exit may synchronously trigger a terminal transition (e.g. a death event).
        if (_stateManager is GameEndState) return;

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        _playerAI?.OnStateChanging();
#endif
        _stateManager = next;

        try { _stateManager?.Entry(); }
        catch (System.Exception e) { Debug.LogException(e); }
        finally
        {
            // Entry may perform nested transitions: use the final current state, not 'next'.
            bool enemy = _stateManager is EnemyStart || _stateManager is EnemyMove;
            if (!enemy)
            {
                EnemyTurnBannerUI.Hide();
                _inspection?.EndInspection();
            }
        }
    }

    // ================================================================
    //  ライフサイクル
    // ================================================================
    private GameAction gameaction;
    private TurnInputHandler _inputHandler;
    private TurnCameraController _cameraController;
    private TurnInspectionController _inspection;

    public GameAction GameAction => gameaction;

    public void Awake()
    {
        gameaction = new GameAction();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _playerAI = new DeveloperPlayerAIController(this);
#endif

        // ML モードを AIConfig へ反映（コンパイル/実行双方で1回だけ）
        AIConfig.Mode = useMLAssistedAI ? AIMode.MLAssisted : AIMode.PureRule;

        // 同じ GameObject 上のヘルパーコンポーネントを取得（無ければ追加）
        _inputHandler = GetComponent<TurnInputHandler>();
        if (_inputHandler == null) _inputHandler = gameObject.AddComponent<TurnInputHandler>();
        _inputHandler.Bind(this);

        _cameraController = GetComponent<TurnCameraController>();
        if (_cameraController == null) _cameraController = gameObject.AddComponent<TurnCameraController>();
        _cameraController.Bind(this);
        _inspection = gameObject.AddComponent<TurnInspectionController>();
        _inspection.Bind(this);
        var movePreview = GetComponent<MovementPreviewUI>();
        if (movePreview == null) movePreview = gameObject.AddComponent<MovementPreviewUI>();
        movePreview.Bind(this);
    }

    public void StartFirstTurn()
    {
        _cameraController?.FocusPlayerBase();
        ChangeState(new PlayerStart(this));
    }

    void Update()
    {
        _inputHandler?.Tick();
        if (GameMenuUI.Instance != null && GameMenuUI.Instance.IsOpen)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _playerAI?.PauseForMenu();
#endif
            return;
        }
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Keyboard.current != null && Keyboard.current[playerAIToggleKey].wasPressedThisFrame)
            _playerAI?.SetEnabled(!DeveloperPlayerAIEnabled);
#endif
        _cameraController?.Tick();
        _inspection?.Tick();
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_playerAI != null && _playerAI.Tick(Time.unscaledDeltaTime)) return;
#endif
        _stateManager?.Update();
    }

    public void OnEnable() => gameaction?.Enable();
    public void OnDisable()
    {
        gameaction?.Disable();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _playerAI?.SetEnabled(false, false);
#endif
    }
    public void OnDestroy()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _playerAI?.Dispose();
#endif
        gameaction?.Dispose();
    }
}
