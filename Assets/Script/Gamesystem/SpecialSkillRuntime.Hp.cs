using UnityEngine;

public static partial class SpecialSkillRuntime
{
    static int counterDepth;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static event System.Action<HpChangeResult> HpChangeCommitted;
#endif
    public static long HpRevision { get; private set; }
    public static HpChangeResult LastHpChange { get; private set; }
    static void ResetHpHistory() { LastHpChange = default; HpRevision = 0; counterDepth = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        HpChangeCommitted = null;
#endif
    }
    public static Status ResolveDamageRecipient(Status target, int amount, HpChangeKind kind, bool respectShield = true)
    {
        if (target == null || kind == HpChangeKind.DirectHpReduction || kind == HpChangeKind.ForcedDeath
            || respectShield && target.HasDamageShield || State(target).damageNullUnused && State(target).damageNullTurns > 0) return target;
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
            && (kind == HpChangeKind.NormalDamage || kind == HpChangeKind.IgnoreDefenceDamage || kind == HpChangeKind.ExecutionDamage || kind == HpChangeKind.BarrierBypassDamage))
            BoardActorAnimation.NotifyAttack(source, target.transform.position);
        var recipient = ResolveDamageRecipient(target, amount, kind);
        int actual = ApplyHpChange(source, target, amount, kind, cause: cause);
        if (recipient != null && actual > 0 && (kind == HpChangeKind.NormalDamage || kind == HpChangeKind.IgnoreDefenceDamage || kind == HpChangeKind.ExecutionDamage || kind == HpChangeKind.BarrierBypassDamage))
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
        if (DamageCalculator.IsFullyProtected(target) || state.damageNullUnused && state.damageNullTurns > 0) return 0;
        long adjusted = amount;
        if (state.nextDamageDouble) adjusted *= 2;
        if (state.holyKnightTurns > 0 || state.holyKnightRecoveryLock) adjusted *= 2;
        return (int)System.Math.Min(int.MaxValue, adjusted);
    }
    public static int PreviewHpDamage(Status target, int amount, HpChangeKind kind = HpChangeKind.NormalDamage)
    {
        if (target == null || !target.IsAlive) return 0;
        return BarrierSystem.PreviewHpLoss(target.HP, target.Barrier, PreviewDamage(target, amount, kind),
            kind == HpChangeKind.BarrierBypassDamage || kind == HpChangeKind.DirectHpReduction || kind == HpChangeKind.ForcedDeath);
    }
    public static int PreviewDedicatedSkillDamage(Status caster, Status target)
    {
        int raw = DamageCalculator.CalcNormal(caster, target);
        var definition = Definition(caster);
        var recipient = definition?.ability == UnitSpecialSkill.HeavyWarriorBreak
            ? ResolveDamageRecipient(target, raw, HpChangeKind.NormalDamage) ?? target : target;
        return PreviewDedicatedSkillDamage(caster, recipient, raw);
    }
    public static int PreviewDedicatedSkillDamage(Status caster, Status target, int raw)
    {
        var definition = Definition(caster);
        if (caster == null || target == null || definition == null) return 0;
        if (definition.ability == UnitSpecialSkill.HeavyWarriorBreak)
            return BarrierSystem.AmplifyBarrierPortion(raw, target.Barrier, definition.heavyBarrierMultiplier);
        if (definition.ability == UnitSpecialSkill.DragonKnightPierce)
            return DamageCalculator.FinalizeHitDamage(raw * definition.dragonKnightSkillMultiplier);
        return 0;
    }
    public static int ApplyHpChange(Status source, Status target, int amount, HpChangeKind kind, bool redirected = false, bool respectShield = true, bool deferCleanup = false, HpChangeCause cause = HpChangeCause.Unknown)
    {
        var recipient = redirected ? target : ResolveDamageRecipient(target, amount, kind, respectShield);
        int loss = ApplyHpChangeCore(source, target, amount, kind, redirected, respectShield, deferCleanup, out int hpLoss, out int barrierLoss);
        if (!redirected && source != null && target != null && source != target && amount > 0
            && (cause == HpChangeCause.NormalAttack || cause == HpChangeCause.Skill))
            MainCrystalRules.RegisterAttack(target, source.team, source.RuntimeSystems?.TurnGenerator?.Context.Turn ?? -1);
        LastHpChange = new HpChangeResult(source, recipient, amount, hpLoss, 0, kind, cause, barrierLoss);
        if (CombatRuleTelemetry.Enabled) CombatRuleTelemetry.Record("actual_hp_loss", source, recipient, cause.ToString(), amount, hpLoss);
        if (source != null && source != target && loss > 0) State(source).dealtDamage = true;
        if (!redirected && recipient != null && loss > 0)
            FloatingDamageUI.ShowCombatDamage(recipient, hpLoss, barrierLoss, !recipient.IsAlive);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!redirected) HpChangeCommitted?.Invoke(LastHpChange);
