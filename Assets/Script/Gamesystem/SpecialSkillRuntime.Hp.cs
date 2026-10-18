using UnityEngine;

public static partial class SpecialSkillRuntime
{
    static int counterDepth;
    public static long HpRevision { get; private set; }
    public static HpChangeResult LastHpChange { get; private set; }
    static void ResetHpHistory() { LastHpChange = default; HpRevision = 0; counterDepth = 0; }
    public static Status ResolveDamageRecipient(Status target, int amount, HpChangeKind kind, bool respectShield = true)
    {
        if (target == null || kind == HpChangeKind.DirectHpReduction || kind == HpChangeKind.ForcedDeath
            || respectShield && target.ShieldTurns > 0 || State(target).damageNullUnused && State(target).damageNullTurns > 0) return target;
        var partner = FindActor(State(target).guardianPartnerId);
        if (partner == null || State(partner).guardianPartnerId != target.ReflectionLifeId) return target;
        return partner;
    }
    public static int ApplyAbilityDamage(Status source, Status target, int amount, HpChangeKind kind, HpChangeCause cause = HpChangeCause.Skill)
    {
        // Direct offensive skills and counters are attacks; buffs, healing,
        // sacrifices and delayed terrain/bomb damage do not animate the caster.
        if (source != null && target != null && source != target && amount > 0
            && (cause == HpChangeCause.Skill || cause == HpChangeCause.Counter)
            && (kind == HpChangeKind.NormalDamage || kind == HpChangeKind.IgnoreDefenceDamage || kind == HpChangeKind.ExecutionDamage))
            BoardActorAnimation.NotifyAttack(source, target.transform.position);
        var recipient = ResolveDamageRecipient(target, amount, kind);
        int actual = ApplyHpChange(source, target, amount, kind, cause: cause);
        if (recipient != null && actual > 0 && (kind == HpChangeKind.NormalDamage || kind == HpChangeKind.IgnoreDefenceDamage || kind == HpChangeKind.ExecutionDamage))
        {
            Status.AwardDamageExperience(source, recipient, actual, source?.RuntimeSystems?.FactionState);
            MatchStats.Instance?.RecordDamage(source != null ? source.team : Team.None, recipient.team, actual, !recipient.IsAlive, recipient.isWildBoss);
        }
        CompleteSpecialDeath(recipient); CompleteSpecialDeath(target);
        if (source != target) CompleteSpecialDeath(source);
        return actual;
    }
    public static void CompleteSpecialDeath(Status actor)
    {
        if (actor == null || actor.HP > 0 || !actor.gameObject.activeInHierarchy) return;
        if (actor.TryTriggerGameEndIfDecisive()) return;
        if (actor.type == Type.Building || actor.type == Type.Wall)
        {
            var systems = actor.RuntimeSystems;
            if (systems?.SubCrystalSystem != null) systems.SubCrystalSystem.DestroyBuilding(actor);
            else actor.gameObject.SetActive(false);
        }
        else actor.HandleDeathIfDead();
    }
    public static int PreviewDamage(Status target, int amount, HpChangeKind kind = HpChangeKind.NormalDamage)
    {
        if (target == null || !target.IsAlive || amount <= 0) return 0;
        if (kind == HpChangeKind.ForcedDeath) return target.HP;
        if (kind == HpChangeKind.DirectHpReduction) return Mathf.Min(target.HP, amount);
        var state = State(target);
        if (target.ShieldTurns > 0 || state.damageNullUnused && state.damageNullTurns > 0) return 0;
        long adjusted = amount;
        if (state.nextDamageDouble) adjusted *= 2;
        if (state.holyKnightTurns > 0 || state.holyKnightRecoveryLock) adjusted *= 2;
        return (int)System.Math.Min(int.MaxValue, adjusted);
    }
    public static int ApplyHpChange(Status source, Status target, int amount, HpChangeKind kind, bool redirected = false, bool respectShield = true, bool deferCleanup = false, HpChangeCause cause = HpChangeCause.Unknown)
    {
        var recipient = redirected ? target : ResolveDamageRecipient(target, amount, kind, respectShield);
        int loss = ApplyHpChangeCore(source, target, amount, kind, redirected, respectShield, deferCleanup);
        LastHpChange = new HpChangeResult(source, recipient, amount, loss, 0, kind, cause);
        if (CombatRuleTelemetry.Enabled) CombatRuleTelemetry.Record("actual_hp_loss", source, recipient, cause.ToString(), amount, loss);
        if (source != null && source != target && loss > 0) State(source).dealtDamage = true;
        return loss;
    }
    public static void RecordHeal(Status target, int requested, int actual)
        => LastHpChange = new HpChangeResult(null, target, requested, 0, actual, HpChangeKind.NormalDamage);
    static int ApplyHpChangeCore(Status source, Status target, int amount, HpChangeKind kind, bool redirected, bool respectShield, bool deferCleanup)
    {
        if (target == null || !target.IsAlive || amount <= 0) return 0;
        bool bypass = kind == HpChangeKind.DirectHpReduction || kind == HpChangeKind.ForcedDeath;
        if (kind == HpChangeKind.ForcedDeath)
        {
            int actual = target.HP; target.HP = 0; HpRevision++;
            OnSpecialDeath(target); if (!deferCleanup) CompleteSpecialDeath(target); return actual;
        }
        if (bypass)
        {
            int actual = target.ApplyRawDamage(amount, true, false);
            if (actual > 0) HpRevision++;
            if (target.HP <= 0) { OnSpecialDeath(target); CompleteSpecialDeath(target); }
            return actual;
        }
        if ((respectShield && target.ShieldTurns > 0) || target.isWildBoss && target.IsWildBossProtected)
        { BoardActionFeedback.Shield(target); return 0; }
        var s = State(target);
        // Nullification protects the intended recipient before any link can intercept.
        if (s.damageNullUnused && s.damageNullTurns > 0)
        { s.damageNullUnused = false; s.damageNullTurns = 0; HpRevision++; BoardActionFeedback.Shield(target); return 0; }
        if (!redirected && !string.IsNullOrEmpty(s.guardianPartnerId))
        {
            var partner = FindActor(s.guardianPartnerId);
            if (partner == null || State(partner).guardianPartnerId != target.ReflectionLifeId) s.guardianPartnerId = null;
            else
            {
                if (PreviewDamage(target, amount, kind) >= target.HP)
                {
                    Unlink(target, partner);
                    return ApplyHpChange(source, partner, partner.HP, HpChangeKind.ForcedDeath, true);
                }
                return ApplyHpChange(source, partner, amount, kind, true);
            }
        }
        long adjusted = amount;
        if (s.nextDamageDouble) adjusted *= 2;
        if (s.holyKnightTurns > 0 || s.holyKnightRecoveryLock) adjusted *= 2;
        int requested = (int)System.Math.Min(int.MaxValue, adjusted);
        if (requested <= 0) return 0;
        int before = target.HP;
        int applied = kind != HpChangeKind.ExecutionDamage && SpecialAbilitySystem.TrySurviveLethalCore(target, requested)
            ? Mathf.Max(0, before - target.HP) : target.ApplyRawDamage(requested);
        if (applied > 0)
        {
            HpRevision++;
            if (source != null && source != target) State(source).dealtDamage = true;
            s.nextDamageDouble = false;
            var definition = Definition(target);
            if (definition != null && definition.holyKnightInterruptOnDamage && definition.holyKnightInterruptionConfigured) { s.holyKnightTurns = 0; s.holyKnightRecoveryLock = false; }
            if (source != null && source != target && counterDepth == 0 && target.IsAlive && source.IsAlive
                && definition != null && definition.enabledForPlay && definition.ability == UnitSpecialSkill.SamuraiCounter && definition.samuraiGaugeMax > 0 && s.samuraiGauge >= definition.samuraiGaugeMax && s.samuraiMaxTurns > 0)
            {
                s.samuraiGauge = s.samuraiMaxTurns = 0; counterDepth++;
                try
                {
                    int counter = (int)System.Math.Min(int.MaxValue, (long)Mathf.RoundToInt(applied * SpecialSkillDefinition.SamuraiCounterFraction) + Mathf.Max(0, target.ATK));
                    int dealt = ApplyHpChange(target, source, counter, HpChangeKind.IgnoreDefenceDamage, cause: HpChangeCause.Counter);
                    target.ApplyHeal(dealt);
                }
                finally { counterDepth--; }
            }
        }
        if (target.HP <= 0) OnSpecialDeath(target);
        return applied;
    }
    static void Unlink(Status actor, Status partner)
    { State(actor).guardianPartnerId = null; if (partner != null && State(partner).guardianPartnerId == actor.ReflectionLifeId) State(partner).guardianPartnerId = null; }
    static void OnSpecialDeath(Status actor)
    {
        actor.MarkLifeFinished();
        var state = State(actor); Unlink(actor, FindActor(state.guardianPartnerId));
        state.ritualTargetId = state.sniperTargetId = null;
        ProcessBombDeath(actor, actor.RuntimeSystems);
    }
}
