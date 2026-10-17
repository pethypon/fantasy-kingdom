using System.Collections.Generic;
using UnityEngine;

/// <summary>Owner-turn reservations and moving lightning hazards. No per-frame scene scan.</summary>
public static partial class SpecialSkillRuntime
{
    static int movementBatchDepth;
    static readonly HashSet<(Status unit, Status caster)> movementHits = new HashSet<(Status, Status)>();
    static IReadOnlyList<Status> turnActors;
    static readonly List<Status> turnLightning = new List<Status>();
    internal static void BeginTurnSpecials(IReadOnlyList<Status> actors)
    {
        turnActors = actors;
        turnLightning.Clear();
        if (actors == null) return;
        for (int i = 0; i < actors.Count; i++)
        {
            var definition = Definition(actors[i]);
            if (definition != null && definition.enabledForPlay && definition.ability == UnitSpecialSkill.CrossbowStakes)
                turnLightning.Add(actors[i]);
        }
    }
    internal static void EndTurnSpecials() { turnActors = null; turnLightning.Clear(); }
    static void BeginMovementBatch() { if (movementBatchDepth++ == 0) movementHits.Clear(); }
    static void EndMovementBatch() { if (--movementBatchDepth <= 0) { movementBatchDepth = 0; movementHits.Clear(); } }

    internal static void TickActor(Status unit, GameSystems systems, int round)
    {
        var state = State(unit);
        var definition = Definition(unit);
        if (state.ritualActive && state.ownerTurn >= state.ritualFireTurn)
        {
            var locked = FindActor(state.ritualTargetId);
            int amount = state.storedSacrificeDamage;
            state.ritualTargetId = null;
            state.storedSacrificeDamage = 0;
            if (locked != null && locked.IsAlive)
                ApplyScheduledDamage(unit, locked, amount, HpChangeKind.IgnoreDefenceDamage);
        }
        if (!unit.IsAlive) return;
        if (state.sniperActive)
        {
            var locked = FindActor(state.sniperTargetId);
            if (locked == null || !locked.IsAlive) state.sniperTargetId = null;

        }
        if (!unit.IsAlive) return;
        PruneStakes(unit);
        if (!string.IsNullOrEmpty(state.bombCasterId) && round >= state.bombDueRound)
            ExplodeBomb(unit, systems);
        if (!unit.IsAlive) return;
        if (state.holyKnightTurns > 0 && definition != null
            && definition.ability == UnitSpecialSkill.HolyKnightSanctuary)
        {
            HealSanctuary(unit, definition);
            state.holyKnightTurns--;
            state.holyKnightRecoveryLock = true;
        }
        if (!unit.IsAlive) return;
        // Staying on one or more live lines is one hit per line, once per owner turn.
        if (turnActors != null && turnLightning.Count == 0) return;
        IReadOnlyList<Status> actors = turnActors;
        if (actors == null) { var collected = new List<Status>(); CombatRegistry.Collect(collected); actors = collected; }
        var lightning = turnActors != null ? (IReadOnlyList<Status>)turnLightning : actors;
        for (int i = 0; i < lightning.Count && unit.IsAlive; i++)
        {
            var crossbow = lightning[i];
            if (!TryLightning(crossbow, actors, out var a, out var b, out var multiplier)) continue;
            var d = Definition(crossbow);
            if (FactionApplies(d.lightningFactionMask, crossbow.team, crossbow, unit, null)
                && SegmentTouchesCell(a.transform.position, b.transform.position, unit.GridPosition)
                && MarkCrossbowStayHit(crossbow, unit, round))
                ApplyScheduledDamage(crossbow, unit, LightningDamage(crossbow, unit, multiplier), HpChangeKind.NormalDamage, HpChangeCause.CrossbowLightning);
        }
    }

    static void HealSanctuary(Status unit, SpecialSkillDefinition definition)
    {
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        var origin = unit.GridPosition;
        for (int i = 0; i < actors.Count; i++)
        {
            var target = actors[i];
            if (!AllyTarget(unit, target) || !OrdinaryUnit(target)) continue;
            var delta = target.GridPosition - origin;
            for (int offset = 0; offset < definition.holyKnightArea.Length; offset++)
            {
                var configured = definition.holyKnightArea[offset];
                if (delta.x != configured.x || delta.z != configured.y) continue;
                target.ApplyHeal(FractionOfMaximum(target, SpecialSkillDefinition.HolyKnightHealFraction));
                break;
            }
        }
    }

    internal static void ProcessBombDeath(Status holder, GameSystems systems)
    {
        if (holder == null || string.IsNullOrEmpty(State(holder).bombCasterId)) return;
        if (State(holder).bombDeathPolicy == BombDeathPolicy.Explode) ExplodeBomb(holder, systems);
        else ClearBomb(State(holder));
    }

