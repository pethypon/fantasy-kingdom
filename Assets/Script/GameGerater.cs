using System.Collections.Generic;
using UnityEngine;

public class GameGenerater : MonoBehaviour
{
    [SerializeField] MapCreate _MapCreate;
    [SerializeField] CrystalSystem _CrystalSystem;
    [SerializeField] TerritorySystem _TerritorySystem;
    [SerializeField] UnitSetting _UnitSetting;
    [SerializeField] MoveGererater _MoveGenerater;
    [SerializeField] VisionGenerater _VisionGenerater;
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

    void Awake()
    {
        InitSingletons();

        // タイトル画面を表示するか判定
        bool showTitle = ShouldShowTitle();
        if (showTitle)
        {
            InitTitleScreen();
            return;
        }

        // タイトルスキップ（ロードまたはReturnToTitle=falseからの直行）
        _waitingForTitle = false;
        StartGameInit();
    }

    void Update()
    {
        // タイトル画面の閉じ待ち
        if (_waitingForTitle)
        {
            var title = TitleScreenUI.Instance;
            if (title != null && !title.IsActive)
            {
                _waitingForTitle = false;
                int loadSlot = title.SelectedLoadSlot;
                Destroy(title.gameObject);
                StartGameInit(loadSlot);
            }
        }
    }

    private bool ShouldShowTitle()
    {
        // ロード要求がある場合はタイトルをスキップしてゲーム開始
        if (GameMenuUI.PendingLoadSlot >= 0) return false;

        // タイトルへ戻る要求がある場合はタイトルを表示
        if (GameMenuUI.ReturnToTitle)
        {
            GameMenuUI.ReturnToTitle = false;
            return true;
        }

        // 初回起動時はタイトル表示
        return true;
    }

    private void InitTitleScreen()
    {
        var titleGo = new GameObject("TitleScreenUI");
        titleGo.AddComponent<TitleScreenUI>();
    }

    private void StartGameInit(int loadSlot = -1)
    {
        // ロード要求のstaticフラグをクリア
        int pendingLoad = GameMenuUI.PendingLoadSlot;
        GameMenuUI.PendingLoadSlot = -1;
        if (loadSlot < 0 && pendingLoad >= 0)
            loadSlot = pendingLoad;

        InitMapAndUnits();

        FactionState factionState = InitFactionState();
        _cachedFactionState = factionState;

        InitGameSystems(factionState);
        InitSkillsAndTimer(factionState);
        InitEconomyAndResources(factionState);
        InitUXSystems();
        PrewarmObjectPools();
        InitAI(factionState);
        InitGameMenu(factionState);

        // ロードデータがあれば適用
        if (loadSlot >= 0)
        {
            ApplyLoadData(loadSlot, factionState);
        }

        _TurnGenerater.StartFirstTurn();
    }

    // =====================================================================
    //  インゲームメニューの初期化
    // =====================================================================
    private void InitGameMenu(FactionState factionState)
    {
        var menuGo = new GameObject("GameMenuUI");
        var menu = menuGo.AddComponent<GameMenuUI>();
        menu.Init(_TurnGenerater, factionState);
    }

    // =====================================================================
    //  ロードデータの適用
    // =====================================================================
    private void ApplyLoadData(int slot, FactionState factionState)
    {
        var data = SaveSystem.LoadGame(slot);
        if (data == null)
        {
            Debug.LogWarning("[GameGerater] ロードデータが見つかりません");
            return;
        }

        Debug.Log($"[GameGerater] ロード適用: Turn{data.Turn} 脅威度{data.ThreatLevel}");

        // ターン数復元
        _TurnGenerater.Turn = data.Turn;

        // 資源復元
        SaveSystem.RestoreResources(data.PlayerResources, factionState.PlayerResources);
        SaveSystem.RestoreResources(data.EnemyResources, factionState.EnemyResources);

        // AP復元
        SaveSystem.RestoreAP(data.PlayerAP, factionState.PlayerAP);
        SaveSystem.RestoreAP(data.EnemyAP, factionState.EnemyAP);

        // サブクリスタル復元
        factionState.PlayerSubCrystals = data.PlayerSubCrystals;
        factionState.EnemySubCrystals = data.EnemySubCrystals;

        // ユニットのHP等を復元
        ApplyUnitLoadData(data, _UnitSetting.PlayerUnit);
        ApplyUnitLoadData(data, _UnitSetting.EnemyUnit);

        ToastMessageUI.Show($"スロット{slot + 1}のデータをロードしました", ToastMessageUI.MessageType.Info, 3f);
    }

