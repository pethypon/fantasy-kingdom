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
    /// <summary>Refresh capacities only. Never consumes materials, pays maintenance, or advances a production turn.</summary>
    public void RecalculateCapacity(Team team)
        => RecalculateCapacity(buildSystem, factionState, team);

    /// <summary>Use the builder's own faction, including isolated simulations and developer fixtures.</summary>
    public static void RecalculateCapacity(BuildSystem buildSystem, FactionState factionState, Team team)
    {
        if (buildSystem == null || factionState == null || (team != Team.Player && team != Team.Enemy)) return;
        long citizens = 0, storage = 0;
        var parent = buildSystem.GetBuildingParent(team);
        if (parent != null) foreach (Transform child in parent)
        {
            var actor = child.GetComponent<Status>(); if (actor == null || !actor.IsAlive) continue;
            var recipe = FacilityData.GetLevel(actor, actor.Level);
            if (actor.facilityKind == FacilityKind.House || actor.facilityKind == FacilityKind.LuxuryHouse) citizens += recipe.SpecialValue;
            if (actor.facilityKind == FacilityKind.Warehouse) storage += recipe.SpecialValue;
        }
        var nation = factionState.GetNation(team);
        nation.CitizenCapacity = (int)System.Math.Min(int.MaxValue - FactionState.BaseCitizenCap, citizens);
        nation.ResourceCapacity = (int)System.Math.Min(int.MaxValue - FactionState.BaseResourceCap, storage);
        nation.BarracksXP = 0; // Legacy save field only. Buildings retain their upgrade levels.
    }

    /// <summary>
    /// 前払い維持費 → クリスタル収入と建物生産 → 市民成長・食料の順。
    /// 当ターンの生産物で、すでに失敗した維持費の支払いを帳消しにしない。
    /// </summary>
    public void ProcessTurn(Team team)
    {
        if (buildSystem == null || factionState == null) return;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;

        { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[EconomySystem] === {team} ターン経済処理開始 === パン={res.Bread} 市民={res.Citizen}"); }

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
        if (IsResourceCapacityEnforced(warehouseBonus))
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
        var income = GetCrystalIncome(nation.TurnsAlive);
        int turn = nation.TurnsAlive;
        int wood = income.Wood, stone = income.Stone, water = income.Water, bread = income.Bread, iron = income.Iron;
        if (wood == 0 && stone == 0 && water == 0 && bread == 0 && iron == 0 && income.MagicOre == 0 && income.Wheat == 0 && income.Citizen == 0) return;

        res.Wood = AddOpeningStock(res.Wood, wood);
        res.Stone = AddOpeningStock(res.Stone, stone);
        res.Water = AddOpeningStock(res.Water, water);
        res.Bread = AddOpeningStock(res.Bread, bread);
        res.Iron = AddOpeningStock(res.Iron, iron);
        res.MagicOre = AddOpeningStock(res.MagicOre, income.MagicOre);
        res.Wheat = AddOpeningStock(res.Wheat, income.Wheat); res.Citizen = AddOpeningStock(res.Citizen, income.Citizen);

        { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[EconomySystem] {team} クリスタル序盤収入(T{turn}): 木+{wood} 石+{stone} 水+{water} パン+{bread} 鉄+{iron}"); }
    }

    /// <summary>The game and every forecast resolve the same decaying opening support.</summary>
    public static FacilityData.ProductionBundle GetCrystalIncome(int ownTurn)
    {
        var rules = GameAuthoringRules.Active;
        int turns = Mathf.Clamp(rules != null ? rules.crystalIncomeTurns : CrystalIncomeTurns, 1, 30);
        float decay = Mathf.Clamp01(1f - Mathf.Max(0, ownTurn) / (float)turns);
        var source = rules != null ? rules.crystalStartIncome : new FacilityData.ProductionBundle
        { Wood = CrystalBaseWood, Stone = CrystalBaseStone, Water = CrystalBaseWater, Bread = CrystalBaseBread, Iron = CrystalBaseIron };
        return new FacilityData.ProductionBundle
        {
            Wood = ScaledOpeningIncome(source.Wood, decay), Stone = ScaledOpeningIncome(source.Stone, decay),
            Water = ScaledOpeningIncome(source.Water, decay), Bread = ScaledOpeningIncome(source.Bread, decay),
            Iron = ScaledOpeningIncome(source.Iron, decay),
            MagicOre = ScaledOpeningIncome(source.MagicOre, decay), Wheat = ScaledOpeningIncome(source.Wheat, decay),
            Citizen = ScaledOpeningIncome(source.Citizen, decay)
        };
    }

    static int AddOpeningStock(int stock, int income)
        => (int)System.Math.Min(int.MaxValue, (long)stock + income);
    static int ScaledOpeningIncome(int amount, float scale)
        => (int)System.Math.Min(int.MaxValue, System.Math.Round((double)Mathf.Max(0, amount) * scale));

    public static bool IsResourceCapacityEnforced(int warehouseBonus)
        => warehouseBonus > 0 || GameAuthoringRules.Active?.enforceResourceCapacity == true;

    // ==================================================================
    //  1. 建築物の生産・維持費・特殊効果
    // ==================================================================
    private void PrepareBuildings(Team team, FactionState.ResourceData res)
    {
        buildingActors.Clear(); buildingRecipes.Clear();
        long citizenCapacity = 0, resourceCapacity = 0;
        Transform parent = buildSystem.GetBuildingParent(team);
        if (parent != null) foreach (Transform child in parent)
        {
            var actor = child.GetComponent<Status>();
            if (actor == null || !actor.IsAlive) continue;
            var recipe = FacilityData.GetProductionLevel(actor, actor.Level, buildSystem.Territory);
            var kind = actor.facilityKind;
            if (kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse) citizenCapacity += recipe.SpecialValue;
            if (kind == FacilityKind.Warehouse) resourceCapacity += recipe.SpecialValue;
            actor.BuildingOperationAvailable = true;
            bool special = kind == FacilityKind.House || kind == FacilityKind.LuxuryHouse
                || kind == FacilityKind.Warehouse || kind == FacilityKind.Barracks;
            if (special && !FacilityData.HasAuthoredRecipe(actor)) continue;
            if (!FacilityData.CanAffordProduction(res, recipe.Maintenance))
            {
                actor.BuildingOperationAvailable = false;
                { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[EconomySystem] {FacilityData.DisplayName(actor)} Lv{actor.Level}: 維持費不足"); }
            }
            else FacilityData.ConsumeProduction(res, recipe.Maintenance);
            buildingActors.Add(actor); buildingRecipes.Add(recipe);
        }
        var nation = factionState.GetNation(team);
        nation.CitizenCapacity = (int)System.Math.Min(int.MaxValue - FactionState.BaseCitizenCap, citizenCapacity);
        nation.ResourceCapacity = (int)System.Math.Min(int.MaxValue - FactionState.BaseResourceCap, resourceCapacity);
        nation.BarracksXP = 0;
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
    //  2. 固定維持費。所有陣営の同じターンには請求/未払い損失を重複しない。
    //  未払いはHP20%損失（分母などはR2暫定設定）。能力低下・離脱は廃止。
    // ==================================================================
    private void ProcessUnitMaintenance(Team team, FactionState.ResourceData res)
    {
        if (unitSetting == null) return;

        Transform unitParent = team == Team.Player ? unitSetting.PlayerUnit : unitSetting.EnemyUnit;
        if (unitParent == null) return;

        int paidCount = 0;
        int unpaidCount = 0;
        int losses = 0;
        int ownTurn = factionState.GetNation(team).TurnsAlive;

        foreach (Transform child in unitParent)
        {
            var status = child.GetComponentInChildren<Status>();
            if (status == null) continue;
            if (status.type != Type.Unit) continue;
            if (status.HP <= 0) continue;
            if (status.LastMaintenanceRound == ownTurn) continue;
            status.LastMaintenanceRound = ownTurn;

            UnitData data = status.GrowthData;
            if (data == null) unitSetting.UnitDataMap.TryGetValue(status.kind, out data);
            if (data == null)
            {
                status.UpkeepUnpaidTurns = 0;
                continue;
            }

            var upkeep = data.GetFixedUpkeep();
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
                status.UpkeepUnpaidTurns = (int)System.Math.Min(int.MaxValue, (long)Mathf.Max(0, status.UpkeepUnpaidTurns) + 1);
                unpaidCount++;
                int damage = R2EconomyRules.UnpaidDamage(status.HP, status.MaxHP);
                // An economic HP loss has no attacking team, combat reward, barrier, link or shield interception.
                SpecialSkillRuntime.ApplyHpChange(null, status, damage, HpChangeKind.DirectHpReduction);
                if (!status.IsAlive) { status.HandleDeathIfDead(); losses++; }
                if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[EconomySystem] {status.kind} 維持費不足 ({status.UpkeepUnpaidTurns}T): HP-{damage}");
            }
        }

        if (paidCount > 0 || unpaidCount > 0)
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[EconomySystem] {team} ユニット維持費: 支払{paidCount}, 不足{unpaidCount}, HP損失による死亡{losses}"); }
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
        long totalBreadNeeded = (long)res.Citizen * breadCost;

        if (res.Bread >= totalBreadNeeded)
        {
            // 全市民を養える → 飢餓カウンターをリセット
            res.Bread -= (int)totalBreadNeeded; // The affordability check guarantees this amount fits the resource integer.
            nation.StarvationCounter = 0;
            { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[EconomySystem] {team} 市民パン消費: {res.Citizen}人×{BreadPerCitizen}パン = {totalBreadNeeded}パン消費  残パン={res.Bread}"); }
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
                { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[EconomySystem] {team} パン不足{nation.StarvationCounter}ターン目: 市民{starved}人離脱 (残{res.Citizen}人)"); }
            }
            else
            {
                int turnsLeft = graceTurns - nation.StarvationCounter;
                { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[EconomySystem] {team} パン不足{nation.StarvationCounter}ターン目 (あと{turnsLeft}ターンで市民減少)  市民{res.Citizen}人維持中"); }
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
