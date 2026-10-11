using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Responsibility: release obsolete own-unit references without expiring Fog observations.
/// Lifetime/Owner: one battle / AICommander. Maximum retained items: scratch keys <= tracked own units;
/// each position list retains the same four entries used by the existing anti-oscillation evaluator.
/// Cleanup timing: configured own-turn boundary and battle disposal. Failure behavior: caller isolates
/// maintenance failures; no enemy information is queried and no live movement history is dropped.
/// </summary>
public sealed class AIRuntimeMaintenance
{
    // The evaluator reads every existing entry. Four is the established decision horizon, not a performance guess.
    public const int PositionHistoryLimit = 4;
    readonly List<Status> removedUnits = new List<Status>(32);

    /// <summary>Removes dead, destroyed, or definitively detached own units. Never inspects hidden hostile entities.</summary>
    public int PrunePositionHistory(Dictionary<Status, List<Vector3Int>> histories, Team owner, Transform ownParent)
    {
        if (histories == null || histories.Count == 0) return 0;
        removedUnits.Clear();
        foreach (var pair in histories)
        {
            var unit = pair.Key;
            bool departed = unit == null || !unit.gameObject.activeInHierarchy || unit.HP <= 0
                || unit.team != owner || unit.type != Type.Unit
                || ownParent != null && !unit.transform.IsChildOf(ownParent);
            if (departed) removedUnits.Add(unit);
        }
        int removed = removedUnits.Count;
        for (int i = 0; i < removedUnits.Count; i++) histories.Remove(removedUnits[i]);
        // Clear releases the reference slots too; capacity can be reused without retaining destroyed objects.
        removedUnits.Clear();
        return removed;
    }

    /// <summary>Records exactly the prior four-position horizon, preserving recency and penalty calculations.</summary>
    public static void RecordPosition(Dictionary<Status, List<Vector3Int>> histories, Status unit, Vector3Int position)
    {
        if (histories == null || unit == null) return;
        if (!histories.TryGetValue(unit, out var history) || history == null)
            histories[unit] = history = new List<Vector3Int>(PositionHistoryLimit);
        if (history.Count >= PositionHistoryLimit)
            history.RemoveRange(0, history.Count - PositionHistoryLimit + 1);
        history.Add(position);
    }

    /// <summary>Releases maintenance scratch references when the owning battle ends.</summary>
    public void Clear() => removedUnits.Clear();
}
