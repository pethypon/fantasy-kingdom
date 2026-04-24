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
        Debug.Log($"[AIBuildPlanner] === 先行建築フェーズ開始 === AP={board.EnemyAP} ターン={turnCount}");

        bool forceByTurn = turnCount >= 30;

        if (EconomyHelper.IsEconomySufficient(board) && !forceByTurn)
        {
            Debug.Log("[AIBuildPlanner] 経済充足 → 先行建築スキップ");
            return 0;
        }

        if (strategy == TurnStrategy.CrystalDefense && !forceByTurn)
        {
            Debug.Log("[AIBuildPlanner] クリスタル防衛中 → 先行建築スキップ");
            return 0;
        }
        if (strategy == TurnStrategy.ContactEngage && !forceByTurn)
        {
            Debug.Log("[AIBuildPlanner] 交戦開始中 → 先行建築スキップ");
            return 0;
        }

        Debug.Log($"[AIBuildPlanner] BuildablePositions={board.BuildablePositions.Count}  " +
                  $"AffordableBuildings={board.AffordableBuildings.Count}  " +
                  $"({string.Join(",", board.AffordableBuildings)})");

        int earlyBuilds = TryScoreBuildPhase(board, strategy, turnCount);

        if (earlyBuilds == 0 && _executor.BuildSystem != null && board.EnemyAP >= 3)
        {
            Debug.Log("[AIBuildPlanner] スコアパスで建築0棟 → 直接建築フォールバック開始");
            earlyBuilds += ForceDirectBuild(board, turnCount);
        }

        Debug.Log($"[AIBuildPlanner] === 先行建築フェーズ終了: {earlyBuilds}棟建築  残AP={board.EnemyAP} ===");
        return earlyBuilds;
    }

    // ================================================================
    //  スコアベース建築 (先行フェーズ用)
    // ================================================================
    int TryScoreBuildPhase(AIBoardState board, TurnStrategy strategy, int turnCount)
    {
        if (board.BuildablePositions.Count == 0 || board.AffordableBuildings.Count == 0)
            return 0;

        var buildActions = new List<AIAction>();
        AIActionEvaluator.GenerateBuildCandidatesPublic(board, buildActions);
        AIActionEvaluator.GenerateSubCrystalCandidatesPublic(board, buildActions);

        Debug.Log($"[AIBuildPlanner] 生成された建築候補数={buildActions.Count}");
        if (buildActions.Count == 0) return 0;

        foreach (var action in buildActions)
        {
            action.Score = AIActionEvaluator.CalcBuildScorePublic(action, _personality, board, _learning);
        }

        ApplyEarlyBuildStrategyBonus(buildActions, board, strategy);

        buildActions.Sort((a, b) => b.Score.CompareTo(a.Score));

        for (int i = 0; i < Mathf.Min(3, buildActions.Count); i++)
        {
            var ba = buildActions[i];
            Debug.Log($"[AIBuildPlanner] 候補{i + 1}: {ba.Facility} score={ba.Score:F1} AP={ba.APCost} pos={ba.TargetPos}");
        }

        int earlyBuilds = 0;
        const int maxEarlyBuilds = 2;

        foreach (var action in buildActions)
        {
            if (earlyBuilds >= maxEarlyBuilds) break;
            if (action.APCost > board.EnemyAP)
            {
                Debug.Log($"[AIBuildPlanner] AP不足でスキップ: {action.Facility} APコスト={action.APCost} 残AP={board.EnemyAP}");
                continue;
            }

            Debug.Log($"[AIBuildPlanner] 建築試行: {action.Facility} @{action.TargetPos} score={action.Score:F1}");
            bool success = _executor.Execute(action, board);
            if (success)
            {
                earlyBuilds++;
                board.Refresh();
                Debug.Log($"[AIBuildPlanner] ★先行建築{earlyBuilds}成功: {action.Facility} 残AP={board.EnemyAP}");
            }
            else
            {
                Debug.Log($"[AIBuildPlanner] ✗先行建築失敗: {action.Facility} @{action.TargetPos}");
            }
        }
        return earlyBuilds;
    }

    static void ApplyEarlyBuildStrategyBonus(List<AIAction> actions, AIBoardState board, TurnStrategy strategy)
    {
        foreach (var action in actions)
        {
            if (strategy == TurnStrategy.EconomyBuild)
            {
                action.Score += 30f;
                if (EconomyHelper.IsMissingCoreFacility(action.Facility, board))
                    action.Score += 40f;
            }
            else if (strategy == TurnStrategy.Balanced)
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
        bool shouldPostBuild = buildsDone == 0 && board.EnemyAP >= 3
            && (!EconomyHelper.IsEconomySufficient(board) || turnCount >= 30);

        if (!shouldPostBuild) return 0;

        Debug.Log($"[AIBuildPlanner] メインループで建築0棟 → 後手建築フェーズ (ターン{turnCount})");
        board.Refresh();
        int lateBuilds = ForceDirectBuild(board, turnCount);
        Debug.Log($"[AIBuildPlanner] 後手建築フェーズ: {lateBuilds}棟建築");
        return lateBuilds;
    }

    // ================================================================
    //  直接建築フォールバック
    // ================================================================
    int ForceDirectBuild(AIBoardState board, int turnCount)
    {
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

        FacilityKind[] buildOrder = {
            FacilityKind.Well, FacilityKind.LoggingCamp, FacilityKind.Quarry,
            FacilityKind.Field, FacilityKind.Mine, FacilityKind.House,
            FacilityKind.Bakery,
            FacilityKind.Warehouse, FacilityKind.Barracks,
        };

        var positions = buildSystem.AIGetBuildablePositions(Team.Enemy);
        Debug.Log($"[AIBuildPlanner] 建築可能位置数={positions.Count}  AP={_apSystem.GetAP(Team.Enemy)}");

        if (positions.Count == 0)
        {
            Debug.LogWarning("[AIBuildPlanner] 建築可能位置がゼロ");
            return 0;
        }

        foreach (var facility in buildOrder)
        {
            if (built >= 3) break;
            int currentAP = _apSystem.GetAP(Team.Enemy);
            if (currentAP < 3) break;

            if (!FacilityData.Table.TryGetValue(facility, out var info)) continue;
            if (currentAP < info.APCost) continue;
            if (!_apSystem.CanBuild(Team.Enemy, facility, _factionState)) continue;
            if (board != null && !board.HasUpstreamProducer(facility)) continue;

            int existing = board != null ? board.GetBuildingCount(facility) : 0;
            int maxForForce = turnCount >= 30 ? 3 : 1;
            if (facility == FacilityKind.House) maxForForce = 3;
            if (existing >= maxForForce) continue;

            for (int i = 0; i < positions.Count; i++)
            {
                var pos = positions[i];
                bool success = buildSystem.AIPlaceBuilding(pos, facility, Team.Enemy);
                if (success)
                {
                    built++;
                    if (board != null) board.Refresh();
                    Debug.Log($"[AIBuildPlanner] ★★ {facility} @({pos.x},{pos.y},{pos.z}) 建築成功! " +
                              $"残AP={_apSystem.GetAP(Team.Enemy)} (今ターン{built}棟目)");
                    positions = buildSystem.AIGetBuildablePositions(Team.Enemy);
                    break;
                }
            }
        }

        if (built == 0)
            Debug.LogWarning("[AIBuildPlanner] 全施設の建築に失敗");

        return built;
    }
}

