using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimBoardEvaluator — SimBoardState専用の盤面評価関数
//  GameObjectに依存せず、純粋にSimBoardStateのデータのみで評価する
//
//  正値=Enemy(AI)有利  負値=Player有利
//
//  評価要素:
//  1. 駒価値差 (Material)
//  2. クリスタル安全度 (Crystal Safety)
//  3. 前線厚み (Frontline Thickness)
//  4. 位置的優位 (Positional Advantage)
//  5. 経済継続性 (Economy)
//  6. 視野支配力 (Vision Dominance)
//  7. 脅威と反撃 (Threat/Counter)
// =====================================================================
public static class SimBoardEvaluator
{
    // ---- 重み定数 ----
    const float W_MATERIAL         = 1.0f;
    const float W_CRYSTAL_SAFETY   = 1.5f;
    const float W_FRONTLINE        = 0.6f;
    const float W_POSITIONAL       = 0.7f;
    const float W_ECONOMY          = 0.8f;
    const float W_VISION           = 0.4f;
    const float W_THREAT           = 0.9f;

    // ================================================================
    //  メイン評価関数
    // ================================================================
    public static float Evaluate(SimBoardState board)
    {
        float score = 0f;

        score += EvalMaterial(board)        * W_MATERIAL;
        score += EvalCrystalSafety(board)   * W_CRYSTAL_SAFETY;
        score += EvalFrontline(board)       * W_FRONTLINE;
        score += EvalPositional(board)      * W_POSITIONAL;
        score += EvalEconomy(board)         * W_ECONOMY;
        score += EvalVision(board)          * W_VISION;
        score += EvalThreat(board)          * W_THREAT;

        // ゲーム終了状態のボーナス/ペナルティ
        score += EvalTerminal(board);

        return score;
    }

    // ================================================================
    //  1. 駒価値差 (Material)
    //  HP減衰付きの駒価値の差を計算
    // ================================================================
    static float EvalMaterial(SimBoardState board)
    {
        float enemyValue = 0f, playerValue = 0f;

        for (int i = 0; i < board.Units.Count; i++)
        {
            var u = board.Units[i];
            if (!u.IsAlive) continue;
            float val = GetPieceValue(u);

            if (u.Team == Team.Enemy)
                enemyValue += val;
            else if (u.Team == Team.Player)
                playerValue += val;
        }

        return enemyValue - playerValue;
    }

    static float GetPieceValue(SimUnit unit)
    {
        float baseVal = SimActionGenerator.GetPieceValue(unit);
        // HP減衰: HPが低いほど価値が下がる
        float hpRatio = unit.MaxHP > 0 ? (float)unit.HP / unit.MaxHP : 1f;
        return baseVal * (0.3f + 0.7f * hpRatio);
    }

    // ================================================================
    //  2. クリスタル安全度
    //  自陣クリスタルのHP、周囲の味方/敵の数
    // ================================================================
    static float EvalCrystalSafety(SimBoardState board)
    {
        float score = 0f;

        // 自陣クリスタルHP
        var eCrystal = board.GetCrystal(Team.Enemy);
        if (eCrystal != null)
        {
            float hpRatio = eCrystal.MaxHP > 0 ? (float)eCrystal.HP / eCrystal.MaxHP : 0f;
            score += hpRatio * 50f;

            // クリスタル周囲の味方駒数
            int guards = 0, threats = 0;
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Type != Type.Unit) continue;
                float dist = SimUtil.Distance(u.Position, board.EnemyCrystalPos);
                if (dist < 5f)
                {
                    if (u.Team == Team.Enemy) guards++;
                    else if (u.Team == Team.Player) threats++;
                }
            }
            score += Mathf.Min(guards * 5f, 25f);
            score -= threats * 15f;

