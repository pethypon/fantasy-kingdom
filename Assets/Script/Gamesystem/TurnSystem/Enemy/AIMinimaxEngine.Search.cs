using System.Collections;
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
    IEnumerator MinSearchSteps(SimBoardState board, int depth, int maxDepth, float alpha, float beta)
    {
        if (SearchElapsedMs >= _timeBudgetMs)
            { _nodeScore = SimBoardEvaluator.Evaluate(board); yield break; }

        // ゲーム終了チェック
        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            { _nodeScore = SimBoardEvaluator.Evaluate(board); yield break; }
        }

        // ターン遷移: Player のターン開始をシミュレーション
        yield return null;
        board.SimulateTurnTransition(Team.Player);

        // ゲーム終了チェック（DoTで死亡した場合）
        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            { _nodeScore = SimBoardEvaluator.Evaluate(board); yield break; }
        }

        // トランスポジションテーブルルックアップ
        long hash = BoardHash(board);
        int remainingDepth = maxDepth - depth;
        TTEntry ttEntry;
        if (_transTable.TryGetValue(hash, out ttEntry) && ttEntry.Depth >= remainingDepth)
        {
            if (ttEntry.Flag == TTFlag.Exact) { _nodeScore = ttEntry.Score; yield break; }
            if (ttEntry.Flag == TTFlag.UpperBound && ttEntry.Score <= alpha) { _nodeScore = ttEntry.Score; yield break; }
            if (ttEntry.Flag == TTFlag.LowerBound && ttEntry.Score >= beta) { _pruned++; { _nodeScore = ttEntry.Score; yield break; } }
        }

        // Playerの候補行動を生成
        var buffer = ActionsAt(depth);
        yield return SimActionGenerator.GenerateAllActionsSteps(board, Team.Player, buffer);
        var actions = buffer.Actions;
        if (actions.Count == 0)
        {
            if (depth < maxDepth)
                { yield return MaxSearchSteps(board, depth + 1, maxDepth, alpha, beta); yield break; }
            _nodesEvaluated++;
            { _nodeScore = SimBoardEvaluator.Evaluate(board); yield break; }
        }

        // 手順序: QuickScoreを事前計算してソート（比較時の再計算を排除）
        yield return PrecomputeAndSortSteps(actions, board);

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
            if (SearchElapsedMs >= _timeBudgetMs) break;

            yield return null;
            var boardCopy = board.Clone();
            float score;
            try
            {
            boardCopy.ApplyAction(actions[i]);

            // Playerの残りターンをgreedyに実行
            yield return SimulateGreedyTurnSteps(boardCopy, Team.Player);

            if (depth < maxDepth)
            {
                yield return MaxSearchSteps(boardCopy, depth + 1, maxDepth, alpha, beta);
                score = _nodeScore;
            }
            else
            {
                score = SimBoardEvaluator.Evaluate(boardCopy);
                _nodesEvaluated++;
            }

            }
            finally { SimBoardPool.ReturnBoard(boardCopy); }

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
            StoreKiller(depth, bestAction);

        float result = minScore == float.MaxValue ? SimBoardEvaluator.Evaluate(board) : minScore;

        // トランスポジションテーブルストア
        if (_transTable.Count < MaxTTSize)
        {
            TTFlag flag = TTFlag.Exact;
            if (result <= alpha) flag = TTFlag.UpperBound;
            else if (result >= beta) flag = TTFlag.LowerBound;
            _transTable[hash] = new TTEntry { Score = result, Depth = remainingDepth, Flag = flag };
        }

        { _nodeScore = result; yield break; }
    }

    // ================================================================
    //  Max探索 (AIの再応手): AIにとって最善のスコアを返す
    // ================================================================
    IEnumerator MaxSearchSteps(SimBoardState board, int depth, int maxDepth, float alpha, float beta)
    {
        if (SearchElapsedMs >= _timeBudgetMs)
            { _nodeScore = SimBoardEvaluator.Evaluate(board); yield break; }

        // ゲーム終了チェック
        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            { _nodeScore = SimBoardEvaluator.Evaluate(board); yield break; }
        }

        // ターン遷移: AI(Enemy)のターン開始をシミュレーション
        yield return null;
        board.SimulateTurnTransition(Team.Enemy);

        if (board.IsTerminal())
        {
            _nodesEvaluated++;
            { _nodeScore = SimBoardEvaluator.Evaluate(board); yield break; }
        }

        // トランスポジションテーブルルックアップ
        long hash = BoardHash(board);
        int remainingDepth = maxDepth - depth;
        TTEntry ttEntry;
        if (_transTable.TryGetValue(hash, out ttEntry) && ttEntry.Depth >= remainingDepth)
        {
            if (ttEntry.Flag == TTFlag.Exact) { _nodeScore = ttEntry.Score; yield break; }
            if (ttEntry.Flag == TTFlag.LowerBound && ttEntry.Score >= beta) { _pruned++; { _nodeScore = ttEntry.Score; yield break; } }
            if (ttEntry.Flag == TTFlag.UpperBound && ttEntry.Score <= alpha) { _nodeScore = ttEntry.Score; yield break; }
        }

        var buffer = ActionsAt(depth);
        yield return SimActionGenerator.GenerateAllActionsSteps(board, Team.Enemy, buffer);
        var actions = buffer.Actions;
        if (actions.Count == 0)
        {
            _nodesEvaluated++;
            { _nodeScore = SimBoardEvaluator.Evaluate(board); yield break; }
        }

        // 手順序: QuickScoreを事前計算してソート（比較時の再計算を排除）
        yield return PrecomputeAndSortSteps(actions, board);

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
            if (SearchElapsedMs >= _timeBudgetMs) break;

            yield return null;
            var boardCopy = board.Clone();
            float score;
            try
            {
            boardCopy.ApplyAction(actions[i]);

            yield return SimulateGreedyTurnSteps(boardCopy, Team.Enemy);

            if (depth < maxDepth)
            {
                yield return MinSearchSteps(boardCopy, depth + 1, maxDepth, alpha, beta);
                score = _nodeScore;
            }
            else
            {
                score = SimBoardEvaluator.Evaluate(boardCopy);
                _nodesEvaluated++;
            }

            }
            finally { SimBoardPool.ReturnBoard(boardCopy); }

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
            StoreKiller(depth, bestAction);

        float result = maxScore == float.MinValue ? SimBoardEvaluator.Evaluate(board) : maxScore;

        // トランスポジションテーブルストア
        if (_transTable.Count < MaxTTSize)
        {
            TTFlag flag = TTFlag.Exact;
            if (result >= beta) flag = TTFlag.LowerBound;
            else if (result <= alpha) flag = TTFlag.UpperBound;
            _transTable[hash] = new TTEntry { Score = result, Depth = remainingDepth, Flag = flag };
        }

        { _nodeScore = result; yield break; }
    }
}
