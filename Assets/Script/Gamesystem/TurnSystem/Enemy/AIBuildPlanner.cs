using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIBuildPlanner — 建築計画の責務を担当
//  AICommander から分離: 先行建築・後手建築・直接建築フォールバック
// =====================================================================
public class AIBuildPlanner
{
    readonly APSystem _apSystem;
    readonly FactionState _factionState;
    readonly AIActionExecutor _executor;
    readonly AIPersonality _personality;
    readonly AILearning _learning;

    public AIBuildPlanner(
        APSystem apSystem, FactionState factionState,
        AIActionExecutor executor, AIPersonality personality, AILearning learning)
    {
        _apSystem = apSystem;
        _factionState = factionState;
        _executor = executor;
        _personality = personality;
        _learning = learning;
    }

    // ================================================================
    //  建築先行フェーズ: 経済未成熟時、メインループの前に建築を優先実行
    // ================================================================
    public int TryEarlyBuildPhase(
        AIBoardState board, TurnStrategy strategy, int turnCount)
    {
        if (AITurnBudget.Expired || board == null) return 0;
        DevelopmentLog.Log($"[AIBuildPlanner] === 先行建築フェーズ開始 === AP={board.EnemyAP} ターン={turnCount}");
        var governor = PrepareGovernor(board);

        if (AITacticalPriorities.HasLocalThreat(board) || AITacticalPriorities.FinishingAttackReserve(board) > 0) return 0;

        if (EconomyHelper.IsEconomySufficient(board))
        {
            DevelopmentLog.Log("[AIBuildPlanner] 経済充足 → 先行建築スキップ");
            return 0;
        }

        if (strategy == TurnStrategy.CrystalDefense && governor.Mode != StrategicMode.EconomicRecovery)
        {
            DevelopmentLog.Log("[AIBuildPlanner] クリスタル防衛中 → 先行建築スキップ");
            return 0;
        }
        if (strategy == TurnStrategy.ContactEngage && governor.Mode != StrategicMode.EconomicRecovery)
        {
            DevelopmentLog.Log("[AIBuildPlanner] 交戦開始中 → 先行建築スキップ");
            return 0;
        }

        DevelopmentLog.Log($"[AIBuildPlanner] BuildablePositions={board.BuildablePositions.Count}  " +
                  $"AffordableBuildings={board.AffordableBuildings.Count}  " +
                  $"({string.Join(",", board.AffordableBuildings)})");

        int earlyBuilds = TryScoreBuildPhase(board, strategy, turnCount);

        if (earlyBuilds == 0 && !AITurnBudget.Expired && _executor.BuildSystem != null && board.EnemyAP >= 3)
        {
            DevelopmentLog.Log("[AIBuildPlanner] スコアパスで建築0棟 → 直接建築フォールバック開始");
            earlyBuilds += ForceDirectBuild(board, turnCount);
        }

        DevelopmentLog.Log($"[AIBuildPlanner] === 先行建築フェーズ終了: {earlyBuilds}棟建築  残AP={board.EnemyAP} ===");
        return earlyBuilds;
    }

    // ================================================================
    //  スコアベース建築 (先行フェーズ用)
    // ================================================================
    int TryScoreBuildPhase(AIBoardState board, TurnStrategy strategy, int turnCount)
    {
        int built = 0;
        // Re-evaluate demand after every purchase; stale scores could build two identical missing facilities.
        for (int i = 0; i < 2 && !AITurnBudget.Expired; i++)
        {
            int result = TryScoreSingleBuild(board, strategy, turnCount);
            if (result == 0) break;
            built += result;
        }
        return built;
    }

