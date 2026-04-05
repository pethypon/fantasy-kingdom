using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゲーム起動のオーケストレーター。
/// 各初期化ステップを専用クラスに委譲し、起動シーケンスの制御のみを担当する。
///
/// 初期化委譲先:
///   MapInitializer      — マップ・ユニット生成
///   SystemInitializer   — ゲームシステム検出・初期化
///   EconomyInitializer  — 資源・AP・UnitRegistry
///   UIInitializer       — UX系UI生成
///   SaveGameApplier     — ロードデータ適用
/// </summary>
public class GameGenerater : MonoBehaviour
{
    // ================================================================
    //  SerializeField（Inspector設定）
    // ================================================================
    [Header("マップ・ユニット")]
    [SerializeField] MapCreate _MapCreate;
    [SerializeField] CrystalSystem _CrystalSystem;
    [SerializeField] TerritorySystem _TerritorySystem;
    [SerializeField] UnitSetting _UnitSetting;
    [SerializeField] MoveGererater _MoveGenerater;
    [SerializeField] VisionGenerater _VisionGenerater;

    [Header("ゲームシステム")]
    [SerializeField] APSystem _APSystem;
    [SerializeField] BuildSystem _BuildSystem;
    [SerializeField] SummonSystem _SummonSystem;
    [SerializeField] EconomySystem _EconomySystem;
    [SerializeField] BuildingAttackSystem _BuildingAttackSystem;
    [SerializeField] SubCrystalSystem _SubCrystalSystem;
    [SerializeField] TurnGenerater _TurnGenerater;
    [SerializeField] SkillSystem _SkillSystem;

    [Header("UI")]
    [SerializeField] APPanelUI _APPanelUI;
    [SerializeField] ResourceBarUI _ResourceBarUI;
    private UIBuilder _uiBuilder;

    [Header("Crystal 親オブジェクト")]
    [SerializeField] Transform _PlayerCrystal;
    [SerializeField] Transform _EnemyCrystal;

    [HideInInspector] public List<GameObject> PlayerCrystalChildren = new List<GameObject>();
    [HideInInspector] public List<GameObject> EnemyCrystalChildren = new List<GameObject>();

    private bool _waitingForTitle = true;
    private FactionState _cachedFactionState;

    // ================================================================
    //  ライフサイクル
    // ================================================================

    void Awake()
    {
        InitSingletons();

        if (ShouldShowTitle())
        {
            InitTitleScreen();
            return;
        }

        _waitingForTitle = false;
        StartGameInit();
    }

    void Update()
    {
        if (!_waitingForTitle) return;

        var title = TitleScreenUI.Instance;
        if (title != null && !title.IsActive)
        {
            _waitingForTitle = false;
            int loadSlot = title.SelectedLoadSlot;
            Destroy(title.gameObject);
            StartGameInit(loadSlot);
        }
    }

    // ================================================================
    //  タイトル画面
    // ================================================================

    private bool ShouldShowTitle()
    {
        if (GameMenuUI.PendingLoadSlot >= 0) return false;
        if (GameMenuUI.ReturnToTitle)
        {
            GameMenuUI.ReturnToTitle = false;
            return true;
        }
        return true;
    }

    private void InitTitleScreen()
    {
        var titleGo = new GameObject("TitleScreenUI");
        titleGo.AddComponent<TitleScreenUI>();
    }

    // ================================================================
    //  メイン初期化シーケンス
    // ================================================================

    private void StartGameInit(int loadSlot = -1)
    {
        // ロード要求解決
        int pendingLoad = GameMenuUI.PendingLoadSlot;
        GameMenuUI.PendingLoadSlot = -1;
        if (loadSlot < 0 && pendingLoad >= 0)
            loadSlot = pendingLoad;

        // セーブデータ読み込み
        SaveSystem.GameSaveData loadData = null;
        if (loadSlot >= 0)
        {
            loadData = SaveSystem.LoadGame(loadSlot);
            if (loadData == null)
            {
                Debug.LogWarning("[GameGerater] ロードデータが見つかりません、新規ゲームを開始");
                loadSlot = -1;
            }
        }

        // Step 1: マップ・ユニット生成
        MapInitializer.Initialize(
            _MapCreate, _CrystalSystem, _UnitSetting, _TerritorySystem,
            _MoveGenerater, _VisionGenerater, loadData);

        MapInitializer.CollectChildren(_PlayerCrystal, PlayerCrystalChildren);
        MapInitializer.CollectChildren(_EnemyCrystal, EnemyCrystalChildren);

        // Step 1.5: コアシステム参照を TurnGenerater に登録
        WireCoreSystemsToTurnGen();

        // Step 2: FactionState 生成
        FactionState factionState = InitFactionState();
        _cachedFactionState = factionState;

        // Step 3: ゲームシステム初期化
        SystemInitializer.InitGameSystems(
            this, _TurnGenerater, factionState,
            _MapCreate, _TerritorySystem, _APSystem,
            _MoveGenerater, _VisionGenerater, _CrystalSystem, _UnitSetting,
            ref _BuildSystem, ref _SummonSystem, ref _EconomySystem,
            ref _BuildingAttackSystem, ref _SubCrystalSystem, _uiBuilder);

        SystemInitializer.InitSkillsAndTimer(
            this, _TurnGenerater, factionState,
            _MoveGenerater, _UnitSetting, ref _SkillSystem);

        // Step 4: 経済・資源・UnitRegistry
        if (loadData != null)
            EconomyInitializer.InitializeForLoad(factionState, _APPanelUI, _ResourceBarUI, _UnitSetting, _BuildSystem);
        else
            EconomyInitializer.InitializeNewGame(factionState, _APPanelUI, _ResourceBarUI, _UnitSetting, _BuildSystem);

        // Step 5: UX系UI
        UIInitializer.Initialize(_TurnGenerater, _APSystem);

        // Step 6: ObjectPool プレウォーム
        PrewarmObjectPools();

        // Step 7: AI
        SystemInitializer.InitAI(
            _TurnGenerater, _MoveGenerater, _VisionGenerater,
            _APSystem, _UnitSetting, _CrystalSystem, _MapCreate,
            _BuildSystem, _SummonSystem, _cachedFactionState,
            _SkillSystem, _SubCrystalSystem);

        // Step 8: ゲームメニュー
        InitGameMenu(factionState);

        // Step 9: ロードデータ適用
        if (loadData != null)
        {
            SaveGameApplier.Apply(
                loadData, factionState, _TurnGenerater,
                _UnitSetting, _CrystalSystem, _BuildSystem,
                _MoveGenerater, _VisionGenerater, _MapCreate);
        }

        // Step 10: 最初のターン開始
        _TurnGenerater.StartFirstTurn();
    }

