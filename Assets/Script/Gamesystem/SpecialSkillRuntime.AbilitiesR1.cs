using System.Collections.Generic;
using UnityEngine;

public static partial class SpecialSkillRuntime
{
    public static float EffectiveATK(Status actor)
        => actor == null ? 0 : Mathf.Max(0f, (float)((long)actor.ATK + GetATKFlatAdjustment(actor))) * StatusEffectSystem.GetATKModifier(actor);
    static int BoundedFloor(float value) => float.IsNaN(value) || value <= 0 ? 0 : value >= int.MaxValue ? int.MaxValue : Mathf.FloorToInt(value);

    public static bool HasAbility(Status actor, UnitSpecialSkill ability)
        => actor != null && Definition(actor)?.enabledForPlay == true && Definition(actor).ability == ability;

    // Compatibility entry points for saved ability IDs and older callers. The former
    // execution/ally-sacrifice passive was replaced; the active vision seal is unchanged.
    public static bool IsAssassinAllyTarget(Status attacker, Status target) => false;

    public static bool IsStrongTarget(Status target) => target != null
        && (target.kind == Kind.Crystal || target.kind == Kind.SubCrystal || target.kind == Kind.Boss || target.isWildBoss);

    public static bool WouldAssassinate(Status attacker, Status target) => false;

    public static bool HasAssassinPassive(Status actor)
        => actor != null && actor.type == Type.Unit && HasAbility(actor, UnitSpecialSkill.AssassinVisionSeal);

    /// <summary>Uses actual observer masks, independent of the presentation fog and the actor's own sight.</summary>
    public static bool HasOtherFriendlyVision(Status actor, IReadOnlyList<Status> observers)
    {
        if (actor == null || !actor.IsAlive || observers == null) return false;
        for (int i = 0; i < observers.Count; i++)
        {
            var observer = observers[i];
            if (observer != actor && observer != null && observer.type == Type.Unit && observer.IsAlive && observer.team == actor.team
                && State(observer).visionZeroTurns <= 0
                && InVision(observer, actor)) return true;
        }
        return false;
    }

    public static int AssassinIsolationDamage(Status actor)
        => actor == null || actor.MaxHP <= 0 ? 0 : Mathf.Max(1, FractionOfMaximum(actor, SpecialSkillDefinition.AssassinIsolationFraction));

    public static bool IsHiddenFrom(Status observer, Status target)
        => observer != null && target != null && State(target).archerEscapeTurns > 0
            && State(target).archerSnapshotIds.Contains(observer.ReflectionLifeId);

    static void ActivateArcherEscape(Status caster)
    {
        var state = State(caster); state.archerSnapshotIds.Clear();
        var iterator = CombatRegistry.Enumerate();
        while (iterator.MoveNext())
        {
            var enemy = iterator.Current;
            if (enemy == null || !enemy.IsAlive || !NeutralFactionSystem.AreHostile(caster, enemy)
                || Distance3D(caster.transform.position, enemy.transform.position) > 2) continue;
            state.archerSnapshotIds.Add(enemy.ReflectionLifeId);
            // Existing poison damage remains authoritative; this snapshot changes only its duration.
            StatusEffectSystem.ApplyDebuff(enemy, StatusEffectType.Poison, SpecialSkillDefinition.ArcherEscapeDuration);
        }
        iterator.Dispose(); state.archerEscapeTurns = SpecialSkillDefinition.ArcherEscapeDuration;
    }

    static int Distance3D(Vector3 a, Vector3 b)
    {
        var delta = GridHelper.ToGrid(a) - GridHelper.ToGrid(b);
        return Mathf.Max(Mathf.Abs(delta.x), Mathf.Max(Mathf.Abs(delta.y), Mathf.Abs(delta.z)));
    }

