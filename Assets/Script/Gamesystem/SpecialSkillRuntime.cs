using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Special ability rules. No Update loop, hidden AI observations, or duplicate ability table.</summary>
public static partial class SpecialSkillRuntime
{
    static readonly List<Status> knightObservers = new List<Status>();
    static readonly List<Status> ruleActors = new List<Status>();
    static int knightCacheFrame = -1;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuleCaches() { knightObservers.Clear(); ruleActors.Clear(); knightCacheFrame = -1; }
    public static void ResetForMatch()
    {
        ResetRuleCaches(); EndTurnSpecials(); movementBatchDepth = 0; movementHits.Clear();
        CombatRuleTelemetry.Reset(); ResetHpHistory();
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        foreach (var actor in actors) { actor.Special = new SpecialState(); actor.NaturalRecovery = new NaturalRecoveryState(); }
    }
    static List<Status> RuleActors()
    {
        if (knightCacheFrame == Time.frameCount) return ruleActors;
        knightCacheFrame = Time.frameCount; knightObservers.Clear(); ruleActors.Clear();
        var iterator = CombatRegistry.Enumerate();
        while (iterator.MoveNext())
        {
            var actor = iterator.Current; var definition = Definition(actor);
            if (actor == null || !actor.IsAlive || definition == null || !definition.enabledForPlay) continue;
            ruleActors.Add(actor);
            if (definition.ability == UnitSpecialSkill.KnightBreakthrough && definition.knightVisibleEnemyAtkPenalty >= 0) knightObservers.Add(actor);
        }
        iterator.Dispose(); return ruleActors;
    }
    public static void InvalidateRules() => knightCacheFrame = -1;
    public static SpecialSkillDefinition Definition(Status actor) => actor != null ? actor.GrowthData?.specialSkill : null;
    public static SpecialState State(Status actor) => actor == null ? null : actor.Special ?? (actor.Special = new SpecialState());
    public static Status FindActor(string lifeId)
    {
        if (string.IsNullOrEmpty(lifeId)) return null;
        return CombatRegistry.FindLife(lifeId);
    }
    public static bool InVision(Status observer, Status target)
    {
        if (observer == null || target == null || !target.IsAlive || observer.VisionCell == null || IsHiddenFrom(observer, target)) return false;
        var p = target.GridPosition;
        foreach (var cell in observer.VisionCell) if (cell.x == p.x && cell.z == p.z) return true;
        return false;
    }
    public static void Refresh(GameSystems systems)
    { InvalidateRules(); systems?.MoveGenerator?.UnitPointCore(); systems?.RefreshVision(); }

    public static bool CanUse(Status caster, GameSystems systems, out string reason)
        => CanUse(caster, systems, 0, out reason);

    public static bool CanUse(Status caster, GameSystems systems, int berserkerStage, out string reason)
    {
        var definition = Definition(caster);
        reason = definition == null ? "駒固有能力が未設定です" : definition.ConfigurationError;
        if (reason != null) return false;
        if (caster == null || !caster.IsAlive || caster.type != Type.Unit) { reason = "駒が行動できません"; return false; }
        if (definition.IsPassive) { reason = "常時能力です"; return false; }
        if (caster.RuntimeSystems?.MapCreate?.Environment?.IsInCaveTransit(caster) == true) { reason = "洞窟を移動中です"; return false; }
        if (StatusEffectSystem.IsStunned(caster) || !AttackPatterns.CanUseSkills(caster) || StatusEffectSystem.HasDebuff(caster, StatusEffectType.Seal)) { reason = "スキルを使用できない状態です"; return false; }
        if (State(caster).cooldown > 0 || State(caster).ritualActive || State(caster).holyKnightTurns > 0 || State(caster).holyKnightRecoveryLock) { reason = "能力の準備・発動中です"; return false; }
        if (definition.ability == UnitSpecialSkill.BerserkerRage)
        {
            int next = Mathf.Clamp(State(caster).berserkerStage, 0, 5) + 1;
            if (next > 5) { reason = "すでに最大の5段階まで強化しています"; return false; }
            if (berserkerStage != 0 && berserkerStage != next) { reason = "強化段階が変わりました。次の1段階だけ強化できます"; return false; }
        }
        int cost = GetAPCost(caster, berserkerStage);
        if (cost < 0 || cost > APSystem.MaximumSkillAP) { reason = "スキルの消費AP設定を確認してください"; return false; }
        if (systems?.FactionState == null || systems.APSystem == null || systems.FactionState.GetAP(caster.team) < cost) { reason = "行動ポイント不足です"; return false; }
        reason = null; return true;
    }

