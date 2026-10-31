using System.Collections.Generic;
using UnityEngine;

/// <summary>Operation-owned known-terrain routes. A route recommends one existing legal
/// move; it neither reveals occupants nor executes a saved sequence unchecked.</summary>
public sealed partial class AIOperationManager
{
    bool RefreshMarchTarget(AIOperationPlan plan, AIBoardState board, int turn)
    {
        if (plan.IsBaseAssault || !plan.HasTargetPosition || plan.Status != AIOperationStatus.Active) return false;
        Vector3 target;
        if (!string.IsNullOrEmpty(plan.TargetLifeId) && features.TryObserved(plan.TargetLifeId, out var observed)
            && board.IsSpecialActorObserved(observed)) target = observed.transform.position;
        else if (plan.TargetCategory == "Territory" && OperationApproachReached(plan)
            && board.Governor?.TerritoryIntel.HasWaypoint == true && !board.Governor.TerritoryIntel.Suspended)
            target = board.Governor.TerritoryIntel.Waypoint;
        else return false;
        int x = Mathf.RoundToInt(target.x), z = Mathf.RoundToInt(target.z);
        if (x == plan.TargetX && z == plan.TargetZ) return false;
        plan.TargetX = x; plan.TargetZ = z;
        // New information updates the retained order, but earns no movement,
        // damage, discovery or learning credit by itself.
        return true;
    }

    void GenerateTerritoryMarch(AIBoardState board, int turn, AIPersonality personality, AIPlayerModel.Profile player)
    {
        var intel = board.Governor?.TerritoryIntel;
        if (intel == null || !intel.HasWaypoint || intel.Suspended || SupplyRecoveryRequired(board)) return;
        bool fightingForce = false;
        foreach (var actor in ownArmy)
            if (EligibleSquadActor(actor) && AICapabilityRoles.For(actor).CanAttack
                && actor.HP >= actor.MaxHP * AIUnitPreservationConfig.Active.RejoinRatio)
            { fightingForce = true; break; }
        if (!fightingForce) return;
        var candidate = AddCandidate(board, turn, AIOperationGoal.LocateEnemyMainForce, 3, 65,
            null, personality, player);
        if (candidate == null) return;
        var plan = candidate.Plan;
        plan.HasTargetPosition = true; plan.TargetX = Mathf.RoundToInt(intel.Waypoint.x); plan.TargetZ = Mathf.RoundToInt(intel.Waypoint.z);
        plan.TargetCategory = "Territory";
        plan.StrategicReason = "観測した敵領土へ部隊を進めて主力・本陣を確認。斥候が不在でも戦闘隊を自陣へ固定しない";
        RefreshCandidateContext(candidate, board, turn);
    }

    readonly ExplorationKnownRoute marchRoutes = new ExplorationKnownRoute();
    readonly Dictionary<Vector3Int, Vector3Int> marchTerrain = new Dictionary<Vector3Int, Vector3Int>(2048);
    readonly HashSet<Vector3Int> marchBlocked = new HashSet<Vector3Int>();
    readonly HashSet<Vector3Int> marchObstacles = new HashSet<Vector3Int>();
    readonly List<Vector3Int> marchGoals = new List<Vector3Int>(8);
    AIBoardState marchBoard;
    int marchGeneration = -1, marchBuilds;

    void PrepareMarch(AIBoardState board)
    {
        if (marchBoard == board && marchGeneration == board.Generation) return;
        marchBoard = board; marchGeneration = board.Generation; marchBuilds = 0;
        marchTerrain.Clear(); marchObstacles.Clear(); marchRoutes.Clear();
        var map = board.MapCreate;
        if (map == null) return;
        // ObservedTerrainCells is the formal explored/visible set, never the hidden map.
        foreach (var cell in board.ObservedTerrainCells)
        {
            if (!map.TryGetHeight(cell.x, cell.z, out float y)) continue;
            var key = GridHelper.ToGridXZ(cell);
            marchTerrain[key] = new Vector3Int(cell.x, Mathf.RoundToInt(y), cell.z);
            if (map.IsRiver(cell.x, cell.z) || map.IsHighMountain(cell.x, cell.z)
                || board.KnownEnvironmentDefinition(cell)?.blocksMovement == true) marchObstacles.Add(key);
        }
        marchObstacles.Add(GridHelper.ToGridXZ(board.EnemyCrystalPos));
        foreach (var structure in features.OwnUnits(board, state.LastStartedOwnTurn))
            if (structure != null && structure.IsAlive && structure.type == Type.Wall)
                marchObstacles.Add(GridHelper.ToGridXZ(structure.transform.position));
        foreach (var ally in board.AliveEnemyUnits)
            if (ally != null && ally.IsAlive) marchObstacles.Add(GridHelper.ToGridXZ(ally.transform.position));
        foreach (var hostile in board.AlivePlayerUnits)
            if (hostile != null && hostile.IsAlive && board.IsSpecialActorObserved(hostile))
                marchObstacles.Add(GridHelper.ToGridXZ(hostile.transform.position));
    }

