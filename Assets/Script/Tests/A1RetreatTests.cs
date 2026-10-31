#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

/// <summary>Native Editor entry point. Helper/adapter contracts; not a claim of RT01-RT10 match acceptance.</summary>
public static class A1RetreatTests
{
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Check(string label, bool ok)
    { if (!ok) throw new InvalidOperationException("[A1Retreat] FAIL " + label); Debug.Log("[A1Retreat] PASS " + label); }
    static Vector3 Cell(int x, int z = 1) => new Vector3(x, 1, z);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
    public static void Run()
    {
        BoundedRoutes(); FormalMovementAndQuotes(); FormalBarrierForecast();
        Debug.Log("[A1Retreat] COMPLETE (native contracts, no match outcome claimed)");
    }
    public static void PlayMode(GameSystems live) => Run();

    sealed class Graph : AIRetreatRouteContext
    {
        internal readonly Dictionary<Vector3, List<Vector3>> Edges = new Dictionary<Vector3, List<Vector3>>();
        internal readonly Dictionary<Vector3, int> Hits = new Dictionary<Vector3, int>();
        internal readonly HashSet<Vector3> Reservations = new HashSet<Vector3>();
        internal bool Recovery = true, Stopped;
        internal Graph()
        { Origin = Cell(1); HP = 100; CurrentAP = 3; NextAP = 3; CurrentRisk = 40; RefugeRiskLimit = 0; }
        internal Graph Edge(Vector3 from, params Vector3[] to) { Edges[from] = new List<Vector3>(to); return this; }
        internal override void CollectSteps(Vector3 from, List<Vector3> result)
        { if (Edges.TryGetValue(from, out var edges)) result.AddRange(edges); }
        internal override int MoveCost(Vector3 from, Vector3 to, int fatigue, bool moved, bool free) => 3 + fatigue;
        internal override bool ReservedFirstHop(Vector3 cell) => Reservations.Contains(cell);
        internal override float EntryLoss(Vector3 point, ref AIObservedDamageBudget protection) => 0;
        internal override float Exposure(Vector3 point, ref AIObservedDamageBudget protection)
        {
            int raw = Hits.TryGetValue(point, out int value) ? value : 0;
            int absorbed = Math.Min(raw, protection.Barrier); protection.Barrier -= absorbed; return raw - absorbed;
        }
        internal override bool CanRecoverAt(Vector3 point, float observedHpRisk) => Recovery;
        internal override bool Stop => Stopped;
    }
    static Graph Corridor()
    {
        var graph = new Graph().Edge(Cell(1), Cell(2)).Edge(Cell(2), Cell(3));
        graph.Hits[Cell(2)] = 30; return graph;
    }
    static AIRetreatRoutePlan Find(Graph graph, int depth = 4, int nodes = 96, int future = 1)
        => AIRetreatRoutePlanner.Find(graph, depth, nodes, future);
    static void BoundedRoutes()
    {
        var graph = Corridor(); var route = Find(graph);
        Check("RT01 known two-step path records current and next owner AP", route.Found && route.Steps.Count == 2
            && route.FirstHop == Cell(2) && route.Destination == Cell(3) && route.OwnTurns == 2
            && route.CurrentTurnAP == 3 && route.NextTurnAP == 3 && route.ObservedHpLoss == 30);
        graph.CurrentAP = 2; graph.NextAP = 100;
        Check("RT06 next-turn AP cannot pay an unaffordable first hop", !Find(graph).Found);
        graph.CurrentAP = 3; graph.NextAP = 2;
        Check("RT06 insufficient next-turn reset rejects the continuation", !Find(graph).Found);
        graph.CurrentAP = 7; graph.NextAP = 0;
        route = Find(graph);
        Check("RT06 same-turn second step charges increased fatigue", route.Found && route.OwnTurns == 1 && route.CurrentTurnAP == 7);
        graph.CurrentAP = 6;
        Check("RT06 same-turn fatigue cannot be reset without a funded boundary", !Find(graph).Found);
        graph = Corridor(); graph.Hits[Cell(2)] = 100;
        Check("RT03 known lethal waiting point is not a safe next-turn path", !Find(graph).Found);
        graph = Corridor(); graph.Hits[Cell(2)] = 10; graph.Hits[Cell(3)] = 5;
        graph.InitialProtection = new AIObservedDamageBudget { Barrier = 5 };
        Check("finite barrier is carried across owner turns instead of refilled per cell", !Find(graph).Found);
        graph = Corridor(); graph.Recovery = false;
        Check("RT02 safety without actual recovery capability is not a regroup endpoint", !Find(graph).Found);
        graph = Corridor(); graph.Edge(Cell(1), Cell(2), Cell(1, 2)); graph.Edge(Cell(1, 2), Cell(1, 3)); graph.Hits[Cell(1, 2)] = 10;
        var first = Find(graph); graph.Reservations.Add(first.FirstHop); var second = Find(graph);
        Check("RT05 separate wounded actors receive distinct first hops", first.Found && second.Found && first.FirstHop != second.FirstHop);
        graph = Corridor(); graph.Reservations.Add(Cell(2));
        Check("RT07 blocked first hop returns no order so legal fight alternatives remain", !Find(graph).Found);
        graph = Corridor();
        Check("configured depth rejects a route beyond its horizon", !Find(graph, 1).Found);
        route = Find(graph, 6, 2);
        Check("configured node cap is respected", !route.Found && route.ExpandedNodes <= 2);
        graph.Stopped = true; route = Find(graph);
        Check("global thinking budget stops route expansion", !route.Found && route.ExpandedNodes == 0);
        graph = Corridor(); graph.Edge(Cell(2), Cell(1));
        Check("RT10 route cycles do not manufacture progress", !Find(graph).Found);
    }