    public static bool TryExecute(SpecialSkillCommand command, GameSystems systems, out string reason)
    {
        var caster = command?.Caster;
        if (!CanUse(caster, systems, command?.BerserkerStage ?? 0, out reason) || !IsLegalTarget(caster, command.Target, out reason)) return false;
        var definition = Definition(caster);
        int actionCost = GetAPCost(caster, command.BerserkerStage);
        if (definition.ability == UnitSpecialSkill.MagicRitual)
        {
            if (command.Sacrifices == null || command.Sacrifices.Count == 0 || command.Sacrifices.Count > definition.sacrificeLimit) { reason = "供儀対象の人数を確認してください"; return false; }
            var unique = new HashSet<Status>();
            foreach (var ally in command.Sacrifices)
                if (!unique.Add(ally) || !IsLegalSacrifice(caster, ally, out reason)) { reason = reason ?? "供儀対象が重複しています"; return false; }
        }
        bool succeeded;
        switch (definition.ability)
        {
            case UnitSpecialSkill.ArcherStationary:
                ActivateArcherEscape(caster); succeeded = true; break;
            case UnitSpecialSkill.SamuraiCounter:
                State(command.Target).tauntedById = caster.ReflectionLifeId;
                State(command.Target).tauntTurns = SpecialSkillDefinition.SamuraiTauntDuration;
                succeeded = true; break;
            case UnitSpecialSkill.AssassinVisionSeal:
                State(command.Target).visionZeroTurns = 2;
                State(caster).nextDamageDouble = true;
                succeeded = true; break;
            case UnitSpecialSkill.ScoutVisionGuard:
                var affected = new List<Status>(); CombatRegistry.Collect(affected);
                State(caster).affectedUnitIds.Clear();
                foreach (var ally in affected)
                    if (ally.team == caster.team && InVision(caster, ally))
                    { State(ally).damageNullUnused = true; State(ally).damageNullTurns = 2; State(caster).affectedUnitIds.Add(ally.ReflectionLifeId); }
                State(caster).scoutVisionPenaltyTurns = 2;
                succeeded = true; break;
            case UnitSpecialSkill.PriestPurification:
                var origin = caster.GridPosition;
                var targets = new List<Status>(); CombatRegistry.Collect(targets);
                ApplyHpChange(caster, caster, caster.HP, HpChangeKind.ForcedDeath);
                foreach (var actor in targets)
                    if (actor != null && actor.IsAlive && GridHelper.ChebyshevDistance(origin, actor.GridPosition) <= 2)
                    { actor.ActiveEffects.Clear(); ClearTemporaryEffects(actor); }
                succeeded = true; break;
            default:
                succeeded = ExecuteAuthorized(command, systems, systems.TurnGenerator != null ? systems.TurnGenerator.Context.Turn : 0, out reason); break;
        }
        if (!succeeded) return false;
        BoardActorAnimation.NotifySkill(caster, command.Target != null ? (Vector3?)command.Target.transform.position : null);
        // Confirmed actions alone consume their authored AP; no implicit random-skill discount.
        systems.MoveUndoSystem?.Clear();
        systems.FactionState.ModifyAP(caster.team, -actionCost);
        State(caster).cooldown = definition.cooldown;
        systems.AICommander?.ObserveSpecialSkill(command);
        if (caster.team == Team.Player || systems.VisionGenerator != null
            && systems.VisionGenerator.IsInVisionXZ(Team.Player, caster.transform.position))
            ActionLogUI.Log($"<color=#D8BCFF>固有スキル</color> {KindNameJP.Get(caster)}：{SpecialSkillPresentation.Name(definition)}");
        CombatRuleTelemetry.Record("skill_use", caster, command.Target, chosen: true);
        Refresh(systems); reason = null; return true;
    }

