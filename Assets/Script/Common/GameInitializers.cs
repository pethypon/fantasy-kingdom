using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  GameGerater から抽出された初期化責務を個別クラスに分離。
//  各クラスは単一責任（SRP）を持ち、GameGerater は薄いオーケストレーターになる。
// =====================================================================

/// <summary>
/// マップ・クリスタル・ユニット・領地・視界の初期化を担当。
/// </summary>
public static class MapInitializer
{
    public static void Init(
        MapCreate mapCreate, CrystalSystem crystalSystem,
        UnitSetting unitSetting, TerritorySystem territorySystem,
        MoveGererater moveGen, VisionGenerater visionGen,
        SaveSystem.GameSaveData loadData = null)
    {
        // ---- マップ生成 ----
        if (loadData != null && (loadData.MapSeedX != 0f || loadData.MapSeedZ != 0f))
            mapCreate.noisegenerater(loadData.MapSeedX, loadData.MapSeedZ);
        else
            mapCreate.noisegenerater();

        mapCreate.BuildTop();

        // ---- クリスタル配置 ----
        if (loadData != null && (loadData.PCPx != 0f || loadData.PCPz != 0f))
        {
            var pcp = new Vector3(loadData.PCPx, loadData.PCPy, loadData.PCPz);
            var ecp = new Vector3(loadData.ECPx, loadData.ECPy, loadData.ECPz);
            crystalSystem.CrystalCore(pcp, ecp);
        }
        else
        {
            crystalSystem.CrystalCore();
        }

        // ---- ユニット配置 ----
        unitSetting.UnitSet();
        ApplyAllUnitData(unitSetting, unitSetting.PlayerUnit);
        ApplyAllUnitData(unitSetting, unitSetting.EnemyUnit);

        // ---- 領地・占有・視界 ----
        territorySystem.Territory();
        moveGen.UnitPointCore();
        visionGen.VisionPoint(mapCreate, moveGen, crystalSystem);
    }

    private static void ApplyAllUnitData(UnitSetting unitSetting, Transform unitParent)
    {
        foreach (Status status in unitParent.GetComponentsInChildren<Status>())
        {
            if (status.type != Type.Unit) continue;
            if (unitSetting.UnitDataMap.TryGetValue(status.kind, out UnitData data))
                data.ApplyToStatus(status, status.Level);
            else
                Debug.LogWarning($"[MapInitializer] Kind:{status.kind} のUnitDataが未登録です");
        }
    }
}

/// <summary>
/// ゲームシステム（Build, Summon, Economy, BuildingAttack, SubCrystal）の初期化を担当。
/// </summary>
public static class SystemInitializer
{
    public static void Init(
        GameGenerater host,
        TurnGenerater turnGen,
        ref BuildSystem buildSystem,
        ref SummonSystem summonSystem,
        ref EconomySystem economySystem,
        ref BuildingAttackSystem buildingAttackSystem,
        ref SubCrystalSystem subCrystalSystem,
        TerritorySystem territorySystem,
        APSystem apSystem,
        FactionState factionState,
        MoveGererater moveGen,
        MapCreate mapCreate,
        UnitSetting unitSetting,
        VisionGenerater visionGen,
        CrystalSystem crystalSystem,
        UIBuilder uiBuilder)
    {
        // ---- BuildSystem ----
        buildSystem = EnsureComponent(host, buildSystem);
        buildSystem.Init(turnGen, territorySystem, apSystem, factionState, moveGen, mapCreate);
        turnGen.buildsystem = buildSystem;
        if (uiBuilder != null)
            uiBuilder.InitBuildButtons(buildSystem, apSystem, factionState);

        // ---- SummonSystem ----
        if (summonSystem == null)
        {
            summonSystem = Object.FindFirstObjectByType<SummonSystem>();
            if (summonSystem != null)
                Debug.LogWarning("[SystemInitializer] _SummonSystem未設定 → 自動検出");
        }
        if (summonSystem != null)
        {
            summonSystem.Init(turnGen, territorySystem, apSystem,
                               factionState, moveGen, mapCreate, unitSetting, visionGen);
            turnGen.summonsystem = summonSystem;
            if (uiBuilder != null)
                uiBuilder.InitSummonButtons(summonSystem, apSystem, factionState, unitSetting);
        }

        // ---- EconomySystem ----
        economySystem = EnsureComponent(host, economySystem);
        economySystem.Init(buildSystem, factionState, unitSetting, crystalSystem);
        turnGen.economysystem = economySystem;

        // ---- BuildingAttackSystem ----
        buildingAttackSystem = EnsureComponent(host, buildingAttackSystem);
        buildingAttackSystem.Init(buildSystem, moveGen, unitSetting, visionGen, mapCreate, crystalSystem);
        turnGen.buildingAttackSystem = buildingAttackSystem;

        // ---- SubCrystalSystem ----
        subCrystalSystem = EnsureComponent(host, subCrystalSystem);
        subCrystalSystem.Init(turnGen, territorySystem, factionState,
                                mapCreate, buildSystem, visionGen, moveGen, crystalSystem);
        turnGen.subCrystalSystem = subCrystalSystem;

        // ---- クロスリファレンス ----
        buildSystem.subCrystalSystem = subCrystalSystem;
        buildingAttackSystem.SetSubCrystalSystem(subCrystalSystem);
        visionGen.SetBuildingParents(buildSystem.PlayerBuildingParent, buildSystem.EnemyBuildingParent);
    }

