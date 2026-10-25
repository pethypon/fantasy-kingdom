using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class AIActionEvaluator
{
    static float SpecialValue(Status unit)
        => AIBoardEvaluator.GetPieceValuePublic(unit) + (MatchObjectiveRules.IsDecisive(unit) ? 200 : 0);
    static float HpCost(Status unit, int loss)
    {
        if (unit == null || loss <= 0) return 0;
        float cost = SpecialValue(unit) * Mathf.Min(loss, unit.HP) / Mathf.Max(1f, unit.HP);
        if (loss >= unit.HP) cost += SpecialValue(unit) + (MatchObjectiveRules.IsDecisive(unit) ? 2000 : 30);
        return cost;
    }
    public static int EstimateSpecialAwareAttack(Status attacker, Status target, AIBoardState board)
    {
        if (attacker == null || target == null || !attacker.IsAlive || !target.IsAlive) return 0;
        if (IsObservedCompleteProtection(board, target)) return 0;
        if(board==null||!board.HasObservedSpecialState)return AIEvalHelpers.EstimateRawDamage(attacker,target);
        // All position-dependent special observations are restricted to the board's actors.
        float attackMod = 1, stackedKnightModifier = 1;
        if (board != null) foreach (var observer in board.SpecialObservedActors)
        {
            var definition = SpecialSkillRuntime.Definition(observer);
            if (observer.team == attacker.team || definition == null || !definition.enabledForPlay
                || definition.ability != UnitSpecialSkill.KnightBreakthrough || definition.knightVisibleEnemyAtkPenalty < 0
                || !SpecialSkillRuntime.InVision(observer, attacker)) continue;
            if (definition.knightPenaltyStackRule == AbilityStackRule.Multiply)
                stackedKnightModifier *= 1 - Mathf.Clamp01(definition.knightVisibleEnemyAtkPenalty);
            else attackMod = Mathf.Min(attackMod, 1 - Mathf.Clamp01(definition.knightVisibleEnemyAtkPenalty));
        }
        attackMod *= stackedKnightModifier;
        if (BarrierCombatSettings.Active.enabledForPlay) attackMod *= AIEvalHelpers.ObservedAttackModifier(attacker)*(1-attacker.UpkeepPenaltyATKDEF);
        foreach (var archer in board.SpecialObservedActors)
            if (archer.Special?.archerEscapeTurns > 0 && archer.Special.archerSnapshotIds.Contains(attacker.ReflectionLifeId))
            { attackMod *= SpecialSkillDefinition.ArcherEnemyAttackBoost; break; }
        int attack = (int)Math.Min(int.MaxValue, Math.Max(0, (attacker.ATK + (long)board.ObservedAttackFlatAdjustment(attacker)) * (double)attackMod));
        int defence = BarrierSystem.UsesBarrier(target) ? 0 : Mathf.Max(0, Mathf.RoundToInt(target.DEF * SpecialSkillRuntime.GetDEFModifier(target)));
        var attackDefinition=SpecialSkillRuntime.Definition(attacker);
        float multiplier=attacker.team!=board.ActorTeam&&attackDefinition?.ability==UnitSpecialSkill.NavigatorPull
            ?SpecialSkillDefinition.NavigatorMaximum:attackDefinition?.ability == UnitSpecialSkill.MagicRitual
                ? ObservedMagicianMultiplier(attacker, target, board)
                : attackDefinition?.ability==UnitSpecialSkill.NinjaSwap ? ObservedNinjaMultiplier(attacker,attackDefinition)
                : SpecialSkillRuntime.GetAttackMultiplier(attacker,target);
        double damage = DamageCalculator.FinalizeHitDamage(DamageCalculator.CalcRawBaseForTarget(attack, defence,
            BarrierSystem.UsesBarrier(target)))*(double)multiplier;
        var targetDefinition = SpecialSkillRuntime.Definition(target);
        if (targetDefinition != null && targetDefinition.enabledForPlay && targetDefinition.ability == UnitSpecialSkill.KnightBreakthrough
            && !SpecialSkillRuntime.InVision(target, attacker)) damage *= SpecialSkillDefinition.KnightBlindSpotMultiplier;
        if (board.KnownEnvironmentDefinition(target.transform.position) != null)
            damage *= board.ReconMap.Environment.IncomingDamageMultiplier(target.transform.position,
                DamageCalculator.IsRangedAttack(attacker), ((attacker.GrowthData?.attackTags ?? CombatAttackTags.None) & CombatAttackTags.Stone) != 0);
        if (BarrierCombatSettings.Active.enabledForPlay) damage *= StatusEffectSystem.GetIncomingDamageModifier(target);
        return (int)Math.Min(int.MaxValue, Math.Max(0, damage));
    }
    static float ObservedMagicianMultiplier(Status attacker, Status target, AIBoardState board)
    {
        foreach (var nearby in board.SpecialObservedActors)
        {
            // An area attack may destroy an observed actor before the board's next refresh.
            // Unity's destroyed-object null check must precede every component/property access.
            if (nearby == null || !nearby.IsAlive || nearby == attacker || nearby.type != Type.Unit
                || nearby.HPRatio > SpecialSkillDefinition.MagicLowHealthThreshold) continue;
            var delta = GridHelper.ToGrid(nearby.transform.position) - GridHelper.ToGrid(attacker.transform.position);
            if (Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z)) <= 1)
                return SpecialSkillRuntime.IsStrongTarget(target) ? SpecialSkillDefinition.MagicStrongTargetMultiplier : SpecialSkillDefinition.MagicOrdinaryTargetMultiplier;
        }
        return 1;
    }
    static float ObservedNinjaMultiplier(Status attacker,SpecialSkillDefinition definition)
    {
        // Opponent vision masks are private. Budget the known out-of-sight damage floor
        // instead of consulting the target's current VisionCell as a combat oracle.
        return .4f*(definition.HasCurve(definition.ninjaChargeCurve)
            ? Mathf.Max(1,definition.ninjaChargeCurve.Evaluate(Mathf.Clamp(attacker.Special?.ninjaCharge??0,0,5))) : 1);
    }
    static Status ObservedDamageRecipient(AIBoardState board, Status target)
    {
        if (target == null || target.ShieldTurns > 0
            || target.Special?.damageNullUnused == true && target.Special.damageNullTurns > 0) return target;
        string partnerId = target.Special?.guardianPartnerId;
        if (string.IsNullOrEmpty(partnerId)) return target;
        var partner = board?.FindObservedSpecialActor(partnerId);
        if (partner == null) return null;
        return partner.Special?.guardianPartnerId == target.ReflectionLifeId ? partner : target;
    }
    /// <summary>Barrier-only bonuses use the observed recipient; a hidden link never exposes its barrier.</summary>
    public static int EstimateObservedNormalPacket(Status attacker, Status target, AIBoardState board, int raw)
    {
        var recipient = ObservedDamageRecipient(board,target);
        return BarrierSystem.NormalAttackDamage(attacker != null ? attacker.kind : Kind.None,
            recipient != null ? recipient.Barrier : 0,raw);
    }
    static int EstimateObservedDedicatedPacket(Status attacker, Status target, AIBoardState board)
    {
        int raw = EstimateSpecialAwareAttack(attacker,target,board);
        var definition = SpecialSkillRuntime.Definition(attacker);
        if (definition?.ability == UnitSpecialSkill.HeavyWarriorBreak)
        {
            var recipient = ObservedDamageRecipient(board,target);
            return BarrierSystem.AmplifyBarrierPortion(raw,recipient != null ? recipient.Barrier : 0,definition.heavyBarrierMultiplier);
        }
        return SpecialSkillRuntime.PreviewDedicatedSkillDamage(attacker,target,raw);
    }
    static float DamageValue(AIBoardState board, Status target, int amount, HpChangeKind kind, bool includeKill = true)
    {
        if (target == null || !target.IsAlive || amount <= 0 || IsObservedCompleteProtection(board, target)) return 0;
        var victim = target;
        int damage;
        string partnerId = target.Special?.guardianPartnerId;
        if (!string.IsNullOrEmpty(partnerId) && kind != HpChangeKind.DirectHpReduction && kind != HpChangeKind.ForcedDeath)
        {
            var partner = ObservedDamageRecipient(board,target);
            // A visible link is not permission to query an unobserved partner's current HP/location.
            if (partner == null) return SpecialValue(target) * .05f;
            int original = SpecialSkillRuntime.PreviewHpDamage(target, amount, kind);
            if (SpecialSkillRuntime.PreviewDamage(target, amount, kind) <= 0) return 0;
            victim = partner;
            damage = original >= target.HP ? partner.HP : SpecialSkillRuntime.PreviewHpDamage(partner, amount, kind);
        }
        else damage = SpecialSkillRuntime.PreviewHpDamage(target, amount, kind);
        float value = SpecialValue(victim) * Mathf.Min(damage, victim.HP) / Mathf.Max(1f, victim.HP);
        if (kind != HpChangeKind.DirectHpReduction && kind != HpChangeKind.ForcedDeath && kind != HpChangeKind.BarrierBypassDamage
            && BarrierSystem.UsesBarrier(victim))
        {
            int barrierLoss = Mathf.Min(victim.Barrier, SpecialSkillRuntime.PreviewDamage(victim, amount, kind));
            value += SpecialValue(victim) * barrierLoss / Mathf.Max(1f, victim.HP + (float)victim.Barrier);
        }
        if (includeKill && damage >= victim.HP) value += 35 + (MatchObjectiveRules.IsDecisive(victim) ? 500 : 0);
        return value;
    }
    static float CounterDamageValue(AIBoardState board, Status target, int amount)
    {
        if (target == null || IsObservedCompleteProtection(board, target) || SpecialSkillRuntime.PreviewDamage(target,amount) <= 0) return 0;
        float value = DamageValue(board,target,amount,HpChangeKind.BarrierBypassDamage);
        var victim = string.IsNullOrEmpty(target.Special?.guardianPartnerId) ? target
            : board.FindObservedSpecialActor(target.Special.guardianPartnerId);
        if (victim != null && BarrierSystem.UsesBarrier(victim) && SpecialSkillRuntime.PreviewDamage(victim,amount)>0)
            value += SpecialValue(victim) * victim.Barrier / Mathf.Max(1f,victim.HP+(float)victim.Barrier);
        return value;
    }
    public static int EstimateEffectiveSpecialDamage(Status attacker, Status target, AIBoardState board, out Status victim)
    {
        victim=target;
        int raw=EstimateSpecialAwareAttack(attacker,target,board);
        raw=EstimateObservedNormalPacket(attacker,target,board,raw);
        if(target==null||IsObservedCompleteProtection(board,target))return 0;
        string partnerId=target.Special?.guardianPartnerId;
        if(!string.IsNullOrEmpty(partnerId))
        {
            var partner=ObservedDamageRecipient(board,target);
            if(partner==null) {victim=null;return 0;}
            int original=SpecialSkillRuntime.PreviewHpDamage(target,raw);
            if(SpecialSkillRuntime.PreviewDamage(target,raw)<=0)return 0;
            victim=partner;return original>=target.HP?partner.HP:SpecialSkillRuntime.PreviewHpDamage(partner,raw);
        }
        return SpecialSkillRuntime.PreviewHpDamage(target,raw);
    }
    public static bool CanFinishSpecialTarget(Status attacker,Status target,AIBoardState board)
    {
        int damage=EstimateEffectiveSpecialDamage(attacker,target,board,out var victim);
        return target!=null&&victim==target&&damage>=target.HP;
    }
    public static int EstimateEffectiveSpecialImpact(Status attacker, Status target, AIBoardState board, out Status victim)
    {
        int hp = EstimateEffectiveSpecialDamage(attacker,target,board,out victim);
        if (victim == null) return 0;
        int raw = EstimateObservedNormalPacket(attacker,target,board,EstimateSpecialAwareAttack(attacker,target,board));
        int barrier = Mathf.Min(victim.Barrier,SpecialSkillRuntime.PreviewDamage(victim,raw));
        return (int)Math.Min(int.MaxValue,(long)hp+barrier);
    }
    public static int EstimateObservedHitLoss(Status unit,AIBoardState board,int amount,HpChangeKind kind)
    {
        var budget = AIObservedDamageBudget.Capture(unit);
        int loss = EstimateObservedHitLoss(unit,board,amount,kind,ref budget);
        return unit != null ? Mathf.Min(unit.HP,loss) : 0;
    }
    /// <summary>Compatibility overload for single packets. Multi-packet forecasts must share the complete budget.</summary>
    public static int EstimateObservedHitLoss(Status unit, AIBoardState board, int amount, HpChangeKind kind, ref int barrier)
    {
        var budget = AIObservedDamageBudget.Capture(unit); budget.Barrier = barrier;
        int loss = EstimateObservedHitLoss(unit,board,amount,kind,ref budget);
        barrier = budget.Barrier;
        return loss;
    }
    public static int EstimateObservedHitLoss(Status unit, AIBoardState board, int amount, HpChangeKind kind, ref AIObservedDamageBudget budget)
    {
        return budget.Apply(unit,board,amount,kind);
    }
    public static int EstimateSpecialReservedThreatAt(AIBoardState board,Status unit,Vector3 point)
    {
        var budget = AIObservedDamageBudget.Capture(unit);
        return EstimateSpecialReservedThreatAt(board, unit, point, ref budget);
    }
    public static int EstimateSpecialReservedThreatAt(AIBoardState board,Status unit,Vector3 point,ref int barrier)
    {
        var budget = AIObservedDamageBudget.Capture(unit); budget.Barrier = barrier;
        int loss = EstimateSpecialReservedThreatAt(board,unit,point,ref budget); barrier = budget.Barrier;
        return loss;
    }
    public static int EstimateSpecialReservedThreatAt(AIBoardState board,Status unit,Vector3 point,ref AIObservedDamageBudget budget)
    {
        if(board==null||unit==null||!board.HasObservedSpecialState)return 0;
        long damage=board.EstimateSpecialHazardAt(unit,point,ref budget);
        foreach(var actor in board.SpecialObservedActors)
        {
            var state=actor.Special;if(state==null)continue;
            if(actor.team!=board.ActorTeam&&state.ritualActive&&state.ritualTargetId==unit.ReflectionLifeId
                &&state.ritualFireTurn<=state.ownerTurn+1)
                damage+=EstimateObservedHitLoss(unit,board,state.storedSacrificeDamage,HpChangeKind.IgnoreDefenceDamage,ref budget);
            if(actor.team!=board.ActorTeam&&state.sniperActive&&state.sniperTargetId==unit.ReflectionLifeId
                &&state.sniperFireTurn<=state.ownerTurn+1&&!SpecialSkillRuntime.AttackBlocked(actor)&&!StatusEffectSystem.IsStunned(actor))
            {
                var definition=SpecialSkillRuntime.Definition(actor);
                if(definition!=null)
                {
                    double raw=DamageCalculator.FinalizeHitDamage(DamageCalculator.CalcRawBaseForTarget(actor.ATK,unit.DEF,BarrierSystem.UsesBarrier(unit)))
                        *(double)definition.SniperMultiplier(GridHelper.ChebyshevDistance(actor.transform.position,point));
                    damage+=EstimateObservedHitLoss(unit,board,(int)Math.Min(int.MaxValue,raw),HpChangeKind.NormalDamage,ref budget);
                }
            }
            if(string.IsNullOrEmpty(state.bombCasterId)||state.bombRadius<0||state.bombDueRound>board.TurnCount+1
                ||GridHelper.ChebyshevDistance(point,actor.transform.position)>state.bombRadius)continue;
            int power=SpecialSkillRuntime.FractionOfMaximum(actor,SpecialSkillDefinition.BombFraction);
            if(actor==unit)damage+=EstimateObservedHitLoss(unit,board,power,HpChangeKind.DirectHpReduction,ref budget);
            bool affected=actor==unit?(state.bombMask&SpecialFactionMask.Holder)!=0
                :unit.team==state.bombCasterTeam?(state.bombMask&SpecialFactionMask.Allies)!=0
                :(state.bombMask&SpecialFactionMask.Hostile)!=0;
            if(affected)damage+=EstimateObservedHitLoss(unit,board,power,HpChangeKind.IgnoreDefenceDamage,ref budget);
        }
        return (int)Math.Min(int.MaxValue,damage);
    }
    public static int ObservedSniperRecoilLoss(Status unit,AIBoardState board,SpecialSkillDefinition definition,float distance,bool locked)
    {
        if (unit==null) return 0;
        var budget=AIObservedDamageBudget.Capture(unit);
        long loss=EstimateObservedHitLoss(unit,board,SpecialSkillRuntime.FractionOfMaximum(unit,
            SpecialSkillDefinition.SniperNormalRecoilFraction),HpChangeKind.NormalDamage,ref budget);
        if (locked && definition!=null)
            loss+=EstimateObservedHitLoss(unit,board,SpecialSkillRuntime.FractionOfMaximum(unit,
                definition.SniperRecoil(distance)),HpChangeKind.NormalDamage,ref budget);
        return (int)Math.Min(unit.HP,loss);
    }
    static int ThreatAt(AIBoardState board, Status unit, Vector3 point)
    {
        if (unit == null) return 0;
        long damage = board.EstimateCounterDamageAt(point, unit);
        // The shared counter query already contains the observed reservations.
        return (int)Math.Min(int.MaxValue, damage);
    }
    static float RiskAt(AIBoardState board, Status unit, Vector3 point)
        => HpCost(unit, ThreatAt(board, unit, point));
    static float AttackValueAt(AIBoardState board, Status unit, Vector3 point)
    {
        float best = 0;
        foreach (var target in board.AlivePlayerUnits)
        {
            if (!board.IsSpecialActorObserved(target) || !AttackPatterns.CanAttack(unit, unit.direction,
                target.transform.position.x - point.x, target.transform.position.z - point.z)) continue;
            best = Mathf.Max(best, DamageValue(board, target, EstimateObservedNormalPacket(unit,target,board,
                EstimateSpecialAwareAttack(unit,target,board)), HpChangeKind.NormalDamage));
        }
        return best;
    }
    public static bool CanSafelyPreserveStationary(Status unit,AIBoardState board)
    {
        if(unit==null||board==null||EstimateSpecialReservedThreatAt(board,unit,unit.transform.position)>0)return false;
        foreach(var opponent in board.AlivePlayerUnits)
        {
            if(!board.IsSpecialActorObserved(opponent))continue;
            if(ObservedDragonThreat.IsDragon(opponent))
            {if(ObservedDragonThreat.EstimateBreathDamage(board,opponent,unit,unit.transform.position)>0)return false;continue;}
            if(opponent.Special?.ritualTargetId==unit.ReflectionLifeId||opponent.Special?.sniperTargetId==unit.ReflectionLifeId)return false;
            if(opponent.Special!=null&&!string.IsNullOrEmpty(opponent.Special.bombCasterId)
                &&opponent.Special.bombDueRound<=board.TurnCount+1
                &&GridHelper.ChebyshevDistance(unit.transform.position,opponent.transform.position)<=opponent.Special.bombRadius)return false;
            if(SpecialSkillRuntime.AttackBlocked(opponent)||StatusEffectSystem.IsStunned(opponent))continue;
            var profile=BoardActionProfile.For(opponent);if(profile?.CanAttack==false)continue;
            Vector3 delta=unit.transform.position-opponent.transform.position;
            if(AttackPatterns.CanAttack(opponent,opponent.direction,delta.x,delta.z))return false;
            if(opponent.type!=Type.Unit||profile?.CanMove==false||StatusEffectSystem.IsMovementBlocked(opponent)
                ||SpecialSkillRuntime.MovementBlocked(opponent))continue;
            float range=AIBoardQuery.EstimateAttackRange(opponent)+1.5f;
            if(delta.sqrMagnitude<=range*range)return false;
        }
        return true;
    }
    public static float CalcSpecialSkillScore(AIAction action, AIBoardState board)
    {
        var command = action?.SpecialCommand;
        var unit = command?.Caster;
        var target = command?.Target;
        var definition = SpecialSkillRuntime.Definition(unit);
        if (board == null || definition == null || !board.IsSpecialActorObserved(unit)
            || !SpecialSkillRuntime.CanUse(unit, board.SpecialSystems ?? unit.RuntimeSystems, command.BerserkerStage, out _)
            || !SpecialSkillRuntime.IsLegalTarget(unit, target, out _)) return -10000;
        float reward = 0, future = 0, cost = 0, opportunity = Mathf.Max(0, action.APCost) * 1.5f;
        switch (definition.ability)
        {
            case UnitSpecialSkill.HeavyWarriorBreak:
                reward = DamageValue(board, target, EstimateObservedDedicatedPacket(unit,target,board), HpChangeKind.NormalDamage);
                opportunity += AttackValueAt(board, unit, unit.transform.position) * .25f;
                break;
            case UnitSpecialSkill.DragonKnightPierce:
                if ((unit.Special?.dragonKnightGauge ?? 0) < 2) return -10000;
                reward = DamageValue(board, target, EstimateObservedDedicatedPacket(unit,target,board), HpChangeKind.BarrierBypassDamage);
                opportunity += AttackValueAt(board, unit, unit.transform.position) * .25f;
                break;
            case UnitSpecialSkill.ArcherStationary:
                foreach (var opponent in board.SpecialObservedActors)
                {
                    if (opponent.team == unit.team || !NeutralFactionSystem.AreHostile(unit, opponent)
                        || GridHelper.ChebyshevDistance(unit.transform.position, opponent.transform.position) > 2) continue;
                    reward += DamageValue(board, opponent, GameConstants.PoisonDamagePerTurn * SpecialSkillDefinition.ArcherEscapeDuration,
                        HpChangeKind.IgnoreDefenceDamage) * AISpecialSkillKnowledge.FutureWeight(board, 2);
                    // Hidden Archer benefits itself; the same snapshot gives the hostile 20% ATK
                    // against every other ally, not just against this caster.
                    reward += HpCost(unit, EstimateSpecialAwareAttack(opponent, unit, board)) * .5f;
                    foreach (var ally in board.AliveEnemyUnits)
                    {
                        if (ally == unit || !ObservedCouldAttack(board, opponent, ally.transform.position, ally)) continue;
                        int raw = EstimateSpecialAwareAttack(opponent, ally, board);
                        cost += HpCost(ally, Mathf.CeilToInt(raw * (SpecialSkillDefinition.ArcherEnemyAttackBoost - 1)))
                            * SpecialSkillDefinition.ArcherEscapeDuration;
                    }
                }
                opportunity += AttackValueAt(board, unit, unit.transform.position) * SpecialSkillDefinition.ArcherEscapeDuration;
                break;
            case UnitSpecialSkill.MagicRitual:
                if (command.Sacrifices == null || command.Sacrifices.Count == 0) return -10000;
                long total = 0;
                foreach (var ally in command.Sacrifices)
                {
                    if (!SpecialSkillRuntime.IsLegalSacrifice(unit, ally)) return -10000;
                    int loss = Mathf.Min(ally.HP, SpecialSkillRuntime.FractionOfMaximum(ally, SpecialSkillDefinition.RitualSacrifice));
                    total += loss; cost += HpCost(ally, loss);
                }
                future = DamageValue(board,target,(int)Math.Min(int.MaxValue,total),HpChangeKind.IgnoreDefenceDamage)
                    * AISpecialSkillKnowledge.FutureWeight(board,1);
                cost += RiskAt(board,unit,unit.transform.position);
                opportunity += AttackValueAt(board,unit,unit.transform.position) * .25f;
                break;
            case UnitSpecialSkill.AssassinVisionSeal:
                reward = target.Special?.visionZeroTurns > 0 ? 0 : SpecialValue(target) * .35f
                    + Mathf.Min(20, target.VisionCell?.Count ?? 0) * .4f;
                if (AIPlayerModel.IsRanged(target.kind) || MatchObjectiveRules.IsCommander(target)) reward += 12;
                cost = RiskAt(board,unit,unit.transform.position) * 2;
                break;
            case UnitSpecialSkill.ScoutVisionGuard:
                foreach (var ally in board.AliveEnemyUnits)
                    if (ally != null && SpecialSkillRuntime.InVision(unit,ally) && !(ally.Special?.damageNullUnused ?? false))
                        reward += HpCost(ally,ThreatAt(board,ally,ally.transform.position));
                cost = Mathf.Min(20, unit.VisionCell?.Count ?? 0) * .2f;
                break;
            case UnitSpecialSkill.PriestPurification:
                foreach (var actor in board.SpecialObservedActors)
                {
                    if (actor == unit || GridHelper.ChebyshevDistance(unit.transform.position,actor.transform.position)>2) continue;
                    int buffs=0,debuffs=0;
                    foreach (var effect in actor.ActiveEffects) { if (effect.buffType != BuffType.None) buffs++; if(effect.debuffType != StatusEffectType.None) debuffs++; }
                    var state=actor.Special;
                    if(state!=null) { if(state.nextDamageDouble)debuffs+=2;if(state.visionZeroTurns>0)debuffs++;if(state.damageNullUnused)buffs++; }
                    reward += (actor.team==board.ActorTeam ? debuffs-buffs : buffs-debuffs)*SpecialValue(actor)*.2f;
                }
                cost = HpCost(unit,unit.HP);
                break;
            case UnitSpecialSkill.GuardianLifeLink:
                int targetDanger=ThreatAt(board,target,target.transform.position), ownDanger=ThreatAt(board,unit,unit.transform.position);
                reward = HpCost(target,targetDanger) + HpCost(unit,ownDanger);
                cost = HpCost(unit,targetDanger >= target.HP ? unit.HP : targetDanger)
                    + HpCost(target,ownDanger >= unit.HP ? target.HP : ownDanger);
                if(MatchObjectiveRules.IsDecisive(target) && targetDanger>0 && ownDanger<unit.HP)reward+=40;
                break;
            case UnitSpecialSkill.CrossbowStakes:
                future=ProspectiveReloadValue(board,unit,definition,action.APCost);
                // Strategy/personality bonuses must not make an impossible or harmful line desirable.
                if(future<=opportunity)return -10000;
                break;
            case UnitSpecialSkill.MagicSniperLock:
                float distance=GridHelper.ChebyshevDistance(unit.transform.position,target.transform.position);
                int sniperDamage=(int)Math.Min(int.MaxValue, EstimateSpecialAwareAttack(unit,target,board)*(double)definition.SniperMultiplier(distance));
                future=DamageValue(board,target,sniperDamage,HpChangeKind.NormalDamage)*AISpecialSkillKnowledge.FutureWeight(board,1);
                int recoil=ObservedSniperRecoilLoss(unit,board,definition,distance,true);
                cost=HpCost(unit,recoil)+RiskAt(board,unit,unit.transform.position);
                opportunity+=Mathf.Max(0,AttackValueAt(board,unit,unit.transform.position)-future)*.5f;
                if(recoil>=unit.HP && !MatchObjectiveRules.IsDecisive(target))cost+=SpecialValue(unit);
                break;
            case UnitSpecialSkill.LifeBomb:
                int bombPower=SpecialSkillRuntime.FractionOfMaximum(target,SpecialSkillDefinition.BombFraction);
                future=BombValueAt(board,target,target.transform.position,bombPower,definition.bombExplosionRadius,definition.bombFactionMask);
                if(board.ReconThreatLevel>=31)
                    foreach(var enemy in board.AlivePlayerUnits)
                        if(board.IsSpecialActorObserved(enemy) && GridHelper.ChebyshevDistance(target.transform.position,enemy.transform.position)<=SpecialSkillDefinition.BombDelay)
                            future=Mathf.Max(future,BombValueAt(board,target,enemy.transform.position,bombPower,definition.bombExplosionRadius,definition.bombFactionMask)*.4f);
                future*=AISpecialSkillKnowledge.FutureWeight(board,SpecialSkillDefinition.BombDelay);
                cost=HpCost(target,bombPower);
                break;
            case UnitSpecialSkill.WhiteMageHeal:
                int healing=Mathf.Min(target.MaxHP-target.HP,SpecialSkillRuntime.FractionOfMaximum(target,SpecialSkillDefinition.WhiteMageHealFraction));
                float healReadiness=Mathf.Clamp01(healing/Mathf.Max(1f,target.MaxHP*SpecialSkillDefinition.WhiteMageHealFraction));
                float missingHealth=1-target.HPRatio;
                // Use the existing healing-score scale, prorated to the HP actually restored.
                reward=(25+missingHealth*35+(target.HPRatio<.3f?15:0))*healReadiness;
                reward+=SpecialValue(target)*healing/Mathf.Max(1f,target.MaxHP);
                int incoming=ThreatAt(board,target,target.transform.position);
                if(incoming>=target.HP&&(long)target.HP+healing>incoming)reward+=25+SpecialValue(target);
                opportunity=Mathf.Max(0,action.APCost-4)*1.5f; // Same AP opportunity baseline as legacy heal evaluation.
                if(healing<=0||reward<=opportunity)return -10000;
                break;
            case UnitSpecialSkill.HolyKnightSanctuary:
                foreach(var ally in board.AliveEnemyUnits)
                {
                    if(ally==null)continue;var delta=GridHelper.ToGridXZ(ally.transform.position-unit.transform.position);
                    foreach(var offset in definition.holyKnightArea)if(delta.x==offset.x&&delta.z==offset.y)
                    { future+=SpecialValue(ally)*Mathf.Min(ally.MaxHP-ally.HP,SpecialSkillRuntime.FractionOfMaximum(ally,SpecialSkillDefinition.HolyKnightHealFraction)*3)/Mathf.Max(1f,ally.MaxHP);break; }
                }
                future*=AISpecialSkillKnowledge.FutureWeight(board,3);
                cost=RiskAt(board,unit,unit.transform.position)*2;
                opportunity+=AttackValueAt(board,unit,unit.transform.position);
                break;
            case UnitSpecialSkill.NinjaSwap:
                reward=RiskAt(board,unit,unit.transform.position)+RiskAt(board,target,target.transform.position)
                    -RiskAt(board,unit,target.transform.position)-RiskAt(board,target,unit.transform.position);
                future=(AttackValueAt(board,unit,target.transform.position)+AttackValueAt(board,target,unit.transform.position)
                    -AttackValueAt(board,unit,unit.transform.position)-AttackValueAt(board,target,target.transform.position))*.5f;
                reward += KnownForcedEntryValue(board, unit, unit.transform.position, target.transform.position)
                    + KnownForcedEntryValue(board, target, target.transform.position, unit.transform.position);
                if(board.ReconThreatLevel>=31 && target.Special!=null&&!string.IsNullOrEmpty(target.Special.bombCasterId))
                {int power=SpecialSkillRuntime.FractionOfMaximum(target,SpecialSkillDefinition.BombFraction);future+=BombValueAt(board,target,unit.transform.position,power,target.Special.bombRadius,target.Special.bombMask)-BombValueAt(board,target,target.transform.position,power,target.Special.bombRadius,target.Special.bombMask);}
                break;
            case UnitSpecialSkill.BerserkerRage:
                int stage=Mathf.Clamp(unit.Special?.berserkerStage??0,0,5)+1;
                if(stage>5||command.BerserkerStage!=0&&command.BerserkerStage!=stage)return -10000;
                float next=Mathf.Clamp(definition.berserkerAttackCurve.Evaluate(stage),1,SpecialSkillDefinition.BerserkerMaximum);
                float current=SpecialSkillRuntime.GetAttackMultiplier(unit,target);
                future=AttackValueAt(board,unit,unit.transform.position)*Mathf.Max(0,next/Mathf.Max(1,current)-1);
                if (future <= 0)
                    future = BestReachableAttackValue(board, unit) * Mathf.Max(0, next / Mathf.Max(1, current) - 1)
                        * AISpecialSkillKnowledge.FutureWeight(board, 1);
                cost=BarrierCombatSettings.Active.enabledForPlay?0:RiskAt(board,unit,unit.transform.position)*(1-Mathf.Clamp01(definition.berserkerDefenceCurve.Evaluate(stage)));
                break;
            case UnitSpecialSkill.NavigatorPull:
                Vector3 front=unit.transform.position+new Vector3(0,0,MovePatterns.DirZ(unit.direction));
                reward=SpecialValue(target)*.2f;
                reward += KnownForcedEntryValue(board, target, target.transform.position, front);
                foreach(var ally in board.AliveEnemyUnits)if(ally!=null)
                    if(AttackPatterns.CanAttack(ally,ally.direction,front.x-ally.transform.position.x,front.z-ally.transform.position.z))future+=8;
                cost=RiskAt(board,unit,front)*.25f;
                break;
            case UnitSpecialSkill.KnightBreakthrough:
                reward = KnightProjectionValue(board, unit, out var advance);
                reward += AttackValueAt(board,unit,advance)-AttackValueAt(board,unit,unit.transform.position);
                cost=Mathf.Max(0,RiskAt(board,unit,advance)-RiskAt(board,unit,unit.transform.position));
                break;
            case UnitSpecialSkill.SamuraiCounter:
                int retaliation = EstimateObservedNormalPacket(target, unit,board, EstimateSpecialAwareAttack(target, unit, board));
                var tauntBudget = AIObservedDamageBudget.Capture(unit);
                long tauntHpLoss = 0;
                for (int turn = 0; turn < SpecialSkillDefinition.SamuraiTauntDuration; turn++)
                    tauntHpLoss += EstimateObservedHitLoss(unit,board,retaliation,HpChangeKind.NormalDamage,ref tauntBudget);
                cost = HpCost(unit, (int)Math.Min(int.MaxValue,tauntHpLoss));
                foreach (var ally in board.AliveEnemyUnits)
                    if (ally != unit && ObservedCouldAttack(board, target, ally.transform.position, ally))
                        reward += HpCost(ally, EstimateObservedHitLoss(ally,board,
                            EstimateSpecialAwareAttack(target, ally, board),HpChangeKind.NormalDamage));
                if (SpecialSkillRuntime.PreviewDamage(unit,retaliation)>0
                    && SpecialSkillRuntime.PreviewHpDamage(unit,retaliation) < unit.HP)
                {
                    int firstReceived=(int)Math.Min(int.MaxValue,Math.Min(SpecialSkillRuntime.PreviewDamage(unit,retaliation),(long)unit.HP+unit.Barrier));
                    reward += CounterDamageValue(board,target,(int)Math.Min(int.MaxValue,(long)unit.ATK
                        + Mathf.RoundToInt(firstReceived * SpecialSkillDefinition.SamuraiCounterFraction)));
                }
                break;
        }
        float score=reward+future-cost-opportunity;
        score = float.IsNaN(score)||float.IsInfinity(score)?-10000:score;
        if (CombatRuleTelemetry.Enabled) CombatRuleTelemetry.Record(score > 0 ? "ai_skill_candidate" : "ai_skill_reject", unit, target,
            definition.ability.ToString(), candidateValue:score);
        return score;
    }
    const int MaximumProspectiveStakeTargets=8;
    static readonly List<Status> prospectiveStakeTargets=new List<Status>(MaximumProspectiveStakeTargets);
    static readonly List<Status> prospectiveReloadTargets=new List<Status>(MaximumProspectiveStakeTargets);
    static readonly Dictionary<Status,int> prospectiveLightningDamage=new Dictionary<Status,int>();

    static bool CanProjectStakeShot(AIBoardState board, Status caster, Status target)
    {
        if (target == null || target.type != Type.Unit || !board.IsSpecialActorObserved(target)
            || !NeutralFactionSystem.AreHostile(caster, target) || !SpecialSkillRuntime.InVision(caster, target)
            || SpecialSkillRuntime.IsStrongTarget(target)) return false;
        Vector3 difference = target.transform.position - caster.transform.position;
        bool reachable = AttackPatterns.CanAttack(caster, caster.direction, difference.x, difference.z);
        if (!reachable && BoardActionProfile.For(caster)?.CanMove != false && !SpecialSkillRuntime.MovementBlocked(caster))
        {
            var offsets = MovePatterns.Offsets(caster);
            int direction = MovePatterns.IsDirectionIndependent(caster) ? 1 : MovePatterns.DirZ(caster.direction);
            for (int i = 0; i < Mathf.Min(16, offsets.Count); i++)
            {
                Vector3 next = caster.transform.position + new Vector3(offsets[i].x, 0, offsets[i].y * direction);
                if (board.IsTerrainKnown(next) && board.ReconMap != null && board.ReconMap.CanTraverse(caster.transform.position, next)
                    && AttackPatterns.CanAttack(caster, caster.direction, target.transform.position.x - next.x, target.transform.position.z - next.z))
                { reachable = true; break; }
            }
        }
        if (!reachable) return false;
        int damage = EstimateEffectiveSpecialDamage(caster, target, board, out var victim);
        return damage > 0 && (victim != target || damage < target.HP);
    }
    static float ProspectiveReloadValue(AIBoardState board, Status caster, SpecialSkillDefinition definition, int apCost)
    {
        prospectiveReloadTargets.Clear(); prospectiveLightningDamage.Clear();
        try
        {
            foreach (var actor in board.SpecialObservedActors)
            {
                if (!CanProjectStakeShot(board, caster, actor)) continue;
                int insert = 0;
                while (insert < prospectiveReloadTargets.Count && CompareProspectiveStakeTargets(prospectiveReloadTargets[insert], actor) <= 0) insert++;
                if (insert >= MaximumProspectiveStakeTargets) continue;
                prospectiveReloadTargets.Insert(insert, actor);
                if (prospectiveReloadTargets.Count > MaximumProspectiveStakeTargets) prospectiveReloadTargets.RemoveAt(MaximumProspectiveStakeTargets);
            }
            float best = float.NegativeInfinity;
            foreach (var actor in prospectiveReloadTargets)
                best = Mathf.Max(best, ProspectiveStakeValueCore(board, caster, actor, definition, apCost));
            int count = SpecialSkillRuntime.CrossbowAmmoCount(caster), normal = 0;
            foreach (var round in SpecialSkillRuntime.CrossbowMagazine(caster)) if (round == CrossbowRoundType.Normal) normal++;
            if (count >= 3) best *= normal / 3f; // Replay-stable conversion is uncertain to planning, never all three rounds.
            float shot = count == 0 ? AttackValueAt(board, caster, caster.transform.position) : 0;
            // Reserve the ordinary attack required to deliver this loaded round.
            float deliveryCost = Mathf.Max(0, board.CalcAttackCost(caster)) * 1.5f;
            return Mathf.Max(shot, best) * AISpecialSkillKnowledge.FutureWeight(board, count == 0 ? 1 : 2) - deliveryCost;
        }
        finally { prospectiveReloadTargets.Clear(); prospectiveStakeTargets.Clear(); prospectiveLightningDamage.Clear(); }
    }

    static Status ObservedLiveStake(AIBoardState board,string id,int appliedTurn,int ownerTurn)
        => string.IsNullOrEmpty(id)||(long)ownerTurn-appliedTurn>=SpecialSkillDefinition.StakeLifetime
            ?null:board.FindObservedSpecialActor(id);

    static float ProspectiveStakeValue(AIBoardState board,Status caster,Status proposed,SpecialSkillDefinition definition,int apCost)
    {
        prospectiveStakeTargets.Clear();prospectiveLightningDamage.Clear();
        try{return ProspectiveStakeValueCore(board,caster,proposed,definition,apCost);}
        finally{prospectiveStakeTargets.Clear();prospectiveLightningDamage.Clear();}
    }

    static float ProspectiveStakeValueCore(AIBoardState board,Status caster,Status proposed,SpecialSkillDefinition definition,int apCost)
    {
        var state=SpecialSkillRuntime.State(caster);
        var oldA=ObservedLiveStake(board,state.stakeAId,state.stakeAAppliedTurn,state.ownerTurn);
        var oldB=ObservedLiveStake(board,state.stakeBId,state.stakeBAppliedTurn,state.ownerTurn);
        bool retainedB=!string.IsNullOrEmpty(state.stakeBId)&&(long)state.ownerTurn-state.stakeBAppliedTurn<SpecialSkillDefinition.StakeLifetime;
        bool retainedA=!string.IsNullOrEmpty(state.stakeAId)&&(long)state.ownerTurn-state.stakeAAppliedTurn<SpecialSkillDefinition.StakeLifetime;
        // A retained but hidden endpoint cannot supply its current geometry to this forecast.
        if(retainedB&&oldB==null||!retainedB&&retainedA&&oldA==null)return float.NegativeInfinity;
        // Adding a third stake keeps B's original age; an expired A promotes B unchanged.
        var anchor=oldB!=null?oldB:oldA;
        int applied=oldB!=null?state.stakeBAppliedTurn:state.stakeAAppliedTurn;
        if(anchor!=null)
        {
            float value=ProjectedLightningValue(board,caster,anchor,proposed,definition,
                Mathf.Max(0,state.ownerTurn-applied),0);
            if(oldA!=null&&oldB!=null)
                value-=ProjectedLightningValue(board,caster,oldA,oldB,definition,
                    Mathf.Max(0,state.ownerTurn-state.stakeAAppliedTurn),0);
            return value;
        }

        int delay=Mathf.Max(0,definition.cooldown);
        int nextPlacementCost = Mathf.Max(0, definition.apCost) + Mathf.Max(0, board.CalcAttackCost(caster));
        if((long)board.EnemyAP<(long)Mathf.Max(0,apCost)+nextPlacementCost)delay=Mathf.Max(1,delay);
        if(delay>=SpecialSkillDefinition.StakeLifetime)return float.NegativeInfinity;
        var systems=board.SpecialSystems??caster.RuntimeSystems;
        if(delay>0&&systems?.FactionState?.GetNation(caster.team)?.AP.Maximum<nextPlacementCost)return float.NegativeInfinity;
        prospectiveStakeTargets.Clear();
        try
        {
            // Keep the best eight visible legal alternatives without enumerating target subsets.
            foreach(var candidate in board.SpecialObservedActors)
            {
                if(candidate==proposed||!CanProjectStakeShot(board,caster,candidate))continue;
                int index=0;
                while(index<prospectiveStakeTargets.Count&&CompareProspectiveStakeTargets(prospectiveStakeTargets[index],candidate)<=0)index++;
                if(index>=MaximumProspectiveStakeTargets)continue;
                prospectiveStakeTargets.Insert(index,candidate);
                if(prospectiveStakeTargets.Count>MaximumProspectiveStakeTargets)prospectiveStakeTargets.RemoveAt(MaximumProspectiveStakeTargets);
            }
            float best=float.NegativeInfinity;
            foreach(var second in prospectiveStakeTargets)
            {
                float value=ProjectedLightningValue(board,caster,proposed,second,definition,delay,delay);
                // Current placement is charged by the common scorer; reserve the second placement here.
                value-=nextPlacementCost*1.5f;
                best=Mathf.Max(best,value);
            }
            return best;
        }
        finally{prospectiveStakeTargets.Clear();}
    }

    static int CompareProspectiveStakeTargets(Status a,Status b)
    {
        int value=SpecialValue(b).CompareTo(SpecialValue(a));
        return value!=0?value:a.GetInstanceID().CompareTo(b.GetInstanceID());
    }

    static float ProjectedLightningValue(AIBoardState board,Status caster,Status a,Status b,
        SpecialSkillDefinition definition,int anchorAge,int delay)
    {
        if(a==null||b==null||a==b||anchorAge>=SpecialSkillDefinition.StakeLifetime)return 0;
        float value=0;
        foreach(var actor in board.SpecialObservedActors)
        {
            if(!board.IsSpecialActorObserved(actor))continue;
            bool ally=actor.team==caster.team;
            if(ally?(definition.lightningFactionMask&SpecialFactionMask.Allies)==0
                :(definition.lightningFactionMask&SpecialFactionMask.Hostile)==0||!NeutralFactionSystem.AreHostile(caster,actor))continue;
            float exposure = SpecialSkillRuntime.SegmentTouchesCell(a.transform.position,b.transform.position,actor.GridPosition) ? 1 : 0;
            exposure += FutureLineCrossingShare(board, actor, a.transform.position, b.transform.position) * .35f;
            if (exposure <= 0) continue;
            if(!prospectiveLightningDamage.TryGetValue(actor,out int normal))
                prospectiveLightningDamage[actor]=normal=EstimateSpecialAwareAttack(caster,actor,board);
            double cumulative=0;
            for(int step=0;anchorAge+step<SpecialSkillDefinition.StakeLifetime;step++)
            {
                // Later exposure is less certain because observed endpoints and victims can move.
                double retention=Math.Pow(.65,step);
                cumulative+=Math.Floor(normal*(double)SpecialSkillDefinition.StakeMultiplier(anchorAge+step+1))
                    *retention*AISpecialSkillKnowledge.FutureWeight(board,delay+step);
            }
            int projected=(int)Math.Min(int.MaxValue,Math.Max(0,Math.Floor(cumulative * exposure)));
            float impact=DamageValue(board,actor,projected,HpChangeKind.NormalDamage);
            value+=ally?-impact:impact;
        }
        return value + KnownLineRouteValue(board, a.transform.position, b.transform.position)
            * AISpecialSkillKnowledge.FutureWeight(board, delay + 1);
    }
    static float BombValueAt(AIBoardState board,Status holder,Vector3 origin,int damage,int radius,SpecialFactionMask mask)
    {
        if(radius<0)return 0;float value=0;
        foreach(var actor in board.SpecialObservedActors)
        {
            if(actor==holder||GridHelper.ChebyshevDistance(origin,actor.transform.position)>radius)continue;
            if(actor.team==holder.team) {if((mask&SpecialFactionMask.Allies)!=0)value-=HpCost(actor,damage);}
            else if((mask&SpecialFactionMask.Hostile)!=0)value+=DamageValue(board,actor,damage,HpChangeKind.IgnoreDefenceDamage);
        }
        if((mask&SpecialFactionMask.Holder)!=0)value-=HpCost(holder,damage);
        return value;
    }
    public static float CalcSpecialStateAdjustment(AIAction action,AIBoardState board)
    {
        if(action?.Unit==null||board==null)return 0;
        if(!board.HasObservedSpecialState&&!AISpecialSkillKnowledge.HasSpecial(action.Unit)&&!AISpecialSkillKnowledge.HasSpecial(action.TargetUnit))return 0;
        var unit=action.Unit;var definition=SpecialSkillRuntime.Definition(unit);var state=unit.Special;
        float score=0;
        bool movement=action.ActionType==AIActionType.Move||action.ActionType==AIActionType.Retreat
            ||action.ActionType==AIActionType.Support||action.ActionType==AIActionType.Surround||action.ActionType==AIActionType.DefenseRepos;
        action.PreserveSpecialEvaluation=board.HasObservedSpecialState&&(movement||action.ActionType==AIActionType.Attack||action.ActionType==AIActionType.Wait);
        if (SpecialSkillRuntime.HasAssassinPassive(unit))
        {
            Vector3 finalPosition = movement ? action.TargetPos : unit.transform.position;
            if (!board.HasOtherAllyVisionAt(unit, finalPosition))
                score -= HpCost(unit, SpecialSkillRuntime.AssassinIsolationDamage(unit));
            action.PreserveSpecialEvaluation = true;
        }
        if(movement)
        {
            score-=HpCost(unit,board.EstimateSpecialHazardAt(unit,action.TargetPos));
            score+=(RiskAt(board,unit,unit.transform.position)-RiskAt(board,unit,action.TargetPos))*.25f;
            score += PassivePositionValue(board, unit, action.TargetPos) - PassivePositionValue(board, unit, unit.transform.position);
            if (definition?.enabledForPlay == true && definition.ability == UnitSpecialSkill.LifeBomb
                && state != null && state.bomberIdleTurns >= SpecialSkillDefinition.BomberIdleLimit - 2
                && definition.BomberAutoRadius >= 0)
            {
                int power = (int)Math.Min(int.MaxValue, 2L * Mathf.Max(0, unit.HP));
                var everyone = SpecialFactionMask.Allies | SpecialFactionMask.Hostile;
                score += (BombValueAt(board, unit, action.TargetPos, power, definition.BomberAutoRadius, everyone)
                    - BombValueAt(board, unit, unit.transform.position, power, definition.BomberAutoRadius, everyone))
                    * AISpecialSkillKnowledge.FutureWeight(board, SpecialSkillDefinition.BomberIdleLimit - state.bomberIdleTurns);
            }
            if (state != null && !string.IsNullOrEmpty(state.bombCasterId) && state.bombRadius >= 0)
            {
                int power = SpecialSkillRuntime.FractionOfMaximum(unit, SpecialSkillDefinition.BombFraction);
                score += (BombValueAt(board, unit, action.TargetPos, power, state.bombRadius, state.bombMask)
                    - BombValueAt(board, unit, unit.transform.position, power, state.bombRadius, state.bombMask))
                    * AISpecialSkillKnowledge.FutureWeight(board, Mathf.Max(1, state.bombDueRound - board.TurnCount));
            }
            if(definition?.ability==UnitSpecialSkill.ArcherStationary)
            {
                float charge=definition.ArcherMultiplier(state?.stationaryTurns??0)-1;
                if(AttackValueAt(board,unit,unit.transform.position)>0)score-=charge*45+12;
            }
            foreach(var enemy in board.AlivePlayerUnits)
            {
                if(!board.IsSpecialActorObserved(enemy))continue;
                var enemyDefinition=SpecialSkillRuntime.Definition(enemy);
                if(enemyDefinition?.ability==UnitSpecialSkill.MagicSniperLock && enemy.Special?.sniperTargetId==unit.ReflectionLifeId)
                    score+=(GridHelper.ChebyshevDistance(unit.transform.position,enemy.transform.position)-GridHelper.ChebyshevDistance(action.TargetPos,enemy.transform.position))*3;
            }
        }
        if (action.ActionType == AIActionType.Wait) score += PassivePositionValue(board, unit, unit.transform.position);
        if(action.ActionType==AIActionType.Wait&&definition?.ability==UnitSpecialSkill.ArcherStationary)
        {
            float shot=AttackValueAt(board,unit,unit.transform.position);
            if(shot>0&&CanSafelyPreserveStationary(unit,board))
                score+=20+(definition.ArcherMultiplier(state?.stationaryTurns??0)-1)*45
                    +(definition.ArcherMultiplier((state?.stationaryTurns??0)+1)-definition.ArcherMultiplier(state?.stationaryTurns??0))*45;
        }
        if(action.ActionType==AIActionType.Attack&&action.TargetUnit!=null)
        {
            var target=action.TargetUnit;
            if (target.team == board.ActorTeam) return 0; // Friendly loss is the complete base valuation, not a hostile bonus.
            int baseDamage=EstimateObservedNormalPacket(unit,target,board,AIEvalHelpers.EstimateRawDamage(unit,target));
            int specialDamage=EstimateSpecialAwareAttack(unit,target,board);
            specialDamage=EstimateObservedNormalPacket(unit,target,board,specialDamage);
            score+=DamageValue(board,target,specialDamage,HpChangeKind.NormalDamage)-DamageValue(board,target,baseDamage,HpChangeKind.NormalDamage);
            if (definition?.enabledForPlay == true && definition.ability == UnitSpecialSkill.MagicSniperLock)
            {
                int recoil = ObservedSniperRecoilLoss(unit,board,definition,
                    GridHelper.ChebyshevDistance(unit.transform.position,target.transform.position),unit.Special?.sniperActive==true);
                score -= HpCost(unit, recoil);
            }
            if (definition?.enabledForPlay == true && definition.ability == UnitSpecialSkill.HolyKnightSanctuary
                && specialDamage < target.HP)
            {
                int stolen = Mathf.FloorToInt(Mathf.Max(0, target.ATK + board.ObservedAttackFlatAdjustment(target)) * SpecialSkillDefinition.PaladinStealFraction);
                float temporary = stolen * AISpecialSkillKnowledge.FutureWeight(board, 1);
                int returned = Mathf.FloorToInt(Mathf.Max(0, unit.ATK + board.ObservedAttackFlatAdjustment(unit) + stolen) * SpecialSkillDefinition.PaladinReturnFraction);
                float survives = Mathf.Clamp01((target.HP - specialDamage) / Mathf.Max(1f, specialDamage * 2));
                score += temporary - returned * survives * AISpecialSkillKnowledge.FutureWeight(board, 2);
            }
            if (definition?.enabledForPlay == true && definition.ability == UnitSpecialSkill.CrossbowStakes
                && SpecialSkillRuntime.CrossbowPeekRound(unit) == CrossbowRoundType.Stake && specialDamage < target.HP)
                score += ProspectiveStakeValue(board, unit, target, definition, action.APCost);
            if(target.Special?.ritualActive==true)score+=12+Mathf.Min(30,target.Special.storedSacrificeDamage*.2f);
            var enemyDefinition=SpecialSkillRuntime.Definition(target);
            if(enemyDefinition?.ability==UnitSpecialSkill.ArcherStationary)score+=(enemyDefinition.ArcherMultiplier(target.Special?.stationaryTurns??0)-1)*20;
            if(target.Special?.damageNullUnused==true&&target.Special.damageNullTurns>0&&string.IsNullOrEmpty(target.Special.guardianPartnerId))
                score+=Mathf.Max(0,10-Mathf.Max(1,action.APCost)*2)-Mathf.Min(30,specialDamage*.5f);
            bool counterReady = enemyDefinition?.ability==UnitSpecialSkill.SamuraiCounter
                && (enemyDefinition.samuraiGaugeMax>0 && target.Special?.samuraiGauge>=enemyDefinition.samuraiGaugeMax && target.Special.samuraiMaxTurns>0
                    || unit.Special?.tauntTurns>0 && unit.Special.tauntedById==target.ReflectionLifeId);
            if(counterReady && SpecialSkillRuntime.PreviewDamage(target,specialDamage)>0
                && SpecialSkillRuntime.PreviewHpDamage(target,specialDamage)<target.HP)
            {
                int received=(int)Math.Min(int.MaxValue,Math.Min(SpecialSkillRuntime.PreviewDamage(target,specialDamage),(long)target.HP+target.Barrier));
                int counter=(int)Math.Min(int.MaxValue,(long)Mathf.RoundToInt(received*SpecialSkillDefinition.SamuraiCounterFraction)+target.ATK);
                score-=CounterDamageValue(board,unit,counter);
            }
        }
        return float.IsNaN(score)||float.IsInfinity(score)?0:score;
    }

    public static bool ShouldPreserveStationary(AIAction action,List<AIAction> actions,AIBoardState board)
    {
        var unit=action?.Unit;
        if(unit==null||board==null||SpecialSkillRuntime.Definition(unit)?.ability!=UnitSpecialSkill.ArcherStationary
            ||!(action.ActionType==AIActionType.Move||action.ActionType==AIActionType.Support||action.ActionType==AIActionType.Surround||action.ActionType==AIActionType.DefenseRepos)
            ||!CanSafelyPreserveStationary(unit,board)||AttackValueAt(board,unit,unit.transform.position)<=0)return false;
        foreach(var candidate in actions)if(candidate.Unit==unit&&candidate.ActionType==AIActionType.Wait&&candidate.Score>=action.Score)return true;
        return false;
    }
}
