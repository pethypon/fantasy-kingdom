using System;
using UnityEngine;

/// <summary>One forecast's local protection state. No actor or hidden partner is mutated or queried.</summary>
public struct AIObservedDamageBudget
{
    AIObservedProtection own, partner;
    bool linkResolved, linked;
    public int Barrier { get => own.Barrier; set => own.Barrier = Mathf.Max(0,value); }

    public static AIObservedDamageBudget Capture(Status actor, bool ignoreFiniteBarrier = false)
        => new AIObservedDamageBudget { own = AIObservedProtection.Capture(actor,ignoreFiniteBarrier) };

    void ResolveLink(Status actor,AIBoardState board)
    {
        if (linkResolved) return;
        linkResolved = true;
        var observed = board?.FindObservedSpecialActor(actor.Special?.guardianPartnerId);
        if (observed == null || observed == actor || observed.Special?.guardianPartnerId != actor.ReflectionLifeId) return;
        partner = AIObservedProtection.Capture(observed);
        linked = partner.HP > 0;
    }

    internal int Apply(Status actor,AIBoardState board,int amount,HpChangeKind kind)
    {
        if (actor == null || amount <= 0) return 0;
        if (kind == HpChangeKind.ForcedDeath)
        { int loss=own.HP;own.HP=own.Barrier=0;linked=false;linkResolved=true;return loss; }
        if (kind == HpChangeKind.DirectHpReduction)
        { int loss=Mathf.Min(own.HP,amount);own.HP-=loss;return loss; }
        if (own.Shielded || AIActionEvaluator.IsObservedWildBossProtected(board,actor) || own.TryNullify()) return 0;
        ResolveLink(actor,board);
        if (linked)
        {
            // Runtime tests the intended target's lethal threshold, then redirects the raw packet.
            // Its damage-doubling effects are not spent while a reciprocal partner intercepts.
            int intended = BarrierSystem.PreviewHpLoss(own.HP,own.Barrier,own.Adjusted(amount),kind==HpChangeKind.BarrierBypassDamage);
            if (intended >= own.HP)
            { partner.HP=partner.Barrier=0;linked=false;return 0; }
            var observedPartner = board?.FindObservedSpecialActor(actor.Special?.guardianPartnerId);
            if (!AIActionEvaluator.IsObservedWildBossProtected(board,observedPartner)) partner.ApplyPacket(amount,kind,true);
            if (partner.HP <= 0) linked=false;
            return 0;
        }
        return own.ApplyPacket(amount,kind,false,alreadyCheckedNull:true);
    }
}

/// <summary>A value snapshot also used for at most one observed life-link partner. No recursive chains.</summary>
struct AIObservedProtection
{
    public int HP, Barrier;
    public bool Shielded;
    bool nullUnused,nextDamageDouble,holyKnightDouble,interruptHolyKnight;

    public static AIObservedProtection Capture(Status actor,bool ignoreFiniteBarrier=false)
    {
        if (actor == null) return default;
        var state=actor.Special;var definition=SpecialSkillRuntime.Definition(actor);
        return new AIObservedProtection
        {
            HP=Mathf.Max(0,actor.HP),
            Barrier=!ignoreFiniteBarrier && BarrierSystem.UsesBarrier(actor)?actor.Barrier:0,
            Shielded=actor.ShieldTurns>0,
            nullUnused=state?.damageNullUnused==true && state.damageNullTurns>0,
            nextDamageDouble=state?.nextDamageDouble==true,
            holyKnightDouble=state!=null && (state.holyKnightTurns>0 || state.holyKnightRecoveryLock),
            interruptHolyKnight=definition!=null && definition.holyKnightInterruptOnDamage && definition.holyKnightInterruptionConfigured
        };
    }
    public bool TryNullify()
    { if (!nullUnused) return false;nullUnused=false;return true; }
    public int Adjusted(int amount)
    {
        long adjusted=amount;
        if (nextDamageDouble) adjusted*=2;
        if (holyKnightDouble) adjusted*=2;
        return (int)Math.Min(int.MaxValue,adjusted);
    }
    public int ApplyPacket(int amount,HpChangeKind kind,bool capHpLoss,bool alreadyCheckedNull=false)
    {
        if (Shielded || !alreadyCheckedNull && TryNullify()) return 0;
        int adjusted=Adjusted(amount),barrierBefore=Barrier,hpLoss;
        if (kind==HpChangeKind.BarrierBypassDamage) hpLoss=Mathf.Min(HP,adjusted);
        else Barrier=BarrierSystem.Absorb(Barrier,adjusted,out hpLoss);
        int actualHp=Mathf.Min(HP,hpLoss);HP-=actualHp;
        if (actualHp>0 || Barrier<barrierBefore)
        {
            nextDamageDouble=false;
            if (interruptHolyKnight) holyKnightDouble=false;
        }
        return capHpLoss?actualHp:hpLoss;
    }
}