    public static int GetAPCost(Status actor, int berserkerStage = 0)
    {
        var definition = Definition(actor); if (definition == null) return -1;
        if (definition.ability != UnitSpecialSkill.BerserkerRage) return definition.apCost;
        var costs = definition.stageAPCosts;
        int nextStage = Mathf.Clamp(State(actor).berserkerStage, 0, 5) + 1;
        return costs != null && costs.Length == 5 && nextStage <= 5 && (berserkerStage == 0 || berserkerStage == nextStage)
            ? costs[nextStage - 1] : -1;
    }
    public static bool MovementBlocked(Status actor) => actor != null && (State(actor).ritualActive || State(actor).holyKnightTurns > 0 || State(actor).holyKnightRecoveryLock || actor.RuntimeSystems?.MapCreate?.Environment?.IsInCaveTransit(actor) == true);
    public static bool AttackBlocked(Status actor) => actor != null && (State(actor).holyKnightTurns > 0 || State(actor).holyKnightRecoveryLock || State(actor).archerEscapeTurns > 0 || actor.RuntimeSystems?.MapCreate?.Environment?.IsInCaveTransit(actor) == true);
    public static bool TryGetSniperTarget(Status actor, out Status target)
    {
        target = null;
        if (actor == null || !State(actor).sniperActive || State(actor).ownerTurn < State(actor).sniperFireTurn) return false;
        target = FindActor(State(actor).sniperTargetId);
        return target != null && target.IsAlive && NeutralFactionSystem.AreHostile(actor, target);
    }
    public static bool CanNormalAttackTarget(Status attacker, Status target)
    {
        if (attacker == null || !attacker.IsAlive || target == null || !target.IsAlive || StatusEffectSystem.IsStunned(attacker)
            || BoardActionProfile.For(attacker)?.CanAttack == false || AttackBlocked(attacker) || !CrossbowCanFire(attacker)) return false;
        if (!NeutralFactionSystem.AreHostile(attacker, target) && !IsAssassinAllyTarget(attacker, target)) return false;
        var taunt = State(attacker);
        var challenger = FindActor(taunt.tauntedById);
        if (taunt.tauntTurns > 0 && challenger != null && challenger.IsAlive && target != challenger) return false;
        if (TryGetSniperTarget(attacker, out var locked)) return locked == target;
        if (IsHiddenFrom(attacker, target)) return false;
        return true;
    }
    public static float GetAttackMultiplier(Status attacker, Status target)
    {
        var d = Definition(attacker); if (d == null || !d.enabledForPlay) return 1;
        var s = State(attacker); float result = 1;
        if (d.ability == UnitSpecialSkill.ArcherStationary) result *= d.ArcherMultiplier(s.stationaryTurns);
        if (d.ability == UnitSpecialSkill.MagicSniperLock && TryGetSniperTarget(attacker, out var locked) && locked == target)
            result *= d.SniperMultiplier(GridHelper.ChebyshevDistance(attacker.GridPosition, target.GridPosition));
        if (d.ability == UnitSpecialSkill.NinjaSwap && d.HasCurve(d.ninjaChargeCurve)) result *= Mathf.Max(1, d.ninjaChargeCurve.Evaluate(Mathf.Clamp(s.ninjaCharge, 0, 5)));
        if (d.ability == UnitSpecialSkill.BerserkerRage && s.berserkerStage > 0 && d.HasCurve(d.berserkerAttackCurve)) result *= Mathf.Clamp(d.berserkerAttackCurve.Evaluate(s.berserkerStage), 1, SpecialSkillDefinition.BerserkerMaximum);
        if (d.ability == UnitSpecialSkill.NavigatorPull && d.HasCurve(d.navigatorAPDamageCurve))
        { var systems = attacker.RuntimeSystems; result *= Mathf.Clamp(d.navigatorAPDamageCurve.Evaluate(systems?.FactionState?.GetAP(attacker.team) ?? 0), 1, SpecialSkillDefinition.NavigatorMaximum); }
        if (d.ability == UnitSpecialSkill.NinjaSwap && target != null && !InVision(target, attacker)) result *= .4f;
        return result * R1AttackMultiplier(attacker, target);
    }
    public static float GetATKModifier(Status actor)
    {
        if (actor == null) return 1;
        float penalty = 0, stacked = 1;
        RuleActors();
        foreach (var knight in knightObservers)
            if (knight != null && knight.IsAlive && NeutralFactionSystem.AreHostile(knight, actor) && InVision(knight, actor))
            {
                var definition = Definition(knight);
                if (definition.knightPenaltyStackRule == AbilityStackRule.Multiply) stacked *= 1 - Mathf.Clamp01(definition.knightVisibleEnemyAtkPenalty);
                else penalty = Mathf.Max(penalty, Mathf.Clamp01(definition.knightVisibleEnemyAtkPenalty));
            }
        return stacked * (1 - penalty) * R1ATKMultiplier(actor);
    }
    public static float GetDEFModifier(Status actor)
    {
        var d = Definition(actor); return d != null && d.enabledForPlay && d.ability == UnitSpecialSkill.BerserkerRage && State(actor).berserkerStage > 0 && d.HasCurve(d.berserkerDefenceCurve)
            ? Mathf.Clamp01(d.berserkerDefenceCurve.Evaluate(State(actor).berserkerStage)) : 1;
    }
    public static int AttackAPAdjustment(Status actor, int ordinaryCost)
    {
        var d = Definition(actor);
        if (d == null || !d.enabledForPlay) return ordinaryCost;
        if (d.ability == UnitSpecialSkill.GuardianLifeLink && d.guardianAttackCostReset != AbilityResetScope.Unconfigured)
            return (int)System.Math.Min(int.MaxValue, (long)ordinaryCost + State(actor).guardianAttackCount);
        if (d.ability != UnitSpecialSkill.BerserkerRage) return ordinaryCost;
        int cost = d.HasCurve(d.berserkerAttackAPCurve) ? Mathf.Max(1, Mathf.RoundToInt(d.berserkerAttackAPCurve.Evaluate(actor.HPRatio))) : ordinaryCost;
        return (int)System.Math.Min(int.MaxValue, (long)cost + System.Math.Max(0L, (long)ordinaryCost - APSystem.BaseAttackCost)
            + (State(actor).berserkerStage > 0 ? 1 : 0));
    }