    static float R1AttackMultiplier(Status attacker, Status target)
    {
        float result = 1;
        if (HasAbility(attacker, UnitSpecialSkill.MagicRitual))
        {
            var iterator = CombatRegistry.Enumerate(); bool lowHP = false;
            while (iterator.MoveNext())
            {
                var nearby = iterator.Current;
                if (nearby != attacker && nearby != null && nearby.IsAlive && OrdinaryUnit(nearby)
                    && nearby.HPRatio <= SpecialSkillDefinition.MagicLowHealthThreshold
                    && Distance3D(attacker.transform.position, nearby.transform.position) <= 1) { lowHP = true; break; }
            }
            iterator.Dispose();
            if (lowHP) result *= IsStrongTarget(target) ? SpecialSkillDefinition.MagicStrongTargetMultiplier : SpecialSkillDefinition.MagicOrdinaryTargetMultiplier;
        }
        if (HasAbility(attacker, UnitSpecialSkill.GuardianLifeLink) && target != null
            && (target.kind == Kind.Crystal || target.kind == Kind.SubCrystal)) result *= 2;
        if (HasAbility(attacker, UnitSpecialSkill.MagicSniperLock) && target != null && target.HPRatio < attacker.HPRatio) result *= 1.5f;
        return result;
    }

    public static int GetATKFlatAdjustment(Status actor)
    {
        if (actor == null) return 0;
        long amount = 0;
        foreach (var owner in RuleActors())
        { if (owner == null || !owner.IsAlive) continue;
            foreach (var cycle in State(owner).paladinCycles)
            {
                if (cycle.returned)
                { if (cycle.targetId == actor.ReflectionLifeId) amount += cycle.returnedAttack; }
                else
                {
                    if (cycle.targetId == actor.ReflectionLifeId) amount -= cycle.stolenAttack;
                    if (cycle.sourceId == actor.ReflectionLifeId && State(owner).ownerTurn > cycle.startOwnerTurn) amount += cycle.stolenAttack;
                }
            }
        }
        return (int)System.Math.Max(int.MinValue, System.Math.Min(int.MaxValue, amount));
    }

    static float R1ATKMultiplier(Status actor)
    {
        float result = 1;
        // Old saved assassinBoost fields remain readable, but no longer modify combat.
        foreach (var archer in RuleActors())
        {
            if (archer != null && archer.IsAlive && State(archer).archerEscapeTurns > 0
                && State(archer).archerSnapshotIds.Contains(actor.ReflectionLifeId)) { result *= SpecialSkillDefinition.ArcherEnemyAttackBoost; break; }
        }
        return result;
    }

    static bool HasFreeAP(Status actor, bool movement)
        => TryFreeAPScope(actor, movement, out _);
    static bool TryFreeAPScope(Status actor, bool movement, out AbilityResetScope scope)
    {
        scope = AbilityResetScope.Unconfigured;
        if (actor == null || !actor.IsAlive || (movement ? State(actor).scoutFreeMoveUsed : State(actor).scoutFreeAttackUsed)) return false;
        foreach (var scout in RuleActors())
        {
            if (!HasAbility(scout, UnitSpecialSkill.ScoutVisionGuard) || !InVision(scout, actor)) continue;
            var definition = Definition(scout);
            var candidate = movement ? definition.scoutFreeMoveScope : definition.scoutFreeAttackScope;
            // Mixed authored scopes use the stricter lifetime token, independent of registry order.
            if (candidate == AbilityResetScope.Lifetime) { scope = candidate; return true; }
            if (candidate == AbilityResetScope.OwnerTurn) scope = candidate;
        }
        return scope != AbilityResetScope.Unconfigured;
    }
    public static bool HasFreeMoveAP(Status actor) => HasFreeAP(actor, true);
    public static bool HasFreeAttackAP(Status actor) => HasFreeAP(actor, false);
    public static void ConsumeFreeAP(Status actor, bool movement)
    {
        if (!TryFreeAPScope(actor, movement, out var scope)) return;
        if (movement) { State(actor).scoutFreeMoveUsed = true; State(actor).scoutMoveUsedScope = scope; }
        else { State(actor).scoutFreeAttackUsed = true; State(actor).scoutAttackUsedScope = scope; }
        CombatRuleTelemetry.Record("ability_trigger", actor, actor, movement ? "ScoutFreeMove" : "ScoutFreeAttack");
    }

