using UnityEngine;

public partial class AIBoardState
{
    /// <summary>Last observed opponent commander, never its hidden live Transform or HP.</summary>
    public bool GetLastKnownPrincipalCommander(out Vector3Int position, out int observedTurn)
    {
        position = default; observedTurn = -1;
        if (ReconThreatLevel < 4) return false;
        int lifetime = ReconThreatLevel >= 10 ? 5 : 3;
        foreach (var entry in _lastKnownPlayerPositions)
        {
            var memory = entry.Value;
            int age = TurnCount - memory.Turn;
            if (!memory.Valid || !memory.WasPrincipalCommander || age < 0 || age >= lifetime
                || IsVisibleToEnemy(memory.Position) || memory.Turn <= observedTurn) continue;
            position = memory.Position; observedTurn = memory.Turn;
        }
        return observedTurn >= 0;
    }
}
