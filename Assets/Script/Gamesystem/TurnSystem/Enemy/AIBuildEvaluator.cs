using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIBuildEvaluator — 建築・召喚・サブクリスタルの基本評価
//  AIActionEvaluator から分離。
// =====================================================================
static class AIBuildEvaluator
{
    const int TurnMidEnd   = AIConstants.TurnMidEnd;
    const float DuplicatePenaltyFactor = 15f;
    static readonly List<Status> ownBuildings = new List<Status>(32);
    static AIBoardState buildingsBoard;
    static int buildingsGeneration = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBuildingCache()
    {
        ownBuildings.Clear();
        buildingsBoard = null;
        buildingsGeneration = -1;
    }

    internal static float CalcSubCrystalBaseScore(AIAction action, AIBoardState board)
    {
        return board.Outposts.Score(action.TargetPos);
    }

    internal static float CalcBuildBaseScore(AIAction action, AIBoardState board)
    {
        float score = 15f;
        var facility = action.FacilityDefinition != null ? action.FacilityDefinition.behaviourKind : action.Facility;
        int turn = board.TurnCount;
        var assessment = EconomyHelper.AssessProductionBuild(action, board);
        float militaryPenalty = 0f;
        if (AIStrategicGovernor.IsMilitaryConstruction(action))
        {
            board.Governor?.Evaluate(board);
            bool emergency = board.Governor != null && board.Governor.IsEmergencyDefenseRequired(action, board);
            if (board.ProductionDemand.State == EconomicState.Warning && !emergency)
                militaryPenalty = AIEconomySettings.Active.WarningMilitaryBuildPenalty;
        }
        score -= militaryPenalty;

        // A second producer has the same marginal value as the first when it closes the same deficit.
        // Capacity facilities use the same assessment and are useful only at an actual bottleneck.
        if (assessment.IsProduction)
        {
            float phase = assessment.ImprovesDeficit ? ProductionPhaseScore(facility, turn) : 0f;
            score += assessment.FinalScore + phase;
            // The reserve/plan policy also values real probabilistic and capacity supply paths.
            // Guaranteed-upkeep forecasting cannot label those useful investments as surplus.
            if (!assessment.ImprovesDeficit && board.Governor?.BasicResources.IsFoundationAction(action) == true)
                score += assessment.OverstockPenalty;
            if (AIEconomySettings.Active.enableDecisionLogs)
                DevelopmentLog.Log($"[AI経済建築] {facility} need={assessment.NeedScore:F1} " +
                    $"chain={assessment.ChainRecoveryScore:F1} reserve={assessment.ReserveRecoveryScore:F1} " +
                    $"overstock={assessment.OverstockPenalty:F1} inputPressure={assessment.InputPressurePenalty:F1} " +
                    $"phase={phase:F1} militaryPenalty={militaryPenalty:F1} base={score:F1}");
            return score;
        }

        switch (facility)
        {
            case FacilityKind.Barracks:
                score += AIActionEvaluator.PhaseScore(turn, 3f, 15f, 20f);
                if (board.GetBuildingCount(FacilityKind.Barracks) == 0 && turn >= 12)
                    score += 20f;
                break;

            case FacilityKind.WoodWall:
            case FacilityKind.StoneWall:
                score += AIActionEvaluator.PhaseScore(turn, 3f, 8f, 15f);
                score -= CalcWallSaturationPenalty(action.TargetPos, board);
                break;

            case FacilityKind.Mortar:
            case FacilityKind.Cannon:
                score += AIActionEvaluator.PhaseScore(turn, 2f, 10f, 18f);
                break;

            case FacilityKind.RestraintTrap:
            case FacilityKind.SpikeTrap:
                score += AIActionEvaluator.PhaseScore(turn, 3f, 10f, 15f);
                break;

            case FacilityKind.HeroSword:
                score += turn > TurnMidEnd ? 20f : 2f;
                break;
        }

        if (FacilityData.IsSubCrystal(facility))
            score += 15f;

        int existingCount = board.GetBuildingCount(facility);
        if (existingCount > 0)
            score -= existingCount * existingCount * DuplicatePenaltyFactor;

        return score;
    }

