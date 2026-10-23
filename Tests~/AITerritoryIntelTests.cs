#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>Native Play Mode memory contracts and measured observation cost; not a complete autoplay match.</summary>
public static class AITerritoryIntelTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    [Serializable] public sealed class Performance
    {
        public int Width, Depth, MapCells, VisibleCells, ExploredCells, OpponentTerritoryCells, Reobservations;
        public bool AllocationCounterAvailable;
        public long ColdAllocatedBytes, ReobserveAllocatedBytes, CachedAllocatedBytes, ReobserveOwnershipQueries, CachedOwnershipQueries;
        public double ColdMs, ReobserveTotalMs, ReobserveMaxMs, Cached1000Ms;
        public string Scope = "EnemyTerritoryIntel.Observe only. Complete synthetic native map/ownership lists; no mesh rendering, main AI search, or full-match timing. Fixture construction and board.Refresh are outside measured intervals.";
    }
    [Serializable] public sealed class Results
    {
        public int Checks;
        public List<Performance> Observations = new List<Performance>();
    }
    static void Check(string label, bool condition)
    {
        if (!condition) throw new InvalidOperationException("[AITerritoryIntel] FAIL " + label);
        passed++; UnityEngine.Debug.Log("[AITerritoryIntel] PASS " + label);
    }
    public static void Run(GameSystems systems)
    {
        if (!Application.isPlaying || systems?.MapCreate == null)
            throw new InvalidOperationException("AITerritoryIntelTests requires a started native Play Mode scene.");
        passed = 0;
        var results = new Results();
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            FairObservationAndMemory(systems, team);
            CompressionAndPersistence(systems, team);
        }
        IndependentFactions(systems);
        results.Observations.Add(Measure(systems, 40));
        results.Observations.Add(Measure(systems, 300));
        RunCandidateFallback(systems);
        results.Checks = passed;
        string output = Environment.GetEnvironmentVariable("FK_TERRITORY_TEST_OUTPUT")
            ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../territory-intel-tests"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "territory-intel-tests.json"), JsonUtility.ToJson(results, true), new UTF8Encoding(false));
        UnityEngine.Debug.Log("[AITerritoryIntel] " + passed + " native memory/fairness/persistence checks passed; output=" + output);
    }

    /// <summary>Controlled topology regressions: bounded work must eventually examine another known approach/actor.</summary>
    public static void RunCandidateFallback(GameSystems systems)
    {
        bool beyondNearest = CandidateFallback(systems, false, out int nearestTurn);
        bool beyondFirstActors = CandidateFallback(systems, true, out int actorTurn);
        UnityEngine.Debug.Log("[AITerritoryCandidateFallback] nearer80=" + beyondNearest + " selectedTurn=" + nearestTurn
            + " fifthMobileActor=" + beyondFirstActors + " selectedTurn=" + actorTurn);
        Check("80 closer disconnected approaches cannot starve a farther known connected region", beyondNearest);
        Check("four disconnected lead actors cannot permanently exclude a fifth actor with a known approach", beyondFirstActors);
    }

    static bool CandidateFallback(GameSystems live, bool fifthActor, out int selectedTurn)
    {
        selectedTurn = -1;
        using var f = new Fixture(live, Team.Enemy, 120);
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        try
        {
            profile.actionType = ActorActionType.Combat;
            profile.movement.useCustom = true; profile.movement.directionIndependent = true;
            foreach (var offset in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                profile.movement.SetCell(3 + offset.x, 3 + offset.y, true);
            f.Actor.GrowthData.actionProfile = profile;
            // One connected, fully known corridor. The island below has no known bridge to it.
            for (int x = 2; x <= 110; x++) f.Explored.Add(new Vector3Int(x, 0, 2));
            var reachable = new Vector3Int(110, 0, 2);
            f.OpponentTerritory.Add((Vector3)reachable); f.Seen.Add(reachable);
            if (fifthActor)
            {
                f.Actor.transform.position = new Vector3(10, 1, 10);
                f.Explored.Add(f.Actor.GridPosition);
                for (int i = 0; i < 3; i++)
                {
                    var unit = f.Economy.Unit(); unit.GrowthData.actionProfile = profile;
                    unit.transform.position = new Vector3(11 + i, 1, 10); f.Explored.Add(unit.GridPosition);
                }
                var fifth = f.Economy.Unit(); fifth.GrowthData.actionProfile = profile;
                fifth.transform.position = new Vector3(2, 1, 2);
            }
            else
            {
                // 80 observed cells all rank before the distant corridor goal. Failed batches
                // used to recur after their eight-turn cooldown, starving every later goal.
                for (int x = 10; x < 20; x++) for (int z = 10; z < 18; z++)
                {
                    var cell = new Vector3Int(x, 0, z);
                    f.OpponentTerritory.Add((Vector3)cell); f.Explored.Add(cell); f.Seen.Add(cell);
                }
            }
            for (int turn = 60; turn < 84; turn++)
            {
                f.Refresh(turn); f.Board.TerritoryIntel.Update(f.Board, false, null);
                if (!f.Board.TerritoryIntel.HasWaypoint) continue;
                var target = f.Board.TerritoryIntel.Waypoint;
                bool valid = target.z == 2 && target.x >= 109 && target.x <= 110;
                if (valid) selectedTurn = turn;
                return valid;
            }
            return false;
        }
        finally { UnityEngine.Object.DestroyImmediate(profile); }
    }

    static void FairObservationAndMemory(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 40);
        var near = new Vector3Int(4, 0, 4); var other = new Vector3Int(5, 0, 4);
        f.OpponentTerritory.Add((Vector3)near); f.OpponentTerritory.Add((Vector3)other);
        f.Explored.UnionWith(new[] { near, other, new Vector3Int(3, 0, 4), new Vector3Int(3, 0, 3) });
        f.Seen.Add(new Vector3Int(3, 0, 3)); f.Refresh(10);
        var intel = f.Board.TerritoryIntel;
        intel.Observe(f.Board);
        Check(team + " explored enemy ownership is not fabricated without current sight", !intel.HasObservedTerritory && intel.ObservedCellCount == 0);
        Check(team + " ownership queries cover only supplied current vision", intel.LastObservationQueries == 1);
        string before = JsonUtility.ToJson(intel.CaptureState());
        // Change only hidden world facts. The known map and current observation are identical.
        f.HiddenFoe.transform.position = new Vector3(28000, 30, -29000); f.HiddenFoe.HP = 1;
        f.SetOpponentCrystal(new Vector3(-24000, 12, 25000));
        f.OpponentTerritory.Add(new Vector3(30, 0, 30)); f.Refresh(10); intel.Observe(f.Board);
        Check(team + " hidden actor/crystal/ownership changes do not alter intel", JsonUtility.ToJson(intel.CaptureState()) == before);
        Check(team + " territory knowledge never invents a principal unit or crystal contact",
            !f.Board.HasPrincipalOpponentContact && !f.Board.PlayerCrystalVisible && !f.Board.GetLastKnownPlayerCrystal().Valid);

        f.Seen.Add(near); f.Seen.Add(other); f.Refresh(11); intel.Observe(f.Board);
        Check(team + " current sight discovers precisely two observed territory cells", intel.HasObservedTerritory && intel.ObservedCellCount == 2
            && Decode(intel.CaptureState()).SetEquals(new[] { near, other }));
        Check(team + " discovery event is emitted once", intel.DiscoveryEvents == 1 && intel.FirstObservedTurn == 11 && intel.LastConfirmedTurn == 11);
        long queries = intel.TotalObservationQueries;
        for (int i = 0; i < 100; i++) intel.Observe(f.Board);
        Check(team + " repeated same-snapshot inspection does not query ownership again", intel.TotalObservationQueries == queries && intel.DiscoveryEvents == 1);

        f.Seen.Clear(); f.Seen.Add(new Vector3Int(3, 0, 3));
        f.OpponentTerritory.Clear(); f.OpponentTerritory.Add(new Vector3(31, 0, 31));
        f.Refresh(12); intel.Observe(f.Board);
        Check(team + " hidden territory changes cannot erase last-confirmed memory", intel.ObservedCellCount == 2 && intel.LastConfirmedTurn == 11);
        f.Refresh(31); intel.Observe(f.Board);
        Check(team + " twenty turns in fog do not apply the unit-memory expiry", intel.ObservedCellCount == 2 && intel.FirstObservedTurn == 11);
        f.Seen.Add(near); f.Refresh(32); intel.Observe(f.Board);
        Check(team + " reobserved removed ownership invalidates only the observed cell", intel.ObservedCellCount == 1
            && Decode(intel.CaptureState()).SetEquals(new[] { other }));
        // Same-turn board refresh is fresh legal evidence even if the vision footprint is identical.
        f.OpponentTerritory.Add((Vector3)near); f.Refresh(32); intel.Observe(f.Board);
        Check(team + " same-turn fresh observation sees a changed visible owner", intel.ObservedCellCount == 2 && intel.LastConfirmedTurn == 32);
        f.Seen.Add(other); f.OpponentTerritory.Clear(); f.Refresh(33); intel.Observe(f.Board);
        Check(team + " fully reobserved loss leaves discovery fact but no usable territory", intel.HasObservedTerritory
            && !intel.HasUsableTerritory && intel.ObservedCellCount == 0 && intel.DiscoveryEvents == 1);
        Check(team + " no hidden crystal position entered the actor memory", !f.Board.GetLastKnownPlayerCrystal().Valid);
        intel.RestoreState(null, team);
        Check(team + " new-match reset clears discovered cells and waypoint", !intel.HasObservedTerritory && intel.ObservedCellCount == 0
            && !intel.HasWaypoint && !intel.Suspended && intel.CaptureState() == null);
    }

    static void CompressionAndPersistence(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team, 40);
        var observed = new HashSet<Vector3Int>();
        for (int x = 16; x < 32; x++) for (int z = 16; z < 32; z++)
        {
            var cell = new Vector3Int(x, 0, z); f.OpponentTerritory.Add((Vector3)cell); f.Explored.Add(cell);
            if (((x + z) & 1) == 0) { f.Seen.Add(cell); observed.Add(cell); }
        }
        f.Refresh(15); var intel = f.Board.TerritoryIntel; intel.Observe(f.Board);
        var state = intel.CaptureState();
        Check(team + " 128 checkerboard observations compress into one exact region", intel.ObservedCellCount == 128 && intel.RegionCount == 1
            && state.Regions.Count == 1 && state.Regions[0].Cells.Length == 8);
        Check(team + " compression never fills unseen holes or an inferred center", Decode(state).SetEquals(observed)
            && !Decode(state).Contains(new Vector3Int(17, 0, 16)));
        Check(team + " valid observation DTO passes map-and-owner validation", EnemyTerritoryIntel.IsValidState(state, 40, 40, team));
        var roundTrip = JsonUtility.FromJson<EnemyTerritoryIntelState>(JsonUtility.ToJson(state));
        var restored = new EnemyTerritoryIntel(); restored.RestoreState(roundTrip, team);
        Check(team + " JSON DTO restore preserves exact observed cells and timestamps", Decode(restored.CaptureState()).SetEquals(observed)
            && restored.FirstObservedTurn == 15 && restored.LastConfirmedTurn == 15 && !restored.HasWaypoint);
        state.Regions[0].Cells[0] = 0;
        Check(team + " capture state is detached from live memory", intel.ObservedCellCount == 128 && Decode(intel.CaptureState()).SetEquals(observed));
        roundTrip.Regions[0].Cells[0] = 0;
        Check(team + " restored memory is detached from the input DTO", restored.ObservedCellCount == 128 && Decode(restored.CaptureState()).SetEquals(observed));

        var original = intel.CaptureState();
        EnemyTerritoryIntelState Copy() => JsonUtility.FromJson<EnemyTerritoryIntelState>(JsonUtility.ToJson(original));
        void Reject(string label, Action<EnemyTerritoryIntelState> mutate)
        {
            var invalid = Copy(); mutate(invalid);
            Check(team + " rejects " + label, !EnemyTerritoryIntel.IsValidState(invalid, 40, 40, team));
            var safe = new EnemyTerritoryIntel(); safe.RestoreState(original, team); safe.RestoreState(invalid, team);
            Check(team + " invalid " + label + " restores safely without hostile knowledge", !safe.HasObservedTerritory && safe.ObservedCellCount == 0 && !safe.HasWaypoint);
        }
        Reject("unknown DTO version", s => s.Version = 999);
        Reject("wrong faction owner", s => s.ActorTeam = team == Team.Player ? Team.Enemy : Team.Player);
        Reject("wrong opposing faction", s => s.OpponentTeam = team);
        Reject("malformed map dimensions", s => s.Width = -1);
        Reject("negative discovery timestamp", s => s.FirstObservedTurn = -3);
        Reject("duplicate regions", s => s.Regions.Add(JsonUtility.FromJson<EnemyTerritoryIntelRegion>(JsonUtility.ToJson(s.Regions[0]))));
        Reject("truncated region bitset", s => s.Regions[0].Cells = new int[7]);
        Reject("unaligned region origin", s => s.Regions[0].OriginX++);
        Reject("inferred unobserved base waypoint", s => { s.HasWaypoint = true; s.Waypoint = new Vector3Int(2, 1, 2); });
        Reject("out-of-map territory bit", s => { s.Regions[0].OriginX = 32; s.Regions[0].Cells[0] |= 1 << 15; });
        Reject("oversized visit history", s =>
        {
            for (int i = 0; i <= EnemyTerritoryIntel.MaximumVisits; i++)
                s.Visits.Add(new EnemyTerritoryIntelVisit { Cell = new Vector3Int(i % 40, 0, i / 40), UntilTurn = 100 });
        });
        Reject("duplicate visit history", s =>
        {
            s.Visits.Add(new EnemyTerritoryIntelVisit { Cell = new Vector3Int(4, 0, 4), UntilTurn = 100 });
            s.Visits.Add(new EnemyTerritoryIntelVisit { Cell = new Vector3Int(4, 0, 4), UntilTurn = 101 });
        });
        Reject("paused state without a pause timestamp", s => { s.Suspended = true; s.PausedTurn = -1; });
        Reject("unbounded saved reason", s => s.Reason = new string('x', 129));
        Check(team + " mismatched current map rejects a valid foreign-sized save", !EnemyTerritoryIntel.IsValidState(original, 300, 300, team));
        Check(team + " missing territory DTO remains compatible with old saves", EnemyTerritoryIntel.IsValidState(null, 40, 40, team));
        restored.Reset(team);
        Check(team + " explicit reset clears exact memory and discovery epoch", !restored.HasObservedTerritory && restored.ObservedCellCount == 0
            && restored.FirstObservedTurn == -1 && restored.LastConfirmedTurn == -1 && restored.DiscoveryEvents == 0);
    }

    static void IndependentFactions(GameSystems live)
    {
        using var p = new Fixture(live, Team.Player, 40);
        using var e = new Fixture(live, Team.Enemy, 40);
        var pCell = new Vector3Int(4, 0, 4); var eCell = new Vector3Int(20, 0, 20);
        p.OpponentTerritory.Add((Vector3)pCell); p.Seen.Add(pCell); p.Explored.Add(pCell); p.Refresh(9);
        e.OpponentTerritory.Add((Vector3)eCell); e.Seen.Add(eCell); e.Explored.Add(eCell); e.Refresh(9);
        p.Board.TerritoryIntel.Observe(p.Board); e.Board.TerritoryIntel.Observe(e.Board);
        Check("player and enemy independently remember different legally observed cells", Decode(p.Board.TerritoryIntel.CaptureState()).SetEquals(new[] { pCell })
            && Decode(e.Board.TerritoryIntel.CaptureState()).SetEquals(new[] { eCell }));
        var wrongOwner = new EnemyTerritoryIntel(); wrongOwner.RestoreState(p.Board.TerritoryIntel.CaptureState(), Team.Enemy);
        Check("cross-faction restore cannot reveal the other faction's intel", !wrongOwner.HasObservedTerritory && wrongOwner.ObservedCellCount == 0);
    }

    static Performance Measure(GameSystems live, int size)
    {
        using var f = new Fixture(live, Team.Enemy, size);
        // A deliberately large ownership list makes the real IsInTerritory list lookup cost visible.
        // Current sight stays 64 cells near the end of that list, not an unrealistically cheap first match.
        f.OpponentTerritory.AddRange(f.Map.SetPos);
        for (int x = size - 10; x < size - 2; x++) for (int z = size - 10; z < size - 2; z++)
        { var cell = new Vector3Int(x, 0, z); f.Seen.Add(cell); f.Explored.Add(cell); }
        f.Refresh(40); var intel = f.Board.TerritoryIntel;
        var result = new Performance { Width = size, Depth = size, MapCells = f.Map.SetPos.Count, VisibleCells = f.Seen.Count,
            ExploredCells = f.Explored.Count, OpponentTerritoryCells = f.OpponentTerritory.Count, Reobservations = 8 };
        long probeBefore = GC.GetAllocatedBytesForCurrentThread();
        var allocationProbe = new byte[4096];
        GC.KeepAlive(allocationProbe);
        result.AllocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() - probeBefore >= allocationProbe.Length;
        var clock = new Stopwatch();
        long allocated = GC.GetAllocatedBytesForCurrentThread(); clock.Start(); intel.Observe(f.Board); clock.Stop();
        result.ColdMs = clock.Elapsed.TotalMilliseconds; result.ColdAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(size + " actual map has all cells while memory imports only visible ownership", f.Map.SetPos.Count == size * size
            && intel.ObservedCellCount == 64 && intel.LastObservationQueries == 64 && Decode(intel.CaptureState()).SetEquals(f.Seen));
        long queryStart = intel.TotalObservationQueries;
        for (int i = 0; i < result.Reobservations; i++)
        {
            f.Refresh(41 + i);
            allocated = GC.GetAllocatedBytesForCurrentThread(); clock.Restart(); intel.Observe(f.Board); clock.Stop();
            double ms = clock.Elapsed.TotalMilliseconds;
            result.ReobserveTotalMs += ms; result.ReobserveMaxMs = Math.Max(result.ReobserveMaxMs, ms);
            result.ReobserveAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
        result.ReobserveOwnershipQueries = intel.TotalObservationQueries - queryStart;
        Check(size + " fresh observations query visible cells, not every map cell", result.ReobserveOwnershipQueries == 64 * result.Reobservations);
        queryStart = intel.TotalObservationQueries;
        allocated = GC.GetAllocatedBytesForCurrentThread(); clock.Restart();
        for (int i = 0; i < 1000; i++) intel.Observe(f.Board);
        clock.Stop(); result.Cached1000Ms = clock.Elapsed.TotalMilliseconds;
        result.CachedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        result.CachedOwnershipQueries = intel.TotalObservationQueries - queryStart;
        if (!result.AllocationCounterAvailable)
            result.ColdAllocatedBytes = result.ReobserveAllocatedBytes = result.CachedAllocatedBytes = -1;
        Check(size + " 1000 same-snapshot previews issue zero new ownership queries", result.CachedOwnershipQueries == 0 && intel.DiscoveryEvents == 1);
        UnityEngine.Debug.Log("[AITerritoryIntelPerf] " + JsonUtility.ToJson(result));
        return result;
    }

    static HashSet<Vector3Int> Decode(EnemyTerritoryIntelState state)
    {
        var cells = new HashSet<Vector3Int>();
        if (state?.Regions == null) return cells;
        foreach (var region in state.Regions)
            for (int word = 0; word < region.Cells.Length; word++)
                for (int bit = 0; bit < 32; bit++)
                    if (((uint)region.Cells[word] & (1u << bit)) != 0)
                    {
                        int index = word * 32 + bit;
                        cells.Add(new Vector3Int(region.OriginX + index % 16, 0, region.OriginZ + index / 16));
                    }
        return cells;
    }

    sealed class Fixture : IDisposable
    {
        public readonly EconomyR2Tests.Fixture Economy;
        public readonly MapCreate Map;
        public readonly TerritorySystem Territory;
        public readonly Status Actor, HiddenFoe;
        public readonly AIBoardState Board;
        public readonly HashSet<Vector3Int> Seen = new HashSet<Vector3Int>(), Explored = new HashSet<Vector3Int>();
        public List<Vector3> OpponentTerritory => Territory.GetTerritory(Board.OpponentTeam);
        readonly GameObject root;
        public Fixture(GameSystems live, Team team, int size)
        {
            Economy = new EconomyR2Tests.Fixture(live, team);
            root = new GameObject("Territory intel native " + size + " fixture"); root.SetActive(false);
            try
            {
                Map = root.AddComponent<MapCreate>(); Map.maxX = Map.maxZ = size; Map.UseR1Terrain = true;
                Map.SetPos = new List<Vector3>(size * size);
                for (int x = 0; x < size; x++) for (int z = 0; z < size; z++) Map.SetPos.Add(new Vector3(x, 1, z));
                typeof(MapCreate).GetField("topY", Hidden).SetValue(Map, new int[size, size]);
                typeof(MapCreate).GetField("rivers", Hidden).SetValue(Map, new bool[size, size]);
                Territory = root.AddComponent<TerritorySystem>(); Territory.PTSetPos = new List<Vector3>(); Territory.ETSetPos = new List<Vector3>();
                var vision = root.AddComponent<VisionGenerator>();
                foreach (var field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                    typeof(VisionGenerator).GetField(field, Hidden).SetValue(vision, new HashSet<Vector3Int>());
                typeof(VisionGenerator).GetField(team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox", Hidden).SetValue(vision, Seen);
                typeof(VisionGenerator).GetField(team == Team.Player ? "_playerExplored" : "_enemyExplored", Hidden).SetValue(vision, Explored);
                typeof(VisionGenerator).GetField("PlayerUnit", Hidden).SetValue(vision, Economy.Units.PlayerUnit);
                typeof(VisionGenerator).GetField("EnemyUnit", Hidden).SetValue(vision, Economy.Units.EnemyUnit);
                typeof(VisionGenerator).GetField("territorysystem", Hidden).SetValue(vision, Territory);
                var moves = root.AddComponent<MoveGenerator>(); moves.mapcreate = Map;
                foreach (var pair in new[] { ("crystalsystem", (object)Economy.Crystals), ("unitsetting", (object)Economy.Units),
                    ("PlayerUnit", (object)Economy.Units.PlayerUnit), ("EnemyUnit", (object)Economy.Units.EnemyUnit) })
                    typeof(MoveGenerator).GetField(pair.Item1, Hidden).SetValue(moves, pair.Item2);
                Economy.Crystal.transform.position = new Vector3(0, 1, 0); Economy.SetOwnCrystalPosition(Economy.Crystal.transform.position);
                SetOpponentCrystal(new Vector3(size - 1, 1, size - 1));
                Actor = Economy.Unit(); Actor.transform.position = new Vector3(2, 1, 2);
                HiddenFoe = Economy.Opponent(new Vector3(-18000, 1, -19000), 1000);
                Board = new AIBoardState(moves, live.AttackGenerator, Economy.AP, Economy.Units, Economy.Crystals, vision,
                    null, null, Economy.State, null, 10, null, team) { MapCreate = Map, TerritorySystem = Territory, ReconThreatLevel = 15 };
            }
            catch { Dispose(); throw; }
        }
        public void SetOpponentCrystal(Vector3 position)
        { if (Economy.Team == Team.Player) Economy.Crystals.ECP = position; else Economy.Crystals.PCP = position; }
        public void Refresh(int turn)
        { typeof(AIBoardState).GetProperty("TurnCount").SetValue(Board, turn); Board.Refresh(); }
        public void Dispose()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            Economy?.Dispose();
        }
    }
}
#endif
