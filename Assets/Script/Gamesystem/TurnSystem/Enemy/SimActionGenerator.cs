using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimActionGenerator — SimBoardState上で候補行動を生成する
//  MoveGenerator / AttackGenerator の移動・攻撃パターンを純粋関数として再現
//  GameObjectに一切依存しない
//
//  改善点:
//  - Direction.S のZ反転を完全再現
//  - スキル攻撃位置をAttackGenerator.AttackPatterns.SkillAttackPositions/AttackPatterns.SkillFixedPositionsと同一に再現
//  - スタン・凍結・束縛による行動制限を考慮
//  - 召喚候補も生成
//  - 壁建築候補も生成
// =====================================================================
public static class SimActionGenerator
{
    // ================================================================
    //  パターン参照（MovePatterns / AttackPatterns に一元化済み）
    // ================================================================

    // ================================================================
    //  候補行動生成: あるチームの全ユニットの全行動を列挙
    // ================================================================
    // ---- 再利用バッファ（GC削減） ----
    [System.ThreadStatic] static List<SimUnit> _unitBuffer;
    [System.ThreadStatic] static HashSet<Vector3Int> _skillPosBuffer;

    public static List<SimAction> GenerateAllActions(SimBoardState board, Team team)
    {
        var actions = new List<SimAction>();
        int ap = board.GetAP(team);
        if (ap <= 0) return actions;

        // ユニットリスト再利用（GCゼロ）
        if (_unitBuffer == null) _unitBuffer = new List<SimUnit>();
        board.GetAliveUnitsNonAlloc(team, _unitBuffer);
        Team enemyTeam = team == Team.Enemy ? Team.Player : Team.Enemy;

        for (int i = 0; i < _unitBuffer.Count; i++)
        {
            var unit = _unitBuffer[i];
            if (unit.IsStunned) continue;

            if (!unit.IsMovementBlocked)
                GenerateMoveActions(board, unit, team, ap, actions);
            GenerateAttackActions(board, unit, team, enemyTeam, ap, actions);
            GenerateSkillActions(board, unit, team, enemyTeam, ap, actions);
        }

        // 建築・召喚候補
        GenerateBuildActions(board, team, ap, actions);
        GenerateSummonActions(board, team, ap, actions);

        return actions;
    }

    // ================================================================
    //  移動候補
    // ================================================================
    static void GenerateMoveActions(SimBoardState board, SimUnit unit, Team team,
        int ap, List<SimAction> results)
    {
        if (!MovePatterns.Map.TryGetValue(unit.Kind, out var predicate)) return;

        int cost = board.CalcMoveCost(unit);
        if (cost > ap) return;

        var pos = unit.Position;
        int dirZ = MovePatterns.DirZ(unit.Direction);
        bool dirIndep = MovePatterns.DirectionIndependent.Contains(unit.Kind);

        foreach (var tile in board.MapTiles)
        {
            float dx = tile.x - pos.x;
            float dz = tile.z - pos.z;

            // Direction.S の場合、方向依存パターンは Z を反転
            float dzAdj = dirIndep ? dz : dz * dirZ;

            if (!predicate(dx, dzAdj)) continue;
            if (board.IsOccupied(tile)) continue;

            results.Add(new SimAction
            {
                Type = SimActionType.Move,
                UnitId = unit.Id,
                TargetPos = tile,
                APCost = cost,
                ActorTeam = team
            });
        }
    }