    // ================================================================
    //  小さなヘルパー（委譲できない Unity 固有処理）
    // ================================================================

    private void InitSingletons()
    {
        if (ObjectPool.Instance == null)
        {
            var poolGo = new GameObject("ObjectPool");
            poolGo.AddComponent<ObjectPool>();
        }

        if (UnitRegistry.Instance == null)
        {
            var regGo = new GameObject("UnitRegistry");
            regGo.AddComponent<UnitRegistry>();
        }

        _uiBuilder = Object.FindFirstObjectByType<UIBuilder>();
        if (_uiBuilder == null)
        {
            var uiGo = new GameObject("UIBuilder");
            _uiBuilder = uiGo.AddComponent<UIBuilder>();
        }

        if (_APPanelUI == null && _uiBuilder.APPanel != null)
            _APPanelUI = _uiBuilder.APPanel;
        if (_ResourceBarUI == null && _uiBuilder.ResourceBar != null)
            _ResourceBarUI = _uiBuilder.ResourceBar;
    }

    private FactionState InitFactionState()
    {
        var playerNationGo = new GameObject("PlayerNation");
        playerNationGo.transform.SetParent(_PlayerCrystal);
        var playerNation = playerNationGo.AddComponent<NationState>();

        var enemyNationGo = new GameObject("EnemyNation");
        enemyNationGo.transform.SetParent(_EnemyCrystal);
        var enemyNation = enemyNationGo.AddComponent<NationState>();

        var factionGo = new GameObject("FactionState");
        FactionState factionState = factionGo.AddComponent<FactionState>();
        factionState.PlayerNation = playerNation;
        factionState.EnemyNation = enemyNation;
        _APSystem.Init(factionState);

        return factionState;
    }

    private void InitGameMenu(FactionState factionState)
    {
        var menuGo = new GameObject("GameMenuUI");
        var menu = menuGo.AddComponent<GameMenuUI>();
        menu.Init(_TurnGenerater, factionState);
    }

    /// <summary>
    /// コアシステム参照を TurnGenerater に登録する。
    /// リファクタリングで Inspector→プロパティ委譲に変わったため、手動で注入が必要。
    /// </summary>
    private void WireCoreSystemsToTurnGen()
    {
        _TurnGenerater.mapcreate = _MapCreate;
        _TurnGenerater.crystalsystem = _CrystalSystem;
        _TurnGenerater.unitset = _UnitSetting;
        _TurnGenerater.movegenerater = _MoveGenerater;
        _TurnGenerater.visiongenerater = _VisionGenerater;
        _TurnGenerater.apsystem = _APSystem;

        // AttackPointt, BattleSystem, UnitClick は SerializeField 未定義のため自動検出
        _TurnGenerater.attackpoint = Object.FindFirstObjectByType<AttackPointt>();
        _TurnGenerater.battlesystem = Object.FindFirstObjectByType<BattleSystem>();
        _TurnGenerater.unitclick = Object.FindFirstObjectByType<UnitClick>();

        // カメラ
        if (_TurnGenerater.CameraObject == null && Camera.main != null)
            _TurnGenerater.CameraObject = Camera.main.transform;
    }

    private void PrewarmObjectPools()
    {
        if (ObjectPool.Instance == null) return;

        if (_MoveGenerater != null && _MoveGenerater.MovePoint != null)
            ObjectPool.Instance.Prewarm(_MoveGenerater.MovePoint, 30, _MoveGenerater.Move);

        if (_TurnGenerater.attackpoint != null && _TurnGenerater.attackpoint.AttackPoint != null)
            ObjectPool.Instance.Prewarm(_TurnGenerater.attackpoint.AttackPoint, 15, _TurnGenerater.attackpoint.APparent);
    }
}