    static void ExplodeBomb(Status holder, GameSystems systems)
    {
        var state = State(holder);
        if (string.IsNullOrEmpty(state.bombCasterId) || state.bombRadius < 0 || state.bombMask == 0) return;
        var source = FindActor(state.bombCasterId);
        Team sourceTeam = state.bombCasterTeam;
        int radius = state.bombRadius;
        var mask = state.bombMask;
        int power = FractionOfMaximum(holder, SpecialSkillDefinition.BombFraction);
        var origin = holder.GridPosition;
        ClearBomb(state); // A self-cost or chain explosion cannot detonate this reservation twice.
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        if (holder.IsAlive) ApplyHpChange(source, holder, power, HpChangeKind.DirectHpReduction);
        foreach (var target in actors)
        {
            if (target == null || !target.IsAlive || GridHelper.ChebyshevDistance(origin, target.GridPosition) > radius) continue;
            if (!FactionApplies(mask, sourceTeam, source, target, holder)) continue;
            ApplyScheduledDamage(source, target, power, HpChangeKind.IgnoreDefenceDamage, HpChangeCause.Bomb);
        }
        Refresh(systems);
    }

    static void ClearBomb(SpecialState state)
    {
        state.bombCasterId = null;
        state.bombDueRound = 0;
        state.bombRadius = -1;
        state.bombMask = SpecialFactionMask.Unconfigured;
        state.bombDeathPolicy = BombDeathPolicy.Unconfigured;
    }

    internal static void ProcessMovementHazards(Status unit, Vector3 from, Vector3 to, GameSystems systems)
    {
        if (unit == null || !unit.IsAlive || GridHelper.ToGridXZ(from) == GridHelper.ToGridXZ(to)) return;
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        for (int i = 0; i < actors.Count && unit.IsAlive; i++)
        {
            var caster = actors[i];
            if (!TryLightning(caster, actors, out var a, out var b, out var multiplier)) continue;
            var d = Definition(caster);
            if (!FactionApplies(d.lightningFactionMask, caster.team, caster, unit, null)) continue;
            if (!SegmentsCross(from, to, a.transform.position, b.transform.position)) continue;
            if (movementBatchDepth > 0 && !movementHits.Add((unit, caster))) continue;
            ApplyScheduledDamage(caster, unit, LightningDamage(caster, unit, multiplier), HpChangeKind.NormalDamage, HpChangeCause.CrossbowLightning);
        }
    }

    /// <summary>AI supplies only observed actors. Endpoint IDs are resolved from that list, never hidden registry actors.</summary>
    public static int HazardDamageAt(Status unit, Vector3 destination, IReadOnlyList<Status> observed)
    {
        if (unit == null || observed == null) return 0;
        long sum = 0;
        for (int i = 0; i < observed.Count; i++)
        {
            var caster = observed[i];
            if (!TryLightning(caster, observed, out var a, out var b, out var multiplier)) continue;
            var d = Definition(caster);
            if (FactionApplies(d.lightningFactionMask, caster.team, caster, unit, null)
                && SegmentsCross(unit.transform.position, destination, a.transform.position, b.transform.position))
                sum += ObservedLightningDamage(caster, unit, multiplier);
        }
        return (int)System.Math.Min(int.MaxValue, sum);
    }

    static int LightningDamage(Status caster, Status unit, float multiplier)
        => (int)System.Math.Min(int.MaxValue, System.Math.Floor(DamageCalculator.CalcNormal(caster, unit) * (double)multiplier));

    static int ObservedLightningDamage(Status caster, Status unit, float multiplier)
    {
        // CalcNormal includes global King/Knight aura queries. Forecasts may only use
        // the supplied observations; the canonical base formula itself has no scene access.
        int raw = (int)System.Math.Min(int.MaxValue, System.Math.Floor(
            DamageCalculator.EstimateBaseDamage(caster.ATK, unit.DEF) * (double)multiplier));
        return PreviewDamage(unit, raw, HpChangeKind.NormalDamage);
    }

    static int ApplyScheduledDamage(Status source, Status target, int amount, HpChangeKind kind, HpChangeCause cause = HpChangeCause.Skill)
    {
        int actual = ApplyAbilityDamage(source, target, amount, kind, cause);
        if (cause == HpChangeCause.CrossbowLightning) CombatRuleTelemetry.Record("crossbow_line_hit", source, target, requestedDamage: amount, actualHpLoss: actual);
        return actual;
    }

    public static float LightningMultiplier(Status crossbow)
    {
        var state = State(crossbow);
        if (state == null || string.IsNullOrEmpty(state.stakeAId)) return 0;
        int elapsed = Mathf.Max(1, state.ownerTurn - state.stakeAAppliedTurn + 1);
        return SpecialSkillDefinition.StakeMultiplier(elapsed);
    }

    public static float GetStakeMultiplier(Status crossbow) => LightningMultiplier(crossbow);

