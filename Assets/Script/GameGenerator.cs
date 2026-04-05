using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// ゲーム起動のオーケストレーター。
/// 各初期化ステップを専用クラスに委譲し、起動シーケンスの制御のみを担当する。
/// </summary>
public class GameGenerator : MonoBehaviour
{
    // ================================================================
    //  SerializeField（Inspector設定）
    // ================================================================
    [Header("マップ・ユニット")]
    [SerializeField] MapCreate _MapCreate;
    [SerializeField] CrystalSystem _CrystalSystem;
    [SerializeField] TerritorySystem _TerritorySystem;
    [SerializeField] UnitSetting _UnitSetting;
    [FormerlySerializedAs("_MoveGenerater")]
    [SerializeField] MoveGenerator _MoveGenerator;
    [FormerlySerializedAs("_VisionGenerater")]
    [SerializeField] VisionGenerator _VisionGenerator;

    [Header("ゲームシステム")]
    [SerializeField] APSystem _APSystem;
    [SerializeField] BuildSystem _BuildSystem;
    [SerializeField] SummonSystem _SummonSystem;
    [SerializeField] EconomySystem _EconomySystem;
    [SerializeField] BuildingAttackSystem _BuildingAttackSystem;
    [SerializeField] SubCrystalSystem _SubCrystalSystem;
    [SerializeField] TurnGenerator _TurnGenerator;
    [SerializeField] SkillSystem _SkillSystem;