    bool TryPickMarchSlot(AIOperationPlan plan, AIOperationSquadMember member, Status actor,
        Vector3 aim, Vector3 ideal, AIBoardState board)
    {
        if (actor.HasMovedThisTurn || board.MapCreate == null || AITurnBudget.Expired) return false;
        PrepareMarch(board);
        if (marchBuilds >= 16 || marchTerrain.Count == 0) return false;
        int id = actor.GetInstanceID();
        var start = GridHelper.ToGridXZ(actor.transform.position);
        marchBlocked.Clear(); marchBlocked.UnionWith(marchObstacles); marchBlocked.Remove(start);
        marchGoals.Clear();
        Status firingTarget = null;
        if (member.Role != AIOperationSquadRole.Healer && !string.IsNullOrEmpty(plan.TargetLifeId) && features.TryObserved(plan.TargetLifeId, out var observed)
            && board.IsSpecialActorObserved(observed)) firingTarget = observed;
        var oppositeFacing = actor.direction == Direction.N ? Direction.S : Direction.N;
        int reach = AICapabilityRoles.For(actor).MaximumAttackReach;
        bool FiringCell(Vector3Int key)
        {
            if (firingTarget == null || reach <= 0 || !marchTerrain.TryGetValue(key, out var position)
                || GridHelper.ChebyshevDistance(position, firingTarget.transform.position) > reach) return false;
            return ProspectiveStrike(actor, position, actor.direction, firingTarget, board)
                || ProspectiveStrike(actor, position, oppositeFacing, firingTarget, board);
        }
        bool hasFiringCell = false;
        if (firingTarget != null)
            foreach (var pair in marchTerrain)
                if (!marchBlocked.Contains(pair.Key) && FiringCell(pair.Key)) { hasFiringCell = true; break; }
        // Keep a handful of closest known staging cells, including detours around
        // occupied firing squares. Unknown cells are directions, never certified paths.
        float GoalScore(Vector3Int cell) => (hasFiringCell ? GridHelper.ChebyshevDistance(cell, start) : GridHelper.ChebyshevDistance(cell, aim) * 8)
            + GridHelper.ChebyshevDistance(cell, ideal) * .15f;
        foreach (var pair in marchTerrain)
        {
            var cell = pair.Key;
            if (marchBlocked.Contains(cell) || cell == start && !hasFiringCell || hasFiringCell && !FiringCell(cell)) continue;
            float score = GoalScore(cell); int at = 0;
            while (at < marchGoals.Count && (GoalScore(marchGoals[at]) < score
                || Mathf.Approximately(GoalScore(marchGoals[at]), score) && CompareCell(marchGoals[at], cell) <= 0)) at++;
            if (at >= 6) continue;
            marchGoals.Insert(at, cell); if (marchGoals.Count > 6) marchGoals.RemoveAt(6);
        }
        var snapshot = new ExplorationActor(id, GridHelper.ToGrid(actor.transform.position), actor.kind,
            actor.HP, true, actor.ReflectionLifeId);
        var moves = board.GetValidMoves(actor);
        for (int goalIndex = 0; goalIndex < marchGoals.Count && goalIndex < 3 && marchBuilds < 16; goalIndex++)
        {
            if (AITurnBudget.Expired) break;
            marchBuilds++;
            if (!marchRoutes.TryMeasure(snapshot, marchGoals[goalIndex], marchTerrain, marchBlocked,
                MovePatterns.Offsets(actor), !MovePatterns.IsDirectionIndependent(actor), MovePatterns.DirZ(actor.direction),
                board.Generation, board.TurnCount, board.Generation, MarchDanger,
                out float before, out _, board.MapCreate.maxX, board.MapCreate.maxZ, requireEnabled: false)) continue;
            bool found = false; float best = float.NegativeInfinity; Vector3 destination = default;
            foreach (var cell in moves)
            {
                var key = GridHelper.ToGridXZ(cell);
                if (squadSlots.Contains(key) || board.CalcMoveCost(actor, cell) > board.EnemyAP
                    || !marchRoutes.TryMeasureDestination(id, key, out float after) || before - after <= .01f) continue;
                float danger = board.Governor?.IncomingDamage(board, actor, cell, null) ?? board.EstimateCounterDamageAt(cell, actor);
                if (danger >= actor.HP || member.Role == AIOperationSquadRole.Ranged && AdjacentObservedHostile(cell, board)) continue;
                float score = (before - after) * 8 - GridHelper.ChebyshevDistance(cell, ideal) * .15f
                    - danger / Mathf.Max(1, actor.HP) * 8;
                if (!found || score > best || Mathf.Approximately(score, best) && CompareCell(cell, destination) < 0)
                { found = true; best = score; destination = cell; }
            }
            if (!found) continue;
            member.HasSlot = true; member.SlotX = Mathf.RoundToInt(destination.x); member.SlotZ = Mathf.RoundToInt(destination.z);
            member.MarchSlot = true; squadSlots.Add(GridHelper.ToGridXZ(destination));
            return true;
        }
        return TryPickProbeSlot(member, actor, aim, board);
    }

