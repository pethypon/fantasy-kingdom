using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Map-owned sparse cells. Effects run only on movement/owner turns; there is no Update or scene search.</summary>
public sealed partial class TerrainEnvironmentRuntime
{
    [Serializable]
    public sealed class CellSnapshot
    {
        public string definitionId;
        public Vector2Int cell;
        public bool burned;
        public int smokeRounds;
        public bool hasCaveExit;
        public Vector2Int caveExit;
        public List<Team> revealedTo = new List<Team>();
        public int caveAvailableRound;
    }
    [Serializable]
    public sealed class Snapshot
    {
        public List<CellSnapshot> cells = new List<CellSnapshot>();
        public int lastWeatherRound = -1;
        public int playerRound = -1, enemyRound = -1, monsterRound = -1, intruderRound = -1, obstacleRound = -1;
        public List<CaveTransitSnapshot> caveTransits = new List<CaveTransitSnapshot>();
        public List<AttackRevealSnapshot> attackReveals = new List<AttackRevealSnapshot>();
        public int currentRound;
    }
    sealed class Cell
    {
        public TerrainEnvironmentDefinition Definition;
        public CellSnapshot State;
        public GameObject Visual;
    }

    readonly MapCreate map;
    readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();
    readonly List<Status> actors = new List<Status>();
    TerrainEnvironmentCatalog catalog;
    Transform visualRoot;
    int lastWeatherRound = -1;
    readonly Dictionary<Team, int> lastOwnerRound = new Dictionary<Team, int>();
    public int Revision { get; private set; }
    public int Count => cells.Count;
    public bool HasEffects => cells.Count != 0;
    public TerrainEnvironmentRuntime(MapCreate owner) { map = owner; }
    static Vector2Int Key(Vector3 value) => new Vector2Int(Mathf.RoundToInt(value.x), Mathf.RoundToInt(value.z));

    public void Initialize(TerrainEnvironmentCatalog source)
    {
        Clear(); catalog = source;
        if (source == null || !source.enabledForPlay || source.placements == null) return;
        foreach (var placement in source.placements)
        {
            if (placement == null || placement.definition == null || !placement.definition.IsActive
                || !map.TryGetHeight(placement.cell.x, placement.cell.y, out _)) continue;
            // One environment per cell makes ordering deterministic. The first authored placement wins.
            if (cells.ContainsKey(placement.cell)) continue;
            Add(placement.definition, new CellSnapshot { definitionId = placement.definition.definitionId, cell = placement.cell,
                hasCaveExit = placement.hasCaveExit, caveExit = placement.caveExit,
                smokeRounds = placement.definition.kind == TerrainEnvironmentKind.Smoke ? placement.definition.burnSmokeRounds : 0 });
        }
        Revision++;
    }
    void Clear()
    {
        cells.Clear(); actors.Clear(); lastOwnerRound.Clear(); lastWeatherRound = -1;
        caveTransits.Clear(); attackReveals.Clear(); currentRound = 0;
        if (visualRoot != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(visualRoot.gameObject);
            else UnityEngine.Object.DestroyImmediate(visualRoot.gameObject);
        }
        visualRoot = null;
    }
    void Add(TerrainEnvironmentDefinition definition, CellSnapshot state)
    {
        var cell = new Cell { Definition = definition, State = state }; cells[state.cell] = cell;
        if (definition.visualPrefab == null || state.burned) return;
        if (visualRoot == null)
        {
            visualRoot = new GameObject("EnvironmentObjects").transform;
            visualRoot.SetParent(map.transform, false);
        }
        if (map.TryGetHeight(state.cell.x, state.cell.y, out float y))
            cell.Visual = UnityEngine.Object.Instantiate(definition.visualPrefab, new Vector3(state.cell.x, y, state.cell.y), Quaternion.identity, visualRoot);
    }
    bool TryCell(Vector3 position, out Cell value) => cells.TryGetValue(Key(position), out value);
    public bool HasCell(Vector3 position) => cells.ContainsKey(Key(position));
    public bool HasKnownCell(HashSet<Vector3Int> known, Team team = Team.Enemy)
    {
        if (known == null) return false;
        foreach (var cell in cells.Keys) if (known.Contains(new Vector3Int(cell.x, 0, cell.y))
            && IsKnownTo(team, new Vector3(cell.x, 0, cell.y))) return true;
        return false;
    }
    public bool IsConcealing(Vector3 position) => TryCell(position, out var cell)
        && (cell.State.burned ? cell.State.smokeRounds > 0 : cell.Definition.concealsOccupants);
    public void RefreshVisibility(HashSet<Vector3Int> visible)
    {
        foreach (var cell in cells.Values)
            if (cell.Visual != null) cell.Visual.SetActive(!cell.State.burned
                && IsKnownTo(Team.Player, new Vector3(cell.State.cell.x, 0, cell.State.cell.y))
                && visible != null && visible.Contains(new Vector3Int(cell.State.cell.x, 0, cell.State.cell.y)));
    }
    public TerrainEnvironmentDefinition DefinitionAt(Vector3 position)
        => TryCell(position, out var cell) && !cell.State.burned ? cell.Definition : null;
    public bool BlocksMovement(int x, int z)
        => cells.TryGetValue(new Vector2Int(x, z), out var cell) && !cell.State.burned && cell.Definition.blocksMovement;
    public bool BlocksShots(int x, int z)
        => cells.TryGetValue(new Vector2Int(x, z), out var cell) && !cell.State.burned && cell.Definition.blocksShots;
    bool BlocksVision(Vector2Int key)
        => cells.TryGetValue(key, out var cell) && (cell.State.burned ? cell.State.smokeRounds > 0 : cell.Definition.blocksVision);
    public int MoveAPAdjustment(Vector3 from, Vector3 to)
        => TryCell(to, out var cell) && !cell.State.burned ? cell.Definition.moveAPAdjustment : 0;

