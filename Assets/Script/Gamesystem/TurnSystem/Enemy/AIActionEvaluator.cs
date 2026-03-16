using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AIAction — 候補行動データ
// =====================================================================
public class AIAction
{
    public AIActionType ActionType;
    public Status Unit;              // 行動する駒（移動/攻撃時）
    public Vector3 TargetPos;        // 移動先 or 配置位置
    public Status TargetUnit;        // 攻撃対象（あれば）
    public int APCost;               // 消費AP
    public float Score;              // 最終評価点
    public FacilityKind Facility;    // 建築の種類
    public Kind SummonKind;          // 召喚するユニット種
    public SkillData Skill;          // 使用スキル
    public List<Status> AreaTargets; // 範囲スキルの対象リスト

    public override string ToString()
        => $"{ActionType}({Unit?.kind}/{Facility}/{SummonKind}) → {TargetPos} score={Score:F1}";
}

// =====================================================================
//  AIActionEvaluator — 行動評価計算
//  最終評価 = 基本評価 + 大きい性格補正 + 細かい性格補正 + 局面補正 + 学習補正
// =====================================================================
public static class AIActionEvaluator
{
    // ---- 全候補行動を生成・評価してスコア順に返す ----
    public static List<AIAction> EvaluateAll(
        AIPersonality personality,
        AIBoardState board,
        AILearning learning,
        TurnStrategy strategy = TurnStrategy.Balanced)
    {
        var actions = new List<AIAction>();

        // 全敵駒について候補行動を生成
        foreach (var unit in board.AliveEnemyUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (unit.type != Type.Unit) continue;
            if (StatusEffectSystem.IsStunned(unit)) continue;

            GenerateMoveCandidates(unit, board, actions);
            GenerateAttackCandidates(unit, board, actions);
            GenerateSkillCandidates(unit, board, actions);
            GenerateRetreatCandidates(unit, board, actions);
            GenerateSupportCandidates(unit, board, actions);
            GenerateSurroundCandidates(unit, board, actions);
            GenerateDefenseReposCandidates(unit, board, actions);
            GenerateWaitCandidate(unit, board, actions);
        }

        // 建築候補を生成
        GenerateBuildCandidates(board, actions);

        // 召喚候補を生成
        GenerateSummonCandidates(board, actions);

        // サブクリスタル展開候補を生成
        GenerateSubCrystalCandidates(board, actions);

        // 各候補にスコア付け
        foreach (var action in actions)
        {
            action.Score = CalcScore(action, personality, board, learning);
        }

        // ターン方針ボーナス
        ApplyStrategyBonus(actions, strategy, board);

        // 次ターン反撃圏ペナルティ
        ApplyCounterDangerPenalty(actions, board, personality);

        // 撤退→回復チェーンボーナス（強化版: ヒーラー/壁/味方カバーまで見る）
        ApplyRetreatRegroupBonus(actions, personality, board);

        // BOSS前線参加条件チェック
        ApplyBossFrontlineConditions(actions, personality, board);

        // 経済余裕による段階的召喚ボーナス
        ApplyGradualArmyExpansion(actions, board);

        // スコア降順
        actions.Sort((a, b) => b.Score.CompareTo(a.Score));
        return actions;
    }

    // ================================================================
    //  候補生成: 移動
    // ================================================================
    static void GenerateMoveCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;

