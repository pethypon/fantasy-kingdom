using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIBoardQuery — AIBoardState から抽出した読み取り専用クエリ・分析メソッド
//  盤面状態を変更せず、AIBoardState の公開プロパティを参照して計算を行う。
// =====================================================================
public static class AIBoardQuery
{
    // ================================================================
    //  座標ヘルパー — GridHelper に委譲
    // ================================================================

    /// <summary>有効ユニット判定: null でない && GameObject がアクティブ。Status.IsAlive と異なり HP=0 でも true を返す（旧来の `u == null || !u.gameObject.activeInHierarchy` 反転チェックと等価）。</summary>
    private static bool IsActiveUnit(Status u) => u != null && u.gameObject.activeInHierarchy;

    /// <summary>範囲セル内に存在する最初のユニットを返す（見つからなければnull）</summary>
    public static Status FindFirstUnitInArea(List<Vector3Int> areaCells, List<Status> units)
    {
        foreach (var ac in areaCells)
        {
            foreach (var u in units)
            {
                if (!IsActiveUnit(u)) continue;
                var uCell = GridHelper.ToGridXZ(u.transform.position);
                if (GridHelper.MatchXZ(ac, uCell)) return u;
            }
        }
        return null;
    }

    /// <summary>スキル範囲内のユニットを収集する共通メソッド</summary>
    public static List<Status> CollectUnitsInArea(SkillData skill, Vector3 targetPos, Direction dir, List<Status> candidates)
    {
        var result = new List<Status>();
        var center = GridHelper.ToGridXZ(targetPos);
        var areaCells = SkillSystem.GetAreaPositions(skill.Area, center, dir);

        foreach (var ac in areaCells)
        {
            foreach (var u in candidates)
            {
                if (!IsActiveUnit(u)) continue;
                var uCell = GridHelper.ToGridXZ(u.transform.position);
                if (GridHelper.MatchXZ(ac, uCell)) result.Add(u);
            }
        }
        return result;
    }

    // ================================================================
    //  戦力分析
    // ================================================================

    /// <summary>駒の有利度（LINQなし高速版）</summary>
    public static float GetAdvantageRatio(AIBoardState board)
    {
        int enemyPower = 0;
        for (int i = 0; i < board.AliveEnemyUnits.Count; i++)
            enemyPower += board.AliveEnemyUnits[i].HP + board.AliveEnemyUnits[i].ATK;
        int playerPower = 0;
        for (int i = 0; i < board.AlivePlayerUnits.Count; i++)
            playerPower += board.AlivePlayerUnits[i].HP + board.AlivePlayerUnits[i].ATK;
        if (playerPower + enemyPower == 0) return 0f;
        return (enemyPower - playerPower) / (float)(enemyPower + playerPower);
    }

