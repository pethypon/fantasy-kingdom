using System.Collections.Generic;
using UnityEngine;

public readonly struct AIRecruitmentNeed
{
    public readonly UnitData Definition;
    public readonly string Role, Reason;
    internal AIRecruitmentNeed(UnitData definition, string role, string reason)
    { Definition = definition; Role = role; Reason = reason; }
}

public sealed partial class AIStrategicGovernor
{
    // Reconstructed from own units and current observations each board generation. This is a
    // bounded shopping order, not another persistent army or a driver that can bypass legality.
    readonly List<AIRecruitmentNeed> recruitmentPlan = new List<AIRecruitmentNeed>(3);
    readonly List<UnitData> recruitmentDefinitions = new List<UnitData>(32);
    readonly List<UnitData> recruitmentForecastUnits = new List<UnitData>(3);
    readonly Dictionary<string, string> recruitmentReasons = new Dictionary<string, string>(4);
    readonly Dictionary<Kind, int> recruitmentCounts = new Dictionary<Kind, int>();
    readonly Dictionary<UnitData, string> recruitmentBlocks = new Dictionary<UnitData, string>();
    AIBoardState recruitmentBoard;
    public IReadOnlyList<AIRecruitmentNeed> RecruitmentPlan => recruitmentPlan;
    public IReadOnlyDictionary<string, string> RecruitmentReasons => recruitmentReasons;
    public float RecruitmentCitizenDemand { get; private set; }
    // Ordered recruits replenish between purchases. A house cannot make that one-per-turn
    // growth faster, so capacity must cover a purchase plus reserve, not the whole order at once.
    public float RecruitmentRequiredCitizenStock { get; private set; }
    public bool HasPreparedRecruitment(AIBoardState board) => recruitmentBoard == board;

    void PrepareRecruitment(AIBoardState board)
    {
        recruitmentBoard = board; recruitmentPlan.Clear(); recruitmentForecastUnits.Clear(); recruitmentReasons.Clear(); recruitmentCounts.Clear(); recruitmentBlocks.Clear();
        RecruitmentCitizenDemand = RecruitmentRequiredCitizenStock = 0;
        board.CollectRecruitDefinitions(recruitmentDefinitions);
        int frontline = 0, ranged = 0, healers = 0, scouts = 0, fighters = 0, wounded = 0, observedFighters = 0;
        foreach (var actor in board.AliveEnemyUnits)
        {
            if (actor == null || !actor.IsAlive || actor.type != Type.Unit || MatchObjectiveRules.IsCommander(actor)) continue;
            recruitmentCounts.TryGetValue(actor.kind, out int count); recruitmentCounts[actor.kind] = count + 1;
            string role = AIHealingCapability.HasHealing(actor) ? "healer" : NonHealingRecruitmentRole(actor.GrowthData, actor.kind);
            if (role == "scout") scouts++;
            else if (role == "healer") healers++;
            else if (role == "frontline" || role == "ranged")
            {
                fighters++; if (role == "frontline") frontline++; else ranged++;
                if (actor.MaxHP > 0 && actor.HP < actor.MaxHP * .8f) wounded++;
            }
        }
        foreach (var actor in board.AlivePlayerUnits)
            if (actor != null && actor.IsAlive && actor.team == board.OpponentTeam && actor.type == Type.Unit
                && !MatchObjectiveRules.IsCommander(actor) && board.IsSpecialActorObserved(actor)
                && BoardActionProfile.For(actor)?.CanAttack != false) observedFighters++;
        bool expedition = board.HasPrincipalOpponentContact || TerritoryIntel.HasUsableTerritory || board.OperationRequiredCitizenDemand > 0;
        int fightingTarget = PrepareArmyDevelopment(board, Mathf.Max(2, Mathf.Min(6, observedFighters)), expedition);
        AddOperationSquadRecruits(board, ref frontline, ref ranged, ref healers, ref scouts, ref fighters);
        // Rescue/support a force that already exists before buying another interchangeable fighter.
        if (healers == 0 && fighters >= 3 && (wounded > 0 || expedition))
            if (AddRecruitmentRole(board, "healer", wounded > 0 ? "wounded_army_without_healer" : "expedition_without_healer")) healers++;
        if (frontline == 0 && AddRecruitmentRole(board, "frontline", "frontline_missing")) { frontline++; fighters++; }
        if (scouts == 0 && (!board.PlayerCrystalVisible && board.GetExplorationRatio() < 1f || TerritoryIntel.HasUsableTerritory && !HasActiveTerritoryAdvance))
            if (AddRecruitmentRole(board, "scout", "reconnaissance_role_missing")) scouts++;
        if (ranged == 0 && fighters >= 2 && AddRecruitmentRole(board, "ranged", "ranged_support_missing")) { ranged++; fighters++; }
        AddDueDevelopmentRecruits(board);
        while (fighters < fightingTarget && recruitmentPlan.Count < 3)
        {
            string role = frontline <= ranged ? "frontline" : "ranged";
            if (!AddRecruitmentRole(board, role, "observed_force_or_core_replacement")) break;
            fighters++; if (role == "frontline") frontline++; else ranged++;
        }
        int reserve = RecruitmentCitizenReserve;
        foreach (var need in recruitmentPlan)
        {
            RecruitmentCitizenDemand += Mathf.Max(0, need.Definition.costCitizen);
            RecruitmentRequiredCitizenStock = Mathf.Max(RecruitmentRequiredCitizenStock,
                (float)System.Math.Min(int.MaxValue, (long)Mathf.Max(0, need.Definition.costCitizen) + reserve) - AIHousingConfig.Active.FreeTarget);
        }
        RecruitmentRequiredCitizenStock = Mathf.Max(RecruitmentRequiredCitizenStock, DevelopmentRequiredCitizenStock);
        AppendDevelopmentForecast();
    }

