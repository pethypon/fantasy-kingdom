using UnityEngine;

/// <summary>
/// 毎ターン開始時に設置済み建築物を走査し、
/// レベル依存の資源生産（確率ボーナス含む）・維持費・特殊効果を処理する。
/// </summary>
public class EconomySystem : MonoBehaviour
{
    private BuildSystem buildSystem;
    private FactionState factionState;

    // 前ターンに適用した特殊ボーナス（毎ターン差し替え用）
    private int prevPlayerBarracksXP;
    private int prevEnemyBarracksXP;

    public void Init(BuildSystem buildSystem, FactionState factionState)
    {
        this.buildSystem = buildSystem;
        this.factionState = factionState;
    }

    /// <summary>
    /// 指定チームの全建築物を走査し、生産・維持費・特殊効果を処理する。
    /// PlayerStart.Entry() で ResetAP() の前に呼ぶ。
    /// </summary>
    public void ProcessTurn(Team team)
    {
        if (buildSystem == null || factionState == null) return;
        if (buildSystem.BuildingParent == null) return;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;

        int producedCount = 0;
        int skippedCount = 0;
        int maintenanceCount = 0;
        int totalCitizenCap = 0;
        int totalResourceCap = 0;
        int totalBarracksXP = 0;

        foreach (Transform child in buildSystem.BuildingParent)
        {
            var status = child.GetComponent<Status>();
            if (status == null) continue;
            if (status.team != team) continue;
            if (status.HP <= 0) continue;

            var facility = status.facilityKind;
            int level = Mathf.Max(1, status.Level);
            var levelData = FacilityData.GetLevel(facility, level);

            // ---- 特殊効果の集計 ----
            if (facility == FacilityKind.Barracks)
            {
                totalBarracksXP += levelData.SpecialValue;
                continue;
            }
            if (facility == FacilityKind.House)
            {
                totalCitizenCap += levelData.SpecialValue;
                // House は生産なし（収容のみ）
                continue;
            }
            if (facility == FacilityKind.Warehouse)
            {
                totalResourceCap += levelData.SpecialValue;
                // Warehouse は生産なし（容量のみ）
                continue;
            }

            // ---- 維持費処理（攻撃型建築物） ----
            if (!levelData.Maintenance.IsEmpty)
            {
                if (FacilityData.CanAffordProduction(res, levelData.Maintenance))
                {
                    FacilityData.ConsumeProduction(res, levelData.Maintenance);
                    maintenanceCount++;
                }
                else
                {
                    Debug.Log($"[EconomySystem] {FacilityData.Table[facility].DisplayName} Lv{level}: 維持費不足");
                    // 維持費が払えない場合、生産もスキップ
                    skippedCount++;
                    continue;
                }
            }

            // ---- 生産処理 ----
            if (!levelData.HasProduction) continue;

            // 入力資源チェック
            if (!levelData.Input.IsEmpty &&
                !FacilityData.CanAffordProduction(res, levelData.Input))
            {
                skippedCount++;
                continue;
            }

            // 入力消費
            if (!levelData.Input.IsEmpty)
                FacilityData.ConsumeProduction(res, levelData.Input);

            // 確定産出
            if (!levelData.Output.IsEmpty)
                FacilityData.AddProduction(res, levelData.Output);

            // 確率ボーナス1
            if (levelData.BonusChance1 > 0 && Random.value < levelData.BonusChance1)
                FacilityData.AddProduction(res, levelData.BonusOutput1);

            // 確率ボーナス2
            if (levelData.BonusChance2 > 0 && Random.value < levelData.BonusChance2)
                FacilityData.AddProduction(res, levelData.BonusOutput2);

            producedCount++;
        }

        // ---- 容量を FactionState に反映 ----
        if (team == Team.Player)
        {
            factionState.PlayerCitizenCapacity = totalCitizenCap;
            factionState.PlayerResourceCapacity = totalResourceCap;
            factionState.PlayerBarracksXP = totalBarracksXP;
        }
        else
        {
            factionState.EnemyCitizenCapacity = totalCitizenCap;
            factionState.EnemyResourceCapacity = totalResourceCap;
            factionState.EnemyBarracksXP = totalBarracksXP;
        }

        // ---- 資源上限クランプ（倉庫容量） ----
        if (totalResourceCap > 0)
            ClampResources(res, totalResourceCap);

        Debug.Log($"[EconomySystem] {team} Turn完了: " +
                  $"生産{producedCount}, スキップ{skippedCount}, 維持費{maintenanceCount}, " +
                  $"市民収容{totalCitizenCap}, 資源容量+{totalResourceCap}, 兵舎XP+{totalBarracksXP}%");
    }

    /// <summary>
    /// 資源を倉庫容量でクランプする。
    /// 基本容量(BaseResourceCap) + 倉庫ボーナスが上限。
    /// </summary>
    private void ClampResources(FactionState.ResourceData res, int warehouseBonus)
    {
        int cap = FactionState.BaseResourceCap + warehouseBonus;
        res.Wood     = Mathf.Min(res.Wood, cap);
        res.Stone    = Mathf.Min(res.Stone, cap);
        res.Coal     = Mathf.Min(res.Coal, cap);
        res.IronOre  = Mathf.Min(res.IronOre, cap);
        res.Iron     = Mathf.Min(res.Iron, cap);
        res.MagicOre = Mathf.Min(res.MagicOre, cap);
        res.Wheat    = Mathf.Min(res.Wheat, cap);
        res.Bread    = Mathf.Min(res.Bread, cap);
        res.Water    = Mathf.Min(res.Water, cap);
        res.Plank    = Mathf.Min(res.Plank, cap);
        res.CutStone = Mathf.Min(res.CutStone, cap);
        // Citizen は倉庫容量の対象外（House の収容で管理）
    }
}