    private void ApplyUnitLoadData(SaveSystem.GameSaveData data, Transform unitParent)
    {
        if (unitParent == null) return;

        foreach (Status s in unitParent.GetComponentsInChildren<Status>(true))
        {
            // セーブデータから同じ種類・チームのユニットを探す
            foreach (var ud in data.Units)
            {
                if (ud.Kind == s.kind.ToString() && ud.Team == s.team.ToString()
                    && Mathf.Abs(ud.PosX - s.transform.position.x) < 0.5f
                    && Mathf.Abs(ud.PosZ - s.transform.position.z) < 0.5f)
                {
                    s.HP = ud.HP;
                    s.MaxHP = ud.MaxHP;
                    s.ATK = ud.ATK;
                    s.DEF = ud.DEF;
                    s.ShieldTurns = ud.ShieldTurns;
                    s.ShieldActivated = ud.ShieldActivated;
                    s.SkillCooldown = ud.SkillCooldown;
                    s.gameObject.SetActive(ud.IsActive);
                    break;
                }
            }
        }
    }

    // =====================================================================
    //  シングルトン・UIの生成
    // =====================================================================
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

    // =====================================================================
    //  マップ生成・ユニット配置・視界構築
    // =====================================================================
    private void InitMapAndUnits()
    {
        _MapCreate.noisegenerater();
        _MapCreate.BuildTop();
        _CrystalSystem.CrystalCore();

        _UnitSetting.UnitSet();
        ApplyAllUnitData(_UnitSetting.PlayerUnit);
        ApplyAllUnitData(_UnitSetting.EnemyUnit);

        _TerritorySystem.Territory();
        _MoveGenerater.UnitPointCore();
        _VisionGenerater.VisionPoint(_MapCreate, _MoveGenerater, _CrystalSystem);

        CollectChildren(_PlayerCrystal, PlayerCrystalChildren);
        CollectChildren(_EnemyCrystal, EnemyCrystalChildren);
    }

    // =====================================================================
    //  NationState / FactionState の生成
    // =====================================================================
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

