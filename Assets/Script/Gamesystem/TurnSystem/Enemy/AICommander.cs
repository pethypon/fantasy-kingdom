using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AICommander — 全体指揮AI
//  将棋・チェスのように盤面全体を見て全駒をまとめて動かす
//  1ターンの行動列を組み立て、AP消費しながら順次実行する
//
//  【動作確認】Unity Console で以下のログを確認:
//    [AICommander] 初期化完了     → ゲーム開始時に1回出る
//    [AICommander] ターン開始     → 敵ターンごとに出る
//    [AICommander] 視界内敵駒     → 視界制限が効いているか確認
//    [AICommander] 候補行動       → 評価されたアクション一覧
//    [AICommander] 選択行動       → 実際に選ばれたアクション
//    [AICommander] 移動           → 移動実行
//    [AICommander] 攻撃           → 攻撃実行
//    [AICommander] ターン終了     → ターン終了時の統計
//    [AIPersonality]              → 性格パラメータ（ゲーム開始時）
//    [AILearning]                 → 学習イベント（成長型のみ）
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

    // 統計（動作確認用）
    int _totalMoves = 0;
    int _totalAttacks = 0;
    int _totalKills = 0;
    int _turnCount = 0;

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

        Debug.Log("=== [AICommander] ==============================");
        Debug.Log($"[AICommander] 初期化完了");
        Debug.Log($"[AICommander] 大きい性格 = {major}");
        Debug.Log($"[AICommander] 慎重性={_personality.Traits.Caution}  " +
                  $"指揮性={_personality.Traits.Command}  " +
                  $"執着性={_personality.Traits.Obsession}");
        Debug.Log($"[AICommander] 防衛性={_personality.Traits.Defense}  " +
                  $"戦術性={_personality.Traits.Tactics}  " +
                  $"発展性={_personality.Traits.Development}");
        Debug.Log($"[AICommander] 合計={_personality.Traits.Total}pt  " +
                  $"学習={(_learning.IsActive ? "有効" : "無効")}");
        Debug.Log("=== [AICommander] ==============================");
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
        _turnCount++;
        _board = new AIBoardState(_moveGen, _attackPoint, _apSystem, _unitSet, _crystalSystem, _visionGen);

        int maxIterations = 50; // 無限ループ防止
        int iteration = 0;
        int turnMoves = 0;
        int turnAttacks = 0;
        int turnKills = 0;

        Debug.Log($"--- [AICommander] ターン{_turnCount}開始 ---");
        Debug.Log($"[AICommander] AP={_board.EnemyAP}  " +
                  $"自軍駒数={_board.AliveEnemyUnits.Count}  " +
                  $"視界内の敵駒数={_board.AlivePlayerUnits.Count}");
        Debug.Log($"[AICommander] 敵クリスタル視認={_board.PlayerCrystalVisible}  " +
                  $"自クリスタルHP={_board.EnemyCrystalHP}/{_board.EnemyCrystalMaxHP}  " +
                  $"有利度={_board.GetAdvantageRatio():F2}");

        // 視界内のプレイヤー駒一覧
        if (_board.AlivePlayerUnits.Count > 0)
        {
            string visibleUnits = string.Join(", ",
                _board.AlivePlayerUnits.Select(u =>
                    $"{u.kind}(HP{u.HP} @{_moveGen.Cell(u.transform.position)})"));
            Debug.Log($"[AICommander] 視界内敵駒: {visibleUnits}");
        }
        else
        {
            Debug.Log("[AICommander] 視界内に敵駒なし → 探索的移動のみ");
        }

        while (_board.EnemyAP > 0 && iteration < maxIterations)
        {
            iteration++;

            // 盤面情報を更新
            _board.Refresh();
            if (_board.EnemyAP <= 0) break;

            // 全候補行動を評価
            var actions = AIActionEvaluator.EvaluateAll(_personality, _board, _learning);
            if (actions.Count == 0)
            {
                Debug.Log("[AICommander] 候補行動なし → ターン終了");
                break;
            }

            // 上位3件の候補をログ出力
            int logCount = Mathf.Min(3, actions.Count);
            for (int i = 0; i < logCount; i++)
            {
                var a = actions[i];
                string targetInfo = a.TargetUnit != null ? $"→{a.TargetUnit.kind}" : "";
                Debug.Log($"[AICommander] 候補{i + 1}: {a.ActionType}({a.Unit?.kind}){targetInfo}  " +
                          $"score={a.Score:F1}  AP={a.APCost}");
            }

            // 最良の行動を選択
            AIAction bestAction = SelectBestAction(actions);
            if (bestAction == null || bestAction.ActionType == AIActionType.Wait)
            {
                Debug.Log("[AICommander] 有効な行動なし or 待機選択 → ターン終了");
                break;
            }

            string bestTargetInfo = bestAction.TargetUnit != null ? $"→{bestAction.TargetUnit.kind}" : "";
            Debug.Log($"[AICommander] ★選択: {bestAction.ActionType}({bestAction.Unit?.kind}){bestTargetInfo}  " +
                      $"score={bestAction.Score:F1}  AP消費={bestAction.APCost}  残AP={_board.EnemyAP}");

            // 実行
            bool success = ExecuteAction(bestAction);
            if (!success)
            {
                Debug.Log("[AICommander] 行動実行失敗 → ターン終了");
                break;
            }

            // 統計
            if (bestAction.ActionType == AIActionType.Move) turnMoves++;
            if (bestAction.ActionType == AIActionType.Attack) turnAttacks++;

            // 行動済みマーク
            if (bestAction.Unit != null)
                _actedUnits.Add(bestAction.Unit);
        }

        _totalMoves += turnMoves;
        _totalAttacks += turnAttacks;
        Debug.Log($"--- [AICommander] ターン{_turnCount}終了: " +
                  $"移動{turnMoves}回  攻撃{turnAttacks}回  " +
                  $"残AP={_board.EnemyAP}  累計(移動{_totalMoves}/攻撃{_totalAttacks}/撃破{_totalKills}) ---");
    }

    // ================================================================
    //  行動選択
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
            Debug.Log($"[AICommander] 移動失敗: AP不足 ({unit.kind})");
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

        Debug.Log($"[AICommander] 移動実行: {unit.kind} {oldCell}→{_moveGen.Cell(actualDest)}  残AP={_board.EnemyAP}");

        // 学習記録
        if (_learning.IsActive)
        {
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

        if (target == null || !target.gameObject.activeInHierarchy)
        {
            Debug.Log($"[AICommander] 攻撃失敗: 対象が無効 ({unit.kind})");
            return false;
        }

        if (!_apSystem.CanAct(Team.Enemy, APSystem.ActionType.Attack, unit))
        {
            Debug.Log($"[AICommander] 攻撃失敗: AP不足 ({unit.kind})");
            return false;
        }

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

        if (killed)
        {
            _totalKills++;
            Debug.Log($"[AICommander] ★撃破! {unit.kind}→{target.kind}  " +
                      $"DMG={hpBefore - hpAfter}  累計撃破={_totalKills}");
        }
        else
        {
            Debug.Log($"[AICommander] 攻撃実行: {unit.kind}→{target.kind}  " +
                      $"DMG={hpBefore - hpAfter}  残HP={hpAfter}  残AP={_board.EnemyAP}");
        }

        // 学習記録
        if (_learning.IsActive)
        {
            if (killed)
            {
                Vector3 diff = unit.transform.position - target.transform.position;
                bool isFlanking = Mathf.Abs(diff.x) > Mathf.Abs(diff.z);
                if (isFlanking)
                    _learning.RecordFlankSuccess(target.transform.position);
            }
            else
            {
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
    public void OnAllyUnitKilled(Status unit, AIBoardState board)
    {
        if (!_learning.IsActive || board == null) return;

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
