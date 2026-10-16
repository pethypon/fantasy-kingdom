using UnityEngine;

/// <summary>Bounded next-turn Dragon damage budget from the observed actor, fixed territory and known terrain.</summary>
public static class ObservedDragonThreat
{
    public static bool IsDragon(Status unit)
        => unit != null && unit.isWildBoss && unit.wildBossArchetype == WildBossArchetype.Dragon;

    public static bool CanBreatheAt(AIBoardState board, Status dragon, Vector3 destination)
    {
        if (board == null || !IsDragon(dragon) || !board.AlivePlayerUnits.Contains(dragon)
            || !dragon.IsAlive || dragon.wildBossMaxAP < 10) return false;
        // ProcessTurn increments the counter, refills AP and promotes the currently observed low-HP boss.
        if (!HighThreat(dragon) && ((long)dragon.wildBossTurnCounter + 1) % 4 != 0) return false;
        if (!GridHelper.IsWithinRange(GridHelper.ToGridXZ(destination), dragon.wildBossTerritoryCenter,
            dragon.wildBossTerritoryRadius)) return false;
        if (MapCreate.IsArcingAttack(dragon.kind, dragon.facilityKind)
            && GridHelper.ChebyshevDistance(dragon.transform.position, destination) >= 2) return true;
        return KnownLineClear(board, dragon.GridPosition, GridHelper.ToGridXZ(destination));
    }

    public static int EstimateBreathDamage(AIBoardState board, Status dragon, Status target, Vector3 destination)
    {
        if (!CanBreatheAt(board, dragon, destination) || target == null || !target.IsAlive
            || target.team != board.ActorTeam || target.type != Type.Unit || target.ShieldTurns > 0) return 0;
        int damage = NormalDamageBudget(dragon, target, destination);
        if (HighThreat(dragon) && dragon.wildBossMaxAP >= 14 && CanHitFrontLine(board, dragon, target, destination))
            return (int)System.Math.Min(int.MaxValue, (long)damage * 2);
        return damage;
    }

    static bool HighThreat(Status dragon) => dragon.wildBossCurrentThreat >= 75 || dragon.HPRatio <= .5f;

    // Like the governor's ordinary forecast this is a damage budget, not a full combat oracle.
    // Reuse the shared damage core; do not inspect enemy vision, global auras or future state.
    // Known defensive passives/nearby support are deliberately not assumed to make a lethal route safe.
    static int NormalDamageBudget(Status dragon, Status target, Vector3 destination)
    {
        float attack = dragon.ATK * StatusEffectSystem.GetATKModifier(dragon) * (1 - dragon.UpkeepPenaltyATKDEF);
        float defense = target.DEF * StatusEffectSystem.GetDEFModifier(target) * (1 - target.UpkeepPenaltyATKDEF);
        float multiplier = 1;
        if (dragon.passiveskill == PassiveSkill.StrangeKingAura && dragon.HPRatio <= GameConstants.StrangeKingRageThreshold)
            multiplier *= GameConstants.StrangeKingRageATKBonus;
        bool back = target.direction == Direction.N ? dragon.transform.position.z < destination.z : dragon.transform.position.z > destination.z;
        if (back) multiplier *= GameConstants.BackAttackBonus;
        if (GridHelper.ToGrid(dragon.transform.position).y - GridHelper.ToGrid(destination).y >= GameConstants.HighGroundYThreshold)
            multiplier *= GameConstants.HighToLowAttackBonus;
        // A knight can receive the out-of-vision increase; hidden/future visibility cannot prove its reduction.
        if (target.kind == Kind.Knight) multiplier *= GameConstants.KnightOutOfVisionDamageIncrease;
        float incoming = StatusEffectSystem.GetIncomingDamageModifier(target);
        int damage = DamageCalculator.FinalizeHitDamage(DamageCalculator.CalcRawBase(attack, defense) * multiplier * incoming, incoming > 0);
        // Roar is consumed before the attack and multiplies the already rounded normal hit.
        if (dragon.wildBossAtkBuffTurns > 1) damage = Mathf.RoundToInt(damage * 1.25f);
        return damage;
    }

    static bool CanHitFrontLine(AIBoardState board, Status dragon, Status target, Vector3 destination)
    {
        var from = dragon.GridPosition;
        var to = GridHelper.ToGridXZ(destination);
        int direction = dragon.direction == Direction.S ? -1 : 1;
        int distance = (to.z - from.z) * direction;
        if (to.x != from.x || distance < 1 || distance > 3) return false;
        bool Blocks(Status actor)
        {
            if (actor == null || actor == target || !actor.IsAlive || actor.type != Type.Unit
                || (actor.team != Team.Player && actor.team != Team.Enemy)) return false;
            var cell = actor.GridPosition;
            int ahead = (cell.z - from.z) * direction;
            if (cell.x != from.x || ahead <= 0 || ahead >= distance) return false;
            // A nearer unit killed by breath is gone before the follow-up selects its first target.
            return !CanBreatheAt(board, dragon, actor.transform.position) || actor.ShieldTurns > 0
                || NormalDamageBudget(dragon, actor, actor.transform.position) < actor.HP;
        }
        foreach (var ally in board.AliveEnemyUnits) if (Blocks(ally)) return false;
        foreach (var hostile in board.AlivePlayerUnits) if (Blocks(hostile)) return false;
        return true;
    }

    static bool KnownLineClear(AIBoardState board, Vector3Int from, Vector3Int to)
    {
        int x = from.x, z = from.z, nx = Mathf.Abs(to.x - x), nz = Mathf.Abs(to.z - z);
        int sx = System.Math.Sign(to.x - x), sz = System.Math.Sign(to.z - z), ix = 0, iz = 0;
        if (KnownMountain(board, x, z)) return false;
        while (ix < nx || iz < nz)
        {
            int decision = (1 + 2 * ix) * nz - (1 + 2 * iz) * nx;
            if (decision == 0)
            {
                if (KnownMountain(board, x + sx, z) || KnownMountain(board, x, z + sz)) return false;
                x += sx; z += sz; ix++; iz++;
            }
            else if (decision < 0) { x += sx; ix++; }
            else { z += sz; iz++; }
            if (KnownMountain(board, x, z)) return false;
        }
        return true;
    }

    static bool KnownMountain(AIBoardState board, int x, int z)
    {
        var map = board.MapCreate ?? board.ReconMap;
        return map != null && board.IsTerrainKnown(new Vector3(x, 0, z)) && map.IsHighMountain(x, z);
    }
}
