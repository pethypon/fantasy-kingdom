using System.Collections.Generic;
using UnityEngine;

public sealed partial class AIStrategicGovernor
{
    readonly List<Vector3> armyAdvanceTargets = new List<Vector3>(32);
    readonly Dictionary<(Status, Vector3Int), bool> armyAdvanceSafety = new Dictionary<(Status, Vector3Int), bool>();
    AIBoardState advanceBoard;
    int advanceGeneration = -1, advanceContentRevision = -1;

    // A healthy fighting unit may deploy alongside a scout instead of losing every
    // priority comparison to exploration. This promotes an already generated move;
    // it neither searches the map nor creates a route or an attack on a hidden actor.
    bool IsSafeArmyAdvance(AIAction action, AIBoardState board)
    {
        if (board == null || action == null || Mode == StrategicMode.EmergencyDefense || Mode == StrategicMode.EconomicRecovery
            || Mode == StrategicMode.Hold || action.ActionType != AIActionType.Move && action.ActionType != AIActionType.Surround
                && action.ActionType != AIActionType.Support) return false;
        var actor = action.Unit;
        if (actor == null || !actor.IsAlive || actor.team != board.ActorTeam || actor.type != Type.Unit
            || MatchObjectiveRules.IsCommander(actor) || actor.kind == Kind.Scout || UnitRoster.IsDedicatedHealer(actor.kind)
            || actor.MaxHP <= 0 || actor.HP < actor.MaxHP * .6f || actor.HasMovedThisTurn
            || StatusEffectSystem.IsStunned(actor) || StatusEffectSystem.IsMovementBlocked(actor)
            || SpecialSkillRuntime.MovementBlocked(actor) || SpecialSkillRuntime.AttackBlocked(actor)
            || BoardActionProfile.For(actor)?.CanMove == false || BoardActionProfile.For(actor)?.CanAttack == false
            || action.APCost < 0 || action.APCost > board.EnemyAP) return false;
        Vector3 from = actor.transform.position, destination = action.TargetPos;
        if (float.IsNaN(destination.sqrMagnitude) || float.IsInfinity(destination.sqrMagnitude)
            || GridHelper.MatchXZ(from, GridHelper.ToGridXZ(destination)) || !board.IsTerrainKnown(destination)) return false;
        PrepareArmyAdvanceTargets(board);
        if (armyAdvanceTargets.Count == 0) return false;
        var key = (actor, GridHelper.ToGrid(destination));
        if (armyAdvanceSafety.TryGetValue(key, out bool cached)) return cached;

        bool result = LegalArmyAdvance(action, board, from, destination)
            && IncomingDamage(board, actor, destination, null) < actor.HP * .25f
            && ArmyAdvanceMakesProgress(from, destination);
        armyAdvanceSafety[key] = result;
        return result;
    }

    void PrepareArmyAdvanceTargets(AIBoardState board)
    {
        if (advanceBoard == board && advanceGeneration == board.Generation && advanceContentRevision == GameContentRevision.Revision) return;
        advanceBoard = board; advanceGeneration = board.Generation; advanceContentRevision = GameContentRevision.Revision;
        armyAdvanceTargets.Clear(); armyAdvanceSafety.Clear();
        // AlivePlayerUnits is the board's observed hostile list. The principal
        // opponent check prevents a nearby roaming third faction from diverting the army.
        foreach (var opponent in board.AlivePlayerUnits)
            if (opponent != null && opponent.IsAlive && opponent.type == Type.Unit && opponent.team == board.OpponentTeam
                && board.IsSpecialActorObserved(opponent)) armyAdvanceTargets.Add(opponent.transform.position);
        if (board.PlayerCrystalVisible && board.PlayerCrystalHP > 0) armyAdvanceTargets.Add(board.PlayerCrystalPos);
        if (armyAdvanceTargets.Count != 0) return;

        var crystal = board.GetLastKnownPlayerCrystal();
        int age = board.TurnCount - crystal.Turn;
        if (board.ReconThreatLevel >= 4 && !board.PlayerCrystalVisible && crystal.Valid && age >= 0 && age < 5
            && !board.IsVisibleToEnemy(crystal.Position))
            armyAdvanceTargets.Add(crystal.Position);
        // This existing snapshot API enforces age, threat-level and visible-empty
        // invalidation; WasPrincipalCommander is captured only while actually observed.
        if (board.GetLastKnownPrincipalCommander(out var commander, out _)) armyAdvanceTargets.Add(commander);
    }

    static bool LegalArmyAdvance(AIAction action, AIBoardState board, Vector3 from, Vector3 destination)
    {
        var map = board.MapCreate;
        if (map == null || !MovePatterns.CanMove(action.Unit, action.Unit.direction, destination.x - from.x, destination.z - from.z)
            || !map.TryGetHeight(Mathf.RoundToInt(destination.x), Mathf.RoundToInt(destination.z), out float height)
            || Mathf.Abs(height - destination.y) > .01f || !map.CanTraverse(from, destination)
            || GridHelper.MatchXZ(destination, GridHelper.ToGridXZ(board.EnemyCrystalPos))
            || board.PlayerCrystalVisible && GridHelper.MatchXZ(destination, GridHelper.ToGridXZ(board.PlayerCrystalPos))) return false;
        // Occupancy uses the existing own/observed lists only. Hidden occupancy
        // remains the ordinary executor's responsibility, with no global scan here.
        foreach (var ally in board.AliveEnemyUnits)
            if (ally != action.Unit && ally != null && ally.IsAlive && GridHelper.MatchXZ(destination, GridHelper.ToGridXZ(ally.transform.position))) return false;
        foreach (var opponent in board.AlivePlayerUnits)
            if (opponent != null && opponent.IsAlive && GridHelper.MatchXZ(destination, GridHelper.ToGridXZ(opponent.transform.position))) return false;
        return true;
    }

    bool ArmyAdvanceMakesProgress(Vector3 from, Vector3 destination)
    {
        foreach (var target in armyAdvanceTargets)
            if (Distance(from, target) - Distance(destination, target) >= 1) return true;
        return false;
    }
}
