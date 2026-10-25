#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Real observed-board coverage of substitute scouting, handover and danger cache invalidation.</summary>
public static class ExplorationFallbackTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string name, bool ok)
    {
        if (!ok) throw new InvalidOperationException("[ExplorationFallback] FAIL " + name);
        passed++; Debug.Log("[ExplorationFallback] PASS " + name);
    }
    public static void PlayMode(GameSystems live)
    {
        passed = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy }) Faction(live, team);
        Debug.Log("[ExplorationFallback] " + passed + " observed-board checks passed");
    }
    static int DangerRevision(AIExplorationPlanner planner)
        => ((ExplorationObservation)typeof(AIExplorationPlanner).GetField("boardObservation", Private).GetValue(planner)).DangerVersion;
    static Status At(EconomyR2Tests.Fixture f, Kind kind, Vector3 cell, string life)
    {
        var actor = f.Unit(); actor.kind = kind; actor.transform.position = cell;
        actor.RestoreReflectionLifeId(life); return actor;
    }
    static void Faction(GameSystems live, Team team)
    {
        using (var f = new EconomyR2Tests.Fixture(live, team))
        {
            var visionObject = new GameObject("Isolated fallback exploration vision"); visionObject.SetActive(false);
            var vision = visionObject.AddComponent<VisionGenerator>();
            var stationary = ScriptableObject.CreateInstance<BoardActionProfile>(); stationary.actionType = ActorActionType.Stationary;
            var support = ScriptableObject.CreateInstance<BoardActionProfile>(); support.actionType = ActorActionType.Support;
            var heights = (int[,])typeof(MapCreate).GetField("topY", Private).GetValue(live.MapCreate);
            Vector3 hiddenCell = Vector3.zero; bool changedHeight = false; int originalHeight = 0;
            try
            {
                var cells = f.AdjacentCells(); var seen = new HashSet<Vector3Int>();
                foreach (var cell in cells) seen.Add(GridHelper.ToGridXZ(cell));
                foreach (string field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                    typeof(VisionGenerator).GetField(field, Private).SetValue(vision,
                        new HashSet<Vector3Int>(field.StartsWith(team == Team.Player ? "_player" : "_enemy") ? seen : new HashSet<Vector3Int>()));
                f.SetOwnCrystalPosition(cells[0]); f.Crystal.transform.position = cells[0];
                var first = At(f, Kind.Knight, cells[0], "fallback-first");
                var second = At(f, Kind.Archer, cells[1], "fallback-second");
                var commander = At(f, Kind.Boss, cells[0], "a-commander"); commander.IsPrimaryCommander = true;
                var priest = At(f, Kind.Priest, cells[0], "a-priest");
                var immobile = At(f, Kind.Guardian, cells[0], "a-stationary"); immobile.GrowthData.actionProfile = stationary;
                var healer = At(f, Kind.Magic, cells[0], "a-support"); healer.GrowthData.actionProfile = support;
                var wounded = At(f, Kind.Knight, cells[0], "a-wounded"); wounded.HP = 40;
                foreach (var cell in live.MapCreate.SetPos)
                    if (!seen.Contains(GridHelper.ToGridXZ(cell)) && GridHelper.ChebyshevDistance(cell, cells[0]) > 10)
                    { hiddenCell = cell; changedHeight = true; break; }
                Check(team + " fixture includes unobserved terrain", changedHeight);
                originalHeight = heights[(int)hiddenCell.x, (int)hiddenCell.z];
                var hidden = f.Opponent(hiddenCell, 9999);
                var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, f.AP, f.Units, f.Crystals,
                    vision, f.Builder, null, f.State, null, 20, null, team);
                board.MapCreate = live.MapCreate; board.ReconThreatLevel = 15; board.Governor = new AIStrategicGovernor();
                var planner = board.Exploration; planner.Update(board, false);
                Check(team + " only one safe substitute receives a persistent objective", planner.IsAssignedUnit(first) && !planner.IsAssignedUnit(second)
                    && planner.Objectives.Count == 1);
                Check(team + " commander, support, priest, stationary and wounded actors stay unassigned", !planner.IsAssignedUnit(commander)
                    && !planner.IsAssignedUnit(priest) && !planner.IsAssignedUnit(immobile) && !planner.IsAssignedUnit(healer) && !planner.IsAssignedUnit(wounded));
                Check(team + " nonassigned actors receive no exploration reveal bonus", planner.GetMoveBonus(second, hiddenCell, board) == 0
                    && planner.GetMoveBonus(commander, hiddenCell, board) == 0 && planner.GetMoveBonus(priest, hiddenCell, board) == 0);
                var target = planner.GetObjective(first.GetInstanceID()).TargetCell;
                float bonus = planner.GetMoveBonus(first, cells[1], board);
                hidden.ATK = 1; hidden.transform.position = hiddenCell + Vector3.forward;
                heights[(int)hiddenCell.x, (int)hiddenCell.z] = originalHeight == 2 ? 0 : 2;
                board.Refresh(); planner.Update(board, false);
                Check(team + " hidden foe and terrain changes cannot alter substitute target or utility", !board.AlivePlayerUnits.Contains(hidden)
                    && planner.GetObjective(first.GetInstanceID()).TargetCell == target && Mathf.Abs(planner.GetMoveBonus(first, cells[1], board) - bonus) < .001f);
                Check(team + " substitute imports only faction-observed cells", planner.Memory.Count == seen.Count && !planner.Memory.ContainsKey(GridHelper.ToGridXZ(hiddenCell)));
                heights[(int)hiddenCell.x, (int)hiddenCell.z] = originalHeight;
                int revision = DangerRevision(planner), builds = planner.KnownRouteBuildCount;
                if (DamageCalculator.UsesLegacyDefence) first.DEF += 30;
                else first.Barrier = Mathf.Max(0, first.Barrier - 1);
                board.Refresh(); planner.Update(board, false); planner.GetMoveBonus(first, cells[1], board);
                Check(team + " substitute active damage protection changes invalidate cached route danger", revision != DangerRevision(planner) && planner.KnownRouteBuildCount > builds);
                revision = DangerRevision(planner); first.ShieldTurns = 1; board.Refresh(); planner.Update(board, false);
                Check(team + " substitute shield changes update route danger", revision != DangerRevision(planner));
                var scout = At(f, Kind.Scout, cells[2], "returning-scout"); board.Refresh(); planner.Update(board, false);
                Check(team + " returning scout takes over and releases both substitutes", planner.IsAssignedUnit(scout) && !planner.IsAssignedUnit(first)
                    && !planner.IsAssignedUnit(second) && planner.GetMoveBonus(first, hiddenCell, board) == 0 && planner.Objectives.Count == 1);
                revision = DangerRevision(planner);
                if (DamageCalculator.UsesLegacyDefence) scout.DEF += 30;
                else scout.Barrier = Mathf.Max(0, scout.Barrier - 1);
                board.Refresh(); planner.Update(board, false);
                Check(team + " scout active damage protection changes still update route danger", revision != DangerRevision(planner));
                scout.gameObject.SetActive(false); board.Refresh(); planner.Update(board, false);
                Check(team + " lost scout resumes exactly one substitute objective", planner.IsAssignedUnit(first) && !planner.IsAssignedUnit(second) && planner.Objectives.Count == 1);
                var state = planner.CaptureState(); planner.RestoreState(state);
                Check(team + " save restore discards runtime substitute identity", (int)typeof(AIExplorationPlanner).GetField("fallbackExplorerId", Private).GetValue(planner) == 0);
                planner.Update(board, false);
                Check(team + " restored exploration rebinds a legal substitute", planner.IsAssignedUnit(first) && planner.Objectives.Count == 1);
                int ap = f.State.GetAP(team); var position = first.transform.position;
                for (int i = 0; i < 30; i++) planner.GetMoveBonus(first, cells[1], board);
                Check(team + " exploration previews consume no AP or movement", ap == f.State.GetAP(team) && first.transform.position == position);
                first.HP = 1; second.GrowthData.actionProfile = stationary; board.Refresh(); planner.Update(board, false);
                Check(team + " no substitute is forced when every candidate is unsafe or immobile", planner.Objectives.Count == 0 && !planner.IsAssignedUnit(first));
            }
            finally
            {
                if (changedHeight) heights[(int)hiddenCell.x, (int)hiddenCell.z] = originalHeight;
                UnityEngine.Object.DestroyImmediate(visionObject); UnityEngine.Object.DestroyImmediate(stationary); UnityEngine.Object.DestroyImmediate(support);
            }
        }
    }
}
#endif