    // ================================================================
    //  攻撃候補
    // ================================================================
    static void GenerateAttackActions(SimBoardState board, SimUnit unit, Team team,
        Team enemyTeam, int ap, List<SimAction> results)
    {
        if (!AttackPatterns.NormalMap.TryGetValue(unit.Kind, out var predicate)) return;

        int cost = board.CalcAttackCost(unit);
        if (cost > ap) return;

        var pos = unit.Position;
        int dirZ = MovePatterns.DirZ(unit.Direction);
        bool dirIndep = AttackPatterns.DirectionIndependent.Contains(unit.Kind);

        // 全敵ユニット + 敵クリスタルを攻撃対象（Listアロケーション排除）
        for (int i = 0; i < board.Units.Count; i++)
        {
            var target = board.Units[i];
            if (!target.IsAlive || target.Team != enemyTeam) continue;
            float dx = target.Position.x - pos.x;
            float dz = target.Position.z - pos.z;
            float dzAdj = dirIndep ? dz : dz * dirZ;

            if (predicate(dx, dzAdj))
            {
                results.Add(new SimAction
                {
                    Type = SimActionType.Attack,
                    UnitId = unit.Id,
                    TargetUnitId = target.Id,
                    TargetPos = target.Position,
                    APCost = cost,
                    ActorTeam = team
                });
            }
        }
    }

    // ================================================================
    //  スキル候補 (正確なスキル攻撃位置を使用)
    // ================================================================
    static void GenerateSkillActions(SimBoardState board, SimUnit unit, Team team,
        Team enemyTeam, int ap, List<SimAction> results)
    {
        if (unit.AssignedSkillId < 0) return;
        if (unit.SkillCooldown > 0) return;
        if (!SkillData.Table.TryGetValue(unit.AssignedSkillId, out var skill)) return;
        if (skill.APCost > ap) return;

        int dirZ = MovePatterns.DirZ(unit.Direction);

        // スキル攻撃位置を決定
        Vector2Int[] offsets = null;
        if (AttackPatterns.SkillFixedPositions.ContainsKey(skill.Id))
            offsets = AttackPatterns.SkillFixedPositions[skill.Id];
        else if (AttackPatterns.SkillAttackPositions.ContainsKey(unit.Kind))
            offsets = AttackPatterns.SkillAttackPositions[unit.Kind];

        if (offsets == null) return;

        // Self / SelfArea スキル
        if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea)
        {
            // 自分自身にスキル使用（バフ系）
            if (skill.GrantBuff != BuffType.None || skill.FixedHeal > 0)
            {
                results.Add(new SimAction
                {
                    Type = SimActionType.SkillUse,
                    UnitId = unit.Id,
                    TargetUnitId = unit.Id,
                    TargetPos = unit.Position,
                    APCost = skill.APCost,
                    SkillId = unit.AssignedSkillId,
                    ActorTeam = team
                });
            }
            return;
        }

        // オフセットからターゲット位置を計算（HashSet再利用）
        if (_skillPosBuffer == null) _skillPosBuffer = new HashSet<Vector3Int>();
        _skillPosBuffer.Clear();
        for (int i = 0; i < offsets.Length; i++)
        {
            int wx = unit.Position.x + offsets[i].x;
            int wz = unit.Position.z + offsets[i].y * dirZ;
            var sp = new Vector3Int(wx, 0, wz);
            if (board.MapTiles.Contains(sp))
                _skillPosBuffer.Add(sp);
        }

        // 攻撃スキル → 敵に使用（Units直接走査、Listアロケーション排除）
        if (skill.Multiplier > 0)
        {
            for (int i = 0; i < board.Units.Count; i++)
            {
                var t = board.Units[i];
                if (!t.IsAlive || t.Team != enemyTeam) continue;
                if (!_skillPosBuffer.Contains(t.Position)) continue;

                results.Add(new SimAction
                {
                    Type = SimActionType.SkillUse,
                    UnitId = unit.Id,
                    TargetUnitId = t.Id,
                    TargetPos = t.Position,
                    APCost = skill.APCost,
                    SkillId = unit.AssignedSkillId,
                    ActorTeam = team
                });
            }
        }