    bool TryPickProbeSlot(AIOperationSquadMember member, Status actor, Vector3 aim, AIBoardState board)
    {
        bool found = false; Vector3 best = default; float bestScore = float.NegativeInfinity;
        float before = GridHelper.ChebyshevDistance(actor.transform.position, aim);
        foreach (var cell in board.GetValidMoves(actor))
        {
            // The existing rules permit exploration moves into Fog. Consider
            // only that legal first hop, without claiming any hidden route is clear.
            if (board.IsTerrainKnown(cell) || squadSlots.Contains(GridHelper.ToGridXZ(cell))
                || board.CalcMoveCost(actor, cell) > board.EnemyAP) continue;
            float gain = before - GridHelper.ChebyshevDistance(cell, aim);
            if (gain <= 0) continue;
            float danger = board.Governor?.IncomingDamage(board, actor, cell, null) ?? board.EstimateCounterDamageAt(cell, actor);
            if (danger >= actor.HP * .25f) continue;
            float score = gain - danger / Mathf.Max(1, actor.HP);
            if (!found || score > bestScore || Mathf.Approximately(score, bestScore) && CompareCell(cell, best) < 0)
            { found = true; best = cell; bestScore = score; }
        }
        if (!found) return false;
        member.HasSlot = member.MarchSlot = true; member.SlotX = Mathf.RoundToInt(best.x); member.SlotZ = Mathf.RoundToInt(best.z);
        squadSlots.Add(GridHelper.ToGridXZ(best)); return true;
    }

    float MarchDanger(int id, Vector3Int cell)
        => marchBoard != null ? marchBoard.Belief.RiskAt(cell, marchBoard.ReconThreatLevel) : 0;

    // Called only for already generated commands. The slot is rebuilt after every
    // board generation, so a former order cannot bypass current AP/occupancy/survival.
    public bool IsPlannedMarch(AIAction action, AIBoardState board)
        => action != null && IsMovement(action.ActionType) && LegalSquadAction(action, board, out _, out var member)
            && member.MarchSlot && member.HasSlot && !action.Unit.HasMovedThisTurn
            && action.TargetPos.x == member.SlotX && action.TargetPos.z == member.SlotZ
            && board.GetValidMoves(action.Unit).Contains(action.TargetPos);

