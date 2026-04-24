using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  TurnStrategyPlanner — 戦略層モジュール
//
//  仕様:
//  ・毎ターンの方針を盤面価値関数に基づいて再選定する
//  ・固定ルーチンではなく、盤面状況に応じて方針を変える
//  ・AP予算配分を方針に沿って計画する
//  ・BOSSの大きい性格が方針選定に影響する
//  ・方針の理由を説明可能にする（デバッグ・納得感）
//
//  出力:
//  ・TurnStrategy: 今ターンの方針
//  ・APBudget: AP配分計画（攻撃/建築/移動/予約の目安比率）
//  ・StrategyReason: 方針選定の理由文字列
// =====================================================================
public class TurnStrategyPlanner
{
    // ================================================================
    //  AP予算配分
    // ================================================================
    public struct APBudget
    {
        public float AttackRatio;   // 攻撃系に使うAPの比率 (0-1)
        public float BuildRatio;    // 建築/召喚に使うAPの比率 (0-1)
        public float MoveRatio;     // 移動系に使うAPの比率 (0-1)
        public int   ReservedAP;    // 建築/召喚用に予約するAP

        public override string ToString()
            => $"攻撃={AttackRatio:P0} 建築={BuildRatio:P0} 移動={MoveRatio:P0} 予約={ReservedAP}";
    }

    // ================================================================
    //  戦略決定の結果
    // ================================================================
    public struct StrategyDecision
    {
        public TurnStrategy Strategy;
        public APBudget Budget;
        public string Reason;
    }