    public bool HasClearVision(Vector3 from, Vector3 to)
    {
        if (cells.Count == 0) return true;
        var start = Key(from); var end = Key(to);
        if (start == end) return true;
        if (cells.TryGetValue(end, out var target))
        {
            bool conceal = target.State.burned ? target.State.smokeRounds > 0 : target.Definition.concealsOccupants;
            int reveal = target.State.burned ? target.Definition.burnSmokeRevealDistance : target.Definition.revealDistance;
            if (target.Definition.kind == TerrainEnvironmentKind.Forest && !target.State.burned) reveal = Mathf.Max(1, reveal);
            if (conceal && !IsAttackRevealed(to)
                && Mathf.Max(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y)) > reveal) return false;
        }
        // A viewer in concealing terrain can see out; other cells block rays behind themselves.
        return Trace(start, end, true);
    }
    public bool HasClearShots(Vector3 from, Vector3 to) => cells.Count == 0 || Trace(Key(from), Key(to), false);
    public bool HasClearMovement(Vector3 from, Vector3 to) => cells.Count == 0 || Trace(Key(from), Key(to), false, true);
    bool Trace(Vector2Int from, Vector2Int to, bool vision, bool movement = false)
    {
        int x = from.x, z = from.y, nx = Mathf.Abs(to.x - x), nz = Mathf.Abs(to.y - z);
        int sx = Math.Sign(to.x - x), sz = Math.Sign(to.y - z), ix = 0, iz = 0;
        while (ix < nx || iz < nz)
        {
            int d = (1 + 2 * ix) * nz - (1 + 2 * iz) * nx;
            if (d == 0)
            {
                if (Blocked(new Vector2Int(x + sx, z), to, vision, movement) || Blocked(new Vector2Int(x, z + sz), to, vision, movement)) return false;
                x += sx; z += sz; ix++; iz++;
            }
            else if (d < 0) { x += sx; ix++; }
            else { z += sz; iz++; }
            if (Blocked(new Vector2Int(x, z), to, vision, movement)) return false;
        }
        return true;
    }
    bool Blocked(Vector2Int cell, Vector2Int end, bool vision, bool movement)
    {
        // The obstructing tile remains discoverable, even though no tile behind it is visible.
        if (vision) return cell != end && BlocksVision(cell);
        if (movement) return BlocksMovement(cell.x, cell.y);
        return BlocksShots(cell.x, cell.y);
    }
    public bool TryCaveExit(Vector3 from, out Vector3 destination)
    {
        destination = default;
        if (!TryCell(from, out var cell) || cell.State.burned || cell.Definition.kind != TerrainEnvironmentKind.Cave
            || !cell.State.hasCaveExit || cell.State.cell == cell.State.caveExit) return false;
        var exit = cell.State.caveExit;
        if (!cells.TryGetValue(exit, out var other) || other.State.burned || other.Definition.kind != TerrainEnvironmentKind.Cave
            || !other.State.hasCaveExit || other.State.caveExit != cell.State.cell) return false;
        if (!map.TryGetHeight(exit.x, exit.y, out float height) || BlocksMovement(exit.x, exit.y)) return false;
        destination = new Vector3(exit.x, height, exit.y); return true;
    }
    public bool IsCaveConnection(Vector3 from, Vector3 to)
        => TryCaveExit(from, out var exit) && Key(exit) == Key(to);

    public int EntryDamage(Status actor, Vector3 destination) => Damage(actor, destination, true);
    public int StandingDamage(Status actor, Vector3 destination) => Damage(actor, destination, false);
    int Damage(Status actor, Vector3 destination, bool entry)
    {
        if (actor == null || actor.type != Type.Unit || !TryCell(destination, out var cell)) return 0;
        if (cell.State.burned) return !entry && cell.State.smokeRounds > 0
            ? Fraction(actor.MaxHP, cell.Definition.burnSmokeDamageFraction) : 0;
        if (cell.Definition.kind == TerrainEnvironmentKind.Geyser) return entry ? Fraction(actor.MaxHP, TerrainEnvironmentDefinition.GeyserDamageFraction) : 0;
        if (cell.Definition.kind == TerrainEnvironmentKind.Smoke) return !entry && cell.State.smokeRounds > 0
            ? Fraction(actor.MaxHP, cell.Definition.maximumHpDamageFraction < 0 ? cell.Definition.burnSmokeDamageFraction : cell.Definition.maximumHpDamageFraction) : 0;
        if (!(entry ? cell.Definition.damageOnEntry : cell.Definition.damageOnOwnerTurn)) return 0;
        long amount = Math.Max(0, cell.Definition.flatDamage);
        if (cell.Definition.maximumHpDamageFraction > 0)
            amount += Fraction(actor.MaxHP, cell.Definition.maximumHpDamageFraction);
        return (int)Math.Min(int.MaxValue, amount);
    }
    public int StandingHeal(Status actor, Vector3 destination)
    {
        if (actor == null || actor.type != Type.Unit || !TryCell(destination, out var cell) || cell.State.burned) return 0;
        return cell.Definition.maximumHpHealFraction > 0 ? Fraction(actor.MaxHP, cell.Definition.maximumHpHealFraction) : 0;
    }
    static int Fraction(int maximumHp, float fraction)
    {
        if (maximumHp <= 0 || fraction <= 0 || float.IsNaN(fraction) || float.IsInfinity(fraction)) return 0;
        // Decimal conversion retains the authored decimal value (e.g. 0.1), preventing 100 * 0.1f from rounding up to 11.
        decimal amount = decimal.Ceiling((decimal)maximumHp * (decimal)Mathf.Min(1, fraction));
        return (int)Math.Min(int.MaxValue, amount);
    }
    public void OnMoved(Status actor, Vector3 from, Vector3 to, GameSystems systems)
    {
        if (actor == null || !actor.IsAlive || actor.type != Type.Unit || Key(from) == Key(to)) return;
        bool wasKnown = IsKnownTo(actor.team, to);
        RevealTo(actor.team, to);
        currentRound = Math.Max(currentRound, systems?.TurnGenerator?.Context.Turn ?? currentRound);
        if (CombatRuleTelemetry.Enabled) CombatRuleTelemetry.Record("terrain_enter", actor, cause: DefinitionAt(to)?.kind.ToString(), knownToAI: wasKnown);
        int amount = EntryDamage(actor, to);
        if (amount > 0)
        {
            int actual = SpecialSkillRuntime.ApplyAbilityDamage(null, actor, amount, HpChangeKind.IgnoreDefenceDamage);
            if (CombatRuleTelemetry.Enabled) CombatRuleTelemetry.Record("terrain_damage", target: actor, cause: DefinitionAt(to)?.kind.ToString() ?? "Smoke", requestedDamage: amount, actualHpLoss: actual, knownToAI: wasKnown);
            if (actual > 0) systems?.MoveUndoSystem?.Clear();
        }
    }
    public void OnOwnerTurnStart(Team team, int round, GameSystems systems)
    {
        if (lastOwnerRound.TryGetValue(team, out int previous) && previous == round) return;
        lastOwnerRound[team] = round;
        currentRound = Math.Max(currentRound, round);
        AdvanceWeather(round, systems);
        CompleteCaveTransits(team, round, systems);
        PruneAttackReveals();
        if (cells.Count == 0) return;
        try
        {
            CombatRegistry.Collect(actors);
            foreach (var actor in actors)
            {
                if (actor == null || actor.team != team || !actor.IsAlive || actor.type != Type.Unit) continue;
                if (CombatRuleTelemetry.Enabled && HasCell(actor.transform.position)) CombatRuleTelemetry.Record("terrain_stay", actor, cause: DefinitionAt(actor.transform.position)?.kind.ToString());
                int damage = StandingDamage(actor, actor.transform.position);
                if (damage > 0)
                {
                    int actual = SpecialSkillRuntime.ApplyAbilityDamage(null, actor, damage, HpChangeKind.IgnoreDefenceDamage);
                    if (CombatRuleTelemetry.Enabled) CombatRuleTelemetry.Record("terrain_damage", target: actor, cause: DefinitionAt(actor.transform.position)?.kind.ToString() ?? "Smoke", requestedDamage: damage, actualHpLoss: actual);
                }
                if (actor.IsAlive)
                {
                    int actual = actor.ApplyHeal(StandingHeal(actor, actor.transform.position));
                    if (actual > 0) CombatRuleTelemetry.Record("terrain_heal", target: actor, cause: "HealingSpring", actualHpLoss: -actual);
                }
            }
        }
        finally { actors.Clear(); }
    }
    void AdvanceWeather(int round, GameSystems systems)
    {
        if (lastWeatherRound < 0) { lastWeatherRound = round; return; }
        if (round <= lastWeatherRound) return;
        long elapsed = (long)round - lastWeatherRound; lastWeatherRound = round; bool changed = false;
        foreach (var cell in cells.Values)
            if (cell.State.smokeRounds > 0)
            {
                cell.State.smokeRounds = (int)Math.Max(0, cell.State.smokeRounds - elapsed);
                if (cell.State.smokeRounds == 0 && cell.Definition.kind == TerrainEnvironmentKind.Smoke) cell.State.burned = true;
                changed = true;
            }
        if (changed) { Revision++; systems?.VisionGenerator?.MarkVisionDirty(); }
    }
    public bool Ignite(Vector3 position, GameSystems systems = null)
    {
        if (!TryCell(position, out var cell) || cell.State.burned || !cell.Definition.burnable) return false;
        cell.State.burned = true; cell.State.smokeRounds = cell.Definition.burnSmokeRounds;
        if (cell.Visual != null) cell.Visual.SetActive(false);
        Revision++; systems?.MoveUndoSystem?.Clear(); systems?.RefreshVision(); return true;
    }
    public Snapshot Capture()
    {
        var result = new Snapshot { lastWeatherRound = lastWeatherRound, playerRound = Round(Team.Player), enemyRound = Round(Team.Enemy),
            monsterRound = Round(Team.Monster), intruderRound = Round(Team.Intruder), obstacleRound = Round(Team.Obstacle), currentRound = currentRound };
        foreach (var cell in cells.Values) result.cells.Add(Copy(cell.State));
        result.cells.Sort((a, b) => a.cell.x != b.cell.x ? a.cell.x.CompareTo(b.cell.x) : a.cell.y.CompareTo(b.cell.y));
        CaptureDynamic(result);
        return result;
    }
    int Round(Team team) => lastOwnerRound.TryGetValue(team, out int round) ? round : -1;
    static CellSnapshot Copy(CellSnapshot value) => new CellSnapshot { definitionId = value.definitionId, cell = value.cell, burned = value.burned,
        smokeRounds = value.smokeRounds, hasCaveExit = value.hasCaveExit, caveExit = value.caveExit,
        revealedTo = value.revealedTo == null ? new List<Team>() : new List<Team>(value.revealedTo), caveAvailableRound = value.caveAvailableRound };
    public void Restore(Snapshot snapshot)
    {
        if (snapshot == null) return; // Old saves keep the current explicitly enabled map configuration.
        var source = catalog; Clear(); catalog = source;
        lastWeatherRound = snapshot.lastWeatherRound;
        currentRound = Math.Max(0, snapshot.currentRound);
        lastOwnerRound[Team.Player] = snapshot.playerRound; lastOwnerRound[Team.Enemy] = snapshot.enemyRound;
        lastOwnerRound[Team.Monster] = snapshot.monsterRound; lastOwnerRound[Team.Intruder] = snapshot.intruderRound;
        lastOwnerRound[Team.Obstacle] = snapshot.obstacleRound;
        if (source == null || !source.enabledForPlay || snapshot.cells == null) { Revision++; return; }
        var definitions = new Dictionary<string, TerrainEnvironmentDefinition>(StringComparer.Ordinal);
        if (source.definitions != null) foreach (var definition in source.definitions)
            if (definition != null && definition.IsActive && !definitions.ContainsKey(definition.definitionId)) definitions.Add(definition.definitionId, definition);
        if (source.placements != null) foreach (var placement in source.placements)
            if (placement?.definition != null && placement.definition.IsActive && !definitions.ContainsKey(placement.definition.definitionId))
                definitions.Add(placement.definition.definitionId, placement.definition);
        foreach (var saved in snapshot.cells)
            if (saved != null && saved.smokeRounds >= 0 && !cells.ContainsKey(saved.cell)
                && saved.definitionId != null && definitions.TryGetValue(saved.definitionId, out var definition)
                && map.TryGetHeight(saved.cell.x, saved.cell.y, out _)) Add(definition, Copy(saved));
        RestoreDynamic(snapshot);
        Revision++;
    }
}