    /// <summary>味方の最寄り駒距離</summary>
    public static float GetNearestAllyDist(AIBoardState board, Vector3 pos, Status self)
    {
        float nearestSqr = float.MaxValue;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (!IsActiveUnit(u) || u == self) continue;
            float sqr = (pos - u.transform.position).sqrMagnitude;
            if (sqr < nearestSqr) nearestSqr = sqr;
        }
        return nearestSqr < float.MaxValue ? Mathf.Sqrt(nearestSqr) : float.MaxValue;
    }

    /// <summary>指定位置から一定距離内の味方ユニット数</summary>
    public static int CountAlliesNear(AIBoardState board, Vector3 pos, Status self, float radius)
    {
        int count = 0;
        float sqrRadius = radius * radius;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (!IsActiveUnit(u) || u == self) continue;
            if ((pos - u.transform.position).sqrMagnitude <= sqrRadius) count++;
        }
        return count;
    }

    /// <summary>指定位置に到達可能な味方ヒーラーがいるか</summary>
    public static bool HasHealerInRange(AIBoardState board, Vector3 pos, float range)
    {
        float sqrRange = range * range;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (!IsActiveUnit(u)) continue;
            if (u.AssignedSkillId < 0) continue;
            if (!SkillData.Table.TryGetValue(u.AssignedSkillId, out var skill)) continue;
            if (skill.FixedHeal > 0 && (pos - u.transform.position).sqrMagnitude <= sqrRange)
                return true;
        }
        return false;
    }

    // ================================================================
    //  次ターン反撃圏の危険度評価
    // ================================================================

    /// <summary>
    /// あるマスに駒が立ったとき、次ターンにプレイヤーから受ける予想ダメージ合計。
    /// 視界内のプレイヤー駒それぞれについて、攻撃が届くかを簡易判定する。
    /// </summary>
    public static int EstimateCounterDamageAt(AIBoardState board, Vector3 pos, Status self)
    {
        int totalDmg = 0;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (!IsActiveUnit(pu)) continue;
            float maxRange = EstimateAttackRange(pu) + 1.5f; // 移動+攻撃マージン
            float sqrThreshold = maxRange * maxRange;
            if ((pos - pu.transform.position).sqrMagnitude > sqrThreshold) continue;
            int dmg = DamageCalculator.EstimateBaseDamage(pu.ATK, self.DEF);
            totalDmg += dmg;
        }
        return totalDmg;
    }

    /// <summary>駒種から大まかな攻撃射程を返す</summary>
    public static float EstimateAttackRange(Status unit)
    {
        switch (unit.kind)
        {
            case Kind.Archer:      return 3f;
            case Kind.Magic:       return 2f;
            case Kind.Crossbow:    return 2f;
            case Kind.Magicsniper: return 4f;
            case Kind.Bomber:      return 3f;
            default:               return 1.5f;
        }
    }

    // ================================================================
    //  索敵・偵察用
    // ================================================================

    /// <summary>未探索方向の概算ベクトル（自陣クリスタルから見て未探索が多い方向）</summary>
    public static Vector3 GetUnexploredDirection(AIBoardState board, VisionGenerator visionGen)
    {
        if (visionGen == null || visionGen.EnemyExplored == null)
            return Vector3.forward;

        Vector3 center = board.EnemyCrystalPos;
        Vector3 bestDir = Vector3.zero;
        float bestScore = 0f;

        // 8方向を調べて最も未探索セルが多い方向を返す
        Vector3[] dirs = {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            new Vector3(1,0,1).normalized, new Vector3(1,0,-1).normalized,
            new Vector3(-1,0,1).normalized, new Vector3(-1,0,-1).normalized
        };

        foreach (var dir in dirs)
        {
            int unexplored = 0;
            for (int step = 2; step <= 8; step += 2)
            {
                var checkPos = center + dir * step;
                var cell = GridHelper.ToGridXZ(checkPos);
                if (!visionGen.IsExplored(Team.Enemy, cell))
                    unexplored++;
            }
            if (unexplored > bestScore)
            {
                bestScore = unexplored;
                bestDir = dir;
            }
        }

        return bestDir == Vector3.zero ? Vector3.forward : bestDir;
    }

    /// <summary>
    /// ある位置に駒を置いた場合、新たに探索されるマス数を概算する。
    /// Scout等の偵察ユニットが未探索エリアへ向かうべきかの判断に使用。
    /// </summary>
    public static int EstimateNewVisionCells(VisionGenerator visionGen, Vector3 pos)
    {
        if (visionGen == null || visionGen.EnemyExplored == null) return 0;

        int newCells = 0;
        var c = GridHelper.ToGridXZ(pos);
        int cx = c.x;
        int cz = c.z;
        // Scoutの視界範囲（-2~+2 x -2~+2）を概算チェック
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                var cell = new Vector3Int(cx + dx, 0, cz + dz);
                if (!visionGen.IsExplored(Team.Enemy, cell))
                    newCells++;
            }
        }
        return newCells;
    }

    /// <summary>探索済み面積の割合（0～1）</summary>
    public static float GetExplorationRatio(VisionGenerator visionGen, MapCreate mapCreate)
    {
        if (visionGen == null || visionGen.EnemyExplored == null || mapCreate == null) return 1f;
        int totalTiles = mapCreate.SetPos.Count;
        if (totalTiles == 0) return 1f;
        return (float)visionGen.EnemyExplored.Count / totalTiles;
    }

    /// <summary>Playerクリスタル座標を「確定目標」として使ってよいか</summary>
    public static bool CanUsePlayerCrystalAsTarget(AIBoardState board)
    {
        return board.PlayerCrystalVisible;
    }

    /// <summary>指定位置が有効なマップタイルかどうか</summary>
    public static bool IsValidTile(MoveGenerator moveGen, Vector3 pos)
    {
        if (moveGen == null || moveGen.mapcreate == null) return false;
        var cell = GridHelper.ToGridXZ(pos);
        return moveGen.mapcreate.HasTileAt(cell.x, cell.z);
    }

    // ================================================================
    //  索敵・Last Known Position クエリ
    // ================================================================

    /// <summary>最後に見たPlayerユニットの位置リスト（信頼度付き: 0=古い 1=新鮮）</summary>
    public static List<(Vector3Int pos, float reliability)> GetLastKnownPlayerPositions(
        Dictionary<int, AIBoardState.LastKnownInfo> lastKnownPositions, int turnCount)
    {
        var result = new List<(Vector3Int, float)>();
        foreach (var kvp in lastKnownPositions)
        {
            if (!kvp.Value.Valid) continue;
            int age = turnCount - kvp.Value.Turn;
            float reliability = Mathf.Clamp01(1f - age * 0.15f); // 7ターンで信頼度0
            if (reliability > 0f)
                result.Add((kvp.Value.Position, reliability));
        }
        return result;
    }

    // ================================================================
    //  経済分析
    // ================================================================

    /// <summary>建物数を取得</summary>
    public static int GetBuildingCount(AIBoardState board, FacilityKind kind)
    {
        return board.EnemyBuildingCounts.TryGetValue(kind, out int c) ? c : 0;
    }

    /// <summary>
    /// 生産チェーンの不足を判定。
    /// 原料が足りないのに加工施設を建てても無意味なので、
    /// 上流の建物が存在するかチェックする。
    /// </summary>
    public static bool HasUpstreamProducer(AIBoardState board, FacilityKind facility)
    {
        switch (facility)
        {
            // Bakery は Field(小麦)と Well(水)が必要
            case FacilityKind.Bakery:
                return GetBuildingCount(board, FacilityKind.Field) > 0 &&
                       GetBuildingCount(board, FacilityKind.Well) > 0;
            // Field は Well(水)が必要
            case FacilityKind.Field:
                return GetBuildingCount(board, FacilityKind.Well) > 0;
            default:
                return true; // 上流不要
        }
    }

    /// <summary>
    /// 生産チェーンの数量不足を判定。
    /// 「上流施設が1棟あるか」ではなく「需要に対して生産量が足りるか」を判断する。
    /// 戻り値: 不足施設のFacilityKindリスト（優先度順）
    /// </summary>
    public static List<FacilityKind> DiagnoseProductionChainDeficit(AIBoardState board)
    {
        var needed = new List<FacilityKind>();
        if (board.EnemyResources == null) return needed;

        var res = board.EnemyResources;

        // パン不足 → 最優先
        if (res.Bread <= 15)
        {
            if (GetBuildingCount(board, FacilityKind.Bakery) == 0)
            {
                // Bakery建設に必要な上流を先にチェック
                if (GetBuildingCount(board, FacilityKind.Field) == 0)
                    needed.Add(FacilityKind.Field);
                if (GetBuildingCount(board, FacilityKind.Well) == 0)
                    needed.Add(FacilityKind.Well);
                needed.Add(FacilityKind.Bakery);
            }
            else
            {
                // Bakeryはあるが小麦/水が足りない
                if (res.Wheat <= 10 && GetBuildingCount(board, FacilityKind.Field) < 2)
                    needed.Add(FacilityKind.Field);
                if (res.Water <= 10 && GetBuildingCount(board, FacilityKind.Well) < 2)
                    needed.Add(FacilityKind.Well);
                // Bakery追加も検討
                if (res.Wheat > 20 && res.Water > 10 && GetBuildingCount(board, FacilityKind.Bakery) < 2)
                    needed.Add(FacilityKind.Bakery);
            }
        }

        // 木材不足
        if (res.Wood <= 10 && GetBuildingCount(board, FacilityKind.LoggingCamp) == 0)
            needed.Add(FacilityKind.LoggingCamp);

        // 石材不足
        if (res.Stone <= 10 && GetBuildingCount(board, FacilityKind.Quarry) == 0)
            needed.Add(FacilityKind.Quarry);

        // Iron不足（Mineが直接生産）
        if (res.Iron <= 5 && GetBuildingCount(board, FacilityKind.Mine) == 0)
            needed.Add(FacilityKind.Mine);

        // 市民不足
        if (res.Citizen <= 1)
            needed.Add(FacilityKind.House);

        // 基礎資源の基本確保（まだ1棟もない場合）
        if (GetBuildingCount(board, FacilityKind.Well) == 0 && !needed.Contains(FacilityKind.Well))
            needed.Add(FacilityKind.Well);
        if (GetBuildingCount(board, FacilityKind.LoggingCamp) == 0 && !needed.Contains(FacilityKind.LoggingCamp))
            needed.Add(FacilityKind.LoggingCamp);
        if (GetBuildingCount(board, FacilityKind.Quarry) == 0 && !needed.Contains(FacilityKind.Quarry))
            needed.Add(FacilityKind.Quarry);
        if (GetBuildingCount(board, FacilityKind.Field) == 0 && !needed.Contains(FacilityKind.Field))
            needed.Add(FacilityKind.Field);

        return needed;
    }

    /// <summary>経済余剰スコア: 維持費に余裕があるかの指標 (0=ギリギリ 1=余裕)</summary>
    public static float GetEconomicSurplus(AIBoardState board)
    {
        if (board.EnemyResources == null) return 0f;
        // パン・鉄・木を総合的に判断。各 ResourceSurplus_* 以上で余裕あり
        float breadSurplus = Mathf.Clamp01(board.EnemyResources.Bread / AIConstants.ResourceSurplus_Bread);
        float ironSurplus = Mathf.Clamp01(board.EnemyResources.Iron / AIConstants.ResourceSurplus_Iron);
        float woodSurplus = Mathf.Clamp01(board.EnemyResources.Wood / AIConstants.ResourceSurplus_Wood);
        return (breadSurplus + ironSurplus + woodSurplus) / 3f;
    }

    /// <summary>
    /// 資源のボトルネック度を返す（0〜1、高いほど不足）。
    /// AIが「何を建てるべきか」の判断に使用。閾値未満で不足感、0で最大不足。
    /// </summary>
    public static float GetResourceScarcity(AIBoardState board, string resourceName)
    {
        if (board.EnemyResources == null) return 0f;
        int amount = GetResourceAmount(board, resourceName);
        return amount < 0 ? 0f : Mathf.Clamp01(1f - amount / AIConstants.ResourceScarcity_Threshold);
    }

    /// <summary>資源名から現在量を取得（不明なら-1）</summary>
    public static int GetResourceAmount(AIBoardState board, string resourceName)
    {
        if (board.EnemyResources == null) return -1;
        switch (resourceName)
        {
            case "Wood":     return board.EnemyResources.Wood;
            case "Stone":    return board.EnemyResources.Stone;
            case "Water":    return board.EnemyResources.Water;
            case "Wheat":    return board.EnemyResources.Wheat;
            case "Bread":    return board.EnemyResources.Bread;
            case "Iron":     return board.EnemyResources.Iron;
            case "MagicOre": return board.EnemyResources.MagicOre;
            default:         return -1;
        }
    }

    /// <summary>指定位置の近くに壁/防衛建築があるか</summary>
    public static bool HasDefensiveStructureNear(BuildSystem buildSystem, Vector3 pos, float range)
    {
        if (buildSystem == null) return false;
        Transform parent = buildSystem.GetBuildingParent(Team.Enemy);
        if (parent == null) return false;
        float sqrRange = range * range;
        foreach (Transform child in parent)
        {
            var s = child.GetComponent<Status>();
            if (s == null || s.HP <= 0) continue;
            if (s.facilityKind == FacilityKind.WoodWall || s.facilityKind == FacilityKind.StoneWall
                || s.facilityKind == FacilityKind.Mortar || s.facilityKind == FacilityKind.Cannon)
            {
                if ((pos - child.position).sqrMagnitude <= sqrRange) return true;
            }
        }
        return false;
    }
}
