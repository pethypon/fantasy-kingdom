using System.Collections.Generic;
using UnityEngine;

public static partial class SpecialSkillRuntime
{
    static bool ValidateForcedDestination(Status actor, Vector3 destination, GameSystems systems, Status ignored,
        out Vector3 resolved, out string reason)
    {
        resolved = destination; reason = null;
        var map = systems?.MapCreate; var cell = GridHelper.ToGridXZ(destination);
        if (actor == null || !actor.IsAlive || map == null || !map.TryGetHeight(cell.x, cell.z, out float height))
            return Reject(out reason, "移動先の地形がありません");
        resolved = new Vector3(cell.x, height, cell.z);
        if (map.Environment.BlocksMovement(cell.x, cell.z)) return Reject(out reason, "移動先に障害物があります");
        var iterator = CombatRegistry.Enumerate(); bool occupied = false;
        while (iterator.MoveNext())
        {
            var other = iterator.Current;
            if (other != null && other != actor && other != ignored && other.IsAlive && other.GridPosition == cell) { occupied = true; break; }
        }
        iterator.Dispose();
        return !occupied || Reject(out reason, "移動先が使用されています");
    }

    /// <summary>Authoritative forced relocation, shared by pull, charge, swap and cave completion.</summary>
    public static bool TryForcedMove(Status actor, Vector3 destination, GameSystems systems, out string reason)
    {
        if (!ValidateForcedDestination(actor, destination, systems, null, out var resolved, out reason)) return false;
        var previous = actor.transform.position;
        actor.transform.position = resolved;
        systems?.MoveGenerator?.MoveUpdate(systems.MoveGenerator.Cell(previous), systems.MoveGenerator.Cell(resolved));
        systems?.MoveUndoSystem?.Clear();
        OnMoved(actor, previous, resolved, systems);
        CombatRuleTelemetry.Record("forced_move", actor, actor);
        Refresh(systems); return true;
    }

    static bool TryForcedSwap(Status caster, Status target, GameSystems systems, out string reason)
    {
        var from = caster.transform.position; var to = target.transform.position;
        if (!ValidateForcedDestination(caster, to, systems, target, out var first, out reason)
            || !ValidateForcedDestination(target, from, systems, caster, out var second, out reason)) return false;
        // Both destinations are validated and committed before entry hazards can kill either actor.
        caster.transform.position = first; target.transform.position = second;
        systems?.MoveGenerator?.UnitPointCore(); systems?.MoveUndoSystem?.Clear();
        OnMoved(caster, from, first, systems); OnMoved(target, to, second, systems);
        CombatRuleTelemetry.Record("forced_move", caster, target, "Swap");
        Refresh(systems); return true;
    }

    static bool TryForcedBatchMove(IReadOnlyList<Status> actors, IReadOnlyList<Vector3> destinations,
        GameSystems systems, out string reason)
    {
        reason = null;
        if (actors == null || destinations == null || actors.Count != destinations.Count || systems?.MapCreate == null) return false;
        var moving = new HashSet<Status>(actors); var occupied = new HashSet<Vector3Int>();
        var resolved = new Vector3[actors.Count]; var previous = new Vector3[actors.Count];
        for (int i = 0; i < actors.Count; i++)
        {
            var actor = actors[i]; var cell = GridHelper.ToGridXZ(destinations[i]);
            if (actor == null || !actor.IsAlive || !occupied.Add(cell)
                || !systems.MapCreate.TryGetHeight(cell.x, cell.z, out float height)
                || systems.MapCreate.Environment.BlocksMovement(cell.x, cell.z)) return Reject(out reason, "強制移動先を確保できません");
            resolved[i] = new Vector3(cell.x, height, cell.z); previous[i] = actor.transform.position;
        }
        var iterator = CombatRegistry.Enumerate();
        while (iterator.MoveNext())
        {
            var other = iterator.Current;
            if (other != null && other.IsAlive && !moving.Contains(other) && occupied.Contains(other.GridPosition))
            { iterator.Dispose(); return Reject(out reason, "強制移動先に別の駒があります"); }
        }
        iterator.Dispose();
        for (int i = 0; i < actors.Count; i++) actors[i].transform.position = resolved[i];
        systems.MoveGenerator?.UnitPointCore(); systems.MoveUndoSystem?.Clear();
        for (int i = actors.Count - 1; i >= 0; i--)
        {
            OnMoved(actors[i], previous[i], resolved[i], systems);
            CombatRuleTelemetry.Record("forced_move", actors[0], actors[i], "Charge");
        }
        return true;
    }
}
