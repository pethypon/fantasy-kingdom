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

    void Awake()
    {
        // ---- UIBuilder の生成・取得 ----
        _uiBuilder = Object.FindFirstObjectByType<UIBuilder>();
        if (_uiBuilder == null)
        {
            var uiGo = new GameObject("UIBuilder");
            _uiBuilder = uiGo.AddComponent<UIBuilder>();
        }

        // UIBuilder が生成した UI パネルを取得
        if (_APPanelUI == null && _uiBuilder.APPanel != null)
            _APPanelUI = _uiBuilder.APPanel;
        if (_ResourceBarUI == null && _uiBuilder.ResourceBar != null)
            _ResourceBarUI = _uiBuilder.ResourceBar;

        // ==== 地形・クリスタル生成 ====
        _MapCreate.noisegenerater();
        _MapCreate.BuildTop();
        _CrystalSystem.CrystalCore();

        // ==== ユニット配置（SpawnUnit 経由で UnitData を適用） ====
        _UnitSetting.UnitSet();

        // ==== ゲーム開始時：シーン上の全ユニットに UnitData を適用 ====
        // UnitSet() が Awake 時間に間に合わないケースや Prefab 直置きに対応するため
        // SpawnUnit() が完了していないものをここで補完する
        ApplyAllUnitData(_UnitSetting.PlayerUnit);
        ApplyAllUnitData(_UnitSetting.EnemyUnit);

        // ==== 領地・移動・視界の構築 ====
        // ApplyAllUnitData の後に実行する理由：
        // Territory・Move・Vision の計算はステータス適用後に行う方がよい
        _TerritorySystem.Territory();
        _MoveGenerater.UnitPointCore();
        _VisionGenerater.VisionPoint(_MapCreate, _MoveGenerater, _CrystalSystem);

        // ==== Crystal 子オブジェクトを収集 ====
        CollectChildren(_PlayerCrystal, PlayerCrystalChildren);
        CollectChildren(_EnemyCrystal, EnemyCrystalChildren);

        // ==== FactionState を APSystem に注入 ====
        FactionState factionState = _PlayerCrystal.GetComponentInChildren<FactionState>();
        if (factionState == null)
            Debug.LogError("[GameGenerater] FactionState が PlayerCrystal の子に見つかりません");
        _APSystem.Init(factionState);

        // ---- BuildSystem 初期化 ----
        if (_BuildSystem != null)
        {
            _BuildSystem.Init(_TurnGenerater, _TerritorySystem, _APSystem,
                              factionState, _MoveGenerater, _MapCreate);
            _TurnGenerater.buildsystem = _BuildSystem;

            // UIBuilder の建築ボタンに BuildSystem を接続
            if (_uiBuilder != null)
                _uiBuilder.InitBuildButtons(_BuildSystem, _APSystem, factionState);
        }

        // ---- SummonSystem 初期化 ----
        if (_SummonSystem != null)
        {
            _SummonSystem.Init(_TurnGenerater, _TerritorySystem, _APSystem,
                               factionState, _MoveGenerater, _MapCreate,
                               _UnitSetting, _VisionGenerater);
            _TurnGenerater.summonsystem = _SummonSystem;

            if (_uiBuilder != null)
                _uiBuilder.InitSummonButtons(_SummonSystem, _APSystem, factionState, _UnitSetting);
        }

        // ---- EconomySystem 初期化 ----
        if (_EconomySystem == null)
        {
            _EconomySystem = gameObject.GetComponent<EconomySystem>();
            if (_EconomySystem == null)
                _EconomySystem = gameObject.AddComponent<EconomySystem>();
        }
        _EconomySystem.Init(_BuildSystem, factionState, _UnitSetting);
        _TurnGenerater.economysystem = _EconomySystem;

        // ---- BuildingAttackSystem 初期化 ----
        if (_BuildingAttackSystem == null)
        {
            _BuildingAttackSystem = gameObject.GetComponent<BuildingAttackSystem>();
            if (_BuildingAttackSystem == null)
                _BuildingAttackSystem = gameObject.AddComponent<BuildingAttackSystem>();
        }
        _BuildingAttackSystem.Init(_BuildSystem, _MoveGenerater, _UnitSetting,
                                   _VisionGenerater, _MapCreate, _CrystalSystem);
        _TurnGenerater.buildingAttackSystem = _BuildingAttackSystem;

        // ---- SubCrystalSystem 初期化 ----
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

        // BuildSystem にサブクリスタルシステム参照を渡す
        if (_BuildSystem != null)
            _BuildSystem.subCrystalSystem = _SubCrystalSystem;

        // BuildingAttackSystem にサブクリスタルシステム参照を渡す
        if (_BuildingAttackSystem != null)
            _BuildingAttackSystem.SetSubCrystalSystem(_SubCrystalSystem);

        // VisionGenerater に BuildingParent を渡す（サブクリスタル視界計算用）
        if (_BuildSystem != null)
            _VisionGenerater.SetBuildingParents(_BuildSystem.PlayerBuildingParent, _BuildSystem.EnemyBuildingParent);

        // ---- SkillSystem 初期化 ----
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

        // ---- 全ユニットにスキルをランダム配布 ----
        SkillData.AssignSkillsToAll(_UnitSetting.PlayerUnit);
        SkillData.AssignSkillsToAll(_UnitSetting.EnemyUnit);

        // ---- TimerSystem 初期化 ----
        var timerSystem = gameObject.GetComponent<TimerSystem>();
        if (timerSystem == null)
            timerSystem = gameObject.AddComponent<TimerSystem>();
        timerSystem.Init(_TurnGenerater, _CrystalSystem);
        _TurnGenerater.timerSystem = timerSystem;

        // ==== UI に FactionState を渡す ====
        if (_APPanelUI != null) _APPanelUI.Init(factionState);
        if (_ResourceBarUI != null) _ResourceBarUI.Init(factionState);

        // ==== 初期資源設定（GameReference 準拠） ====
        if (factionState != null)
        {
            InitResources(factionState.PlayerResources);
            InitResources(factionState.EnemyResources);

            // 初期市民APボーナスを反映
            factionState.PlayerAP.Plus = factionState.PlayerResources.Citizen * EconomySystem.APPerCitizen;
            factionState.EnemyAP.Plus = factionState.EnemyResources.Citizen * EconomySystem.APPerCitizen;

            // サブクリスタル初期配布（2個）
            factionState.PlayerSubCrystals = 2;
            factionState.EnemySubCrystals = 2;
        }

        // ==== AI指揮官の初期化 ====
        var aiMajor = AIPersonality.RandomMajor();
        _TurnGenerater.aiCommander = new AICommander(
            _TurnGenerater, _MoveGenerater, _TurnGenerater.attackpoint,
            _TurnGenerater.battlesystem, _VisionGenerater,
            _APSystem, _UnitSetting, _CrystalSystem, _MapCreate, aiMajor,
            _BuildSystem, _SummonSystem, factionState, _SkillSystem,
            _SubCrystalSystem);

        // ==== ターン開始 ====
        _TurnGenerater.StartFirstTurn();
    }

    // =====================================================================
    //  ゲーム開始時の全ユニット適用
    //  ゲーム中の生成は UnitSetting.SpawnUnit() が全担するため、
    //  ここでは「開始時点でシーン上に存在した」ものを対象にする
    // =====================================================================

    /// <summary>
    /// 指定の親オブジェクト配下の全ユニットに UnitData を適用する。
    /// ゲーム開始時の一括適用を担保（以後は UnitSetting.SpawnUnit() が担保）。
    /// </summary>
    private void ApplyAllUnitData(Transform unitParent)
    {
        foreach (Status status in unitParent.GetComponentsInChildren<Status>())
        {
            // MovePoint 等のユニット以外は除外
            if (status.type != Type.Unit) continue;

            if (_UnitSetting.UnitDataMap.TryGetValue(status.kind, out UnitData data))
                data.ApplyToStatus(status, status.Level);  // Lv はデフォルト Lv1
            else
                Debug.LogWarning($"[GameGenerater] Kind:{status.kind} のUnitDataが未登録です");
        }
    }

    // =====================================================================
    //  初期資源設定
    // =====================================================================

    /// <summary>
    /// 初期配布資源をセットする（GameReference 準拠）。
    /// 数値はマジックナンバーにしない（設計原則5）。
    /// </summary>
    private void InitResources(FactionState.ResourceData res)
    {
        const int InitWood = 100;
        const int InitStone = 100;
        const int InitWater = 50;
        const int InitPlank = 50;
        const int InitCutStone = 50;
        const int InitBread = 60;
        const int InitCitizen = 5;

        res.Wood = InitWood;
        res.Stone = InitStone;
        res.Water = InitWater;
        res.Plank = InitPlank;
        res.CutStone = InitCutStone;
        res.Bread = InitBread;
        res.Citizen = InitCitizen;
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