    // =====================================================================
    //  ゲームシステムの初期化（Build, Summon, Economy, Attack, SubCrystal）
    // =====================================================================
    private void InitGameSystems(FactionState factionState)
    {
        // ---- BuildSystem ----
        if (_BuildSystem == null)
        {
            _BuildSystem = Object.FindFirstObjectByType<BuildSystem>();
            if (_BuildSystem == null)
                _BuildSystem = gameObject.AddComponent<BuildSystem>();
            Debug.LogWarning("[GameGerater] _BuildSystem未設定 → 自動検出/生成");
        }
        if (_BuildSystem != null)
        {
            _BuildSystem.Init(_TurnGenerater, _TerritorySystem, _APSystem,
                              factionState, _MoveGenerater, _MapCreate);
            _TurnGenerater.buildsystem = _BuildSystem;

            if (_uiBuilder != null)
                _uiBuilder.InitBuildButtons(_BuildSystem, _APSystem, factionState);
        }

        // ---- SummonSystem ----
        if (_SummonSystem == null)
        {
            _SummonSystem = Object.FindFirstObjectByType<SummonSystem>();
            if (_SummonSystem != null)
                Debug.LogWarning("[GameGerater] _SummonSystem未設定 → 自動検出");
        }
        if (_SummonSystem != null)
        {
            _SummonSystem.Init(_TurnGenerater, _TerritorySystem, _APSystem,
                               factionState, _MoveGenerater, _MapCreate,
                               _UnitSetting, _VisionGenerater);
            _TurnGenerater.summonsystem = _SummonSystem;

            if (_uiBuilder != null)
                _uiBuilder.InitSummonButtons(_SummonSystem, _APSystem, factionState, _UnitSetting);
        }

        // ---- EconomySystem ----
        if (_EconomySystem == null)
        {
            _EconomySystem = gameObject.GetComponent<EconomySystem>();
            if (_EconomySystem == null)
                _EconomySystem = gameObject.AddComponent<EconomySystem>();
        }
        _EconomySystem.Init(_BuildSystem, factionState, _UnitSetting, _CrystalSystem);
        _TurnGenerater.economysystem = _EconomySystem;

        // ---- BuildingAttackSystem ----
        if (_BuildingAttackSystem == null)
        {
            _BuildingAttackSystem = gameObject.GetComponent<BuildingAttackSystem>();
            if (_BuildingAttackSystem == null)
                _BuildingAttackSystem = gameObject.AddComponent<BuildingAttackSystem>();
        }
        _BuildingAttackSystem.Init(_BuildSystem, _MoveGenerater, _UnitSetting,
                                   _VisionGenerater, _MapCreate, _CrystalSystem);
        _TurnGenerater.buildingAttackSystem = _BuildingAttackSystem;

        // ---- SubCrystalSystem ----
        if (_SubCrystalSystem == null)
        {
            _SubCrystalSystem = gameObject.GetComponent<SubCrystalSystem>();
            if (_SubCrystalSystem == null)
                _SubCrystalSystem = gameObject.AddComponent<SubCrystalSystem>();
        }
        _SubCrystalSystem.Init(_TurnGenerater, _TerritorySystem, factionState,
                                _MapCreate, _BuildSystem, _VisionGenerater,
                                _MoveGenerater, _CrystalSystem);
        _TurnGenerater.subCrystalSystem = _SubCrystalSystem;

        // ---- クロスリファレンス ----
        if (_BuildSystem != null)
            _BuildSystem.subCrystalSystem = _SubCrystalSystem;
        if (_BuildingAttackSystem != null)
            _BuildingAttackSystem.SetSubCrystalSystem(_SubCrystalSystem);
        if (_BuildSystem != null)
            _VisionGenerater.SetBuildingParents(_BuildSystem.PlayerBuildingParent, _BuildSystem.EnemyBuildingParent);
    }

    // =====================================================================
    //  スキル・タイマーの初期化
    // =====================================================================
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

    // =====================================================================
    //  経済・資源・UnitRegistryの初期化
    // =====================================================================
    private void InitEconomyAndResources(FactionState factionState)
    {
        if (_APPanelUI != null) _APPanelUI.Init(factionState);
        if (_ResourceBarUI != null) _ResourceBarUI.Init(factionState);

        if (factionState != null)
        {
            InitResources(factionState.PlayerResources);
            InitResources(factionState.EnemyResources);

            factionState.PlayerAP.Plus = factionState.PlayerResources.Citizen * EconomySystem.APPerCitizen;
            factionState.EnemyAP.Plus = factionState.EnemyResources.Citizen * EconomySystem.APPerCitizen;

            factionState.PlayerSubCrystals = 2;
            factionState.EnemySubCrystals = 2;
        }

        if (UnitRegistry.Instance != null)
        {
            UnitRegistry.Instance.ScanAndRegister(
                _UnitSetting.PlayerUnit, _UnitSetting.EnemyUnit,
                _BuildSystem != null ? _BuildSystem.PlayerBuildingParent : null,
                _BuildSystem != null ? _BuildSystem.EnemyBuildingParent : null);
        }
    }

