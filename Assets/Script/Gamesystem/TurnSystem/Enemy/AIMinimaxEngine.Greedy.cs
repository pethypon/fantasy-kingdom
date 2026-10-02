using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIMinimaxEngine.Greedy — 貪欲ターンシミュレーション + ソートヘルパー
// =====================================================================
public partial class AIMinimaxEngine
{
    // ================================================================
    //  貪欲ターンシミュレーション
    //  あるチームの1ターン分をgreedy(最高スコアの行動を順次実行)で完了する
    // ================================================================
    IEnumerator SimulateGreedyTurnSteps(SimBoardState board, Team team)
    {
        _greedyActedUnits.Clear();

        for (int step = 0; step < _greedyActionsPerTurn; step++)
        {
            if (SearchElapsedMs >= _timeBudgetMs) break;
            int ap = board.GetAP(team);
            if (ap <= 0) break;

            // ゲーム終了チェック
            if (board.IsTerminal()) break;

            yield return null;
            yield return SimActionGenerator.GenerateAllActionsSteps(board, team, greedyActions);
            var actions = greedyActions.Actions;
            yield return null;
            if (actions.Count == 0) break;

            // 最高スコアの行動を選択
            SimAction best = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < actions.Count; i++)
            {
                if ((i & 15) == 0) yield return null;
                var a = actions[i];
                if (a.APCost > ap) continue;

                float score = SimActionGenerator.QuickScore(a, board);
                if (ResponseModel != null) score += ResponseModel.Preference(a, board) * 12;

                // 既に行動した駒は割引（ただし確殺可能な攻撃は例外）
                if (a.UnitId >= 0 && _greedyActedUnits.Contains(a.UnitId))
                {
                    bool isKillShot = false;
                    if (a.Type == SimActionType.Attack && a.TargetUnitId >= 0)
                    {
                        var attacker = board.GetUnit(a.UnitId);
                        var target = board.GetUnit(a.TargetUnitId);
                        if (attacker != null && target != null)
                        {
                            int dmg = SimBoardState.CalcDamage(attacker, target);
                            if (dmg >= target.HP) isKillShot = true;
                        }
                    }
                    if (!isKillShot) score *= 0.4f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = a;
                }
            }

            if (best == null || bestScore < -5f) break; // 有益な行動がなければ終了

            board.ApplyAction(best);
            if (best.UnitId >= 0)
                _greedyActedUnits.Add(best.UnitId);
        }
    }

    // ================================================================
    //  QuickScore事前計算＋降順ソート（比較中のQuickScore再計算を排除）
    //  N個の行動に対してQuickScoreがN回で済む（Sort比較でN log N回→N回に削減）
    // ================================================================
    IEnumerator PrecomputeAndSortSteps(List<SimAction> actions, SimBoardState board)
    {
        int count = actions.Count;
        // バッファサイズ確保
        while (_sortScoreBuffer.Count < count) _sortScoreBuffer.Add(0f);
        for (int i = 0; i < count; i++)
        {
            if ((i & 15) == 0) yield return null;
            _sortScoreBuffer[i] = SimActionGenerator.QuickScore(actions[i], board)
                + (ResponseModel != null ? ResponseModel.Preference(actions[i], board) * 12 : 0);
        }

        // Stable bottom-up merge sort: O(n log n), buffers are retained between searches.
        // Equal scores preserve generation order, as the former insertion sort did.
        while (_mergeActions.Count < count) { _mergeActions.Add(null); _mergeScores.Add(0f); }
        for (int width = 1; width < count; width *= 2)
        {
            for (int left = 0; left < count; left += width * 2)
            {
                int middle = System.Math.Min(left + width, count);
                int right = System.Math.Min(left + width * 2, count);
                int a = left, b = middle;
                for (int k = left; k < right; k++)
                {
                    if ((k & 31) == 0) yield return null;
                    int source = a < middle && (b >= right || _sortScoreBuffer[a] >= _sortScoreBuffer[b]) ? a++ : b++;
                    _mergeActions[k] = actions[source];
                    _mergeScores[k] = _sortScoreBuffer[source];
                }
            }
            for (int k = 0; k < count; k++)
            {
                if ((k & 31) == 0) yield return null;
                actions[k] = _mergeActions[k];
                _sortScoreBuffer[k] = _mergeScores[k];
            }
        }
    }
}
