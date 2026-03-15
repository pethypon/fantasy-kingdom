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
        AILearning learning)
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
            GenerateWaitCandidate(unit, board, actions);
        }

        // 建築候補を生成
        GenerateBuildCandidates(board, actions);

        // 召喚候補を生成
        GenerateSummonCandidates(board, actions);

        // 各候補にスコア付け
        foreach (var action in actions)
        {
            action.Score = CalcScore(action, personality, board, learning);
        }

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
    //  候補生成: 建築
    // ================================================================
    static void GenerateBuildCandidates(AIBoardState board, List<AIAction> results)
    {
        if (board.BuildablePositions.Count == 0 || board.AffordableBuildings.Count == 0) return;

        // 建築可能な建物 × 建築可能な位置（位置は最大3つに絞ってコスト削減）
        foreach (var facility in board.AffordableBuildings)
        {
            if (!FacilityData.Table.TryGetValue(facility, out var info)) continue;

            // 位置はクリスタル付近を優先（最大3つ）
            var positions = board.BuildablePositions
                .OrderBy(p => Vector3.Distance(new Vector3(p.x, 0, p.z), board.EnemyCrystalPos))
                .Take(3);

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

    // ================================================================
    //  候補生成: 召喚
    // ================================================================
    static void GenerateSummonCandidates(AIBoardState board, List<AIAction> results)
    {
        if (board.SummonablePositions.Count == 0 || board.AffordableUnits.Count == 0) return;

        foreach (var kind in board.AffordableUnits)
        {
            if (!UnitStaticData.Table.TryGetValue(kind, out var info)) continue;

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
            case AIActionType.Build:
                return CalcBuildBaseScore(action, board);
            case AIActionType.Summon:
                return CalcSummonBaseScore(action, board);
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

    static float CalcBuildBaseScore(AIAction action, AIBoardState board)
    {
        float score = 15f; // 建築の基本価値

        var facility = action.Facility;

        // 経済建築は基本価値が高い
        if (facility == FacilityKind.Field || facility == FacilityKind.Bakery ||
            facility == FacilityKind.LoggingCamp || facility == FacilityKind.LumberMill ||
            facility == FacilityKind.Quarry || facility == FacilityKind.StoneWorks ||
            facility == FacilityKind.Mine || facility == FacilityKind.Smelter ||
            facility == FacilityKind.Well)
            score += 10f;

        // 住宅（市民+AP増加）
        if (facility == FacilityKind.House)
            score += 12f;

        // 倉庫
        if (facility == FacilityKind.Warehouse)
            score += 5f;

        // 兵舎（経験値ボーナス）
        if (facility == FacilityKind.Barracks)
            score += 8f;

        // 壁（防衛）
        if (FacilityData.IsWall(facility))
            score += 6f;

        // 攻撃建築物
        if (FacilityData.IsOffensive(facility))
            score += 10f;

        // サブクリスタル（領地拡張）
        if (FacilityData.IsSubCrystal(facility))
            score += 15f;

        return score;
    }

    static float CalcSummonBaseScore(AIAction action, AIBoardState board)
    {
        float score = 20f; // 召喚の基本価値

        // 自軍駒数が少ないほど召喚価値が上がる
        int allyCount = board.AliveEnemyUnits.Count;
        if (allyCount <= 2) score += 20f;
        else if (allyCount <= 4) score += 10f;

        // 前線に近い位置に配置するほど加点
        float dist = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
        score += Mathf.Max(0, 15f - dist);

        return score;
    }

    // ---- 大きい性格補正 ----
    static float CalcMajorBonus(AIAction action, AIPersonality p, AIBoardState board)
    {
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
                break;
        }

        return bonus;
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
                    // 自己バフ → 慎重性が影響
                    if (action.Skill.Target == SkillTarget.Self && action.Skill.GrantBuff != BuffType.None)
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
                    bonus -= p.DefenseRate * 15f;
                float allyDist = GetNearestAllyDist(action.TargetPos, action.Unit, board);
                if (allyDist < 3f)
                    bonus += p.CommandRate * 12f;
                else if (allyDist > 6f)
                    bonus -= p.CommandRate * 10f;
                float dangerDist = GetNearestPlayerDist(action.TargetPos, board);
                if (dangerDist < 2f)
                    bonus -= p.CautionRate * 10f;
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

            case AIActionType.Build:
                bonus += p.DevelopRate * 20f;
                if (FacilityData.IsWall(action.Facility) || FacilityData.IsOffensive(action.Facility))
                    bonus += p.DefenseRate * 15f;
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
