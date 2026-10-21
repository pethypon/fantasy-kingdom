using System.Collections.Generic;
using UnityEngine;

/// <summary>Authoritative special actions. AP and cooldown are committed by TryExecute only.</summary>
public static partial class SpecialSkillRuntime
{
    /// <summary>Self/area actions have no manually selected recipient. Map-wide single-target actions still do.</summary>
    public static bool UsesAutomaticTarget(UnitSpecialSkill skill) => skill == UnitSpecialSkill.KnightBreakthrough
        || skill == UnitSpecialSkill.ArcherStationary || skill == UnitSpecialSkill.ScoutVisionGuard
        || skill == UnitSpecialSkill.PriestPurification || skill == UnitSpecialSkill.CrossbowStakes
        || skill == UnitSpecialSkill.HolyKnightSanctuary || skill == UnitSpecialSkill.BerserkerRage;

    /// <summary>Player command guard. AI must continue to use its observed board, not this world query.</summary>
    public static bool HasAutomaticSkillTarget(Status caster)
    {
        var definition = Definition(caster);
        if (caster == null || !caster.IsAlive || definition == null || !UsesAutomaticTarget(definition.ability)) return false;
        // Movement, preparation and self buffs remain useful before an enemy has been found.
        if (definition.ability == UnitSpecialSkill.KnightBreakthrough || definition.ability == UnitSpecialSkill.CrossbowStakes
            || definition.ability == UnitSpecialSkill.BerserkerRage) return true;
        var iterator = CombatRegistry.Enumerate();
        try
        {
            while (iterator.MoveNext())
            {
                var target = iterator.Current;
                if (target == null || !target.IsAlive) continue;
                switch (definition.ability)
                {
                    case UnitSpecialSkill.ArcherStationary:
                        if (NeutralFactionSystem.AreHostile(caster, target) && Distance3D(caster.transform.position, target.transform.position) <= 2) return true;
                        break;
                    case UnitSpecialSkill.ScoutVisionGuard:
                        if (target.team == caster.team && InVision(caster, target)) return true;
                        break;
                    case UnitSpecialSkill.PriestPurification:
                        if (target != caster && GridHelper.ChebyshevDistance(caster.GridPosition, target.GridPosition) <= 2) return true;
                        break;
                    case UnitSpecialSkill.HolyKnightSanctuary:
                        if (!AllyTarget(caster, target) || !OrdinaryUnit(target) || definition.holyKnightArea == null) break;
                        var delta = target.GridPosition - caster.GridPosition;
                        foreach (var offset in definition.holyKnightArea)
                            if (delta.x == offset.x && delta.z == offset.y) return true;
                        break;
                }
            }
        }
        finally { iterator.Dispose(); }
        return false;
    }