    public bool ReversesHealthyMarch(AIAction action, AIBoardState board)
    {
        if (action?.Unit == null || board == null || !IsMovement(action.ActionType) || !action.Unit.HasMovedThisTurn
            || board.Governor?.Mode == StrategicMode.EmergencyDefense
            || !TryGetSquad(action.Unit.ReflectionLifeId, out var squad)
            || !squadMembers.TryGetValue(action.Unit.ReflectionLifeId, out var member) || member.Recovering
            || member.Role == AIOperationSquadRole.Healer) return false;
        var actor = action.Unit; var plan = FindActive(squad.OperationId);
        if (plan == null || !plan.HasTargetPosition || plan.Status != AIOperationStatus.Active
            || actor.HP < actor.MaxHP * AIUnitPreservationConfig.Active.RejoinRatio) return false;
        var aim = MovementAim(plan, actor);
        if (AIOperationFeatureExtractor.Distance(action.TargetPos, aim)
            <= AIOperationFeatureExtractor.Distance(actor.transform.position, aim)) return false;
        float before = board.Governor?.IncomingDamage(board, actor, actor.transform.position, null)
            ?? board.EstimateCounterDamageAt(actor.transform.position, actor);
        float after = board.Governor?.IncomingDamage(board, actor, action.TargetPos, null)
            ?? board.EstimateCounterDamageAt(action.TargetPos, actor);
        if (after < before || coordinationHead == action && CurrentCoordination(board)) return false;
        if ((long)action.APCost + board.CoordinationAttackCost(actor, true) <= board.EnemyAP)
        {
            int examined = 0;
            foreach (var target in board.AlivePlayerUnits)
            {
                if (examined++ >= 16) break;
                if (ProspectiveStrike(actor, action.TargetPos, actor.direction, target, board)) return false;
            }
        }
        // Do not undo this turn's actual advance just to consume surplus AP or
        // satisfy a later fallback score. Real retreat and firing preparation win.
        return true;
    }

    void ClearMarch()
    {
        marchRoutes.Clear(); marchTerrain.Clear(); marchBlocked.Clear(); marchObstacles.Clear(); marchGoals.Clear();
        marchBoard = null; marchGeneration = -1;
    }

    bool OperationApproachReached(AIOperationPlan plan)
    {
        if (!plan.HasTargetPosition) return false;
        int ready = 0, active = 0;
        var target = new Vector3(plan.TargetX, 0, plan.TargetZ);
        foreach (string life in plan.AssignedUnitLifeIds)
        {
            if (!features.TryOwn(life, out var actor) || actor == null || !actor.IsAlive
                || actor.HP < actor.MaxHP * AIUnitPreservationConfig.Active.WoundedRatio) continue;
            active++;
            int reach = Mathf.Clamp(AICapabilityRoles.For(actor).MaximumAttackReach, 1, 8);
            if (AIOperationFeatureExtractor.Distance(actor.transform.position, target) <= reach + 1) ready++;
        }
        return active > 0 && ready * 2 >= active;
    }

    bool OperationAssembled(AIOperationPlan plan)
    {
        Vector3 center = Vector3.zero; int count = 0, near = 0;
        foreach (string life in plan.AssignedUnitLifeIds)
            if (features.TryOwn(life, out var actor) && actor != null && actor.IsAlive
                && actor.HP >= actor.MaxHP * AIUnitPreservationConfig.Active.RejoinRatio)
            { center += actor.transform.position; count++; }
        if (count == 0) return false;
        center /= count;
        foreach (string life in plan.AssignedUnitLifeIds)
            if (features.TryOwn(life, out var actor) && actor != null && actor.IsAlive
                && actor.HP >= actor.MaxHP * AIUnitPreservationConfig.Active.RejoinRatio
                && AIOperationFeatureExtractor.Distance(actor.transform.position, center) <= AIOperationSquadConfig.Active.CohesionRadius) near++;
        return near * 2 >= count;
    }

    static bool PhysicalMove(AIActionRecord record) => record.ExecutionSucceeded && IsMovement(record.ActionType)
        && record.Before != null && record.After != null
        && (record.Before.ActorX != record.After.ActorX || record.Before.ActorY != record.After.ActorY);
    static bool RealPlannedMarch(AIActionRecord record) => record.OperationMarchOrdered && PhysicalMove(record);
    static int OwnOperationDistanceGain(AIActionRecord record, AIOperationPlan plan)
    {
        if (!plan.HasTargetPosition || !PhysicalMove(record)) return 0;
        var target = new Vector3(plan.TargetX, 0, plan.TargetZ);
        return AIOperationFeatureExtractor.Distance(new Vector3(record.Before.ActorX, 0, record.Before.ActorY), target)
            - AIOperationFeatureExtractor.Distance(new Vector3(record.After.ActorX, 0, record.After.ActorY), target);
    }
    bool DamageAdvancesOperation(AIActionRecord record, AIOperationPlan plan)
        => !plan.IsBaseAssault || record.TargetLifeId == plan.TargetLifeId
            || record.Before != null && record.Before.TargetObserved
                && AIOperationFeatureExtractor.Distance(new Vector3(record.Before.TargetX, 0, record.Before.TargetY),
                    new Vector3(plan.TargetX, 0, plan.TargetZ)) <= DefenseRadius;
}
