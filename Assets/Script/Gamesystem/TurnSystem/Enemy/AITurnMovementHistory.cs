using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded current-own-turn movement outcomes; never observes an occupant or creates an attack target.</summary>
public sealed class AITurnMovementHistory
{
    const int Limit = 64;
    struct Departure { public Status Actor; public Vector3Int Cell; }
    readonly List<Departure> departures = new List<Departure>(Limit);
    // A hypothetical reveal gets one chance at a previously departed cell per
    // own turn. A failed information prediction cannot repeatedly waive cycles.
    readonly List<Departure> fruitlessReturns = new List<Departure>(Limit);
    readonly List<Vector3Int> blockedDestinations = new List<Vector3Int>(Limit);
    public int Count => departures.Count;
    public int BlockedDestinationCount => blockedDestinations.Count;
    public void Clear() { departures.Clear(); blockedDestinations.Clear(); fruitlessReturns.Clear(); }

    /// <summary>Call only after the real executor reports occupancy refusal at the requested destination.</summary>
    public void RecordBlockedDestination(Vector3 requested)
    {
        var cell = GridHelper.ToGridXZ(requested);
        if (blockedDestinations.Contains(cell)) return;
        if (blockedDestinations.Count >= Limit) blockedDestinations.RemoveAt(0);
        blockedDestinations.Add(cell);
    }
    /// <summary>A successful attack/skill may destroy or relocate an occupant; no hidden re-query is needed.</summary>
    public void InvalidateBlockedDestinations() => blockedDestinations.Clear();

    public void Record(Status actor, Vector3 before, Vector3 after, bool success, int actualNewlyExplored = 0)
    {
        if (!success || actor == null || GridHelper.ToGridXZ(before) == GridHelper.ToGridXZ(after)) return;
        var destination = GridHelper.ToGridXZ(after);
        bool returned = false;
        foreach (var departure in departures)
            if (departure.Actor == actor && departure.Cell == destination) { returned = true; break; }
        if (returned)
        {
            if (actualNewlyExplored > 0)
            {
                for (int i = fruitlessReturns.Count - 1; i >= 0; i--)
                    if (fruitlessReturns[i].Actor == actor && fruitlessReturns[i].Cell == destination)
                        fruitlessReturns.RemoveAt(i);
            }
            else if (!FruitlessReturn(actor, destination))
            {
                if (fruitlessReturns.Count >= Limit) fruitlessReturns.RemoveAt(0);
                fruitlessReturns.Add(new Departure { Actor = actor, Cell = destination });
            }
        }
        blockedDestinations.Remove(GridHelper.ToGridXZ(before));
        if (departures.Count >= Limit) departures.RemoveAt(0);
        departures.Add(new Departure { Actor = actor, Cell = GridHelper.ToGridXZ(before) });
    }

    bool FruitlessReturn(Status actor, Vector3Int destination)
    {
        foreach (var entry in fruitlessReturns)
            if (entry.Actor == actor && entry.Cell == destination) return true;
        return false;
    }

    public bool ShouldReject(AIAction action, AIBoardState board)
    {
        if (action?.Unit == null) return false;
        bool ordinaryAdvance = action.ActionType == AIActionType.Move || action.ActionType == AIActionType.Support || action.ActionType == AIActionType.Surround;
        if (!ordinaryAdvance && action.ActionType != AIActionType.Retreat && action.ActionType != AIActionType.DefenseRepos) return false;
        var destination = GridHelper.ToGridXZ(action.TargetPos);
        if (blockedDestinations.Contains(destination)) return true;
        if (!ordinaryAdvance) return false;
        bool returning = false;
        for (int i = departures.Count - 1; i >= 0; i--)
            if (departures[i].Actor == action.Unit && departures[i].Cell == destination) { returning = true; break; }
        if (!returning) return false;
        if (board == null) return true;
        var current = action.Unit.transform.position;
        if (board.EstimateCounterDamageAt(action.TargetPos, action.Unit) < board.EstimateCounterDamageAt(current, action.Unit)) return false;
        // A full barrier may make both HP forecasts zero. Compare the same observed threats
        // without the finite barrier budget before rejecting a safer return.
        if (AIBoardQuery.EstimateCounterDamageAt(board, action.TargetPos, action.Unit, true)
            < AIBoardQuery.EstimateCounterDamageAt(board, current, action.Unit, true)) return false;
        if (OpensObservedAttack(action, board)) return false;
        return FruitlessReturn(action.Unit, destination)
            || board.EstimateNewVisionCells(action.TargetPos, action.Unit) <= 0;
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
