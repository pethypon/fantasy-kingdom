using System;

/// <summary>Retains the existing operation's final objective while changing means. Does not invent victory rules.</summary>
public static class AIObjectiveReasoner
{
    public static bool TryChoose(AIObservedWorldModel world, AICognitivePlan current, bool emergency,
        bool economicRecovery, out AIWorldObjectiveValue objective)
    {
        objective = default;
        if (world == null || world.Objectives.Count == 0) return false;
        if (!emergency && !economicRecovery && current != null && current.OwnerTeam == world.ActorTeam &&
            string.Equals(current.BattleId, world.BattleId, StringComparison.Ordinal))
            foreach (var candidate in world.Objectives)
                if (candidate.Goal == current.Goal && candidate.Id == current.ObjectiveRef && Valid(candidate))
                { objective = candidate; return true; }

        bool found = false; int best = int.MaxValue;
        foreach (var candidate in world.Objectives)
        {
            if (!Valid(candidate)) continue;
            int rank = Rank(candidate.Goal, emergency, economicRecovery);
            if (!found || rank < best || rank == best && string.CompareOrdinal(candidate.Id, objective.Id) < 0)
            { found = true; best = rank; objective = candidate; }
        }
        return found;
    }

    static bool Valid(AIWorldObjectiveValue value) => value.Goal != AIOperationGoal.None &&
        !string.IsNullOrEmpty(value.Id) && value.Confidence > 0 && value.Source != AIWorldFactSource.Unknown;
    static int Rank(AIOperationGoal goal, bool emergency, bool recovery)
    {
        if (emergency) return goal == AIOperationGoal.DefendOwnCrystal ? 0 : 10;
        if (recovery) return goal == AIOperationGoal.EconomicRecovery ? 0 : 10;
        switch (goal)
        {
            case AIOperationGoal.DestroyEnemyCrystal: return 0;
            case AIOperationGoal.BreakEnemyDefense: return 1;
            case AIOperationGoal.CaptureTerritory: return 2;
            case AIOperationGoal.EliminateEnemyForce: return 3;
            case AIOperationGoal.LocateEnemyMainForce: return 4;
            case AIOperationGoal.ScoutRegion: return 5;
            default: return 6;
        }
    }
}
