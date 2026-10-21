using System.Collections.Generic;
using UnityEngine;

/// <summary>At most 64 successful departures during one owner's turn; contains only owned actors.</summary>
public sealed class AITurnMovementHistory
{
    const int Limit = 64;
    struct Departure { public Status Actor; public Vector3Int Cell; }
    readonly List<Departure> departures = new List<Departure>(Limit);
    public int Count => departures.Count;
    public void Clear() => departures.Clear();

    public void Record(Status actor, Vector3 before, Vector3 after, bool success)
    {
        if (!success || actor == null || GridHelper.ToGridXZ(before) == GridHelper.ToGridXZ(after)) return;
        if (departures.Count >= Limit) departures.RemoveAt(0);
        departures.Add(new Departure { Actor = actor, Cell = GridHelper.ToGridXZ(before) });
    }

    public bool ShouldReject(AIAction action, AIBoardState board)
    {
        if (action?.Unit == null || action.ActionType != AIActionType.Move
            && action.ActionType != AIActionType.Support && action.ActionType != AIActionType.Surround) return false;
        var destination = GridHelper.ToGridXZ(action.TargetPos);
        bool returning = false;
        for (int i = departures.Count - 1; i >= 0; i--)
            if (departures[i].Actor == action.Unit && departures[i].Cell == destination) { returning = true; break; }
        if (!returning) return false;
        if (board == null) return true;
        if (board.EstimateNewVisionCells(action.TargetPos) > 0) return false;
        var current = action.Unit.transform.position;
        if (board.EstimateCounterDamageAt(action.TargetPos, action.Unit) < board.EstimateCounterDamageAt(current, action.Unit)) return false;
        // A full barrier may make both HP forecasts zero. Compare the same observed threats
        // without the finite barrier budget before rejecting a safer return.
        if (AIBoardQuery.EstimateCounterDamageAt(board, action.TargetPos, action.Unit, true)
            < AIBoardQuery.EstimateCounterDamageAt(board, current, action.Unit, true)) return false;
        return !OpensObservedAttack(action, board);
    }

    static bool OpensObservedAttack(AIAction action, AIBoardState board)
    {
        var actor = action.Unit;
        if (SpecialSkillRuntime.AttackBlocked(actor) || AISpecialSkillKnowledge.SniperRestrictsTargets(actor)
            || action.APCost + board.CalcAttackCost(actor) > board.EnemyAP) return false;
        foreach (var target in board.AlivePlayerUnits)
        {
            if (!board.IsSpecialActorObserved(target) || !SpecialSkillRuntime.CanNormalAttackTarget(actor, target)) continue;
            if (CanAttackFrom(actor, action.TargetPos, target.transform.position, board)
                && !CanAttackFrom(actor, actor.transform.position, target.transform.position, board)) return true;
        }
        return false;
    }

    static bool CanAttackFrom(Status actor, Vector3 from, Vector3 to, AIBoardState board)
    {
        if (!AttackPatterns.CanAttack(actor, actor.direction, to.x - from.x, to.z - from.z) || board.ReconMap == null) return false;
        int steps = Mathf.CeilToInt(GridHelper.ChebyshevDistance(from, to));
        if (steps > 64) return false;
        for (int i = 0; i <= steps; i++)
        {
            var cell = Vector3.Lerp(from, to, i / (float)Mathf.Max(1, steps));
            cell.x = Mathf.Round(cell.x); cell.z = Mathf.Round(cell.z);
            if (!board.IsTerrainKnown(cell)) return false;
            if (i > 0 && i < steps && board.KnownEnvironmentDefinition(cell)?.blocksShots == true) return false;
        }
        return MapCreate.IsArcingAttack(actor.kind, actor.facilityKind) && steps >= 2
            || board.ReconMap.HasClearTerrainLine(from, to);
    }
}
