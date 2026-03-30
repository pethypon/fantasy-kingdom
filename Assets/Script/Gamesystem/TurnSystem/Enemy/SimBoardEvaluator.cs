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
// 10. 資源投影 (Resource Projection) — 建物からの将来収入
// 11. 領土 (Territory) — ユニット展開範囲
// 12. 連携攻撃 (Coordination) — 複数ユニットの集中攻撃
// 13. 視界 (Vision) — マップ認識範囲の差
// =====================================================================
public static class SimBoardEvaluator
{
    // ================================================================
    //  メイン評価関数
    // ================================================================
    public static float Evaluate(SimBoardState board)
    {
        // ゲーム終了状態 — 即座に極値を返す
        float terminal = EvalTerminal(board);
        if (terminal != 0f) return terminal;

        float score = 0f;
        score += EvalMaterial(board)          * AIConstants.W_MATERIAL;
        score += EvalCrystalSafety(board)     * AIConstants.W_CRYSTAL_SAFETY;
        score += EvalKingSafety(board)        * AIConstants.W_KING_SAFETY;
        score += EvalFrontline(board)         * AIConstants.W_FRONTLINE;
        score += EvalPositional(board)        * AIConstants.W_POSITIONAL;
        score += EvalMobility(board)          * AIConstants.W_MOBILITY;
        score += EvalEconomy(board)           * AIConstants.W_ECONOMY;
        score += EvalThreat(board)            * AIConstants.W_THREAT;
        score += EvalTempo(board)             * AIConstants.W_TEMPO;
        score += EvalResourceProjection(board) * AIConstants.W_RESOURCE_PROJ;
        score += EvalTerritory(board)          * AIConstants.W_TERRITORY;
        score += EvalCoordination(board)       * AIConstants.W_COORDINATION;
        score += EvalVision(board)             * AIConstants.W_VISION;

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
        float baseVal = AIConstants.GetPieceValue(unit.Kind);

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
            score += hpRatio * AIConstants.CS_HP_Weight;

            // シールド中は安全
            if (eCrystal.ShieldTurns > 0)
                score += eCrystal.ShieldTurns * AIConstants.CS_Shield_Per_Turn;

            // 周囲の駒数（距離減衰付き — 近いほど影響が大きい）
            float guardScore = 0f, threatScore = 0f;
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Type != Type.Unit) continue;
                int dist = SimUtil.Manhattan(u.Position, board.EnemyCrystalPos);
                if (dist > 7) continue; // 遠すぎる駒は無視
                // 距離減衰: dist=1→1.0, dist=4→0.4, dist=7→0.1
                float decay = 1f / (1f + 0.5f * dist);
                if (u.Team == Team.Enemy)
                    guardScore += AIConstants.CS_Guard_Per_Unit * decay;
                else if (u.Team == Team.Player)
                    threatScore += AIConstants.CS_Threat_Per_Unit * decay;
            }
            score += Mathf.Min(guardScore, AIConstants.CS_Guard_Max);
            score -= threatScore;

            // 脅威がある場合のHP低下ペナルティ増幅
            if (threatScore > 0f && hpRatio < 0.5f)
                score -= (1f - hpRatio) * threatScore * 0.6f;
        }
        else
        {
            score -= AIConstants.CS_Lost_Penalty;
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

            // 自軍が敵クリスタル付近にいるボーナス（距離減衰付き）
            float attackerScore = 0f;
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Team != Team.Enemy || u.Type != Type.Unit) continue;
                int dist = SimUtil.Manhattan(u.Position, board.PlayerCrystalPos);
                if (dist > 7) continue;
                float decay = 1f / (1f + 0.5f * dist);
                attackerScore += 8f * decay;
            }
            score += attackerScore;
        }
        else
        {
            score += AIConstants.CS_Lost_Penalty;
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
            score += hpRatio * AIConstants.KS_HP_Weight;

            // Kingが敵に囲まれていると危険
            int nearbyEnemies = 0;
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Team != Team.Player || u.Type != Type.Unit) continue;
                float dist = SimUtil.Distance(u.Position, eKing.Position);
                float threatRange = AIConstants.GetAttackRange(u.Kind) + SimActionGenerator.EstimateMoveRange(u.Kind);
                if (dist <= threatRange) nearbyEnemies++;
            }
            if (nearbyEnemies >= 2) score -= nearbyEnemies * AIConstants.KS_Threat_Per_Enemy;

            // 味方による護衛
            int nearbyAllies = 0;
            for (int i = 0; i < board.Units.Count; i++)
            {
                var u = board.Units[i];
                if (!u.IsAlive || u.Team != Team.Enemy || u.Type != Type.Unit) continue;
                if (u.Kind == Kind.King) continue;
                if (SimUtil.Manhattan(u.Position, eKing.Position) <= 3) nearbyAllies++;
            }
            score += Mathf.Min(nearbyAllies * AIConstants.KS_Guard_Per_Ally, AIConstants.KS_Guard_Max);
        }
        else if (eKing != null)
        {
            score -= AIConstants.KS_Death_Penalty;
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
                if (dist <= AIConstants.GetAttackRange(u.Kind) + 1f)
                    score += 8f;
            }
        }
        else if (pKing != null)
        {
            score += AIConstants.KS_Death_Penalty;
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
        score += enemyHouses * AIConstants.BUILD_House_AP_Value;

        return score;
    }

    static float EvalBuildingValue(Dictionary<FacilityKind, int> counts)
    {
        float val = 0f;
        int total = 0;
        foreach (var kvp in counts)
            total += kvp.Value;

        val += Mathf.Min(total * AIConstants.BUILD_Per_Building, AIConstants.BUILD_Max_Total);

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
        val += coreTypes * AIConstants.BUILD_Core_Type_Value;

        // 加工施設
        FacilityKind[] procKinds = {
            FacilityKind.LumberMill, FacilityKind.StoneWorks,
            FacilityKind.Bakery, FacilityKind.Smelter
        };
        foreach (var fk in procKinds)
        {
            int c = 0;
            counts.TryGetValue(fk, out c);
            if (c > 0) val += AIConstants.BUILD_Process_Value;
        }

        // 兵舎
        int barracks = 0;
        counts.TryGetValue(FacilityKind.Barracks, out barracks);
        val += barracks * AIConstants.BUILD_Barracks_Value;

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
    //  9. テンポ
    //  AP残量による行動力の差
    // ================================================================
    static float EvalTempo(SimBoardState board)
    {
        float apDiff = board.EnemyAP - board.PlayerAP;
        float base_score = apDiff * 0.5f;
        // 序盤はテンポの価値が高い（先行展開の重要性）
        if (board.TurnCount <= AIConstants.TurnEarlyEnd)
            base_score *= AIConstants.W_TEMPO_EARLY_MULT;
        return base_score;
    }

    // ================================================================
    // 10. 資源投影 (Resource Projection)
    //  建物からの将来収入を見積もる
    // ================================================================
    static float EvalResourceProjection(SimBoardState board)
    {
        float enemyProj = CalcResourceProjection(board.EnemyBuildingCounts);
        float playerProj = CalcResourceProjection(board.PlayerBuildingCounts);
        return enemyProj - playerProj;
    }

    static float CalcResourceProjection(Dictionary<FacilityKind, int> counts)
    {
        float proj = 0f;

        // 各建物からの推定収入（将来ターンほど割引）
        foreach (var kvp in counts)
        {
            float perBuilding = AIConstants.GetResourceProjection(kvp.Key);
            float turnRevenue = perBuilding * kvp.Value;
            float discount = 1f;
            int turns = (int)AIConstants.RP_Projection_Turns;
            for (int t = 0; t < turns; t++)
            {
                proj += turnRevenue * discount;
                discount *= AIConstants.RP_Discount_Rate;
            }
        }

        // シナジーボーナス（原料→加工チェーン）
        int logging = 0, lumber = 0, quarry = 0, stone = 0;
        int mine = 0, smelter = 0, field = 0, bakery = 0;
        counts.TryGetValue(FacilityKind.LoggingCamp, out logging);
        counts.TryGetValue(FacilityKind.LumberMill, out lumber);
        counts.TryGetValue(FacilityKind.Quarry, out quarry);
        counts.TryGetValue(FacilityKind.StoneWorks, out stone);
        counts.TryGetValue(FacilityKind.Mine, out mine);
        counts.TryGetValue(FacilityKind.Smelter, out smelter);
        counts.TryGetValue(FacilityKind.Field, out field);
        counts.TryGetValue(FacilityKind.Bakery, out bakery);

        if (logging > 0 && lumber > 0) proj += AIConstants.SYNERGY_LogLumber;
        if (quarry > 0 && stone > 0)   proj += AIConstants.SYNERGY_QuarryStone;
        if (mine > 0 && smelter > 0)   proj += AIConstants.SYNERGY_MineSmelter;
        if (field > 0 && bakery > 0)    proj += AIConstants.SYNERGY_FieldBakery;

        return proj;
    }

    // ================================================================
    // 11. 領土 (Territory)
    //  ユニットの展開範囲の広さを評価
    // ================================================================
    static float EvalTerritory(SimBoardState board)
    {
        float enemyTerritory = CalcTerritory(board, Team.Enemy);
        float playerTerritory = CalcTerritory(board, Team.Player);
        return enemyTerritory - playerTerritory;
    }

    // マンハッタン距離3以内のオフセットを事前計算（25セル）
    static readonly Vector3Int[] TerritoryOffsets = BuildTerritoryOffsets();
    static Vector3Int[] BuildTerritoryOffsets()
    {
        var list = new List<Vector3Int>();
        for (int dx = -3; dx <= 3; dx++)
            for (int dz = -3; dz <= 3; dz++)
                if (Mathf.Abs(dx) + Mathf.Abs(dz) <= 3)
                    list.Add(new Vector3Int(dx, 0, dz));
        return list.ToArray();
    }

    static float CalcTerritory(SimBoardState board, Team team)
    {
        var cells = SimBoardPool.RentHashSet();
        for (int i = 0; i < board.Units.Count; i++)
        {
            var u = board.Units[i];
            if (!u.IsAlive || u.Team != team || u.Type != Type.Unit) continue;

            // 事前計算済みオフセットを使用
            for (int j = 0; j < TerritoryOffsets.Length; j++)
            {
                var cell = new Vector3Int(
                    u.Position.x + TerritoryOffsets[j].x, 0,
                    u.Position.z + TerritoryOffsets[j].z);
                if (board.MapTiles.Contains(cell))
                    cells.Add(cell);
            }
        }

        float result = Mathf.Min(cells.Count * AIConstants.TERRITORY_Per_Cell, AIConstants.TERRITORY_Max);
        SimBoardPool.ReturnHashSet(cells);
        return result;
    }

    // ================================================================
    // 12. 連携攻撃 (Coordination)
    //  複数ユニットが同一ターゲットを脅かすボーナス
    // ================================================================
    static float EvalCoordination(SimBoardState board)
    {
        float score = 0f;

        // AI(Enemy)がPlayer目標を集中攻撃できるか
        score += CalcCoordinationScore(board, Team.Enemy, Team.Player);
        // Player がAI目標を集中攻撃できるか（マイナス）
        score -= CalcCoordinationScore(board, Team.Player, Team.Enemy);

        return score;
    }

    // 最大攻撃+移動射程（マンハッタン枝刈り用の上限値）
    const int COORD_MaxRange = 8; // Magicsniper(4) + 移動(4) 程度

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

    // ================================================================
    // 13. 視界 (Vision)
    //  マップ認識範囲の差を評価
    // ================================================================
    static float EvalVision(SimBoardState board)
    {
        int enemyVision = board.EstimateVisionCells(Team.Enemy);
        int playerVision = board.EstimateVisionCells(Team.Player);

        float enemyScore = Mathf.Min(enemyVision * AIConstants.VISION_Per_Cell, AIConstants.VISION_Max);
        float playerScore = Mathf.Min(playerVision * AIConstants.VISION_Per_Cell, AIConstants.VISION_Max);

        // Scoutユニットによる追加ボーナス（偵察能力）
        float scoutBonus = 0f;
        for (int i = 0; i < board.Units.Count; i++)
        {
            var u = board.Units[i];
            if (!u.IsAlive || u.Kind != Kind.Scout) continue;
            if (u.Team == Team.Enemy) scoutBonus += AIConstants.VISION_Scout_Bonus;
            else if (u.Team == Team.Player) scoutBonus -= AIConstants.VISION_Scout_Bonus;
        }

        return (enemyScore - playerScore) + scoutBonus;
    }

    // ================================================================
    //  ゲーム終了状態
    // ================================================================
    static float EvalTerminal(SimBoardState board)
    {
        // 敵(Player)クリスタル破壊 = AI勝利
        var pCrystal = board.GetCrystal(Team.Player);
        if (pCrystal != null && !pCrystal.IsAlive)
            return AIConstants.TERMINAL_Win;

        // 自陣(Enemy)クリスタル破壊 = AI敗北
        var eCrystal = board.GetCrystal(Team.Enemy);
        if (eCrystal != null && !eCrystal.IsAlive)
            return AIConstants.TERMINAL_Lose;

        // King死亡もゲーム終了
        var eKing = board.GetKing(Team.Enemy);
        if (eKing != null && !eKing.IsAlive)
            return AIConstants.TERMINAL_King_Lose;

        var pKing = board.GetKing(Team.Player);
        if (pKing != null && !pKing.IsAlive)
            return AIConstants.TERMINAL_King_Win;

        return 0f;
    }
}