    static void R1ActorTurnStart(Status actor, GameSystems systems, int round)
    {
        var state = State(actor); var definition = Definition(actor);
        if (state.scoutMoveUsedScope == AbilityResetScope.OwnerTurn) state.scoutFreeMoveUsed = false;
        if (state.scoutAttackUsedScope == AbilityResetScope.OwnerTurn) state.scoutFreeAttackUsed = false;
        if (state.archerEscapeTurns > 0 && --state.archerEscapeTurns == 0) state.archerSnapshotIds.Clear();
        if (state.assassinBoostTurns > 0 && --state.assassinBoostTurns == 0) state.assassinBoostStacks = 0;
        if (definition?.guardianAttackCostReset == AbilityResetScope.OwnerTurn) state.guardianAttackCount = 0;
        int currentAttack = state.paladinCycles.Count > 0 ? BoundedFloor(EffectiveATK(actor)) : 0;
        foreach (var cycle in state.paladinCycles)
            if (!cycle.returned && state.ownerTurn >= cycle.startOwnerTurn + 2)
            {
                cycle.returnedAttack = BoundedFloor(currentAttack * SpecialSkillDefinition.PaladinReturnFraction);
                cycle.returned = true;
            }
        if (HasAbility(actor, UnitSpecialSkill.PriestPurification))
        {
            var iterator = CombatRegistry.Enumerate();
            while (iterator.MoveNext())
            {
                var target = iterator.Current;
                if (target != null && target.IsAlive && OrdinaryUnit(target) && Distance3D(actor.transform.position, target.transform.position) <= 1)
                    target.ApplyHeal(FractionOfMaximum(target, SpecialSkillDefinition.PriestAuraFraction));
            }
            iterator.Dispose();
        }
    }

    public static void BeginNormalAttack(Status actor)
    {
        if (HasAbility(actor, UnitSpecialSkill.GuardianLifeLink)
            && Definition(actor).guardianAttackCostReset != AbilityResetScope.Unconfigured)
            State(actor).guardianAttackCount = System.Math.Min(1000000, State(actor).guardianAttackCount + 1);
        var systems = actor?.RuntimeSystems;
        systems?.MapCreate?.Environment?.NotifyAttack(actor, systems.TurnGenerator?.Context.Turn ?? 0);
    }
    public static void OnNormalAttackResolved(Status attacker, Status target, int actualDamage, bool killed, float lockDistance = -1)
    {
        if (attacker == null) return;
        if (attacker.IsAlive && HasAbility(attacker, UnitSpecialSkill.DragonKnightPierce)) State(attacker).dragonKnightGauge = Mathf.Min(2, State(attacker).dragonKnightGauge + 1);
        if (actualDamage > 0) State(attacker).dealtDamage = true;
        if (HasAbility(attacker, UnitSpecialSkill.HolyKnightSanctuary) && actualDamage > 0
            && target != null && NeutralFactionSystem.AreHostile(attacker, target))
        {
            int stolen = BoundedFloor(EffectiveATK(target) * SpecialSkillDefinition.PaladinStealFraction);
            State(attacker).paladinCycles.Add(new PaladinAttackCycle { sourceId = attacker.ReflectionLifeId,
                targetId = target.ReflectionLifeId, stolenAttack = stolen, startOwnerTurn = State(attacker).ownerTurn });
        }
        if (HasAbility(attacker, UnitSpecialSkill.MagicSniperLock) && attacker.IsAlive)
        {
            ApplyAbilityDamage(attacker, attacker, FractionOfMaximum(attacker, SpecialSkillDefinition.SniperNormalRecoilFraction), HpChangeKind.NormalDamage, HpChangeCause.SelfRecoil);
            if (lockDistance >= 0 && attacker.IsAlive)
                ApplyAbilityDamage(attacker, attacker, FractionOfMaximum(attacker, Definition(attacker).SniperRecoil(lockDistance)), HpChangeKind.NormalDamage, HpChangeCause.SelfRecoil);
        }
    }

