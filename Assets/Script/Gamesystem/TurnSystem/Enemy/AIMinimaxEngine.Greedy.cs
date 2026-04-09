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
    void SimulateGreedyTurn(SimBoardState board, Team team)
    {
        _greedyActedUnits.Clear();

        for (int step = 0; step < _greedyActionsPerTurn; step++)
        {
            int ap = board.GetAP(team);
            if (ap <= 0) break;

            // ゲーム終了チェック
            if (board.IsTerminal()) break;

            var actions = SimActionGenerator.GenerateAllActions(board, team);
            if (actions.Count == 0) break;

            // 最高スコアの行動を選択
            SimAction best = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                if (a.APCost > ap) continue;

                float score = SimActionGenerator.QuickScore(a, board);

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
    void PrecomputeAndSort(List<SimAction> actions, SimBoardState board)
    {
        int count = actions.Count;
        // バッファサイズ確保
        while (_sortScoreBuffer.Count < count) _sortScoreBuffer.Add(0f);
        for (int i = 0; i < count; i++)
            _sortScoreBuffer[i] = SimActionGenerator.QuickScore(actions[i], board);

        // 挿入ソート（候補数が少ないため O(n^2) でも高速、GCゼロ）
        for (int i = 1; i < count; i++)
        {
            var keyAction = actions[i];
            float keyScore = _sortScoreBuffer[i];
            int j = i - 1;
            while (j >= 0 && _sortScoreBuffer[j] < keyScore)
            {
                actions[j + 1] = actions[j];
                _sortScoreBuffer[j + 1] = _sortScoreBuffer[j];
                j--;
            }
            actions[j + 1] = keyAction;
            _sortScoreBuffer[j + 1] = keyScore;
        }
    }
}
