using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>既存の開発用回帰入口からPlayModeを呼ぶ。生成にPrefabやUnity起動処理は不要。</summary>
public static class EnvironmentGenerationTests
{
    const int Origin = 47000;
    static int checks;
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
    static Vector3 Position(int x, int z) => new Vector3(Origin + x, 1, Origin + z);
    static Vector2Int Cell(int x, int z) => new Vector2Int(Origin + x, Origin + z);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception("[EnvironmentGeneration] " + message); }
    static MapCreate FlatMap(int width = 32, int depth = 32)
    {
        var go = new GameObject("EnvironmentGenerationFixture"); owned.Add(go);
        var map = go.AddComponent<MapCreate>(); map.UseR1Terrain = false; map.maxX = width; map.maxZ = depth;
        for (int x = 0; x < width; x++) for (int z = 0; z < depth; z++) map.SetPos.Add(Position(x, z));
        return map;
    }
    static TerrainEnvironmentCatalog Catalog()
    {
        var catalog = ScriptableObject.CreateInstance<TerrainEnvironmentCatalog>(); owned.Add(catalog);
        catalog.enabledForPlay = true; catalog.generation = TerrainEnvironmentGenerationSettings.CreatePlayableDefaults();
        catalog.generation.coverage = .4f; catalog.generation.maximumCells = 120; catalog.generation.maximumAttempts = 900;
        foreach (TerrainEnvironmentKind kind in Enum.GetValues(typeof(TerrainEnvironmentKind)))
        {
            var value = ScriptableObject.CreateInstance<TerrainEnvironmentDefinition>(); owned.Add(value);
            value.kind = kind; value.definitionId = "generation.test." + kind; value.enabledForPlay = true;
            value.revealDistance = value.burnSmokeRevealDistance = 1;
            value.attackRevealRounds = value.caveCooldownTurns = 1;
            if (kind == TerrainEnvironmentKind.Forest) { value.concealsOccupants = value.blocksVision = value.burnable = true; }
            if (kind == TerrainEnvironmentKind.Bush) value.concealsOccupants = true;
            if (kind == TerrainEnvironmentKind.Rock) value.blocksMovement = value.blocksShots = value.blocksVision = true;
            if (kind == TerrainEnvironmentKind.Mud) value.moveAPAdjustment = 1;
            if (kind == TerrainEnvironmentKind.Road) value.moveAPAdjustment = -1;
            if (kind == TerrainEnvironmentKind.HealingSpring) value.maximumHpHealFraction = .05f;
            if (kind == TerrainEnvironmentKind.PoisonSwamp) { value.damageOnEntry = value.damageOnOwnerTurn = true; value.maximumHpDamageFraction = .05f; }
            catalog.definitions.Add(value);
        }
        return catalog;
    }
    static TerrainEnvironmentDefinition Definition(TerrainEnvironmentCatalog catalog, TerrainEnvironmentKind kind)
        => catalog.definitions.Find(definition => definition.kind == kind);
    static string Signature(TerrainEnvironmentRuntime runtime) => JsonUtility.ToJson(runtime.Capture());
    public static void PlayMode(GameSystems systems = null)
    {
        checks = 0; var originalRandom = UnityEngine.Random.state;
        try
        {
            var map = FlatMap(); var catalog = Catalog();
            var safe = new[] { Position(9, 9), Position(24, 24) };
            map.InitializeEnvironment(catalog);
            Check(map.Environment.Count == 0, "BuildTop initialization only installs authored cells before crystal placement");
            UnityEngine.Random.InitState(7123);
            string randomBefore = JsonUtility.ToJson(UnityEngine.Random.state);
            map.Environment.GenerateInitial(safe);
            string first = Signature(map.Environment);
            Check(map.Environment.Count > 0 && map.Environment.Count <= 120, "new games generate a bounded environment population");
            Check(randomBefore == JsonUtility.ToJson(UnityEngine.Random.state), "environment generation never consumes UnityEngine.Random");
            Check(map.Environment.LastGenerationAttemptCount <= 900, "generation obeys its global attempt budget");
            map.Environment.GenerateInitial(safe);
            Check(first == Signature(map.Environment), "repeated start callback is idempotent");
            map.InitializeEnvironment(catalog); UnityEngine.Random.InitState(17); UnityEngine.Random.value.ToString();
            map.Environment.GenerateInitial(safe);
            Check(first == Signature(map.Environment), "same seed and board ignore unrelated global random calls");
            var shapes = new HashSet<string>();
            Validate(map, catalog, safe, shapes);
            var saved = JsonUtility.FromJson<TerrainEnvironmentRuntime.Snapshot>(first);
            catalog.generation.seedOffset += 55; catalog.generation.maximumCells = 0;
            map.InitializeEnvironment(catalog); map.Environment.Restore(saved); map.Environment.GenerateInitial(safe);
            Check(first == Signature(map.Environment), "saved cells and seed restore exactly despite changed generation settings");
            catalog.generation.maximumCells = 120;
            map.InitializeEnvironment(catalog); map.Environment.Restore(new TerrainEnvironmentRuntime.Snapshot()); map.Environment.GenerateInitial(safe);
            Check(map.Environment.Count == 0, "loaded empty environment snapshot cannot acquire new generated cells");
            map.InitializeEnvironment(catalog); map.Environment.Restore(null); map.Environment.GenerateInitial(safe);
            Check(map.Environment.Count == 0, "pre-environment saves also lock generation");
            for (int seed = 1; seed <= 12; seed++)
            {
                catalog.generation.seedOffset = seed; map.InitializeEnvironment(catalog); map.Environment.GenerateInitial(safe);
                Validate(map, catalog, safe, shapes);
            }
            Check(shapes.Count >= 4, "multiple seeds produce varied connected triomino and tetromino outlines");
            CheckAuthoredAndDisabled(map, catalog);
            CheckChokepoint(); CheckRealHeightmap(); CheckTinyBudgets(); CheckBurnedForestVisuals();
            Debug.Log("[EnvironmentGenerationTests] " + checks + " generation/cluster/reservation/save/budget checks passed");
        }
        finally
        {
            UnityEngine.Random.state = originalRandom;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }
    }
    static void Validate(MapCreate map, TerrainEnvironmentCatalog catalog, IReadOnlyList<Vector3> safe, HashSet<string> shapes)
    {
        var saved = map.Environment.Capture(); var vegetation = new Dictionary<Vector2Int, TerrainEnvironmentKind>();
        var kinds = new HashSet<TerrainEnvironmentKind>();
        foreach (var cell in saved.cells)
        {
            var definition = catalog.definitions.Find(value => value.definitionId == cell.definitionId); kinds.Add(definition.kind);
            Check(map.TryGetHeight(cell.cell.x, cell.cell.y, out _) && !map.IsRiver(cell.cell.x, cell.cell.y)
                && !map.IsHighMountain(cell.cell.x, cell.cell.y), "every generated cell uses legal ground height");
            if (safe != null) foreach (var center in safe)
                Check(GridHelper.ChebyshevDistance(new Vector3(cell.cell.x, 0, cell.cell.y), center) > 5, "initial crystal and squad radius is reserved");
            if (definition.kind == TerrainEnvironmentKind.Forest || definition.kind == TerrainEnvironmentKind.Bush) vegetation.Add(cell.cell, definition.kind);
            if (definition.kind == TerrainEnvironmentKind.Cave)
            {
                var other = saved.cells.Find(value => value.cell == cell.caveExit);
                Check(cell.hasCaveExit && other != null && other.hasCaveExit && other.caveExit == cell.cell, "generated cave endpoints are reciprocal pairs");
            }
            if (definition.blocksMovement) for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                if (x != 0 || z != 0) Check(map.HasTileAt(cell.cell.x + x, cell.cell.y + z)
                    && !map.Environment.BlocksMovement(cell.cell.x + x, cell.cell.y + z), "generated rocks keep an intact traversable ring");
        }
        Check(!kinds.Contains(TerrainEnvironmentKind.Smoke), "smoke only appears after burning rather than at map generation");
        var visited = new HashSet<Vector2Int>();
        foreach (var origin in vegetation.Keys)
        {
            if (visited.Contains(origin)) continue;
            var cluster = new List<Vector2Int>(); var pending = new Queue<Vector2Int>(); pending.Enqueue(origin); visited.Add(origin);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue(); cluster.Add(current);
                for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                {
                    var next = current + new Vector2Int(x, z);
                    if (vegetation.ContainsKey(next) && visited.Add(next)) pending.Enqueue(next);
                }
            }
            Check(cluster.Count == 3 || cluster.Count == 4, "touching vegetation never merges into a cluster larger than four");
            var orthogonal = new HashSet<Vector2Int> { origin }; pending.Enqueue(origin);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                foreach (var direction in Directions) if (cluster.Contains(current + direction) && orthogonal.Add(current + direction)) pending.Enqueue(current + direction);
            }
            Check(orthogonal.Count == cluster.Count, "every vegetation cluster is connected by tile edges");
            int minX = int.MaxValue, minZ = int.MaxValue;
            map.TryGetHeight(origin.x, origin.y, out float height);
            foreach (var cell in cluster)
            {
                Check(vegetation[cell] == vegetation[origin], "forest and bush clusters do not blend together");
                Check(map.TryGetHeight(cell.x, cell.y, out float otherHeight) && Mathf.Abs(height - otherHeight) < .01f, "a vegetation cluster stays on one height");
                minX = Math.Min(minX, cell.x); minZ = Math.Min(minZ, cell.y);
            }
            cluster.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            string shape = ""; foreach (var cell in cluster) shape += (cell.x - minX) + "," + (cell.y - minZ) + ";";
            shapes.Add(shape);
        }
    }
    static void CheckAuthoredAndDisabled(MapCreate map, TerrainEnvironmentCatalog catalog)
    {
        var authored = Cell(3, 3); catalog.placements.Add(new TerrainEnvironmentPlacement { definition = Definition(catalog, TerrainEnvironmentKind.Forest), cell = authored });
        map.InitializeEnvironment(catalog); map.Environment.GenerateInitial();
        Check(map.Environment.DefinitionAt(new Vector3(authored.x, 1, authored.y))?.kind == TerrainEnvironmentKind.Forest, "authored placement is preserved");
        var saved = map.Environment.Capture();
        foreach (var cell in saved.cells) if (cell.cell != authored && (cell.definitionId.EndsWith("Forest") || cell.definitionId.EndsWith("Bush")))
            Check(Math.Max(Math.Abs(cell.cell.x - authored.x), Math.Abs(cell.cell.y - authored.y)) > 1, "generated vegetation cannot merge with authored vegetation");
        catalog.generation.enabled = false; map.InitializeEnvironment(catalog); map.Environment.GenerateInitial();
        Check(map.Environment.Count == 1, "disabled auto generation keeps manual placements");
        catalog.enabledForPlay = false; map.InitializeEnvironment(catalog); map.Environment.GenerateInitial();
        Check(map.Environment.Count == 0, "disabled catalog keeps the original unmodified terrain");
    }
    static void CheckChokepoint()
    {
        var map = FlatMap(15, 9); map.SetPos.RemoveAll(value => value.x > Origin + 5 && value.x < Origin + 9 && value.z != Origin + 4);
        var catalog = Catalog(); catalog.generation.rules.RemoveAll(value => value.kind != TerrainEnvironmentKind.Rock);
        catalog.generation.rules[0].maximumCells = 80; map.InitializeEnvironment(catalog); map.Environment.GenerateInitial();
        Check(map.Environment.Count > 0, "open rooms still receive rocks");
        for (int x = 6; x <= 8; x++) Check(!map.Environment.HasCell(Position(x, 4)), "one-cell corridor remains clear");
        var reached = new HashSet<Vector2Int> { Cell(0, 0) }; var pending = new Queue<Vector2Int>(); pending.Enqueue(Cell(0, 0));
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var direction in Directions)
            {
                var next = current + direction;
                if (map.HasTileAt(next.x, next.y) && !map.Environment.BlocksMovement(next.x, next.y) && reached.Add(next)) pending.Enqueue(next);
            }
        }
        Check(reached.Count == map.SetPos.Count - map.Environment.Count, "blocking objects cannot disconnect traversable ground");
    }
    static void CheckRealHeightmap()
    {
        var map = FlatMap(40, 40); map.SetPos.Clear(); map.UseR1Terrain = true; map.GenerateNoise(12.35f, 88.41f);
        for (int x = 0; x < 40; x++) for (int z = 0; z < 40; z++) if (map.TryGetHeight(x, z, out float height)) map.SetPos.Add(new Vector3(x, height, z));
        var catalog = Catalog(); map.InitializeEnvironment(catalog); map.Environment.GenerateInitial();
        Check(map.Environment.Count > 0, "Perlin terrain accepts legal environment cells");
        Validate(map, catalog, null, new HashSet<string>());
    }
    static void CheckTinyBudgets()
    {
        var map = FlatMap(20, 20); var catalog = Catalog(); catalog.generation.maximumCells = 3; catalog.generation.maximumAttempts = 5;
        map.InitializeEnvironment(catalog); map.Environment.GenerateInitial();
        Check(map.Environment.Count <= 3 && map.Environment.LastGenerationAttemptCount <= 5, "tiny cell and work budgets are hard limits");
        foreach (var cell in map.Environment.Capture().cells) Check(cell.definitionId.EndsWith("Forest"), "first bounded cluster remains complete");
        catalog.generation.coverage = float.NaN; map.InitializeEnvironment(catalog); map.Environment.GenerateInitial();
        Check(map.Environment.Count == 0, "non-finite density cannot create unbounded generation work");
    }
    static int VisibleVisuals(MapCreate map, string prefix)
    {
        int count = 0;
        foreach (var child in map.GetComponentsInChildren<Transform>(true))
            if (child.name.StartsWith(prefix, StringComparison.Ordinal) && child.gameObject.activeInHierarchy) count++;
        return count;
    }
    static void CheckBurnedForestVisuals()
    {
        var map = FlatMap(8, 8); var catalog = Catalog(); catalog.generation.enabled = false;
        var forest = Definition(catalog, TerrainEnvironmentKind.Forest); forest.burnSmokeRounds = 2;
        var smoke = Definition(catalog, TerrainEnvironmentKind.Smoke);
        var forestPrefab = new GameObject("GenerationForestVisual"); owned.Add(forestPrefab); forestPrefab.SetActive(false);
        var smokePrefab = new GameObject("GenerationSmokeVisual"); owned.Add(smokePrefab); smokePrefab.SetActive(false);
        forest.visualPrefab = forestPrefab; smoke.visualPrefab = smokePrefab;
        catalog.placements.Add(new TerrainEnvironmentPlacement { definition = forest, cell = Cell(3, 3) });
        var visible = new HashSet<Vector3Int> { GridHelper.ToGridXZ(Position(3, 3)) };
        map.InitializeEnvironment(catalog);
        Check(VisibleVisuals(map, "GenerationForestVisual") == 0, "new visual waits for fog visibility before rendering");
        map.Environment.RefreshVisibility(visible);
        Check(VisibleVisuals(map, "GenerationForestVisual") == 1, "visible forest uses authored prefab");
        Check(map.Environment.Ignite(Position(3, 3)), "forest fixture burns");
        map.Environment.RefreshVisibility(visible);
        Check(VisibleVisuals(map, "GenerationForestVisual") == 0 && VisibleVisuals(map, "GenerationSmokeVisual") == 1,
            "burning replaces the same cell's forest visual with smoke");
        map.Environment.RefreshVisibility(new HashSet<Vector3Int>());
        Check(VisibleVisuals(map, "GenerationSmokeVisual") == 0, "smoke visual obeys current fog visibility");
        var saved = JsonUtility.FromJson<TerrainEnvironmentRuntime.Snapshot>(Signature(map.Environment));
        map.Environment.RefreshVisibility(visible);
        map.Environment.OnOwnerTurnStart(Team.Player, 0, null); map.Environment.OnOwnerTurnStart(Team.Player, 2, null);
        Check(VisibleVisuals(map, "GenerationSmokeVisual") == 0 && !map.Environment.IsConcealing(Position(3, 3)), "expired smoke hides immediately with its gameplay effect");
        map.Environment.Restore(saved); map.Environment.RefreshVisibility(visible);
        Check(VisibleVisuals(map, "GenerationForestVisual") == 0 && VisibleVisuals(map, "GenerationSmokeVisual") == 1,
            "loading burned forest recreates smoke rather than resurrecting a tree");
    }
}