// =====================================================================
//  EconomyHelper — 経済判定ユーティリティ（複数クラスで共用）
// =====================================================================
public static class EconomyHelper
{
    public static int CountEconBuildings(AIBoardState board)
    {
        return board.GetBuildingCount(FacilityKind.Well)
             + board.GetBuildingCount(FacilityKind.LoggingCamp)
             + board.GetBuildingCount(FacilityKind.Quarry)
             + board.GetBuildingCount(FacilityKind.Field)
             + board.GetBuildingCount(FacilityKind.Mine);
    }

    public static int CountProcessingBuildings(AIBoardState board)
    {
        return board.GetBuildingCount(FacilityKind.Bakery);
    }

    public static bool IsEconomySufficient(AIBoardState board)
    {
        int raw = CountEconBuildings(board);
        int proc = CountProcessingBuildings(board);
        int house = board.GetBuildingCount(FacilityKind.House);
        return raw >= 4 && proc >= 2 && house >= 1;
    }

    public static bool IsMissingCoreFacility(FacilityKind facility, AIBoardState board)
    {
        switch (facility)
        {
            case FacilityKind.Well:
            case FacilityKind.LoggingCamp:
            case FacilityKind.Quarry:
            case FacilityKind.Field:
            case FacilityKind.Bakery:
            case FacilityKind.Mine:
            case FacilityKind.House:
                return board.GetBuildingCount(facility) == 0;
            default:
                return false;
        }
    }

    /// <summary>基礎経済施設5種(Well,LoggingCamp,Quarry,Field,House)の設置済み種類数</summary>
    public static int CalcCoreEconomyCount(AIBoardState board)
    {
        return (board.GetBuildingCount(FacilityKind.Well) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Quarry) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.Field) > 0 ? 1 : 0)
             + (board.GetBuildingCount(FacilityKind.House) > 0 ? 1 : 0);
    }

    public static bool IsProcessingFacility(FacilityKind facility)
    {
        return facility == FacilityKind.Bakery;
    }
}