    // ★ 追加: PlayerMove / PlayerStart が直参照する系
    [SerializeField] AttackGenerator _AttackPoint;
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
                Debug.LogWarning("[GameGenerator] ロードデータが見つかりません、新規ゲームを開始");
                loadSlot = -1;
            }
        }

        // Step 1: マップ・ユニット生成
        MapInitializer.Initialize(
            _MapCreate, _CrystalSystem, _UnitSetting, _TerritorySystem,
            _MoveGenerator, _VisionGenerator, loadData);

        // ★ TurnGenerator へ参照注入（最重要）
        WireTurnGeneratorReferences();

        MapInitializer.CollectChildren(_PlayerCrystal, PlayerCrystalChildren);
        MapInitializer.CollectChildren(_EnemyCrystal, EnemyCrystalChildren);

        // Step 2: FactionState 生成
        FactionState factionState = InitFactionState();
        _cachedFactionState = factionState;

        // Step 3: ゲームシステム初期化
        SystemInitializer.InitGameSystems(
            this, _TurnGenerator, factionState,
            _MapCreate, _TerritorySystem, _APSystem,
            _MoveGenerator, _VisionGenerator, _CrystalSystem, _UnitSetting,
            ref _BuildSystem, ref _SummonSystem, ref _EconomySystem,
            ref _BuildingAttackSystem, ref _SubCrystalSystem, _uiBuilder);

        SystemInitializer.InitSkillsAndTimer(
            this, _TurnGenerator, factionState,
            _MoveGenerator, _UnitSetting, ref _SkillSystem);

        // SystemInitializer で生成されるものを再注入（上書き）
        if (_TurnGenerator != null)
        {
            _TurnGenerator.buildsystem = _BuildSystem;
            _TurnGenerator.summonsystem = _SummonSystem;
            _TurnGenerator.economysystem = _EconomySystem;
            _TurnGenerator.buildingAttackSystem = _BuildingAttackSystem;
            _TurnGenerator.subCrystalSystem = _SubCrystalSystem;
            _TurnGenerator.skillsystem = _SkillSystem;
            _TurnGenerator.timerSystem = _TurnGenerator.timerSystem ?? _TimerSystem;
        }

        // Step 4: 経済・資源・UnitRegistry
        if (loadData != null)
            EconomyInitializer.InitializeForLoad(factionState, _APPanelUI, _ResourceBarUI, _UnitSetting, _BuildSystem);
        else
            EconomyInitializer.InitializeNewGame(factionState, _APPanelUI, _ResourceBarUI, _UnitSetting, _BuildSystem);

        // Step 5: UX系UI
        UIInitializer.Initialize(_TurnGenerator, _APSystem);

        // Step 6: ObjectPool プレウォーム
        PrewarmObjectPools();

        // Step 7: AI
        SystemInitializer.InitAI(
            _TurnGenerator, _MoveGenerator, _VisionGenerator,
            _APSystem, _UnitSetting, _CrystalSystem, _MapCreate,
            _BuildSystem, _SummonSystem, _cachedFactionState,
            _SkillSystem, _SubCrystalSystem);

        // Step 8: ゲームメニュー
        InitGameMenu(factionState);

        // Step 9: ロードデータ適用
        if (loadData != null)
        {
            SaveGameApplier.Apply(
                loadData, factionState, _TurnGenerator,
                _UnitSetting, _CrystalSystem, _BuildSystem,
                _MoveGenerator, _VisionGenerator, _MapCreate);
        }

        // ★ 最終チェック
        ValidateTurnGeneratorWiring();

        // Step 10: 最初のターン開始
        _TurnGenerator.StartFirstTurn();
    }

    private void WireTurnGeneratorReferences()
    {
        if (_TurnGenerator == null)
        {
            Debug.LogError("[GameGenerator] TurnGenerator が未設定です");
            return;
        }

        // フォールバック取得
        _AttackPoint ??= Object.FindFirstObjectByType<AttackGenerator>();
        _UnitClick ??= Object.FindFirstObjectByType<UnitClick>();
        _BattleSystem ??= Object.FindFirstObjectByType<BattleSystem>();
        _TimerSystem ??= Object.FindFirstObjectByType<TimerSystem>();

        if (_CameraTransform == null && Camera.main != null)
            _CameraTransform = Camera.main.transform;

        // 基盤
        _TurnGenerator.mapcreate = _MapCreate;
        _TurnGenerator.crystalsystem = _CrystalSystem;
        _TurnGenerator.unitset = _UnitSetting;
        _TurnGenerator.moveGenerator = _MoveGenerator;
        _TurnGenerator.visionGenerator = _VisionGenerator;
        _TurnGenerator.apsystem = _APSystem;

        // コア
        _TurnGenerator.attackGenerator = _AttackPoint;
        _TurnGenerator.unitclick = _UnitClick;
        _TurnGenerator.battlesystem = _BattleSystem;
        _TurnGenerator.timerSystem = _TimerSystem;

        // カメラ
        _TurnGenerator.CameraObject = _CameraTransform;
    }

    private void ValidateTurnGeneratorWiring()
    {
        if (_TurnGenerator == null) return;

        if (_TurnGenerator.crystalsystem == null) Debug.LogError("[WireCheck] crystalsystem is null");
        if (_TurnGenerator.unitset == null) Debug.LogError("[WireCheck] unitset is null");
        if (_TurnGenerator.apsystem == null) Debug.LogError("[WireCheck] apsystem is null");
        if (_TurnGenerator.moveGenerator == null) Debug.LogError("[WireCheck] moveGenerator is null");
        if (_TurnGenerator.visionGenerator == null) Debug.LogError("[WireCheck] visionGenerator is null");
        if (_TurnGenerator.unitclick == null) Debug.LogError("[WireCheck] unitclick is null");
        if (_TurnGenerator.attackGenerator == null) Debug.LogError("[WireCheck] attackGenerator is null");
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
        menu.Init(_TurnGenerator, factionState);
    }

    private void PrewarmObjectPools()
    {
        if (ObjectPool.Instance == null) return;

        if (_MoveGenerator != null && _MoveGenerator.MovePoint != null)
            ObjectPool.Instance.Prewarm(_MoveGenerator.MovePoint, 30, _MoveGenerator.Move);

        if (_TurnGenerator != null && _TurnGenerator.attackGenerator != null && _TurnGenerator.attackGenerator.AttackPoint != null)
            ObjectPool.Instance.Prewarm(_TurnGenerator.attackGenerator.AttackPoint, 15, _TurnGenerator.attackGenerator.APparent);
    }
}