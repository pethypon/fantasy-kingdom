using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AIBoardEvaluator — 盤面価値関数
//  駒価値・前線厚み・クリスタル安全度・経済継続性・視界優位を
//  同一スコアで評価する。3手先探索の末端評価に使用。
//
//  仕様:
//  ・正値=敵(AI)有利  負値=プレイヤー有利
//  ・各要素は正規化されたサブスコアを合算
// =====================================================================
public static class AIBoardEvaluator
{
    // ---- 重み定数 ----
    const float W_PIECE_VALUE     = 1.0f;   // 駒価値差
    const float W_CRYSTAL_SAFETY  = 1.5f;   // クリスタル安全度
    const float W_FRONTLINE       = 0.6f;   // 前線厚み
    const float W_ECONOMY         = 0.8f;   // 経済継続性
    const float W_VISION          = 0.4f;   // 視界優位

    // ---- 駒種別の価値 ----
    static readonly Dictionary<Kind, float> PieceValues = new Dictionary<Kind, float>
    {
        { Kind.Crystal,      200f },
        { Kind.King,         100f },
        { Kind.Boss,          80f },
        { Kind.Guardian,      30f },
        { Kind.Knight,        25f },
        { Kind.Archer,        28f },
        { Kind.Magic,         30f },
        { Kind.Crossbow,      28f },
        { Kind.Magicsniper,   35f },
        { Kind.Bomber,        32f },
        { Kind.Assassin,      26f },
        { Kind.Scout,         18f },
        { Kind.Priest,        35f },
    };

    /// <summary>
    /// 盤面全体の価値を評価する。
    /// 正値=AI(Enemy)有利、負値=Player有利。
    /// </summary>
    public static float Evaluate(AIBoardState board)
    {
        float score = 0f;

        score += EvalPieceValue(board)        * W_PIECE_VALUE;
        score += EvalCrystalSafety(board)     * W_CRYSTAL_SAFETY;
        score += EvalFrontlineThickness(board) * W_FRONTLINE;
        score += EvalEconomy(board)           * W_ECONOMY;
        score += EvalVisionAdvantage(board)   * W_VISION;

        return score;
    }

    /// <summary>脅威度による重み調整済み評価</summary>
    public static float EvaluateWithThreat(AIBoardState board, int threatLevel)
    {
        // 低脅威度では盤面評価の精度を落とす（わざとノイズを入れる）
        float base_ = Evaluate(board);
        if (threatLevel < 20)
        {
            // チュートリアル帯: 評価をぼかす
            float noise = (threatLevel / 20f);
            return base_ * noise;
        }
        return base_;
    }

    // ================================================================
    //  サブスコア: 駒価値差
    // ================================================================
    static float EvalPieceValue(AIBoardState board)
    {
        float enemyValue = 0f;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            enemyValue += GetPieceValue(u);
        }

        float playerValue = 0f;
        foreach (var u in board.AlivePlayerUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            playerValue += GetPieceValue(u);
        }