    bool AddRecruitmentRole(AIBoardState board, string role, string reason)
    {
        if (recruitmentPlan.Count >= 3) return false;
        UnitData best = null; float bestCost = float.PositiveInfinity;
        bool capped = false;
        foreach (var definition in recruitmentDefinitions)
        {
            if (definition == null || !definition.IsValidForAuthoring || !definition.AvailableFor(board.ActorTeam)
                || RecruitmentRole(definition, definition.kind) != role) continue;
            recruitmentCounts.TryGetValue(definition.kind, out int count);
            if (count >= AIActionGenerator.GetMaxUnitCount(definition.kind)) { capped = true; continue; }
            var cost = AIBasicResourceEconomy.UnitCost(definition);
            float investment = Mathf.Max(0, definition.costAP);
            for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++) investment += StrategicEconomyForecast.Amount(cost, i);
            // Compare real costs and upkeep, not maximum ATK. Observed barrier specialists retain
            // the existing bounded preference; a different Kind does not manufacture a new role.
            var upkeep = definition.GetUpkeep(1);
            for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
                investment += StrategicEconomyForecast.Amount(upkeep, i) * forecast.ForecastTurns;
            investment -= AIBuildEvaluator.ObservedBarrierRecruitmentBonus(definition.kind, board);
            if (investment > bestCost || investment == bestCost && best != null && (int)definition.kind >= (int)best.kind) continue;
            best = definition; bestCost = investment;
        }
        if (best == null) { recruitmentReasons[role] = capped ? "recruit_kind_cap" : "recruit_definition_missing"; return false; }
        AddRecruitmentNeed(best, role, reason);
        recruitmentReasons[role] = reason;
        return true;
    }

    void AddRecruitmentNeed(UnitData definition, string role, string reason)
    {
        recruitmentCounts.TryGetValue(definition.kind, out int existing); recruitmentCounts[definition.kind] = existing + 1;
        recruitmentPlan.Add(new AIRecruitmentNeed(definition, role, reason)); recruitmentForecastUnits.Add(definition);
    }

    public static string RecruitmentRole(UnitData definition, Kind kind)
    {
        if (kind == Kind.King || kind == Kind.Boss || kind == Kind.Crystal || kind == Kind.SubCrystal) return null;
        return AIHealingCapability.HasHealing(definition) ? "healer" : NonHealingRecruitmentRole(definition, kind);
    }

    static string NonHealingRecruitmentRole(UnitData definition, Kind kind)
    {
        if (kind == Kind.Scout) return "scout";
        if (kind == Kind.King || kind == Kind.Boss || kind == Kind.Crystal || kind == Kind.SubCrystal
            || definition?.actionProfile?.CanAttack == false) return null;
        switch (UnitRoster.EvaluationBaseKind(kind))
        {
            case Kind.Knight: case Kind.Guardian: case Kind.Assassin: return "frontline";
            case Kind.Archer: case Kind.Magic: case Kind.Crossbow: case Kind.Magicsniper: case Kind.Bomber: return "ranged";
            default: return null;
        }
    }

    public string RecruitmentBlockReason(AIAction action, AIBoardState board)
    {
        if (action == null || action.ActionType != AIActionType.Summon || board == null) return "recruit_not_a_summon";
        var definition = action.SummonDefinition ?? board.ResolveUnitDefinition(action.SummonKind);
        if (definition == null || !definition.IsValidForAuthoring) return "recruit_definition_missing";
        if (recruitmentBlocks.TryGetValue(definition, out string cached)) return cached;
        string result = AssessRecruitmentBlock(action, board, definition);
        recruitmentBlocks[definition] = result;
        return result;
    }

    string AssessRecruitmentBlock(AIAction action, AIBoardState board, UnitData definition)
    {
        int existing = 0;
        foreach (var actor in board.AliveEnemyUnits)
            if (actor != null && actor.IsAlive && actor.team == board.ActorTeam && actor.type == Type.Unit
                && actor.kind == definition.kind) existing++;
        if (existing >= AIActionGenerator.GetMaxUnitCount(definition.kind)) return "recruit_kind_cap";
        if (board.SummonablePositions.Count == 0) return "recruit_spawn_cell_missing";
        if (action.APCost > board.EnemyAP) return "recruit_ap_shortage";
        if (definition.costCitizen > (board.EnemyResources?.Citizen ?? 0)) return "recruit_citizen_shortage";
        if (!board.CanSummonDefinition(definition)) return "recruit_resources_or_legality";
        var projected = forecast.Simulate(board, action);
        if (projected.BreadPaymentFailed) return "recruit_bread_forecast_fail";
        if (projected.UpkeepPaymentFailed) return "recruit_upkeep_forecast_fail";
        if (projected.ImportantProductionStopped) return "recruit_production_forecast_fail";
        if (projected.BreadCoverageTurns < BreadReserveTurns) return "recruit_bread_reserve_fail";
        return BasicResources.AllowSpend(action, false, out string reason) ? null : reason;
    }

    bool IsNextPlannedRecruit(AIAction action, AIBoardState board)
    {
        if (action == null || action.ActionType != AIActionType.Summon || board == null
            || recruitmentBoard != board || recruitmentPlan.Count == 0) return false;
        var definition = action.SummonDefinition ?? board.ResolveUnitDefinition(action.SummonKind);
        if (definition == null) return false;
        string role = RecruitmentRole(definition, definition.kind);
        // Retain the bounded order and its supply forecast. A different definition may fill
        // the first blocked role only after its own complete official spend forecast passes.
        foreach (var need in recruitmentPlan)
        {
            if (need.Definition == definition) return RecruitmentBlockReason(action, board) == null;
            recruitmentProbe.SummonDefinition = need.Definition; recruitmentProbe.SummonKind = need.Definition.kind;
            recruitmentProbe.APCost = need.Definition.costAP;
            if (RecruitmentBlockReason(recruitmentProbe, board) == null) return false;
            if (role != null && need.Role == role && RecruitmentBlockReason(action, board) == null) return true;
        }
        return false;
    }
    bool IsSafePlannedRecruit(AIAction action, AIBoardState board)
        => Mode != StrategicMode.EmergencyDefense && Mode != StrategicMode.EconomicRecovery
            && IsNextPlannedRecruit(action, board) && RecruitmentBlockReason(action, board) == null;

    public float RecruitmentScore(AIAction action, AIBoardState board)
        // At P3 this need can compete with staging/exploration. P0/P1 survival and P2 current
        // battle opportunities still precede it. Other legal summons keep their original scores.
        => IsSafePlannedRecruit(action, board) ? 30f : 0;

    public RecruitmentForecastResult ForecastRecruitment(AIBoardState board, AIAction investment = null)
        => forecast.SimulateRecruitment(board, recruitmentForecastUnits, investment, AIHousingConfig.Active.Horizon,
            RecruitmentCitizenReserve);
    static int RecruitmentCitizenReserve => Mathf.Max(AIHousingConfig.Active.FreeTarget,
        Mathf.Max(AIBasicResourceSettings.Active.minimumReserve.Citizen, AIEconomySettings.Active.minimumOperationalBuffer.Citizen));
}
