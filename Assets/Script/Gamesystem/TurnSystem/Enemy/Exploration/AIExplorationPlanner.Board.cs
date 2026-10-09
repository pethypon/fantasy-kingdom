using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIExplorationPlanner
{
    readonly ExplorationObservation boardObservation = new ExplorationObservation();
    readonly Dictionary<int, Status> boardActors = new Dictionary<int, Status>();
    readonly HashSet<Vector3Int> importedTerrain = new HashSet<Vector3Int>();
    readonly HashSet<Vector3Int> moveFootprint = new HashSet<Vector3Int>();
    AIBoardState observedBoard;
    int boardGeneration = -1;
    bool initialExploredImported;
    readonly HashSet<Vector3Int> previousBoardView = new HashSet<Vector3Int>();
    readonly Dictionary<Vector3Int, ExplorationObservedCell> boardTerrainEvidence = new Dictionary<Vector3Int, ExplorationObservedCell>();
    int boardViewVersion;
    int boardCellVersion;

    /// <summary>The adapter reads public extents, own observations and cached observed threats only.</summary>
    public void Update(AIBoardState board, bool isEmergency)
    {
        if (!settings.Enabled || board == null) return;
        var map = board.MapCreate != null ? board.MapCreate : board.ReconMap;
        if (map == null) return;
        if (width > 0 && (actorTeam != board.ActorTeam || width != map.maxX || depth != map.maxZ))
        {
            ResetKnowledge(); ClearBoardCaches(); width = depth = 0; observation = null;
        }
        if (observedBoard != board || boardGeneration != board.Generation || boardObservation.Turn != board.TurnCount)
        {
            observedBoard = board; boardGeneration = board.Generation;
            boardObservation.Turn = board.TurnCount; boardObservation.Width = map.maxX; boardObservation.Depth = map.maxZ;
            boardObservation.ActorTeam = board.ActorTeam; boardObservation.OwnBase = GridHelper.ToGridXZ(board.EnemyCrystalPos);
            boardObservation.KnownCells.Clear(); boardObservation.VisibleCells.Clear(); boardObservation.Actors.Clear(); boardObservation.ImportantCells.Clear(); boardActors.Clear();
            if (!initialExploredImported)
            {
                foreach (var cell in board.OwnExploredCells) ImportObservedTerrain(map, cell);
                initialExploredImported = true;
            }
            bool viewChanged = previousBoardView.Count != board.OwnVisionCells.Count;
            foreach (var cell in board.OwnVisionCells) if (!previousBoardView.Contains(GridHelper.ToGridXZ(cell))) { viewChanged = true; break; }
            if (viewChanged) { previousBoardView.Clear(); boardViewVersion++; }
            {
                foreach (var cell in board.OwnVisionCells)
                {
                    var xz = GridHelper.ToGridXZ(cell); boardObservation.VisibleCells.Add(xz);
                    if (viewChanged) previousBoardView.Add(xz);
                    // A cell already observed can be refreshed legally; hidden terrain is never queried.
                    ImportObservedTerrain(map, cell, true);
                }
                boardObservation.ViewVersion = boardViewVersion;
                boardObservation.CellVersion = boardCellVersion;
            }
            foreach (var unit in board.AliveEnemyUnits)
            {
                if (unit == null || !unit.IsAlive || unit.team != board.ActorTeam || unit.type != Type.Unit || unit.kind != Kind.Scout) continue;
                int id = unit.GetInstanceID(); boardActors[id] = unit;
                boardObservation.Actors.Add(new ExplorationActor(id, GridHelper.ToGrid(unit.transform.position), unit.kind, unit.HP,
                    !StatusEffectSystem.IsStunned(unit) && !StatusEffectSystem.IsMovementBlocked(unit) && BoardActionProfile.For(unit)?.CanMove != false, unit.ReflectionLifeId)
                {
                    MovementOffsets = MovePatterns.Offsets(unit), MovementDirectionIndependent = MovePatterns.IsDirectionIndependent(unit), Direction = unit.direction
                });
            }
            // Own base and previously observed static objectives are legitimate long-term intelligence targets.
            boardObservation.ImportantCells[boardObservation.OwnBase] = 1f;
            foreach (var offset in neighbours)
            {
                var cell = boardObservation.OwnBase + offset;
                if (boardTerrainEvidence.TryGetValue(cell, out var evidence) && evidence.Passable) boardObservation.ImportantCells[cell] = 1f;
            }
            foreach (var entry in board.ObservedHistory)
                if (entry.Value.Valid && entry.Value.Type != Type.Unit && board.IsTerrainKnown(entry.Value.Position))
                    boardObservation.ImportantCells[GridHelper.ToGridXZ(entry.Value.Position)] = 1f;
            boardObservation.EstimateDanger = EstimateObservedDanger;
            boardObservation.DangerVersion = ObservedDangerVersion(board);
        }
        Update(boardObservation, isEmergency);
    }

    void ImportObservedTerrain(MapCreate map, Vector3Int cell, bool refresh = false)
    {
        var xz = GridHelper.ToGridXZ(cell);
        if (!refresh && importedTerrain.Contains(xz)) return;
        // This method is called only for own explored/visible cells. TryGetHeight is forbidden for unknown cells.
        bool hasHeight = map.TryGetHeight(xz.x, xz.z, out float height);
        bool passable = hasHeight && !map.IsRiver(xz.x, xz.z) && !map.IsHighMountain(xz.x, xz.z);
        var evidence = new ExplorationObservedCell(new Vector3Int(xz.x, passable ? Mathf.RoundToInt(height) : cell.y, xz.z), passable);
        boardObservation.KnownCells.Add(evidence);
        if (!boardTerrainEvidence.TryGetValue(xz, out var previous) || previous.Cell != evidence.Cell || previous.Passable != evidence.Passable)
        { boardCellVersion++; boardTerrainEvidence[xz] = evidence; }
        importedTerrain.Add(xz);
    }
    float EstimateObservedDanger(int unitId, Vector3Int destination)
        => observedBoard != null && boardActors.TryGetValue(unitId, out var unit) ? observedBoard.EstimateCounterDamageAt(destination, unit) : 0;
    static int ObservedDangerVersion(AIBoardState board)
    {
        unchecked
        {
            int version = board.AlivePlayerUnits.Count * 397;
            // These are the board's already filtered visible hostiles. No hidden transforms or global registries.
            foreach (var unit in board.AlivePlayerUnits) if (unit != null) version ^= DangerActorStamp(unit);
            foreach (var unit in board.AliveEnemyUnits)
                if (unit != null && unit.kind == Kind.Scout && unit.team == board.ActorTeam)
                    version ^= unit.GetInstanceID() * 397 ^ unit.DEF * 31;
            return version;
        }
    }
    static int DangerActorStamp(Status unit)
    {
        unchecked
        {
            int value = unit.GetInstanceID() * 397 ^ GridHelper.ToGrid(unit.transform.position).GetHashCode();
            value = value * 31 + unit.HP; value = value * 31 + unit.ATK; value = value * 31 + unit.DEF;
            value = value * 31 + unit.AssignedSkillId; value = value * 31 + unit.SkillCooldown;
            value = value * 31 + unit.ShieldTurns; value = value * 31 + (int)unit.direction;
            if (unit.ActiveEffects != null) for (int i = 0; i < unit.ActiveEffects.Count; i++)
            {
                var effect = unit.ActiveEffects[i]; if (effect == null) continue;
                value = value * 31 + (int)effect.debuffType; value = value * 31 + (int)effect.buffType; value = value * 31 + effect.remainingTurns;
            }
            var profile = BoardActionProfile.For(unit);
            if (profile != null)
            {
                value = value * 31 + profile.GetInstanceID(); value = value * 31 + (profile.CanAttack ? 1 : 0);
                if (profile.attack != null && profile.attack.useCustom)
                    foreach (var offset in profile.attack.Offsets) value = value * 31 + offset.GetHashCode();
            }
            return value;
        }
    }
    void ClearBoardCaches()
    {
        importedTerrain.Clear(); initialExploredImported = false; observedBoard = null; boardGeneration = -1;
        boardTerrainEvidence.Clear(); previousBoardView.Clear(); boardViewVersion = boardCellVersion = 0;
    }

    public bool IsAssignedUnit(Status unit) => unit != null && TryGetAssignedTarget(unit, out var _);
    public bool IsObjectiveProgress(Status unit, Vector3 destination, AIBoardState board)
    {
        if (unit == null || board == null || unit.team != board.ActorTeam || !IsAssignedUnit(unit)) return false;
        Update(board, emergency);
        return GetGoalProgress(unit.GetInstanceID(), GridHelper.ToGrid(destination)) > 0;
    }
    public bool TryGetAssignedTarget(Status unit, out Vector3 target)
    {
        if (unit != null && unit.kind == Kind.Scout && TryGetAssignedTarget(unit.GetInstanceID(), out var cell)) { target = cell; return true; }
        target = Vector3.zero; return false;
    }
    public float GetMoveBonus(Status unit, Vector3 destination, AIBoardState board)
    {
        if (unit == null || unit.kind != Kind.Scout || unit.kind == Kind.King || board == null || unit.team != board.ActorTeam || !settings.Enabled) return 0;
        Update(board, emergency);
        float score = GetMoveBonus(unit.GetInstanceID(), GridHelper.ToGrid(destination), board.EstimateCounterDamageAt(destination, unit));
        if (emergency) return score;
        moveFootprint.Clear(); var target = GridHelper.ToGridXZ(destination);
        bool blind = StatusEffectSystem.HasDebuff(unit, StatusEffectType.Blind) || StatusEffectSystem.HasDebuff(unit, StatusEffectType.NarrowVision);
        if (blind) moveFootprint.Add(target + new Vector3Int(0, 0, MovePatterns.DirZ(unit.direction)));
        else foreach (var offset in VisionGenerator.BaseVisionOffsets(unit)) moveFootprint.Add(target + GridHelper.ToGridXZ(offset));
        int gain = 0;
        foreach (var cell in moveFootprint) if (Inside(cell) && !memory.ContainsKey(cell) && KnownVisionLineClear(target, cell)) gain++;
        return score + Mathf.Min(Mathf.Max(0, settings.MaximumLocalRevealBonus), gain * Mathf.Max(0, settings.LocalRevealWeight));
    }

    bool KnownVisionLineClear(Vector3Int from, Vector3Int to)
    {
        int x = from.x, z = from.z, dx = Mathf.Abs(to.x - x), dz = Mathf.Abs(to.z - z), sx = System.Math.Sign(to.x - x), sz = System.Math.Sign(to.z - z), ix = 0, iz = 0;
        while (ix < dx || iz < dz)
        {
            int decision = (1 + 2 * ix) * dz - (1 + 2 * iz) * dx;
            if (decision == 0) { x += sx; z += sz; ix++; iz++; }
            else if (decision < 0) { x += sx; ix++; }
            else { z += sz; iz++; }
            if (x == to.x && z == to.z) break;
            var cell = new Vector3Int(x, 0, z);
            if (blocked.Contains(cell)) return false;
        }
        return true;
    }
    public void RecordAction(AIAction action, bool success, AIBoardState board)
    {
        if (action == null || action.Unit == null || action.Unit.kind != Kind.Scout || board == null) return;
        if (action.ActionType != AIActionType.Move && action.ActionType != AIActionType.Retreat && action.ActionType != AIActionType.Support
            && action.ActionType != AIActionType.DefenseRepos && action.ActionType != AIActionType.Surround) return;
        RecordAction(action.Unit.GetInstanceID(), GridHelper.ToGrid(action.TargetPos), success);
    }
}