#endif
        return loss;
    }
    public static int ExperienceDamage(Status source, Status target, int totalDamage)
    {
        if (BarrierCombatSettings.Active.barrierDamageGrantsExperience) return Mathf.Max(0, totalDamage);
        var result = LastHpChange;
        return result.Source == source && result.Target == target ? Mathf.Max(0, result.ActualHpLoss) : Mathf.Max(0, totalDamage);
    }
    public static void RecordHeal(Status target, int requested, int actual)
        => LastHpChange = new HpChangeResult(null, target, requested, 0, actual, HpChangeKind.NormalDamage);
    static int ApplyHpChangeCore(Status source, Status target, int amount, HpChangeKind kind, bool redirected, bool respectShield, bool deferCleanup, out int hpLoss, out int barrierLoss)
    {
        hpLoss = barrierLoss = 0;
        if (target == null || !target.IsAlive || amount <= 0) return 0;
        bool bypass = kind == HpChangeKind.DirectHpReduction || kind == HpChangeKind.ForcedDeath;
        if (kind == HpChangeKind.ForcedDeath)
        {
            int actual = target.HP; target.HP = 0; target.Barrier = 0; hpLoss = actual; HpRevision++;
            OnSpecialDeath(target); if (!deferCleanup) CompleteSpecialDeath(target); return actual;
        }
        if (bypass)
        {
            int actual = target.ApplyRawDamage(amount, true, false); hpLoss = actual;
            if (actual > 0) HpRevision++;
            if (target.HP <= 0) { OnSpecialDeath(target); CompleteSpecialDeath(target); }
            return actual;
        }
        if ((respectShield && target.HasDamageShield) || target.isWildBoss && target.IsWildBossProtected)
        { BarrierSystem.RecordNegatedHit(target); BoardActionFeedback.Shield(target); return 0; }
        var s = State(target);
        // Nullification protects the intended recipient before any link can intercept.
        if (s.damageNullUnused && s.damageNullTurns > 0)
        { s.damageNullUnused = false; s.damageNullTurns = 0; HpRevision++; BarrierSystem.RecordNegatedHit(target); BoardActionFeedback.Shield(target); return 0; }
        if (!redirected && !string.IsNullOrEmpty(s.guardianPartnerId))
        {
            var partner = FindActor(s.guardianPartnerId);
            if (partner == null || State(partner).guardianPartnerId != target.ReflectionLifeId) s.guardianPartnerId = null;
            else
            {
                if (PreviewHpDamage(target, amount, kind) >= target.HP)
                {
                    Unlink(target, partner);
                    int redirectedLoss = ApplyHpChange(source, partner, partner.HP, HpChangeKind.ForcedDeath, true);
                    hpLoss = LastHpChange.ActualHpLoss; barrierLoss = LastHpChange.ActualBarrierLoss; return redirectedLoss;
                }
                int redirectedDamage = ApplyHpChange(source, partner, amount, kind, true);
                hpLoss = LastHpChange.ActualHpLoss; barrierLoss = LastHpChange.ActualBarrierLoss; return redirectedDamage;
            }
        }
        long adjusted = amount;
        if (s.nextDamageDouble) adjusted *= 2;
        if (s.holyKnightTurns > 0 || s.holyKnightRecoveryLock) adjusted *= 2;
        int requested = (int)System.Math.Min(int.MaxValue, adjusted);
        if (requested <= 0) return 0;
        int hpRequested = requested;
        if (kind != HpChangeKind.BarrierBypassDamage)
        {
            int beforeBarrier = target.Barrier;
            target.Barrier = BarrierSystem.Absorb(beforeBarrier, requested, out hpRequested);
            barrierLoss = beforeBarrier - target.Barrier;
            if (barrierLoss > 0) { BarrierSystem.RecordDamage(target); BoardActorAnimation.NotifyHit(target); }
        }
        int before = target.HP;
        int applied = kind != HpChangeKind.ExecutionDamage && SpecialAbilitySystem.TrySurviveLethalCore(target, hpRequested)
            ? Mathf.Max(0, before - target.HP) : target.ApplyRawDamage(hpRequested);
        hpLoss = applied;
        applied += barrierLoss;
        if (applied > 0)
        {
            HpRevision++;
            if (source != null && source != target) State(source).dealtDamage = true;
            s.nextDamageDouble = false;
            var definition = Definition(target);
            if (definition != null && definition.holyKnightInterruptOnDamage && definition.holyKnightInterruptionConfigured) { s.holyKnightTurns = 0; s.holyKnightRecoveryLock = false; }
            if (source != null && source != target && counterDepth == 0 && target.IsAlive && source.IsAlive
                && definition != null && definition.enabledForPlay && definition.ability == UnitSpecialSkill.SamuraiCounter
                && (definition.samuraiGaugeMax > 0 && s.samuraiGauge >= definition.samuraiGaugeMax && s.samuraiMaxTurns > 0
                    || State(source).tauntTurns > 0 && State(source).tauntedById == target.ReflectionLifeId))
            {
                s.samuraiGauge = s.samuraiMaxTurns = 0; counterDepth++;
                try
                {
                    int counter = (int)System.Math.Min(int.MaxValue, (long)Mathf.RoundToInt(applied * SpecialSkillDefinition.SamuraiCounterFraction) + Mathf.Max(0, target.ATK));
                    // Counter strips the recipient's current barrier before its damage, never its maximum.
                    var counterRecipient = ResolveDamageRecipient(source, counter, HpChangeKind.IgnoreDefenceDamage);
                    if (counterRecipient != null && PreviewDamage(source, counter, HpChangeKind.IgnoreDefenceDamage) > 0
                        && !counterRecipient.HasDamageShield && PreviewDamage(counterRecipient, counter, HpChangeKind.IgnoreDefenceDamage) > 0 && !(counterRecipient.isWildBoss && counterRecipient.IsWildBossProtected))
                    {
                        int stripped = counterRecipient.Barrier;
                        counterRecipient.Barrier = 0;
                        if (stripped > 0) { BarrierSystem.RecordDamage(counterRecipient); FloatingDamageUI.ShowBarrierBreak(counterRecipient); }
                    }
                    ApplyHpChange(target, source, counter, HpChangeKind.IgnoreDefenceDamage, cause: HpChangeCause.Counter);
                    target.ApplyHeal(LastHpChange.ActualHpLoss);
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
        actor.MarkLifeFinished(); actor.Barrier = 0; State(actor).dragonKnightGauge = 0;
        var state = State(actor); Unlink(actor, FindActor(state.guardianPartnerId));
        state.ritualTargetId = state.sniperTargetId = null;
        ProcessBombDeath(actor, actor.RuntimeSystems);
    }
}
