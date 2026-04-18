using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimCombatEval — 戦闘関連の評価メトリクス
//  駒価値差 (Material), 脅威/反撃 (Threat/Counter), 連携攻撃 (Coordination)
// =====================================================================
public static class SimCombatEval
{
    // 最大攻撃+移動射程（マンハッタン枝刈り用の上限値）
    const int COORD_MaxRange = 8; // Magicsniper(4) + 移動(4) 程度

    // ================================================================
    //  駒価値差 (Material)
    //  HP減衰 + ステータス効果ペナルティ付き
    // ================================================================
    public static float EvalMaterial(SimBoardState board)
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

    /// <summary>
    /// ユニットのステータス効果を考慮した調整済み価値を返す。
    /// 他の評価クラスからも参照されるため public。
    /// </summary>
    public static float GetAdjustedValue(SimUnit unit)
    {
        float baseVal = AIConstants.GetPieceValue(unit.Kind);

        // HP減衰
        float hpRatio = unit.MaxHP > 0 ? (float)unit.HP / unit.MaxHP : 1f;
        float val = baseVal * (0.3f + 0.7f * hpRatio);

        // ステータス効果ペナルティ
        if (unit.IsStunned) val *= 0.5f;
        if (unit.IsMovementBlocked) val *= 0.7f;
        if (unit.HasDebuff(StatusEffectType.Poison)) val *= 0.85f;

        // バフボーナス
        if (unit.HasBuff(BuffType.Offensive)) val *= 1.1f;
        if (unit.HasBuff(BuffType.Defensive)) val *= 1.08f;
        if (unit.HasBuff(BuffType.Barrier)) val *= 1.12f;

        return val;
    }

    // ================================================================
    //  脅威と反撃
    //  次ターンに受ける/与えるダメージの推定
    // ================================================================
    public static float EvalThreat(SimBoardState board)
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
                // マンハッタン距離で早期枝刈り
                if (SimUtil.Manhattan(pu.Position, eu.Position) > COORD_MaxRange) continue;

                float dist = SimUtil.Distance(pu.Position, eu.Position);
                float threatRange = AIConstants.GetAttackRange(pu.Kind)
                    + SimActionGenerator.EstimateMoveRange(pu.Kind);

                if (dist <= threatRange)
                {
                    int potentialDmg = SimBoardState.CalcDamage(pu, eu);
                    score -= potentialDmg * 0.3f;

                    // 確殺可能ならさらに危険
                    if (potentialDmg >= eu.HP)
                    {
                        score -= GetAdjustedValue(eu) * 0.5f;
                        // King確殺は壊滅的
                        if (eu.Kind == Kind.King) score -= 100f;
                    }
                }
            }

            // クリスタルへの脅威
            float crystalDist = SimUtil.Distance(pu.Position, board.EnemyCrystalPos);
            if (crystalDist <= AIConstants.GetAttackRange(pu.Kind) + SimActionGenerator.EstimateMoveRange(pu.Kind))
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
                // マンハッタン距離で早期枝刈り
                if (SimUtil.Manhattan(eu.Position, pu.Position) > COORD_MaxRange) continue;

                float dist = SimUtil.Distance(eu.Position, pu.Position);
                float threatRange = AIConstants.GetAttackRange(eu.Kind)
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
    //  連携攻撃 (Coordination)
    //  複数ユニットが同一ターゲットを脅かすボーナス
    // ================================================================
    public static float EvalCoordination(SimBoardState board)
    {
        float score = 0f;

        // AI(Enemy)がPlayer目標を集中攻撃できるか
        score += CalcCoordinationScore(board, Team.Enemy, Team.Player);
        // Player がAI目標を集中攻撃できるか（マイナス）
        score -= CalcCoordinationScore(board, Team.Player, Team.Enemy);

        return score;
    }

    static float CalcCoordinationScore(SimBoardState board, Team attackerTeam, Team defenderTeam)
    {
        float score = 0f;
        var attackers = board.GetAliveUnits(attackerTeam);
        var defenders = board.GetAliveUnits(defenderTeam);

        for (int i = 0; i < defenders.Count; i++)
        {
            var target = defenders[i];
            int threateningCount = 0;
            int totalDmg = 0;

            for (int j = 0; j < attackers.Count; j++)
            {
                var atk = attackers[j];
                if (atk.IsStunned) continue;

                // 安価なマンハッタン距離で早期枝刈り
                int manhattan = SimUtil.Manhattan(atk.Position, target.Position);
                if (manhattan > COORD_MaxRange) continue;

                float dist = SimUtil.Distance(atk.Position, target.Position);
                float range = AIConstants.GetAttackRange(atk.Kind)
                    + SimActionGenerator.EstimateMoveRange(atk.Kind);

                if (dist <= range)
                {
                    threateningCount++;
                    totalDmg += SimBoardState.CalcDamage(atk, target);
                }
            }

            if (threateningCount >= 3)
                score += AIConstants.COORD_Triple_Threat;
            else if (threateningCount >= 2)
                score += AIConstants.COORD_Dual_Threat;

            if (threateningCount >= 2 && totalDmg >= target.HP)
                score += AIConstants.COORD_Kill_Threat;
        }

        return score;
    }
}