    int TryScoreSingleBuild(AIBoardState board, TurnStrategy strategy, int turnCount)
    {
        if (AITurnBudget.Expired || board.BuildablePositions.Count == 0)
            return 0;
        var governor = PrepareGovernor(board);

        var buildActions = new List<AIAction>();
        AIActionEvaluator.GenerateBuildCandidatesPublic(board, buildActions);
        AIActionEvaluator.GenerateSubCrystalCandidatesPublic(board, buildActions);

        DevelopmentLog.Log($"[AIBuildPlanner] 生成された建築候補数={buildActions.Count}");
        if (buildActions.Count == 0) return 0;

        foreach (var action in buildActions)
        {
            if (AITurnBudget.Expired) return 0;
            action.Score = AIActionEvaluator.CalcBuildScorePublic(action, _personality, board, _learning);
        }

        ApplyEarlyBuildStrategyBonus(buildActions, board, strategy);
        governor.Filter(buildActions, board);
        LogRejectedBuilds(governor);
        buildActions.Sort(AIAction.ComparePriorityThenScore);

        for (int i = 0; i < Mathf.Min(3, buildActions.Count); i++)
        {
            var ba = buildActions[i];
            DevelopmentLog.Log($"[AIBuildPlanner] 候補{i + 1}: {ba.Facility} P{ba.StrategicPriority} score={ba.Score:F1} AP={ba.APCost} pos={ba.TargetPos}");
        }

        foreach (var action in buildActions)
        {
            if (AITurnBudget.Expired) break;
            if (action.APCost > board.EnemyAP)
            {
                DevelopmentLog.Log($"[AIBuildPlanner] AP不足でスキップ: {action.Facility} APコスト={action.APCost} 残AP={board.EnemyAP}");
                continue;
            }

            if (!governor.AllowExecution(action, board)) continue;
            DevelopmentLog.Log($"[AIBuildPlanner] 建築試行: {action.Facility} @{action.TargetPos} P{action.StrategicPriority} score={action.Score:F1}");
            bool success = _executor.Execute(action, board);
            if (success)
            {
                governor.Evaluate(board);
                governor.Telemetry(action, true);
                DevelopmentLog.Log($"[AIBuildPlanner] ★先行建築成功: {action.Facility} 残AP={board.EnemyAP} 経済={governor.Economy.State}");
                return 1;
            }
            else
            {
                DevelopmentLog.Log($"[AIBuildPlanner] ✗先行建築失敗: {action.Facility} @{action.TargetPos}");
            }
        }
        return 0;
    }

    static void ApplyEarlyBuildStrategyBonus(List<AIAction> actions, AIBoardState board, TurnStrategy strategy)
    {
        foreach (var action in actions)
        {
            var assessment = EconomyHelper.AssessProductionBuild(action, board);
            if (strategy == TurnStrategy.EconomyBuild)
            {
                if (assessment.ImprovesDeficit) action.Score += 30f;
            }
            else if (strategy == TurnStrategy.Balanced && assessment.ImprovesDeficit)
            {
                action.Score += 15f;
            }
        }
    }

    // ================================================================
    //  後手建築フェーズ: メインループ後にAPが残っている場合
    // ================================================================
    public int TryLateBuildPhase(AIBoardState board, int buildsDone, int turnCount)
    {
        if (AITurnBudget.Expired || board == null) return 0;
        bool shouldPostBuild = buildsDone == 0 && board.EnemyAP >= 3
            && !EconomyHelper.IsEconomySufficient(board);

        if (!shouldPostBuild) return 0;

        DevelopmentLog.Log($"[AIBuildPlanner] メインループで建築0棟 → 後手建築フェーズ (ターン{turnCount})");
        board.Refresh();
        PrepareGovernor(board);
        int lateBuilds = ForceDirectBuild(board, turnCount);
        DevelopmentLog.Log($"[AIBuildPlanner] 後手建築フェーズ: {lateBuilds}棟建築");
        return lateBuilds;
    }

