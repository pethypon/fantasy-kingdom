using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded, observation-only offensive intent shared by all units. No hidden target references.</summary>
public sealed class AIEvolutionPolicy
{
    public bool HasObjective { get; private set; }
    public Vector3 Objective { get; private set; }
    public int StartedTurn { get; private set; }
    public int Attacks { get; private set; }
    public int Rotations { get; private set; }
    public int FacilityAttacks { get; private set; }
    int objectiveId, initialArmy, economySpent, initialAP;

    public void BeginTurn(AIBoardState board)
    {
        economySpent = 0; initialAP = board.EnemyAP;
        UpdateObjective(board);
    }

    public void UpdateObjective(AIBoardState board)
    {
        bool crisis = board.EnemyCrystalMaxHP > 0 && board.EnemyCrystalHP < board.EnemyCrystalMaxHP * .4f;
        foreach (var enemy in board.AlivePlayerUnits)
            if (enemy != null && GridHelper.ChebyshevDistance(enemy.transform.position, board.EnemyCrystalPos) <= 3)
                crisis = true;
        if (crisis || board.AliveEnemyUnits.Count * 2 < initialArmy)
        { HasObjective = false; return; }
        int duration = board.ReconThreatLevel >= 10 ? 5 : 3;
        // Newly observed victory objectives can interrupt a lower-value standing order.
        // The opponent's main crystal is stored separately from AlivePlayerUnits.
        float decisiveScore = float.NegativeInfinity;
        Vector3 decisivePosition = default;
        int decisiveId = 0;
        foreach (var target in board.AlivePlayerUnits)
        {
            if (target == null || !target.IsAlive || target.team != board.OpponentTeam
                || !MatchObjectiveRules.IsCommander(target) || !board.IsSpecialActorObserved(target)) continue;
            float score = 60 - GridHelper.ChebyshevDistance(target.transform.position, board.EnemyCrystalPos);
            if (score <= decisiveScore) continue;
            decisiveScore = score; decisivePosition = target.transform.position; decisiveId = target.GetInstanceID();
        }
        if (board.CanUsePlayerCrystalAsTarget() && board.PlayerCrystalHP > 0)
        {
            float score = 60 - GridHelper.ChebyshevDistance(board.PlayerCrystalPos, board.EnemyCrystalPos);
            if (score > decisiveScore)
            { decisiveScore = score; decisivePosition = board.PlayerCrystalPos; decisiveId = int.MinValue; }
        }
        if (decisiveId != 0)
        { SetObjective(board, decisivePosition, decisiveId); return; }

        if (HasObjective && board.TurnCount - StartedTurn < duration)
        {
            foreach (var target in board.AlivePlayerUnits)
                if (target != null && target.IsAlive && target.GetInstanceID() == objectiveId && board.IsSpecialActorObserved(target))
                { Objective = target.transform.position; return; }
            // Seeing a destroyed crystal or an empty old contact invalidates the order.
            bool destroyedCrystal = objectiveId == int.MinValue && (!board.GetLastKnownPlayerCrystal().Valid
                || board.PlayerCrystalVisible && board.PlayerCrystalHP <= 0);
            if (!destroyedCrystal && board.ReconThreatLevel >= 4 && !board.IsVisibleToEnemy(Objective)) return;
        }
        HasObjective = false;
        float best = float.NegativeInfinity;
        foreach (var target in board.AlivePlayerUnits)
        {
            if (target == null || !target.IsAlive || !board.IsSpecialActorObserved(target)) continue;
            float priority = -GridHelper.ChebyshevDistance(target.transform.position, board.EnemyCrystalPos);
            // Principal opposition takes precedence over incidental neutral structures.
            if (target.team == board.OpponentTeam) priority += target.type == Type.Unit ? 25 : 12;
            if (priority <= best) continue;
            best = priority; SetObjective(board, target.transform.position, target.GetInstanceID());
        }
        if (board.ReconThreatLevel < 4) return;
        var crystal = board.GetLastKnownPlayerCrystal();
        int age = board.TurnCount - crystal.Turn;
        if (crystal.Valid && age >= 0 && age < duration && !board.IsVisibleToEnemy(crystal.Position))
        {
            float priority = 40 - GridHelper.ChebyshevDistance(crystal.Position, board.EnemyCrystalPos);
            if (priority > best) { best = priority; SetObjective(board, crystal.Position, int.MinValue, crystal.Turn); }
        }
        if (board.GetLastKnownPrincipalCommander(out var remembered, out int observedTurn))
        {
            float priority = 40 - GridHelper.ChebyshevDistance(remembered, board.EnemyCrystalPos);
            if (priority > best) SetObjective(board, remembered, int.MinValue + 1, observedTurn);
        }
    }

