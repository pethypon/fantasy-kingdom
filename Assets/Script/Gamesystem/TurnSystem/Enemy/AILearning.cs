using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AILearning — 成長型BOSS専用の試合中学習システム
//  記録: 成功ルート、失敗ルート、被撃破位置、プレイヤー配置傾向
//  出力: 行動評価への学習補正
// =====================================================================
public class AILearning
{
    // 学習補正の上限
    const float MaxBonus = 30f;
    const int MinOccurrences = 2; // 偶然による過剰反応防止

    // ---- 記録データ ----
    // 正面突破が失敗した位置（回数カウント）
    Dictionary<Vector3Int, int> _failedFrontalAttacks = new Dictionary<Vector3Int, int>();

    // 奇襲成功位置（回数カウント）
    Dictionary<Vector3Int, int> _successFlanks = new Dictionary<Vector3Int, int>();

    // 孤立して落とされた自軍駒位置
    Dictionary<Vector3Int, int> _isolatedDeaths = new Dictionary<Vector3Int, int>();

    // プレイヤーの頻出防衛位置
    Dictionary<Vector3Int, int> _playerDefensePositions = new Dictionary<Vector3Int, int>();

    // ルート成功/失敗（セル→セル方向）
    Dictionary<Vector3Int, int> _routeSuccess = new Dictionary<Vector3Int, int>();
    Dictionary<Vector3Int, int> _routeFailure = new Dictionary<Vector3Int, int>();

    // 性格出力補正（学習による動的調整）
    float _cautionModifier = 0f;
    float _commandModifier = 0f;
    float _tacticsModifier = 0f;
    float _defenseModifier = 0f;
    float _developModifier = 0f; // 発展性補正（経済差学習で上昇）

    public bool IsActive { get; private set; }

    public AILearning(bool active)
    {
        IsActive = active;
    }

    // ================================================================
    //  記録メソッド（各イベント発生時にAICommanderから呼ぶ）
    // ================================================================

    // 正面攻撃で失敗（ダメージ負けまたは撃破失敗）
    public void RecordFrontalFailure(Vector3 pos)
    {
        if (!IsActive) return;
        var cell = ToCell(pos);
        Increment(_failedFrontalAttacks, cell);

        if (_failedFrontalAttacks[cell] >= MinOccurrences)
        {
            _tacticsModifier = Mathf.Min(MaxBonus, _tacticsModifier + 2f);
            _cautionModifier = Mathf.Min(MaxBonus, _cautionModifier + 1f);
            Debug.Log($"[AILearning] 正面突破失敗学習 @{cell} → 戦術性+2, 慎重性+1");
        }
    }

    // 側面・背後からの攻撃で撃破成功
    public void RecordFlankSuccess(Vector3 pos)
    {
        if (!IsActive) return;
        var cell = ToCell(pos);
        Increment(_successFlanks, cell);

        if (_successFlanks[cell] >= MinOccurrences)
        {
            _tacticsModifier = Mathf.Min(MaxBonus, _tacticsModifier + 2f);
            Debug.Log($"[AILearning] 奇襲成功学習 @{cell} → 戦術性+2");
        }
    }

    // 自軍の孤立駒が撃破された
    public void RecordIsolatedDeath(Vector3 pos)
    {
        if (!IsActive) return;
        var cell = ToCell(pos);
        Increment(_isolatedDeaths, cell);

        if (_isolatedDeaths[cell] >= MinOccurrences)
        {
            _commandModifier = Mathf.Min(MaxBonus, _commandModifier + 3f);
            Debug.Log($"[AILearning] 孤立被撃破学習 @{cell} → 指揮性+3");
        }
    }

    // プレイヤーの防衛配置を記録
    public void RecordPlayerDefensePosition(Vector3 pos)
    {
        if (!IsActive) return;
        var cell = ToCell(pos);
        Increment(_playerDefensePositions, cell);
    }

    // ルート成功/失敗
    public void RecordRouteResult(Vector3 pos, bool success)
    {
        if (!IsActive) return;
        var cell = ToCell(pos);
        if (success)
            Increment(_routeSuccess, cell);
        else
        {
            Increment(_routeFailure, cell);
            if (_routeFailure[cell] >= MinOccurrences)
            {
                _cautionModifier = Mathf.Min(MaxBonus, _cautionModifier + 1.5f);
            }
        }
    }

