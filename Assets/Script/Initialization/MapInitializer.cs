using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// マップ生成・クリスタル配置・ユニット初期配置・領地設定・視界構築を担当する。
/// GameGerater から分離された単一責任クラス。
/// </summary>
public static class MapInitializer
{
    /// <summary>
    /// マップ・ユニット・視界の初期化を実行する。
    /// ロードデータがある場合はシード・クリスタル位置を復元する。
    /// </summary>
    public static void Initialize(
        MapCreate mapCreate,
        CrystalSystem crystalSystem,
        UnitSetting unitSetting,
        TerritorySystem territorySystem,
        MoveGererater moveGenerator,
        VisionGenerater visionGenerator,
        SaveSystem.GameSaveData loadData = null)
    {
        if (mapCreate == null)
        {
            Debug.LogError("[MapInitializer] MapCreate が null です");
            return;
        }

        // マップ生成
        if (loadData != null && (loadData.MapSeedX != 0f || loadData.MapSeedZ != 0f))
        {
            mapCreate.noisegenerater(loadData.MapSeedX, loadData.MapSeedZ);
        }
        else
        {
            mapCreate.noisegenerater();
        }
        mapCreate.BuildTop();

        // クリスタル配置
        if (crystalSystem != null)
        {
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

            if (unitSetting.UnitDataMap.TryGetValue(status.kind, out UnitData data))
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
