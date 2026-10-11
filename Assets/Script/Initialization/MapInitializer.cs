using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// マップ生成・クリスタル配置・ユニット初期配置・領地設定・視界構築を担当する。
/// GameGenerator から分離された単一責任クラス。
/// </summary>
public static class MapInitializer
{
    public static bool HasSavedMapSeed(SaveSystem.GameSaveData data) => data != null
        && (data.HasExplicitMapSnapshot || data.MapSeedX != 0f || data.MapSeedZ != 0f);
    public static bool HasSavedCrystalPositions(SaveSystem.GameSaveData data) => data != null
        && (data.HasExplicitMapSnapshot || data.PCPx != 0f || data.PCPz != 0f);
    /// <summary>
    /// マップ・ユニット・視界の初期化を実行する。
    /// ロードデータがある場合はシード・クリスタル位置を復元する。
    /// </summary>
    public static void Initialize(
        MapCreate mapCreate,
        CrystalSystem crystalSystem,
        UnitSetting unitSetting,
        TerritorySystem territorySystem,
        MoveGenerator moveGenerator,
        VisionGenerator visionGenerator,
        SaveSystem.GameSaveData loadData = null)
    {
        if (mapCreate == null)
        {
            Debug.LogError("[MapInitializer] MapCreate が null です");
            return;
        }

        mapCreate.UseR1Terrain = loadData == null || loadData.R1Terrain;
        if (loadData == null) GameAuthoringRules.Active?.ApplyNewMap(mapCreate);
        else if (loadData.HasMapGenerationSettings)
        {
            mapCreate.noiseScale = loadData.MapNoiseScale;
            mapCreate.riverHalfWidth = loadData.MapRiverHalfWidth;
        }
        if (loadData != null && loadData.HasExplicitMapSnapshot)
        {
            mapCreate.minY = loadData.MapMinY;
            mapCreate.maxY = loadData.MapMaxY;
            mapCreate.AverageFoundation = loadData.MapAverageFoundation;
            mapCreate.Amplitude = loadData.MapAmplitude;
        }
        if (loadData != null && loadData.MapWidth > 0 && loadData.MapDepth > 0)
        {
            mapCreate.maxX = loadData.MapWidth;
            mapCreate.maxZ = loadData.MapDepth;
        }
        // マップ生成
        if (HasSavedMapSeed(loadData))
        {
            mapCreate.GenerateNoise(loadData.MapSeedX, loadData.MapSeedZ);
        }
        else
        {
            mapCreate.GenerateNoise();
        }
        mapCreate.RestoreSavedLand(loadData);
        mapCreate.BuildTop();

        // クリスタル配置
        if (crystalSystem != null)
        {
            if (HasSavedCrystalPositions(loadData))
            {
                var pcp = new Vector3(loadData.PCPx, loadData.PCPy, loadData.PCPz);
                var ecp = new Vector3(loadData.ECPx, loadData.ECPy, loadData.ECPz);
                crystalSystem.CrystalCore(pcp, ecp);
            }
            else
            {
                crystalSystem.CrystalCore();
            }
        }

        // ユニット配置
        if (unitSetting != null)
        {
            unitSetting.UnitSet();
            ApplyAllUnitData(unitSetting, unitSetting.PlayerUnit);
            ApplyAllUnitData(unitSetting, unitSetting.EnemyUnit);
        }

        // 領地・占有セル・視界
        if (territorySystem != null) territorySystem.Territory();
        if (moveGenerator != null) moveGenerator.UnitPointCore();
        if (visionGenerator != null && mapCreate != null && moveGenerator != null && crystalSystem != null)
            visionGenerator.VisionPoint(mapCreate, moveGenerator, crystalSystem);
    }

    /// <summary>ユニットにUnitDataのステータスを適用する</summary>
    public static void ApplyAllUnitData(UnitSetting unitSetting, Transform unitParent)
    {
        if (unitSetting == null || unitParent == null) return;

        foreach (Status status in unitParent.GetComponentsInChildren<Status>())
        {
            if (status.type != Type.Unit) continue;

            UnitData data = status.GrowthData ?? unitSetting.GetDefinitionById(status.unitDefinitionId);
            if (data == null) unitSetting.UnitDataMap.TryGetValue(status.kind, out data);
            if (data != null)
                data.ApplyToStatus(status, status.Level);
            else
                Debug.LogWarning($"[MapInitializer] Kind:{status.kind} のUnitDataが未登録です");
        }
    }

    /// <summary>親Transform以下の子GameObjectをリストに収集する</summary>
    public static void CollectChildren(Transform parent, List<GameObject> result)
    {
        if (parent == null || result == null) return;
        result.Clear();
        foreach (Transform child in parent)
            result.Add(child.gameObject);
    }
}