        // 回復スキル → 味方に使用（Units直接走査）
        if (skill.FixedHeal > 0)
        {
            for (int i = 0; i < board.Units.Count; i++)
            {
                var a = board.Units[i];
                if (!a.IsAlive || a.Team != team || a.Type != Type.Unit) continue;
                if (a.Id == unit.Id) continue;
                if (a.HP >= a.MaxHP) continue;
                if (!_skillPosBuffer.Contains(a.Position)) continue;

                results.Add(new SimAction
                {
                    Type = SimActionType.SkillUse,
                    UnitId = unit.Id,
                    TargetUnitId = a.Id,
                    TargetPos = a.Position,
                    APCost = skill.APCost,
                    SkillId = unit.AssignedSkillId,
                    ActorTeam = team
                });
            }
        }

        // バフスキル (味方支援) — Units直接走査
        if (skill.GrantBuff != BuffType.None && skill.Multiplier <= 0 && skill.FixedHeal <= 0)
        {
            if (skill.Target == SkillTarget.AllySingle)
            {
                for (int i = 0; i < board.Units.Count; i++)
                {
                    var a = board.Units[i];
                    if (!a.IsAlive || a.Team != team || a.Type != Type.Unit) continue;
                    if (a.Id == unit.Id) continue;
                    if (!_skillPosBuffer.Contains(a.Position)) continue;

                    results.Add(new SimAction
                    {
                        Type = SimActionType.SkillUse,
                        UnitId = unit.Id,
                        TargetUnitId = a.Id,
                        TargetPos = a.Position,
                        APCost = skill.APCost,
                        SkillId = unit.AssignedSkillId,
                        ActorTeam = team
                    });
                }
            }
        }
    }

    // ================================================================
    //  建築候補
    // ================================================================
    static void GenerateBuildActions(SimBoardState board, Team team, int ap,
        List<SimAction> results)
    {
        var counts = team == Team.Enemy ? board.EnemyBuildingCounts : board.PlayerBuildingCounts;

        FacilityKind[] priorities = {
            FacilityKind.Well, FacilityKind.LoggingCamp, FacilityKind.Quarry,
            FacilityKind.Field, FacilityKind.House, FacilityKind.Bakery,
            FacilityKind.Mine, FacilityKind.Barracks,
        };

        foreach (var fk in priorities)
        {
            if (!FacilityData.Table.TryGetValue(fk, out var info)) continue;
            if (info.APCost > ap) continue;

            int existing = 0;
            counts.TryGetValue(fk, out existing);
            if (existing >= 3) continue;

            var crystalPos = team == Team.Enemy ? board.EnemyCrystalPos : board.PlayerCrystalPos;

            results.Add(new SimAction
            {
                Type = SimActionType.Build,
                Facility = fk,
                TargetPos = crystalPos,
                APCost = info.APCost,
                ActorTeam = team
            });
        }
    }

    // ================================================================
    //  召喚候補
    // ================================================================
    static void GenerateSummonActions(SimBoardState board, Team team, int ap,
        List<SimAction> results)
    {
        Kind[] summonableKinds = {
            Kind.Knight, Kind.Archer, Kind.Magic, Kind.Assassin,
            Kind.Scout, Kind.Priest, Kind.Guardian, Kind.Crossbow,
        };

        var crystalPos = team == Team.Enemy ? board.EnemyCrystalPos : board.PlayerCrystalPos;

        foreach (var kind in summonableKinds)
        {
            if (!UnitStaticData.Table.TryGetValue(kind, out var data)) continue;
            if (data.CostAP > ap) continue;

            // クリスタル周囲に空きマスを探す
            Vector3Int spawnPos = FindSpawnPosition(board, crystalPos);
            if (spawnPos.x == int.MinValue) continue;

            results.Add(new SimAction
            {
                Type = SimActionType.Summon,
                SummonKind = kind,
                TargetPos = spawnPos,
                APCost = data.CostAP,
                ActorTeam = team
            });
        }
    }

    static Vector3Int FindSpawnPosition(SimBoardState board, Vector3Int center)
    {
        // クリスタル周囲の空きマスを探す
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                var pos = new Vector3Int(center.x + dx, 0, center.z + dz);
                if (board.MapTiles.Contains(pos) && !board.IsOccupied(pos))
                    return pos;
            }
        }
        return new Vector3Int(int.MinValue, 0, 0);
    }

    // ================================================================
    //  単一行動のスコアリング (探索のノード選択用: 簡易評価)
    //  上位N候補を絞り込むために使用。完全な評価はSimBoardEvaluatorで行う。
    //
    //  正値 = 行動者にとって有利
    // ================================================================
    public static float QuickScore(SimAction action, SimBoardState board)
    {
        float score = 0f;
        // 行動者のチームを使って敵クリスタル方向を判定
        bool isEnemy = action.ActorTeam == Team.Enemy;

        switch (action.Type)
        {
            case SimActionType.Attack:
            {
                var attacker = board.GetUnit(action.UnitId);
                var target = board.GetUnit(action.TargetUnitId);
                if (attacker != null && target != null)
                {
                    int dmg = SimBoardState.CalcDamage(attacker, target);
                    bool wouldKill = dmg >= target.HP;

                    score += dmg * AIConstants.QS_Attack_DmgMult;
                    if (wouldKill)
                    {
                        score += GetPieceValue(target) * 2f;
                        if (target.Kind == Kind.Crystal) score += AIConstants.QS_Kill_Crystal;
                        if (target.Kind == Kind.King) score += AIConstants.QS_Kill_King;
                    }
                    // シールド中のターゲットは低優先
                    if (target.ShieldTurns > 0)
                        score *= 0.1f;
                }
                break;
            }

            case SimActionType.SkillUse:
            {
                var unit = board.GetUnit(action.UnitId);
                var target = board.GetUnit(action.TargetUnitId);
                if (unit != null && target != null && SkillData.Table.TryGetValue(action.SkillId, out var skill))
                {
                    if (skill.Multiplier > 0)
                    {
                        int dmg = SimBoardState.CalcSkillDamage(unit, target, skill);
                        score += dmg * AIConstants.QS_Skill_DmgMult;
                        if (dmg >= target.HP)
                        {
                            score += GetPieceValue(target) * 1.5f;
                            if (target.Kind == Kind.Crystal) score += AIConstants.QS_Kill_Crystal;
                        }
                        // デバフ付与ボーナス
                        if (skill.InflictDebuff != StatusEffectType.None)
                            score += AIConstants.QS_Skill_Debuff;
                    }
                    if (skill.FixedHeal > 0)
                    {
                        float healValue = Mathf.Min(skill.FixedHeal, target.MaxHP - target.HP);
                        float hpRatio = target.MaxHP > 0 ? (float)target.HP / target.MaxHP : 1f;
                        score += healValue * (1f - hpRatio);
                    }
                    if (skill.GrantBuff != BuffType.None)
                        score += 8f;
                }
                break;
            }

            case SimActionType.Move:
            {
                var unit = board.GetUnit(action.UnitId);
                if (unit != null)
                {
                    // 敵クリスタルへの接近
                    Vector3Int targetCrystal = isEnemy
                        ? board.PlayerCrystalPos : board.EnemyCrystalPos;
                    float distBefore = SimUtil.Distance(unit.Position, targetCrystal);
                    float distAfter = SimUtil.Distance(action.TargetPos, targetCrystal);
                    score += (distBefore - distAfter) * AIConstants.QS_Move_DistMult;

                    // 敵ユニットへの接近（攻撃射程に入る移動を高評価）
                    Team enemyTeam = isEnemy ? Team.Player : Team.Enemy;
                    var enemies = board.GetAliveUnits(enemyTeam);
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        float da = SimUtil.Distance(action.TargetPos, enemies[i].Position);
                        if (da <= 1.5f) score += AIConstants.QS_Move_AttackRange;
                        else if (da <= 3f) score += 2f;
                    }

                    // 自陣クリスタル防衛
                    Vector3Int ownCrystal = isEnemy
                        ? board.EnemyCrystalPos : board.PlayerCrystalPos;
                    float ownCrystalDist = SimUtil.Distance(action.TargetPos, ownCrystal);
                    if (ownCrystalDist < 4f) score += 3f;

                    // 視界拡大ボーナス（新たに見えるセル数）
                    int newVision = board.EstimateNewVisionCellsAt(unit, action.TargetPos, action.ActorTeam);
                    if (newVision > 0)
                    {
                        float visionBonus = Mathf.Min(newVision * 1.5f, 12f);
                        // Scoutは偵察に特化
                        if (unit.Kind == Kind.Scout) visionBonus *= 2f;
                        score += visionBonus;
                    }
                }
                break;
            }

            case SimActionType.Build:
                score += AIConstants.QS_Build_Base;
                // 早期の経済施設は特に有益
                if (board.TurnCount <= 15) score += 4f;
                break;

            case SimActionType.Summon:
                score += AIConstants.QS_Summon_Base;
                break;
        }

        // AP効率: コストが高い行動は若干減点
        score -= action.APCost * AIConstants.QS_AP_Penalty;

        return score;
    }

    // ================================================================
    //  駒の価値 (評価関数共有)
    // ================================================================
    public static float GetPieceValue(SimUnit unit)
    {
        if (unit == null) return 0f;
        switch (unit.Kind)
        {
            case Kind.SubCrystal: return 40f;
            case Kind.WoodWall:   return 8f;
            case Kind.StoneWall:  return 12f;
            default: return AIConstants.GetPieceValue(unit.Kind);
        }
    }

    // ================================================================
    //  攻撃/移動射程の推定 (評価関数用)
    // ================================================================
    public static float EstimateAttackRange(Kind kind)
    {
        return AIConstants.GetAttackRange(kind);
    }

    public static float EstimateMoveRange(Kind kind)
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

    // ================================================================
    //  あるユニットの有効な移動先の数を数える（モビリティ計算用）
    // ================================================================
    public static int CountMoves(SimBoardState board, SimUnit unit)
    {
        if (!MovePatterns.Map.TryGetValue(unit.Kind, out var predicate)) return 0;
        if (unit.IsStunned || unit.IsMovementBlocked) return 0;

        int count = 0;
        var pos = unit.Position;
        int dirZ = MovePatterns.DirZ(unit.Direction);
        bool dirIndep = MovePatterns.DirectionIndependent.Contains(unit.Kind);

        foreach (var tile in board.MapTiles)
        {
            float dx = tile.x - pos.x;
            float dz = tile.z - pos.z;
            float dzAdj = dirIndep ? dz : dz * dirZ;

            if (predicate(dx, dzAdj) && !board.IsOccupied(tile))
                count++;
        }
        return count;
    }

    // ================================================================
    //  あるユニットが攻撃可能なターゲット数を数える
    // ================================================================
    public static int CountAttackTargets(SimBoardState board, SimUnit unit, Team enemyTeam)
    {
        if (!AttackPatterns.NormalMap.TryGetValue(unit.Kind, out var predicate)) return 0;
        if (unit.IsStunned) return 0;

        int count = 0;
        var pos = unit.Position;
        int dirZ = MovePatterns.DirZ(unit.Direction);
        bool dirIndep = AttackPatterns.DirectionIndependent.Contains(unit.Kind);

        for (int i = 0; i < board.Units.Count; i++)
        {
            var t = board.Units[i];
            if (!t.IsAlive || t.Team != enemyTeam) continue;
            float dx = t.Position.x - pos.x;
            float dz = t.Position.z - pos.z;
            float dzAdj = dirIndep ? dz : dz * dirZ;
            if (predicate(dx, dzAdj)) count++;
        }
        return count;
    }
}