    // 攻め急いで崩れた
    public void RecordOverextension()
    {
        if (!IsActive) return;
        _defenseModifier = Mathf.Min(MaxBonus, _defenseModifier + 2f);
        _cautionModifier = Mathf.Min(MaxBonus, _cautionModifier + 1f);
        Debug.Log("[AILearning] 攻め急ぎ崩壊学習 → 防衛性+2, 慎重性+1");
    }

    // 経済差で勝てていると感じた
    public void RecordEconomicAdvantage()
    {
        if (!IsActive) return;
        _developModifier = Mathf.Min(MaxBonus, _developModifier + 2f);
        Debug.Log("[AILearning] 経済優位学習 → 発展性+2");
    }

    // ================================================================
    //  学習補正の出力（AIActionEvaluatorから呼ばれる）
    // ================================================================
    public float GetBonus(AIAction action, AIBoardState board)
    {
        if (!IsActive) return 0f;

        float bonus = 0f;
        var targetCell = ToCell(action.TargetPos);

        switch (action.ActionType)
        {
            case AIActionType.Attack:
                // 正面突破が失敗した位置では減点
                if (_failedFrontalAttacks.ContainsKey(targetCell))
                    bonus -= Mathf.Min(_failedFrontalAttacks[targetCell] * 3f, MaxBonus);
                // 奇襲成功位置では加点
                if (_successFlanks.ContainsKey(targetCell))
                    bonus += Mathf.Min(_successFlanks[targetCell] * 3f, MaxBonus);
                // プレイヤーが頻繁に守る位置からは避ける
                if (_playerDefensePositions.ContainsKey(targetCell) &&
                    _playerDefensePositions[targetCell] >= MinOccurrences)
                    bonus -= _playerDefensePositions[targetCell] * 2f;
                break;

            case AIActionType.Move:
                // 孤立被撃破位置には行きにくくなる
                if (_isolatedDeaths.ContainsKey(targetCell))
                    bonus -= Mathf.Min(_isolatedDeaths[targetCell] * 4f, MaxBonus);
                // 失敗ルートは避ける
                if (_routeFailure.ContainsKey(targetCell))
                    bonus -= Mathf.Min(_routeFailure[targetCell] * 2f, MaxBonus);
                // 成功ルートは好む
                if (_routeSuccess.ContainsKey(targetCell))
                    bonus += Mathf.Min(_routeSuccess[targetCell] * 2f, MaxBonus);
                // 指揮性補正: 味方から離れすぎる移動を減点
                bonus -= _commandModifier * 0.3f *
                    Mathf.Max(0, GetNearestAllyDist(action.TargetPos, action.Unit, board) - 3f);
                break;
        }

        // 全体的な慎重性補正
        if (action.ActionType == AIActionType.Attack ||
            action.ActionType == AIActionType.Move)
        {
            bonus += _cautionModifier * 0.1f; // 慎重になるほど待機寄りに
        }

        // 防衛性補正: 自陣寄り移動を加点
        if (action.ActionType == AIActionType.Move && _defenseModifier > 0f)
        {
            float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
            if (crystalDist < 5f)
                bonus += _defenseModifier * 0.3f;
        }

        // 発展性補正: 経済系行動を加点
        if ((action.ActionType == AIActionType.Build ||
             action.ActionType == AIActionType.SubCrystal) && _developModifier > 0f)
        {
            bonus += _developModifier * 0.5f;
        }

        return Mathf.Clamp(bonus, -MaxBonus, MaxBonus);
    }

    // ================================================================
    //  ヘルパー
    // ================================================================
    static Vector3Int ToCell(Vector3 v)
        => new Vector3Int(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.z));

    static void Increment(Dictionary<Vector3Int, int> dict, Vector3Int key)
    {
        if (dict.ContainsKey(key))
            dict[key]++;
        else
            dict[key] = 1;
    }

    static float GetNearestAllyDist(Vector3 pos, Status self, AIBoardState board)
    {
        float nearest = float.MaxValue;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy || u == self) continue;
            float d = Vector3.Distance(pos, u.transform.position);
            if (d < nearest) nearest = d;
        }
        return nearest;
    }
}
