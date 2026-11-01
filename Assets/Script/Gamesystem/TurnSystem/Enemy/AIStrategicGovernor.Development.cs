using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct AIArmyDevelopmentNeed
{
    public readonly UnitData Definition;
    public readonly int DesiredCount, MissingCount, NeededFromTurn;
    public AIArmyDevelopmentNeed(UnitData definition, int desired, int missing, int fromTurn)
    { Definition = definition; DesiredCount = desired; MissingCount = missing; NeededFromTurn = fromTurn; }
}

public sealed partial class AIStrategicGovernor
{
    // A bounded, value-like summary reconstructed from configuration, own army and observed operations.
    // It is not an additional action driver or an all-at-once resource reservation.
    readonly List<AIArmyDevelopmentNeed> developmentPlan = new List<AIArmyDevelopmentNeed>(18);
    readonly HashSet<Kind> developmentKinds = new HashSet<Kind>();
    readonly AIAction recruitmentProbe = new AIAction { ActionType = AIActionType.Summon };
    public IReadOnlyList<AIArmyDevelopmentNeed> DevelopmentPlan => developmentPlan;
    public int DevelopmentFightingTarget { get; private set; }
    public float DevelopmentCitizenDemand { get; private set; }
    public float DevelopmentRequiredCitizenStock { get; private set; }

    int PrepareArmyDevelopment(AIBoardState board, int shortTermTarget, bool operation)
    {
        var config = AIHousingConfig.Active;
        developmentPlan.Clear(); developmentKinds.Clear();
        DevelopmentCitizenDemand = DevelopmentRequiredCitizenStock = 0;
        int target = shortTermTarget;
        if (board.TurnCount >= Mathf.Clamp(config.StandingArmyStartTurn, 1, 100))
            target = Mathf.Max(target, Mathf.Clamp(config.MinimumStandingFighters, 2, 12));
        if (config.EnableDevelopmentGrowth && (operation || board.TurnCount >= Mathf.Clamp(config.StandingArmyStartTurn, 1, 100)))
        {
            long planningTurn = (long)Mathf.Max(0, board.TurnCount) + config.DevelopmentHorizon;
            long elapsed = Math.Max(0, planningTurn - Mathf.Clamp(config.DevelopmentGrowthStartTurn, 1, 200));
            long extra = elapsed / Mathf.Clamp(config.DevelopmentGrowthIntervalTurns, 1, 100)
                * Mathf.Clamp(config.DevelopmentFightersPerInterval, 0, 8);
            target = (int)Math.Min(Mathf.Clamp(config.DevelopmentMaximumFighters, 2, 64), (long)target + extra);
            target = Mathf.Max(shortTermTarget, target);
        }
        DevelopmentFightingTarget = target;
        var roster = config.DevelopmentRoster;
        int limit = Mathf.Min(32, roster?.Length ?? 0);
        for (int i = 0; i < limit; i++)
        {
            var request = roster[i];
            if (request == null || !Enum.IsDefined(typeof(Kind), request.Kind) || developmentKinds.Contains(request.Kind)) continue;
            int fromTurn = Mathf.Clamp(request.NeededFromTurn, 1, 1000);
            if ((long)fromTurn > (long)board.TurnCount + config.DevelopmentHorizon) continue;
            var definition = board.ResolveUnitDefinition(request.Kind);
            if (definition == null || !definition.IsValidForAuthoring || !definition.AvailableFor(board.ActorTeam)
                || RecruitmentRole(definition, definition.kind) == null)
            { recruitmentReasons["development:" + request.Kind] = "development_definition_missing"; continue; }
            int desired = Mathf.Clamp(request.DesiredCount, 0, AIActionGenerator.GetMaxUnitCount(request.Kind));
            recruitmentCounts.TryGetValue(request.Kind, out int present);
            int missing = Mathf.Max(0, desired - present);
            if (missing == 0) continue;
            // An out-of-horizon, invalid or already satisfied row must not hide a later
            // actionable request for this kind. Keep only the first usable requirement.
            developmentKinds.Add(request.Kind);
            developmentPlan.Add(new AIArmyDevelopmentNeed(definition, desired, missing, fromTurn));
            DevelopmentCitizenDemand += (long)Mathf.Max(0, definition.costCitizen) * missing;
            // Population is a spendable stock. Sequential purchases do not require the sum
            // of an army's citizen costs to exist simultaneously, and houses do not accelerate growth.
            DevelopmentRequiredCitizenStock = Mathf.Max(DevelopmentRequiredCitizenStock,
                (float)Math.Min(int.MaxValue, (long)Mathf.Max(0, definition.costCitizen) + RecruitmentCitizenReserve) - config.FreeTarget);
            recruitmentReasons["development:" + request.Kind] = fromTurn > board.TurnCount
                ? "development_capacity_preparation" : "development_role_due";
        }
        return target;
    }

    void AddDueDevelopmentRecruits(AIBoardState board)
    {
        foreach (var need in developmentPlan)
        {
            if (need.NeededFromTurn > board.TurnCount) continue;
            while (recruitmentPlan.Count < 3)
            {
                recruitmentCounts.TryGetValue(need.Definition.kind, out int present);
                if (present >= need.DesiredCount) break;
                AddRecruitmentNeed(need.Definition, RecruitmentRole(need.Definition, need.Definition.kind), "development_role_due");
            }
        }
    }

    void AppendDevelopmentForecast()
    {
        // Housing's maintenance preview must include the expensive future purchase that
        // justified its capacity, even if that purchase is not yet due in the short shopping list.
        if (recruitmentForecastUnits.Count >= 3) return;
        UnitData next = null;
        foreach (var need in developmentPlan)
            if (!recruitmentForecastUnits.Contains(need.Definition)
                && (next == null || need.Definition.costCitizen > next.costCitizen)) next = need.Definition;
        if (next != null) recruitmentForecastUnits.Add(next);
    }
}
