using System.Collections.Generic;
using UnityEngine;

public static partial class AIActionGenerator
{
    const int MaximumSpecialTargets = 8;
    // Scratch belongs to one synchronous candidate generation; no subset enumeration.
    static readonly List<Status> specialTargets = new List<Status>(32);
    static readonly List<Status> ritualSacrifices = new List<Status>(32);
    static readonly List<Status> ritualOrder = new List<Status>(32);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSpecialCandidateCaches()
    { specialTargets.Clear();ritualSacrifices.Clear();ritualOrder.Clear(); }

    public static void GenerateSpecialSkillCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (board == null || results == null || !board.IsSpecialActorObserved(unit)
            || !SpecialSkillRuntime.CanUse(unit, board.SpecialSystems ?? unit.RuntimeSystems, out _)) return;
        var definition = SpecialSkillRuntime.Definition(unit);
        if (definition == null || definition.IsPassive || SpecialSkillRuntime.GetAPCost(unit) > board.EnemyAP) return;
        if (definition.ability == UnitSpecialSkill.BerserkerRage)
        {
            // One cast advances exactly one stage. The expected stage guards stale decisions.
            int stage = Mathf.Clamp(unit.Special?.berserkerStage ?? 0, 0, 5) + 1;
            if (stage <= 5 && SpecialSkillRuntime.GetAPCost(unit, stage) <= board.EnemyAP
                && SpecialSkillRuntime.CanUse(unit, board.SpecialSystems ?? unit.RuntimeSystems, stage, out _))
                AddSpecial(unit, unit, null, SpecialSkillRuntime.GetAPCost(unit, stage), results, stage);
            return;
        }
        board.CollectSpecialTargets(unit, specialTargets);
        specialTargets.Sort(CompareSpecialTargets);
        int limit = Mathf.Min(MaximumSpecialTargets, specialTargets.Count);
        if (definition.ability == UnitSpecialSkill.MagicRitual) board.CollectRitualSacrifices(unit, ritualSacrifices);
        for (int i = 0; i < limit; i++)
        {
            var target = specialTargets[i];
            if(definition.ability==UnitSpecialSkill.CrossbowStakes)
            {
                var state=unit.Special;string id=target.ReflectionLifeId;
                if(state!=null&&((state.stakeAId==id&&state.ownerTurn-state.stakeAAppliedTurn<SpecialSkillDefinition.StakeLifetime-1)
                    ||(state.stakeBId==id&&state.ownerTurn-state.stakeBAppliedTurn<SpecialSkillDefinition.StakeLifetime-1)))continue;
            }
            if (definition.ability != UnitSpecialSkill.MagicRitual)
            { AddSpecial(unit, target, null, SpecialSkillRuntime.GetAPCost(unit), results); continue; }
            GenerateRitualRepresentatives(unit, target, definition, results);
        }
        specialTargets.Clear(); ritualSacrifices.Clear(); ritualOrder.Clear();
    }

    static int CompareSpecialTargets(Status a, Status b)
    {
        float av = AIBoardEvaluator.GetPieceValuePublic(a), bv = AIBoardEvaluator.GetPieceValuePublic(b);
        if (MatchObjectiveRules.IsDecisive(a)) av += 1000;
        if (MatchObjectiveRules.IsDecisive(b)) bv += 1000;
        int value = bv.CompareTo(av);
        return value != 0 ? value : a.GetInstanceID().CompareTo(b.GetInstanceID());
    }
    static void AddSpecial(Status unit, Status target, List<Status> sacrifices, int cost, List<AIAction> results, int berserkerStage = 0)
    {
        results.Add(new AIAction { ActionType = AIActionType.SkillUse, Unit = unit, TargetUnit = target,
            TargetPos = target != null ? target.transform.position : unit.transform.position,
            APCost = cost, SpecialCommand = new SpecialSkillCommand(unit, target, sacrifices, berserkerStage) });
    }
    static int SacrificePower(Status unit) => Mathf.Min(Mathf.Max(0, unit.HP),
        SpecialSkillRuntime.FractionOfMaximum(unit, SpecialSkillDefinition.RitualSacrifice));
    static float SacrificeCost(Status unit)
    {
        float cost = AIBoardEvaluator.GetPieceValuePublic(unit) * SacrificePower(unit) / Mathf.Max(1f, unit.HP);
        if (MatchObjectiveRules.IsDecisive(unit)) cost += 1000;
        if (SacrificePower(unit) >= unit.HP) cost += AIBoardEvaluator.GetPieceValuePublic(unit);
        return cost;
    }
    static void GenerateRitualRepresentatives(Status caster, Status target, SpecialSkillDefinition definition, List<AIAction> results)
    {
        ritualOrder.Clear();
        foreach (var ally in ritualSacrifices) if (SacrificePower(ally) > 0) ritualOrder.Add(ally);
        if (ritualOrder.Count == 0) return;
        int start = results.Count;
        // Cheapest single sacrifice preserves a legal low-cost option.
        ritualOrder.Sort((a,b) => SacrificeCost(a).CompareTo(SacrificeCost(b)));
        AddRitualRepresentative(caster, target, definition, new List<Status> { ritualOrder[0] }, results, start);

        // Largest contributions first achieve a kill with the minimum number of victims.
        ritualOrder.Sort((a,b) => { int power = SacrificePower(b).CompareTo(SacrificePower(a));
            return power != 0 ? power : SacrificeCost(a).CompareTo(SacrificeCost(b)); });
        var kill = new List<Status>(); long damage = 0;
        int count = Mathf.Min(definition.sacrificeLimit, ritualOrder.Count);
        for (int i = 0; i < count && damage < target.HP; i++) { kill.Add(ritualOrder[i]); damage += SacrificePower(ritualOrder[i]); }
        if (damage >= target.HP) AddRitualRepresentative(caster, target, definition, kill, results, start);
        var maximum = new List<Status>(count);
        for (int i = 0; i < count; i++) maximum.Add(ritualOrder[i]);
        AddRitualRepresentative(caster, target, definition, maximum, results, start);

        // Do not force royals/high-value wounded support into the maximum-power branch.
        var protectedAllies = new List<Status>(count);
        ritualOrder.Sort((a,b) => SacrificeCost(a).CompareTo(SacrificeCost(b)));
        foreach (var ally in ritualOrder)
        {
            if (protectedAllies.Count >= definition.sacrificeLimit) break;
            if (!MatchObjectiveRules.IsDecisive(ally) && SacrificePower(ally) < ally.HP
                && ally.HPRatio >= .5f) protectedAllies.Add(ally);
        }
        AddRitualRepresentative(caster, target, definition, protectedAllies, results, start);
    }
    static void AddRitualRepresentative(Status caster, Status target, SpecialSkillDefinition definition,
        List<Status> victims, List<AIAction> results, int start)
    {
        if (victims.Count == 0) return;
        for (int i = start; i < results.Count; i++)
        {
            var previous = results[i].SpecialCommand?.Sacrifices;
            if (previous == null || previous.Count != victims.Count) continue;
            bool same = true;
            foreach (var victim in victims) if (!previous.Contains(victim)) { same = false; break; }
            if (same) return;
        }
        AddSpecial(caster, target, victims, SpecialSkillRuntime.GetAPCost(caster), results);
    }
}