            // クリスタル破壊は壊滅的
            if (!eCrystal.IsAlive)
                score -= 500f;
        }
        else
        {
            score -= 500f; // クリスタル不在
        }

        // 敵クリスタルHP (視認していた場合)
        var pCrystal = board.GetCrystal(Team.Player);
        if (pCrystal != null)
        {
            float hpRatio = pCrystal.MaxHP > 0 ? (float)pCrystal.HP / pCrystal.MaxHP : 1f;
            score -= hpRatio * 40f;

            if (!pCrystal.IsAlive)
                score += 500f; // 敵クリスタル破壊 = 勝利
        }

        return score;
    }

    // ================================================================
    //  3. 前線厚み
    //  味方が集団で行動しているか、前線位置がどこか
    // ================================================================
    static float EvalFrontline(SimBoardState board)
    {
        var enemyUnits = board.GetAliveUnits(Team.Enemy);
        if (enemyUnits.Count == 0) return -20f;

        // 重心計算
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < enemyUnits.Count; i++)
        {
            centroid += new Vector3(enemyUnits[i].Position.x, 0, enemyUnits[i].Position.z);
        }
        centroid /= enemyUnits.Count;

        // 密度
        float density = 0f;
        for (int i = 0; i < enemyUnits.Count; i++)
        {
            float d = Vector3.Distance(
                new Vector3(enemyUnits[i].Position.x, 0, enemyUnits[i].Position.z), centroid);
            density += Mathf.Max(0f, 5f - d);
        }

        // 進軍度
        float advancement = 0f;
        float centroidToTarget = Vector3.Distance(centroid,
            new Vector3(board.PlayerCrystalPos.x, 0, board.PlayerCrystalPos.z));
        advancement = Mathf.Max(0f, 20f - centroidToTarget);

        return density * 0.5f + advancement * 0.3f;
    }

    // ================================================================
    //  4. 位置的優位
    //  各駒の位置的な価値 (攻撃範囲、脅威を与える能力)
    // ================================================================
    static float EvalPositional(SimBoardState board)
    {
        float score = 0f;
        var enemyUnits = board.GetAliveUnits(Team.Enemy);
        var playerUnits = board.GetAliveUnits(Team.Player);

        // 敵ユニットへの近接度
        for (int i = 0; i < enemyUnits.Count; i++)
        {
            var eu = enemyUnits[i];

            // 敵との距離による攻撃機会
            for (int j = 0; j < playerUnits.Count; j++)
            {
                float dist = SimUtil.Distance(eu.Position, playerUnits[j].Position);
                if (dist <= 2f)
                    score += 5f; // 攻撃圏内
                else if (dist <= 4f)
                    score += 2f; // 1手で攻撃圏内に入れる距離
            }

            // プレイヤークリスタルへの近接度
            float crystalDist = SimUtil.Distance(eu.Position, board.PlayerCrystalPos);
            if (crystalDist <= 3f)
                score += 10f; // クリスタル攻撃可能圏
            else if (crystalDist <= 6f)
                score += 4f;

            // 味方との連携 (孤立ペナルティ)
            float nearestAlly = float.MaxValue;
            for (int j = 0; j < enemyUnits.Count; j++)
            {
                if (i == j) continue;
                float d = SimUtil.Distance(eu.Position, enemyUnits[j].Position);
                if (d < nearestAlly) nearestAlly = d;
            }
            if (nearestAlly > 6f)
                score -= 5f; // 孤立
            else if (nearestAlly >= 2f && nearestAlly <= 4f)
                score += 2f; // 良い連携距離
        }

        // プレイヤーの位置的優位もマイナスとして加算
        for (int i = 0; i < playerUnits.Count; i++)
        {
            var pu = playerUnits[i];
            float crystalDist = SimUtil.Distance(pu.Position, board.EnemyCrystalPos);
            if (crystalDist <= 3f)
                score -= 12f;
            else if (crystalDist <= 6f)
                score -= 5f;
        }

        return score;
    }

    // ================================================================
    //  5. 経済継続性
    //  建物数による経済基盤の評価
    // ================================================================
    static float EvalEconomy(SimBoardState board)
    {
        float score = 0f;

        int totalBuildings = 0;
        foreach (var kvp in board.EnemyBuildingCounts)
            totalBuildings += kvp.Value;
        score += Mathf.Min(totalBuildings * 3f, 30f);

        // 基礎経済施設の種類数
        int coreTypes = 0;
        FacilityKind[] coreKinds = {
            FacilityKind.Well, FacilityKind.LoggingCamp,
            FacilityKind.Quarry, FacilityKind.Field, FacilityKind.House
        };
        foreach (var fk in coreKinds)
        {
            int count = 0;
            board.EnemyBuildingCounts.TryGetValue(fk, out count);
            if (count > 0) coreTypes++;
        }
        score += coreTypes * 5f;

        // 加工施設
        FacilityKind[] procKinds = {
            FacilityKind.LumberMill, FacilityKind.StoneWorks,
            FacilityKind.Bakery, FacilityKind.Smelter
        };
        foreach (var fk in procKinds)
        {
            int count = 0;
            board.EnemyBuildingCounts.TryGetValue(fk, out count);
            if (count > 0) score += 4f;
        }

        return score;
    }

    // ================================================================
    //  6. 視野支配力
    //  ユニット数と散開度による視野カバー推定
    // ================================================================
    static float EvalVision(SimBoardState board)
    {
        var enemyUnits = board.GetAliveUnits(Team.Enemy);
        float score = 0f;

        // ユニット数が多いほど視野が広い
        score += Mathf.Min(enemyUnits.Count * 3f, 20f);

        // Scoutは視野貢献が大きい
        for (int i = 0; i < enemyUnits.Count; i++)
        {
            if (enemyUnits[i].Kind == Kind.Scout)
                score += 5f;
        }

        // 散開度 (一箇所に固まりすぎていないか)
        if (enemyUnits.Count >= 3)
        {
            float maxDist = 0f;
            for (int i = 0; i < enemyUnits.Count; i++)
            {
                for (int j = i + 1; j < enemyUnits.Count; j++)
                {
                    float d = SimUtil.Distance(enemyUnits[i].Position, enemyUnits[j].Position);
                    if (d > maxDist) maxDist = d;
                }
            }
            if (maxDist > 8f)
                score += 5f; // 適度な散開
        }

        return score;
    }

    // ================================================================
    //  7. 脅威と反撃
    //  次ターンに受けるダメージの推定
    // ================================================================
    static float EvalThreat(SimBoardState board)
    {
        float score = 0f;
        var enemyUnits = board.GetAliveUnits(Team.Enemy);
        var playerUnits = board.GetAliveUnits(Team.Player);

        // 敵から自軍への脅威
        for (int i = 0; i < playerUnits.Count; i++)
        {
            var pu = playerUnits[i];
            for (int j = 0; j < enemyUnits.Count; j++)
            {
                var eu = enemyUnits[j];
                float dist = SimUtil.Distance(pu.Position, eu.Position);

                // 攻撃射程+移動1手の範囲に敵がいる
                float threatRange = EstimateAttackRange(pu.Kind) + EstimateMoveRange(pu.Kind);
                if (dist <= threatRange)
                {
                    int potentialDmg = SimBoardState.CalcDamage(pu, eu);
                    score -= potentialDmg * 0.3f;

                    // 確殺可能な場合はさらに危険
                    if (potentialDmg >= eu.HP)
                        score -= GetPieceValue(eu) * 0.5f;
                }
            }
        }

        // 自軍から敵への脅威 (機会)
        for (int i = 0; i < enemyUnits.Count; i++)
        {
            var eu = enemyUnits[i];
            for (int j = 0; j < playerUnits.Count; j++)
            {
                var pu = playerUnits[j];
                float dist = SimUtil.Distance(eu.Position, pu.Position);

                float threatRange = EstimateAttackRange(eu.Kind) + EstimateMoveRange(eu.Kind);
                if (dist <= threatRange)
                {
                    int potentialDmg = SimBoardState.CalcDamage(eu, pu);
                    score += potentialDmg * 0.2f;

                    if (potentialDmg >= pu.HP)
                        score += GetPieceValue(pu) * 0.3f;
                }
            }
        }

        return score;
    }

    // ================================================================
    //  ゲーム終了状態
    // ================================================================
    static float EvalTerminal(SimBoardState board)
    {
        // 敵クリスタル破壊 = AI勝利
        var pCrystal = board.GetCrystal(Team.Player);
        if (pCrystal != null && !pCrystal.IsAlive)
            return 10000f;

        // 自陣クリスタル破壊 = AI敗北
        var eCrystal = board.GetCrystal(Team.Enemy);
        if (eCrystal != null && !eCrystal.IsAlive)
            return -10000f;

        return 0f;
    }

    // ================================================================
    //  ヘルパー: 攻撃射程推定
    // ================================================================
    static float EstimateAttackRange(Kind kind)
    {
        switch (kind)
        {
            case Kind.Magicsniper: return 4f;
            case Kind.Archer:     return 3f;
            case Kind.Bomber:     return 3f;
            case Kind.Magic:      return 2f;
            case Kind.Crossbow:   return 2f;
            default:              return 1.5f;
        }
    }

    // ================================================================
    //  ヘルパー: 移動射程推定
    // ================================================================
    static float EstimateMoveRange(Kind kind)
    {
        switch (kind)
        {
            case Kind.Assassin:    return 2.5f;
            case Kind.Scout:       return 2f;
            case Kind.Crossbow:    return 3f;
            case Kind.Magicsniper: return 3f;
            case Kind.Bomber:      return 3f;
            default:               return 1.5f;
        }
    }
}
