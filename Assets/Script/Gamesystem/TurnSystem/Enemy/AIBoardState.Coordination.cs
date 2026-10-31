using System;
using UnityEngine;

public partial class AIBoardState
{
    internal int CoordinationAttackCost(Status actor, bool afterMove)
    {
        if (actor == null || _apSystem == null) return int.MaxValue;
        long fatigue = Mathf.Max(0, actor.Fatigue);
        if (afterMove) fatigue += 1L + GameConstants.GetExtraFatiguePerAction(actor.kind);
        return _apSystem.CalcAttackCostPreview(actor, (int)Math.Min(int.MaxValue, fatigue));
    }
    internal int CoordinationReturnCost(Status actor, Vector3 from, Vector3 to)
    {
        if (actor == null || _apSystem == null) return int.MaxValue;
        long fatigue = Mathf.Max(0, actor.Fatigue) + 2L * (1L + GameConstants.GetExtraFatiguePerAction(actor.kind));
        return _apSystem.CalcMoveCostPreview(actor, from, to, (int)Math.Min(int.MaxValue, fatigue), true, false);
    }
}
