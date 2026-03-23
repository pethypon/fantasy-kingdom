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
//  2. クリスタル安全度 (Crystal Safety) — シールド考慮
//  3. King安全度 (King Safety) — King死亡=即敗北
//  4. 前線厚み (Frontline Thickness)
//  5. 位置的優位 (Positional Advantage)
//  6. 機動力差 (Mobility) — 実際の移動可能数
//  7. 経済継続性 (Economy)
//  8. 脅威と反撃 (Threat/Counter)
//  9. テンポ (Tempo) — AP残量と行動の主導権
// =====================================================================
public static class SimBoardEvaluator
{
    // ---- 重み定数 ----
    const float W_MATERIAL         = 1.0f;
    const float W_CRYSTAL_SAFETY   = 1.8f;
    const float W_KING_SAFETY      = 1.2f;
    const float W_FRONTLINE        = 0.5f;
    const float W_POSITIONAL       = 0.7f;
    const float W_MOBILITY         = 0.4f;
    const float W_ECONOMY          = 0.7f;
    const float W_THREAT           = 0.9f;
    const float W_TEMPO            = 0.3f;

    // ================================================================
    //  メイン評価関数
    // ================================================================
    public static float Evaluate(SimBoardState board)
    {
        // ゲーム終了状態 — 即座に極値を返す
        float terminal = EvalTerminal(board);
        if (terminal != 0f) return terminal;

        float score = 0f;
        score += EvalMaterial(board)        * W_MATERIAL;
        score += EvalCrystalSafety(board)   * W_CRYSTAL_SAFETY;
        score += EvalKingSafety(board)      * W_KING_SAFETY;
        score += EvalFrontline(board)       * W_FRONTLINE;
        score += EvalPositional(board)      * W_POSITIONAL;
        score += EvalMobility(board)        * W_MOBILITY;
        score += EvalEconomy(board)         * W_ECONOMY;
        score += EvalThreat(board)          * W_THREAT;
        score += EvalTempo(board)           * W_TEMPO;

        return score;
    }

    // ================================================================
    //  1. 駒価値差 (Material)
    //  HP減衰 + ステータス効果ペナルティ付き
    // ================================================================
    static float EvalMaterial(SimBoardState board)
    {
        float enemyValue = 0f, playerValue = 0f;

        for (int i = 0; i < board.Units.Count; i++)
        {
            var u = board.Units[i];
            if (!u.IsAlive) continue;
            if (u.Kind == Kind.Crystal) continue; // クリスタルは別途評価
            float val = GetAdjustedValue(u);

            if (u.Team == Team.Enemy)
                enemyValue += val;
            else if (u.Team == Team.Player)
                playerValue += val;
        }

        return enemyValue - playerValue;
    }

    static float GetAdjustedValue(SimUnit unit)
    {
        float baseVal = SimActionGenerator.GetPieceValue(unit);

        // HP減衰
        float hpRatio = unit.MaxHP > 0 ? (float)unit.HP / unit.MaxHP : 1f;
        float val = baseVal * (0.3f + 0.7f * hpRatio);

        // ステータス効果ペナルティ
        if (unit.IsStunned) val *= 0.5f;
        if (unit.IsMovementBlocked) val *= 0.7f;
        if (unit.HasDebuff(StatusEffectType.Poison)) val *= 0.85f;
        if (unit.HasDebuff(StatusEffectType.Bleed)) val *= 0.9f;

        // バフボーナス
        if (unit.HasBuff(BuffType.Offensive)) val *= 1.1f;
        if (unit.HasBuff(BuffType.Defensive)) val *= 1.08f;
        if (unit.HasBuff(BuffType.Barrier)) val *= 1.12f;

        return val;
    }

    // ================================================================
    //  2. クリスタル安全度
    //  HP比率 + シールド考慮 + 周囲の味方/敵バランス
    // ================================================================
    static float EvalCrystalSafety(SimBoardState board)
    {
        float score = 0f;

        // --- 自陣クリスタル ---
        var eCrystal = board.GetCrystal(Team.Enemy);
        if (eCrystal != null && eCrystal.IsAlive)
        {
            float hpRatio = eCrystal.MaxHP > 0 ? (float)eCrystal.HP / eCrystal.MaxHP : 0f;
            score += hpRatio * 50f;

            // シールド中は安全
            if (eCrystal.ShieldTurns > 0)
                score += eCrystal.ShieldTurns * 8f;

            // 周囲の駒数
            int guards = 0, threats = 0;
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Type != Type.Unit) continue;
                int dist = SimUtil.Manhattan(u.Position, board.EnemyCrystalPos);
                if (dist <= 4)
                {
                    if (u.Team == Team.Enemy) guards++;
                    else if (u.Team == Team.Player) threats++;
                }
            }
            score += Mathf.Min(guards * 6f, 30f);
            score -= threats * 18f;