    public static bool IsLegalTarget(Status caster, Status target, out string reason)
    {
        reason = null;
        var definition = Definition(caster);
        if (caster == null || !caster.IsAlive || definition == null)
            return Reject(out reason, "固有能力を使用できる駒がありません");
        switch (definition.ability)
        {
            case UnitSpecialSkill.KnightBreakthrough:
                if (target != null && target != caster) return Reject(out reason, "前方突破は自分から発動します");
                return CanAdvanceKnight(caster, out reason);
            case UnitSpecialSkill.SamuraiCounter:
                return EnemyTarget(caster, target, true, out reason);
            case UnitSpecialSkill.HeavyWarriorBreak:
            case UnitSpecialSkill.DragonKnightPierce:
                return EnemyTarget(caster, target, false, out reason)
                    && (GridHelper.ChebyshevDistance(caster.GridPosition, target.GridPosition) <= 1 || Reject(out reason, "周囲1マス以内の敵を選択してください"))
                    && (caster.RuntimeSystems?.MapCreate?.CanAttackAcrossTerrain(caster, target.transform.position) != false || Reject(out reason, "地形に遮られています"))
                    && (definition.ability != UnitSpecialSkill.DragonKnightPierce || State(caster).dragonKnightGauge >= 2
                        || Reject(out reason, "竜騎士のゲージが2必要です"));
            case UnitSpecialSkill.ArcherStationary:
                return (target == null || target == caster) && State(caster).archerEscapeTurns <= 0
                    || Reject(out reason, "自分から発動する能力です（発動中は再使用できません）");
            case UnitSpecialSkill.MagicRitual:
                return EnemyTarget(caster, target, false, out reason)
                    && (!State(caster).ritualActive || Reject(out reason, "すでに儀式中です"));
            case UnitSpecialSkill.AssassinVisionSeal:
                return EnemyTarget(caster, target, true, out reason);
            case UnitSpecialSkill.CrossbowStakes:
                if (target != null && target != caster) return Reject(out reason, "杭弾は自分の弾倉へ装填します");
                return definition.enabledForPlay && definition.CrossbowConfigurationError == null
                    || Reject(out reason, definition.CrossbowConfigurationError ?? "弾倉を使用できません");
            case UnitSpecialSkill.MagicSniperLock:
                return EnemyTarget(caster, target, false, out reason)
                    && (!State(caster).sniperActive || Reject(out reason, "すでに標的をロックしています"));
            case UnitSpecialSkill.GuardianLifeLink:
                if (!AllyTarget(caster, target) || target == caster || target.type > Type.Wall)
                    return Reject(out reason, "自分以外の味方の駒・建物を選択してください");
                return string.IsNullOrEmpty(State(caster).guardianPartnerId)
                    && string.IsNullOrEmpty(State(target).guardianPartnerId)
                    || Reject(out reason, "すでに生命接続中の駒です");
            case UnitSpecialSkill.LifeBomb:
                if (!AllyTarget(caster, target) || !OrdinaryUnit(target))
                    return Reject(out reason, "味方の駒を選択してください");
                return string.IsNullOrEmpty(State(target).bombCasterId)
                    || Reject(out reason, "すでに爆弾を所持しています");
            case UnitSpecialSkill.WhiteMageHeal:
                if (!AllyTarget(caster, target) || !OrdinaryUnit(target) || !InVision(caster, target))
                    return Reject(out reason, "自分の視界内にいる味方の駒を選択してください");
                return target.HP < target.MaxHP || Reject(out reason, "HPが満タンです");
            case UnitSpecialSkill.NinjaSwap:
                if (!AllyTarget(caster, target) || target == caster || !OrdinaryUnit(target))
                    return Reject(out reason, "自分以外の味方の駒を選択してください");
                return true;
            case UnitSpecialSkill.NavigatorPull:
                if (!EnemyTarget(caster, target, true, out reason)) return false;
                return CanPull(caster, target, out _, out reason);
            case UnitSpecialSkill.ScoutVisionGuard:
            case UnitSpecialSkill.PriestPurification:
            case UnitSpecialSkill.HolyKnightSanctuary:
                if (target != null && target != caster) return Reject(out reason, "自分から発動する能力です");
                return definition.ability != UnitSpecialSkill.HolyKnightSanctuary || State(caster).holyKnightTurns <= 0
                    || Reject(out reason, "すでに回復中です");
            case UnitSpecialSkill.BerserkerRage:
                if (target != null && target != caster) return Reject(out reason, "自分から発動する能力です");
                return State(caster).berserkerStage < 5 || Reject(out reason, "すでに最大の5段階まで強化しています");
            default:
                return Reject(out reason, "この能力は自動で発動します");
        }
    }

    public static bool IsLegalSacrifice(Status caster, Status sacrifice)
        => caster != null && AllyTarget(caster, sacrifice) && OrdinaryUnit(sacrifice)
            && GridHelper.ChebyshevDistance(caster.transform.position, sacrifice.transform.position) <= 3;

    public static bool IsLegalSacrifice(Status caster, Status sacrifice, out string reason)
    {
        reason = null;
        return IsLegalSacrifice(caster, sacrifice) || Reject(out reason, "周囲3マス以内の味方の駒を供儀対象にしてください");
    }

    public static int GetSacrificeAmount(Status sacrifice)
        => sacrifice == null ? 0 : Mathf.Min(Mathf.Max(0, sacrifice.HP), FractionOfMaximum(sacrifice, SpecialSkillDefinition.RitualSacrifice));

