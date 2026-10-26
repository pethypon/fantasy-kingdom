using System;
using UnityEngine;

/// <summary>Main-crystal-only rules. Territory area and RoundToInt retain the existing formal counter rules.</summary>
public static class MainCrystalRules
{
    public const int Version = 1;
    public const int MaximumHP = 5000;
    public const int LegacyBaseMaximumHP = 15000;
    public const int MaximumCounterTargets = 3;
    public const float CounterFraction = .15f;

    public static int CounterDamage(int maximumHP) => maximumHP <= 0 ? 0 : Mathf.RoundToInt(maximumHP * CounterFraction);

    public static int NormalizeSavedHP(int hp, int oldMaximum, int savedRuleVersion)
    {
        if (hp <= 0) return 0;
        int maximum = NormalizeSavedMaximum(oldMaximum, savedRuleVersion);
        if (savedRuleVersion >= Version || oldMaximum <= 0) return Mathf.Clamp(hp, 1, maximum);
        // Existing balance restoration preserves health ratio, including genuinely dead crystals.
        return Mathf.Clamp((int)Math.Round((double)hp / oldMaximum * maximum), 1, maximum);
    }
    /// <summary>Preserve CrystalShard/earned max-HP additions without reapplying the old base on every load.</summary>
    public static int NormalizeSavedMaximum(int maximum, int savedRuleVersion)
        => savedRuleVersion >= Version ? Mathf.Max(1, maximum)
            : (int)Math.Min(int.MaxValue, (long)MaximumHP + Math.Max(0, (long)maximum - LegacyBaseMaximumHP));

    public static void ClearLegacyShield(Status actor)
    {
        if (actor == null || actor.kind != Kind.Crystal) return;
        actor.ShieldTurns = 0; actor.ShieldActivated = actor.ShieldEverActivated = false;
    }

    public static void RegisterAttack(Status crystal, Team attacker, int round)
    {
        if (crystal == null || crystal.kind != Kind.Crystal || !crystal.IsAlive || round < 0
            || attacker == crystal.team || !MainCrystalCounterState.SupportedTeam(attacker)) return;
        if (crystal.CrystalCounter == null) crystal.CrystalCounter = new MainCrystalCounterState();
        crystal.CrystalCounter.Register(attacker, round);
    }

    public static bool InCounterArea(TerritorySystem territory, Vector3 position, Team owner)
        => territory != null && territory.IsInTerritory(GridHelper.ToGridXZ(position), owner);

    // Tie ordering was unspecified: deterministic cell order, then stable life identity, is an implementation proposal.
    public static int CompareTargets(Vector3 origin, Vector3 a, string aIdentity, Vector3 b, string bIdentity)
    {
        int order = GridHelper.ChebyshevDistance(origin, a).CompareTo(GridHelper.ChebyshevDistance(origin, b));
        if (order != 0) return order;
        order = a.x.CompareTo(b.x); if (order != 0) return order;
        order = a.z.CompareTo(b.z); if (order != 0) return order;
        order = a.y.CompareTo(b.y); if (order != 0) return order;
        return string.CompareOrdinal(aIdentity, bIdentity);
    }

