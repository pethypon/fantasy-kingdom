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
        if (HasObjective && board.TurnCount - StartedTurn < duration)
        {
            foreach (var target in board.AlivePlayerUnits)
                if (target != null && target.IsAlive && target.GetInstanceID() == objectiveId)
                { Objective = target.transform.position; return; }
            // An empty visible location invalidates a stale contact immediately.
            if (board.ReconThreatLevel >= 4 && !board.IsVisibleToEnemy(Objective)) return;
        }
        HasObjective = false;
        float best = float.NegativeInfinity;
        foreach (var target in board.AlivePlayerUnits)
        {
            if (target == null || !target.IsAlive) continue;
            float priority = -GridHelper.ChebyshevDistance(target.transform.position, board.EnemyCrystalPos);
            if (board.ReconThreatLevel >= 8 && target.type != Type.Unit) priority += 20;
            if (priority <= best) continue;
            best = priority; Objective = target.transform.position; objectiveId = target.GetInstanceID(); HasObjective = true;
        }
        if (HasObjective) { StartedTurn = board.TurnCount; initialArmy = board.AliveEnemyUnits.Count; }
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
                int damage = target.ShieldTurns > 0 ? 0 : AIEvalHelpers.EstimateDamage(a.Unit, target);
                if (level >= 2 && damage >= target.HP) a.Score += 100;
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
            if (a.ActionType == AIActionType.Move && a.Unit != null && HasObjective && a.Unit.kind != Kind.King)
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
            if (level >= 71 && a.ActionType == AIActionType.Move && a.Unit != null && a.Unit.kind != Kind.King)
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
            a.Score += model?.Bonus(a, level) ?? 0;
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
