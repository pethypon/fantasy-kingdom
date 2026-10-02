using System.Collections.Generic;
using UnityEngine;

/// <summary>Evaluate alternate facing without mutating the live unit or querying hidden occupants.</summary>
public static class AIOrientation
{
    public static void AppendCandidates(AIBoardState board, List<AIAction> output)
    {
        if (board.ReconMap == null) return;
        foreach (var unit in board.AliveEnemyUnits)
        {
            if (unit == null || !unit.IsAlive || unit.type != Type.Unit
                || StatusEffectSystem.IsStunned(unit) || board.RotatedUnits.Contains(unit.GetInstanceID())) continue;
            var other = unit.direction == Direction.N ? Direction.S : Direction.N;
            float current = AttackUtility(board, unit, unit.direction);
            float alternate = AttackUtility(board, unit, other);
            bool attackAffordable = board.CalcAttackCost(unit) <= board.EnemyAP;
            float gain = attackAffordable ? alternate - current : 0;
            if (gain <= 1 && attackAffordable && current > 0) continue;
            if (gain <= 1 && !MovePatterns.DirectionIndependent.Contains(unit.kind))
            {
                gain = MoveUtility(board, unit, other) - MoveUtility(board, unit, unit.direction);
                if (gain <= 1) continue;
                gain = Mathf.Min(gain, 12);
            }
            else if (gain <= 1) continue;
            output.Add(new AIAction { ActionType = AIActionType.Rotate, Unit = unit,
                TargetDirection = other, TargetPos = unit.transform.position, APCost = 0,
                Score = 40 + gain });
        }
    }

    static float MoveUtility(AIBoardState board, Status unit, Direction facing)
    {
        Vector3 origin = unit.transform.position;
        bool contact = board.Recon.TryGetContactTarget(out var objective);
        float best = 0;
        foreach (var offset in MovePatterns.Offsets(unit.kind))
        {
            var destination = origin + new Vector3(offset.x, 0, offset.y * MovePatterns.DirZ(facing));
            if (!board.ReconMap.TryGetHeight(Mathf.RoundToInt(destination.x), Mathf.RoundToInt(destination.z), out float y)) continue;
            destination.y = y;
            if (!board.ReconMap.CanTraverse(origin, destination) || board.CalcMoveCost(unit, destination) > board.EnemyAP) continue;
            bool occupied = false;
            foreach (var ally in board.AliveEnemyUnits)
                if (ally != null && ally != unit && GridHelper.MatchXZ(ally.transform.position, GridHelper.ToGridXZ(destination))) { occupied = true; break; }
            foreach (var enemy in board.AlivePlayerUnits)
                if (enemy != null && GridHelper.MatchXZ(enemy.transform.position, GridHelper.ToGridXZ(destination))) { occupied = true; break; }
            if (occupied) continue;
            float value = contact ? (GridHelper.ChebyshevDistance(origin, objective) - GridHelper.ChebyshevDistance(destination, objective)) * 5
                : Mathf.Min(10, board.EstimateNewVisionCells(destination));
            best = Mathf.Max(best, value);
        }
        return best;
    }

    public static float AttackUtility(AIBoardState board, Status unit, Direction facing)
    {
        float best = 0;
        foreach (var target in board.AlivePlayerUnits)
        {
            if (target == null || !target.IsAlive || !board.IsVisibleToEnemy(target.transform.position)) continue;
            Vector3 offset = target.transform.position - unit.transform.position;
            if (!AttackPatterns.CanAttack(unit.kind, facing, offset.x, offset.z)
                || !board.ReconMap.CanAttackAcrossTerrain(unit, target.transform.position)) continue;
            int damage = target.ShieldTurns > 0 ? 0 : AIEvalHelpers.EstimateDamage(unit, target);
            best = Mathf.Max(best, Mathf.Min(damage, target.HP) + (damage >= target.HP ? 60 : 0));
        }
        return best;
    }
}
