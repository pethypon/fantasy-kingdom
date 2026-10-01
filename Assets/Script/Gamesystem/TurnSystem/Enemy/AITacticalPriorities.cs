using UnityEngine;

/// <summary>Shared opening-phase priorities based exclusively on observed, legal targets.</summary>
public static class AITacticalPriorities
{
    /// <summary>Keep enough AP for one affordable finishing blow before optional spending.</summary>
    public static int FinishingAttackReserve(AIBoardState board)
    {
        int reserve = int.MaxValue;
        foreach (var unit in board.AliveEnemyUnits)
        {
            if (unit == null || !unit.IsAlive || unit.type != Type.Unit || StatusEffectSystem.IsStunned(unit)) continue;
            int cost = board.CalcAttackCost(unit);
            if (cost > board.EnemyAP || cost >= reserve) continue;
            foreach (var target in board.GetAttackTargets(unit))
            {
                if (target == null || !target.IsAlive || target.ShieldTurns > 0) continue;
                if (AIEvalHelpers.EstimateDamage(unit, target) < target.HP) continue;
                reserve = cost;
                break;
            }
        }
        return reserve == int.MaxValue ? 0 : reserve;
    }

    /// <summary>Remote memories guide deployment but must not freeze the economy.</summary>
    public static bool HasLocalThreat(AIBoardState board)
    {
        foreach (var target in board.AlivePlayerUnits)
        {
            if (target == null || !target.IsAlive) continue;
            if (GridHelper.ChebyshevDistance(target.transform.position, board.EnemyCrystalPos) <= 6) return true;
            foreach (var ally in board.AliveEnemyUnits)
                if (ally != null && ally.IsAlive && GridHelper.ChebyshevDistance(target.transform.position, ally.transform.position) <= 4) return true;
        }
        return false;
    }
}
