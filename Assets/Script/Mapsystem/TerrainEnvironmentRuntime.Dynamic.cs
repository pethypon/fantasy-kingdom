using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Discovery, attack disclosure and cave reservations extend the map's existing environment state.</summary>
public sealed partial class TerrainEnvironmentRuntime
{
    public const int CaveUseAP = 3;
    public bool IsCaveMove(Vector3 from, Vector3 to) => IsCaveConnection(from, to);

    [Serializable]
    public sealed class CaveTransitSnapshot
    {
        public string occupyingUnitId;
        public Team team;
        public Vector2Int entrance;
        public Vector2Int destination;
        public int transitStartRound;
    }

    [Serializable]
    public sealed class AttackRevealSnapshot
    {
        public string entityId;
        public int expireRound;
    }

    public struct CellEffects
    {
        public int VisionModifier, MoveCostModifier, EnterDamage, StayDamage, Heal;
        public bool Concealment, BlocksLineOfSight, Hidden, DynamicState;
        public float IncomingRangedDamageModifier, StoneDamageModifier;
    }

    readonly List<CaveTransitSnapshot> caveTransits = new List<CaveTransitSnapshot>();
    readonly List<AttackRevealSnapshot> attackReveals = new List<AttackRevealSnapshot>();
    int currentRound;

    public bool IsKnownTo(Team team, Vector3 position)
        => !TryCell(position, out var cell) || !cell.Definition.IsHidden
            || cell.State.revealedTo != null && cell.State.revealedTo.Contains(team);

    public TerrainEnvironmentDefinition KnownDefinitionAt(Team team, Vector3 position)
        => IsKnownTo(team, position) ? DefinitionAt(position) : null;

    public bool RevealTo(Team team, Vector3 position)
    {
        if (team == Team.None || !TryCell(position, out var cell) || !cell.Definition.IsHidden) return false;
        if (cell.State.revealedTo == null) cell.State.revealedTo = new List<Team>();
        if (cell.State.revealedTo.Contains(team)) return false;
        cell.State.revealedTo.Add(team); cell.State.revealedTo.Sort(); Revision++;
        return true;
    }

    public int VisionModifier(Vector3 position)
        => TryCell(position, out var cell) && !cell.State.burned ? cell.Definition.VisionModifier : 0;

    public float IncomingDamageMultiplier(Vector3 position, bool isRanged, bool isStone)
    {
        if (!TryCell(position, out var cell) || cell.State.burned || cell.Definition.kind != TerrainEnvironmentKind.Rock) return 1;
        return (isRanged ? .75f : 1) * (isStone ? 1.25f : 1);
    }

    public CellEffects EffectsAt(Status actor, Vector3 position)
    {
        bool found = TryCell(position, out var cell);
        return new CellEffects
        {
            VisionModifier = VisionModifier(position),
            MoveCostModifier = MoveAPAdjustment(position, position),
            EnterDamage = EntryDamage(actor, position), StayDamage = StandingDamage(actor, position), Heal = StandingHeal(actor, position),
            Concealment = IsConcealing(position), BlocksLineOfSight = found && BlocksVision(Key(position)),
            Hidden = found && cell.Definition.IsHidden,
            DynamicState = found && (cell.State.burned || cell.State.smokeRounds > 0),
            IncomingRangedDamageModifier = IncomingDamageMultiplier(position, true, false),
            StoneDamageModifier = IncomingDamageMultiplier(position, false, true)
        };
    }

    public void NotifyAttack(Status actor, int round)
    {
        if (actor == null || !actor.IsAlive || !TryCell(actor.transform.position, out var cell) || cell.State.burned
            || cell.Definition.kind != TerrainEnvironmentKind.Forest || cell.Definition.attackRevealRounds < 0) return;
        currentRound = Math.Max(currentRound, round);
        int expiry = (int)Math.Min(int.MaxValue, (long)round + cell.Definition.attackRevealRounds);
        foreach (var reveal in attackReveals)
            if (reveal.entityId == actor.ReflectionLifeId) { reveal.expireRound = Math.Max(reveal.expireRound, expiry); Revision++; return; }
        attackReveals.Add(new AttackRevealSnapshot { entityId = actor.ReflectionLifeId, expireRound = expiry });
        Revision++;
    }

    bool IsAttackRevealed(Vector3 position)
    {
        foreach (var reveal in attackReveals)
        {
            if (reveal.expireRound <= currentRound) continue;
            var actor = SpecialSkillRuntime.FindActor(reveal.entityId);
            if (actor != null && actor.IsAlive && Key(actor.transform.position) == Key(position)) return true;
        }
        return false;
    }

    void PruneAttackReveals()
    {
        if (attackReveals.RemoveAll(value => value.expireRound <= currentRound || SpecialSkillRuntime.FindActor(value.entityId) == null) > 0) Revision++;
    }

    public bool IsInCaveTransit(Status actor)
    {
        if (actor == null) return false;
        foreach (var transit in caveTransits) if (transit.occupyingUnitId == actor.ReflectionLifeId) return true;
        return false;
    }

    bool CaveReserved(Vector2Int entrance, Vector2Int exit)
    {
        foreach (var transit in caveTransits)
            if ((transit.entrance == entrance || transit.entrance == exit) && SpecialSkillRuntime.FindActor(transit.occupyingUnitId) != null) return true;
        return false;
    }