    public static void OnOwnerTurnEnd(GameSystems systems, Team team, int round)
    {
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        var isolatedAssassins = new HashSet<Status>();
        bool needsAssassinCheck = false;
        foreach (var actor in actors)
            if (actor != null && actor.IsAlive && actor.team == team && HasAssassinPassive(actor)
                && State(actor).lastEndRound != round) { needsAssassinCheck = true; break; }
        if (needsAssassinCheck)
        {
            // Refresh only this faction's observer masks once at its end boundary.
            // Buildings, crystals and the third faction use the same authored vision rules.
            if (systems?.VisionGenerator != null && systems.MapCreate != null)
                foreach (var observer in actors)
                    if (observer != null && observer.IsAlive && observer.team == team)
                        systems.VisionGenerator.RefreshActorVision(observer, systems.MapCreate, systems.CrystalSystem);
            // Snapshot coverage before applying damage so iteration order cannot remove support.
            foreach (var actor in actors)
                if (actor != null && actor.IsAlive && actor.team == team && HasAssassinPassive(actor)
                    && !HasOtherFriendlyVision(actor, actors)) isolatedAssassins.Add(actor);
        }
        foreach (var actor in actors)
        {
            if (actor == null || !actor.IsAlive || actor.team != team) continue;
            var state = State(actor);
            if (state.lastEndRound == round) continue;
            state.lastEndRound = round;
            if (isolatedAssassins.Contains(actor))
            {
                int loss = ApplyHpChange(actor, actor, AssassinIsolationDamage(actor), HpChangeKind.DirectHpReduction,
                    cause: HpChangeCause.AssassinIsolation);
                CombatRuleTelemetry.Record("assassin_isolation", actor, actor, requestedDamage: AssassinIsolationDamage(actor), actualHpLoss: loss);
            }
            if (!actor.IsAlive) continue;
            if (state.tauntTurns > 0 && --state.tauntTurns == 0) state.tauntedById = null;
            if (HasAbility(actor, UnitSpecialSkill.LifeBomb))
            {
                state.bomberIdleTurns = state.dealtDamage ? 0 : Mathf.Min(SpecialSkillDefinition.BomberIdleLimit, state.bomberIdleTurns + 1);
                state.dealtDamage = false;
                if (state.bomberIdleTurns >= SpecialSkillDefinition.BomberIdleLimit) ExplodeIdleBomber(actor, systems);
            }
            state.paladinCycles.RemoveAll(cycle => cycle.returned || FindActor(cycle.targetId) == null);
        }
        if (systems?.TurnGenerator?.IsGameOver != true)
        {
            BattleSystem.ProcessCrystalCounterAtTurnEnd(systems, team, round);
            if (systems?.TurnGenerator?.IsGameOver == true) return;
            BarrierSystem.ProcessOwnerTurnEnd(team, round);
            NaturalRecoverySystem.ProcessOwnerTurnEnd(systems, team, round);
        }
    }

    static void ExplodeIdleBomber(Status actor, GameSystems systems)
    {
        int radius = Definition(actor).BomberAutoRadius;
        if (radius < 0) return; // UNRESOLVED_BALANCE: configure existing AoE before enabling automatic explosion.
        int power = (int)System.Math.Min(int.MaxValue, (long)actor.HP * 2);
        var origin = actor.transform.position;
        var targets = new List<Status>(); CombatRegistry.Collect(targets);
        ApplyHpChange(actor, actor, actor.HP, HpChangeKind.ForcedDeath);
        foreach (var target in targets)
            if (target != actor && target != null && target.IsAlive && Distance3D(origin, target.transform.position) <= radius)
                ApplyAbilityDamage(actor, target, power, HpChangeKind.NormalDamage, HpChangeCause.Explosion);
        Refresh(systems);
    }
}