    internal static bool ExecuteAuthorized(SpecialSkillCommand command, GameSystems systems, int round, out string reason)
    {
        reason = null;
        var caster = command.Caster;
        var target = command.Target;
        var definition = Definition(caster);
        var state = State(caster);
        switch (definition.ability)
        {
            case UnitSpecialSkill.HeavyWarriorBreak:
            case UnitSpecialSkill.DragonKnightPierce:
                int power = PreviewDedicatedSkillDamage(caster, target);
                var damageKind = definition.ability == UnitSpecialSkill.DragonKnightPierce ? HpChangeKind.BarrierBypassDamage : HpChangeKind.NormalDamage;
                if (definition.ability == UnitSpecialSkill.DragonKnightPierce) state.dragonKnightGauge = 0;
                ApplyAbilityDamage(caster, target, power, damageKind);
                return true;
            case UnitSpecialSkill.KnightBreakthrough:
                return AdvanceKnight(caster, systems, out reason);
            case UnitSpecialSkill.MagicRitual:
                if (command.Sacrifices == null || command.Sacrifices.Count == 0
                    || command.Sacrifices.Count > definition.sacrificeLimit)
                    return Reject(out reason, "供儀対象を設定人数内で選択してください");
                for (int i = 0; i < command.Sacrifices.Count; i++)
                {
                    if (!IsLegalSacrifice(caster, command.Sacrifices[i]))
                        return Reject(out reason, "供儀に使用できない対象が含まれます");
                    for (int j = 0; j < i; j++)
                        if (command.Sacrifices[i] == command.Sacrifices[j])
                            return Reject(out reason, "同じ駒を複数回供儀には使用できません");
                }
                state.ritualTargetId = target.ReflectionLifeId;
                state.ritualFireTurn = state.ownerTurn + 1;
                state.storedSacrificeDamage = 0;
                long total = 0;
                for (int i = 0; i < command.Sacrifices.Count; i++)
                {
                    var sacrifice = command.Sacrifices[i];
                    total += ApplyHpChange(caster, sacrifice, FractionOfMaximum(sacrifice, SpecialSkillDefinition.RitualSacrifice), HpChangeKind.DirectHpReduction, cause: HpChangeCause.Sacrifice);
                }
                state.storedSacrificeDamage = (int)System.Math.Min(int.MaxValue, total);
                // Death during one's own sacrifice does not refund or transfer the ritual.
                if (!caster.IsAlive) { state.ritualTargetId = null; state.storedSacrificeDamage = 0; }
                break;
            case UnitSpecialSkill.GuardianLifeLink:
                state.guardianPartnerId = target.ReflectionLifeId;
                State(target).guardianPartnerId = caster.ReflectionLifeId;
                break;
            case UnitSpecialSkill.CrossbowStakes:
                if (!LoadCrossbowStake(caster, systems, round, out reason)) return false;
                break;
            case UnitSpecialSkill.MagicSniperLock:
                state.sniperTargetId = target.ReflectionLifeId;
                state.sniperFireTurn = state.ownerTurn + 1;
                break;
            case UnitSpecialSkill.LifeBomb:
                var bomb = State(target);
                bomb.bombCasterId = caster.ReflectionLifeId;
                bomb.bombDueRound = round + SpecialSkillDefinition.BombDelay;
                bomb.bombRadius = definition.bombExplosionRadius;
                bomb.bombMask = definition.bombFactionMask;
                bomb.bombDeathPolicy = definition.bombOnHolderDeathPolicy;
                bomb.bombCasterTeam = caster.team;
                break;
            case UnitSpecialSkill.WhiteMageHeal:
                target.ApplyHeal(FractionOfMaximum(target, SpecialSkillDefinition.WhiteMageHealFraction));
                break;
            case UnitSpecialSkill.HolyKnightSanctuary:
                state.holyKnightTurns = 3;
                break;
            case UnitSpecialSkill.NinjaSwap:
                return TryForcedSwap(caster, target, systems, out reason);
            case UnitSpecialSkill.BerserkerRage:
                int nextStage = Mathf.Clamp(state.berserkerStage, 0, 5) + 1;
                if (nextStage > 5 || command.BerserkerStage != 0 && command.BerserkerStage != nextStage)
                    return Reject(out reason, "次の1段階だけ強化できます");
                state.berserkerStage = nextStage;
                break;
            case UnitSpecialSkill.NavigatorPull:
                if (!CanPull(caster, target, out var destination, out reason)) return false;
                return TryForcedMove(target, destination, systems, out reason);
            default:
                return Reject(out reason, "この能力の実行方法がありません");
        }
        return true;
    }

    static void AddStake(Status caster, Status target)
    {
        var state = State(caster);
        PruneStakes(caster);
        string id = target.ReflectionLifeId;
        CombatRuleTelemetry.Record("crossbow_stake_attach", caster, target);
        if (state.stakeAId == id || state.stakeBId == id) return;
        if (string.IsNullOrEmpty(state.stakeAId))
        { state.stakeAId = id; state.stakeAAppliedTurn = state.ownerTurn; return; }
        if (!string.IsNullOrEmpty(state.stakeBId))
        { state.stakeAId = state.stakeBId; state.stakeAAppliedTurn = state.stakeBAppliedTurn; }
        state.stakeBId = id;
        state.stakeBAppliedTurn = state.ownerTurn;
    }