    static void FormalMovementAndQuotes()
    {
        var root = new GameObject("A1 isolated retreat movement contracts");
        var data = ScriptableObject.CreateInstance<UnitData>(); var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        var special = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        try
        {
            var actor = root.AddComponent<Status>(); actor.team = Team.Enemy; actor.type = Type.Unit; actor.kind = Kind.Priest;
            actor.HP = actor.MaxHP = 100; actor.transform.position = Cell(3, 3); actor.GrowthData = data; data.kind = actor.kind;
            data.actionProfile = profile; profile.actionType = ActorActionType.All; profile.movement.useCustom = true;
            profile.movement.directionIndependent = false; profile.movement.SetCell(3, 4, true); actor.direction = Direction.S;
            var map = root.AddComponent<MapCreate>(); map.preset = MapPresetSize.Custom; map.maxX = map.maxZ = 8;
            Set(map, "topY", new int[8, 8]); map.UseR1Terrain = false;
            var moves = root.AddComponent<MoveGenerator>(); moves.mapcreate = map;
            var ap = root.AddComponent<APSystem>(); ap.Init(null, map);
            var board = (AIBoardState)FormatterServices.GetUninitializedObject(typeof(AIBoardState));
            Set(board, "_moveGen", moves); Set(board, "_apSystem", ap);
            var known = new HashSet<Vector3Int>(); for (int x = 0; x < 8; x++) for (int z = 0; z < 8; z++) known.Add(new Vector3Int(x, 0, z));
            Set(board, "knownCells", known); Set(board, "ownedWallCells", new HashSet<Vector3Int>());
            var occupied = new HashSet<Vector3Int>(); var legal = new List<Vector3>();
            board.CollectKnownRetreatSteps(actor, actor.transform.position, occupied, legal);
            Check("RL02 RT01 authored direction-dependent mask is used at each origin", legal.Count == 1
                && legal[0] == Cell(3, 2) && map.IsLegalMove(actor, actor.transform.position, legal[0]));
            occupied.Add(new Vector3Int(3, 0, 2)); legal.Clear(); board.CollectKnownRetreatSteps(actor, actor.transform.position, occupied, legal);
            Check("RT05 owned occupation blocks a projected step", legal.Count == 0);
            occupied.Clear(); known.Remove(new Vector3Int(3, 0, 2)); legal.Clear();
            board.CollectKnownRetreatSteps(actor, actor.transform.position, occupied, legal);
            Check("unexplored terrain is not a certified retreat chain", legal.Count == 0);
            known.Add(new Vector3Int(3, 0, 2));
            foreach (int fatigue in new[] { 0, 2, 9 }) foreach (bool moved in new[] { false, true })
            {
                actor.Fatigue = fatigue; actor.HasMovedThisTurn = moved; actor.specialAbility = SpecialAbility.SwiftPosture;
                foreach (var destination in new[] { Cell(3, 2), new Vector3(3, 2, 2) })
                    Check("formal current move-preview parity fatigue=" + fatigue + " moved=" + moved + " height=" + destination.y,
                        ap.CalcCost(APSystem.ActionType.Move, actor, actor.transform.position, destination)
                        == ap.CalcMoveCostPreview(actor, actor.transform.position, destination, fatigue, moved, true));
                Check("formal current attack-preview parity fatigue=" + fatigue,
                    ap.CalcCost(APSystem.ActionType.Attack, actor) == ap.CalcAttackCostPreview(actor, fatigue, true));
            }
            actor.Fatigue = 4; actor.HasMovedThisTurn = false;
            int quoted = ap.CalcMoveCostPreview(actor, Cell(3, 3), Cell(3, 2), 0, true);
            Check("projected quote leaves live fatigue and first-move flags intact", actor.Fatigue == 4 && !actor.HasMovedThisTurn
                && quoted == APSystem.BaseMoveCost);
            data.specialSkill = special; special.enabledForPlay = true; special.ability = UnitSpecialSkill.AssassinVisionSeal;
            // Assassin definition detection must remain the canonical runtime decision.
            actor.kind = Kind.Assassin; data.kind = actor.kind;
            Check("Assassin quote retains formal early-return precedence", ap.CalcCost(APSystem.ActionType.Move, actor, Cell(3), Cell(4)) == SpecialSkillDefinition.AssassinMoveAP
                && ap.CalcCost(APSystem.ActionType.Move, actor, Cell(3), Cell(4))
                == ap.CalcMoveCostPreview(actor, Cell(3), Cell(4), actor.Fatigue, actor.HasMovedThisTurn, true));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(data);
            UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(special); }
    }
    static void FormalBarrierForecast()
    {
        var root = new GameObject("A1 isolated formal barrier forecast");
        var settings = ScriptableObject.CreateInstance<BarrierCombatSettings>();
        settings.enabledForPlay = true; settings.PopulateR1Values(); settings.recoveryAtOwnerTurnStart = false;
        using var scope = BarrierCombatSettings.UseForValidation(settings);
        using var effects = BoardActorAnimation.SuppressEffects();
        try
        {
            var actor = root.AddComponent<Status>(); actor.team = Team.Enemy; actor.kind = Kind.Knight; actor.type = Type.Unit;
            actor.HP = actor.MaxHP = 100; actor.ResetBarrier(); actor.Barrier = 0;
            var before = JsonUtility.ToJson(actor.BarrierRecovery);
            var two = AIUnitRecoveryForecast.Project(actor, 2, false); var three = AIUnitRecoveryForecast.Project(actor, 3, false);
            Check("RT08 full HP waits the first two formal owner boundaries", two.Barrier == 0 && two.FirstRecoveryBoundary == -1);
            Check("RT08 third full-HP boundary restores formal rounded twenty percent", three.FirstRecoveryBoundary == 3
                && three.Barrier == BarrierSystem.RegenerationAmount(actor.MaxBarrier));
            for (int i = 1; i <= 3; i++) BarrierSystem.ProcessActorEnd(actor, i);
            Check("RT08 forecast equals native recovery transition", actor.Barrier == three.Barrier);
            actor.HP = 99; var wounded = AIUnitRecoveryForecast.Project(actor, 12, false);
            Check("RT08 incomplete HP never invents a future healer action", wounded.FirstRecoveryBoundary == -1 && wounded.Barrier == actor.Barrier);
            actor.ApplyHeal(1); actor.Barrier = 0;
            var unpaid = AIUnitRecoveryForecast.Project(actor, 3, true);
            Check("AD04 RT08 unpaid forecast cites formal HP-loss rule and cannot promise barrier regeneration", unpaid.UpkeepRisk
                && unpaid.PossibleUnpaidHpLoss == R2EconomyRules.UnpaidDamage(actor.HP, actor.MaxHP)
                && unpaid.Barrier == 0 && unpaid.FirstRecoveryBoundary == -1);
            before = JsonUtility.ToJson(actor.BarrierRecovery);
            AIUnitRecoveryForecast.Project(actor, 12, false);
            Check("forecast never advances the live full-HP clock", before == JsonUtility.ToJson(actor.BarrierRecovery));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(settings); }
    }
}
#endif
