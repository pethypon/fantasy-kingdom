using UnityEngine;

// =====================================================================
//  AICommander.Strategy — ターン方針決定とフォールバック
// =====================================================================
public partial class AICommander
{
    // ================================================================
    //  ターン方針の決定
    //  盤面を見て「今ターン何を重視するか」を1つ選ぶ
    // ================================================================
    TurnStrategy DecideStrategy(AIBoardState board)
    {
        float crystalHpRatio = board.EnemyCrystalMaxHP > 0
            ? (float)board.EnemyCrystalHP / board.EnemyCrystalMaxHP : 1f;

        // クリスタルが危険なら最優先で防衛
        if (crystalHpRatio < 0.4f)
            return TurnStrategy.CrystalDefense;

        // クリスタル付近に敵がいる場合も防衛
        bool crystalThreatened = false;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(pu.transform.position, board.EnemyCrystalPos);
            if (d < 4f) { crystalThreatened = true; break; }
        }
        if (crystalThreatened && crystalHpRatio < 0.6f)
            return TurnStrategy.CrystalDefense;

        // 味方に瀕死が多い → 再編
        int criticalCount = 0;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (u.MaxHP > 0 && (float)u.HP / u.MaxHP < 0.35f) criticalCount++;
        }
        if (criticalCount >= 2)
            return TurnStrategy.RetreatRegroup;

        // 経済基盤の充実度で判断（原料 + 加工 + 住宅の全体で見る）
        int econBuildingCount = EconomyHelper.CountEconBuildings(board);
        int processingCount = EconomyHelper.CountProcessingBuildings(board);
        int houseCount = board.GetBuildingCount(FacilityKind.House);
        bool hasMinimalRaw = econBuildingCount >= 2;       // 最低限の原料施設
        bool hasBasicEconomy = econBuildingCount >= 4;     // 基礎原料が充実
        bool hasProcessing = processingCount >= 1;         // 加工施設あり
        bool hasMatureEconomy = hasBasicEconomy && processingCount >= 2 && houseCount >= 1;
        bool econSufficient = EconomyHelper.IsEconomySufficient(board);

        // 原料施設が最低限もない → 経済最優先
        if (!hasMinimalRaw)
            return TurnStrategy.EconomyBuild;

        // 基礎原料が不十分 → 経済優先（ターン制限なし）
        // ※ AffordableBuildings が空でも EconomyBuild を選ぶ
        //   （建築先行フェーズやフォールバックで補完する）
        if (!hasBasicEconomy && board.BuildablePositions.Count > 0)
            return TurnStrategy.EconomyBuild;

        // 加工施設が1棟もない → 加工施設を建てる
        if (!hasProcessing && board.BuildablePositions.Count > 0)
            return TurnStrategy.EconomyBuild;

        // 住宅がなく市民不足 → 経済優先
        if (houseCount == 0 && board.EnemyResources != null && board.EnemyResources.Citizen <= 1
            && board.BuildablePositions.Count > 0)
            return TurnStrategy.EconomyBuild;

        // 経済が十分に成熟するまでBalanced（建築も並行する）
        // 軍が少ない時期もBalancedで建築+召喚を両立
        if (!econSufficient && board.TurnCount <= 20)
            return TurnStrategy.Balanced;

        if (board.AliveEnemyUnits.Count <= 6 && board.TurnCount <= 15)
            return TurnStrategy.Balanced;

        // ★ 初接敵: 敵を見つけた直後は交戦開始を優先
        if (board.IsFirstContact && board.AlivePlayerUnits.Count > 0)
            return TurnStrategy.ContactEngage;

        // ★ 索敵戦略: 敵が見えず、探索率が低い場合
        if (board.AlivePlayerUnits.Count == 0 && board.GetExplorationRatio() < 0.6f)
        {
            // 経済基盤がある程度あれば索敵に出る
            if (hasBasicEconomy)
                return TurnStrategy.ScoutSearch;
        }

        // 中盤以降は敵が見えなくても Balanced に移行（攻めの準備）
        if (board.TurnCount > 10 && board.AlivePlayerUnits.Count == 0)
            return TurnStrategy.Balanced;

        // 有利時は攻勢
        float advantage = board.GetAdvantageRatio();
        if (advantage > 0.25f && board.AlivePlayerUnits.Count > 0)
            return TurnStrategy.Assault;

        // 大きい性格が影響
        if (_personality.ShouldApplyMajorBonus)
        {
            switch (_personality.Major)
            {
                case MajorPersonality.Combat:
                    if (advantage > 0f) return TurnStrategy.Assault;
                    break;
                case MajorPersonality.Intellect:
                    if (advantage < -0.1f) return TurnStrategy.RetreatRegroup;
                    break;
                case MajorPersonality.Growth:
                    if (!hasMatureEconomy)
                        return TurnStrategy.EconomyBuild;
                    break;
            }
        }

        return TurnStrategy.Balanced;
    }

    // ================================================================
    //  戦略フォールバック: 現戦略が行き詰まった時に別の戦略を試す
    // ================================================================
    bool TryFallbackStrategy()
    {
        // フォールバック優先順
        TurnStrategy[] fallbackOrder = {
            TurnStrategy.Balanced,
            TurnStrategy.ContactEngage,
            TurnStrategy.ScoutSearch,
            TurnStrategy.EconomyBuild,
            TurnStrategy.Assault,
            TurnStrategy.RetreatRegroup,
            TurnStrategy.CrystalDefense
        };

        foreach (var strategy in fallbackOrder)
        {
            if (_triedStrategies.Contains(strategy)) continue;
            _currentStrategy = strategy;
            _triedStrategies.Add(strategy);

            // AP予算を新戦略に合わせて再計画
            var newDecision = _strategyPlanner.DecideStrategy(_board, _personality, _threatLevel, _turnCount);
            _apBudget = newDecision.Budget;

            // 戦略変更時にロール再割当
            if (_threatLevel.UseRoleAssignment)
                _roleAssigner.AssignRoles(_board, _currentStrategy, _personality);

            return true;
        }
        return false;
    }
}