    public bool CanUseCave(Status actor, Vector3 from, Vector3 to, int round)
    {
        if (actor == null || !actor.IsAlive || actor.type != Type.Unit || IsInCaveTransit(actor)
            || BoardActionProfile.For(actor)?.CanMove == false || StatusEffectSystem.IsStunned(actor)
            || StatusEffectSystem.IsMovementBlocked(actor) || SpecialSkillRuntime.MovementBlocked(actor)
            || !TryCaveExit(from, out var exit) || Key(exit) != Key(to)
            || !IsKnownTo(actor.team, from) || !IsKnownTo(actor.team, to)) return false;
        var entranceCell = cells[Key(from)]; var exitCell = cells[Key(to)];
        return round >= entranceCell.State.caveAvailableRound && round >= exitCell.State.caveAvailableRound
            && !CaveReserved(Key(from), Key(to));
    }

    public bool TryBeginCaveTransit(Status actor, Vector3 destination, int round, GameSystems systems)
    {
        if (!CanUseCave(actor, actor != null ? actor.transform.position : default, destination, round)) return false;
        var moves = systems?.MoveGenerator;
        moves?.UnitPointCore();
        if (moves != null && moves.IsOccupied(moves.Cell(destination))) return false;
        var occupants = CombatRegistry.Enumerate();
        try
        {
            while (occupants.MoveNext())
            {
                var other = occupants.Current;
                if (other != null && other != actor && other.IsAlive && other.type <= Type.Wall && Key(other.transform.position) == Key(destination)) return false;
            }
        }
        finally { occupants.Dispose(); }
        caveTransits.Add(new CaveTransitSnapshot { occupyingUnitId = actor.ReflectionLifeId, team = actor.team,
            entrance = Key(actor.transform.position), destination = Key(destination), transitStartRound = round });
        systems?.MoveUndoSystem?.Clear(); Revision++;
        CombatRuleTelemetry.Record("cave_transit_start", actor, cause: "Cave");
        return true;
    }

    void CompleteCaveTransits(Team team, int round, GameSystems systems)
    {
        // Start order is stable; each reservation is considered once at the faction's next turn boundary.
        for (int i = caveTransits.Count - 1; i >= 0; i--)
        {
            var transit = caveTransits[i];
            var actor = SpecialSkillRuntime.FindActor(transit.occupyingUnitId);
            if (actor == null || !actor.IsAlive || Key(actor.transform.position) != transit.entrance)
            { caveTransits.RemoveAt(i); Revision++; continue; }
            if (transit.team != team || round <= transit.transitStartRound) continue;
            if (!TryCaveExit(actor.transform.position, out var destination) || Key(destination) != transit.destination)
            { caveTransits.RemoveAt(i); Revision++; continue; }
            // Release the movement lock for the shared movement validator, restoring the reservation if an occupied exit delays arrival.
            caveTransits.RemoveAt(i);
            if (!SpecialSkillRuntime.TryForcedMove(actor, destination, systems, out _)) { caveTransits.Insert(i, transit); continue; }
            int cooldown = Math.Max(cells[transit.entrance].Definition.caveCooldownTurns, cells[transit.destination].Definition.caveCooldownTurns);
            int available = (int)Math.Min(int.MaxValue, (long)round + Math.Max(1, cooldown));
            cells[transit.entrance].State.caveAvailableRound = available;
            cells[transit.destination].State.caveAvailableRound = available;
            Revision++;
            CombatRuleTelemetry.Record("cave_transit_arrive", actor, cause: "Cave");
        }
    }

    void CaptureDynamic(Snapshot snapshot)
    {
        foreach (var value in caveTransits) snapshot.caveTransits.Add(new CaveTransitSnapshot { occupyingUnitId = value.occupyingUnitId,
            team = value.team, entrance = value.entrance, destination = value.destination, transitStartRound = value.transitStartRound });
        foreach (var value in attackReveals) snapshot.attackReveals.Add(new AttackRevealSnapshot { entityId = value.entityId, expireRound = value.expireRound });
        snapshot.caveTransits.Sort((a, b) => string.CompareOrdinal(a.occupyingUnitId, b.occupyingUnitId));
        snapshot.attackReveals.Sort((a, b) => string.CompareOrdinal(a.entityId, b.entityId));
    }

    void RestoreDynamic(Snapshot snapshot)
    {
        // Entity references resolve after all saved actors are restored; do not eagerly discard them during map restoration.
        if (snapshot.caveTransits != null) foreach (var value in snapshot.caveTransits)
            if (value != null && !string.IsNullOrEmpty(value.occupyingUnitId) && cells.ContainsKey(value.entrance)
                && cells.ContainsKey(value.destination) && caveTransits.Count < 1024)
                caveTransits.Add(new CaveTransitSnapshot { occupyingUnitId = value.occupyingUnitId, team = value.team,
                    entrance = value.entrance, destination = value.destination, transitStartRound = Math.Max(0, value.transitStartRound) });
        if (snapshot.attackReveals != null) foreach (var value in snapshot.attackReveals)
            if (value != null && !string.IsNullOrEmpty(value.entityId) && value.expireRound > currentRound && attackReveals.Count < 1024)
                attackReveals.Add(new AttackRevealSnapshot { entityId = value.entityId, expireRound = value.expireRound });
    }
}
