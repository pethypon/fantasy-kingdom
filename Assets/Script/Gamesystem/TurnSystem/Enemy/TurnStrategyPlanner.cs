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
        board.Governor?.Evaluate(board);
        bool econSufficient = EconomyHelper.IsEconomySufficient(board);
        int supplyReserve = board.Governor?.GetRecoveryReserveAP(board) ?? 0;
        bool recoveryInvestment = board.Governor?.Mode == StrategicMode.EconomicRecovery && supplyReserve > 0;
        float advantage = board.GetAdvantageRatio();
        // The principal crystal is a live strategic target even when its defenders are gone.
        // Memories still guide reconnaissance, but cannot reveal an unseen or destroyed objective.
        bool opponentContact = board.HasPrincipalOpponentContact
            || board.CanUsePlayerCrystalAsTarget() && board.PlayerCrystalHP > 0;
        bool territoryDeployment = board.Governor?.HasActiveTerritoryAdvance == true;

        // ---- 優先度ベース判定（上位条件が優先） ----

        // 1. クリスタル危機: HP低いまたは敵接近
        if (board.Governor?.Mode == StrategicMode.EmergencyDefense || crystalHpRatio < 0.4f)
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
        else if (recoveryInvestment)
        {
            decision.Strategy = TurnStrategy.EconomyBuild;
            decision.Reason = "不足する供給を回復する実行可能な計画あり";
        }
        else if (opponentContact && econSufficient && advantage > 0.25f)
        {
            decision.Strategy = TurnStrategy.Assault;
            decision.Reason = $"供給安定・戦力有利({advantage:F2}) → 攻勢";
        }
        else if (opponentContact)
        {
            decision.Strategy = TurnStrategy.ContactEngage;
            decision.Reason = "敵部隊または生存拠点を視認: 経済の完成を待たず交戦";
        }
        else if (territoryDeployment)
        {
            // Territory confirms an approach region, not contact with an enemy
            // unit or a guessed crystal. The governor evaluates safe army moves.
            decision.Strategy = TurnStrategy.Balanced;
            decision.Reason = "発見済みの敵領土へ進軍・再偵察（敵兵・拠点は未確認）";
        }
        else if (board.GetExplorationRatio() < 0.6f)
        {
            decision.Strategy = TurnStrategy.ScoutSearch;
            decision.Reason = "生産を待つ間も探索・領域の確認を継続";
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
        // 中盤・敵未視認（接敵と索敵の条件は上で処理済み）
        else if (turnCount > 10)
        {
            decision.Strategy = TurnStrategy.Balanced;
            decision.Reason = "中盤・敵未視認 → バランス";
        }
        // BOSS性格の影響
        else if (personality.ShouldApplyMajorBonus)
        {
            decision.Strategy = DecideByPersonality(personality, advantage, recoveryInvestment);
            decision.Reason = $"{personality.Major}性格の方針判断";
        }
        else
        {
            decision.Strategy = TurnStrategy.Balanced;
            decision.Reason = "標準バランス";
        }

        // ---- AP予算配分 ----
        decision.Budget = PlanAPBudget(decision.Strategy, board, econSufficient);

        { if (DevelopmentLog.IsEnabled(AILogLevel.Verbose)) DevelopmentLog.Log($"[TurnStrategyPlanner] 方針={decision.Strategy}  " +
                  $"理由=\"{decision.Reason}\"  {decision.Budget}"); }

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
        => board.Governor?.GetRecoveryReserveAP(board) ?? 0;

    // ================================================================
    //  BOSS性格による方針微調整
    // ================================================================
    static TurnStrategy DecideByPersonality(AIPersonality personality, float advantage, bool recoveryInvestment)
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
                if (recoveryInvestment) return TurnStrategy.EconomyBuild;
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
