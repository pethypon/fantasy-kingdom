using UnityEngine;

/// <summary>
/// 毎ターン終了時に設置済み建築物を走査し、
/// レベル依存の資源生産（確率ボーナス含む）・維持費・特殊効果を処理する。
/// さらにユニット維持費・市民パン消費・市民APボーナスも管理する。
/// </summary>
public class EconomySystem : MonoBehaviour
{
    private BuildSystem buildSystem;
    private FactionState factionState;
    private UnitSetting unitSetting;

    // 前ターンに適用した特殊ボーナス（毎ターン差し替え用）
    private int prevPlayerBarracksXP;
    private int prevEnemyBarracksXP;

    /// <summary> 市民1人あたりのパン消費量/ターン </summary>
    public const int BreadPerCitizen = 1;

    /// <summary> 市民1人あたりの AP ボーナス </summary>
    public const int APPerCitizen = 1;

    public void Init(BuildSystem buildSystem, FactionState factionState, UnitSetting unitSetting = null)
    {
        this.buildSystem = buildSystem;
        this.factionState = factionState;
        this.unitSetting = unitSetting;
    }

    /// <summary>
    /// 指定チームの全建築物を走査し、生産・維持費・特殊効果を処理する。
    /// その後ユニット維持費・市民パン消費を行い、市民APボーナスを FactionState に反映する。
    /// </summary>
    public void ProcessTurn(Team team)
    {
        if (buildSystem == null || factionState == null) return;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;

        Debug.Log($"[EconomySystem] === {team} ターン経済処理開始 === パン={res.Bread} 市民={res.Citizen}");

        // ---- 1. 建築物の生産・維持費・特殊効果 ----
        ProcessBuildings(team, res);

        // ---- 2. ユニット維持費（Lv6以上） ----
        ProcessUnitMaintenance(team, res);

        // ---- 3. 市民パン消費（全市民が毎ターンパンを食べる） ----
        ProcessCitizenBread(team, res);

        // ---- 4. 市民APボーナスを FactionState に反映 ----
        UpdateCitizenAPBonus(team, res);

        // ---- 5. 資源上限クランプ（倉庫容量） ----
        int warehouseBonus = team == Team.Player
            ? factionState.PlayerResourceCapacity
            : factionState.EnemyResourceCapacity;
        if (warehouseBonus > 0)
            ClampResources(res, warehouseBonus);
    }