    public static void OnOwnerTurnStart(GameSystems systems, Team team, int round)
    {
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        BeginTurnSpecials(actors);
        try
        {
        foreach (var actor in actors)
        {
            if (actor == null || !actor.IsAlive || actor.team != team) continue;
            OnActorTurnStart(actor, systems, round);
            if (systems?.TurnGenerator?.IsGameOver == true) break;
        }
        }
        finally { EndTurnSpecials(); }
        Refresh(systems);
    }
    public static void OnActorTurnStart(Status actor, GameSystems systems, int round)
    {
            if (actor == null || !actor.IsAlive) return;
            var s = State(actor); if (s.lastRound == round) return;
            int previousOwnerTurn = s.ownerTurn;
            s.lastRound = round; s.ownerTurn++;
            OnCrossbowOwnerTurnStart(actor, previousOwnerTurn);
            R1ActorTurnStart(actor, systems, round);
            s.holyKnightRecoveryLock = false;
            if (s.cooldown > 0) s.cooldown--;
            if (s.ownerTurn > 1) s.stationaryTurns = s.moved ? 0 : Mathf.Min(100000, s.stationaryTurns + 1);
            if (s.ownerTurn > 1) s.ninjaCharge = s.attacked ? 0 : Mathf.Min(5, s.ninjaCharge + 1);
            s.moved = s.attacked = false;
            if (s.visionZeroTurns > 0) s.visionZeroTurns--;
            if (s.scoutVisionPenaltyTurns > 0) s.scoutVisionPenaltyTurns--;
            if (s.damageNullTurns > 0 && --s.damageNullTurns == 0) s.damageNullUnused = false;
            if (s.samuraiMaxTurns > 0 && --s.samuraiMaxTurns == 0) s.samuraiGauge = 0;
            TickActor(actor, systems, round);
    }
    public static void OnMoved(Status actor, Vector3 from, Vector3 to, GameSystems systems)
    {
        if (actor == null || GridHelper.ToGridXZ(from) == GridHelper.ToGridXZ(to)) return;
        BoardActorAnimation.NotifyMove(actor, from);
        var s = State(actor); s.moved = true; s.stationaryTurns = 0;
        actor.HasMovedThisTurn = true;
        long before = HpRevision;
        ProcessMovementHazards(actor, from, to, systems);
        systems?.MapCreate?.Environment?.OnMoved(actor, from, to, systems);
        if (before != HpRevision) systems?.MoveUndoSystem?.Clear();
    }
    public static void OnAttackLanded(Status attacker, bool normalAttack = true)
    {
        if (attacker == null) return;
        var s = State(attacker); s.attacked = true; s.ninjaCharge = 0;
        var d = Definition(attacker);
        if (normalAttack && d != null && d.enabledForPlay && d.ability == UnitSpecialSkill.SamuraiCounter && d.samuraiGaugeMax > 0 && d.samuraiGaugeGain >= 0)
        { bool wasFull = s.samuraiGauge >= d.samuraiGaugeMax; s.samuraiGauge = (int)System.Math.Min(d.samuraiGaugeMax, (long)s.samuraiGauge + d.samuraiGaugeGain); if (!wasFull && s.samuraiGauge >= d.samuraiGaugeMax) s.samuraiMaxTurns = 3; }
        if (normalAttack && d != null && d.ability == UnitSpecialSkill.BerserkerRage) s.berserkerStage = 0;
        if (normalAttack && d != null && d.ability == UnitSpecialSkill.MagicSniperLock && s.ownerTurn >= s.sniperFireTurn) s.sniperTargetId = null;
    }
    public static void ClearTemporaryEffects(Status actor)
    { var s = State(actor); s.nextDamageDouble = false; s.visionZeroTurns = s.scoutVisionPenaltyTurns = s.damageNullTurns = 0; s.damageNullUnused = false; s.berserkerStage = 0; s.archerEscapeTurns = 0; s.archerSnapshotIds.Clear(); s.tauntTurns = 0; s.tauntedById = null; s.assassinBoostTurns = s.assassinBoostStacks = 0; }
    public static int HazardDamageAt(Status actor, Vector3 destination)
    { var all = new List<Status>(); CombatRegistry.Collect(all); return HazardDamageAt(actor, destination, all); }
}
