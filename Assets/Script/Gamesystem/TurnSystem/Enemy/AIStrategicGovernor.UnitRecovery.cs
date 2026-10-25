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
    AIBoardState preservationBoard;
    int preservationGeneration = -1, preservationConfigRevision = -1;
    public int TrackedRecoveryUnits => unitRecovery.Count;

    /// <summary>Also called for an empty army so destroyed actors cannot retain recovery records.</summary>
    public void PrunePreservationState(AIBoardState board)
    {
        if (board != null) PreparePreservation(board);
    }

    void PreparePreservation(AIBoardState board)
    {
        var config = AIUnitPreservationConfig.Active;
        if (preservationBoard == board && preservationGeneration == board.Generation && preservationConfigRevision == config.Revision) return;
        preservationBoard = board; preservationGeneration = board.Generation; preservationConfigRevision = config.Revision;
        preservationAssessments.Clear(); preservationOwnIds.Clear(); staleRecoveryIds.Clear(); repeatedLossCache.Clear();
        foreach (var actor in board.AliveEnemyUnits)
            if (actor != null && actor.IsAlive && actor.team == board.ActorTeam && actor.type == Type.Unit)
                preservationOwnIds.Add(actor.ReflectionLifeId);
        foreach (var pair in unitRecovery)
            if (!preservationOwnIds.Contains(pair.Key) || pair.Value.LastTurn > board.TurnCount) staleRecoveryIds.Add(pair.Key);
        foreach (var id in staleRecoveryIds) unitRecovery.Remove(id);
        if (!config.Enabled) unitRecovery.Clear();
    }

    public AIUnitPreservationAssessment AssessPreservation(Status actor, AIBoardState board)
    {
        if (board == null || actor == null || !actor.IsAlive || actor.team != board.ActorTeam || actor.type != Type.Unit
            || MatchObjectiveRules.IsCommander(actor)) return default;
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
        bool ongoing = prior != null && prior.Phase != AIUnitRecoveryPhase.Ready && ratio < config.RejoinRatio && healing;
        bool lethal = incoming >= actor.HP;
        bool attrition = ratio <= config.RejoinRatio && repeated >= actor.HP;
        bool wounded = ratio <= config.WoundedRatio && incoming > 0 && healing;
        bool considerEscape = lethal || attrition || wounded || ongoing && incoming > 0;
        bool escape = false; int options = 0; Vector3 refuge = origin;
        float bestRisk = incoming, bestRepeated = repeated; bool refugeHasHealer = false; int bestCost = int.MaxValue;
        if (considerEscape && !StatusEffectSystem.IsStunned(actor) && !StatusEffectSystem.IsMovementBlocked(actor)
            && !SpecialSkillRuntime.MovementBlocked(actor)
            && BoardActionProfile.For(actor)?.CanMove != false)
        {
            foreach (var destination in board.GetValidMoves(actor))
            {
                int cost = board.CalcMoveCost(actor, destination); if (cost > board.EnemyAP || cost < 0) continue;
                float risk = IncomingDamage(board, actor, destination, null);
                float later = RepeatedObservedLoss(board, actor, destination, null, config.Waves);
                if (risk >= incoming - .001f && later >= repeated - .001f) continue;
                options++;
                bool healer = board.HasHealerInRange(destination, 4);
                bool retainedRefuge = prior != null && GridHelper.MatchXZ(destination, GridHelper.ToGridXZ(prior.Refuge));
                bool sameRisk = Mathf.Abs(risk - bestRisk) <= .001f && Mathf.Abs(later - bestRepeated) <= .001f;
                if (!escape || risk < bestRisk - .001f || Mathf.Abs(risk - bestRisk) <= .001f && later < bestRepeated - .001f
                    || sameRisk && (healer && !refugeHasHealer || healer == refugeHasHealer && (retainedRefuge || cost < bestCost)))
                { escape = true; refuge = destination; bestRisk = risk; bestRepeated = later; refugeHasHealer = healer; bestCost = cost; }
            }
        }
        var safety = lethal ? AIUnitSafetyLevel.Emergency
            : escape && (attrition || wounded || ongoing && incoming > 0) ? AIUnitSafetyLevel.WithdrawalRecommended
            : AIUnitSafetyLevel.HoldPositionOrFight;
        var phase = safety != AIUnitSafetyLevel.HoldPositionOrFight ? AIUnitRecoveryPhase.Withdrawing
            : ongoing ? AIUnitRecoveryPhase.Recovering : AIUnitRecoveryPhase.Ready;
        string reason = lethal ? (escape ? "observed_lethal_escape" : "observed_lethal_no_escape")
            : safety == AIUnitSafetyLevel.WithdrawalRecommended ? (attrition ? "observed_repeated_exchange_risk" : "wounded_unit_safe_recovery")
            : phase == AIUnitRecoveryPhase.Recovering ? "recovering_without_observed_exposure" : "fight_or_progress_remains_reasonable";
        if (phase != AIUnitRecoveryPhase.Ready)
        {
            if (prior != null || unitRecovery.Count < config.Limit)
                unitRecovery[actor.ReflectionLifeId] = new AIUnitPreservationRecord { ActorLifeId = actor.ReflectionLifeId,
                    Phase = phase, LastTurn = board.TurnCount, Refuge = escape ? refuge : origin };
        }
        else unitRecovery.Remove(actor.ReflectionLifeId);
        var result = new AIUnitPreservationAssessment(safety, phase, incoming, repeated, bestRisk, escape, healing, options, allies, refuge, reason);
        preservationAssessments[actor] = result; return result;
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
        if (assessment.Safety == AIUnitSafetyLevel.WithdrawalRecommended && IsMovement(action)
            && !StatusEffectSystem.IsMovementBlocked(action.Unit) && !SpecialSkillRuntime.MovementBlocked(action.Unit)
            && board.GetValidMoves(action.Unit).Contains(action.TargetPos)
            && (IncomingDamage(board, action.Unit, action.TargetPos, null) + .001f < assessment.IncomingCurrent
                || RepeatedObservedLoss(board, action.Unit, action.TargetPos, null, AIUnitPreservationConfig.Active.Waves)
                    + .001f < assessment.RepeatedObservedLoss)) return 1;
        // A useful observed kill may remove the very threat that motivated withdrawal.
        if (action.ActionType == AIActionType.Attack && action.TargetUnit != null && board.IsSpecialActorObserved(action.TargetUnit)
            && board.GetAttackTargets(action.Unit).Contains(action.TargetUnit)
            && AIActionEvaluator.CanFinishSpecialTarget(action.Unit, action.TargetUnit, board))
        {
            foreach (var patient in board.AliveEnemyUnits)
            {
                if (patient == null || !patient.IsAlive || patient.type != Type.Unit) continue;
                var threatened = AssessPreservation(patient, board);
                if (threatened.Safety == AIUnitSafetyLevel.HoldPositionOrFight) continue;
                if (IncomingDamage(board, patient, patient.transform.position, action.TargetUnit) + .001f < threatened.IncomingCurrent
                    || RepeatedObservedLoss(board, patient, patient.transform.position, action.TargetUnit, AIUnitPreservationConfig.Active.Waves)
                        + .001f < threatened.RepeatedObservedLoss) return 1;
            }
        }
        if (action.ActionType == AIActionType.SkillUse && action.TargetUnit != null && action.TargetUnit.team == board.ActorTeam)
        {
            var patient = action.TargetUnit; var injured = AssessPreservation(patient, board);
            int healing = action.Skill != null && action.Unit.AssignedSkillId == action.Skill.Id
                && action.Unit.SkillCooldown == 0 && AttackPatterns.CanUseSkills(action.Unit)
                && !StatusEffectSystem.HasDebuff(action.Unit, StatusEffectType.Seal)
                && board.GetSkillTargets(action.Unit, action.Skill).Contains(patient) ? action.Skill.FixedHeal : 0;
            if (action.SpecialCommand != null && SpecialSkillRuntime.Definition(action.Unit)?.ability == UnitSpecialSkill.WhiteMageHeal
                && AIActionEvaluator.CalcSpecialSkillScore(action, board) > 0)
                healing = SpecialSkillRuntime.FractionOfMaximum(patient, SpecialSkillDefinition.WhiteMageHealFraction);
            if (healing > 0 && patient.HP < patient.MaxHP && (injured.Safety != AIUnitSafetyLevel.HoldPositionOrFight
                || injured.Phase == AIUnitRecoveryPhase.Recovering)) return 1;
        }
        return 4;
    }

    public string PreservationRejectReason(AIAction action, AIBoardState board)
    {
        if (action?.Unit == null || !IsMovement(action) || board == null) return null;
        var recovery = AssessPreservation(action.Unit, board);
        if (recovery.Phase != AIUnitRecoveryPhase.Recovering || !recovery.HealingPossible) return null;
        float after = IncomingDamage(board, action.Unit, action.TargetPos, null);
        return after > recovery.IncomingCurrent + .001f ? "unit_recovery_unsafe_reentry" : null;
    }

    public AIUnitPreservationState CapturePreservationState(Team owner)
    {
        var result = new AIUnitPreservationState { Owner = owner };
        foreach (var entry in unitRecovery.Values) if (result.Records.Count < 128) result.Records.Add(entry.Copy());
        result.Records.Sort((a, b) => string.CompareOrdinal(a.ActorLifeId, b.ActorLifeId));
        return result;
    }
    public void RestorePreservationState(AIUnitPreservationState state, Team owner)
    {
        ReleasePreservationState();
        if (state == null || state.Version != 1 || state.Owner != owner || state.Records == null || state.Records.Count > 128) return;
        foreach (var record in state.Records)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.ActorLifeId) || record.ActorLifeId.Length > 128
                || record.LastTurn < 0 || !Enum.IsDefined(typeof(AIUnitRecoveryPhase), record.Phase)
                || record.Phase == AIUnitRecoveryPhase.Ready || !FiniteRecoveryPoint(record.Refuge)) continue;
            if (!unitRecovery.ContainsKey(record.ActorLifeId)) unitRecovery.Add(record.ActorLifeId, record.Copy());
        }
    }
    static bool FiniteRecoveryPoint(Vector3 point) => !float.IsNaN(point.sqrMagnitude) && !float.IsInfinity(point.sqrMagnitude)
        && point.x >= 0 && point.x < 32768 && point.z >= 0 && point.z < 32768 && point.y >= -1024 && point.y <= 1024;
    public void ReleasePreservationState()
    {
        unitRecovery.Clear(); preservationAssessments.Clear(); preservationOwnIds.Clear(); staleRecoveryIds.Clear(); repeatedLossCache.Clear();
        preservationBoard = null; preservationGeneration = preservationConfigRevision = -1;
    }
}
