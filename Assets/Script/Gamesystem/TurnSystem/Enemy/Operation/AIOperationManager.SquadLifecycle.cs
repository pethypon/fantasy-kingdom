using System;
using System.Collections.Generic;

public sealed partial class AIOperationManager
{
    readonly HashSet<string> operationSquadAssignees = new HashSet<string>(StringComparer.Ordinal);

    static bool LiveSquadActor(Status actor, AIBoardState board) => board != null && LiveSquadActor(actor, board.ActorTeam);
    internal static bool LiveSquadActor(Status actor, Team owner) => actor != null && actor.IsAlive
        && actor.team == owner && actor.type == Type.Unit && actor.gameObject.activeInHierarchy
        && !MatchObjectiveRules.IsCommander(actor);

    internal static bool CurrentSquadMembership(AIOperationSquad squad, AIOperationSquadMember member, string life)
    {
        if (!ValidSquadLife(life) || squad?.Members == null || squad.Members.Count < 1 || squad.Members.Count > 5
            || squad.Phase == AIOperationSquadPhase.Suspended || member == null || member.ActorLifeId != life
            || !Enum.IsDefined(typeof(AIOperationSquadPhase), squad.Phase)
            || !Enum.IsDefined(typeof(AIOperationSquadRole), member.Role)) return false;
        int matches = 0; bool present = false;
        foreach (var candidate in squad.Members)
            if (candidate != null && candidate.ActorLifeId == life)
            { matches++; present |= ReferenceEquals(candidate, member); }
        return present && matches == 1;
    }

    void PruneOperationSquadAssignees(AIOperationPlan plan)
    {
        operationSquadAssignees.Clear();
        for (int i = 0; i < plan.AssignedUnitLifeIds.Count;)
        {
            string life = plan.AssignedUnitLifeIds[i];
            bool duplicate = !string.IsNullOrEmpty(life) && operationSquadAssignees.Contains(life);
            bool eligible = !duplicate && ValidSquadLife(life) && features.TryOwn(life, out var actor)
                && EligibleSquadActor(actor) && (!assignments.TryGetValue(life, out var owner) || owner == plan);
            if (eligible) { operationSquadAssignees.Add(life); i++; continue; }
            plan.AssignedUnitLifeIds.RemoveAt(i);
            // A duplicate entry is removed without releasing the first valid assignment.
            if (!duplicate && life != null && assignments.TryGetValue(life, out var prior) && prior == plan) assignments.Remove(life);
        }
    }

    /// <summary>Save-boundary sanitation only. Live ownership/death is checked again at the next official observed-board refresh.</summary>
    internal static void SanitizeSquadState(AIOperationBattleState restored)
    {
        if (restored == null) return;
        var valid = new List<AIOperationSquad>(16);
        var identities = new HashSet<long>(); var members = new HashSet<string>(StringComparer.Ordinal);
        var plans = new Dictionary<long, AIOperationPlan>();
        var assignees = new Dictionary<string, long>(StringComparer.Ordinal);
        if (restored.ActiveOperations != null && restored.ActiveOperations.Count <= 16)
            foreach (var plan in restored.ActiveOperations)
            {
                if (plan == null || !Running(plan) || plan.OperationId <= 0 || plan.AssignedUnitLifeIds == null
                    || plan.AssignedUnitLifeIds.Count > 80 || plans.ContainsKey(plan.OperationId)) continue;
                plans.Add(plan.OperationId, plan);
                if (plan.Status == AIOperationStatus.Suspended) continue;
                foreach (string life in plan.AssignedUnitLifeIds)
                    if (ValidSquadLife(life) && !assignees.ContainsKey(life)) assignees.Add(life, plan.OperationId);
            }
        int ownTurn = Math.Max(restored.OwnTurns, restored.LastStartedOwnTurn);
        if (restored.Squads != null && restored.Squads.Count <= 16)
        {
            // Restore order must not decide which otherwise valid squad owns a duplicate life ID.
            var ordered = new List<AIOperationSquad>(restored.Squads);
            ordered.Sort((a, b) => a == null ? b == null ? 0 : 1 : b == null ? -1 : a.SquadId.CompareTo(b.SquadId));
            foreach (var squad in ordered)
            {
                if (squad == null || !plans.TryGetValue(squad.OperationId, out var owner)
                    || squad.SquadId < 1 || squad.SquadId == long.MaxValue || identities.Contains(squad.SquadId)
                    || squad.Members == null || squad.Members.Count > 5
                    || squad.FormedTurn < 0 || squad.FormedTurn > ownTurn
                    || squad.LastProgressTurn < squad.FormedTurn || squad.LastProgressTurn > ownTurn
                    || squad.LastAssemblyTurn < squad.FormedTurn || squad.LastAssemblyTurn > ownTurn
                    || squad.GoalX < 0 || squad.GoalZ < 0 || squad.GoalX >= 32768 || squad.GoalZ >= 32768
                    || !Enum.IsDefined(typeof(AIOperationSquadPhase), squad.Phase)) continue;
                squad.Members.RemoveAll(member => member == null || !ValidSquadLife(member.ActorLifeId)
                    || !Enum.IsDefined(typeof(AIOperationSquadRole), member.Role)
                    || !owner.AssignedUnitLifeIds.Contains(member.ActorLifeId)
                    || assignees.TryGetValue(member.ActorLifeId, out long assigned) && assigned != owner.OperationId
                    || !members.Add(member.ActorLifeId));
                if (squad.Members.Count == 0) continue;
                // Slots and approach waypoints are proposals tied to the old observed generation.
                // They must be regenerated, not replayed after a load.
                foreach (var member in squad.Members) { member.HasSlot = false; member.SlotX = member.SlotZ = 0; }
                squad.HasApproachWaypoint = false; squad.ApproachX = squad.ApproachZ = 0;
                if (owner.Status == AIOperationStatus.Suspended) squad.Phase = AIOperationSquadPhase.Suspended;
                if (squad.LastReason != null && squad.LastReason.Length > 256) squad.LastReason = squad.LastReason.Substring(0, 256);
                identities.Add(squad.SquadId); valid.Add(squad);
                restored.NextSquadId = Math.Max(restored.NextSquadId, squad.SquadId);
            }
        }
        restored.Squads = valid;
        restored.NextSquadId = Math.Max(0, Math.Min(long.MaxValue - 1, restored.NextSquadId));
    }
    static bool ValidSquadLife(string life) => !string.IsNullOrWhiteSpace(life) && life.Length <= 128;
}