    static float ProductionPhaseScore(FacilityKind facility, int turn)
    {
        switch (facility)
        {
            case FacilityKind.Well:
            case FacilityKind.LoggingCamp:
            case FacilityKind.Quarry:
            case FacilityKind.Field:
                return AIActionEvaluator.PhaseScore(turn, 12f, 5f, 2f);
            case FacilityKind.Bakery:
                return AIActionEvaluator.PhaseScore(turn, 12f, 8f, 4f);
            case FacilityKind.Mine:
            case FacilityKind.House:
            case FacilityKind.LuxuryHouse:
                return AIActionEvaluator.PhaseScore(turn, 5f, 8f, 5f);
            default:
                return 0f;
        }
    }

    static float CalcWallSaturationPenalty(Vector3 position, AIBoardState board)
    {
        if (buildingsBoard != board || buildingsGeneration != board.Generation)
        {
            buildingsBoard = board;
            buildingsGeneration = board.Generation;
            board.CollectOwnBuildings(ownBuildings);
        }

        int adjacentWalls = 0;
        for (int i = 0; i < ownBuildings.Count; i++)
        {
            var building = ownBuildings[i];
            if (building == null || !building.IsAlive || !FacilityData.IsWall(building.facilityKind)) continue;
            if (GridHelper.ChebyshevDistance(position, building.transform.position) <= 2) adjacentWalls++;
        }

        float penalty = adjacentWalls * AIEconomySettings.Active.LocalWallSaturationPenalty;
        bool aligned = WallFacesObservedThreat(position, board.EnemyCrystalPos, board);
        if (!aligned)
        {
            foreach (var actor in board.AliveEnemyUnits)
            {
                if (actor == null || !actor.IsAlive || actor.kind != Kind.King) continue;
                if (WallFacesObservedThreat(position, actor.transform.position, board)) { aligned = true; break; }
            }
        }
        if (!aligned) penalty += AIEconomySettings.Active.UncoveredWallThreatPenalty;
        return penalty;
    }

    static bool WallFacesObservedThreat(Vector3 position, Vector3 protectedPosition, AIBoardState board)
    {
        if (GridHelper.ChebyshevDistance(position, protectedPosition) > 5) return false;
        Vector3 direction = position - protectedPosition;
        direction.y = 0;
        foreach (var opponent in board.AlivePlayerUnits)
        {
            if (opponent == null || !opponent.IsAlive || BoardActionProfile.For(opponent)?.CanAttack == false) continue;
            if (GridHelper.ChebyshevDistance(opponent.transform.position, protectedPosition) > 8) continue;
            Vector3 incoming = opponent.transform.position - protectedPosition;
            incoming.y = 0;
            if (Vector3.Dot(direction.normalized, incoming.normalized) >= 0.5f) return true;
        }
        return false;
    }

    internal static float CalcSummonBaseScore(AIAction action, AIBoardState board)
    {
        float score = 30f;

        bool contact = board.Recon.TryGetContactTarget(out _);
        bool combatRecruit = action.SummonKind != Kind.Scout && action.SummonKind != Kind.Priest;
        var demand = board.ProductionDemand;
        bool foundationWeak = AIBasicResourceSettings.Active.enabled && board.Governor?.BasicResources.HasShortage == true;
        if (contact && combatRecruit)
            score += 45f; // Contact must not leave recruitment locked behind a mature economy.
        else if (demand.State >= EconomicState.Crisis)
            score -= 120f;
        else if (foundationWeak || !EconomyHelper.IsEconomySufficient(board))
            score -= 60f;
        else
            score += 20f;

        int allyCount = board.AliveEnemyUnits.Count;
        if (allyCount <= 2) score += 35f;
        else if (allyCount <= 4) score += 25f;
        else if (allyCount <= 6) score += 15f;
        else score += 5f;

        if (board.CanUsePlayerCrystalAsTarget())
        {
            float dist = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
            score += Mathf.Max(0, 15f - dist);
        }

        if (action.SummonKind == Kind.Scout && board.AlivePlayerUnits.Count == 0)
        {
            float explorationRatio = board.GetExplorationRatio();
            if (explorationRatio < 0.5f)
                score += 20f;
            else if (explorationRatio < 0.7f)
                score += 10f;
        }

        switch (action.SummonKind)
        {
            case Kind.Knight:  score += 12f; break;
            case Kind.Archer:  score += 10f; break;
            case Kind.Magic:   score += 8f; break;
            case Kind.Assassin: score += 6f; break;
            case Kind.Scout:   score += 5f; break;
        }

        return score;
    }
}
