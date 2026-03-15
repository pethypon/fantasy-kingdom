using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AICommander — 全体指揮AI
//  将棋・チェスのように盤面全体を見て全駒をまとめて動かす
//  1ターンの行動列を組み立て、AP消費しながら順次実行する
// =====================================================================
public class AICommander
{
    readonly AIPersonality _personality;
    readonly AILearning _learning;
    readonly TurnGenerater _turnGen;
    readonly MoveGererater _moveGen;
    readonly AttackPointt _attackPoint;
    readonly BattleSystem _battleSystem;
    readonly VisionGenerater _visionGen;
    readonly APSystem _apSystem;
    readonly UnitSetting _unitSet;
    readonly CrystalSystem _crystalSystem;
    readonly MapCreate _mapCreate;

    AIBoardState _board;

    // 1ターン内で既に行動した駒を追跡
    HashSet<Status> _actedUnits = new HashSet<Status>();

    // ---- 生成（試合開始時に1回） ----
    public AICommander(
        TurnGenerater turnGen, MoveGererater moveGen, AttackPointt attackPoint,
        BattleSystem battleSystem, VisionGenerater visionGen,
        APSystem apSystem, UnitSetting unitSet, CrystalSystem crystalSystem,
        MapCreate mapCreate, MajorPersonality major)
    {
        _turnGen = turnGen;
        _moveGen = moveGen;
        _attackPoint = attackPoint;
        _battleSystem = battleSystem;
        _visionGen = visionGen;
        _apSystem = apSystem;
        _unitSet = unitSet;
        _crystalSystem = crystalSystem;
        _mapCreate = mapCreate;

        _personality = new AIPersonality(major);
        _learning = new AILearning(major == MajorPersonality.Growth);

        Debug.Log($"[AICommander] 初期化完了: 大きい性格={major}");
    }

    public AIPersonality Personality => _personality;
    public AILearning Learning => _learning;

    // ================================================================
    //  ExecuteTurn — 1ターン分の全行動を実行
    //  EnemyMove.Entry() から呼ばれる
    // ================================================================
    public void ExecuteTurn()
    {
        _actedUnits.Clear();
        _board = new AIBoardState(_moveGen, _attackPoint, _apSystem, _unitSet, _crystalSystem);

        int maxIterations = 50; // 無限ループ防止
        int iteration = 0;

        Debug.Log($"[AICommander] ターン開始: AP={_board.EnemyAP}  " +
                  $"敵駒数={_board.AliveEnemyUnits.Count}  味方駒数={_board.AlivePlayerUnits.Count}");

        while (_board.EnemyAP > 0 && iteration < maxIterations)
        {
            iteration++;

            // 盤面情報を更新
            _board.Refresh();
            if (_board.EnemyAP <= 0) break;

            // 全候補行動を評価
            var actions = AIActionEvaluator.EvaluateAll(_personality, _board, _learning);
            if (actions.Count == 0) break;

            // 最良の行動を選択（既に行動済みの駒を除外して有意義な行動を選ぶ）
            AIAction bestAction = SelectBestAction(actions);
            if (bestAction == null || bestAction.ActionType == AIActionType.Wait) break;

            // 実行
            bool success = ExecuteAction(bestAction);
            if (!success) break;

            // 行動済みマーク（同じ駒が連続行動しすぎないよう）
            if (bestAction.Unit != null)
                _actedUnits.Add(bestAction.Unit);
        }

        Debug.Log($"[AICommander] ターン終了: 実行回数={iteration}  残AP={_board.EnemyAP}");
    }

    // ================================================================
    //  行動選択 — 既に行動済み駒のスコアを減衰させつつ最良を選ぶ
    // ================================================================
    AIAction SelectBestAction(List<AIAction> actions)
    {
        AIAction best = null;
        float bestScore = float.MinValue;

        foreach (var action in actions)
        {
            if (action.ActionType == AIActionType.Wait) continue;
            if (action.APCost > _board.EnemyAP) continue;

            float score = action.Score;

            // 既に行動した駒は優先度を下げる（他の駒にAPを回す）
            if (_actedUnits.Contains(action.Unit))
                score *= 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                best = action;
            }
        }