    void SetObjective(AIBoardState board, Vector3 position, int id, int observedTurn = -1)
    {
        HasObjective = true; Objective = position; objectiveId = id;
        StartedTurn = observedTurn >= 0 ? observedTurn : board.TurnCount;
        initialArmy = board.AliveEnemyUnits.Count;
    }

    public void Score(List<AIAction> actions, AIBoardState board, AIPlayerModel model)
    {
        int level = board.ReconThreatLevel;
        float economyFraction = level <= 3 ? .15f : level <= 6 ? .25f : .30f;
        bool minimumEconomy = EconomyHelper.CountEconBuildings(board) < 2;
        foreach (var a in actions)
        {
            if (a.ActionType == AIActionType.Build || a.ActionType == AIActionType.SubCrystal)
            {
                if (economySpent >= Mathf.CeilToInt(initialAP * economyFraction)) a.Score -= 70;
                else if (minimumEconomy && a.ActionType == AIActionType.Build) a.Score += 20;
            }
            if (a.ActionType == AIActionType.Attack && a.Unit != null && a.TargetUnit != null)
            {
                var target = a.TargetUnit;
                int damage = AIActionEvaluator.EstimateEffectiveSpecialDamage(a.Unit,target,board,out var victim);
                if (level >= 2 && victim!=null&&damage>=victim.HP) a.Score += 100;
                if (level >= 3)
                {
                    int defenders = 0;
                    foreach (var other in board.AlivePlayerUnits)
                        if (other != null && other != target && GridHelper.ChebyshevDistance(other.transform.position, target.transform.position) <= 2) defenders++;
                    if (defenders == 0) a.Score += 10;
                }
                if (level >= 5 && HasObjective && target.GetInstanceID() == objectiveId) a.Score += 18;
                if (level >= 8 && target.type != Type.Unit)
                    a.Score += target.facilityKind == FacilityKind.SubCrystal ? 35 : 22;
                // Small growth preference must never outweigh a finishing blow or lethal retaliation.
                if (level >= 3 && a.Unit.Level < GameConstants.MaxUnitLevel
                    && (long)a.Unit.Experience + Mathf.Min(damage, target.HP) >= Status.XPRequiredForLevel(a.Unit.Level + 1)
                    && board.EstimateCounterDamageAt(a.Unit.transform.position, a.Unit) < a.Unit.HP)
                    a.Score += 8;
            }
            if (a.ActionType == AIActionType.Move && a.Unit != null && HasObjective && !MatchObjectiveRules.IsCommander(a.Unit))
            {
                float progress = GridHelper.ChebyshevDistance(a.Unit.transform.position, Objective)
                    - GridHelper.ChebyshevDistance(a.TargetPos, Objective);
                if (level < 6 || board.EstimateCounterDamageAt(a.TargetPos, a.Unit) < a.Unit.HP)
                    a.Score += Mathf.Clamp(progress, -3, 3) * 10;
            }
            // Higher levels exploit observed support geometry, never predicted hidden attack targets.
            if (level >= 51 && a.Unit != null && a.ActionType == AIActionType.Retreat
                && board.EstimateCounterDamageAt(a.TargetPos, a.Unit) < a.Unit.HP / 2)
                a.Score += Mathf.Min(3, board.CountAlliesNear(a.TargetPos, a.Unit, 3)) * 4;
            if (level >= 71 && a.ActionType == AIActionType.Move && a.Unit != null && !MatchObjectiveRules.IsCommander(a.Unit))
            {
                // A second wing may pressure an observed economic target while the main force holds its objective.
                foreach (var target in board.AlivePlayerUnits)
                {
                    if (target == null || target.type == Type.Unit || target.GetInstanceID() == objectiveId) continue;
                    float progress = GridHelper.ChebyshevDistance(a.Unit.transform.position, target.transform.position)
                        - GridHelper.ChebyshevDistance(a.TargetPos, target.transform.position);
                    if (progress > 0 && board.CountAlliesNear(a.TargetPos, a.Unit, 3) > 0
                        && board.EstimateCounterDamageAt(a.TargetPos, a.Unit) < a.Unit.HP / 2)
                    { a.Score += Mathf.Min(2, progress) * 6; break; }
                }
            }
            a.Score += model?.Bonus(a, level,board) ?? 0;
        }
    }

    public void Record(AIAction action)
    {
        if (action.ActionType == AIActionType.Build || action.ActionType == AIActionType.SubCrystal)
            economySpent += action.APCost;
        if (action.ActionType == AIActionType.Rotate) Rotations++;
        if (action.ActionType == AIActionType.Attack)
        { Attacks++; if (action.TargetUnit != null && action.TargetUnit.type != Type.Unit) FacilityAttacks++; }
    }
}
