using UnityEngine;

// =====================================================================
//  AICommander.Hierarchical — 師団長制AIフェーズ
// =====================================================================
public partial class AICommander
{
    // ================================================================
    //  師団長制AI: 階層的なターン処理
    //  1. 師団長を選出し、兵を割り当てる
    //  2. 各師団長が提案を生成
    //  3. 王が競合解決・予算予約・戦略整合を評価して採択
    //  4. 採択された行動を実行
    //  ML機能は師団長制モードでは無効化される。
    // ================================================================
    void ExecuteHierarchicalPhase(ref TurnStats turnStats)
    {
        Debug.Log("[AICommander] === 師団長制フェーズ開始 ===");

        // 1. 師団長の選出
        _kingCommanderSystem.SelectDivisionCommanders(_board.AliveEnemyUnits, _turnCount);

        if (!_kingCommanderSystem.HasDivisions)
        {
            Debug.Log("[AICommander] 師団長なし → 全ユニット王直轄で通常処理");
            return;
        }

        // 2. 兵の割り当て
        _kingCommanderSystem.AssignUnits(_board.AliveEnemyUnits, _board);

        // 3. 各師団長の提案を収集
        var proposals = _kingCommanderSystem.CollectProposals(
            _board, _learning, _currentStrategy, _roleAssigner, _threatLevel);

        // 4. 王による採択処理（競合解決 + 予算予約 + 戦略整合評価）
        int availableAP = _board.EnemyAP;
        var acceptedActions = _kingCommanderSystem.EvaluateAndAcceptProposals(
            proposals, _board, _currentStrategy, availableAP);

        Debug.Log($"[AICommander] 師団長提案から{acceptedActions.Count}件を採択  AP残={availableAP}");

        // 5. 採択された行動を実行
        int executed = 0;
        foreach (var action in acceptedActions)
        {
            // AP再チェック（実行中にAPが変化する可能性）
            _board.Refresh();
            if (_board.EnemyAP < action.APCost)
            {
                Debug.Log($"[AICommander] 師団行動スキップ(AP不足): {action}  " +
                          $"必要AP={action.APCost} 残AP={_board.EnemyAP}");
                continue;
            }

            bool success = _actionExecutor.Execute(action, _board);
            if (success)
            {
                executed++;
                turnStats.Record(action.ActionType);

                if (action.Unit != null)
                    _actedUnits.Add(action.Unit);

                Debug.Log($"[AICommander] 師団行動実行: {action}");
            }
            else
            {
                Debug.Log($"[AICommander] 師団行動失敗: {action}");
            }
        }

        // 不採用理由ログ出力
        foreach (var proposal in proposals)
        {
            if (!proposal.Accepted && proposal.Rejection.HasValue)
            {
                Debug.Log($"[AICommander] 【不採用ログ】 {proposal.DivisionName}  " +
                          $"理由コード={proposal.Rejection.Value}  " +
                          $"詳細=\"{proposal.RejectionDetail}\"  " +
                          $"提案スコア={proposal.TotalScore:F1}  提案AP={proposal.TotalAPCost}");
            }
        }

        Debug.Log($"[AICommander] === 師団長制フェーズ完了: 実行={executed}/{acceptedActions.Count} ===");
    }
}
