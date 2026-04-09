using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIMinimaxEngine.Search — Min/Max 再帰探索 (Alpha-Beta 枝刈り)
// =====================================================================
public partial class AIMinimaxEngine
{
    // ================================================================
    //  Min探索 (Playerの応手): AIにとって最悪のスコアを返す
    // ================================================================
    float MinSearch(SimBoardState board, int depth, int maxDepth, float alpha, float beta)
    {
        if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs)
            return SimBoardEvaluator.Evaluate(board);

        // ゲーム終了チェック
        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // ターン遷移: Player のターン開始をシミュレーション
        board.SimulateTurnTransition(Team.Player);

        // ゲーム終了チェック（DoTで死亡した場合）
        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // トランスポジションテーブルルックアップ
        long hash = BoardHash(board);
        int remainingDepth = maxDepth - depth;
        TTEntry ttEntry;
        if (_transTable.TryGetValue(hash, out ttEntry) && ttEntry.Depth >= remainingDepth)
        {
            if (ttEntry.Flag == TTFlag.Exact) return ttEntry.Score;
            if (ttEntry.Flag == TTFlag.UpperBound && ttEntry.Score <= alpha) return ttEntry.Score;
            if (ttEntry.Flag == TTFlag.LowerBound && ttEntry.Score >= beta) { _pruned++; return ttEntry.Score; }
        }

        // Playerの候補行動を生成
        var actions = SimActionGenerator.GenerateAllActions(board, Team.Player);
        if (actions.Count == 0)
        {
            if (depth < maxDepth)
                return MaxSearch(board, depth + 1, maxDepth, alpha, beta);
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // 手順序: QuickScoreを事前計算してソート（比較時の再計算を排除）
        PrecomputeAndSort(actions, board);

        // キラームーブを先頭に移動
        if (depth < _killerMoves.Length && _killerMoves[depth] != null)
        {
            var killer = _killerMoves[depth];
            for (int i = 1; i < actions.Count; i++)
            {
                if (actions[i].UnitId == killer.UnitId && actions[i].Type == killer.Type
                    && actions[i].TargetPos == killer.TargetPos)
                {
                    var tmp = actions[i];
                    actions[i] = actions[0];
                    actions[0] = tmp;
                    break;
                }
            }
        }

        int limit = Mathf.Min(_candidateLimit, actions.Count);
        float minScore = float.MaxValue;
        SimAction bestAction = null;

        for (int i = 0; i < limit; i++)
        {
            if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs) break;

            var boardCopy = board.Clone();
            boardCopy.ApplyAction(actions[i]);

            // Playerの残りターンをgreedyに実行
            SimulateGreedyTurn(boardCopy, Team.Player);

            float score;
            if (depth < maxDepth)
            {
                score = MaxSearch(boardCopy, depth + 1, maxDepth, alpha, beta);
            }
            else
            {
                score = SimBoardEvaluator.Evaluate(boardCopy);
                _nodesEvaluated++;
            }

            SimBoardPool.ReturnBoard(boardCopy);

            if (score < minScore)
            {
                minScore = score;
                bestAction = actions[i];
            }

            // Beta枝刈り
            if (score <= alpha)
            {
                _pruned++;
                break;
            }
            if (score < beta) beta = score;
        }

        // キラームーブ記録
        if (bestAction != null && depth < _killerMoves.Length)
            _killerMoves[depth] = bestAction;

        float result = minScore == float.MaxValue ? SimBoardEvaluator.Evaluate(board) : minScore;

        // トランスポジションテーブルストア
        if (_transTable.Count < MaxTTSize)
        {
            TTFlag flag = TTFlag.Exact;
            if (result <= alpha) flag = TTFlag.UpperBound;
            else if (result >= beta) flag = TTFlag.LowerBound;
            _transTable[hash] = new TTEntry { Score = result, Depth = remainingDepth, Flag = flag };
        }

        return result;
    }

    // ================================================================
    //  Max探索 (AIの再応手): AIにとって最善のスコアを返す
    // ================================================================
    float MaxSearch(SimBoardState board, int depth, int maxDepth, float alpha, float beta)
    {
        if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs)
            return SimBoardEvaluator.Evaluate(board);

        // ゲーム終了チェック
        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // ターン遷移: AI(Enemy)のターン開始をシミュレーション
        board.SimulateTurnTransition(Team.Enemy);

        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // トランスポジションテーブルルックアップ
        long hash = BoardHash(board);
        int remainingDepth = maxDepth - depth;
        TTEntry ttEntry;
        if (_transTable.TryGetValue(hash, out ttEntry) && ttEntry.Depth >= remainingDepth)
        {
            if (ttEntry.Flag == TTFlag.Exact) return ttEntry.Score;
            if (ttEntry.Flag == TTFlag.LowerBound && ttEntry.Score >= beta) { _pruned++; return ttEntry.Score; }
            if (ttEntry.Flag == TTFlag.UpperBound && ttEntry.Score <= alpha) return ttEntry.Score;
        }

        var actions = SimActionGenerator.GenerateAllActions(board, Team.Enemy);
        if (actions.Count == 0)
        {
            _nodesEvaluated++;
            return SimBoardEvaluator.Evaluate(board);
        }

        // 手順序: QuickScoreを事前計算してソート（比較時の再計算を排除）
        PrecomputeAndSort(actions, board);

        // キラームーブ
        if (depth < _killerMoves.Length && _killerMoves[depth] != null)
        {
            var killer = _killerMoves[depth];
            for (int i = 1; i < actions.Count; i++)
            {
                if (actions[i].UnitId == killer.UnitId && actions[i].Type == killer.Type
                    && actions[i].TargetPos == killer.TargetPos)
                {
                    var tmp = actions[i];
                    actions[i] = actions[0];
                    actions[0] = tmp;
                    break;
                }
            }
        }

        int limit = Mathf.Min(_candidateLimit, actions.Count);
        float maxScore = float.MinValue;
        SimAction bestAction = null;

        for (int i = 0; i < limit; i++)
        {
            if (_stopwatch.ElapsedMilliseconds > _timeBudgetMs) break;

            var boardCopy = board.Clone();
            boardCopy.ApplyAction(actions[i]);

            SimulateGreedyTurn(boardCopy, Team.Enemy);

            float score;
            if (depth < maxDepth)
            {
                score = MinSearch(boardCopy, depth + 1, maxDepth, alpha, beta);
            }
            else
            {
                score = SimBoardEvaluator.Evaluate(boardCopy);
                _nodesEvaluated++;
            }

            SimBoardPool.ReturnBoard(boardCopy);

            if (score > maxScore)
            {
                maxScore = score;
                bestAction = actions[i];
            }

            // Alpha枝刈り
            if (score >= beta)
            {
                _pruned++;
                break;
            }
            if (score > alpha) alpha = score;
        }

        if (bestAction != null && depth < _killerMoves.Length)
            _killerMoves[depth] = bestAction;

        float result = maxScore == float.MinValue ? SimBoardEvaluator.Evaluate(board) : maxScore;

        // トランスポジションテーブルストア
        if (_transTable.Count < MaxTTSize)
        {
            TTFlag flag = TTFlag.Exact;
            if (result >= beta) flag = TTFlag.LowerBound;
            else if (result <= alpha) flag = TTFlag.UpperBound;
            _transTable[hash] = new TTEntry { Score = result, Depth = remainingDepth, Flag = flag };
        }

        return result;
    }
}
