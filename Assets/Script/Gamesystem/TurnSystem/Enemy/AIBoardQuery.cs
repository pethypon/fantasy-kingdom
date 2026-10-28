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
            enemyPower += board.AliveEnemyUnits[i].HP + board.AliveEnemyUnits[i].Barrier + board.AliveEnemyUnits[i].ATK;
        int playerPower = 0;
        for (int i = 0; i < board.AlivePlayerUnits.Count; i++)
            playerPower += board.AlivePlayerUnits[i].HP + board.AlivePlayerUnits[i].Barrier + board.AlivePlayerUnits[i].ATK;
        if (playerPower + enemyPower == 0) return 0f;
        return (enemyPower - playerPower) / (float)(enemyPower + playerPower);
    }

    /// <summary>味方の最寄り駒距離</summary>
    public static float GetNearestAllyDist(AIBoardState board, Vector3 pos, Status self)
    {
        if(board.NearestAllyCache.TryGetValue((pos,self),out var cached)) return cached;
        float nearestSqr = float.MaxValue;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (!IsActiveUnit(u) || u == self) continue;
            float sqr = (pos - u.transform.position).sqrMagnitude;
            if (sqr < nearestSqr) nearestSqr = sqr;
        }
        return board.NearestAllyCache[(pos,self)] = nearestSqr < float.MaxValue ? Mathf.Sqrt(nearestSqr) : float.MaxValue;
    }

    /// <summary>指定位置から一定距離内の味方ユニット数</summary>
    public static int CountAlliesNear(AIBoardState board, Vector3 pos, Status self, float radius)
    {
        if(board.AllyDensityCache.TryGetValue((pos,self,radius),out var cached)) return cached;
        int count = 0;
        float sqrRadius = radius * radius;
        foreach (var u in board.Allies.Near(pos, radius))
        {
            if (!IsActiveUnit(u) || u == self) continue;
            if ((pos - u.transform.position).sqrMagnitude <= sqrRadius) count++;
        }
        return board.AllyDensityCache[(pos,self,radius)] = count;
    }

    /// <summary>指定位置に到達可能な味方ヒーラーがいるか</summary>
    public static bool HasHealerInRange(AIBoardState board, Vector3 pos, float range)
    {
        if(board.HealerCache.TryGetValue((pos,range),out var cached)) return cached;
        float sqrRange = range * range;
        foreach (var u in board.Allies.Near(pos, range))
        {
            if (!IsActiveUnit(u)) continue;
            if (!AttackPatterns.CanUseSkills(u)) continue;
            var definition = SpecialSkillRuntime.Definition(u);
            if (definition != null && definition.enabledForPlay && definition.ConfigurationError == null
                && (definition.ability == UnitSpecialSkill.WhiteMageHeal || definition.ability == UnitSpecialSkill.HolyKnightSanctuary)
                && (pos - u.transform.position).sqrMagnitude <= sqrRange)
                return board.HealerCache[(pos,range)] = true;
            if (u.AssignedSkillId < 0) continue;
            if (!SkillData.Table.TryGetValue(u.AssignedSkillId, out var skill)) continue;
            if (skill.FixedHeal > 0 && (pos - u.transform.position).sqrMagnitude <= sqrRange)
                return board.HealerCache[(pos,range)] = true;
        }
        return board.HealerCache[(pos,range)] = false;
    }

    // ================================================================
    //  次ターン反撃圏の危険度評価
    // ================================================================

    /// <summary>
    /// あるマスに駒が立ったとき、次ターンにプレイヤーから受ける予想ダメージ合計。
    /// 視界内のプレイヤー駒それぞれについて、攻撃が届くかを簡易判定する。
    /// </summary>
    public static int EstimateCounterDamageAt(AIBoardState board, Vector3 pos, Status self, bool ignoreFiniteBarrier = false)
    {
        int stateKey;
        unchecked
        {
            stateKey = ((DamageCalculator.UsesLegacyDefence ? self.DEF : 0) * 397 ^ self.Barrier) * 397 ^ self.HP;
            var state = self.Special;
            stateKey = stateKey * 397 ^ self.ShieldTurns;
            stateKey = stateKey * 397 ^ (state?.damageNullUnused == true && state.damageNullTurns > 0 ? 1 : 0);
            stateKey = stateKey * 397 ^ (state?.nextDamageDouble == true ? 1 : 0);
            stateKey = stateKey * 397 ^ (state != null && (state.holyKnightTurns > 0 || state.holyKnightRecoveryLock) ? 1 : 0);
        }
        if(!ignoreFiniteBarrier && board.CounterCache.TryGetValue((pos,self,stateKey),out var cached)) return cached;
        long totalDmg = 0;
        var budget = AIObservedDamageBudget.Capture(self,ignoreFiniteBarrier);
        totalDmg += AIActionEvaluator.EstimateObservedHitLoss(self, board,
            MainCrystalRules.ObservedCounterDamage(board, self, pos), HpChangeKind.NormalDamage, ref budget);
        // A dragon can breathe across its fixed territory even when its current position
        // lies beyond the local index radius. Only the board's observed actors are eligible.
        foreach (var opponent in board.AlivePlayerUnits)
        {
            if (!ObservedDragonThreat.IsDragon(opponent)) continue;
            totalDmg = Math.Min(int.MaxValue, totalDmg + AIActionEvaluator.EstimateObservedHitLoss(self,board,
                ObservedDragonThreat.EstimateBreathDamage(board, opponent, self, pos), HpChangeKind.NormalDamage,ref budget));
        }
        foreach (var pu in board.Enemies.Near(pos, 12f))
        {
            if (!IsActiveUnit(pu) || ObservedDragonThreat.IsDragon(pu) || BoardActionProfile.For(pu)?.CanAttack == false
                || !SpecialSkillRuntime.CanNormalAttackTarget(pu, self)||pu.Special?.sniperActive==true) continue;
            int knownMoveCost = board.KnownEnvironmentMoveCost(pu.transform.position, pos);
            float movementMargin = knownMoveCost > 0 ? 1.5f / (1 + knownMoveCost) : 1.5f;
            float maxRange = EstimateAttackRange(pu) + movementMargin;
            float sqrThreshold = maxRange * maxRange;
            if ((pos - pu.transform.position).sqrMagnitude > sqrThreshold) continue;
            int raw = AIActionEvaluator.EstimateSpecialAwareAttack(pu,self,board);
            bool ranged = DamageCalculator.IsRangedAttack(pu);
            bool stone = ((pu.GrowthData?.attackTags ?? CombatAttackTags.None) & CombatAttackTags.Stone) != 0;
            float oldCover = board.KnownEnvironmentDefinition(self.transform.position) != null
                ? board.ReconMap.Environment.IncomingDamageMultiplier(self.transform.position, ranged, stone) : 1;
            float newCover = board.KnownEnvironmentDefinition(pos) != null
                ? board.ReconMap.Environment.IncomingDamageMultiplier(pos, ranged, stone) : 1;
            raw = (int)Math.Min(int.MaxValue, Math.Max(0, raw * (double)newCover / Mathf.Max(.01f, oldCover)));
            raw = BarrierSystem.NormalAttackDamage(pu.kind, budget.Barrier, raw);
            int dmg = AIActionEvaluator.EstimateObservedHitLoss(self,board,raw,HpChangeKind.NormalDamage,ref budget);
            totalDmg = Math.Min(int.MaxValue, totalDmg + dmg);
        }
        totalDmg=Math.Min(int.MaxValue,totalDmg+AIActionEvaluator.EstimateSpecialReservedThreatAt(board,self,pos,ref budget));
        totalDmg=Math.Min(int.MaxValue,totalDmg+board.KnownEnvironmentRisk(self,pos,ref budget));
        if (!ignoreFiniteBarrier) board.CounterCache[(pos,self,stateKey)] = (int)totalDmg;
        return (int)totalDmg;
    }

    /// <summary>駒種から大まかな攻撃射程を返す</summary>
    public static float EstimateAttackRange(Status unit)
    {
        var profile = BoardActionProfile.For(unit);
        if (profile != null && !profile.CanAttack) return 0;
        if (profile?.attack?.useCustom == true)
        {
            float squared = 0;
            foreach (var offset in profile.attack.Offsets) squared = Mathf.Max(squared, offset.sqrMagnitude);
            return Mathf.Sqrt(squared);
        }
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
        if (visionGen == null)
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
                if (!visionGen.IsExplored(board.ActorTeam, cell))
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
    public static int EstimateNewVisionCells(VisionGenerator visionGen, Vector3 pos, Team actorTeam = Team.Enemy,
        MapCreate map = null, Status observer = null)
    {
        // Candidate information only: public bounds + this faction's explored cells.
        // Never query unknown terrain/heights or promise that an untested LoS is clear.
        if (visionGen == null || map == null || map.maxX <= 0 || map.maxZ <= 0
            || actorTeam != Team.Player && actorTeam != Team.Enemy
            || observer != null && (!observer.IsAlive || observer.team != actorTeam)) return 0;
        var c = GridHelper.ToGridXZ(pos);
        if (c.x < 0 || c.z < 0 || c.x >= map.maxX || c.z >= map.maxZ) return 0;
        int newCells = visionGen.IsExplored(actorTeam, c) ? 0 : 1;
        if (observer != null && observer.Special?.visionZeroTurns > 0) return newCells;
        if (observer != null && (StatusEffectSystem.HasDebuff(observer, StatusEffectType.Blind)
            || StatusEffectSystem.HasDebuff(observer, StatusEffectType.NarrowVision)))
        {
            var front = c + new Vector3Int(0, 0, MovePatterns.DirZ(observer.direction));
            return newCells + (front.x >= 0 && front.z >= 0 && front.x < map.maxX && front.z < map.maxZ
                && !visionGen.IsExplored(actorTeam, front) ? 1 : 0);
        }
        var offsets = observer != null ? VisionGenerator.BaseVisionOffsets(observer)
            : VisionGenerator.BaseVisionOffsets(Kind.Scout);
        foreach (var offset in offsets)
        {
            // Legacy masks contain the same XZ at y=-1 and y=0. Count the
            // horizontal layer once; authored 8x8 profiles also use y=0.
            if (offset.y != 0 || offset.x == 0 && offset.z == 0) continue;
            var cell = c + new Vector3Int(offset.x, 0, offset.z);
            if (cell.x < 0 || cell.z < 0 || cell.x >= map.maxX || cell.z >= map.maxZ) continue;
            if (!visionGen.IsExplored(actorTeam, cell)) newCells++;
        }
        return newCells;
    }

    /// <summary>探索済み面積の割合（0～1）</summary>
    public static float GetExplorationRatio(VisionGenerator visionGen, MapCreate mapCreate, Team actorTeam = Team.Enemy)
    {
        if (visionGen == null || mapCreate == null) return 1f;
        int totalTiles = mapCreate.SetPos.Count;
        if (totalTiles == 0) return 1f;
        var explored = actorTeam == Team.Player ? visionGen.PlayerExplored : visionGen.EnemyExplored;
        return (float)explored.Count / totalTiles;
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
        => HasProductionInputs(board, FacilityData.GetLevel(facility, 1));

    public static bool HasProductionInputs(AIBoardState board, FacilityData.FacilityLevelData recipe)
    {
        if (board == null || board.EnemyResources == null) return false;
        var demand = board.ProductionDemand;
        for (int i = 0; i < StrategicEconomyForecast.ResourceCount; i++)
        {
            if (StrategicEconomyForecast.Amount(recipe.Input, i) <= 0) continue;
            var resource = demand.Get((ResourceKind)i);
            if (resource.ProductionPerTurn <= 0 && resource.Stock <= 0) return false;
        }
        return true;
    }

    /// <summary>
    /// 生産チェーンの数量不足を判定。
    /// 「上流施設が1棟あるか」ではなく「需要に対して生産量が足りるか」を判断する。
    /// 戻り値: 不足施設のFacilityKindリスト（優先度順）
    /// </summary>
    public static List<FacilityKind> DiagnoseProductionChainDeficit(AIBoardState board)
    {
        return board == null || board.EnemyResources == null ? new List<FacilityKind>()
            : new List<FacilityKind>(board.ProductionDemand.RecommendedFacilities);
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
    public static bool HasDefensiveStructureNear(BuildSystem buildSystem, Vector3 pos, float range, Team actorTeam = Team.Enemy)
    {
        if (buildSystem == null) return false;
        Transform parent = buildSystem.GetBuildingParent(actorTeam);
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