    // =====================================================================
    //  UX系システムの初期化
    // =====================================================================
    private void InitUXSystems()
    {
        if (ToastMessageUI.Instance == null)
        {
            var toastGo = new GameObject("ToastMessageUI");
            toastGo.AddComponent<ToastMessageUI>();
        }

        var hintGo = new GameObject("InputHintUI");
        var inputHint = hintGo.AddComponent<InputHintUI>();
        _TurnGenerater.inputHintUI = inputHint;

        var dmgPreviewGo = new GameObject("DamagePreviewUI");
        var dmgPreview = dmgPreviewGo.AddComponent<DamagePreviewUI>();
        dmgPreview.turngenerater = _TurnGenerater;
        _TurnGenerater.damagePreviewUI = dmgPreview;

        if (FloatingDamageUI.Instance == null)
        {
            var floatDmgGo = new GameObject("FloatingDamageUI");
            floatDmgGo.AddComponent<FloatingDamageUI>();
        }

        if (EnemyTurnBannerUI.Instance == null)
        {
            var bannerGo = new GameObject("EnemyTurnBannerUI");
            bannerGo.AddComponent<EnemyTurnBannerUI>();
        }

        if (TurnAPIndicatorUI.Instance == null)
        {
            var indicatorGo = new GameObject("TurnAPIndicatorUI");
            var indicator = indicatorGo.AddComponent<TurnAPIndicatorUI>();
            indicator.Init(_TurnGenerater, _APSystem);
        }

        if (UnitHoverTooltipUI.Instance == null)
        {
            var tooltipGo = new GameObject("UnitHoverTooltipUI");
            tooltipGo.AddComponent<UnitHoverTooltipUI>();
        }

        var highlightGo = new GameObject("UnitSelectionHighlight");
        var highlight = highlightGo.AddComponent<UnitSelectionHighlight>();
        highlight.Init(_TurnGenerater);

        _TurnGenerater.moveUndoSystem = new MoveUndoSystem(_APSystem, _MoveGenerater);

        if (ActionLogUI.Instance == null)
        {
            var logGo = new GameObject("ActionLogUI");
            logGo.AddComponent<ActionLogUI>();
        }
    }

    // =====================================================================
    //  ObjectPool プレウォーム
    // =====================================================================
    private void PrewarmObjectPools()
    {
        if (ObjectPool.Instance != null && _MoveGenerater.MovePoint != null)
            ObjectPool.Instance.Prewarm(_MoveGenerater.MovePoint, 30, _MoveGenerater.Move);

        if (ObjectPool.Instance != null && _TurnGenerater.attackpoint.AttackPoint != null)
            ObjectPool.Instance.Prewarm(_TurnGenerater.attackpoint.AttackPoint, 15, _TurnGenerater.attackpoint.APparent);
    }

    // =====================================================================
    //  AI指揮官の初期化
    // =====================================================================
    private void InitAI(FactionState factionState)
    {
        var aiMajor = AIPersonality.RandomMajor();
        int threatLevel = SaveSystem.LoadProfile().ThreatLevel;
        _TurnGenerater.aiCommander = new AICommander(
            _TurnGenerater, _MoveGenerater, _TurnGenerater.attackpoint,
            _TurnGenerater.battlesystem, _VisionGenerater,
            _APSystem, _UnitSetting, _CrystalSystem, _MapCreate, aiMajor,
            _BuildSystem, _SummonSystem, factionState, _SkillSystem,
            _SubCrystalSystem, threatLevel);
    }

    // =====================================================================
    //  ゲーム開始時の全ユニット適用
    // =====================================================================
    private void ApplyAllUnitData(Transform unitParent)
    {
        foreach (Status status in unitParent.GetComponentsInChildren<Status>())
        {
            if (status.type != Type.Unit) continue;

            if (_UnitSetting.UnitDataMap.TryGetValue(status.kind, out UnitData data))
                data.ApplyToStatus(status, status.Level);
            else
                Debug.LogWarning($"[GameGenerater] Kind:{status.kind} のUnitDataが未登録です");
        }
    }

    // =====================================================================
    //  初期資源設定
    // =====================================================================
    private void InitResources(FactionState.ResourceData res)
    {
        const int InitWood = 200;
        const int InitStone = 200;
        const int InitWater = 50;
        const int InitPlank = 50;
        const int InitCutStone = 50;
        const int InitBread = 100;
        const int InitCitizen = 5;
        const int InitIron = 30;
        const int InitMagicOre = 15;
        const int InitIronOre = 10;
        const int InitCoal = 10;

        res.Wood = InitWood;
        res.Stone = InitStone;
        res.Water = InitWater;
        res.Plank = InitPlank;
        res.CutStone = InitCutStone;
        res.Bread = InitBread;
        res.Citizen = InitCitizen;
        res.Iron = InitIron;
        res.MagicOre = InitMagicOre;
        res.IronOre = InitIronOre;
        res.Coal = InitCoal;
    }

    // =====================================================================
    //  ユーティリティ
    // =====================================================================
    private void CollectChildren(Transform parent, List<GameObject> result)
    {
        result.Clear();
        foreach (Transform child in parent)
            result.Add(child.gameObject);
    }
}
