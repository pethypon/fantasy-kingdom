#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Actual AI/game DTO JSON round trips and production restoration, on isolated observed boards.
/// These do not certify a whole match resumed mid-search or a product-build performance budget.</summary>
public static class AIR2ObservedMemoryTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly FieldInfo MemoryField = typeof(AICommander).GetField("_sharedMemory", Hidden);
    static readonly FieldInfo BoardField = typeof(AICommander).GetField("_board", Hidden);
    static int checks;
    static void Check(string label, bool result)
    { if (!result) throw new InvalidOperationException("[AIR2ObservedMemory] FAIL " + label); checks++; Debug.Log("[AIR2ObservedMemory] PASS " + label); }
    static T JsonCopy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
    static Dictionary<int, AIBoardState.LastKnownInfo> Memory(AICommander value)
        => (Dictionary<int, AIBoardState.LastKnownInfo>)MemoryField.GetValue(value);
    static AIObservedMemoryEntry Find(AIObservedMemoryState value, string id)
        => value.Entries.Find(e => e != null && e.LifeId == id);

    public static void Run(GameSystems live)
    {
        if (!Application.isPlaying || live?.MapCreate == null) throw new InvalidOperationException("Started native game required.");
        checks = 0;
        string nations = JsonUtility.ToJson(live.FactionState.PlayerNation) + JsonUtility.ToJson(live.FactionState.EnemyNation);
        var random = UnityEngine.Random.state;
        using (BoardActorAnimation.SuppressEffects())
            foreach (var team in new[] { Team.Player, Team.Enemy }) Verify(live, team);
        Check("memory tests preserve both actual nations", nations == JsonUtility.ToJson(live.FactionState.PlayerNation) + JsonUtility.ToJson(live.FactionState.EnemyNation));
        Check("memory tests preserve actual gameplay random state", random.Equals(UnityEngine.Random.state));
        Debug.Log("[AIR2ObservedMemory] ALL PASSED " + checks);
    }

    static void Verify(GameSystems live, Team team)
    {
        using var f = new Fixture(live, team);
        string label = team.ToString();
        var board = f.Board(f.Commander, 60);
        var kingPos = GridHelper.ToGrid(f.King.transform.position); var crystalPos = GridHelper.ToGrid(f.Crystal.transform.position);
        var observed = f.Commander.CaptureObservedMemory();
        var king = Find(observed, f.King.ReflectionLifeId); var crystal = Find(observed, f.Crystal.ReflectionLifeId);
        Check(label + " actual visible king and separate-parent crystal enter observation memory", king != null && king.WasPrincipalCommander
            && crystal != null && crystal.IsMainCrystal && board.IsSpecialActorObserved(f.King) && board.PlayerCrystalVisible);
        long observedMask = king.ActionProfile.Attack.Cells;
        var inside = typeof(AICommander).GetMethod("InsideObservedPosition", Hidden);
        bool allHeights = true;
        foreach (var cell in live.MapCreate.SetPos)
            if (!(bool)inside.Invoke(f.Commander, new object[] { GridHelper.ToGrid(cell) })) { allHeights = false; break; }
        Check(label + " configured Y bounds include every generated SetPos top-surface height", allHeights);

        f.Seen.Clear(); board.Refresh();
        f.King.transform.position = f.Cells[0] + Vector3.forward * 7; f.King.HP = 1; f.King.ATK = 9999;
        f.King.direction = Direction.N; f.King.kind = Kind.Scout;
        f.Profile.attack.cells = long.MaxValue;
        f.Crystal.transform.position += Vector3.right * 10000; f.Crystal.HP = 1;
        if (f.Crystal.team == Team.Player) f.Economy.Crystals.PCP = f.Crystal.transform.position;
        else f.Economy.Crystals.ECP = f.Crystal.transform.position;
        board.Refresh();
        var frozen = f.Commander.CaptureObservedMemory(); king = Find(frozen, f.King.ReflectionLifeId);
        Check(label + " hidden live movement, HP, attack, kind and facing do not refresh saved facts", king != null
            && king.Position == kingPos && king.Kind == Kind.King && king.Direction == Direction.S && king.ObservedAttackPower == 33
            && king.ActionProfile.Attack.Cells == observedMask && Find(frozen, f.Crystal.ReflectionLifeId).Position == crystalPos);
        string ap = JsonUtility.ToJson(f.Economy.State.GetAPData(team)), resources = JsonUtility.ToJson(f.Economy.Resources);
        var ai = new SaveSystem.AISaveData();
        typeof(SaveSystem).GetMethod("CollectAI", StaticHidden).Invoke(null, new object[] { ai, f.Commander });
        string json = JsonUtility.ToJson(ai);
        ai = JsonUtility.FromJson<SaveSystem.AISaveData>(json);
        Check(label + " AI DTO persists only explicit bounded value observations", ai.HasObservedMemorySnapshot
            && ai.ObservedMemory.Entries.Count >= 2 && ai.ObservedMemory.Entries.Count <= AIObservedMemoryState.MaximumEntries
            && !json.Contains("instanceID") && !json.Contains("ActionProfileSnapshot"));
        var loaded = f.NewCommander(); SaveSystem.RestoreAICommander(ai, loaded);
        var restored = f.Board(loaded, 61);
        Check(label + " actual AI restore keeps the last observed king location and turn", restored.GetLastKnownPrincipalCommander(out var remembered, out int at)
            && remembered == kingPos && at == 60 && restored.AlivePlayerUnits.Count == 0);
        Check(label + " actual AI restore keeps crystal position without reading hidden HP", restored.GetLastKnownPlayerCrystal().Valid
            && restored.GetLastKnownPlayerCrystal().Position == crystalPos && restored.PlayerCrystalHP == -1 && !restored.PlayerCrystalVisible);
        var currentKing = Find(loaded.CaptureObservedMemory(), f.King.ReflectionLifeId);
        Check(label + " loaded masks are observation snapshots rather than current live assets", loaded.RestoredObservationProfileCount > 0
            && currentKing.ActionProfile.Attack.Cells == observedMask && currentKing.ObservedAttackPower == 33);
        Check(label + " restoring AI facts does not change either payment state", ap == JsonUtility.ToJson(f.Economy.State.GetAPData(team))
            && resources == JsonUtility.ToJson(f.Economy.Resources));
        BoardActionProfile runtime = null;
        foreach (var pair in Memory(loaded)) if (pair.Value.LifeId == f.King.ReflectionLifeId) runtime = pair.Value.ActionProfile;
        loaded.RestoreObservedMemory(JsonCopy(ai.ObservedMemory));
        Check(label + " repeated restore releases previous runtime profiles instead of accumulating them", runtime == null
            && loaded.RestoredObservationProfileCount <= AIObservedMemoryState.MaximumEntries);

        restored = f.Board(loaded, 61); f.Seen.Add(GridHelper.ToGridXZ(f.King.transform.position)); restored.Refresh();
        int count = 0; AIBoardState.LastKnownInfo latest = default;
        foreach (var pair in restored.ObservedHistory) if (pair.Value.LifeId == f.King.ReflectionLifeId) { count++; latest = pair.Value; }
        Check(label + " legitimate re-observation replaces a synthetic key once and retains observed movement history", count == 1
            && latest.Position == GridHelper.ToGrid(f.King.transform.position) && latest.PreviousPosition == kingPos
            && latest.HasPrevious && latest.PreviousTurn == 60 && latest.Turn == 61 && latest.ObservedAttackPower == 9999);
        Check(label + " re-observation can update the formerly hidden mask and classification", !latest.WasPrincipalCommander
            && latest.ActionProfileSnapshot.Attack.Cells == long.MaxValue && latest.Kind == Kind.Scout);
        loaded.ReleaseRuntimeReferences();
        Check(label + " battle/scene release boundary frees all restored observation profiles", loaded.RestoredObservationProfileCount == 0);

        BoundsAndLegacy(f, ai.ObservedMemory, label);
        PlayerSaveRoute(f, ai.ObservedMemory, label);
    }

    static void BoundsAndLegacy(Fixture f, AIObservedMemoryState saved, string label)
    {
        var commander = f.NewCommander(); commander.SaveTurnCount = 60;
        var valid = JsonCopy(saved);
        commander.RestoreObservedMemory(valid); BoardField.SetValue(commander, f.Board(commander, 61));
        commander.RestoreObservedMemory(null);
        Check(label + " absent optional memory clears old copied board facts and owned profiles", Memory(commander).Count == 0
            && BoardField.GetValue(commander) == null && commander.RestoredObservationProfileCount == 0);
        var old = JsonUtility.FromJson<SaveSystem.AISaveData>("{\"AITurnCount\":60}");
        SaveSystem.RestoreAICommander(old, commander);
        Check(label + " old AI saves without the optional snapshot stay empty", Memory(commander).Count == 0);
        var wrongSide = JsonCopy(saved); wrongSide.Owner = f.Team == Team.Player ? Team.Enemy : Team.Player;
        commander.RestoreObservedMemory(wrongSide); Check(label + " wrong faction memory is rejected", Memory(commander).Count == 0);
        var future = JsonCopy(saved); future.Version = 99; commander.RestoreObservedMemory(future);
        Check(label + " future optional memory versions are ignored safely", Memory(commander).Count == 0);
        future = JsonCopy(saved); future.SavedAtTurn = 61; commander.RestoreObservedMemory(future);
        Check(label + " future observation-save clock is rejected", Memory(commander).Count == 0);
        future = JsonCopy(saved); future.SavedAtTurn = 59; commander.RestoreObservedMemory(future);
        Check(label + " observations cannot postdate their containing snapshot", Memory(commander).Count == 0);
        var oversized = JsonCopy(saved);
        while (oversized.Entries.Count <= AIObservedMemoryState.MaximumEntries) oversized.Entries.Add(oversized.Entries[0]);
        commander.RestoreObservedMemory(oversized);
        Check(label + " oversized optional arrays allocate no restored profiles", Memory(commander).Count == 0 && commander.RestoredObservationProfileCount == 0);

        var template = Find(saved, f.King.ReflectionLifeId);
        foreach (string invalid in new[] { "x", "y", "team", "direction", "mask", "attack", "time" })
        {
            var sample = JsonCopy(template);
            switch (invalid)
            {
                case "x": sample.Position.x = f.Live.MapCreate.maxX; break;
                case "y": sample.Position.y = f.Live.MapCreate.maxY + 1; break;
                case "team": sample.ObservedTeam = f.Team; break;
                case "direction": sample.Direction = (Direction)999; break;
                case "mask": sample.ActionProfile.Attack.OriginX = BoardTilePattern.Size; break;
                case "attack": sample.ObservedAttackPower = -1; break;
                case "time": sample.Turn = -1; break;
            }
            var state = new AIObservedMemoryState { Owner = f.Team, SavedAtTurn = 60, Entries = new List<AIObservedMemoryEntry> { sample } };
            commander.RestoreObservedMemory(state);
            Check(label + " invalid " + invalid + " observation is discarded", Memory(commander).Count == 0);
        }
        var invalidDuplicate = JsonCopy(template); invalidDuplicate.Position.x = -1;
        var validDuplicate = JsonCopy(template);
        commander.RestoreObservedMemory(new AIObservedMemoryState { Owner = f.Team, SavedAtTurn = 60,
            Entries = new List<AIObservedMemoryEntry> { invalidDuplicate, validDuplicate, JsonCopy(validDuplicate) } });
        Check(label + " an invalid duplicate cannot shadow a valid stable identity and accepted duplicates collapse", Memory(commander).Count == 1);
        var previous = JsonCopy(template); previous.HasPrevious = true; previous.PreviousTurn = 61;
        commander.RestoreObservedMemory(new AIObservedMemoryState { Owner = f.Team, SavedAtTurn = 60,
            Entries = new List<AIObservedMemoryEntry> { previous } });
        Check(label + " a malformed optional previous position does not invent a movement history",
            commander.CaptureObservedMemory().Entries.Count == 1 && !commander.CaptureObservedMemory().Entries[0].HasPrevious);
        var aged = JsonCopy(saved); commander.SaveTurnCount = 67; commander.RestoreObservedMemory(aged);
        var agedState = commander.CaptureObservedMemory();
        Check(label + " seven-turn mobile memory expires while the observed fixed crystal remains", Find(agedState, f.King.ReflectionLifeId) == null
            && Find(agedState, f.Crystal.ReflectionLifeId)?.IsMainCrystal == true);
        commander.SaveTurnCount = 60; commander.RestoreObservedMemory(null);
        var dictionary = Memory(commander);
        for (int i = 0; i < 160; i++) dictionary[i + 1] = new AIBoardState.LastKnownInfo { LifeId = "r2-bounded-" + i, ObservedTeam = f.Opponent,
            Position = GridHelper.ToGrid(f.Cells[0]), Kind = Kind.Scout, Type = Type.Unit, Direction = Direction.N, Turn = 60, Valid = true };
        dictionary[600] = new AIBoardState.LastKnownInfo { LifeId = "r2-important-commander", ObservedTeam = f.Opponent,
            Position = GridHelper.ToGrid(f.Cells[1]), Kind = Kind.King, Type = Type.Unit, Direction = Direction.N, Turn = 60, Valid = true, WasPrincipalCommander = true };
        dictionary[int.MinValue] = new AIBoardState.LastKnownInfo { LifeId = "r2-important-crystal", ObservedTeam = f.Opponent,
            Position = GridHelper.ToGrid(f.Cells[2]), Kind = Kind.Crystal, Type = Type.Building, Direction = Direction.N, Turn = 60, Valid = true };
        var bounded = commander.CaptureObservedMemory();
        Check(label + " capture bounds observations and retains fixed/commander priorities", bounded.Entries.Count == AIObservedMemoryState.MaximumEntries
            && bounded.Entries[0].IsMainCrystal && Find(bounded, "r2-important-commander") != null);
        var board = f.Board(commander, 60);
        Check(label + " normal refresh independently bounds its runtime observation table", Memory(commander).Count <= AIObservedMemoryState.MaximumEntries);
        commander.ReleaseRuntimeReferences();
    }

    static void PlayerSaveRoute(Fixture f, AIObservedMemoryState original, string label)
    {
        if (f.Team != Team.Player) return;
        f.Seen.Clear(); f.Hub.Context.Turn = 60;
        // Keep the actual world snapshot legal as well as the separately frozen AI facts.
        f.Crystal.transform.position = f.Cells[2];
        if (f.Crystal.team == Team.Player) f.Economy.Crystals.PCP = f.Cells[2]; else f.Economy.Crystals.ECP = f.Cells[2];
        f.Hub.RestorePlayerObservedMemory(JsonCopy(original));
        var opponentCommander = f.NewCommander(Team.Enemy);
        var game = SaveSystem.CollectGameState(f.Hub, f.Economy.State, f.Live.TimerSystem, f.Vision, opponentCommander);
        Check(label + " actual game collection includes a separate player observation snapshot", game.HasPlayerObservedMemorySnapshot
            && game.PlayerObservedMemory.Owner == Team.Player && Find(game.PlayerObservedMemory, f.King.ReflectionLifeId) != null
            && game.AI.ObservedMemory.Owner == Team.Enemy);
        var parse = typeof(SaveSystem).GetMethod("ParseGame", StaticHidden);
        game = (SaveSystem.GameSaveData)parse.Invoke(null, new object[] { JsonUtility.ToJson(game) });
        string storage = Path.Combine(Path.GetTempPath(), "FK_R2ObservedMemory_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storage);
        try
        {
            using ((IDisposable)typeof(SaveSystem).GetMethod("UseTestStorageRoot", StaticHidden).Invoke(null, new object[] { storage }))
            {
                bool saved = SaveSystem.TrySaveGame(0, game, out string reason);
                Check(label + " actual validated atomic save accepts the separate player memory: " + reason, saved);
                bool loaded = SaveSystem.TryLoadGame(0, out var disk, out reason);
                Check(label + " actual disk reload preserves both faction observation sections: " + reason, loaded
                    && disk.AI.ObservedMemory.Owner == Team.Enemy && disk.PlayerObservedMemory.Owner == Team.Player
                    && Find(disk.PlayerObservedMemory, f.King.ReflectionLifeId).ObservedAttackPower == 33);
                game = disk;
            }
        }
        finally
        {
            string absolute = Path.GetFullPath(storage);
            string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.GetDirectoryName(absolute) == tempParent && Path.GetFileName(absolute).StartsWith("FK_R2ObservedMemory_", StringComparison.Ordinal))
                Directory.Delete(absolute, true);
        }
        var host = new GameObject("R2 player memory pending load"); host.SetActive(false);
        DeveloperPlayerAIController controller = null;
        try
        {
            var turn = host.AddComponent<TurnGenerator>(); turn.Context.Turn = 60;
            turn.RestorePlayerObservedMemory(game.HasPlayerObservedMemorySnapshot ? game.PlayerObservedMemory : null);
            Check(label + " game JSON round trip and pending load preserve player memory with AI off", !turn.DeveloperPlayerAIEnabled
                && Find(turn.CapturePlayerObservedMemory(), f.King.ReflectionLifeId).Position == original.Entries.Find(e => e.WasPrincipalCommander).Position);
            controller = new DeveloperPlayerAIController(f.Hub);
            controller.RestoreObservedMemory(turn.CapturePlayerObservedMemory());
            Check(label + " developer controller can hold restored observations without enabling automation", !controller.Enabled
                && Find(controller.CaptureObservedMemory(), f.King.ReflectionLifeId) != null);
            var owned = f.NewCommander();
            typeof(DeveloperPlayerAIController).GetField("commander", Hidden).SetValue(controller, owned);
            controller.RestoreObservedMemory(turn.CapturePlayerObservedMemory());
            Check(label + " existing developer commander receives the same frozen facts", Find(owned.CaptureObservedMemory(), f.King.ReflectionLifeId).ObservedAttackPower == 33);
            controller.Dispose();
            Check(label + " controller disposal releases loaded observation profiles", owned.RestoredObservationProfileCount == 0);
            var absent = (SaveSystem.GameSaveData)parse.Invoke(null, new object[] { "{\"AI\":{\"ObservedMemory\":{}},\"PlayerObservedMemory\":{}}" });
            Check(label + " JsonUtility placeholders cannot invent either optional snapshot", absent.AI.ObservedMemory == null && absent.PlayerObservedMemory == null);
        }
        finally { controller?.Dispose(); UnityEngine.Object.DestroyImmediate(host); }
    }

    sealed class Fixture : IDisposable
    {
        public readonly GameSystems Live;
        public readonly Team Team, Opponent;
        public readonly EconomyR2Tests.Fixture Economy;
        public readonly TurnGenerator Hub;
        public readonly VisionGenerator Vision;
        public readonly Status King, Crystal;
        public readonly BoardActionProfile Profile;
        public readonly Vector3[] Cells;
        public readonly AICommander Commander;
        public readonly HashSet<Vector3Int> Seen = new HashSet<Vector3Int>();
        readonly GameObject host;
        readonly List<AICommander> commanders = new List<AICommander>();
        public Fixture(GameSystems live, Team team)
        {
            Live = live; Team = team; Opponent = team == global::Team.Player ? global::Team.Enemy : global::Team.Player;
            Economy = new EconomyR2Tests.Fixture(live, team);
            host = new GameObject("R2 memory isolated services"); host.SetActive(false);
            Hub = host.AddComponent<TurnGenerator>(); Hub.MarkDeveloperMatch(); Hub.Context.Turn = 60;
            Vision = host.AddComponent<VisionGenerator>(); Cells = Economy.AdjacentCells();
            Economy.Crystal.transform.position = Cells[0]; Economy.SetOwnCrystalPosition(Cells[0]);
            var observer = Economy.Unit(); observer.transform.position = Cells[0];
            King = Economy.Opponent(Cells[1], 33); King.kind = Kind.King; King.direction = Direction.S;
            King.RestoreReflectionLifeId("r2-remembered-king-" + team);
            Profile = King.GrowthData.actionProfile; Profile.actionType = ActorActionType.Combat;
            Profile.movement.useCustom = Profile.vision.useCustom = true; Profile.movement.SetCell(4, 3, true); Profile.vision.SetCell(3, 2, true);
            Crystal = Economy.Opponent(Cells[2], 0); Crystal.kind = Kind.Crystal; Crystal.type = Type.Building;
            Crystal.RestoreReflectionLifeId("r2-remembered-crystal-" + team);
            Crystal.transform.SetParent(Crystal.team == global::Team.Player ? Economy.Crystals.Playercrystal : Economy.Crystals.Enemycrystal);
            if (Crystal.team == global::Team.Player) Economy.Crystals.PCP = Cells[2]; else Economy.Crystals.ECP = Cells[2];
            foreach (var cell in Cells) Seen.Add(GridHelper.ToGridXZ(cell));
            var explored = new HashSet<Vector3Int>(Seen);
            foreach (string field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                typeof(VisionGenerator).GetField(field, Hidden).SetValue(Vision,
                    field == (team == global::Team.Player ? "_playerVisionBox" : "_enemyVisionBox") ? Seen
                    : field == (team == global::Team.Player ? "_playerExplored" : "_enemyExplored") ? explored : new HashSet<Vector3Int>());
            Hub.Systems.MapCreate = live.MapCreate; Hub.Systems.CrystalSystem = Economy.Crystals; Hub.Systems.UnitSetting = Economy.Units;
            Hub.Systems.FactionState = Economy.State; Hub.Systems.APSystem = Economy.AP; Hub.Systems.BuildSystem = Economy.Builder;
            Hub.Systems.MoveGenerator = live.MoveGenerator; Hub.Systems.VisionGenerator = Vision;
            Commander = NewCommander();
        }
        public AICommander NewCommander(Team? side = null)
        {
            var commander = new AICommander(Hub, Live.MoveGenerator, Live.AttackGenerator, Live.BattleSystem, Vision, Economy.AP,
                Economy.Units, Economy.Crystals, Live.MapCreate, MajorPersonality.Combat, Economy.Builder, null, Economy.State, null, null,
                15, 251016, side ?? Team);
            commander.SaveTurnCount = 60; commanders.Add(commander); return commander;
        }
        public AIBoardState Board(AICommander commander, int round)
        {
            var board = new AIBoardState(Live.MoveGenerator, Live.AttackGenerator, Economy.AP, Economy.Units, Economy.Crystals, Vision,
                Economy.Builder, null, Economy.State, null, round, Memory(commander), Team) { MapCreate = Live.MapCreate, ReconThreatLevel = 15 };
            BoardField.SetValue(commander, board); return board;
        }
        public void Dispose()
        {
            foreach (var commander in commanders) commander.ReleaseRuntimeReferences();
            UnityEngine.Object.DestroyImmediate(host); Economy.Dispose();
        }
    }
}
#endif