        var moves = board.GetValidMoves(unit);
        foreach (var dest in moves)
        {
            int cost = board.CalcMoveCost(unit, dest);
            if (cost > board.EnemyAP) continue;

            results.Add(new AIAction
            {
                ActionType = AIActionType.Move,
                Unit = unit,
                TargetPos = dest,
                APCost = cost
            });
        }
    }

    // ================================================================
    //  候補生成: 攻撃
    // ================================================================
    static void GenerateAttackCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        var targets = board.GetAttackTargets(unit);
        foreach (var target in targets)
        {
            int cost = board.CalcAttackCost(unit);
            if (cost > board.EnemyAP) continue;

            results.Add(new AIAction
            {
                ActionType = AIActionType.Attack,
                Unit = unit,
                TargetPos = target.transform.position,
                TargetUnit = target,
                APCost = cost
            });
        }
    }

    // ================================================================
    //  候補生成: スキル使用
    // ================================================================
    static void GenerateSkillCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (unit.AssignedSkillId < 0) return;
        if (!SkillData.Table.TryGetValue(unit.AssignedSkillId, out var skill)) return;
        if (skill.APCost > board.EnemyAP) return;
        if (StatusEffectSystem.HasDebuff(unit, StatusEffectType.Seal)) return;
        if (unit.SkillCooldown > 0) return; // クールダウン中は使用不可

        var targets = board.GetSkillTargets(unit, skill);
        if (targets.Count == 0) return;

        switch (skill.Target)
        {
            case SkillTarget.Self:
                // 自己バフ・自己回復
                results.Add(new AIAction
                {
                    ActionType = AIActionType.SkillUse,
                    Unit = unit,
                    TargetPos = unit.transform.position,
                    TargetUnit = unit,
                    APCost = skill.APCost,
                    Skill = skill
                });
                break;

            case SkillTarget.SelfArea:
                // 自分中心範囲（味方バフ/回復 or 敵攻撃）
                {
                    var allies = board.GetAlliesInSkillArea(unit, skill, unit.transform.position);
                    var enemies = board.GetEnemiesInSkillArea(unit, skill, unit.transform.position);
                    if (allies.Count > 0 || enemies.Count > 0)
                    {
                        results.Add(new AIAction
                        {
                            ActionType = AIActionType.SkillUse,
                            Unit = unit,
                            TargetPos = unit.transform.position,
                            TargetUnit = unit,
                            APCost = skill.APCost,
                            Skill = skill,
                            AreaTargets = skill.Multiplier > 0 ? enemies : allies
                        });
                    }
                }
                break;

            case SkillTarget.AllySingle:
                // 味方回復・バフ（HP低い順に最大1候補）
                {
                    Status bestAlly = null;
                    float bestScore = float.MinValue;
                    foreach (var ally in targets)
                    {
                        float s = 0f;
                        if (skill.FixedHeal > 0 && ally.MaxHP > 0)
                            s += (1f - (float)ally.HP / ally.MaxHP) * 30f;
                        if (skill.GrantBuff != BuffType.None)
                            s += 15f;
                        if (s > bestScore) { bestScore = s; bestAlly = ally; }
                    }
                    if (bestAlly != null)
                    {
                        results.Add(new AIAction
                        {
                            ActionType = AIActionType.SkillUse,
                            Unit = unit,
                            TargetPos = bestAlly.transform.position,
                            TargetUnit = bestAlly,
                            APCost = skill.APCost,
                            Skill = skill
                        });
                    }
                }
                break;

            case SkillTarget.EnemySingle:
            case SkillTarget.EnemyOrBuilding:
            case SkillTarget.LowHPEnemy:
            case SkillTarget.FlyingEnemy:
                // 敵単体スキル（最大2候補）
                {
                    int count = 0;
                    foreach (var t in targets)
                    {
                        if (count >= 2) break;
                        results.Add(new AIAction
                        {
                            ActionType = AIActionType.SkillUse,
                            Unit = unit,
                            TargetPos = t.transform.position,
                            TargetUnit = t,
                            APCost = skill.APCost,
                            Skill = skill
                        });
                        count++;
                    }
                }
                break;

            case SkillTarget.DesignatedTile:
            case SkillTarget.AdjacentCenter:
            case SkillTarget.DirectionLine:
            case SkillTarget.DesignatedRow:
                // 範囲スキル（敵が含まれる位置を候補）
                {
                    foreach (var t in targets)
                    {
                        var enemies = board.GetEnemiesInSkillArea(unit, skill, t.transform.position);
                        results.Add(new AIAction
                        {
                            ActionType = AIActionType.SkillUse,
                            Unit = unit,
                            TargetPos = t.transform.position,
                            TargetUnit = t,
                            APCost = skill.APCost,
                            Skill = skill,
                            AreaTargets = enemies
                        });
                    }
                }
                break;
        }
    }

    // ================================================================
    //  候補生成: 撤退（自陣方向への移動）
    // ================================================================
    static void GenerateRetreatCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;

        // HPが30%以下、または近くに敵が多い場合のみ撤退候補を生成
        float hpRatio = unit.MaxHP > 0 ? (float)unit.HP / unit.MaxHP : 1f;
        float nearestEnemy = float.MaxValue;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(unit.transform.position, pu.transform.position);
            if (d < nearestEnemy) nearestEnemy = d;
        }

        if (hpRatio > 0.4f && nearestEnemy > 3f) return;

        var moves = board.GetValidMoves(unit);
        // 自陣クリスタルに一番近い移動先を撤退先とする
        Vector3 bestDest = unit.transform.position;
        float bestDist = Vector3.Distance(unit.transform.position, board.EnemyCrystalPos);
        int bestCost = 0;
        foreach (var dest in moves)
        {
            float dist = Vector3.Distance(dest, board.EnemyCrystalPos);
            if (dist < bestDist)
            {
                int cost = board.CalcMoveCost(unit, dest);
                if (cost <= board.EnemyAP)
                {
                    bestDist = dist;
                    bestDest = dest;
                    bestCost = cost;
                }
            }
        }

        if (bestDest != unit.transform.position)
        {
            results.Add(new AIAction
            {
                ActionType = AIActionType.Retreat,
                Unit = unit,
                TargetPos = bestDest,
                APCost = bestCost
            });
        }
    }

    // ================================================================
    //  候補生成: 援護配置（味方近くへの移動）
    // ================================================================
    static void GenerateSupportCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;

        // 弱っている味方が近くにいるか探す
        Status weakAlly = null;
        float weakAllyDist = float.MaxValue;
        foreach (var ally in board.AliveEnemyUnits)
        {
            if (ally == null || !ally.gameObject.activeInHierarchy || ally == unit) continue;
            if (ally.MaxHP <= 0) continue;
            float allyHpRatio = (float)ally.HP / ally.MaxHP;
            if (allyHpRatio > 0.5f) continue;
            float d = Vector3.Distance(unit.transform.position, ally.transform.position);
            if (d < weakAllyDist && d > 1.5f && d < 8f)
            {
                weakAllyDist = d;
                weakAlly = ally;
            }
        }

        if (weakAlly == null) return;

        var moves = board.GetValidMoves(unit);
        Vector3 bestDest = unit.transform.position;
        float bestDist = weakAllyDist;
        int bestCost = 0;
        foreach (var dest in moves)
        {
            float dist = Vector3.Distance(dest, weakAlly.transform.position);
            if (dist < bestDist && dist >= 1f)
            {
                int cost = board.CalcMoveCost(unit, dest);
                if (cost <= board.EnemyAP)
                {
                    bestDist = dist;
                    bestDest = dest;
                    bestCost = cost;
                }
            }
        }

        if (bestDest != unit.transform.position)
        {
            results.Add(new AIAction
            {
                ActionType = AIActionType.Support,
                Unit = unit,
                TargetPos = bestDest,
                TargetUnit = weakAlly,
                APCost = bestCost
            });
        }
    }

    // ================================================================
    //  候補生成: 包囲移動（敵の背後・側面へ）
    // ================================================================
    static void GenerateSurroundCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;
        if (board.AlivePlayerUnits.Count == 0) return;

        // 最も近い敵を包囲ターゲットにする
        Status nearestPlayer = null;
        float nearestDist = float.MaxValue;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(unit.transform.position, pu.transform.position);
            if (d < nearestDist) { nearestDist = d; nearestPlayer = pu; }
        }

        if (nearestPlayer == null || nearestDist > 6f) return;

        // 敵の側面・背後のセルを計算
        var enemyPos = nearestPlayer.transform.position;
        Vector3[] flankOffsets = {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
            new Vector3(0, 0, -1), // 背後（北向き敵）
        };

        var moves = board.GetValidMoves(unit);
        foreach (var offset in flankOffsets)
        {
            Vector3 flankPos = enemyPos + offset;
            // 移動可能先からフランク位置に最も近いものを探す
            foreach (var dest in moves)
            {
                float distToFlank = Vector3.Distance(dest, flankPos);
                if (distToFlank < 1.5f)
                {
                    int cost = board.CalcMoveCost(unit, dest);
                    if (cost <= board.EnemyAP)
                    {
                        results.Add(new AIAction
                        {
                            ActionType = AIActionType.Surround,
                            Unit = unit,
                            TargetPos = dest,
                            TargetUnit = nearestPlayer,
                            APCost = cost
                        });
                        return; // 1つだけ追加
                    }
                }
            }
        }
    }

    // ================================================================
    //  候補生成: 待機
    // ================================================================
    static void GenerateWaitCandidate(Status unit, AIBoardState board, List<AIAction> results)
    {
        results.Add(new AIAction
        {
            ActionType = AIActionType.Wait,
            Unit = unit,
            TargetPos = unit.transform.position,
            APCost = 0
        });
    }

    // ================================================================
    //  候補生成: 建築（戦略的フィルタリング付き）
    // ================================================================
    static void GenerateBuildCandidates(AIBoardState board, List<AIAction> results)
    {
        if (board.BuildablePositions.Count == 0 || board.AffordableBuildings.Count == 0) return;

        foreach (var facility in board.AffordableBuildings)
        {
            // サブクリスタルは GenerateSubCrystalCandidates で扱うので除外
            if (FacilityData.IsSubCrystal(facility)) continue;

            if (!FacilityData.Table.TryGetValue(facility, out var info)) continue;

            // 同種建物の上限チェック（無駄な重複を防ぐ）
            int existing = board.GetBuildingCount(facility);
            int maxAllowed = GetMaxBuildingCount(facility);
            if (existing >= maxAllowed) continue;

            // 加工施設は上流の生産施設がないと無意味 → 候補から除外
            if (!board.HasUpstreamProducer(facility)) continue;

            // 建物タイプに応じて最適な配置位置を選択
            var positions = SelectBuildPositions(facility, board);

            foreach (var pos in positions)
            {
                results.Add(new AIAction
                {
                    ActionType = AIActionType.Build,
                    Facility = facility,
                    TargetPos = new Vector3(pos.x, pos.y, pos.z),
                    APCost = info.APCost
                });
            }
        }
    }

    // 建物種別ごとの最大建設数
    static int GetMaxBuildingCount(FacilityKind facility)
    {
        switch (facility)
        {
            case FacilityKind.Well:         return 3;
            case FacilityKind.LoggingCamp:  return 3;
            case FacilityKind.Quarry:       return 3;
            case FacilityKind.Field:        return 3;
            case FacilityKind.Mine:         return 2;
            case FacilityKind.LumberMill:   return 2;
            case FacilityKind.StoneWorks:   return 2;
            case FacilityKind.Smelter:      return 2;
            case FacilityKind.Bakery:       return 2;
            case FacilityKind.House:        return 4;
            case FacilityKind.Warehouse:    return 2;
            case FacilityKind.Barracks:     return 1;
            case FacilityKind.Mortar:       return 3;
            case FacilityKind.Cannon:       return 3;
            case FacilityKind.WoodWall:     return 8;
            case FacilityKind.StoneWall:    return 8;
            case FacilityKind.RestraintTrap:return 4;
            case FacilityKind.SpikeTrap:    return 4;
            case FacilityKind.HeroSword:    return 1;
            default:                        return 5;
        }
    }

    // ユニット種別ごとの最大召喚数（同種の過剰召喚を防止）
    static int GetMaxUnitCount(Kind kind)
    {
        switch (kind)
        {
            case Kind.Scout:    return 2;  // 偵察は2体で十分
            case Kind.Priest:   return 2;  // ヒーラーも2体で十分
            case Kind.Guardian: return 2;
            case Kind.Knight:   return 4;
            case Kind.Archer:   return 3;
            case Kind.Magic:    return 3;
            case Kind.Assassin: return 2;
            case Kind.Crossbow: return 2;
            default:            return 3;
        }
    }

    // 建物タイプに応じた配置位置の選択
    static IEnumerable<Vector3Int> SelectBuildPositions(FacilityKind facility, AIBoardState board)
    {
        // 防衛建築・攻撃建築 → 前線寄り（プレイヤークリスタル方向）
        if (FacilityData.IsWall(facility) || FacilityData.IsOffensive(facility))
        {
            // 自陣クリスタルとプレイヤークリスタルの中間付近
            Vector3 frontline = Vector3.Lerp(board.EnemyCrystalPos, board.PlayerCrystalPos, 0.35f);
            return board.BuildablePositions
                .OrderBy(p => Vector3.Distance(new Vector3(p.x, 0, p.z), frontline))
                .Take(3);
        }

        // 経済・生産建築 → 自陣クリスタル付近（安全地帯）
        return board.BuildablePositions
            .OrderBy(p => Vector3.Distance(new Vector3(p.x, 0, p.z), board.EnemyCrystalPos))
            .Take(3);
    }

    // ================================================================
    //  候補生成: 召喚
    // ================================================================
    static void GenerateSummonCandidates(AIBoardState board, List<AIAction> results)
    {
        if (board.SummonablePositions.Count == 0)
        {
            Debug.Log("[AI Summon] 召喚可能位置なし — サブクリスタルを配置してください");
            return;
        }
        if (board.AffordableUnits.Count == 0)
        {
            var res = board.EnemyResources;
            Debug.Log($"[AI Summon] 資源不足で召喚不可 — Bread:{res.Bread} Iron:{res.Iron} Wood:{res.Wood} Stone:{res.Stone} Water:{res.Water} Citizen:{res.Citizen}");
            return;
        }

        foreach (var kind in board.AffordableUnits)
        {
            if (!UnitStaticData.Table.TryGetValue(kind, out var info)) continue;

            // 同種ユニットの上限チェック（過剰召喚防止）
            int existingOfKind = board.AliveEnemyUnits.Count(u => u.kind == kind);
            int maxOfKind = GetMaxUnitCount(kind);
            if (existingOfKind >= maxOfKind) continue;

            // 前線に近い位置を優先（最大2つ）
            var positions = board.SummonablePositions
                .OrderBy(p => Vector3.Distance(new Vector3(p.x, 0, p.z), board.PlayerCrystalPos))
                .Take(2);

            foreach (var pos in positions)
            {
                results.Add(new AIAction
                {
                    ActionType = AIActionType.Summon,
                    SummonKind = kind,
                    TargetPos = new Vector3(pos.x, pos.y, pos.z),
                    APCost = info.CostAP
                });
            }
        }
    }

    // ================================================================
    //  候補生成: 防衛再配置（自陣クリスタル付近への移動）
    // ================================================================
    static void GenerateDefenseReposCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;

        // クリスタルが脅かされている場合のみ
        float unitToCrystal = Vector3.Distance(unit.transform.position, board.EnemyCrystalPos);
        if (unitToCrystal < 3f) return; // 既にクリスタル付近にいる

        // 敵がクリスタルの近くにいるか
        bool crystalThreatened = false;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(pu.transform.position, board.EnemyCrystalPos);
            if (d < 5f) { crystalThreatened = true; break; }
        }

        // クリスタルHP50%以下もトリガー
        if (!crystalThreatened && board.EnemyCrystalHP >= board.EnemyCrystalMaxHP * 0.5f) return;

        var moves = board.GetValidMoves(unit);
        Vector3 bestDest = unit.transform.position;
        float bestDist = unitToCrystal;
        int bestCost = 0;
        foreach (var dest in moves)
        {
            float dist = Vector3.Distance(dest, board.EnemyCrystalPos);
            if (dist < bestDist)
            {
                int cost = board.CalcMoveCost(unit, dest);
                if (cost <= board.EnemyAP)
                {
                    bestDist = dist;
                    bestDest = dest;
                    bestCost = cost;
                }
            }
        }

        if (bestDest != unit.transform.position)
        {
            results.Add(new AIAction
            {
                ActionType = AIActionType.DefenseRepos,
                Unit = unit,
                TargetPos = bestDest,
                APCost = bestCost
            });
        }
    }

    // ================================================================
    //  候補生成: サブクリスタル展開
    // ================================================================
    static void GenerateSubCrystalCandidates(AIBoardState board, List<AIAction> results)
    {
        if (board.SubCrystalPlaceable.Count == 0) return;
        if (board.EnemySubCrystals <= 0) return;

        // SubCrystalのAPコストを取得（テーブルに無い場合はデフォルト2）
        int apCost = 2;
        if (FacilityData.Table.TryGetValue(FacilityKind.SubCrystal, out var info) && info.APCost > 0)
            apCost = info.APCost;
        if (apCost > board.EnemyAP) return;

        // 最大2位置
        int count = 0;
        foreach (var pos in board.SubCrystalPlaceable)
        {
            if (count >= 2) break;

            results.Add(new AIAction
            {
                ActionType = AIActionType.SubCrystal,
                TargetPos = new Vector3(pos.x, pos.y, pos.z),
                APCost = apCost,
                Facility = FacilityKind.SubCrystal
            });
            count++;
        }
    }

    // ================================================================
    //  ターン方針ボーナス
    //  AICommander が選んだ方針に合う行動にボーナスを与える
    // ================================================================
    static void ApplyStrategyBonus(List<AIAction> actions, TurnStrategy strategy, AIBoardState board)
    {
        foreach (var action in actions)
        {
            float bonus = 0f;
            switch (strategy)
            {
                case TurnStrategy.Assault:
                    if (action.ActionType == AIActionType.Attack) bonus += 18f;
                    if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0) bonus += 15f;
                    if (action.ActionType == AIActionType.Surround) bonus += 12f;
                    if (action.ActionType == AIActionType.Move)
                        bonus += GetApproachToEnemy(action, board) * 4f;
                    if (action.ActionType == AIActionType.Retreat) bonus -= 10f;
                    if (action.ActionType == AIActionType.Build) bonus -= 5f;
                    break;

                case TurnStrategy.CrystalDefense:
                    if (action.ActionType == AIActionType.DefenseRepos) bonus += 22f;
                    if (action.ActionType == AIActionType.Move && action.Unit != null)
                    {
                        // Scoutは防衛時でも偵察に出す（早期警戒）
                        if (action.Unit.kind == Kind.Scout)
                        {
                            int scoutNewCells = board.EstimateNewVisionCells(action.TargetPos);
                            if (scoutNewCells > 3) bonus += 15f;
                        }
                        else
                        {
                            float dist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                            if (dist < 4f) bonus += 18f;
                            else if (dist < 6f) bonus += 8f;
                        }
                    }
                    if (action.ActionType == AIActionType.Build && FacilityData.IsWall(action.Facility)) bonus += 15f;
                    if (action.ActionType == AIActionType.Attack)
                    {
                        // クリスタル付近の敵への攻撃は加点
                        if (action.TargetUnit != null)
                        {
                            float tDist = Vector3.Distance(action.TargetUnit.transform.position, board.EnemyCrystalPos);
                            if (tDist < 5f) bonus += 15f;
                        }
                    }
                    // クリスタルから離れる動きを抑制（Scoutは例外）
                    if (action.ActionType == AIActionType.Surround || action.ActionType == AIActionType.Move)
                    {
                        if (action.Unit != null && action.Unit.kind != Kind.Scout)
                        {
                            float destDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                            if (destDist > 8f) bonus -= 12f;
                        }
                    }
                    break;

                case TurnStrategy.RetreatRegroup:
                    if (action.ActionType == AIActionType.Retreat) bonus += 20f;
                    if (action.ActionType == AIActionType.Support) bonus += 15f;
                    if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.FixedHeal > 0) bonus += 18f;
                    if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.GrantBuff == BuffType.Defensive) bonus += 10f;
                    if (action.ActionType == AIActionType.Attack) bonus -= 8f;
                    if (action.ActionType == AIActionType.Surround) bonus -= 12f;
                    break;

                case TurnStrategy.EconomyBuild:
                {
                    // 基礎5施設（Well, LoggingCamp, Quarry, Field, House）の設置状況を確認
                    bool hasWell = board.GetBuildingCount(FacilityKind.Well) > 0;
                    bool hasLogging = board.GetBuildingCount(FacilityKind.LoggingCamp) > 0;
                    bool hasQuarry = board.GetBuildingCount(FacilityKind.Quarry) > 0;
                    bool hasField = board.GetBuildingCount(FacilityKind.Field) > 0;
                    bool hasHouse = board.GetBuildingCount(FacilityKind.House) > 0;
                    int coreCount = (hasWell ? 1 : 0) + (hasLogging ? 1 : 0)
                                  + (hasQuarry ? 1 : 0) + (hasField ? 1 : 0) + (hasHouse ? 1 : 0);

                    if (action.ActionType == AIActionType.Build)
                    {
                        bonus += 30f; // 建築にAPを集中

                        // 基礎5施設が揃っていない場合、その建築を最優先
                        if (coreCount < 5)
                        {
                            bool isCoreBuilding = (action.Facility == FacilityKind.Well && !hasWell)
                                || (action.Facility == FacilityKind.LoggingCamp && !hasLogging)
                                || (action.Facility == FacilityKind.Quarry && !hasQuarry)
                                || (action.Facility == FacilityKind.Field && !hasField)
                                || (action.Facility == FacilityKind.House && !hasHouse);
                            if (isCoreBuilding)
                                bonus += 40f; // 欠けた基礎施設は最優先
                            else
                                bonus -= 15f; // 基礎が揃うまで他の建築は後回し
                        }
                    }
                    if (action.ActionType == AIActionType.SubCrystal) bonus += 15f;
                    // 基礎5施設が揃ってから召喚を検討
                    if (action.ActionType == AIActionType.Summon)
                    {
                        if (coreCount >= 5) bonus += 10f;
                        else bonus -= 20f; // 基礎未整備 → 召喚を強く抑制
                    }
                    // 経済フェーズ中は移動・攻撃を強く抑制してAPを温存
                    if (action.ActionType == AIActionType.Attack) bonus -= 10f;
                    if (action.ActionType == AIActionType.Move) bonus -= 8f;
                    if (action.ActionType == AIActionType.Surround) bonus -= 8f;
                    if (action.ActionType == AIActionType.Retreat) bonus -= 5f;
                    break;
                }

                case TurnStrategy.Balanced:
                {
                    // 攻めと内政のバランス — 基礎施設の充実度に応じて配分を変える
                    bool balHasWell = board.GetBuildingCount(FacilityKind.Well) > 0;
                    bool balHasLogging = board.GetBuildingCount(FacilityKind.LoggingCamp) > 0;
                    bool balHasQuarry = board.GetBuildingCount(FacilityKind.Quarry) > 0;
                    bool balHasField = board.GetBuildingCount(FacilityKind.Field) > 0;
                    bool balHasHouse = board.GetBuildingCount(FacilityKind.House) > 0;
                    int balCoreCount = (balHasWell ? 1 : 0) + (balHasLogging ? 1 : 0)
                                     + (balHasQuarry ? 1 : 0) + (balHasField ? 1 : 0)
                                     + (balHasHouse ? 1 : 0);
                    bool econEstablished = balCoreCount >= 5;

                    if (action.ActionType == AIActionType.Summon)
                        bonus += econEstablished ? 20f : -10f;
                    if (action.ActionType == AIActionType.Build)
                        bonus += econEstablished ? 4f : 20f; // 基礎未整備なら建築大幅優先
                    if (action.ActionType == AIActionType.Attack) bonus += 8f;
                    if (action.ActionType == AIActionType.SkillUse) bonus += 5f;
                    if (action.ActionType == AIActionType.Move)
                        bonus += GetApproachToEnemy(action, board) * 3f;
                    break;
                }
            }
            action.Score += bonus;
        }
    }

    // ================================================================
    //  次ターン反撃圏ペナルティ
    //  行動後の位置でプレイヤーに狙われやすいかを評価し減点
    //  中核ユニット・ヒーラー・BOSS護衛・召喚直後は死亡リスクを重く見る
    // ================================================================
    static void ApplyCounterDangerPenalty(List<AIAction> actions, AIBoardState board, AIPersonality personality)
    {
        foreach (var action in actions)
        {
            // 移動系・攻撃後にその場に残る行動が対象
            if (action.Unit == null) continue;
            if (action.ActionType != AIActionType.Move
                && action.ActionType != AIActionType.Surround
                && action.ActionType != AIActionType.Support
                && action.ActionType != AIActionType.Attack
                && action.ActionType != AIActionType.SkillUse) continue;

            // 行動後の位置を推定
            Vector3 posAfter;
            if (action.ActionType == AIActionType.Move
                || action.ActionType == AIActionType.Surround
                || action.ActionType == AIActionType.Support)
            {
                posAfter = action.TargetPos;
            }
            else
            {
                posAfter = action.Unit.transform.position; // 攻撃/スキルは移動しない
            }

            int counterDmg = board.EstimateCounterDamageAt(posAfter, action.Unit);
            if (counterDmg <= 0) continue;

            float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;

            // 反撃ダメで死ぬ場合は大きなペナルティ
            bool wouldDie = counterDmg >= action.Unit.HP;

            // 重要度重み: BOSS・ヒーラー・Priest・King は死亡リスクを重く見る
            float importanceMult = 1f;
            if (action.Unit.IsBoss) importanceMult = 2.0f;
            else if (action.Unit.kind == Kind.King) importanceMult = 2.5f;
            else if (action.Unit.kind == Kind.Priest) importanceMult = 1.8f;
            // BOSS近接護衛（BOSSの近くにいるGuardian/Knight）
            else if (personality.HasBoss && (action.Unit.kind == Kind.Guardian || action.Unit.kind == Kind.Knight))
            {
                float bossDist = Vector3.Distance(posAfter, personality.BossUnit.transform.position);
                if (bossDist < 3f) importanceMult = 1.5f;
            }

            // 孤立度: 周りに味方がいないと危険度UP
            int alliesNear = board.CountAlliesNear(posAfter, action.Unit, 3f);
            float isolationMult = alliesNear == 0 ? 1.5f : alliesNear == 1 ? 1.2f : 1f;

            float penalty;
            if (wouldDie)
            {
                // 確殺されるなら大ペナルティ（ただし攻撃で相手を確殺する場合は軽減）
                penalty = 35f * importanceMult * isolationMult;
                if (action.ActionType == AIActionType.Attack && action.TargetUnit != null)
                {
                    int myDmg = EstimateDamage(action.Unit, action.TargetUnit);
                    if (myDmg >= action.TargetUnit.HP)
                        penalty *= 0.3f; // 相打ちなら許容
                }
            }
            else
            {
                // 死なないが痛い
                float dmgRatio = (float)counterDmg / Mathf.Max(1, action.Unit.HP);
                penalty = dmgRatio * 20f * importanceMult * isolationMult;
                // HP低い駒がさらにダメージを受ける場合は追加
                if (hpRatio < 0.4f) penalty += 10f * importanceMult;
            }

            action.Score -= penalty;
        }
    }

    // ================================================================
    //  撤退→再編チェーンボーナス（強化版）
    //  撤退先で: ヒーラー圏内 / 壁の後ろ / 味方がカバー / 次ターン反撃位置
    // ================================================================
    static void ApplyRetreatRegroupBonus(List<AIAction> actions, AIPersonality p, AIBoardState board)
    {
        float chainMultiplier = 1f;
        if (p.ShouldApplyMajorBonus && p.Major == MajorPersonality.Intellect)
            chainMultiplier = 1.5f;

        foreach (var action in actions)
        {
            if (action.ActionType != AIActionType.Retreat && action.ActionType != AIActionType.DefenseRepos)
                continue;
            if (action.Unit == null) continue;

            float bonus = 0f;

            // 1. ヒーラー範囲内に下がれる
            if (board.HasHealerInRange(action.TargetPos, 4f))
                bonus += 12f;

            // 2. 壁/防衛建築の後ろに下がれる
            if (board.HasDefensiveStructureNear(action.TargetPos, 3f))
                bonus += 8f;

            // 3. 味方がカバーできる位置（味方が2体以上近くにいる）
            int alliesNear = board.CountAlliesNear(action.TargetPos, action.Unit, 3f);
            if (alliesNear >= 2)
                bonus += 10f;
            else if (alliesNear >= 1)
                bonus += 5f;

            // 4. 撤退先から次ターン反撃可能（近くに敵がいて反撃圏に入る）
            float nearestPlayerDist = GetNearestPlayerDist(action.TargetPos, board);
            if (nearestPlayerDist >= 2f && nearestPlayerDist <= 4f)
                bonus += 6f; // 適度な距離=次ターン攻撃可能

            // 5. 撤退先で反撃を受けにくい
            int counterDmg = board.EstimateCounterDamageAt(action.TargetPos, action.Unit);
            if (counterDmg == 0)
                bonus += 8f;
            else if (counterDmg < action.Unit.HP * 0.2f)
                bonus += 4f;

            action.Score += bonus * chainMultiplier;
        }
    }

    // ================================================================
    //  BOSS前線参加条件
    //  ただ前に出るだけでなく、価値がある時だけ前進させる
    // ================================================================
    static void ApplyBossFrontlineConditions(List<AIAction> actions, AIPersonality p, AIBoardState board)
    {
        if (!p.HasBoss) return;
        var boss = p.BossUnit;

        // 敵が見えない場合は前進条件を緩和（展開期に引き籠り防止）
        bool noVisibleEnemies = board.AlivePlayerUnits.Count == 0;

        foreach (var action in actions)
        {
            if (action.Unit != boss) continue;
            if (action.ActionType != AIActionType.Move && action.ActionType != AIActionType.Surround) continue;

            float approach = GetApproachToEnemy(action, board);
            if (approach <= 0) continue; // 前進していないなら条件不要

            // 敵が見えない場合は自由に前進可能（味方に追従して展開する）
            if (noVisibleEnemies) continue;

            // 前進条件を評価
            float conditionScore = 0f;
            int conditionsMet = 0;

            // 条件1: 周囲に護衛がいる（2体以上）
            int escortsNear = board.CountAlliesNear(action.TargetPos, boss, 3f);
            if (escortsNear >= 2) { conditionScore += 8f; conditionsMet++; }

            // 条件2: 前線の穴を埋められる（クリスタル方向に味方がいない空白地帯）
            float nearestAllyOnFrontline = float.MaxValue;
            foreach (var u in board.AliveEnemyUnits)
            {
                if (u == null || u == boss || !u.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(action.TargetPos, u.transform.position);
                if (d < nearestAllyOnFrontline) nearestAllyOnFrontline = d;
            }
            if (nearestAllyOnFrontline > 4f) // 穴がある
            { conditionScore += 6f; conditionsMet++; }

            // 条件3: 確殺ラインを作れる（近くの敵を次ターン確殺できる）
            foreach (var pu in board.AlivePlayerUnits)
            {
                if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                float dist = Vector3.Distance(action.TargetPos, pu.transform.position);
                if (dist < 2.5f) // 攻撃圏内
                {
                    int dmg = EstimateDamage(boss, pu);
                    if (dmg >= pu.HP) { conditionScore += 12f; conditionsMet++; break; }
                }
            }

            // 条件4: 次ターン下がれる（後ろに移動先がある＝安全マス）
            int counterDmg = board.EstimateCounterDamageAt(action.TargetPos, boss);
            if (counterDmg < boss.HP * 0.3f) { conditionScore += 5f; conditionsMet++; }

            // 条件5: 自分が出ることで2体以上の指揮影響が上がる
            int influencedCount = 0;
            foreach (var u in board.AliveEnemyUnits)
            {
                if (u == null || u == boss || !u.gameObject.activeInHierarchy) continue;
                float distBefore = Vector3.Distance(u.transform.position, boss.transform.position);
                float distAfter = Vector3.Distance(u.transform.position, action.TargetPos);
                if (distAfter < distBefore && distAfter < 10f) influencedCount++;
            }
            if (influencedCount >= 2) { conditionScore += 8f; conditionsMet++; }

            // 条件が不十分ならペナルティ（出る時は強い、出ない時は徹底して出ない）
            if (conditionsMet < 2)
            {
                // 条件不足: 前進を大きく減点
                action.Score -= approach * 15f;
            }
            else
            {
                // 条件十分: 前進を加点
                action.Score += conditionScore;
            }
        }
    }

    // ================================================================
    //  経済余裕による段階的軍拡
    //  資源に余裕が出てきて維持費も払えるなら少しずつ駒を増やす
    // ================================================================
    static void ApplyGradualArmyExpansion(List<AIAction> actions, AIBoardState board)
    {
        int allyCount = board.AliveEnemyUnits.Count;
        float surplus = board.GetEconomicSurplus();

        // 軍が極端に少ない場合は経済余裕に関係なく軍拡を検討
        bool desperateForUnits = allyCount <= 3 && board.TurnCount > 5;
        if (surplus < 0.15f && !desperateForUnits) return;

        // 維持費を払える余裕度に応じて召喚ボーナス
        float expansionBonus = 0f;
        if (desperateForUnits)
            expansionBonus = 20f; // 駒が少なすぎる → 最優先
        else if (surplus > 0.7f)
            expansionBonus = 15f; // 資源潤沢
        else if (surplus > 0.5f)
            expansionBonus = 10f; // まあまあ
        else if (surplus > 0.3f)
            expansionBonus = 8f;
        else
            expansionBonus = 5f;  // 最低限

        // 軍が充実している場合は軍拡を控える
        if (allyCount >= 8) expansionBonus *= 0.3f;
        else if (allyCount >= 6) expansionBonus *= 0.6f;

        foreach (var action in actions)
        {
            if (action.ActionType != AIActionType.Summon) continue;
            action.Score += expansionBonus;

            // 安い駒を優先（維持費が少ない = 長期的に負担が少ない）
            if (action.SummonKind == Kind.Knight || action.SummonKind == Kind.Archer || action.SummonKind == Kind.Scout)
                action.Score += 5f; // 低コスト駒は維持しやすい
        }
    }

    // ================================================================
    //  スコア計算
    // ================================================================
    static float CalcScore(AIAction action, AIPersonality p, AIBoardState board, AILearning learning)
    {
        float baseScore = CalcBaseScore(action, board);
        float majorBonus = CalcMajorBonus(action, p, board);
        float traitBonus = CalcTraitBonus(action, p, board);
        float situationBonus = CalcSituationBonus(action, p, board);
        float learnBonus = learning != null ? learning.GetBonus(action, board) : 0f;

        return baseScore + majorBonus + traitBonus + situationBonus + learnBonus;
    }

    // ---- 基本評価 ----
    static float CalcBaseScore(AIAction action, AIBoardState board)
    {
        switch (action.ActionType)
        {
            case AIActionType.Attack:
                return CalcAttackBaseScore(action, board);
            case AIActionType.Move:
                return CalcMoveBaseScore(action, board);
            case AIActionType.SkillUse:
                return CalcSkillBaseScore(action, board);
            case AIActionType.Retreat:
                return CalcRetreatBaseScore(action, board);
            case AIActionType.Support:
                return CalcSupportBaseScore(action, board);
            case AIActionType.Surround:
                return CalcSurroundBaseScore(action, board);
            case AIActionType.DefenseRepos:
                return CalcDefenseReposBaseScore(action, board);
            case AIActionType.Build:
                return CalcBuildBaseScore(action, board);
            case AIActionType.Summon:
                return CalcSummonBaseScore(action, board);
            case AIActionType.SubCrystal:
                return CalcSubCrystalBaseScore(action, board);
            case AIActionType.Wait:
                return 1f;
            default:
                return 5f;
        }
    }

    static float CalcAttackBaseScore(AIAction action, AIBoardState board)
    {
        if (action.TargetUnit == null) return 0f;

        float score = 30f;

        int expectedDmg = EstimateDamage(action.Unit, action.TargetUnit);
        if (expectedDmg >= action.TargetUnit.HP)
            score += 40f;

        if (action.TargetUnit.MaxHP > 0)
        {
            float hpRatio = (float)action.TargetUnit.HP / action.TargetUnit.MaxHP;
            score += (1f - hpRatio) * 15f;
        }

        if (action.TargetUnit.kind == Kind.Crystal)
            score += 50f;
        if (action.TargetUnit.kind == Kind.King)
            score += 35f;

        if (action.TargetUnit.ShieldTurns > 0)
            score -= 30f;

        return score;
    }

    static float CalcMoveBaseScore(AIAction action, AIBoardState board)
    {
        float score = 10f;

        Vector3 unitPos = action.Unit.transform.position;
        Vector3 dest = action.TargetPos;

        float distBefore = Vector3.Distance(unitPos, board.PlayerCrystalPos);
        float distAfter = Vector3.Distance(dest, board.PlayerCrystalPos);
        float approach = distBefore - distAfter;
        score += approach * 3f;

        float nearestPlayerDist = GetNearestPlayerDist(dest, board);
        if (nearestPlayerDist < 3f)
            score += 5f;

        if (dest.y > unitPos.y)
            score += 2f;

        // Scout偵察ボーナス: 未探索エリアへの移動を高評価
        if (action.Unit.kind == Kind.Scout)
        {
            int newVisionCells = board.EstimateNewVisionCells(dest);
            if (newVisionCells > 0)
            {
                // 新たに見えるマスが多いほど高得点（最大+30）
                score += Mathf.Min(newVisionCells * 3f, 30f);
            }
            // 探索率が低いほど偵察の価値が高い
            float explorationRatio = board.GetExplorationRatio();
            if (explorationRatio < 0.5f)
                score += (1f - explorationRatio) * 15f;
        }

        return score;
    }

    static float CalcSkillBaseScore(AIAction action, AIBoardState board)
    {
        if (action.Skill == null) return 0f;
        float score = 20f; // スキル使用の基本価値（通常攻撃より少し低め→APコスト高い分）

        var skill = action.Skill;

        // 攻撃スキル
        if (skill.Multiplier > 0 && action.TargetUnit != null)
        {
            int expectedDmg = SkillSystem.CalcSkillDamage(action.Unit, action.TargetUnit, skill);
            if (expectedDmg >= action.TargetUnit.HP)
                score += 45f; // 確殺ボーナス

            if (action.TargetUnit.kind == Kind.Crystal)
                score += 40f;
            if (action.TargetUnit.kind == Kind.King)
                score += 30f;

            if (action.TargetUnit.ShieldTurns > 0)
                score -= 25f;

            // 範囲スキルで複数ヒット
            if (action.AreaTargets != null && action.AreaTargets.Count > 1)
                score += action.AreaTargets.Count * 12f;

            // 高倍率スキル
            score += skill.Multiplier * 10f;
        }

        // 回復スキル
        if (skill.FixedHeal > 0 && action.TargetUnit != null)
        {
            float hpRatio = action.TargetUnit.MaxHP > 0
                ? (float)action.TargetUnit.HP / action.TargetUnit.MaxHP : 1f;
            score += (1f - hpRatio) * 35f; // HP低いほど価値が高い
            if (hpRatio < 0.3f)
                score += 15f; // 瀕死ボーナス
        }

        // バフスキル
        if (skill.GrantBuff != BuffType.None)
        {
            score += 12f;
            if (skill.GrantBuff == BuffType.Haste)
                score += 8f; // AP回復は非常に価値が高い

            // 自己バフは敵が視界内にいない場合、大幅に減点（無駄なAP消費を防止）
            if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.SelfArea)
            {
                if (board.AlivePlayerUnits.Count == 0)
                    score -= 30f; // 敵不在時は自己バフの価値を大幅に下げる
                else
                {
                    // 敵がいても遠い場合は減点
                    float nearestEnemy = float.MaxValue;
                    foreach (var pu in board.AlivePlayerUnits)
                    {
                        if (pu == null || !pu.gameObject.activeInHierarchy) continue;
                        float d = Vector3.Distance(action.Unit.transform.position, pu.transform.position);
                        if (d < nearestEnemy) nearestEnemy = d;
                    }
                    if (nearestEnemy > 8f)
                        score -= 15f; // 敵が遠い場合もバフの価値は低い
                }
            }
        }

        // デバフ付き
        if (skill.InflictDebuff != StatusEffectType.None)
        {
            score += 8f;
            if (skill.InflictDebuff == StatusEffectType.Stun)
                score += 12f;
            if (skill.InflictDebuff == StatusEffectType.Freeze)
                score += 10f;
        }

        // APコスト効率（高APスキルは少し減点）
        score -= (skill.APCost - 4) * 1.5f;

        return score;
    }

    static float CalcRetreatBaseScore(AIAction action, AIBoardState board)
    {
        float score = 8f;
        if (action.Unit == null) return score;

        float hpRatio = action.Unit.MaxHP > 0 ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
        // HP低いほど撤退価値UP
        score += (1f - hpRatio) * 25f;
        if (hpRatio < 0.2f) score += 15f; // 瀕死

        // 自陣に近づくほど加点
        float dist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        if (dist < 5f) score += 8f;

        return score;
    }

    static float CalcSupportBaseScore(AIAction action, AIBoardState board)
    {
        float score = 15f;
        if (action.TargetUnit == null) return score;

        // 援護対象のHP低いほど価値UP
        float targetHpRatio = action.TargetUnit.MaxHP > 0
            ? (float)action.TargetUnit.HP / action.TargetUnit.MaxHP : 1f;
        score += (1f - targetHpRatio) * 20f;

        // 重要駒の援護は価値が高い
        if (action.TargetUnit.kind == Kind.King) score += 10f;

        return score;
    }

    static float CalcSurroundBaseScore(AIAction action, AIBoardState board)
    {
        float score = 18f;
        if (action.TargetUnit == null) return score;

        // 包囲対象のHP低いほど確殺チャンスで価値UP
        float hpRatio = action.TargetUnit.MaxHP > 0
            ? (float)action.TargetUnit.HP / action.TargetUnit.MaxHP : 1f;
        score += (1f - hpRatio) * 15f;

        // 重要駒の包囲は価値が高い
        if (action.TargetUnit.kind == Kind.Crystal) score += 25f;
        if (action.TargetUnit.kind == Kind.King) score += 15f;

        return score;
    }

    static float CalcDefenseReposBaseScore(AIAction action, AIBoardState board)
    {
        float score = 12f;
        if (action.Unit == null) return score;

        // クリスタルに近づくほど加点
        float dist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        if (dist < 3f) score += 15f;
        else if (dist < 5f) score += 8f;

        // クリスタルが危険なほど加点
        if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.3f)
            score += 20f;
        else if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
            score += 10f;

        return score;
    }

    static float CalcSubCrystalBaseScore(AIAction action, AIBoardState board)
    {
        float score = 22f; // サブクリ展開は領地拡張の高い価値

        // 敵クリスタルから離れすぎていない場所が良い
        float distFromHome = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        if (distFromHome < 8f) score += 10f;

        // 前線に近い場所は加点
        float distToEnemy = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
        score += Mathf.Max(0, 15f - distToEnemy);

        return score;
    }

    static float CalcBuildBaseScore(AIAction action, AIBoardState board)
    {
        float score = 15f; // 建築の基本価値
        var facility = action.Facility;
        int turn = board.TurnCount;

        // ================================================================
        //  フェーズ判定: 序盤/中盤/終盤で優先度を変える
        //  序盤(T1-5): 基礎経済（水・木・石・畑）
        //  中盤(T6-12): 加工施設・住宅・軍事準備
        //  終盤(T13+): 軍事建築・防衛強化
        // ================================================================
        bool isEarly = turn <= 5;
        bool isMid   = turn > 5 && turn <= 12;
        bool isLate  = turn > 12;

        // ================================================================
        //  生産チェーン認識: 不足資源を生産する建物を高評価
        // ================================================================
        float scarcityBonus = CalcScarcityBonus(facility, board);
        score += scarcityBonus;

        // ================================================================
        //  建物種別ごとのフェーズ対応スコア
        // ================================================================
        switch (facility)
        {
            // --- 基礎資源（原料生産） ---
            case FacilityKind.Well:
                // 水はほぼ全チェーンに必要 → 序盤最優先
                score += isEarly ? 35f : isMid ? 12f : 5f;
                // 井戸が0棟 → 水の生産手段が無い＝全チェーン停止の危機
                if (board.GetBuildingCount(FacilityKind.Well) == 0)
                {
                    score += 40f; // 最初の1棟は最重要
                    // 水残量が少ないほどさらに緊急度UP（50以下で効き始める）
                    if (board.EnemyResources != null)
                    {
                        int water = board.EnemyResources.Water;
                        if (water <= 0)       score += 50f; // 枯渇 → 最優先
                        else if (water <= 20) score += 30f;
                        else if (water <= 50) score += 15f;
                    }
                }
                break;

            case FacilityKind.LoggingCamp:
                // 木材はほぼ全施設に必要 → Well と同等に最重要
                score += isEarly ? 35f : isMid ? 12f : 5f;
                if (board.GetBuildingCount(FacilityKind.LoggingCamp) == 0)
                {
                    score += 40f; // 唯一の木材源が0棟 → 最優先
                    if (board.EnemyResources != null)
                    {
                        int wood = board.EnemyResources.Wood;
                        if (wood <= 0)       score += 50f; // 枯渇 → 最優先
                        else if (wood <= 20) score += 30f;
                        else if (wood <= 50) score += 15f;
                    }
                }
                break;

            case FacilityKind.Quarry:
                // 石材は壁・加工施設(Bakery等)に必要 → 基礎施設として重要
                score += isEarly ? 30f : isMid ? 12f : 5f;
                if (board.GetBuildingCount(FacilityKind.Quarry) == 0)
                {
                    score += 30f; // 唯一の石材源が0棟
                    if (board.EnemyResources != null)
                    {
                        int stone = board.EnemyResources.Stone;
                        if (stone <= 0)       score += 40f; // 枯渇 → 最優先
                        else if (stone <= 20) score += 20f;
                        else if (stone <= 50) score += 10f;
                    }
                }
                break;

            case FacilityKind.Field:
                // 畑は水が必要なのでWellの次（パン→市民維持に必須）
                score += isEarly ? 22f : isMid ? 12f : 5f;
                if (board.GetBuildingCount(FacilityKind.Field) == 0) score += 12f;
                // パン枯渇時は緊急加点
                if (board.EnemyResources != null && board.EnemyResources.Bread <= 5)
                    score += 10f;
                break;

            case FacilityKind.Mine:
                // 鉱山は鉄・魔法鉱石の唯一の供給源 → 序盤から重要
                score += isEarly ? 18f : isMid ? 18f : 10f;
                if (board.GetBuildingCount(FacilityKind.Mine) == 0) score += 15f;
                // Iron/MagicOreが枯渇寸前なら緊急加点
                if (board.EnemyResources != null)
                {
                    if (board.EnemyResources.Iron <= 5) score += 12f;
                    if (board.EnemyResources.MagicOre <= 5) score += 10f;
                }
                break;

            // --- 加工施設 ---
            case FacilityKind.LumberMill:
                score += isEarly ? 8f : isMid ? 18f : 8f;
                // 1棟目の加工施設は重要
                if (board.GetBuildingCount(FacilityKind.LumberMill) == 0 &&
                    board.GetBuildingCount(FacilityKind.LoggingCamp) > 0) score += 12f;
                break;

            case FacilityKind.StoneWorks:
                score += isEarly ? 8f : isMid ? 18f : 8f;
                if (board.GetBuildingCount(FacilityKind.StoneWorks) == 0 &&
                    board.GetBuildingCount(FacilityKind.Quarry) > 0) score += 12f;
                break;

            case FacilityKind.Bakery:
                // パンはほぼ全ユニット召喚に必要 → 上流さえあれば序盤から重要
                score += isEarly ? 15f : isMid ? 18f : 10f;
                if (board.GetBuildingCount(FacilityKind.Bakery) == 0 &&
                    board.GetBuildingCount(FacilityKind.Field) > 0) score += 18f;
                // パン不足で召喚できない場合は追加加点
                if (board.EnemyResources != null && board.EnemyResources.Bread < 10)
                    score += 12f;
                break;

            case FacilityKind.Smelter:
                // Mineがあれば序盤でも鉄を生産して召喚を可能にする
                score += isEarly ? 12f : isMid ? 18f : 12f;
                if (board.GetBuildingCount(FacilityKind.Smelter) == 0 &&
                    board.GetBuildingCount(FacilityKind.Mine) > 0) score += 15f;
                // 鉄不足で召喚できない場合は緊急加点
                if (board.EnemyResources != null && board.EnemyResources.Iron <= 5)
                    score += 12f;
                break;

            // --- インフラ ---
            case FacilityKind.House:
                // 市民はAP増加＋建築・召喚に必須 → 基礎施設と同等の最優先
                score += isEarly ? 35f : isMid ? 25f : 15f;
                // 1棟目の住宅は最重要（市民成長の基盤 → Citizen Capacity=0は致命的）
                if (board.GetBuildingCount(FacilityKind.House) == 0)
                {
                    score += 40f;
                    // 市民0なら全経済が停止 → 最優先
                    if (board.EnemyResources != null && board.EnemyResources.Citizen <= 0)
                        score += 50f;
                }
                // 市民が足りない場合は大幅加点（召喚には1市民必要）
                if (board.EnemyResources != null)
                {
                    if (board.EnemyResources.Citizen <= 0)
                        score += 35f; // 市民0 → 召喚不可・AP激減
                    else if (board.EnemyResources.Citizen <= 2)
                        score += 20f;
                }
                break;

            case FacilityKind.Warehouse:
                // 倉庫は資源が溢れ始める中盤以降
                score += isEarly ? 2f : isMid ? 10f : 8f;
                break;

            case FacilityKind.Barracks:
                score += isEarly ? 3f : isMid ? 12f : 15f;
                break;

            // --- 防衛建築 ---
            case FacilityKind.WoodWall:
            case FacilityKind.StoneWall:
                score += isEarly ? 3f : isMid ? 8f : 15f;
                // クリスタル危機時は大幅加点
                if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
                    score += 20f;
                // 経済基盤が未確立なのに壁を建てるのは無駄 → 大幅減点
                {
                    int coreEconCount = (board.GetBuildingCount(FacilityKind.Well) > 0 ? 1 : 0)
                        + (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0 ? 1 : 0)
                        + (board.GetBuildingCount(FacilityKind.Field) > 0 ? 1 : 0)
                        + (board.GetBuildingCount(FacilityKind.House) > 0 ? 1 : 0)
                        + (board.GetBuildingCount(FacilityKind.Quarry) > 0 ? 1 : 0);
                    if (coreEconCount < 4)
                        score -= 30f; // 基礎施設が揃うまで壁建築を大幅抑制
                }
                break;

            // --- 攻撃建築 ---
            case FacilityKind.Mortar:
            case FacilityKind.Cannon:
                score += isEarly ? 2f : isMid ? 10f : 18f;
                break;

            case FacilityKind.RestraintTrap:
            case FacilityKind.SpikeTrap:
                score += isEarly ? 3f : isMid ? 10f : 15f;
                break;

            case FacilityKind.HeroSword:
                score += isLate ? 20f : 2f;
                break;
        }

        // サブクリスタル（領地拡張）
        if (FacilityData.IsSubCrystal(facility))
            score += 15f;

        // 2棟目以降は価値が大幅に下がる（指数的収穫逓減）
        int existingCount = board.GetBuildingCount(facility);
        if (existingCount > 0)
        {
            // 1棟目: -15, 2棟目: -45, 3棟目: -90 ... 重複は非常に非効率
            score -= existingCount * existingCount * 15f;
        }

        // ================================================================
        //  加工施設ボーナス: 原料が備蓄過多で加工資源が不足している場合
        //  加工施設を強く推奨する（原料→加工の流れを促進）
        // ================================================================
        if (board.EnemyResources != null)
        {
            var res = board.EnemyResources;
            switch (facility)
            {
                case FacilityKind.LumberMill:
                    // Wood大量備蓄 & Plank不足 → 製材所が急務
                    if (res.Wood > 200 && res.Plank < 20)
                        score += 40f;
                    break;
                case FacilityKind.StoneWorks:
                    // Stone大量備蓄 & CutStone不足 → 石工所が急務
                    if (res.Stone > 200 && res.CutStone < 20)
                        score += 40f;
                    break;
                case FacilityKind.Smelter:
                    // IronOre/Coal備蓄 & Iron不足 → 製鉄所が急務
                    if ((res.IronOre > 10 || res.Coal > 20) && res.Iron < 10)
                        score += 35f;
                    break;
                case FacilityKind.Bakery:
                    // Wheat大量備蓄 & Bread不足 → パン屋が急務
                    if (res.Wheat > 100 && res.Bread < 20)
                        score += 35f;
                    break;
            }
        }

        return score;
    }

    // ================================================================
    //  不足資源ボーナス: その建物が生産する資源が不足しているほど高評価
    // ================================================================
    static float CalcScarcityBonus(FacilityKind facility, AIBoardState board)
    {
        float bonus = 0f;
        switch (facility)
        {
            case FacilityKind.Well:
                bonus += board.GetResourceScarcity("Water") * 30f;
                // 井戸が無い場合、水が潤沢でも将来の枯渇を見越して加点
                if (board.GetBuildingCount(FacilityKind.Well) == 0)
                    bonus += 20f;
                break;
            case FacilityKind.LoggingCamp:
                bonus += board.GetResourceScarcity("Wood") * 30f;
                // 伐採所が無い場合、木材が潤沢でも将来の枯渇を見越して加点
                if (board.GetBuildingCount(FacilityKind.LoggingCamp) == 0)
                    bonus += 20f;
                break;
            case FacilityKind.Quarry:
                bonus += board.GetResourceScarcity("Stone") * 28f;
                bonus += board.GetResourceScarcity("Coal") * 10f;
                // 採石場が無い場合、石材が潤沢でも将来の枯渇を見越して加点
                if (board.GetBuildingCount(FacilityKind.Quarry) == 0)
                    bonus += 15f;
                break;
            case FacilityKind.Field:
                bonus += board.GetResourceScarcity("Wheat") * 15f;
                break;
            case FacilityKind.Mine:
                bonus += board.GetResourceScarcity("IronOre") * 18f;
                bonus += board.GetResourceScarcity("MagicOre") * 12f;
                break;
            case FacilityKind.LumberMill:
                bonus += board.GetResourceScarcity("Plank") * 18f;
                break;
            case FacilityKind.StoneWorks:
                bonus += board.GetResourceScarcity("CutStone") * 18f;
                break;
            case FacilityKind.Bakery:
                bonus += board.GetResourceScarcity("Bread") * 15f;
                break;
            case FacilityKind.Smelter:
                bonus += board.GetResourceScarcity("Iron") * 20f;
                break;
        }
        return bonus;
    }

    static float CalcSummonBaseScore(AIAction action, AIBoardState board)
    {
        float score = 30f; // 召喚の基本価値

        // ================================================================
        //  経済基盤の充実度で召喚の可否を判断
        //  原料施設（各1pt）+ 加工施設（各2pt）= 基盤スコア
        //  基盤スコアが足りないほど召喚を抑制し、建築を優先させる
        // ================================================================
        int rawCount = 0; // 原料施設の数
        if (board.GetBuildingCount(FacilityKind.Well) > 0)        rawCount++;
        if (board.GetBuildingCount(FacilityKind.LoggingCamp) > 0) rawCount++;
        if (board.GetBuildingCount(FacilityKind.Quarry) > 0)      rawCount++;
        if (board.GetBuildingCount(FacilityKind.Field) > 0)       rawCount++;
        if (board.GetBuildingCount(FacilityKind.Mine) > 0)        rawCount++;

        int procCount = 0; // 加工施設の数
        if (board.GetBuildingCount(FacilityKind.Smelter) > 0)     procCount++;
        if (board.GetBuildingCount(FacilityKind.Bakery) > 0)      procCount++;
        if (board.GetBuildingCount(FacilityKind.LumberMill) > 0)  procCount++;
        if (board.GetBuildingCount(FacilityKind.StoneWorks) > 0)  procCount++;

        // 基盤スコア: 原料×1 + 加工×2（最大 5+8=13）
        float infraScore = rawCount + procCount * 2f;

        // 基盤スコアが5未満（原料施設がほぼ揃っていない）→ 召喚を大幅抑制
        // 基盤スコア5以上（原料一通り＋加工少し）→ 召喚解禁
        // 基盤スコア7以上（加工も揃ってきた）→ 召喚を積極推奨
        if (infraScore < 3f)
            score -= 80f;  // 基盤なし → ほぼ召喚しない
        else if (infraScore < 5f)
            score -= 40f;  // 基盤不足 → 建築優先
        else if (infraScore < 7f)
            score += 0f;   // 基盤が整い始めた → 召喚解禁
        else
            score += 20f;  // 基盤十分 → 積極的に召喚

        // 自軍駒数が少ないほど召喚価値が上がる
        int allyCount = board.AliveEnemyUnits.Count;
        if (allyCount <= 2) score += 35f;
        else if (allyCount <= 4) score += 25f;
        else if (allyCount <= 6) score += 15f;
        else score += 5f;

        // 前線に近い位置に配置するほど加点
        float dist = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
        score += Mathf.Max(0, 15f - dist);

        // Scout召喚ボーナス: 探索率が低く敵が見えない場合、偵察要員として優先
        if (action.SummonKind == Kind.Scout && board.AlivePlayerUnits.Count == 0)
        {
            float explorationRatio = board.GetExplorationRatio();
            if (explorationRatio < 0.5f)
                score += 20f;
            else if (explorationRatio < 0.7f)
                score += 10f;
        }

        // 戦闘ユニット召喚ボーナス
        switch (action.SummonKind)
        {
            case Kind.Knight:  score += 12f; break;
            case Kind.Archer:  score += 10f; break;
            case Kind.Magic:   score += 8f; break;
            case Kind.Assassin: score += 6f; break;
            case Kind.Scout:   score += 5f; break;
        }

        return score;
    }

    // ---- 大きい性格補正 ----
    // 仕様: BOSSが生存している場合のみ適用
    // 通常駒はBOSSからの距離に応じて影響度が減衰する
    static float CalcMajorBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        if (!p.ShouldApplyMajorBonus) return 0f;

        // BOSSからの指揮影響度を計算（BOSS自身=1.0、遠い駒ほど低い）
        float influence = action.Unit != null ? p.GetCommandInfluence(action.Unit) : 0.5f;

        // BOSS自身の前線参加は性格によって制御
        // ただし敵が見えない場合は前進を制限しない（展開期に引き籠り防止）
        if (action.Unit != null && action.Unit.IsBoss)
        {
            if (action.ActionType == AIActionType.Move && board.AlivePlayerUnits.Count > 0)
            {
                float approach = GetApproachToEnemy(action, board);
                if (approach > 0 && p.BossFrontlineRate < 0.5f)
                    return -approach * (1f - p.BossFrontlineRate) * 8f; // 知性型BOSSは前に出にくい
            }
        }

        float bonus = 0f;

        switch (p.Major)
        {
            case MajorPersonality.Combat:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 15f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0)
                    bonus += 12f; // 攻撃スキル好む
                if (action.ActionType == AIActionType.Move)
                {
                    float approach = GetApproachToEnemy(action, board);
                    bonus += approach * 5f;
                }
                if (action.ActionType == AIActionType.Surround)
                    bonus += 10f; // 包囲好む
                if (action.ActionType == AIActionType.Wait)
                    bonus -= 5f;
                if (action.ActionType == AIActionType.Retreat)
                    bonus -= 8f; // 撤退嫌い
                if (action.ActionType == AIActionType.Summon)
                    bonus += 8f;
                break;

            case MajorPersonality.Intellect:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 5f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null)
                {
                    // バフ・回復スキル好む
                    if (action.Skill.FixedHeal > 0 || action.Skill.GrantBuff != BuffType.None)
                        bonus += 12f;
                    else
                        bonus += 5f;
                }
                if (action.ActionType == AIActionType.Move)
                {
                    float allyDist = GetNearestAllyDist(action.TargetPos, action.Unit, board);
                    if (allyDist < 4f)
                        bonus += 8f;
                    float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (crystalDist > 10f)
                        bonus -= 5f;
                }
                if (action.ActionType == AIActionType.Support)
                    bonus += 10f; // 援護好む
                if (action.ActionType == AIActionType.Build)
                    bonus += 12f;
                if (action.ActionType == AIActionType.Retreat)
                    bonus += 5f; // 撤退に積極的
                break;

            case MajorPersonality.Adaptive:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 8f;
                if (action.ActionType == AIActionType.SkillUse)
                    bonus += 6f;
                break;

            case MajorPersonality.Growth:
                if (action.ActionType == AIActionType.Attack)
                    bonus += 10f;
                if (action.ActionType == AIActionType.SkillUse)
                    bonus += 8f;
                // 成長型は経済建築を重視
                if (action.ActionType == AIActionType.Build)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Summon)
                    bonus += 6f;
                break;
        }

        // 指揮影響度を乗算（BOSSから遠い駒ほど大きい性格の影響が弱まる）
        return bonus * influence;
    }

    // ---- 細かい性格補正 ----
    static float CalcTraitBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;

        switch (action.ActionType)
        {
            case AIActionType.Attack:
                bonus += p.ObsessionRate * 20f;
                if (action.TargetUnit != null)
                {
                    int myDmg = EstimateDamage(action.Unit, action.TargetUnit);
                    int counterDmg = EstimateDamage(action.TargetUnit, action.Unit);
                    if (counterDmg > myDmg)
                        bonus -= p.CautionRate * 25f;
                }
                break;

            case AIActionType.SkillUse:
                if (action.Skill != null)
                {
                    // 攻撃スキル → 執着性が影響
                    if (action.Skill.Multiplier > 0)
                        bonus += p.ObsessionRate * 15f;
                    // 回復・バフスキル → 指揮性が影響
                    if (action.Skill.FixedHeal > 0 || action.Skill.GrantBuff != BuffType.None)
                        bonus += p.CommandRate * 12f;
                    // デバフスキル → 戦術性が影響
                    if (action.Skill.InflictDebuff != StatusEffectType.None)
                        bonus += p.TacticsRate * 15f;
                    // 自己バフ → 慎重性が影響（敵が視界内にいる場合のみ）
                    if (action.Skill.Target == SkillTarget.Self && action.Skill.GrantBuff != BuffType.None
                        && board.AlivePlayerUnits.Count > 0)
                        bonus += p.CautionRate * 8f;
                    // 範囲スキルで複数ヒット → 戦術性
                    if (action.AreaTargets != null && action.AreaTargets.Count > 1)
                        bonus += p.TacticsRate * (action.AreaTargets.Count * 5f);
                }
                break;

            case AIActionType.Move:
                bonus += CalcTacticalMoveBonus(action, p, board);
                float distFromBase = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                if (distFromBase > 8f)
                {
                    // Scoutは偵察任務のため遠方ペナルティを軽減
                    float defPenalty = action.Unit.kind == Kind.Scout ? 5f : 15f;
                    bonus -= p.DefenseRate * defPenalty;
                }
                float allyDist = GetNearestAllyDist(action.TargetPos, action.Unit, board);
                if (allyDist < 3f)
                    bonus += p.CommandRate * 12f;
                else if (allyDist > 6f)
                    bonus -= p.CommandRate * 10f;
                float dangerDist = GetNearestPlayerDist(action.TargetPos, board);
                if (dangerDist < 2f)
                    bonus -= p.CautionRate * 10f;
                // Scout偵察は戦術性で強化（索敵は戦術の基礎）
                if (action.Unit.kind == Kind.Scout)
                {
                    int newCells = board.EstimateNewVisionCells(action.TargetPos);
                    if (newCells > 0)
                        bonus += p.TacticsRate * Mathf.Min(newCells * 2f, 20f);
                }
                break;

            case AIActionType.Retreat:
                bonus += p.CautionRate * 20f;
                bonus += p.DefenseRate * 10f;
                bonus -= p.ObsessionRate * 8f; // 執着性高いと撤退しにくい
                break;

            case AIActionType.Support:
                bonus += p.CommandRate * 18f;
                bonus += p.DefenseRate * 8f;
                break;

            case AIActionType.Surround:
                bonus += p.TacticsRate * 20f;
                bonus += p.ObsessionRate * 8f;
                break;

            case AIActionType.DefenseRepos:
                bonus += p.DefenseRate * 22f;
                bonus += p.CautionRate * 10f;
                bonus -= p.ObsessionRate * 5f; // 攻め気質だと防衛再配置しにくい
                break;

            case AIActionType.Build:
                bonus += p.DevelopRate * 20f;
                if (FacilityData.IsWall(action.Facility) || FacilityData.IsOffensive(action.Facility))
                    bonus += p.DefenseRate * 15f;
                // 慎重な性格は序盤の経済投資を重視
                if (board.TurnCount <= 5 && !FacilityData.IsWall(action.Facility) && !FacilityData.IsOffensive(action.Facility))
                    bonus += p.CautionRate * 12f;
                break;

            case AIActionType.Summon:
                bonus += p.CommandRate * 15f;
                bonus += p.ObsessionRate * 5f;
                break;

            case AIActionType.Wait:
                bonus += p.CautionRate * 3f;
                bonus -= p.ObsessionRate * 5f;
                break;
        }

        if (action.ActionType == AIActionType.SubCrystal)
        {
            bonus += p.DevelopRate * 25f;
        }

        return bonus;
    }

    // ---- 局面補正 ----
    static float CalcSituationBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;
        float advantageRatio = board.GetAdvantageRatio();

        if (p.Major == MajorPersonality.Adaptive)
        {
            if (advantageRatio > 0.2f)
            {
                // 有利時：攻撃・スキル攻撃・前進・包囲を強化
                if (action.ActionType == AIActionType.Attack)
                    bonus += 12f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Move)
                    bonus += GetApproachToEnemy(action, board) * 4f;
                if (action.ActionType == AIActionType.Surround)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Summon)
                    bonus += 8f;
            }
            else if (advantageRatio < -0.2f)
            {
                // 不利時：撤退・援護・回復・建築を強化
                if (action.ActionType == AIActionType.Retreat)
                    bonus += 15f;
                if (action.ActionType == AIActionType.Support)
                    bonus += 12f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.FixedHeal > 0)
                    bonus += 12f;
                if (action.ActionType == AIActionType.Build)
                    bonus += 10f;
                if (action.ActionType == AIActionType.Move)
                {
                    float retreatValue = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (retreatValue < 5f)
                        bonus += 10f;
                }
            }
        }

        // クリスタル危機時は防衛優先
        if (board.EnemyCrystalHP < board.EnemyCrystalMaxHP * 0.5f)
        {
            if (action.ActionType == AIActionType.Move && action.Unit != null)
            {
                float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                if (crystalDist < 3f)
                    bonus += 20f;
            }
            // 防衛再配置の価値UP
            if (action.ActionType == AIActionType.DefenseRepos)
                bonus += 18f;
            // 壁建築の価値UP
            if (action.ActionType == AIActionType.Build && FacilityData.IsWall(action.Facility))
                bonus += 15f;
        }

        // 駒が少ない時は召喚優先
        if (board.AliveEnemyUnits.Count <= 2)
        {
            if (action.ActionType == AIActionType.Summon)
                bonus += 15f;
        }

        // 経済逼迫時：敵不在の自己バフスキルにAPを浪費しない
        if (action.ActionType == AIActionType.SkillUse && action.Skill != null)
        {
            float surplus = board.GetEconomicSurplus();
            // 経済余剰が低い場合、非攻撃スキル（バフ・回復）のスコアを減点
            if (surplus < 0.3f && action.Skill.Multiplier <= 0 && board.AlivePlayerUnits.Count == 0)
            {
                bonus -= 20f; // 経済逼迫 + 敵不在 → スキル使用は無駄
            }
            // AP残量が少ない場合、高コストスキルを抑制
            if (board.EnemyAP <= action.Skill.APCost + 2 && action.Skill.Multiplier <= 0)
            {
                bonus -= 10f; // APギリギリで非攻撃スキルは避ける（移動や建築のAPを残す）
            }
        }

        // ================================================================
        //  経済状況に応じた建築ボーナス
        // ================================================================
        if (action.ActionType == AIActionType.Build)
        {
            // 生産施設がゼロの場合、最初の1棟は緊急性が高い
            int totalProducers = board.GetBuildingCount(FacilityKind.Well)
                               + board.GetBuildingCount(FacilityKind.LoggingCamp)
                               + board.GetBuildingCount(FacilityKind.Quarry);
            if (totalProducers == 0 && !FacilityData.IsWall(action.Facility)
                && !FacilityData.IsOffensive(action.Facility))
            {
                // 基礎生産がゼロなら経済建築を強く推奨
                bonus += 25f;
            }

            // 序盤に軍事建築を建てようとするのを抑制
            if (board.TurnCount <= 4 && (FacilityData.IsOffensive(action.Facility)
                || FacilityData.IsWall(action.Facility)))
            {
                bonus -= 15f;
            }
        }

        return bonus;
    }

    // ================================================================
    //  ヘルパー
    // ================================================================
    static int EstimateDamage(Status attacker, Status defender)
    {
        int atk = attacker.ATK;
        int def = defender.DEF;
        return Mathf.Max(0, 1 + (atk / 6) + ((atk / 2) - (def / 4)));
    }

    static float GetApproachToEnemy(AIAction action, AIBoardState board)
    {
        if (action.Unit == null) return 0f;
        Vector3 from = action.Unit.transform.position;
        Vector3 to = action.TargetPos;
        float distBefore = Vector3.Distance(from, board.PlayerCrystalPos);
        float distAfter = Vector3.Distance(to, board.PlayerCrystalPos);
        return distBefore - distAfter;
    }

    static float GetNearestPlayerDist(Vector3 pos, AIBoardState board)
    {
        float nearest = float.MaxValue;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(pos, pu.transform.position);
            if (d < nearest) nearest = d;
        }
        return nearest;
    }

    static float GetNearestAllyDist(Vector3 pos, Status self, AIBoardState board)
    {
        float nearest = float.MaxValue;
        if (self == null) return nearest;
        foreach (var au in board.AliveEnemyUnits)
        {
            if (au == null || !au.gameObject.activeInHierarchy) continue;
            if (au == self) continue;
            float d = Vector3.Distance(pos, au.transform.position);
            if (d < nearest) nearest = d;
        }
        return nearest;
    }

    static float CalcTacticalMoveBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
        float bonus = 0f;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float dist = Vector3.Distance(action.TargetPos, pu.transform.position);
            if (dist < 2f)
            {
                Vector3 diff = action.TargetPos - pu.transform.position;
                bool isFlanking = Mathf.Abs(diff.x) > Mathf.Abs(diff.z);
                if (isFlanking)
                    bonus += p.TacticsRate * 10f;
            }
        }
        return bonus;
    }
}