        return best;
    }

    // ================================================================
    //  行動実行
    // ================================================================
    bool ExecuteAction(AIAction action)
    {
        switch (action.ActionType)
        {
            case AIActionType.Move:
                return ExecuteMove(action);
            case AIActionType.Attack:
                return ExecuteAttack(action);
            default:
                Debug.Log($"[AICommander] 未実装アクション: {action.ActionType}");
                return false;
        }
    }

    // ---- 移動実行 ----
    bool ExecuteMove(AIAction action)
    {
        var unit = action.Unit;
        var dest = action.TargetPos;

        if (!_apSystem.CanAct(Team.Enemy, APSystem.ActionType.Move, unit,
                unit.transform.position, dest))
        {
            return false;
        }

        Vector3 oldPos = unit.transform.position;
        Vector3 oldCell = _moveGen.Cell(oldPos);

        // 実際の移動先Y座標をSetPosから取得
        Vector3 actualDest = dest;
        foreach (var sp in _moveGen.mapcreate.SetPos)
        {
            if (Mathf.RoundToInt(sp.x) == Mathf.RoundToInt(dest.x) &&
                Mathf.RoundToInt(sp.z) == Mathf.RoundToInt(dest.z))
            {
                actualDest = new Vector3(sp.x, sp.y - 1f, sp.z);
                break;
            }
        }

        // AP消費
        _board.ConsumeMove(unit, actualDest);

        // 駒を移動
        unit.transform.position = actualDest;
        _moveGen.MoveUpdate(oldCell, _moveGen.Cell(actualDest));

        Debug.Log($"[AICommander] 移動: {unit.kind} {oldCell}→{_moveGen.Cell(actualDest)}");

        // 学習記録
        if (_learning.IsActive)
        {
            // 前進→敵陣方向への移動を記録
            float distBefore = Vector3.Distance(oldPos, _board.PlayerCrystalPos);
            float distAfter = Vector3.Distance(actualDest, _board.PlayerCrystalPos);
            if (distAfter < distBefore)
                _learning.RecordRouteResult(actualDest, true);
        }

        return true;
    }

    // ---- 攻撃実行 ----
    bool ExecuteAttack(AIAction action)
    {
        var unit = action.Unit;
        var target = action.TargetUnit;

        if (target == null || !target.gameObject.activeInHierarchy) return false;

        if (!_apSystem.CanAct(Team.Enemy, APSystem.ActionType.Attack, unit))
            return false;

        // SelectUnit を一時的に設定（BattleSystem が参照する）
        var prevSelect = _turnGen.SelectUnit;
        _turnGen.SelectUnit = unit;
        _battleSystem.target = target;

        int hpBefore = target.HP;

        // AP消費
        _board.ConsumeAttack(unit);

        // ダメージ計算・適用
        _battleSystem.DamageGenerater(_turnGen);

        int hpAfter = target.HP;
        bool killed = hpAfter <= 0;

        Debug.Log($"[AICommander] 攻撃: {unit.kind}→{target.kind}  " +
                  $"DMG={hpBefore - hpAfter}  {(killed ? "撃破!" : $"残HP={hpAfter}")}");

        // 学習記録
        if (_learning.IsActive)
        {
            if (killed)
            {
                // 側面攻撃か正面攻撃かを判定
                Vector3 diff = unit.transform.position - target.transform.position;
                bool isFlanking = Mathf.Abs(diff.x) > Mathf.Abs(diff.z);
                if (isFlanking)
                    _learning.RecordFlankSuccess(target.transform.position);
            }
            else
            {
                // ダメージが少なかった場合、正面攻撃失敗として記録
                int dmgDealt = hpBefore - hpAfter;
                int expectedDmg = Mathf.Max(0, 1 + (unit.ATK / 6) + ((unit.ATK / 2) - (target.DEF / 4)));
                if (dmgDealt < expectedDmg * 0.5f)
                    _learning.RecordFrontalFailure(target.transform.position);
            }
        }

        // SelectUnit を戻す
        _turnGen.SelectUnit = prevSelect;

        return true;
    }

    // ================================================================
    //  外部からの学習イベント通知
    // ================================================================
    // 自軍駒が撃破された時（EnemyMove中以外でも呼べる）
    public void OnAllyUnitKilled(Status unit, AIBoardState board)
    {
        if (!_learning.IsActive || board == null) return;

        // 孤立判定: 最寄り味方が4マス以上離れていたら孤立
        float nearestAlly = float.MaxValue;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy || u == unit) continue;
            float d = Vector3.Distance(unit.transform.position, u.transform.position);
            if (d < nearestAlly) nearestAlly = d;
        }

        if (nearestAlly > 4f)
        {
            _learning.RecordIsolatedDeath(unit.transform.position);
        }
    }
}
