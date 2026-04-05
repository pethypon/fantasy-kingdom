using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゲーム起動のオーケストレーター。
/// 各初期化ステップを専用クラスに委譲し、起動シーケンスの制御のみを担当する。
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

    // ★ 追加: PlayerMove / PlayerStart が直参照する系
    [SerializeField] AttackPointt _AttackPoint;
    [SerializeField] UnitClick _UnitClick;
    [SerializeField] BattleSystem _BattleSystem;
    [SerializeField] TimerSystem _TimerSystem;
    [SerializeField] Transform _CameraTransform;

    [Header("UI")]
    [SerializeField] APPanelUI _APPanelUI;
    [SerializeField] ResourceBarUI _ResourceBarUI;
    private UIBuilder _uiBuilder;

    [Header("Crystal 親オブジェクト")]
    [SerializeField] Transform _PlayerCrystal;
    [SerializeField] Transform _EnemyCrystal;

    [HideInInspector] public List<GameObject> PlayerCrystalChildren = new();
    [HideInInspector] public List<GameObject> EnemyCrystalChildren = new();

    private bool _waitingForTitle = true;
    private FactionState _cachedFactionState;

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

    private void StartGameInit(int loadSlot = -1)
    {
        // ロード要求解決
        int pendingLoad = GameMenuUI.PendingLoadSlot;
        GameMenuUI.PendingLoadSlot = -1;
        if (loadSlot < 0 && pendingLoad >= 0) loadSlot = pendingLoad;

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

        // ★ TurnGenerater へ参照注入（最重要）
        WireTurnGeneratorReferences();

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

        // SystemInitializer で生成されるものを再注入（上書き）
        if (_TurnGenerater != null)
        {
            _TurnGenerater.buildsystem = _BuildSystem;
            _TurnGenerater.summonsystem = _SummonSystem;
            _TurnGenerater.economysystem = _EconomySystem;
            _TurnGenerater.buildingAttackSystem = _BuildingAttackSystem;
            _TurnGenerater.subCrystalSystem = _SubCrystalSystem;
            _TurnGenerater.skillsystem = _SkillSystem;
            _TurnGenerater.timerSystem = _TurnGenerater.timerSystem ?? _TimerSystem;
        }

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

        // ★ 最終チェック
        ValidateTurnGeneratorWiring();

        // Step 10: 最初のターン開始
        _TurnGenerater.StartFirstTurn();
    }

    private void WireTurnGeneratorReferences()
    {
        if (_TurnGenerater == null)
        {
            Debug.LogError("[GameGerater] TurnGenerater が未設定です");
            return;
        }

        // フォールバック取得
        _AttackPoint ??= Object.FindFirstObjectByType<AttackPointt>();
        _UnitClick ??= Object.FindFirstObjectByType<UnitClick>();
        _BattleSystem ??= Object.FindFirstObjectByType<BattleSystem>();
        _TimerSystem ??= Object.FindFirstObjectByType<TimerSystem>();

        if (_CameraTransform == null && Camera.main != null)
            _CameraTransform = Camera.main.transform;

        // 基盤
        _TurnGenerater.mapcreate = _MapCreate;
        _TurnGenerater.crystalsystem = _CrystalSystem;
        _TurnGenerater.unitset = _UnitSetting;
        _TurnGenerater.movegenerater = _MoveGenerater;
        _TurnGenerater.visiongenerater = _VisionGenerater;
        _TurnGenerater.apsystem = _APSystem;

        // コア
        _TurnGenerater.attackpoint = _AttackPoint;
        _TurnGenerater.unitclick = _UnitClick;
        _TurnGenerater.battlesystem = _BattleSystem;
        _TurnGenerater.timerSystem = _TimerSystem;

        // カメラ
        _TurnGenerater.CameraObject = _CameraTransform;
    }

    private void ValidateTurnGeneratorWiring()
    {
        if (_TurnGenerater == null) return;

        if (_TurnGenerater.crystalsystem == null) Debug.LogError("[WireCheck] crystalsystem is null");
        if (_TurnGenerater.unitset == null) Debug.LogError("[WireCheck] unitset is null");
        if (_TurnGenerater.apsystem == null) Debug.LogError("[WireCheck] apsystem is null");
        if (_TurnGenerater.movegenerater == null) Debug.LogError("[WireCheck] movegenerater is null");
        if (_TurnGenerater.visiongenerater == null) Debug.LogError("[WireCheck] visiongenerater is null");
        if (_TurnGenerater.unitclick == null) Debug.LogError("[WireCheck] unitclick is null");
        if (_TurnGenerater.attackpoint == null) Debug.LogError("[WireCheck] attackpoint is null");
    }

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

        if (_TurnGenerater != null && _TurnGenerater.attackpoint != null && _TurnGenerater.attackpoint.AttackPoint != null)
            ObjectPool.Instance.Prewarm(_TurnGenerater.attackpoint.AttackPoint, 15, _TurnGenerater.attackpoint.APparent);
    }
}