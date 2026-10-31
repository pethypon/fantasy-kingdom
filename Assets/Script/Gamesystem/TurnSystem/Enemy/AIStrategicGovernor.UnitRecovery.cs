using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIStrategicGovernor
{
    // Owner/lifetime: governor/battle. Only active own-unit recovery records survive generations;
    // plain values and stable life IDs, at most 128. Per-generation caches release Unity references.
    readonly Dictionary<string, AIUnitPreservationRecord> unitRecovery = new Dictionary<string, AIUnitPreservationRecord>(StringComparer.Ordinal);
    readonly Dictionary<Status, AIUnitPreservationAssessment> preservationAssessments = new Dictionary<Status, AIUnitPreservationAssessment>();
    readonly HashSet<string> preservationOwnIds = new HashSet<string>(StringComparer.Ordinal);
    readonly List<string> staleRecoveryIds = new List<string>();
    readonly Dictionary<(Status actor, Vector3Int cell, Status eliminated), float> repeatedLossCache
        = new Dictionary<(Status, Vector3Int, Status), float>();
    readonly Dictionary<Vector3Int, string> retreatFirstHopReservations = new Dictionary<Vector3Int, string>();
    readonly Dictionary<Status, AIRetreatRoutePlan> retreatRoutes = new Dictionary<Status, AIRetreatRoutePlan>();
    AIBoardState preservationBoard;
    Team preservationOwner;
    bool hasPreservationOwner;
    int preservationGeneration = -1, preservationConfigRevision = -1;
    public int TrackedRecoveryUnits => unitRecovery.Count;

    /// <summary>Also called for an empty army so destroyed actors cannot retain recovery records.</summary>
    public void PrunePreservationState(AIBoardState board)
    {
        if (board != null) PreparePreservation(board);
    }

    void PreparePreservation(AIBoardState board)
    {
        // A developer-controlled governor can be rebound. A life ID alone cannot transfer
        // an earlier faction's recovery decision to another faction after capture/restore.
        if (!hasPreservationOwner || preservationOwner != board.ActorTeam)
        {
            ReleasePreservationState();
            preservationOwner = board.ActorTeam; hasPreservationOwner = true;
        }
        var config = AIUnitPreservationConfig.Active;
        if (preservationBoard == board && preservationGeneration == board.Generation && preservationConfigRevision == config.Revision) return;
        preservationBoard = board; preservationGeneration = board.Generation; preservationConfigRevision = config.Revision;
        preservationAssessments.Clear(); preservationOwnIds.Clear(); staleRecoveryIds.Clear(); repeatedLossCache.Clear();
        retreatFirstHopReservations.Clear(); retreatRoutes.Clear();
        foreach (var actor in board.AliveEnemyUnits)
            if (IsRecoveryActor(actor, board.ActorTeam))
                preservationOwnIds.Add(actor.ReflectionLifeId);
        foreach (var pair in unitRecovery)
            if (!preservationOwnIds.Contains(pair.Key) || !AIUnitPreservationState.IsValidRecord(pair.Value)
                || pair.Value.ActorLifeId != pair.Key || pair.Value.LastTurn > board.TurnCount) staleRecoveryIds.Add(pair.Key);
        foreach (var id in staleRecoveryIds) unitRecovery.Remove(id);
        if (!config.Enabled) unitRecovery.Clear();
    }

    static bool IsRecoveryActor(Status actor, Team owner)
        => actor != null && actor.IsAlive && actor.team == owner && actor.type == Type.Unit
            && actor.gameObject.activeInHierarchy
            && !MatchObjectiveRules.IsCommander(actor) && AIUnitPreservationState.IsValidPoint(actor.transform.position);

    public AIUnitPreservationAssessment AssessPreservation(Status actor, AIBoardState board)
    {
        if (board == null || !IsRecoveryActor(actor, board.ActorTeam) || !board.AliveEnemyUnits.Contains(actor)) return default;
        Evaluate(board); PreparePreservation(board);
        if (preservationAssessments.TryGetValue(actor, out var cached)) return cached;
        var config = AIUnitPreservationConfig.Active;
        if (!config.Enabled) return default;
        Vector3 origin = actor.transform.position;
        float incoming = IncomingDamage(board, actor, origin, null);
        float repeated = RepeatedObservedLoss(board, actor, origin, null, config.Waves);
        float ratio = actor.HP / Mathf.Max(1f, actor.MaxHP);
        int allies = board.CountAlliesNear(origin, actor, 3);
        bool healing = NaturalRecoverySystem.HealAmount(actor.MaxHP) > 0 || board.KnownEnvironmentHeal(actor, origin) > 0
            || board.HasHealerInRange(origin, 4);
        unitRecovery.TryGetValue(actor.ReflectionLifeId, out var prior);
        bool upkeepRisk = Economy.UpkeepPaymentFailed && Economy.ExpectedUnitHPDamage > 0;
        var recoveryForecast = AIUnitRecoveryForecast.Project(actor, BarrierSystem.SafeTurnThreshold, upkeepRisk);
        bool barrierReady = !BarrierSystem.UsesBarrier(actor) || actor.MaxBarrier <= 0
            || actor.Barrier >= actor.MaxBarrier * config.BarrierRejoinRatio;
        bool recovered = ratio >= config.RejoinRatio && barrierReady;
        int lastProgress = prior != null && prior.HasRecoveryBaseline ? prior.LastProgressTurn : board.TurnCount;
        if (prior != null && (actor.HP > prior.LastHP || actor.Barrier > prior.LastBarrier)) lastProgress = board.TurnCount;
        bool ongoing = prior != null && prior.Phase != AIUnitRecoveryPhase.Ready && !recovered && healing
            && !upkeepRisk && board.TurnCount - lastProgress < config.RecoveryIdleTurns;
        bool lethal = incoming >= actor.HP;
        bool attrition = ratio <= config.RejoinRatio && repeated >= actor.HP;
        bool wounded = ratio <= config.WoundedRatio && incoming > 0 && healing;
        bool considerEscape = lethal || attrition || wounded || ongoing && incoming > 0;
        bool escape = false; int options = 0; Vector3 refuge = origin;
        float bestRisk = incoming;
        AIRetreatRoutePlan route = null;
        if (considerEscape && !StatusEffectSystem.IsStunned(actor) && !StatusEffectSystem.IsMovementBlocked(actor)
            && !SpecialSkillRuntime.MovementBlocked(actor)
            && BoardActionProfile.For(actor)?.CanMove != false)
        {
            var context = new GovernorRetreatContext(this, actor, board, prior, incoming, config.RefugeRiskRatio, upkeepRisk);
            route = AIRetreatRoutePlanner.Find(context, config.RouteDepth, config.RouteNodes, config.RouteFutureTurns);
            retreatRoutes[actor] = route;
            if (route.Found)
            {
                escape = true; refuge = route.FirstHop; options = route.FirstHopOptions;
                bestRisk = IncomingDamage(board, actor, refuge, null);
                retreatFirstHopReservations[GridHelper.ToGridXZ(refuge)] = actor.ReflectionLifeId;
            }
        }
        var safety = lethal ? AIUnitSafetyLevel.Emergency
            : escape && (attrition || wounded || ongoing && incoming > 0) ? AIUnitSafetyLevel.WithdrawalRecommended
            : AIUnitSafetyLevel.HoldPositionOrFight;
        var phase = safety != AIUnitSafetyLevel.HoldPositionOrFight ? AIUnitRecoveryPhase.Withdrawing
            : ongoing ? AIUnitRecoveryPhase.Recovering : AIUnitRecoveryPhase.Ready;
        string reason = lethal ? (escape ? "observed_lethal_escape" : "observed_lethal_no_escape")
            : safety == AIUnitSafetyLevel.WithdrawalRecommended ? (attrition ? "observed_repeated_exchange_risk" : "wounded_unit_safe_recovery")
            : phase == AIUnitRecoveryPhase.Recovering ? (actor.HP == actor.MaxHP && !barrierReady
                ? "recovering_formal_full_hp_barrier_clock" : "recovering_without_observed_exposure")
            : upkeepRisk ? "recovery_supply_unpaid_risk" : "fight_or_progress_remains_reasonable";
        if (phase != AIUnitRecoveryPhase.Ready)
        {
            if (prior != null || unitRecovery.Count < config.Limit)
                unitRecovery[actor.ReflectionLifeId] = new AIUnitPreservationRecord { ActorLifeId = actor.ReflectionLifeId,
                    Phase = phase, LastTurn = board.TurnCount, Refuge = escape ? route.Destination : origin,
                    HasRecoveryBaseline = true, LastProgressTurn = lastProgress, LastHP = actor.HP, LastBarrier = actor.Barrier };
        }
        else unitRecovery.Remove(actor.ReflectionLifeId);
        var result = new AIUnitPreservationAssessment(safety, phase, incoming, repeated, bestRisk, escape, healing, options, allies, refuge, reason,
            route, recoveryForecast);
        preservationAssessments[actor] = result; return result;
    }

    /// <summary>Generation-bound diagnostics only; the executor still accepts ordinary current actions.</summary>
    internal bool TryGetRetreatRoute(Status actor, AIBoardState board, out AIRetreatRoutePlan route)
    {
        route = null;
        if (board == null || !IsRecoveryActor(actor, board.ActorTeam)) return false;
        AssessPreservation(actor, board);
        return retreatRoutes.TryGetValue(actor, out route) && route.Found;
    }

    sealed class GovernorRetreatContext : AIRetreatRouteContext
    {
        readonly AIStrategicGovernor governor;
        readonly Status actor;
        readonly AIBoardState board;
        readonly bool unpaid;
        readonly HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();
        internal GovernorRetreatContext(AIStrategicGovernor governor, Status actor, AIBoardState board,
            AIUnitPreservationRecord previous, float incoming, float riskRatio, bool unpaid)
        {
            this.governor = governor; this.actor = actor; this.board = board; this.unpaid = unpaid;
            Origin = actor.transform.position; RetainedRefuge = previous?.Refuge ?? Origin;
            HP = actor.HP; CurrentAP = board.EnemyAP; NextAP = board.OwnAPData?.Maximum ?? 0;
            // When the nation's paid population bonus is uncertain, use only the
            // already-authored base reset/penalty rather than promising citizen growth.
            if ((unpaid || governor.Economy.BreadPaymentFailed) && board.OwnAPData != null)
                NextAP = Mathf.Min(NextAP, new FactionState.APData { Reset = board.OwnAPData.Reset, Minus = board.OwnAPData.Minus }.Maximum);
            Fatigue = Mathf.Max(0, actor.Fatigue); HasMoved = actor.HasMovedThisTurn;
            ExtraFatigue = GameConstants.GetExtraFatiguePerAction(actor.kind);
            CurrentRisk = incoming; RefugeRiskLimit = actor.HP * riskRatio;
            InitialProtection = AIObservedDamageBudget.Capture(actor);
            board.CollectRetreatOccupancy(actor, occupied);
        }
        internal override void CollectSteps(Vector3 from, List<Vector3> result)
            => board.CollectKnownRetreatSteps(actor, from, occupied, result);
        internal override int MoveCost(Vector3 from, Vector3 to, int fatigue, bool moved, bool freeAvailable)
            => board.RetreatMoveQuote(actor, from, to, fatigue, moved, freeAvailable);
        internal override bool ReservedFirstHop(Vector3 destination)
            => governor.retreatFirstHopReservations.TryGetValue(GridHelper.ToGridXZ(destination), out string owner)
                && owner != actor.ReflectionLifeId;
        internal override float EntryLoss(Vector3 destination, ref AIObservedDamageBudget protection)
            => AIActionEvaluator.EstimateObservedHitLoss(actor, board, board.KnownEnvironmentDamage(actor, destination, true),
                HpChangeKind.IgnoreDefenceDamage, ref protection);
        internal override float Exposure(Vector3 destination, ref AIObservedDamageBudget protection)
        {
            float loss = governor.ObservedDamageWave(board, actor, destination, null, ref protection, null, false);
            if (unpaid) loss += AIActionEvaluator.EstimateObservedHitLoss(actor, board,
                R2EconomyRules.UnpaidDamage(actor.HP, actor.MaxHP), HpChangeKind.DirectHpReduction, ref protection);
            return loss;
        }
        internal override bool CanRecoverAt(Vector3 position, float observedHpRisk)
            => observedHpRisk <= .001f && NaturalRecoverySystem.HealAmount(actor.MaxHP) > 0 || board.KnownEnvironmentHeal(actor, position) > 0
                || board.HasHealerInRange(position, 4);
    }

    float RepeatedObservedLoss(AIBoardState board, Status actor, Vector3 position, Status eliminated, int waves)
    {
        var key = (actor, GridHelper.ToGridXZ(position), eliminated);
        if (repeatedLossCache.TryGetValue(key, out float cached)) return cached;
        var budget = AIObservedDamageBudget.Capture(actor); double total = 0;
        for (int i = 0; i < Mathf.Clamp(waves, 1, 2); i++) total += ObservedDamageWave(board, actor, position, eliminated, ref budget);
        return repeatedLossCache[key] = (float)Math.Min(int.MaxValue, total);
    }

    /// <summary>Additional priority inside the existing governor. Returns 4 when ordinary scoring should decide.</summary>
    public int PreservationPriority(AIAction action, AIBoardState board)
    {
        if (action == null || action.Unit == null || board == null || !action.Unit.IsAlive || action.Unit.team != board.ActorTeam
            || action.APCost < 0 || action.APCost > board.EnemyAP
            || StatusEffectSystem.IsStunned(action.Unit)) return 4;
        var assessment = AssessPreservation(action.Unit, board);
        if (assessment.Safety != AIUnitSafetyLevel.HoldPositionOrFight && IsMovement(action)
            && !StatusEffectSystem.IsMovementBlocked(action.Unit) && !SpecialSkillRuntime.MovementBlocked(action.Unit)
            && board.GetValidMoves(action.Unit).Contains(action.TargetPos)
            && assessment.HasLegalSaferMove && GridHelper.MatchXZ(action.TargetPos, GridHelper.ToGridXZ(assessment.Refuge))) return 1;
        // A useful observed kill may remove the very threat that motivated withdrawal.
        if (action.ActionType == AIActionType.Attack && action.TargetUnit != null && board.IsSpecialActorObserved(action.TargetUnit)
            && board.GetAttackTargets(action.Unit).Contains(action.TargetUnit)
            && AIActionEvaluator.CanFinishSpecialTarget(action.Unit, action.TargetUnit, board))
        {
            foreach (var patient in board.AliveEnemyUnits)
            {
                if (!IsRecoveryActor(patient, board.ActorTeam)) continue;
                var threatened = AssessPreservation(patient, board);
                if (threatened.Safety == AIUnitSafetyLevel.HoldPositionOrFight) continue;
                if (IncomingDamage(board, patient, patient.transform.position, action.TargetUnit) + .001f < threatened.IncomingCurrent
                    || RepeatedObservedLoss(board, patient, patient.transform.position, action.TargetUnit, AIUnitPreservationConfig.Active.Waves)
                        + .001f < threatened.RepeatedObservedLoss) return 1;
            }
        }
        if (action.ActionType == AIActionType.SkillUse && IsRecoveryActor(action.TargetUnit, board.ActorTeam)
            && board.AliveEnemyUnits.Contains(action.TargetUnit))
        {
            var patient = action.TargetUnit; var injured = AssessPreservation(patient, board);
            int healing = action.Skill != null && action.Unit.AssignedSkillId == action.Skill.Id
                && action.Unit.SkillCooldown == 0 && AttackPatterns.CanUseSkills(action.Unit)
                && !StatusEffectSystem.HasDebuff(action.Unit, StatusEffectType.Seal)
                && board.GetSkillTargets(action.Unit, action.Skill).Contains(patient) ? action.Skill.FixedHeal : 0;
            if (action.SpecialCommand != null && SpecialSkillRuntime.Definition(action.Unit)?.ability == UnitSpecialSkill.WhiteMageHeal
                && AIActionEvaluator.CalcSpecialSkillScore(action, board) > 0)
                healing = SpecialSkillRuntime.FractionOfMaximum(patient, SpecialSkillDefinition.WhiteMageHealFraction);
            bool usefulSquadSupport = healing > 0 && Mode != StrategicMode.EconomicRecovery
                && board.Operations?.SupportsSquadPatient(action.Unit, patient) == true
                && patient.MaxHP - patient.HP >= Mathf.Max(1, healing / 2);
            if (healing > 0 && patient.HP < patient.MaxHP && (injured.Safety != AIUnitSafetyLevel.HoldPositionOrFight
                || injured.Phase == AIUnitRecoveryPhase.Recovering || usefulSquadSupport)) return 1;
        }
        return 4;
    }

    public string PreservationRejectReason(AIAction action, AIBoardState board)
    {
        if (action?.Unit == null || !IsMovement(action) || board == null
            || !AIUnitPreservationState.IsValidPoint(action.TargetPos)) return null;
        // Existing emergency/base rescue and observed decisive-victory orders keep ownership.
        if (Mode == StrategicMode.EmergencyDefense || MatchObjectiveRules.IsCommander(action.Unit)
            || IsImmediateRescue(action, board)) return null;
        var recovery = AssessPreservation(action.Unit, board);
        if (retreatFirstHopReservations.TryGetValue(GridHelper.ToGridXZ(action.TargetPos), out var reserved)
            && reserved != action.Unit.ReflectionLifeId) return "unit_retreat_first_hop_reserved";
        float after = IncomingDamage(board, action.Unit, action.TargetPos, null);
        if (recovery.HasLegalSaferMove && after >= action.Unit.HP) return "unit_observed_lethal_advance";
        if (recovery.Phase != AIUnitRecoveryPhase.Recovering || !recovery.HealingPossible) return null;
        return after > recovery.IncomingCurrent + .001f ? "unit_recovery_unsafe_reentry" : null;
    }

    public AIUnitPreservationState CapturePreservationState(Team owner)
    {
        var result = new AIUnitPreservationState { Owner = owner };
        if (!hasPreservationOwner || preservationOwner != owner) return result;
        foreach (var entry in unitRecovery.Values) if (result.Records.Count < AIUnitPreservationState.MaximumRecords) result.Records.Add(entry.Copy());
        result.Records.Sort((a, b) => string.CompareOrdinal(a.ActorLifeId, b.ActorLifeId));
        return result;
    }
    public void RestorePreservationState(AIUnitPreservationState state, Team owner)
    {
        ReleasePreservationState();
        var snapshot = AIUnitPreservationState.CopyForOwner(state, owner);
        if (snapshot == null) return;
        preservationOwner = owner; hasPreservationOwner = true;
        foreach (var record in snapshot.Records) unitRecovery.Add(record.ActorLifeId, record);
    }
    public void ReleasePreservationState()
    {
        unitRecovery.Clear(); preservationAssessments.Clear(); preservationOwnIds.Clear(); staleRecoveryIds.Clear(); repeatedLossCache.Clear();
        retreatFirstHopReservations.Clear(); retreatRoutes.Clear();
        preservationBoard = null; preservationGeneration = preservationConfigRevision = -1;
        preservationOwner = default; hasPreservationOwner = false;
    }
}