        return enemyValue - playerValue;
    }

    static float GetPieceValue(Status unit)
    {
        float base_ = PieceValues.TryGetValue(unit.kind, out float v) ? v : 20f;
        // HPが減っている駒は価値が下がる
        float hpRatio = unit.MaxHP > 0 ? (float)unit.HP / unit.MaxHP : 1f;
        return base_ * (0.3f + 0.7f * hpRatio);
    }

    /// <summary>駒の基本価値を取得（HP減衰込み）。AISearchEngineから参照可能。</summary>
    public static float GetPieceValuePublic(Status unit)
    {
        if (unit == null) return 0f;
        return GetPieceValue(unit);
    }

    // ================================================================
    //  サブスコア: クリスタル安全度
    // ================================================================
    static float EvalCrystalSafety(AIBoardState board)
    {
        float score = 0f;

        // 自陣クリスタルHP
        float eCrystalRatio = board.EnemyCrystalMaxHP > 0
            ? (float)board.EnemyCrystalHP / board.EnemyCrystalMaxHP : 0f;
        score += eCrystalRatio * 50f;

        // 相手クリスタルHP（視認済みの場合のみ）
        if (board.PlayerCrystalVisible && board.PlayerCrystalHP >= 0)
        {
            float pCrystalRatio = board.PlayerCrystalHP / 100f; // 概算
            score -= pCrystalRatio * 40f;
        }

        // 自陣クリスタル周囲の味方駒数（守りの厚さ）
        int guardsNear = 0;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(u.transform.position, board.EnemyCrystalPos) < 5f)
                guardsNear++;
        }
        score += Mathf.Min(guardsNear * 5f, 25f);

        // 自陣クリスタル周囲の敵駒数（脅威）
        int threatNear = 0;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(pu.transform.position, board.EnemyCrystalPos) < 5f)
                threatNear++;
        }
        score -= threatNear * 15f;

        return score;
    }

    // ================================================================
    //  サブスコア: 前線厚み
    //  前線位置に味方が密集しているほど有利
    // ================================================================
    static float EvalFrontlineThickness(AIBoardState board)
    {
        if (board.AliveEnemyUnits.Count == 0) return 0f;

        // 前線位置 = 味方の平均位置
        Vector3 centroid = Vector3.zero;
        int count = 0;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            centroid += u.transform.position;
            count++;
        }
        if (count == 0) return 0f;
        centroid /= count;

        // 前線の密度（味方同士が近いほど高い）
        float density = 0f;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            float distToCentroid = Vector3.Distance(u.transform.position, centroid);
            density += Mathf.Max(0f, 5f - distToCentroid); // 近いほど加点
        }

        // 前線が敵陣に近いほど加点（ただし視界制限を尊重）
        float advancement = 0f;
        if (board.CanUsePlayerCrystalAsTarget())
        {
            float centroidToEnemy = Vector3.Distance(centroid, board.PlayerCrystalPos);
            advancement = Mathf.Max(0f, 20f - centroidToEnemy);
        }

        return density * 0.5f + advancement * 0.3f;
    }

    // ================================================================
    //  サブスコア: 経済継続性
    // ================================================================
    static float EvalEconomy(AIBoardState board)
    {
        if (board.EnemyResources == null) return 0f;
        var res = board.EnemyResources;

        float score = 0f;

        // パン（維持の基盤）
        score += Mathf.Clamp(res.Bread, 0, 50) * 0.3f;

        // 市民（AP源）
        score += Mathf.Clamp(res.Citizen, 0, 10) * 2f;

        // 加工資源
        score += Mathf.Clamp(res.Plank, 0, 30) * 0.15f;
        score += Mathf.Clamp(res.CutStone, 0, 30) * 0.15f;
        score += Mathf.Clamp(res.Iron, 0, 20) * 0.2f;

        // 基礎資源（木/石/水/小麦）
        score += Mathf.Clamp(res.Wood, 0, 50) * 0.05f;
        score += Mathf.Clamp(res.Stone, 0, 50) * 0.05f;
        score += Mathf.Clamp(res.Water, 0, 50) * 0.05f;
        score += Mathf.Clamp(res.Wheat, 0, 50) * 0.05f;

        // 建物数（経済基盤の充実度）
        int buildingCount = 0;
        foreach (var kvp in board.EnemyBuildingCounts)
            buildingCount += kvp.Value;
        score += Mathf.Min(buildingCount * 2f, 20f);

        return score;
    }

    // ================================================================
    //  サブスコア: 視界優位
    // ================================================================
    static float EvalVisionAdvantage(AIBoardState board)
    {
        float explorationRatio = board.GetExplorationRatio();
        float score = explorationRatio * 30f;

        // 視界内の敵数が多いほど情報優位
        score += board.AlivePlayerUnits.Count * 3f;

        return score;
    }
}
