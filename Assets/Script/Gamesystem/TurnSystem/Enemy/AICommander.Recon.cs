using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class AICommander
{
    IEnumerator ExecuteReconnaissancePhase(TurnStats stats)
    {
        if (AITurnBudget.Expired) yield break;
        var candidates = new List<AIAction>();
        var exploredBy = new HashSet<Status>();
        int remainingAP = Mathf.Min(Mathf.Max(1, _board.EnemyAP / 3), _board.EnemyAP - AITacticalPriorities.FinishingAttackReserve(_board));
        for (int move = 0; move < 3 && remainingAP > 0; move++)
        {
            AIAction best = null;
            float bestScore = 8;
            foreach (var scout in _board.AliveEnemyUnits)
            {
                if (scout == null || !scout.IsAlive || scout.kind != Kind.Scout || exploredBy.Contains(scout) || StatusEffectSystem.IsStunned(scout)) continue;
                yield return null;
                if (AITurnBudget.Expired || _turnGen.IsGameOver) yield break;
                // Do not sacrifice an immediate finishing blow or a dying scout for exploration.
                if (scout.HP < scout.MaxHP / 3) continue;
                bool killAvailable = false;
                foreach (var target in _board.GetAttackTargets(scout))
                    if (DamageCalculator.EstimateBaseDamage(scout.ATK, target.DEF) >= target.HP) { killAvailable = true; break; }
                if (killAvailable) continue;
                candidates.Clear();
                AIActionGenerator.GenerateMoveCandidates(scout, _board, candidates);
                foreach (var action in candidates)
                {
                    if (action.APCost > remainingAP) continue;
                    if (_board.EstimateCounterDamageAt(action.TargetPos, scout) >= scout.HP / 2f) continue;
                    float value = _board.Recon.ScoreMove(scout, action.TargetPos) - action.APCost;
                    if (value <= bestScore) continue;
                    bestScore = value; best = action;
                }
            }
            if (best == null || AITurnBudget.Expired || _turnGen.IsGameOver) yield break;
            if (best.APCost > _board.EnemyAP - AITacticalPriorities.FinishingAttackReserve(_board)) yield break;
            if (_actionExecutor.Execute(best, _board))
            {
                stats.Record(best.ActionType);
                remainingAP -= best.APCost;
                exploredBy.Add(best.Unit);
                _actedUnits.Add(best.Unit);
                // Executor refreshes vision; publish discoveries before the army chooses its actions.
                _board.Refresh();
            }
            else yield break;
        }
    }

    // One reserved reinforcement; normal evaluation may recruit more if resources permit.
    IEnumerator ExecuteReinforcementPhase(TurnStats stats)
    {
        if (AITurnBudget.Expired || !_board.Recon.TryGetContactTarget(out _)) yield break;
        var candidates = new List<AIAction>();
        AIActionGenerator.GenerateSummonCandidates(_board, candidates);
        AIAction best = null;
        float bestScore = float.NegativeInfinity;
        int spendableAP = _board.EnemyAP - AITacticalPriorities.FinishingAttackReserve(_board);
        foreach (var action in candidates)
        {
            yield return null;
            if (AITurnBudget.Expired || _turnGen.IsGameOver) yield break;
            if (action.SummonKind == Kind.Scout || action.SummonKind == Kind.Priest || action.APCost > spendableAP) continue;
            float score = AIBuildEvaluator.CalcSummonBaseScore(action, _board);
            if (score > bestScore) { bestScore = score; best = action; }
        }
        if (best != null && _actionExecutor.Execute(best, _board))
        {
            stats.Record(best.ActionType);
            _board.Refresh();
        }
    }
}
