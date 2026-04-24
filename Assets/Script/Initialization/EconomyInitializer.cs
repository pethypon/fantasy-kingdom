using UnityEngine;

/// <summary>
/// 初期資源の設定、AP初期化、UnitRegistry スキャンを担当する。
/// GameGenerator の InitEconomyAndResources() / InitResources() から分離。
/// </summary>
public static class EconomyInitializer
{
    // =====================================================================
    //  初期資源定数（GameConstants に集約）
    // =====================================================================

    /// <summary>新規ゲーム時の全資源を初期化する。</summary>
    public static void InitializeNewGame(
        FactionState factionState,
        APPanelUI apPanelUI,
        ResourceBarUI resourceBarUI,
        UnitSetting unitSetting,
        BuildSystem buildSystem)
    {
        if (factionState == null) return;

        if (apPanelUI != null) apPanelUI.Init(factionState);
        if (resourceBarUI != null) resourceBarUI.Init(factionState);

        InitResources(factionState.PlayerResources);
        InitResources(factionState.EnemyResources);

        factionState.PlayerAP.Plus = factionState.PlayerResources.Citizen * EconomySystem.APPerCitizen;
        factionState.EnemyAP.Plus = factionState.EnemyResources.Citizen * EconomySystem.APPerCitizen;

        factionState.PlayerSubCrystals = GameConstants.InitialSubCrystals;
        factionState.EnemySubCrystals = GameConstants.InitialSubCrystals;

        RegisterUnits(unitSetting, buildSystem);
    }

    /// <summary>ロード時のUI初期化のみを行う。</summary>
    public static void InitializeForLoad(
        FactionState factionState,
        APPanelUI apPanelUI,
        ResourceBarUI resourceBarUI,
        UnitSetting unitSetting,
        BuildSystem buildSystem)
    {
        if (apPanelUI != null) apPanelUI.Init(factionState);
        if (resourceBarUI != null) resourceBarUI.Init(factionState);
        RegisterUnits(unitSetting, buildSystem);
    }

    /// <summary>資源の初期値を設定する</summary>
    private static void InitResources(FactionState.ResourceData res)
    {
        if (res == null) return;

        res.Wood = GameConstants.InitWood;
        res.Stone = GameConstants.InitStone;
        res.Water = GameConstants.InitWater;
        res.Bread = GameConstants.InitBread;
        res.Citizen = GameConstants.InitCitizen;
        res.Iron = GameConstants.InitIron;
        res.MagicOre = GameConstants.InitMagicOre;
    }

    /// <summary>UnitRegistry にユニットを登録する</summary>
    private static void RegisterUnits(UnitSetting unitSetting, BuildSystem buildSystem)
    {
        if (UnitRegistry.Instance == null || unitSetting == null) return;

        UnitRegistry.Instance.ScanAndRegister(
            unitSetting.PlayerUnit, unitSetting.EnemyUnit,
            buildSystem != null ? buildSystem.PlayerBuildingParent : null,
            buildSystem != null ? buildSystem.EnemyBuildingParent : null);
    }
}
