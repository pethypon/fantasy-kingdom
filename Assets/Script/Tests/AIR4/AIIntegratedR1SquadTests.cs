#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>R1 save/ownership lifecycle contracts. Source only; NOT RUN and not a simulated match.</summary>
public static class AIIntegratedR1SquadTests
{
    static void Check(string name, bool condition)
    { if (!condition) throw new InvalidOperationException("[R1Squad] FAIL " + name); Debug.Log("[R1Squad] PASS " + name); }
    public static void Run()
    {
        var saved = State(); var original = saved.Squads[0].Members[0];
        original.Recovering = true; original.HasSlot = true; original.SlotX = 19; original.SlotZ = 21;
        saved.Squads[0].HasApproachWaypoint = true; saved.Squads[0].ApproachX = 20; saved.Squads[0].ApproachZ = 20;
        AIOperationManager.SanitizeSquadState(saved);
        Check("squad identity and recovering membership survive value restore", saved.Squads.Count == 1 && saved.Squads[0].SquadId == 1
            && saved.Squads[0].Members.Count == 3 && original.Recovering);
        Check("old formation slots and waypoints cannot be replayed after load", !original.HasSlot && original.SlotX == 0 && original.SlotZ == 0
            && !saved.Squads[0].HasApproachWaypoint && saved.Squads[0].ApproachX == 0 && saved.Squads[0].ApproachZ == 0);
        Check("current member reference is required", AIOperationManager.CurrentSquadMembership(saved.Squads[0], original, "a")
            && !AIOperationManager.CurrentSquadMembership(saved.Squads[0], Member("a"), "a"));
        saved.Squads[0].Members.Remove(original);
        Check("removed member cannot use an old index entry", !AIOperationManager.CurrentSquadMembership(saved.Squads[0], original, "a"));

        saved = State(); saved.Squads[0].Members.Add(Member("a"));
        Check("duplicate live identity is not a current membership", !AIOperationManager.CurrentSquadMembership(saved.Squads[0], saved.Squads[0].Members[0], "a"));
        AIOperationManager.SanitizeSquadState(saved);
        Check("one actor is counted once within a squad", saved.Squads.Count == 1 && saved.Squads[0].Members.Count == 3);
        saved = State(); var second = Squad(2, 1, "a"); saved.Squads.Insert(0, second);
        AIOperationManager.SanitizeSquadState(saved);
        Check("cross-squad duplicates use stable squad identity order", saved.Squads.Count == 1 && saved.Squads[0].SquadId == 1);

        saved = State(); saved.ActiveOperations.Add(Plan(2, "a"));
        saved.Squads.Clear(); saved.Squads.Add(Squad(1, 2, "a")); saved.Squads.Add(Squad(2, 1, "a"));
        AIOperationManager.SanitizeSquadState(saved);
        Check("operation assignment precedence prevents cross-operation theft", saved.Squads.Count == 1
            && saved.Squads[0].OperationId == 1 && saved.Squads[0].SquadId == 2);

        saved = State(); saved.Squads[0].Members.Add(Member("foreign-or-unassigned"));
        AIOperationManager.SanitizeSquadState(saved);
        Check("only owning operation assignees may fill squad or recruitment counts", saved.Squads[0].Members.Count == 3);
        saved = State(); saved.Squads[0].OperationId = 99;
        AIOperationManager.SanitizeSquadState(saved);
        Check("finished or absent operation releases its squads", saved.Squads.Count == 0);
        saved = State(); saved.ActiveOperations[0].Status = AIOperationStatus.Success;
        AIOperationManager.SanitizeSquadState(saved);
        Check("completed operation cannot retain a running squad", saved.Squads.Count == 0);
        saved = State(); saved.ActiveOperations[0].Status = AIOperationStatus.Suspended;
        AIOperationManager.SanitizeSquadState(saved);
        Check("suspension retains stable members without exposing actionable squad orders", saved.Squads.Count == 1
            && saved.Squads[0].Members.Count == 3 && saved.Squads[0].Phase == AIOperationSquadPhase.Suspended
            && !AIOperationManager.CurrentSquadMembership(saved.Squads[0], saved.Squads[0].Members[0], "a"));

        foreach (int assembly in new[] { -1, int.MaxValue })
        {
            saved = State(); saved.Squads[0].LastAssemblyTurn = assembly;
            AIOperationManager.SanitizeSquadState(saved);
            Check("invalid assembly timestamp cannot create an indefinite regroup wait " + assembly, saved.Squads.Count == 0);
        }
        saved = State(); saved.Squads[0].FormedTurn = 5;
        AIOperationManager.SanitizeSquadState(saved);
        Check("future formation timestamp rejected", saved.Squads.Count == 0);
        saved = State(); saved.Squads[0].LastProgressTurn = 5;
        AIOperationManager.SanitizeSquadState(saved);
        Check("future progress timestamp rejected", saved.Squads.Count == 0);
        saved = State(); var invalidFirst = Squad(1, 1, "c"); invalidFirst.LastAssemblyTurn = int.MaxValue;
        saved.Squads.Insert(0, invalidFirst);
        AIOperationManager.SanitizeSquadState(saved);
        Check("rejected record does not consume the valid squad identity", saved.Squads.Count == 1 && saved.Squads[0].Members.Count == 3);

        saved = State(); saved.Squads[0].Members.Clear();
        AIOperationManager.SanitizeSquadState(saved);
        Check("empty squads are disbanded", saved.Squads.Count == 0);
        foreach (int count in new[] { 1, 2, 5 })
        {
            saved = State(); saved.ActiveOperations[0].AssignedUnitLifeIds = new List<string>(); saved.Squads[0].Members.Clear();
            for (int i = 0; i < count; i++) { string life = "unit" + i; saved.ActiveOperations[0].AssignedUnitLifeIds.Add(life); saved.Squads[0].Members.Add(Member(life)); }
            AIOperationManager.SanitizeSquadState(saved);
            Check("valid temporary or standard squad remains usable size " + count, saved.Squads.Count == 1 && saved.Squads[0].Members.Count == count);
        }
        saved = State(); saved.Squads[0].Members[0].Role = (AIOperationSquadRole)999;
        AIOperationManager.SanitizeSquadState(saved);
        Check("invalid role does not count toward replenishment", saved.Squads.Count == 1 && saved.Squads[0].Members.Count == 2);
        saved = State(); saved.Squads[0].Phase = (AIOperationSquadPhase)999;
        AIOperationManager.SanitizeSquadState(saved);
        Check("invalid phase rejected", saved.Squads.Count == 0);
        saved = State(); saved.Squads[0].LastReason = new string('x', 1000);
        AIOperationManager.SanitizeSquadState(saved);
        Check("saved diagnostic text remains bounded", saved.Squads[0].LastReason.Length == 256);
        saved = State(); for (int i = 2; i <= 17; i++) saved.Squads.Add(Squad(i, 1, "a"));
        AIOperationManager.SanitizeSquadState(saved);
        Check("over-limit squad payload cannot grow runtime collections", saved.Squads.Count == 0);
        LiveActorContracts();
    }

