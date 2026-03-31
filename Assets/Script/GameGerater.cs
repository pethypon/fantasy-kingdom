using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゲーム起動時のオーケストレーター。
/// 各初期化責務は専用クラス（MapInitializer, SystemInitializer, UXInitializer,
/// AIInitializer, ResourceInitializer）に委譲し、この クラスは呼び出し順序のみを管理する。
/// </summary>
public class GameGenerater : MonoBehaviour
{
    // ================================================================
    //  SerializeField（Inspector設定）
    // ================================================================
    [Header("マップ・ユニット基盤")]
    [SerializeField] MapCreate _MapCreate;
    [SerializeField] CrystalSystem _CrystalSystem;
    [SerializeField] TerritorySystem _TerritorySystem;
    [SerializeField] UnitSetting _UnitSetting;
    [SerializeField] MoveGererater _MoveGenerater;
    [SerializeField] VisionGenerater _VisionGenerater;

    [Header("コアゲームシステム")]
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

    // 生成済み Crystal 子（他スクリプトから参照）
    [HideInInspector] public List<GameObject> PlayerCrystalChildren = new List<GameObject>();
    [HideInInspector] public List<GameObject> EnemyCrystalChildren = new List<GameObject>();

    // タイトル画面からの遷移待ち用
    private bool _waitingForTitle = true;
    private FactionState _cachedFactionState;

    // ================================================================
    //  ライフサイクル
    // ================================================================
    void Awake()
    {
        InitSingletons();

        bool showTitle = ShouldShowTitle();
        if (showTitle)
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
    //  タイトル画面判定
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
    //  ゲーム初期化（メインフロー）
    // ================================================================
    private void StartGameInit(int loadSlot = -1)
    {
        // ロード要求のstaticフラグをクリア
        int pendingLoad = GameMenuUI.PendingLoadSlot;
        GameMenuUI.PendingLoadSlot = -1;
        if (loadSlot < 0 && pendingLoad >= 0)
            loadSlot = pendingLoad;

        // ロードデータを先読み
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

        // Phase 1: マップ・ユニット生成
        MapInitializer.Init(_MapCreate, _CrystalSystem, _UnitSetting, _TerritorySystem,
                            _MoveGenerater, _VisionGenerater, loadData);
        CollectChildren(_PlayerCrystal, PlayerCrystalChildren);
        CollectChildren(_EnemyCrystal, EnemyCrystalChildren);

        // Phase 2: FactionState 生成
        FactionState factionState = InitFactionState();
        _cachedFactionState = factionState;

        // Phase 3: ゲームシステム初期化
        SystemInitializer.Init(this, _TurnGenerater,
            ref _BuildSystem, ref _SummonSystem, ref _EconomySystem,
            ref _BuildingAttackSystem, ref _SubCrystalSystem,
            _TerritorySystem, _APSystem, factionState,
            _MoveGenerater, _MapCreate, _UnitSetting, _VisionGenerater,
            _CrystalSystem, _uiBuilder);

        // Phase 4: スキル・タイマー
        InitSkillsAndTimer(factionState);

        // Phase 5: 資源・UnitRegistry
        if (loadData != null)
        {
            if (_APPanelUI != null) _APPanelUI.Init(factionState);
            if (_ResourceBarUI != null) _ResourceBarUI.Init(factionState);
            if (UnitRegistry.Instance != null)
            {
                UnitRegistry.Instance.ScanAndRegister(
                    _UnitSetting.PlayerUnit, _UnitSetting.EnemyUnit,
                    _BuildSystem != null ? _BuildSystem.PlayerBuildingParent : null,
                    _BuildSystem != null ? _BuildSystem.EnemyBuildingParent : null);
            }
        }
        else
        {
            ResourceInitializer.Init(factionState, _APPanelUI, _ResourceBarUI,
                                     _UnitSetting, _BuildSystem);
        }

        // Phase 6: UXシステム
        UXInitializer.Init(_TurnGenerater, _APSystem, _MoveGenerater);

        // Phase 7: ObjectPool プレウォーム
        PrewarmObjectPools();

        // Phase 8: AI
        AIInitializer.Init(_TurnGenerater, _MoveGenerater, _VisionGenerater,
                           _APSystem, _UnitSetting, _CrystalSystem, _MapCreate,
                           _BuildSystem, _SummonSystem, factionState, _SkillSystem,
                           _SubCrystalSystem);

        // Phase 9: インゲームメニュー
        InitGameMenu(factionState);

        // Phase 10: ロードデータ適用
        if (loadData != null)
        {
            LoadDataApplier.Apply(loadData, factionState, _TurnGenerater,
                                  _UnitSetting, _CrystalSystem, _BuildSystem,
                                  _MoveGenerater, _VisionGenerater, _MapCreate);
        }

        _TurnGenerater.StartFirstTurn();
    }

    // ================================================================
    //  シングルトン・UIの生成
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

    // ================================================================
    //  NationState / FactionState の生成
    // ================================================================
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

    // ================================================================
    //  スキル・タイマーの初期化
    // ================================================================
    private void InitSkillsAndTimer(FactionState factionState)
    {
        if (_SkillSystem == null)
        {
            _SkillSystem = gameObject.GetComponent<SkillSystem>();
            if (_SkillSystem == null)
                _SkillSystem = gameObject.AddComponent<SkillSystem>();
        }
        _SkillSystem.turngenerater = _TurnGenerater;
        _SkillSystem.battlesystem = _TurnGenerater.battlesystem;
        _SkillSystem.movegenerater = _MoveGenerater;
        _SkillSystem.Init(factionState);
        _TurnGenerater.skillsystem = _SkillSystem;

        SkillData.AssignSkillsToAll(_UnitSetting.PlayerUnit);
        SkillData.AssignSkillsToAll(_UnitSetting.EnemyUnit);

        var timerSystem = gameObject.GetComponent<TimerSystem>();
        if (timerSystem == null)
            timerSystem = gameObject.AddComponent<TimerSystem>();
        timerSystem.Init(_TurnGenerater, _CrystalSystem);
        _TurnGenerater.timerSystem = timerSystem;
    }

    // ================================================================
    //  インゲームメニューの初期化
    // ================================================================
    private void InitGameMenu(FactionState factionState)
    {
        var menuGo = new GameObject("GameMenuUI");
        var menu = menuGo.AddComponent<GameMenuUI>();
        menu.Init(_TurnGenerater, factionState);
    }

    // ================================================================
    //  ObjectPool プレウォーム
    // ================================================================
    private void PrewarmObjectPools()
    {
        if (ObjectPool.Instance != null && _MoveGenerater.MovePoint != null)
            ObjectPool.Instance.Prewarm(_MoveGenerater.MovePoint, 30, _MoveGenerater.Move);

        if (ObjectPool.Instance != null && _TurnGenerater.attackpoint.AttackPoint != null)
            ObjectPool.Instance.Prewarm(_TurnGenerater.attackpoint.AttackPoint, 15, _TurnGenerater.attackpoint.APparent);
    }

    // ================================================================
    //  ユーティリティ
    // ================================================================
    private void CollectChildren(Transform parent, List<GameObject> result)
    {
        result.Clear();
        foreach (Transform child in parent)
            result.Add(child.gameObject);
    }
}