    // ================================================================
    //  戦略決定: 盤面を評価して今ターンの方針を決める
    // ================================================================
    public StrategyDecision DecideStrategy(
        AIBoardState board,
        AIPersonality personality,
        AIThreatLevel threatLevel,
        int turnCount)
    {
        var decision = new StrategyDecision();

        // ---- 評価要素を計算 ----
        float crystalHpRatio = board.EnemyCrystalMaxHP > 0
            ? (float)board.EnemyCrystalHP / board.EnemyCrystalMaxHP : 1f;

        bool crystalThreatened = IsCrystalThreatened(board);
        int criticalUnitCount = CountCriticalUnits(board);
        int econBuildingCount = CountEconBuildings(board);
        int processingCount = CountProcessingBuildings(board);
        int houseCount = board.GetBuildingCount(FacilityKind.House);
        bool hasMinimalRaw = econBuildingCount >= 2;
        bool hasBasicEconomy = econBuildingCount >= 4;
        bool hasProcessing = processingCount >= 1;
        bool hasMatureEconomy = hasBasicEconomy && processingCount >= 2 && houseCount >= 1;
        bool econSufficient = hasBasicEconomy && processingCount >= 2 && houseCount >= 1;
        float advantage = board.GetAdvantageRatio();

        // ---- 優先度ベース判定（上位条件が優先） ----

        // 1. クリスタル危機: HP低いまたは敵接近
        if (crystalHpRatio < 0.4f)
        {
            decision.Strategy = TurnStrategy.CrystalDefense;
            decision.Reason = $"クリスタルHP危機({crystalHpRatio:P0})";
        }
        else if (crystalThreatened && crystalHpRatio < 0.6f)
        {
            decision.Strategy = TurnStrategy.CrystalDefense;
            decision.Reason = $"クリスタル付近に敵接近(HP={crystalHpRatio:P0})";
        }
        // 2. 味方瀕死多数: 再編
        else if (criticalUnitCount >= 2)
        {
            decision.Strategy = TurnStrategy.RetreatRegroup;
            decision.Reason = $"瀕死駒{criticalUnitCount}体 → 再編";
        }
        // 3. 経済基盤不足: 建築優先
        else if (!hasMinimalRaw)
        {
            decision.Strategy = TurnStrategy.EconomyBuild;
            decision.Reason = "原料施設不足(最低限もない)";
        }
        else if (!hasBasicEconomy && board.BuildablePositions.Count > 0)
        {
            decision.Strategy = TurnStrategy.EconomyBuild;
            decision.Reason = $"基礎原料不足(原料{econBuildingCount}/4)";
        }
        else if (!hasProcessing && board.BuildablePositions.Count > 0)
        {
            decision.Strategy = TurnStrategy.EconomyBuild;
            decision.Reason = "加工施設なし";
        }
        else if (houseCount == 0 && board.EnemyResources != null
            && board.EnemyResources.Citizen <= 1
            && board.BuildablePositions.Count > 0)
        {
            decision.Strategy = TurnStrategy.EconomyBuild;
            decision.Reason = "住宅なし・市民不足";
        }
        // 4. 経済未成熟期: バランス（建築も並行）
        else if (!econSufficient && turnCount <= 20)
        {
            decision.Strategy = TurnStrategy.Balanced;
            decision.Reason = "経済未成熟期のバランス運用";
        }
        else if (board.AliveEnemyUnits.Count <= 6 && turnCount <= 15)
        {
            decision.Strategy = TurnStrategy.Balanced;
            decision.Reason = "序盤の少数精鋭期";
        }
        // 5. 初接敵
        else if (board.IsFirstContact && board.AlivePlayerUnits.Count > 0)
        {
            decision.Strategy = TurnStrategy.ContactEngage;
            decision.Reason = "初接敵 → 交戦開始";
        }
        // 6. 索敵
        else if (board.AlivePlayerUnits.Count == 0 && board.GetExplorationRatio() < 0.6f
            && hasBasicEconomy)
        {
            decision.Strategy = TurnStrategy.ScoutSearch;
            decision.Reason = $"敵未視認・探索率{board.GetExplorationRatio():P0}";
        }
        // 7. 中盤敵未視認
        else if (turnCount > 10 && board.AlivePlayerUnits.Count == 0)
        {
            decision.Strategy = TurnStrategy.Balanced;
            decision.Reason = "中盤・敵未視認 → バランス";
        }
        // 8. 有利時攻勢
        else if (advantage > 0.25f && board.AlivePlayerUnits.Count > 0)
        {
            decision.Strategy = TurnStrategy.Assault;
            decision.Reason = $"戦力有利({advantage:F2}) → 攻勢";
        }
        // 9. BOSS性格の影響
        else if (personality.ShouldApplyMajorBonus)
        {
            decision.Strategy = DecideByPersonality(personality, advantage, hasMatureEconomy);
            decision.Reason = $"{personality.Major}性格の方針判断";
        }
        else
        {
            decision.Strategy = TurnStrategy.Balanced;
            decision.Reason = "標準バランス";
        }

        // ---- 戦略品質による劣化（チュートリアル〜ノーマル帯） ----
        float quality = threatLevel.StrategyQuality;
        if (quality < 0.9f)
        {
            // 低品質では攻勢をバランスに緩和
            if (quality < 0.5f && decision.Strategy == TurnStrategy.Assault)
            {
                decision.Strategy = TurnStrategy.Balanced;
                decision.Reason += " (低脅威度で攻勢を緩和)";
            }
            // やりこみ帯未満では防衛→バランスに劣化することもある
            if (quality < 0.7f && decision.Strategy == TurnStrategy.CrystalDefense)
            {
                decision.Strategy = TurnStrategy.Balanced;
                decision.Reason += " (低脅威度で防衛判断が甘い)";
            }
        }

        // ---- AP予算配分 ----
        decision.Budget = PlanAPBudget(decision.Strategy, board, econSufficient);

        Debug.Log($"[TurnStrategyPlanner] 方針={decision.Strategy}  " +
                  $"理由=\"{decision.Reason}\"  {decision.Budget}");

        return decision;
    }

    // ================================================================
    //  AP予算配分計画
    //  方針に沿ってAPの使い方を計画する
    // ================================================================
    APBudget PlanAPBudget(TurnStrategy strategy, AIBoardState board, bool econSufficient)
    {
        var budget = new APBudget();

        switch (strategy)
        {
            case TurnStrategy.Assault:
                budget.AttackRatio = 0.6f;
                budget.BuildRatio = 0.1f;
                budget.MoveRatio = 0.3f;
                break;

            case TurnStrategy.CrystalDefense:
                budget.AttackRatio = 0.3f;
                budget.BuildRatio = 0.2f;
                budget.MoveRatio = 0.5f;
                break;

            case TurnStrategy.RetreatRegroup:
                budget.AttackRatio = 0.1f;
                budget.BuildRatio = 0.2f;
                budget.MoveRatio = 0.7f;
                break;

            case TurnStrategy.EconomyBuild:
                budget.AttackRatio = 0.1f;
                budget.BuildRatio = 0.6f;
                budget.MoveRatio = 0.3f;
                break;

            case TurnStrategy.ScoutSearch:
                budget.AttackRatio = 0.2f;
                budget.BuildRatio = 0.1f;
                budget.MoveRatio = 0.7f;
                break;

            case TurnStrategy.ContactEngage:
                budget.AttackRatio = 0.5f;
                budget.BuildRatio = 0.1f;
                budget.MoveRatio = 0.4f;
                break;

            default: // Balanced
                budget.AttackRatio = econSufficient ? 0.35f : 0.2f;
                budget.BuildRatio = econSufficient ? 0.2f : 0.4f;
                budget.MoveRatio = econSufficient ? 0.45f : 0.4f;
                break;
        }

        // AP予約計算: 建築/召喚用にAPを確保する
        budget.ReservedAP = CalcReservedAP(board, strategy);

        return budget;
    }

