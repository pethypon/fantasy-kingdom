#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class AIKnownBaseTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string label, bool value)
    { if (!value) throw new InvalidOperationException("[KnownBases] FAIL " + label); passed++; Debug.Log("[KnownBases] PASS " + label); }
    static void Set(VisionGenerator vision, string name, object value) => typeof(VisionGenerator).GetField(name, Private).SetValue(vision, value);
    public static void PlayMode(GameSystems live)
    {
        passed = 0;
        foreach (var team in new[] { Team.Enemy, Team.Player }) Scenario(live, team);
        Debug.Log("[KnownBases] " + passed + " observed-base/opportunity/lifetime/save checks passed");
    }
    static void Scenario(GameSystems live, Team team)
    {
        using var f = new EconomyR2Tests.Fixture(live, team);
        var holder = new GameObject("Known bases isolated vision"); holder.SetActive(false);
        var vision = holder.AddComponent<VisionGenerator>(); var config = ScriptableObject.CreateInstance<AIOperationConfig>();
        config.EnablePersistentLearning = false; config.MaximumKnownBases = 8; config.MinimumBaseAssaultScore = 0;
        try
        {
            Set(vision, "PlayerUnit", f.Units.PlayerUnit); Set(vision, "EnemyUnit", f.Units.EnemyUnit); Set(vision, "territorysystem", live.TerritorySystem);
            var seen = new HashSet<Vector3Int>();
            foreach (string field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" }) Set(vision, field, new HashSet<Vector3Int>());
            Set(vision, team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox", seen);
            var cells = f.AdjacentCells();
            for (int i = 0; i < 6; i++) { var unit = f.Unit(); unit.ATK = 40; unit.DEF = 20; unit.transform.position = cells[0]; }
            var scout = f.Unit(); scout.kind = Kind.Scout; scout.GrowthData.kind = Kind.Scout; scout.transform.position = cells[1];
            var target = f.Opponent(cells[2], 0); target.kind = Kind.SubCrystal; target.type = Type.Building;
            target.facilityKind = FacilityKind.SubCrystal; target.transform.SetParent(f.Builder.GetBuildingParent(target.team));
            var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, f.AP, f.Units, f.Crystals, vision, f.Builder,
                null, f.State, null, 20, null, team); board.MapCreate = live.MapCreate; board.ReconThreatLevel = 15;
            board.Governor = new AIStrategicGovernor(); board.Governor.Evaluate(board);
            var manager = new AIOperationManager(config, new AIOperationLearningProfile { ProfileId = "KnownBase_Test" });
            manager.BeginBattle(team, "KnownBase_Test"); manager.BeginTurn(board, TurnStrategy.Balanced, 1);
            Check("T02/T21 unseen base is absent " + team, manager.KnownEnemyBases.Count == 0);
            for (int x = 0; x < live.MapCreate.maxX; x++) for (int z = 0; z < live.MapCreate.maxZ; z++) seen.Add(new Vector3Int(x, 0, z));
            board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            Check("T01 observed subcrystal registered " + team, manager.KnownEnemyBases.Count == 1 && manager.KnownEnemyBases[0].Type == AIEnemyBaseType.SubCrystal);
            var known = manager.KnownEnemyBases[0]; int discovered = known.FirstSeenTurn; float oldHp = known.LastKnownHpRatio;
            Check("T05 bounded assault candidates " + team, CountAssaults(manager) > 0 && CountAssaults(manager) <= 6);
            Check("T24 repeat refresh retains first discovery " + team, known.FirstSeenTurn == discovered && known.RediscoveryCount == 0);
            seen.Clear(); board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            target.HP = 1; target.ATK = 9999;
            board.Refresh(); manager.Refresh(board, TurnStrategy.Balanced);
            Check("T21 hidden updated HP is not read " + team, known.LastKnownHpRatio == oldHp && !known.CurrentlyVisible && !known.ConfirmedDestroyed);
            manager.EndTurn(board, 1); manager.BeginTurn(board, TurnStrategy.Balanced, 5);
            Check("T07 stale target remains known with lower confidence " + team, manager.KnownEnemyBases.Count == 1 && known.IntelConfidence < 1);
            bool scoutPattern = false; foreach (var candidate in manager.LastCandidates) if (candidate.Plan.IsBaseAssault && candidate.Plan.BaseAssaultPattern == AIBaseAssaultPattern.ScoutThenAssault) scoutPattern = true;
            Check("T06/T07 stale intel requests scout " + team, scoutPattern);
            var snapshot = manager.Snapshot(false); var loaded = new AIOperationManager(config); loaded.BeginBattle(team, "KnownBase_Test"); loaded.Restore(snapshot);
            Check("T22 battle save retains base LifeID without live refs " + team, loaded.KnownEnemyBases.Count == 1 && loaded.KnownEnemyBases[0].StableTargetId == target.ReflectionLifeId
                && !loaded.KnownEnemyBases[0].CurrentlyVisible);
            loaded.ConfirmKnownBaseDestroyed(target.ReflectionLifeId, false, false, board);
            Check("hidden death event does not leak " + team, !loaded.KnownEnemyBases[0].ConfirmedDestroyed);
            loaded.ConfirmKnownBaseDestroyed(target.ReflectionLifeId, true, false, board);
            Check("T12 observed other-faction destruction confirmed " + team, loaded.KnownEnemyBases[0].ConfirmedDestroyed);
            snapshot.KnownEnemyBases[0].X = int.MaxValue; loaded.Restore(snapshot); loaded.BeginTurn(board, TurnStrategy.Balanced, 6);
            Check("T23 invalid saved coordinate discarded " + team, loaded.KnownEnemyBases.Count == 0);
            seen.Add(GridHelper.ToGridXZ(cells[2])); target.HP = target.MaxHP;
            for (int turn = 6; turn <= 505; turn++)
            {
                target.RestoreReflectionLifeId("bounded_base_" + turn); board.Refresh(); manager.EndTurn(board, turn - 1); manager.BeginTurn(board, TurnStrategy.Balanced, turn);
            }
            Check("T20 500 turns registry stays capped " + team, manager.KnownEnemyBases.Count <= config.MaximumKnownBases);
            foreach (var entry in manager.KnownEnemyBases) Check("battle memory remains value-only " + team, entry.StableTargetId.StartsWith("bounded_base_") || entry.StableTargetId == known.StableTargetId);
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(config); }
    }
    static int CountAssaults(AIOperationManager manager)
    { int count = 0; foreach (var candidate in manager.LastCandidates) if (candidate.Plan.IsBaseAssault) count++; return count; }
}
#endif
