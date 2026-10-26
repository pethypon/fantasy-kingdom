using UnityEngine;

public sealed partial class AIStrategicGovernor
{
    // Existing shopping/forecast/payment paths remain authoritative. These are
    // missing own roles, not an extra force reservation or instant population.
    void AddOperationSquadRecruits(AIBoardState board, ref int frontline, ref int ranged,
        ref int healers, ref int scouts, ref int fighters)
    {
        var needs = board.Operations?.SquadRecruitmentNeeds;
        if (needs == null) return;
        for (int pass = 0; pass < 2 && recruitmentPlan.Count < 3; pass++)
            foreach (var need in needs)
            {
                if (recruitmentPlan.Count >= 3) break;
                if (need.MissingCount <= 0 || need.NeededFromTurn > board.TurnCount
                    || (pass == 0) != (need.Role == "healer")) continue;
                if (need.Role != "frontline" && need.Role != "ranged" && need.Role != "healer" && need.Role != "scout") continue;
                int alreadyPlanned = 0;
                foreach (var planned in recruitmentPlan) if (planned.Role == need.Role) alreadyPlanned++;
                int missing = Mathf.Clamp(need.MissingCount - alreadyPlanned, 0, 3 - recruitmentPlan.Count);
                for (int i = 0; i < missing; i++)
                {
                    if (!AddRecruitmentRole(board, need.Role, "operation_squad_role_missing")) break;
                    if (need.Role == "healer") healers++;
                    else if (need.Role == "scout") scouts++;
                    else { fighters++; if (need.Role == "frontline") frontline++; else ranged++; }
                }
            }
    }
}