    // ==================================================================
    //  1. 建築物の生産・維持費・特殊効果
    // ==================================================================
    private void ProcessBuildings(Team team, FactionState.ResourceData res)
    {
        Transform buildingParent = buildSystem.GetBuildingParent(team);
        if (buildingParent == null) return;

        int producedCount = 0;
        int skippedCount = 0;
        int maintenanceCount = 0;
        int totalCitizenCap = 0;
        int totalResourceCap = 0;
        int totalBarracksXP = 0;

        foreach (Transform child in buildingParent)
        {
            var status = child.GetComponent<Status>();
            if (status == null) continue;
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
                continue;
            }
            if (facility == FacilityKind.Warehouse)
            {
                totalResourceCap += levelData.SpecialValue;
                continue;
            }

            // ---- 維持費処理（攻撃型建築物など） ----
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
                    skippedCount++;
                    continue;
                }
            }

            // ---- 生産処理 ----
            if (!levelData.HasProduction) continue;

            if (!levelData.Input.IsEmpty &&
                !FacilityData.CanAffordProduction(res, levelData.Input))
            {
                skippedCount++;
                continue;
            }

            if (!levelData.Input.IsEmpty)
                FacilityData.ConsumeProduction(res, levelData.Input);

            if (!levelData.Output.IsEmpty)
                FacilityData.AddProduction(res, levelData.Output);

            if (levelData.BonusChance1 > 0 && Random.value < levelData.BonusChance1)
                FacilityData.AddProduction(res, levelData.BonusOutput1);

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

        // ---- 市民成長（パンがあれば毎ターン+1、収容上限まで） ----
        int citizenCap = factionState.GetCitizenCap(team);
        if (res.Bread > 0 && res.Citizen < citizenCap)
        {
            res.Bread -= 1;
            res.Citizen += 1;
        }

        Debug.Log($"[EconomySystem] {team} 建築処理完了: " +
                  $"生産{producedCount}, スキップ{skippedCount}, 維持費{maintenanceCount}, " +
                  $"市民収容{totalCitizenCap}, 資源容量+{totalResourceCap}, 兵舎XP+{totalBarracksXP}%");
    }

    // ==================================================================
    //  2. ユニット維持費（Lv6以上のユニットが毎ターン資源を消費）
    // ==================================================================
    private void ProcessUnitMaintenance(Team team, FactionState.ResourceData res)
    {
        if (unitSetting == null) return;

        Transform unitParent = team == Team.Player ? unitSetting.PlayerUnit : unitSetting.EnemyUnit;
        if (unitParent == null) return;

        int paidCount = 0;
        int unpaidCount = 0;

        foreach (Transform child in unitParent)
        {
            var status = child.GetComponent<Status>();
            if (status == null) continue;
            if (status.type != Type.Unit) continue;
            if (status.HP <= 0) continue;

            // Lv5以下は維持費なし
            if (status.Level <= 5) continue;

            // UnitData から維持費を取得
            if (!unitSetting.UnitDataMap.TryGetValue(status.kind, out UnitData data)) continue;

            var upkeep = data.GetUpkeep(status.Level);
            if (upkeep.IsEmpty) continue;

            if (FacilityData.CanAffordProduction(res, upkeep))
            {
                FacilityData.ConsumeProduction(res, upkeep);
                paidCount++;
            }
            else
            {
                // 維持費が払えない場合はログのみ（将来的にデバフ等を追加可能）
                Debug.Log($"[EconomySystem] {status.kind} Lv{status.Level}: 維持費不足");
                unpaidCount++;
            }
        }

        if (paidCount > 0 || unpaidCount > 0)
            Debug.Log($"[EconomySystem] {team} ユニット維持費: 支払{paidCount}, 不足{unpaidCount}");
    }

    // ==================================================================
    //  3. 市民パン消費（全市民が毎ターンパンを消費。不足分は市民が減少）
    // ==================================================================
    private void ProcessCitizenBread(Team team, FactionState.ResourceData res)
    {
        if (res.Citizen <= 0) return;

        int totalBreadNeeded = res.Citizen * BreadPerCitizen;

        if (res.Bread >= totalBreadNeeded)
        {
            // 全市民を養える
            res.Bread -= totalBreadNeeded;
            Debug.Log($"[EconomySystem] {team} 市民パン消費: {res.Citizen}人×{BreadPerCitizen}パン = {totalBreadNeeded}パン消費  残パン={res.Bread}");
        }
        else
        {
            // パン不足：養える市民数を計算し、残りは離脱
            int fedCitizens = BreadPerCitizen > 0 ? res.Bread / BreadPerCitizen : 0;
            int starved = res.Citizen - fedCitizens;
            res.Bread = Mathf.Max(0, res.Bread - fedCitizens * BreadPerCitizen);
            res.Citizen = fedCitizens;
            Debug.Log($"[EconomySystem] {team} パン不足: 市民{starved}人離脱 (残{res.Citizen}人)");
        }
    }

    // ==================================================================
    //  4. 市民APボーナスを FactionState に反映
    // ==================================================================
    private void UpdateCitizenAPBonus(Team team, FactionState.ResourceData res)
    {
        int citizenBonus = res.Citizen * APPerCitizen;
        var apData = team == Team.Player ? factionState.PlayerAP : factionState.EnemyAP;
        apData.Plus = citizenBonus;
    }

    // ==================================================================
    //  資源上限クランプ
    // ==================================================================
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