    static void LiveActorContracts()
    {
        var go = new GameObject("R1-squad-contract-actor");
        try
        {
            var actor = go.AddComponent<Status>(); actor.team = Team.Enemy; actor.kind = Kind.Knight;
            actor.type = Type.Unit; actor.MaxHP = actor.HP = 20; actor.IsPrimaryCommander = false;
            Check("own living unit is a valid squad actor", AIOperationManager.LiveSquadActor(actor, Team.Enemy));
            actor.team = Team.Player;
            Check("changed or foreign ownership invalidates cached squad membership", !AIOperationManager.LiveSquadActor(actor, Team.Enemy));
            actor.team = Team.Enemy; actor.type = Type.Building;
            Check("building cannot fill a unit role or recruitment quota", !AIOperationManager.LiveSquadActor(actor, Team.Enemy));
            actor.type = Type.Unit; actor.IsPrimaryCommander = true;
            Check("loss-condition commander is not a disposable squad member", !AIOperationManager.LiveSquadActor(actor, Team.Enemy));
            actor.IsPrimaryCommander = false; go.SetActive(false);
            Check("despawned actor invalidates cached squad membership", !AIOperationManager.LiveSquadActor(actor, Team.Enemy));
            go.SetActive(true); actor.HP = 0;
            Check("dead actor cannot remain counted toward required roles", !AIOperationManager.LiveSquadActor(actor, Team.Enemy));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    static AIOperationBattleState State()
    {
        var state = new AIOperationBattleState { Faction = Team.Enemy, BattleId = "r1-squad-fixture", OwnTurns = 3, LastStartedOwnTurn = 4 };
        state.ActiveOperations.Add(Plan(1, "a", "b", "c")); state.Squads.Add(Squad(1, 1, "a", "b", "c")); return state;
    }
    static AIOperationPlan Plan(long id, params string[] lives) => new AIOperationPlan { OperationId = id, Status = AIOperationStatus.Active,
        PrimaryGoal = AIOperationGoal.DestroyEnemyCrystal, AssignedUnitLifeIds = new List<string>(lives) };
    static AIOperationSquad Squad(long id, long operation, params string[] lives)
    {
        var squad = new AIOperationSquad { SquadId = id, OperationId = operation, FormedTurn = 1, LastProgressTurn = 2,
            LastAssemblyTurn = 1, Phase = AIOperationSquadPhase.Advancing, GoalX = 10, GoalZ = 10 };
        foreach (string life in lives) squad.Members.Add(Member(life)); return squad;
    }
    static AIOperationSquadMember Member(string life) => new AIOperationSquadMember { ActorLifeId = life, Role = AIOperationSquadRole.Frontline };
}
#endif
