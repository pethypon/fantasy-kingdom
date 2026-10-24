using UnityEngine;

public static partial class AIOrientation
{
    /// <summary>A free look at a known route's next step, using only friendly masks and observed goals.</summary>
    public static bool IsVisibilityPreparation(AIAction action, AIBoardState board)
        => action != null && action.ActionType == AIActionType.Rotate && action.APCost == 0
            && CanPrepareVisibility(board, action.Unit, action.TargetDirection);

    /// <summary>A facing change that opens a currently observed legal attack without discarding a useful one.</summary>
    public static bool IsObservedAttackPreparation(AIAction action, AIBoardState board)
    {
        if (action == null || action.ActionType != AIActionType.Rotate || action.APCost != 0
            || !CanConsiderFacing(board, action.Unit, action.TargetDirection)) return false;
        var unit = action.Unit;
        return board.CalcAttackCost(unit) <= board.EnemyAP && AttackUtility(board, unit, unit.direction) <= 0
            && AttackUtility(board, unit, action.TargetDirection) > 1;
    }

    static bool CanConsiderFacing(AIBoardState board, Status unit, Direction facing)
        => board != null && board.ReconMap != null && unit != null && unit.IsAlive && unit.team == board.ActorTeam
            && unit.gameObject.activeInHierarchy && unit.type == Type.Unit && !StatusEffectSystem.IsStunned(unit)
            && (facing == Direction.N || facing == Direction.S) && facing != unit.direction
            && !board.RotatedUnits.Contains(unit.GetInstanceID());

    static bool CanPrepareVisibility(AIBoardState board, Status unit, Direction facing)
    {
        if (!CanConsiderFacing(board, unit, facing) || unit.HasMovedThisTurn || unit.Special?.visionZeroTurns > 0
            || unit.VisionCell == null || SpecialSkillRuntime.MovementBlocked(unit) || StatusEffectSystem.IsMovementBlocked(unit)) return false;
        // An immediately useful attack remains preferable to looking away.
        if (board.CalcAttackCost(unit) <= board.EnemyAP && AttackUtility(board, unit, unit.direction) > 0) return false;
        bool territory = board.TerritoryIntel.HasWaypoint && !board.TerritoryIntel.Suspended;
        bool exploration = board.Exploration.TryGetAssignedTarget(unit, out _);
        if (!territory && !exploration) return false;

        var origin = unit.transform.position;
        // The actual friendly footprint already includes blindness, radius penalties and height bonuses.
        // Do not promise a farther reveal than this observer currently supports.
        int radius = 0, originX = Mathf.RoundToInt(origin.x), originZ = Mathf.RoundToInt(origin.z);
        foreach (var cell in unit.VisionCell)
            radius = Mathf.Max(radius, Mathf.Max(Mathf.Abs(cell.x - originX), Mathf.Abs(cell.z - originZ)));
        if (radius == 0) return false;
        foreach (var destination in board.GetValidMoves(unit))
        {
            if (!board.IsTerrainKnown(destination) || board.IsVisibleToEnemy(destination)
                || GridHelper.ChebyshevDistance(origin, destination) > radius || board.CalcMoveCost(unit, destination) > board.EnemyAP) continue;
            float dx = destination.x - origin.x, dz = destination.z - origin.z;
            // Looking must leave the proposed follow-up move legal under the same authored mask.
            if (!MovePatterns.CanMove(unit, facing, dx, dz)
                || !FacingSeesOffset(unit, facing, Mathf.RoundToInt(dx), Mathf.RoundToInt(dz))
                || FacingSeesOffset(unit, unit.direction, Mathf.RoundToInt(dx), Mathf.RoundToInt(dz))) continue;
            if ((territory && board.TerritoryIntel.IsProgress(unit, destination, board))
                || (exploration && board.Exploration.IsObjectiveProgress(unit, destination, board))) return true;
        }
        return false;
    }

    static bool FacingSeesOffset(Status unit, Direction facing, int dx, int dz)
    {
        if (StatusEffectSystem.HasDebuff(unit, StatusEffectType.Blind) || StatusEffectSystem.HasDebuff(unit, StatusEffectType.NarrowVision))
            return dx == 0 && dz == MovePatterns.DirZ(facing);
        var offsets = VisionGenerator.BaseVisionOffsets(unit.kind, facing, BoardActionProfile.For(unit));
        for (int i = 0; i < offsets.Count; i++) if (offsets[i].x == dx && offsets[i].z == dz) return true;
        return false;
    }
}