    static bool TryLightning(Status caster, IReadOnlyList<Status> actors, out Status a, out Status b, out float multiplier)
    {
        a = b = null; multiplier = 0;
        var d = Definition(caster);
        if (caster == null || !caster.IsAlive || d == null || !d.enabledForPlay || d.ability != UnitSpecialSkill.CrossbowStakes
            || d.lightningFactionMask == 0) return false;
        var state = State(caster);
        if (string.IsNullOrEmpty(state.stakeAId) || string.IsNullOrEmpty(state.stakeBId)
            || StakeExpired(state.ownerTurn, state.stakeAAppliedTurn) || StakeExpired(state.ownerTurn, state.stakeBAppliedTurn)) return false;
        a = ObservedActor(state.stakeAId, actors); b = ObservedActor(state.stakeBId, actors);
        if (a == null || b == null || a == b) return false;
        multiplier = LightningMultiplier(caster);
        return true;
    }

    static Status ObservedActor(string id, IReadOnlyList<Status> actors)
    {
        for (int i = 0; i < actors.Count; i++)
            if (actors[i] != null && actors[i].IsAlive && actors[i].ReflectionLifeId == id) return actors[i];
        return null;
    }

    static bool StakeExpired(int ownerTurn, int applied) => ownerTurn - applied >= SpecialSkillDefinition.StakeLifetime;

    static void PruneStakes(Status caster)
    {
        var state = State(caster);
        bool hadA = !string.IsNullOrEmpty(state.stakeAId), hadB = !string.IsNullOrEmpty(state.stakeBId);
        if (!string.IsNullOrEmpty(state.stakeAId)
            && (StakeExpired(state.ownerTurn, state.stakeAAppliedTurn) || FindActor(state.stakeAId) == null)) state.stakeAId = null;
        if (!string.IsNullOrEmpty(state.stakeBId)
            && (StakeExpired(state.ownerTurn, state.stakeBAppliedTurn) || FindActor(state.stakeBId) == null)) state.stakeBId = null;
        if (hadA && string.IsNullOrEmpty(state.stakeAId) || hadB && string.IsNullOrEmpty(state.stakeBId))
            CombatRuleTelemetry.Record("crossbow_stake_expire", caster);
        if (string.IsNullOrEmpty(state.stakeAId) && !string.IsNullOrEmpty(state.stakeBId))
        {
            CombatRuleTelemetry.Record("crossbow_stake_promote", caster);
            state.stakeAId = state.stakeBId;
            state.stakeAAppliedTurn = state.stakeBAppliedTurn;
            state.stakeBId = null;
        }
    }

    static bool FactionApplies(SpecialFactionMask mask, Team sourceTeam, Status source, Status target, Status holder)
    {
        if (target == null) return false;
        if (holder != null && target == holder) return (mask & SpecialFactionMask.Holder) != 0;
        if (target.team == sourceTeam) return (mask & SpecialFactionMask.Allies) != 0;
        bool hostile = source != null ? NeutralFactionSystem.AreHostile(source, target)
            : NeutralFactionSystem.AreHostile(sourceTeam, target);
        return hostile && (mask & SpecialFactionMask.Hostile) != 0;
    }

    // Segment / square intersection makes diagonal lines and multi-cell movement deterministic.
    public static bool SegmentTouchesCell(Vector3 a, Vector3 b, Vector3Int cell)
    {
        float minX = cell.x - .5f, maxX = cell.x + .5f, minZ = cell.z - .5f, maxZ = cell.z + .5f;
        float t0 = 0, t1 = 1, dx = b.x - a.x, dz = b.z - a.z;
        return Clip(-dx, a.x - minX, ref t0, ref t1) && Clip(dx, maxX - a.x, ref t0, ref t1)
            && Clip(-dz, a.z - minZ, ref t0, ref t1) && Clip(dz, maxZ - a.z, ref t0, ref t1);
    }

    static bool Clip(float p, float q, ref float t0, ref float t1)
    {
        if (Mathf.Abs(p) < .00001f) return q >= 0;
        float r = q / p;
        if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
        else { if (r < t0) return false; if (r < t1) t1 = r; }
        return true;
    }

    static bool SegmentsCross(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        // Leaving or travelling along an already occupied line is not a fresh entry.
        if (SegmentTouchesCell(c, d, GridHelper.ToGridXZ(a))) return false;
        // Consider every traversed grid cell once, independent of path length or line thickness.
        int steps = Mathf.Max(Mathf.Abs(Mathf.RoundToInt(b.x - a.x)), Mathf.Abs(Mathf.RoundToInt(b.z - a.z)));
        if (steps == 0) return SegmentTouchesCell(c, d, GridHelper.ToGridXZ(b));
        for (int step = 0; step <= steps; step++)
            if (SegmentTouchesCell(c, d, GridHelper.ToGridXZ(Vector3.Lerp(a, b, step / (float)steps)))) return true;
        return false;
    }
}
