using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TerrainEnvironmentRuntime
{
    static readonly Vector2Int[] ClusterDirections = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
    sealed class GenerationRule
    {
        public TerrainEnvironmentDefinition Definition;
        public TerrainEnvironmentGenerationRule Settings;
        public int Placed, Failures;
    }
    // An environment-only PRNG keeps generation independent of global UnityEngine.Random state.
    struct EnvironmentRandom
    {
        uint state;
        public EnvironmentRandom(int seed) { state = unchecked((uint)seed); if (state == 0) state = 0x6D2B79F5u; }
        public int Next(int exclusiveMaximum)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (int)(state % (uint)exclusiveMaximum);
        }
    }
    bool generatedAtStart;
    bool generationLocked;
    int generationSeed;
    public bool GeneratedAtStart => generatedAtStart;
    public int GenerationSeed => generationSeed;
    public int LastGenerationAttemptCount { get; private set; }

    /// <summary>
    /// 初期駒の配置後、新規ゲームだけから呼ぶ。森・草むらは上下左右につながる3～4マス。
    /// 保存された配置はSnapshotが正であり、ロードからは生成を呼ばない。
    /// </summary>
    public void GenerateInitial(IReadOnlyList<Vector3> initialCrystalCenters = null)
    {
        var settings = catalog?.generation;
        if (generatedAtStart || generationLocked || catalog == null || !catalog.enabledForPlay || settings == null || !settings.enabled) return;
        generatedAtStart = true;
        generationSeed = ComputeGenerationSeed(settings.seedOffset);
        var random = new EnvironmentRandom(generationSeed);
        var legal = new Dictionary<Vector2Int, float>();
        if (map.SetPos != null) foreach (var position in map.SetPos)
        {
            var grid = GridHelper.ToGridXZ(position); var cell = new Vector2Int(grid.x, grid.z);
            if (!legal.ContainsKey(cell) && map.TryGetHeight(cell.x, cell.y, out float height)) legal.Add(cell, height);
        }
        var reserved = new HashSet<Vector2Int>();
        int safeRadius = Mathf.Clamp(settings.initialSafeRadius, 5, 12);
        if (initialCrystalCenters != null) foreach (var center in initialCrystalCenters)
        {
            var origin = GridHelper.ToGridXZ(center);
            for (int x = -safeRadius; x <= safeRadius; x++) for (int z = -safeRadius; z <= safeRadius; z++)
                reserved.Add(new Vector2Int(origin.x + x, origin.z + z));
        }
        try
        {
            CombatRegistry.Collect(actors);
            foreach (var actor in actors) if (actor != null && actor.IsAlive) reserved.Add(Key(actor.transform.position));
        }
        finally { actors.Clear(); }
        var candidates = new List<Vector2Int>();
        foreach (var cell in legal.Keys) if (!reserved.Contains(cell) && !cells.ContainsKey(cell)) candidates.Add(cell);
        candidates.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        float coverage = float.IsNaN(settings.coverage) || float.IsInfinity(settings.coverage) ? 0 : Mathf.Clamp(settings.coverage, 0, .4f);
        int budget = Math.Min(Math.Max(0, Mathf.Clamp(settings.maximumCells, 0, 4096) - cells.Count), Mathf.FloorToInt(candidates.Count * coverage));
        int maximumAttempts = Mathf.Clamp(settings.maximumAttempts, 1, 8192);
        var rules = BuildGenerationRules(settings);
        int placed = 0;
        // Give each configured kind a chance before spending the remaining density budget by weight.
        foreach (var rule in rules)
        {
            for (int attempt = 0; attempt < 48 && CanGenerate(rule, budget - placed) && LastGenerationAttemptCount < maximumAttempts; attempt++)
            {
                LastGenerationAttemptCount++;
                int count = TryGenerateRule(rule, budget - placed, candidates, legal, reserved, ref random);
                if (count > 0) { placed += count; break; }
                rule.Failures++;
            }
        }
        while (placed < budget && LastGenerationAttemptCount < maximumAttempts)
        {
            int totalWeight = 0;
            foreach (var rule in rules) if (CanGenerate(rule, budget - placed)) totalWeight += Mathf.Clamp(rule.Settings.weight, 1, 100);
            if (totalWeight == 0) break;
            int roll = random.Next(totalWeight); GenerationRule selected = null;
            foreach (var rule in rules)
            {
                if (!CanGenerate(rule, budget - placed)) continue;
                roll -= Mathf.Clamp(rule.Settings.weight, 1, 100);
                if (roll < 0) { selected = rule; break; }
            }
            LastGenerationAttemptCount++;
            int count = TryGenerateRule(selected, budget - placed, candidates, legal, reserved, ref random);
            if (count == 0) selected.Failures++; else { placed += count; selected.Failures = 0; }
        }
        Revision++;
    }

    int ComputeGenerationSeed(int offset)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)BitConverter.ToInt32(BitConverter.GetBytes(map.SeedX), 0)) * 16777619u;
            hash = (hash ^ (uint)BitConverter.ToInt32(BitConverter.GetBytes(map.SeedZ), 0)) * 16777619u;
            hash = (hash ^ (uint)map.maxX) * 16777619u; hash = (hash ^ (uint)map.maxZ) * 16777619u;
            return (int)((hash ^ (uint)offset) * 16777619u);
        }
    }
    List<GenerationRule> BuildGenerationRules(TerrainEnvironmentGenerationSettings settings)
    {
        var result = new List<GenerationRule>(); var seen = new HashSet<TerrainEnvironmentKind>();
        if (settings.rules == null || catalog.definitions == null) return result;
        foreach (var rule in settings.rules)
        {
            if (rule == null || !rule.enabled || rule.weight <= 0 || rule.maximumCells <= 0
                || rule.kind == TerrainEnvironmentKind.Smoke || !seen.Add(rule.kind)) continue;
            foreach (var definition in catalog.definitions)
                if (definition != null && definition.kind == rule.kind && definition.IsActive)
                {
                    result.Add(new GenerationRule { Definition = definition, Settings = rule }); break;
                }
        }
        return result;
    }
    static bool Vegetation(TerrainEnvironmentKind kind) => kind == TerrainEnvironmentKind.Forest || kind == TerrainEnvironmentKind.Bush;
    static int MinimumCells(GenerationRule rule) => Vegetation(rule.Definition.kind) ? 3 : rule.Definition.kind == TerrainEnvironmentKind.Cave ? 2 : 1;
    static bool CanGenerate(GenerationRule rule, int budget) => rule != null && rule.Failures < 96
        && budget >= MinimumCells(rule) && Mathf.Clamp(rule.Settings.maximumCells, 0, 4096) - rule.Placed >= MinimumCells(rule);

    int TryGenerateRule(GenerationRule rule, int budget, List<Vector2Int> candidates, Dictionary<Vector2Int, float> legal,
        HashSet<Vector2Int> reserved, ref EnvironmentRandom random)
    {
        if (!CanGenerate(rule, budget) || candidates.Count == 0) return 0;
        var origin = candidates[random.Next(candidates.Count)];
        if (!CanGenerateAt(origin, rule.Definition, legal, reserved)) return 0;
        var shape = new List<Vector2Int>(4) { origin };
        if (Vegetation(rule.Definition.kind))
        {
            int remaining = Math.Min(budget, Mathf.Clamp(rule.Settings.maximumCells, 0, 4096) - rule.Placed);
            int size = remaining >= 4 ? 3 + random.Next(2) : 3;
            var frontier = new List<Vector2Int>(12);
            while (shape.Count < size)
            {
                frontier.Clear();
                foreach (var member in shape) foreach (var direction in ClusterDirections)
                {
                    var next = member + direction;
                    if (!shape.Contains(next) && !frontier.Contains(next) && CanGenerateAt(next, rule.Definition, legal, reserved)
                        && Mathf.Abs(legal[next] - legal[origin]) < .01f) frontier.Add(next);
                }
                if (frontier.Count == 0) return 0; // Never leave a partial 1- or 2-cell vegetation cluster.
                shape.Add(frontier[random.Next(frontier.Count)]);
            }
        }
        else if (rule.Definition.kind == TerrainEnvironmentKind.Cave)
        {
            bool paired = false;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                var other = candidates[random.Next(candidates.Count)];
                if (Math.Max(Math.Abs(origin.x - other.x), Math.Abs(origin.y - other.y)) < 6
                    || !CanGenerateAt(other, rule.Definition, legal, reserved)) continue;
                shape.Add(other); paired = true; break;
            }
            if (!paired) return 0; // A solitary entrance is never usable.
        }
        foreach (var cell in shape) Add(rule.Definition, new CellSnapshot
        {
            definitionId = rule.Definition.definitionId, cell = cell,
            hasCaveExit = shape.Count == 2 && rule.Definition.kind == TerrainEnvironmentKind.Cave,
            caveExit = shape.Count == 2 && rule.Definition.kind == TerrainEnvironmentKind.Cave ? shape[cell == shape[0] ? 1 : 0] : default
        });
        rule.Placed += shape.Count;
        return shape.Count;
    }
    bool CanGenerateAt(Vector2Int cell, TerrainEnvironmentDefinition definition, Dictionary<Vector2Int, float> legal, HashSet<Vector2Int> reserved)
    {
        if (!legal.ContainsKey(cell) || reserved.Contains(cell) || cells.ContainsKey(cell)) return false;
        int approaches = 0;
        foreach (var direction in ClusterDirections)
        {
            var neighbor = cell + direction;
            if (legal.ContainsKey(neighbor) && !BlocksMovement(neighbor.x, neighbor.y)) approaches++;
        }
        if (approaches < 3) return false; // Keep one-cell bridges and narrow terrain corridors clear.
        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
        {
            if (x == 0 && z == 0) continue;
            var neighbor = cell + new Vector2Int(x, z);
            // An intact eight-cell ring guarantees a path around each generated blocking object.
            if (definition.blocksMovement && (!legal.ContainsKey(neighbor) || BlocksMovement(neighbor.x, neighbor.y))) return false;
            if (Vegetation(definition.kind) && cells.TryGetValue(neighbor, out var existing) && Vegetation(existing.Definition.kind)) return false;
        }
        return true;
    }
}