    /// <summary>コンポーネントがnullなら自動検出/生成する</summary>
    private static T EnsureComponent<T>(GameGenerater host, T current) where T : Component
    {
        if (current != null) return current;
        current = host.gameObject.GetComponent<T>();
        if (current == null)
            current = host.gameObject.AddComponent<T>();
        return current;
    }
}

/// <summary>
/// UX系UI（Toast, InputHint, DamagePreview, FloatingDamage等）の初期化を担当。
/// </summary>
public static class UXInitializer
{
    public static void Init(TurnGenerater turnGen, APSystem apSystem, MoveGererater moveGen)
    {
        EnsureSingleton<ToastMessageUI>("ToastMessageUI", ToastMessageUI.Instance);
        EnsureSingleton<FloatingDamageUI>("FloatingDamageUI", FloatingDamageUI.Instance);
        EnsureSingleton<EnemyTurnBannerUI>("EnemyTurnBannerUI", EnemyTurnBannerUI.Instance);

        var hintGo = new GameObject("InputHintUI");
        var inputHint = hintGo.AddComponent<InputHintUI>();
        turnGen.inputHintUI = inputHint;

        var dmgPreviewGo = new GameObject("DamagePreviewUI");
        var dmgPreview = dmgPreviewGo.AddComponent<DamagePreviewUI>();
        dmgPreview.turngenerater = turnGen;
        turnGen.damagePreviewUI = dmgPreview;

        if (TurnAPIndicatorUI.Instance == null)
        {
            var indicatorGo = new GameObject("TurnAPIndicatorUI");
            var indicator = indicatorGo.AddComponent<TurnAPIndicatorUI>();
            indicator.Init(turnGen, apSystem);
        }

        EnsureSingleton<UnitHoverTooltipUI>("UnitHoverTooltipUI", UnitHoverTooltipUI.Instance);

        var highlightGo = new GameObject("UnitSelectionHighlight");
        var highlight = highlightGo.AddComponent<UnitSelectionHighlight>();
        highlight.Init(turnGen);

        turnGen.moveUndoSystem = new MoveUndoSystem(apSystem, moveGen);

        EnsureSingleton<ActionLogUI>("ActionLogUI", ActionLogUI.Instance);
    }

    private static void EnsureSingleton<T>(string name, T existing) where T : Component
    {
        if (existing != null) return;
        var go = new GameObject(name);
        go.AddComponent<T>();
    }
}

/// <summary>
/// AI指揮官の初期化を担当。
/// </summary>
public static class AIInitializer
{
    public static void Init(
        TurnGenerater turnGen,
        MoveGererater moveGen,
        VisionGenerater visionGen,
        APSystem apSystem,
        UnitSetting unitSetting,
        CrystalSystem crystalSystem,
        MapCreate mapCreate,
        BuildSystem buildSystem,
        SummonSystem summonSystem,
        FactionState factionState,
        SkillSystem skillSystem,
        SubCrystalSystem subCrystalSystem)
    {
        var aiMajor = AIPersonality.RandomMajor();
        int threatLevel = SaveSystem.LoadProfile().ThreatLevel;
        turnGen.aiCommander = new AICommander(
            turnGen, moveGen, turnGen.attackpoint,
            turnGen.battlesystem, visionGen,
            apSystem, unitSetting, crystalSystem, mapCreate, aiMajor,
            buildSystem, summonSystem, factionState, skillSystem,
            subCrystalSystem, threatLevel);
    }
}

/// <summary>
/// 初期資源を設定する。
/// </summary>
public static class ResourceInitializer
{
    public static void Init(FactionState factionState, APPanelUI apPanelUI,
                            ResourceBarUI resourceBarUI, UnitSetting unitSetting,
                            BuildSystem buildSystem)
    {
        if (apPanelUI != null) apPanelUI.Init(factionState);
        if (resourceBarUI != null) resourceBarUI.Init(factionState);

        if (factionState != null)
        {
            InitResources(factionState.PlayerResources);
            InitResources(factionState.EnemyResources);

            factionState.PlayerAP.Plus = factionState.PlayerResources.Citizen * EconomySystem.APPerCitizen;
            factionState.EnemyAP.Plus = factionState.EnemyResources.Citizen * EconomySystem.APPerCitizen;

            factionState.PlayerSubCrystals = GameConstants.InitialSubCrystals;
            factionState.EnemySubCrystals = GameConstants.InitialSubCrystals;
        }

        if (UnitRegistry.Instance != null)
        {
            UnitRegistry.Instance.ScanAndRegister(
                unitSetting.PlayerUnit, unitSetting.EnemyUnit,
                buildSystem != null ? buildSystem.PlayerBuildingParent : null,
                buildSystem != null ? buildSystem.EnemyBuildingParent : null);
        }
    }

    private static void InitResources(FactionState.ResourceData res)
    {
        res.Wood     = GameConstants.InitWood;
        res.Stone    = GameConstants.InitStone;
        res.Water    = GameConstants.InitWater;
        res.Plank    = GameConstants.InitPlank;
        res.CutStone = GameConstants.InitCutStone;
        res.Bread    = GameConstants.InitBread;
        res.Citizen  = GameConstants.InitCitizen;
        res.Iron     = GameConstants.InitIron;
        res.MagicOre = GameConstants.InitMagicOre;
        res.IronOre  = GameConstants.InitIronOre;
        res.Coal     = GameConstants.InitCoal;
    }
}