    static bool Reject(out string reason, string message) { reason = message; return false; }
    static bool OrdinaryUnit(Status unit) => unit != null && unit.type == Type.Unit
        && unit.kind != Kind.Crystal && unit.kind != Kind.SubCrystal;
    static bool AllyTarget(Status caster, Status target) => caster != null && target != null
        && target.IsAlive && caster.team == target.team;
    static bool EnemyTarget(Status caster, Status target, bool unitOnly, out string reason)
    {
        reason = null;
        if (target == null || !target.IsAlive || !NeutralFactionSystem.AreHostile(caster, target)
            || target.type > Type.Wall || unitOnly && !OrdinaryUnit(target))
            return Reject(out reason, unitOnly ? "敵の駒を選択してください" : "敵を選択してください");
        return InVision(caster, target) || Reject(out reason, "自分の視界内の敵を選択してください");
    }

    internal static int FractionOfMaximum(Status unit, float fraction)
        => unit == null ? 0 : (int)System.Math.Min(int.MaxValue,
            System.Math.Floor(System.Math.Max(0, unit.MaxHP) * (double)fraction));

    static Vector3 FrontPosition(Status unit) => unit.transform.position + new Vector3(0, 0, MovePatterns.DirZ(unit.direction));

    static bool CanPull(Status caster, Status target, out Vector3 destination, out string reason)
    {
        destination = FrontPosition(caster);
        reason = null;
        var systems = GetActorSystems(caster);
        var map = systems?.MapCreate ?? systems?.MoveGenerator?.mapcreate;
        if (map == null || !map.TryGetHeight(Mathf.RoundToInt(destination.x), Mathf.RoundToInt(destination.z), out float height))
            return Reject(out reason, "正面に移動できる地形がありません");
        destination.y = height;
        if (GridHelper.ToGridXZ(target.transform.position) == GridHelper.ToGridXZ(destination))
            return Reject(out reason, "すでに正面にいます");
        var actors = new List<Status>();
        CombatRegistry.Collect(actors);
        if (ActorAt(actors, destination) != null || !map.CanTraverse(target.transform.position, destination))
            return Reject(out reason, "正面に障害物があるか、引き寄せる経路が通れません");
        return true;
    }

    static bool CanAdvanceKnight(Status caster, out string reason)
    {
        var systems = GetActorSystems(caster);
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        return TryKnightStep(caster, systems, actors, null, null, out reason);
    }

    static bool AdvanceKnight(Status caster, GameSystems systems, out string reason)
    {
        reason = null;
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        var chain = new List<Status>();
        var destinations = new List<Vector3>();
        bool moved = false;
        BeginMovementBatch();
        try
        {
            for (int step = 0; step < 3 && caster.IsAlive && systems?.TurnGenerator?.IsGameOver != true; step++)
            {
                chain.Clear(); destinations.Clear();
                if (!TryKnightStep(caster, systems, actors, chain, destinations, out var stopReason))
                { if (!moved) reason = stopReason; break; }
                // Positions commit atomically before any movement-triggered damage can destroy an actor.
                if (!TryForcedBatchMove(chain, destinations, systems, out var moveReason))
                { if (!moved) reason = moveReason; break; }
                moved = true;
            }
        }
        finally { EndMovementBatch(); }
        if (moved) reason = null;
        return moved;
    }

    static bool TryKnightStep(Status caster, GameSystems systems, List<Status> actors,
        List<Status> chain, List<Vector3> destinations, out string reason)
    {
        reason = null;
        var map = systems?.MapCreate ?? systems?.MoveGenerator?.mapcreate;
        if (map == null) return Reject(out reason, "マップ情報がありません");
        var next = FrontPosition(caster);
        var moving = caster;
        // Each successful iteration advances one cell, so the finite map bounds terminate a chain.
        for (int count = 0; count <= actors.Count; count++)
        {
            if (!map.TryGetHeight(Mathf.RoundToInt(next.x), Mathf.RoundToInt(next.z), out float height))
                return Reject(out reason, "前方に突破できる地形がありません");
            next.y = height;
            if (!map.CanTraverse(moving.transform.position, next))
                return Reject(out reason, "前方に進入できません");
            chain?.Add(moving); destinations?.Add(next);
            var blocker = ActorAt(actors, next);
            if (blocker == null) return true;
            if (!OrdinaryUnit(blocker) || !NeutralFactionSystem.AreHostile(caster, blocker))
                return Reject(out reason, "味方・建物・クリスタルが前方を塞いでいます");
            moving = blocker;
            next += new Vector3(0, 0, MovePatterns.DirZ(caster.direction));
        }
        return Reject(out reason, "前方に空きマスがありません");
    }

    static Status ActorAt(IReadOnlyList<Status> actors, Vector3 position)
    {
        var cell = GridHelper.ToGridXZ(position);
        for (int i = 0; i < actors.Count; i++)
            if (actors[i] != null && actors[i].IsAlive && actors[i].GridPosition == cell) return actors[i];
        return null;
    }

    static GameSystems GetActorSystems(Status unit)
        => unit != null ? unit.RuntimeSystems : null;
}