            // 脅威がある場合のHP低下ペナルティ増幅
            if (threats > 0 && hpRatio < 0.5f)
                score -= (1f - hpRatio) * threats * 10f;
        }
        else
        {
            score -= 500f;
        }

        // --- 敵クリスタル ---
        var pCrystal = board.GetCrystal(Team.Player);
        if (pCrystal != null && pCrystal.IsAlive)
        {
            float hpRatio = pCrystal.MaxHP > 0 ? (float)pCrystal.HP / pCrystal.MaxHP : 1f;
            score -= hpRatio * 40f;

            // 敵クリスタルにシールドがある場合はペナルティ
            if (pCrystal.ShieldTurns > 0)
                score -= pCrystal.ShieldTurns * 5f;

            // 自軍が敵クリスタル付近にいるボーナス
            int attackers = 0;
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Team != Team.Enemy || u.Type != Type.Unit) continue;
                int dist = SimUtil.Manhattan(u.Position, board.PlayerCrystalPos);
                if (dist <= 4) attackers++;
            }
            score += attackers * 8f;
        }
        else
        {
            score += 500f;
        }

        return score;
    }

    // ================================================================
    //  3. King安全度
    //  King死亡=即敗北なので特別に評価
    // ================================================================
    static float EvalKingSafety(SimBoardState board)
    {
        float score = 0f;

        // 自軍King
        var eKing = board.GetKing(Team.Enemy);
        if (eKing != null && eKing.IsAlive)
        {
            float hpRatio = eKing.MaxHP > 0 ? (float)eKing.HP / eKing.MaxHP : 0f;
            score += hpRatio * 30f;

            // Kingが敵に囲まれていると危険
            int nearbyEnemies = 0;
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Team != Team.Player || u.Type != Type.Unit) continue;
                float dist = SimUtil.Distance(u.Position, eKing.Position);
                float threatRange = SimActionGenerator.EstimateAttackRange(u.Kind) + SimActionGenerator.EstimateMoveRange(u.Kind);
                if (dist <= threatRange) nearbyEnemies++;
            }
            if (nearbyEnemies >= 2) score -= nearbyEnemies * 12f;

            // 味方による護衛
            int nearbyAllies = 0;
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Team != Team.Enemy || u.Type != Type.Unit) continue;
                if (u.Kind == Kind.King) continue;
                if (SimUtil.Manhattan(u.Position, eKing.Position) <= 3) nearbyAllies++;
            }
            score += Mathf.Min(nearbyAllies * 4f, 16f);
        }
        else if (eKing != null)
        {
            score -= 300f; // King死亡
        }

        // 敵King
        var pKing = board.GetKing(Team.Player);
        if (pKing != null && pKing.IsAlive)
        {
            float hpRatio = pKing.MaxHP > 0 ? (float)pKing.HP / pKing.MaxHP : 1f;
            score -= hpRatio * 20f;

            // 自軍が敵Kingを脅かしている
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Team != Team.Enemy || u.Type != Type.Unit) continue;
                float dist = SimUtil.Distance(u.Position, pKing.Position);
                if (dist <= SimActionGenerator.EstimateAttackRange(u.Kind) + 1f)
                    score += 8f;
            }
        }
        else if (pKing != null)
        {
            score += 300f;
        }

        return score;
    }

    // ================================================================
    //  4. 前線厚み
    //  味方の集団性と進軍度
    // ================================================================
    static float EvalFrontline(SimBoardState board)
    {
        var enemyUnits = board.GetAliveUnits(Team.Enemy);
        var playerUnits = board.GetAliveUnits(Team.Player);
        if (enemyUnits.Count == 0) return -20f;

        float score = 0f;

        // 敵前線
        Vector3 eCentroid = CalcCentroid(enemyUnits);
        float eDensity = CalcDensity(enemyUnits, eCentroid);
        float eAdvancement = Mathf.Max(0f, 20f - Vector3.Distance(eCentroid,
            new Vector3(board.PlayerCrystalPos.x, 0, board.PlayerCrystalPos.z)));
        score += eDensity * 0.4f + eAdvancement * 0.3f;

        // プレイヤー前線（マイナス要素）
        if (playerUnits.Count > 0)
        {
            Vector3 pCentroid = CalcCentroid(playerUnits);
            float pAdvancement = Mathf.Max(0f, 20f - Vector3.Distance(pCentroid,
                new Vector3(board.EnemyCrystalPos.x, 0, board.EnemyCrystalPos.z)));
            score -= pAdvancement * 0.25f;
        }

        return score;
    }

    static Vector3 CalcCentroid(List<SimUnit> units)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < units.Count; i++)
            sum += new Vector3(units[i].Position.x, 0, units[i].Position.z);
        return sum / Mathf.Max(1, units.Count);
    }

    static float CalcDensity(List<SimUnit> units, Vector3 centroid)
    {
        float density = 0f;
        for (int i = 0; i < units.Count; i++)
        {
            float d = Vector3.Distance(
                new Vector3(units[i].Position.x, 0, units[i].Position.z), centroid);
            density += Mathf.Max(0f, 5f - d);
        }
        return density;
    }

    // ================================================================
    //  5. 位置的優位
    //  各駒の位置的な価値（攻撃機会、クリスタル接近、連携）
    // ================================================================
    static float EvalPositional(SimBoardState board)
    {
        float score = 0f;
        var enemyUnits = board.GetAliveUnits(Team.Enemy);
        var playerUnits = board.GetAliveUnits(Team.Player);

        // 敵ユニットの位置評価
        for (int i = 0; i < enemyUnits.Count; i++)
        {
            var eu = enemyUnits[i];

            // 攻撃可能なターゲット数
            int targets = SimActionGenerator.CountAttackTargets(board, eu, Team.Player);
            score += targets * 6f;

            // プレイヤークリスタルへの接近
            int crystalDist = SimUtil.Manhattan(eu.Position, board.PlayerCrystalPos);
            if (crystalDist <= 3) score += 12f;
            else if (crystalDist <= 6) score += 5f;
            else if (crystalDist <= 10) score += 2f;

            // 味方との連携（孤立ペナルティ）
            int nearestAlly = int.MaxValue;
            for (int j = 0; j < enemyUnits.Count; j++)
            {
                if (i == j) continue;
                int d = SimUtil.Manhattan(eu.Position, enemyUnits[j].Position);
                if (d < nearestAlly) nearestAlly = d;
            }
            if (enemyUnits.Count > 1)
            {
                if (nearestAlly > 6) score -= 6f;
                else if (nearestAlly >= 2 && nearestAlly <= 4) score += 3f;
            }
        }

        // プレイヤーの位置的優位（マイナス）
        for (int i = 0; i < playerUnits.Count; i++)
        {
            var pu = playerUnits[i];
            int targets = SimActionGenerator.CountAttackTargets(board, pu, Team.Enemy);
            score -= targets * 5f;

            int crystalDist = SimUtil.Manhattan(pu.Position, board.EnemyCrystalPos);
            if (crystalDist <= 3) score -= 14f;
            else if (crystalDist <= 6) score -= 6f;
        }

        return score;
    }

    // ================================================================
    //  6. 機動力差 (Mobility)
    //  各駒の移動可能マス数の合計差
    // ================================================================
    static float EvalMobility(SimBoardState board)
    {
        var enemyUnits = board.GetAliveUnits(Team.Enemy);
        var playerUnits = board.GetAliveUnits(Team.Player);

        int enemyMobility = 0;
        for (int i = 0; i < enemyUnits.Count; i++)
            enemyMobility += SimActionGenerator.CountMoves(board, enemyUnits[i]);

        int playerMobility = 0;
        for (int i = 0; i < playerUnits.Count; i++)
            playerMobility += SimActionGenerator.CountMoves(board, playerUnits[i]);

        return (enemyMobility - playerMobility) * 0.3f;
    }

    // ================================================================
    //  7. 経済継続性
    //  建物数・種類による経済基盤 + AP生成能力の推定
    // ================================================================
    static float EvalEconomy(SimBoardState board)
    {
        float score = 0f;

        // 建物価値
        score += EvalBuildingValue(board.EnemyBuildingCounts);
        score -= EvalBuildingValue(board.PlayerBuildingCounts) * 0.7f;

        // AP生産力（市民から）
        int enemyHouses = 0;
        board.EnemyBuildingCounts.TryGetValue(FacilityKind.House, out enemyHouses);
        score += enemyHouses * 5f; // House → Citizen → AP

        return score;
    }

    static float EvalBuildingValue(Dictionary<FacilityKind, int> counts)
    {
        float val = 0f;
        int total = 0;
        foreach (var kvp in counts)
            total += kvp.Value;

        val += Mathf.Min(total * 3f, 30f);

        // 基礎経済施設
        FacilityKind[] coreKinds = {
            FacilityKind.Well, FacilityKind.LoggingCamp,
            FacilityKind.Quarry, FacilityKind.Field, FacilityKind.House
        };
        int coreTypes = 0;
        foreach (var fk in coreKinds)
        {
            int c = 0;
            counts.TryGetValue(fk, out c);
            if (c > 0) coreTypes++;
        }
        val += coreTypes * 5f;

        // 加工施設
        FacilityKind[] procKinds = {
            FacilityKind.LumberMill, FacilityKind.StoneWorks,
            FacilityKind.Bakery, FacilityKind.Smelter
        };
        foreach (var fk in procKinds)
        {
            int c = 0;
            counts.TryGetValue(fk, out c);
            if (c > 0) val += 4f;
        }

        // 兵舎
        int barracks = 0;
        counts.TryGetValue(FacilityKind.Barracks, out barracks);
        val += barracks * 6f;

        return val;
    }

    // ================================================================
    //  8. 脅威と反撃
    //  次ターンに受ける/与えるダメージの推定
    // ================================================================
    static float EvalThreat(SimBoardState board)
    {
        float score = 0f;
        var enemyUnits = board.GetAliveUnits(Team.Enemy);
        var playerUnits = board.GetAliveUnits(Team.Player);

        // プレイヤーから自軍への脅威
        for (int i = 0; i < playerUnits.Count; i++)
        {
            var pu = playerUnits[i];
            if (pu.IsStunned) continue;

            for (int j = 0; j < enemyUnits.Count; j++)
            {
                var eu = enemyUnits[j];
                float dist = SimUtil.Distance(pu.Position, eu.Position);
                float threatRange = SimActionGenerator.EstimateAttackRange(pu.Kind)
                    + SimActionGenerator.EstimateMoveRange(pu.Kind);

                if (dist <= threatRange)
                {
                    int potentialDmg = SimBoardState.CalcDamage(pu, eu);
                    score -= potentialDmg * 0.3f;

                    // 確殺可能ならさらに危険
                    if (potentialDmg >= eu.HP)
                    {
                        score -= GetAdjustedValue(eu) * 0.5f;
                        // King/Crystal確殺は壊滅的
                        if (eu.Kind == Kind.King) score -= 100f;
                    }
                }
            }

            // クリスタルへの脅威
            float crystalDist = SimUtil.Distance(pu.Position, board.EnemyCrystalPos);
            if (crystalDist <= SimActionGenerator.EstimateAttackRange(pu.Kind) + SimActionGenerator.EstimateMoveRange(pu.Kind))
            {
                var ec = board.GetCrystal(Team.Enemy);
                if (ec != null && ec.IsAlive && ec.ShieldTurns <= 0)
                {
                    int dmg = SimBoardState.CalcDamage(pu, ec);
                    score -= dmg * 0.5f;
                }
            }
        }

        // 自軍から敵への攻撃機会
        for (int i = 0; i < enemyUnits.Count; i++)
        {
            var eu = enemyUnits[i];
            if (eu.IsStunned) continue;

            for (int j = 0; j < playerUnits.Count; j++)
            {
                var pu = playerUnits[j];
                float dist = SimUtil.Distance(eu.Position, pu.Position);
                float threatRange = SimActionGenerator.EstimateAttackRange(eu.Kind)
                    + SimActionGenerator.EstimateMoveRange(eu.Kind);

                if (dist <= threatRange)
                {
                    int potentialDmg = SimBoardState.CalcDamage(eu, pu);
                    score += potentialDmg * 0.2f;

                    if (potentialDmg >= pu.HP)
                    {
                        score += GetAdjustedValue(pu) * 0.3f;
                        if (pu.Kind == Kind.King) score += 80f;
                    }
                }
            }
        }

        return score;
    }

    // ================================================================
    //  9. テンポ
    //  AP残量による行動力の差
    // ================================================================
    static float EvalTempo(SimBoardState board)
    {
        float apDiff = board.EnemyAP - board.PlayerAP;
        return apDiff * 0.5f;
    }

    // ================================================================
    //  ゲーム終了状態
    // ================================================================
    static float EvalTerminal(SimBoardState board)
    {
        // 敵(Player)クリスタル破壊 = AI勝利
        var pCrystal = board.GetCrystal(Team.Player);
        if (pCrystal != null && !pCrystal.IsAlive)
            return 10000f;

        // 自陣(Enemy)クリスタル破壊 = AI敗北
        var eCrystal = board.GetCrystal(Team.Enemy);
        if (eCrystal != null && !eCrystal.IsAlive)
            return -10000f;

        // King死亡もゲーム終了
        var eKing = board.GetKing(Team.Enemy);
        if (eKing != null && !eKing.IsAlive)
            return -8000f;

        var pKing = board.GetKing(Team.Player);
        if (pKing != null && !pKing.IsAlive)
            return 8000f;

        return 0f;
    }
}