    /// <summary>Observed, raw counter packet. Caller applies its ordinary finite protection budget.</summary>
    public static int ObservedCounterDamage(AIBoardState board, Status actor, Vector3 position, Status attackedCrystal = null,
        Status excludedCrystal = null)
    {
        if (board == null || actor == null || !actor.IsAlive || actor.team != board.ActorTeam
            || actor.type != Type.Unit || actor.kind == Kind.Crystal || board.TerritorySystem == null) return 0;
        int damage = 0;
        foreach (var crystal in board.SpecialObservedActors)
        {
            if (crystal == null || crystal == excludedCrystal || !crystal.IsAlive || crystal.kind != Kind.Crystal || crystal.team != board.OpponentTeam
                || !board.IsSpecialActorObserved(crystal) || !board.IsVisibleToEnemy(crystal.transform.position)) continue;
            bool pending = crystal == attackedCrystal || crystal.CrystalCounter?.PendingRound(board.ActorTeam) == board.TurnCount;
            if (crystal.CrystalCounter?.ConsumedRound(board.ActorTeam) >= board.TurnCount) continue;
            if (!pending || !board.IsVisibleToEnemy(position) || !InCounterArea(board.TerritorySystem, position, crystal.team)) continue;
            int before = 0;
            foreach (var ally in board.AliveEnemyUnits)
            {
                if (ally == null || !ally.IsAlive || ally == actor || ally.team != board.ActorTeam
                    || ally.type != Type.Unit || ally.kind == Kind.Crystal
                    || !board.IsVisibleToEnemy(ally.transform.position)
                    || !InCounterArea(board.TerritorySystem, ally.transform.position, crystal.team)) continue;
                if (CompareTargets(crystal.transform.position, ally.transform.position, ally.ReflectionLifeId,
                    position, actor.ReflectionLifeId) < 0) before++;
                if (before >= MaximumCounterTargets) break;
            }
            if (before < MaximumCounterTargets) damage = (int)Math.Min(int.MaxValue, (long)damage + CounterDamage(actor.MaxHP));
        }
        return damage;
    }
}

/// <summary>Four fixed faction slots; no actor references and no growing history. Optional in old saves.</summary>
[Serializable]
public sealed class MainCrystalCounterState
{
    public int PlayerPending = -1, EnemyPending = -1, MonsterPending = -1, IntruderPending = -1;
    public int PlayerConsumed = -1, EnemyConsumed = -1, MonsterConsumed = -1, IntruderConsumed = -1;
    public static bool SupportedTeam(Team team) => team == Team.Player || team == Team.Enemy || team == Team.Monster || team == Team.Intruder;
    public int PendingRound(Team team) => team == Team.Player ? PlayerPending : team == Team.Enemy ? EnemyPending
        : team == Team.Monster ? MonsterPending : team == Team.Intruder ? IntruderPending : -1;
    public int ConsumedRound(Team team) => team == Team.Player ? PlayerConsumed : team == Team.Enemy ? EnemyConsumed
        : team == Team.Monster ? MonsterConsumed : team == Team.Intruder ? IntruderConsumed : -1;
    void Pending(Team team, int round)
    { if (team == Team.Player) PlayerPending = round; else if (team == Team.Enemy) EnemyPending = round; else if (team == Team.Monster) MonsterPending = round; else if (team == Team.Intruder) IntruderPending = round; }
    void Consumed(Team team, int round)
    { if (team == Team.Player) PlayerConsumed = round; else if (team == Team.Enemy) EnemyConsumed = round; else if (team == Team.Monster) MonsterConsumed = round; else if (team == Team.Intruder) IntruderConsumed = round; }
    public void Register(Team team, int round)
    { if (SupportedTeam(team) && round >= 0 && ConsumedRound(team) < round) Pending(team, round); }
    public bool TryConsume(Team team, int round)
    {
        if (!SupportedTeam(team) || round < 0 || ConsumedRound(team) >= round || PendingRound(team) != round) return false;
        Pending(team, -1); Consumed(team, round); return true;
    }
    public MainCrystalCounterState Copy(bool swapPlayerEnemy = false)
    {
        var copy = (MainCrystalCounterState)MemberwiseClone();
        if (swapPlayerEnemy)
        {
            copy.PlayerPending = EnemyPending; copy.EnemyPending = PlayerPending;
            copy.PlayerConsumed = EnemyConsumed; copy.EnemyConsumed = PlayerConsumed;
        }
        return copy;
    }
    public bool IsValidForRound(int round) => ValidSlot(PlayerPending, round) && ValidSlot(EnemyPending, round)
        && ValidSlot(MonsterPending, round) && ValidSlot(IntruderPending, round) && ValidSlot(PlayerConsumed, round)
        && ValidSlot(EnemyConsumed, round) && ValidSlot(MonsterConsumed, round) && ValidSlot(IntruderConsumed, round);
    static bool ValidSlot(int value, int round) => value >= -1 && value <= round;
}