    // ================================================================
    //  直接建築フォールバック
    // ================================================================
    int ForceDirectBuild(AIBoardState board, int turnCount)
    {
        if (AITurnBudget.Expired || board == null || board.ActorTeam != _executor.ActorTeam) return 0;
        var buildSystem = _executor.BuildSystem;
        if (buildSystem == null)
        {
            Debug.LogWarning("[AIBuildPlanner] _buildSystem==null → 建築不可");
            return 0;
        }
        if (_factionState == null)
        {
            Debug.LogWarning("[AIBuildPlanner] _factionState==null → 建築不可");
            return 0;
        }

        try
        {
            return ForceDirectBuildInternal(board, buildSystem, turnCount);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AIBuildPlanner] 例外発生: {e.Message}\n{e.StackTrace}");
            return 0;
        }
    }

    int ForceDirectBuildInternal(AIBoardState board, BuildSystem buildSystem, int turnCount)
    {
        int built = 0;
        if (board == null || _apSystem == null || _factionState == null || AITurnBudget.Expired) return built;
        var governor = PrepareGovernor(board);

        FacilityKind[] buildOrder = {
            FacilityKind.Well, FacilityKind.LoggingCamp, FacilityKind.Quarry,
            FacilityKind.Field, FacilityKind.Mine, FacilityKind.House,
            FacilityKind.Bakery,
            FacilityKind.Warehouse, FacilityKind.Barracks,
        };

        // Rebuild the candidate set after every purchase. Both demand and available cells have changed.
        while (built < 3 && !AITurnBudget.Expired)
        {
            int currentAP = _apSystem.GetAP(board.ActorTeam);
            if (currentAP < 3) break;
            governor.Evaluate(board);
            var positions = buildSystem.AIGetBuildablePositions(board.ActorTeam);
            if (positions.Count == 0) break;
            var actions = new List<AIAction>(buildOrder.Length);

            foreach (var facility in buildOrder)
            {
                if (AITurnBudget.Expired) return built;
                if (!FacilityData.Table.TryGetValue(facility, out var info) || currentAP < info.APCost) continue;
                if (!_apSystem.CanBuild(board.ActorTeam, facility, _factionState) || !board.HasUpstreamProducer(facility)) continue;
                var action = new AIAction { ActionType = AIActionType.Build, Facility = facility,
                    TargetPos = positions[0], APCost = info.APCost };
                var assessment = governor.BuildAssessment(action, board);
                // A fallback fills diagnosed supply/capacity gaps; it is never a reason to buy surplus production.
                if (!assessment.IsProduction || !assessment.ImprovesDeficit) continue;
                action.Score = AIActionEvaluator.CalcBuildScorePublic(action, _personality, board, _learning);
                actions.Add(action);
            }
            governor.Filter(actions, board);
            LogRejectedBuilds(governor);
            actions.Sort(AIAction.ComparePriorityThenScore);

            bool purchased = false;
            foreach (var action in actions)
            {
                if (AITurnBudget.Expired) break;
                for (int i = 0; i < positions.Count && !AITurnBudget.Expired; i++)
                {
                    var pos = positions[i];
                    action.TargetPos = pos;
                    if (!governor.AllowExecution(action, board)) continue;
                    if (!buildSystem.AIPlaceBuilding(pos, action.Facility, board.ActorTeam)) continue;
                    built++;
                    purchased = true;
                    board.Refresh();
                    governor.Evaluate(board);
                    governor.Telemetry(action, true);
                    DevelopmentLog.Log($"[AIBuildPlanner] ★★ {action.Facility} @({pos.x},{pos.y},{pos.z}) 建築成功! " +
                              $"残AP={board.EnemyAP} (今ターン{built}棟目) 経済={governor.Economy.State}");
                    break;
                }
                if (purchased) break;
            }
            if (!purchased) break;
        }

        if (built == 0)
            DevelopmentLog.Log("[AIBuildPlanner] 直接建築なし: 必要な供給改善・安全条件・配置条件を満たす候補なし");

        return built;
    }

    static AIStrategicGovernor PrepareGovernor(AIBoardState board)
    {
        if (board.Governor == null)
            board.Governor = new AIStrategicGovernor { BreadReserveTurns = AIEconomySettings.Active.BreadReserveTurns };
        board.Governor.Evaluate(board);
        return board.Governor;
    }

    static void LogRejectedBuilds(AIStrategicGovernor governor)
    {
        if (!AIEconomySettings.Active.enableDecisionLogs) return;
        foreach (var rejected in governor.Rejections)
            DevelopmentLog.Log($"[AIBuildPlanner] 建築禁止: {rejected.Key.Facility} reason={rejected.Value}");
    }
}