    // ================================================================
    //  AP予約計算
    // ================================================================
    int CalcReservedAP(AIBoardState board, TurnStrategy strategy)
    {
        int reserved = 0;

        // 経済建築用AP予約
        if (board.BuildablePositions.Count > 0)
        {
            int econCount = CountEconBuildings(board);
            if (econCount < 4)
                reserved = Mathf.Max(reserved, 3); // 最安の原料施設

            int procCount = CountProcessingBuildings(board);
            if (procCount == 0 && econCount >= 2)
                reserved = Mathf.Max(reserved, 5); // 加工施設

            if (board.GetBuildingCount(FacilityKind.House) == 0
                && board.EnemyResources != null && board.EnemyResources.Citizen <= 1)
                reserved = Mathf.Max(reserved, 7); // 住宅

            // AffordableBuildings の最安APコスト
            if (board.AffordableBuildings.Count > 0)
            {
                int cheapest = int.MaxValue;
                foreach (var fk in board.AffordableBuildings)
                {
                    if (FacilityData.Table.TryGetValue(fk, out var info))
                        cheapest = Mathf.Min(cheapest, info.APCost);
                }
                if (cheapest < int.MaxValue)
                    reserved = Mathf.Max(reserved, cheapest);
            }
        }

        // 召喚用AP予約
        if (board.SummonablePositions.Count > 0 && board.AffordableUnits.Count > 0)
        {
            int cheapest = int.MaxValue;
            foreach (var k in board.AffordableUnits)
            {
                if (UnitStaticData.Table.TryGetValue(k, out var info))
                    cheapest = Mathf.Min(cheapest, info.CostAP);
            }
            if (cheapest < int.MaxValue)
                reserved = Mathf.Max(reserved, cheapest);
        }

        // 経済建築戦略では予約を強化
        if (strategy == TurnStrategy.EconomyBuild)
            reserved = Mathf.Max(reserved, 5);

        return reserved;
    }

    // ================================================================
    //  BOSS性格による方針微調整
    // ================================================================
    static TurnStrategy DecideByPersonality(AIPersonality personality, float advantage, bool hasMatureEconomy)
    {
        switch (personality.Major)
        {
            case MajorPersonality.Combat:
                if (advantage > 0f) return TurnStrategy.Assault;
                break;
            case MajorPersonality.Intellect:
                if (advantage < -0.1f) return TurnStrategy.RetreatRegroup;
                break;
            case MajorPersonality.Growth:
                if (!hasMatureEconomy) return TurnStrategy.EconomyBuild;
                break;
        }
        return TurnStrategy.Balanced;
    }

    // ================================================================
    //  ヘルパー
    // ================================================================

    static bool IsCrystalThreatened(AIBoardState board)
    {
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(pu.transform.position, board.EnemyCrystalPos);
            if (d < 4f) return true;
        }
        return false;
    }

    static int CountCriticalUnits(AIBoardState board)
    {
        int count = 0;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (u.MaxHP > 0 && (float)u.HP / u.MaxHP < 0.35f) count++;
        }
        return count;
    }

    static int CountEconBuildings(AIBoardState board)
    {
        return board.GetBuildingCount(FacilityKind.Well)
             + board.GetBuildingCount(FacilityKind.LoggingCamp)
             + board.GetBuildingCount(FacilityKind.Quarry)
             + board.GetBuildingCount(FacilityKind.Field)
             + board.GetBuildingCount(FacilityKind.Mine);
    }

    static int CountProcessingBuildings(AIBoardState board)
    {
        return board.GetBuildingCount(FacilityKind.Bakery);
    }
}
