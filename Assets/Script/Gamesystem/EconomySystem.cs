using System.Collections.Generic;
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
    private CrystalSystem crystalSystem;
    readonly List<Status> buildingActors = new List<Status>(32);
    readonly List<FacilityData.FacilityLevelData> buildingRecipes = new List<FacilityData.FacilityLevelData>(32);

    /// <summary> 市民1人あたりのパン消費量/ターン </summary>
    public const int BreadPerCitizen = 1;

    /// <summary> 市民1人あたりの AP ボーナス </summary>
    public const int APPerCitizen = 1;
    public static int CitizenAPBonus => Mathf.Clamp(GameAuthoringRules.Active?.citizenAP ?? APPerCitizen, 0, 10);
    public static int CitizenBreadCost => Mathf.Clamp(GameAuthoringRules.Active?.breadPerCitizen ?? BreadPerCitizen, 0, 10);

    public void Init(BuildSystem buildSystem, FactionState factionState,
                     UnitSetting unitSetting = null, CrystalSystem crystalSystem = null)
    {
        this.buildSystem = buildSystem;
        this.factionState = factionState;
        this.unitSetting = unitSetting;
        this.crystalSystem = crystalSystem;
    }

    /// <summary>
    /// 前払い維持費 → クリスタル収入と建物生産 → 市民成長・食料の順。
    /// 当ターンの生産物で、すでに失敗した維持費の支払いを帳消しにしない。
    /// </summary>
    public void ProcessTurn(Team team)
    {
        if (buildSystem == null || factionState == null) return;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;

        Debug.Log($"[EconomySystem] === {team} ターン経済処理開始 === パン={res.Bread} 市民={res.Citizen}");

        // ---- 1. 前払い維持費（当ターンの生産より先に確定） ----
        ProcessUnitMaintenance(team, res);
        PrepareBuildings(team, res);

        // ---- 2. クリスタル収入・建物の周期生産 ----
        ProcessCrystalIncome(team, res);
        ProcessBuildingProduction(team, res);

        // ---- 3. 市民成長・パン消費 ----
        ProcessCitizenGrowth(team, res);
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
    //  0. クリスタル基本収入（序盤10ターン逓減式）
    //  木20/石20/水10/パン10/鉄5 から毎ターン10%ずつ減少。
    // ==================================================================
    private const int CrystalIncomeTurns = 10;
    private const int CrystalBaseWood  = 20;
    private const int CrystalBaseStone = 20;
    private const int CrystalBaseWater = 10;
    private const int CrystalBaseBread = 10;
    private const int CrystalBaseIron  = 5;

    private void ProcessCrystalIncome(Team team, FactionState.ResourceData res)
    {
        // クリスタルの生存チェック
        if (crystalSystem != null)
        {
            Transform parent = team == Team.Player
                ? crystalSystem.Playercrystal
                : crystalSystem.Enemycrystal;
            if (parent != null && parent.childCount > 0)
            {
                var status = parent.GetChild(0).GetComponent<Status>();
                if (status != null && status.HP <= 0) return;
            }
        }

        var nation = factionState.GetNation(team);
        int turn = nation.TurnsAlive;
        if (turn >= CrystalIncomeTurns) return;

        float decay = 1f - (0.10f * turn); // ターン0で1.0、ターン9で0.10
        if (decay <= 0f) return;

        int wood  = Mathf.RoundToInt(CrystalBaseWood  * decay);
        int stone = Mathf.RoundToInt(CrystalBaseStone * decay);
        int water = Mathf.RoundToInt(CrystalBaseWater * decay);
        int bread = Mathf.RoundToInt(CrystalBaseBread * decay);
        int iron  = Mathf.RoundToInt(CrystalBaseIron  * decay);

        res.Wood  += wood;
        res.Stone += stone;
        res.Water += water;
        res.Bread += bread;
        res.Iron  += iron;

        Debug.Log($"[EconomySystem] {team} クリスタル序盤収入(T{turn}): 木+{wood} 石+{stone} 水+{water} パン+{bread} 鉄+{iron}");
    }

    // ==================================================================
    //  1. 建築物の生産・維持費・特殊効果
    // ==================================================================
    private void PrepareBuildings(Team team, FactionState.ResourceData res)
    {
        buildingActors.Clear(); buildingRecipes.Clear();
        int citizenCapacity = 0, resourceCapacity = 0, barracksXP = 0;
        Transform parent = buildSystem.GetBuildingParent(team);
        if (parent != null) foreach (Transform child in parent)
        {
            var actor = child.GetComponent<Status>();
            if (actor == null || !actor.IsAlive) continue;
            var recipe = FacilityData.GetLevel(actor, actor.Level);
            var kind = actor.facilityKind;
            if (kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse) citizenCapacity += recipe.SpecialValue;
            if (kind == FacilityKind.Warehouse) resourceCapacity += recipe.SpecialValue;
            if (kind == FacilityKind.Barracks) barracksXP += recipe.SpecialValue;
            actor.BuildingOperationAvailable = true;
            bool special = kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse
                || kind == FacilityKind.Warehouse || kind == FacilityKind.Barracks;
            if (special && actor.AuthoredFacility == null) continue;
            if (!FacilityData.CanAffordProduction(res, recipe.Maintenance))
            {
                actor.BuildingOperationAvailable = false;
                Debug.Log($"[EconomySystem] {FacilityData.DisplayName(actor)} Lv{actor.Level}: 維持費不足");
            }
            else FacilityData.ConsumeProduction(res, recipe.Maintenance);
            buildingActors.Add(actor); buildingRecipes.Add(recipe);
        }
        var nation = factionState.GetNation(team);
        nation.CitizenCapacity = citizenCapacity; nation.ResourceCapacity = resourceCapacity; nation.BarracksXP = barracksXP;
    }

    private void ProcessBuildingProduction(Team team, FactionState.ResourceData res)
    {
        int turn = factionState.GetNation(team).TurnsAlive;
        for (int i = 0; i < buildingActors.Count; i++)
        {
            var actor = buildingActors[i]; var recipe = buildingRecipes[i];
            if (actor == null || !actor.IsAlive || !actor.BuildingOperationAvailable || !recipe.HasProduction
                || !FacilityData.IsProductionTurn(turn, recipe)) continue;
            if (!FacilityData.CanAffordProduction(res, recipe.Input)) continue;
            FacilityData.ConsumeProduction(res, recipe.Input);
            FacilityData.AddProduction(res, recipe.Output);
            if (recipe.BonusChance1 > 0 && Random.value < recipe.BonusChance1)
                FacilityData.AddProduction(res, recipe.BonusOutput1);
            if (recipe.BonusChance2 > 0 && Random.value < recipe.BonusChance2)
                FacilityData.AddProduction(res, recipe.BonusOutput2);
        }
    }

    private void ProcessCitizenGrowth(Team team, FactionState.ResourceData res)
    {
        if (res.Bread > 0 && res.Citizen < factionState.GetCitizenCap(team))
        { res.Bread--; res.Citizen++; }
    }

    // ==================================================================
    //  2. ユニット維持費（通常駒はLv1から毎ターン資源を消費）
    //  未払いターン数を Status.UpkeepUnpaidTurns で管理。
    //    1-3: ATK/DEF -10%, 4-6: -25%, 7-9: -40%, 10+: 離脱
    // ==================================================================
    private void ProcessUnitMaintenance(Team team, FactionState.ResourceData res)
    {
        if (unitSetting == null) return;

        Transform unitParent = team == Team.Player ? unitSetting.PlayerUnit : unitSetting.EnemyUnit;
        if (unitParent == null) return;

        int paidCount = 0;
        int unpaidCount = 0;
        int defectCount = 0;

        foreach (Transform child in unitParent)
        {
            var status = child.GetComponent<Status>();
            if (status == null) continue;
            if (status.type != Type.Unit) continue;
            if (status.HP <= 0) continue;

            UnitData data = status.GrowthData;
            if (data == null) unitSetting.UnitDataMap.TryGetValue(status.kind, out data);
            if (data == null)
            {
                status.UpkeepUnpaidTurns = 0;
                continue;
            }

            var upkeep = data.GetUpkeep(status.Level);
            if (upkeep.IsEmpty)
            {
                status.UpkeepUnpaidTurns = 0;
                continue;
            }

            if (FacilityData.CanAffordProduction(res, upkeep))
            {
                FacilityData.ConsumeProduction(res, upkeep);
                status.UpkeepUnpaidTurns = 0;
                paidCount++;
            }
            else
            {
                status.UpkeepUnpaidTurns = Mathf.Clamp(status.UpkeepUnpaidTurns, 0, GameConstants.UpkeepPenaltyDefectTurns) + 1;
                unpaidCount++;
                if (status.UpkeepUnpaidTurns >= GameConstants.UpkeepPenaltyDefectTurns)
                {
                    // 10ターン不足 → 離脱（占有セル解除・Lv1リセット込みの共通死亡処理を使う）
                    Debug.Log($"[EconomySystem] {status.kind} Lv{status.Level} が維持費不足で離脱");
                    status.ApplyDamage(status.HP);
                    status.HandleDeathIfDead();
                    defectCount++;
                }
                else
                {
                    Debug.Log($"[EconomySystem] {status.kind} Lv{status.Level} 維持費不足 ({status.UpkeepUnpaidTurns}T)");
                }
            }
        }

        if (paidCount > 0 || unpaidCount > 0 || defectCount > 0)
            Debug.Log($"[EconomySystem] {team} ユニット維持費: 支払{paidCount}, 不足{unpaidCount}, 離脱{defectCount}");
    }

    // ==================================================================
    //  3. 市民パン消費（全市民が毎ターンパンを消費。不足分は市民が減少）
    // ==================================================================
    /// <summary>パン不足が何ターン連続したら市民が減少するか</summary>
    private const int StarvationGraceTurns = 10;

    private void ProcessCitizenBread(Team team, FactionState.ResourceData res)
    {
        if (res.Citizen <= 0) return;

        var nation = factionState.GetNation(team);
        int breadCost = CitizenBreadCost;
        int totalBreadNeeded = res.Citizen * breadCost;

        if (res.Bread >= totalBreadNeeded)
        {
            // 全市民を養える → 飢餓カウンターをリセット
            res.Bread -= totalBreadNeeded;
            nation.StarvationCounter = 0;
            Debug.Log($"[EconomySystem] {team} 市民パン消費: {res.Citizen}人×{BreadPerCitizen}パン = {totalBreadNeeded}パン消費  残パン={res.Bread}");
        }
        else
        {
            // パン不足：持っているパンは全消費するが、市民減少は猶予期間後
            int fedCitizens = breadCost > 0 ? res.Bread / breadCost : res.Citizen;
            res.Bread = 0;
            nation.StarvationCounter++;

            int graceTurns = Mathf.Clamp(GameAuthoringRules.Active?.starvationGraceTurns ?? StarvationGraceTurns, 1, 100);
            if (nation.StarvationCounter >= graceTurns)
            {
                // 猶予期間を超えた → 市民が離脱
                int starved = res.Citizen - fedCitizens;
                res.Citizen = fedCitizens;
                Debug.Log($"[EconomySystem] {team} パン不足{nation.StarvationCounter}ターン目: 市民{starved}人離脱 (残{res.Citizen}人)");
            }
            else
            {
                int turnsLeft = graceTurns - nation.StarvationCounter;
                Debug.Log($"[EconomySystem] {team} パン不足{nation.StarvationCounter}ターン目 (あと{turnsLeft}ターンで市民減少)  市民{res.Citizen}人維持中");
            }
        }
    }

    // ==================================================================
    //  4. 市民APボーナスを FactionState に反映
    // ==================================================================
    private void UpdateCitizenAPBonus(Team team, FactionState.ResourceData res)
    {
        factionState.UpdateCitizenAPBonus(team);
    }

    // ==================================================================
    //  資源上限クランプ
    // ==================================================================
    private void ClampResources(FactionState.ResourceData res, int warehouseBonus)
    {
        int cap = FactionState.BaseResourceCap + warehouseBonus;
        res.Wood     = Mathf.Min(res.Wood, cap);
        res.Stone    = Mathf.Min(res.Stone, cap);
        res.Iron     = Mathf.Min(res.Iron, cap);
        res.MagicOre = Mathf.Min(res.MagicOre, cap);
        res.Wheat    = Mathf.Min(res.Wheat, cap);
        res.Bread    = Mathf.Min(res.Bread, cap);
        res.Water    = Mathf.Min(res.Water, cap);
        // Citizen は倉庫容量の対象外（House の収容で管理）
    }
}
