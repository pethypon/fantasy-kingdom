using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// AI候補行動の生成を担当する。
/// 移動・攻撃・スキル・撤退・援護・包囲・建築・召喚の各候補を生成する。
/// AIActionEvaluator から分離。
/// </summary>
public static class AIActionGenerator
{
    // --- 建築上限 ---
    const int DefaultMaxBuildingCount = 5;
    static readonly Dictionary<FacilityKind, int> MaxBuildingCounts = new Dictionary<FacilityKind, int>
    {
        { FacilityKind.Well,          3 }, { FacilityKind.LoggingCamp,   3 },
        { FacilityKind.Quarry,        3 }, { FacilityKind.Field,         3 },
        { FacilityKind.Mine,          2 }, { FacilityKind.LumberMill,    2 },
        { FacilityKind.StoneWorks,    2 }, { FacilityKind.Smelter,       2 },
        { FacilityKind.Bakery,        2 }, { FacilityKind.House,         4 },
        { FacilityKind.Warehouse,     2 }, { FacilityKind.Barracks,      1 },
        { FacilityKind.Mortar,        3 }, { FacilityKind.Cannon,        3 },
        { FacilityKind.WoodWall,      8 }, { FacilityKind.StoneWall,     8 },
        { FacilityKind.RestraintTrap, 4 }, { FacilityKind.SpikeTrap,     4 },
        { FacilityKind.HeroSword,     1 },
    };

    // --- 召喚上限 ---
    const int DefaultMaxUnitCount = 3;
    static readonly Dictionary<Kind, int> MaxUnitCounts = new Dictionary<Kind, int>
    {
        { Kind.Scout,    2 }, { Kind.Priest,   2 }, { Kind.Guardian, 2 },
        { Kind.Knight,   4 }, { Kind.Archer,   3 }, { Kind.Magic,    3 },
        { Kind.Assassin, 2 }, { Kind.Crossbow, 2 },
    };

    public static int GetMaxBuildingCount(FacilityKind facility)
        => MaxBuildingCounts.TryGetValue(facility, out int max) ? max : DefaultMaxBuildingCount;

    public static int GetMaxUnitCount(Kind kind)
        => MaxUnitCounts.TryGetValue(kind, out int max) ? max : DefaultMaxUnitCount;

    /// <summary>全ユニットの候補行動を生成する</summary>
    public static void GenerateAllCandidates(AIBoardState board, List<AIAction> actions)
    {
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

        GenerateBuildCandidates(board, actions);
        GenerateSummonCandidates(board, actions);
        GenerateSubCrystalCandidates(board, actions);
    }

    // ================================================================
    //  候補生成: 移動
    // ================================================================
    public static void GenerateMoveCandidates(Status unit, AIBoardState board, List<AIAction> results)
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
    public static void GenerateAttackCandidates(Status unit, AIBoardState board, List<AIAction> results)
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
    public static void GenerateSkillCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (unit.AssignedSkillId < 0) return;
        if (!SkillData.Table.TryGetValue(unit.AssignedSkillId, out var skill)) return;
        if (skill.APCost > board.EnemyAP) return;
        if (StatusEffectSystem.HasDebuff(unit, StatusEffectType.Seal)) return;
        if (unit.SkillCooldown > 0) return;

        var targets = board.GetSkillTargets(unit, skill);
        if (targets.Count == 0) return;

        switch (skill.Target)
        {
            case SkillTarget.Self:
                results.Add(new AIAction
                {
                    ActionType = AIActionType.SkillUse, Unit = unit,
                    TargetPos = unit.transform.position, TargetUnit = unit,
                    APCost = skill.APCost, Skill = skill
                });
                break;

            case SkillTarget.SelfArea:
                {
                    var allies = board.GetAlliesInSkillArea(unit, skill, unit.transform.position);
                    var enemies = board.GetEnemiesInSkillArea(unit, skill, unit.transform.position);
                    if (allies.Count > 0 || enemies.Count > 0)
                    {
                        results.Add(new AIAction
                        {
                            ActionType = AIActionType.SkillUse, Unit = unit,
                            TargetPos = unit.transform.position, TargetUnit = unit,
                            APCost = skill.APCost, Skill = skill,
                            AreaTargets = skill.Multiplier > 0 ? enemies : allies
                        });
                    }
                }
                break;

            case SkillTarget.AllySingle:
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
                            ActionType = AIActionType.SkillUse, Unit = unit,
                            TargetPos = bestAlly.transform.position, TargetUnit = bestAlly,
                            APCost = skill.APCost, Skill = skill
                        });
                    }
                }
                break;

            case SkillTarget.EnemySingle:
            case SkillTarget.EnemyOrBuilding:
            case SkillTarget.LowHPEnemy:
            case SkillTarget.FlyingEnemy:
                {
                    int count = 0;
                    foreach (var t in targets)
                    {
                        if (count >= 2) break;
                        results.Add(new AIAction
                        {
                            ActionType = AIActionType.SkillUse, Unit = unit,
                            TargetPos = t.transform.position, TargetUnit = t,
                            APCost = skill.APCost, Skill = skill
                        });
                        count++;
                    }
                }
                break;

            case SkillTarget.DesignatedTile:
            case SkillTarget.AdjacentCenter:
            case SkillTarget.DirectionLine:
            case SkillTarget.DesignatedRow:
                {
                    foreach (var t in targets)
                    {
                        var enemies = board.GetEnemiesInSkillArea(unit, skill, t.transform.position);
                        results.Add(new AIAction
                        {
                            ActionType = AIActionType.SkillUse, Unit = unit,
                            TargetPos = t.transform.position, TargetUnit = t,
                            APCost = skill.APCost, Skill = skill,
                            AreaTargets = enemies
                        });
                    }
                }
                break;
        }
    }

    // ================================================================
    //  候補生成: 撤退
    // ================================================================
    public static void GenerateRetreatCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;

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
                    bestDist = dist; bestDest = dest; bestCost = cost;
                }
            }
        }

        if (bestDest != unit.transform.position)
        {
            results.Add(new AIAction
            {
                ActionType = AIActionType.Retreat, Unit = unit,
                TargetPos = bestDest, APCost = bestCost
            });
        }
    }

    // ================================================================
    //  候補生成: 援護配置
    // ================================================================
    public static void GenerateSupportCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;

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
            { weakAllyDist = d; weakAlly = ally; }
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
                { bestDist = dist; bestDest = dest; bestCost = cost; }
            }
        }

        if (bestDest != unit.transform.position)
        {
            results.Add(new AIAction
            {
                ActionType = AIActionType.Support, Unit = unit,
                TargetPos = bestDest, TargetUnit = weakAlly, APCost = bestCost
            });
        }
    }

    // ================================================================
    //  候補生成: 包囲移動
    // ================================================================
    public static void GenerateSurroundCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;
        if (board.AlivePlayerUnits.Count == 0) return;

        Status nearestPlayer = null;
        float nearestDist = float.MaxValue;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(unit.transform.position, pu.transform.position);
            if (d < nearestDist) { nearestDist = d; nearestPlayer = pu; }
        }

        if (nearestPlayer == null || nearestDist > 6f) return;

        var enemyPos = nearestPlayer.transform.position;
        Vector3[] flankOffsets = {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, -1),
        };

        var moves = board.GetValidMoves(unit);
        foreach (var offset in flankOffsets)
        {
            Vector3 flankPos = enemyPos + offset;
            foreach (var dest in moves)
            {
                if (Vector3.Distance(dest, flankPos) < 1.5f)
                {
                    int cost = board.CalcMoveCost(unit, dest);
                    if (cost <= board.EnemyAP)
                    {
                        results.Add(new AIAction
                        {
                            ActionType = AIActionType.Surround, Unit = unit,
                            TargetPos = dest, TargetUnit = nearestPlayer, APCost = cost
                        });
                        return;
                    }
                }
            }
        }
    }

    // ================================================================
    //  候補生成: 待機
    // ================================================================
    public static void GenerateWaitCandidate(Status unit, AIBoardState board, List<AIAction> results)
    {
        results.Add(new AIAction
        {
            ActionType = AIActionType.Wait, Unit = unit,
            TargetPos = unit.transform.position, APCost = 0
        });
    }

    // ================================================================
    //  候補生成: 建築
    // ================================================================
    public static void GenerateBuildCandidates(AIBoardState board, List<AIAction> results)
    {
        if (board.BuildablePositions.Count == 0 || board.AffordableBuildings.Count == 0)
        {
            if (board.BuildablePositions.Count == 0)
                Debug.Log("[AI Build] 建築可能位置=0 → 建築候補なし（領地不足?）");
            else
                Debug.Log($"[AI Build] 購入可能建物=0 → 建築候補なし（AP={board.EnemyAP} 資源不足?）");
            return;
        }

        int candidatesBefore = results.Count;

        foreach (var facility in board.AffordableBuildings)
        {
            if (FacilityData.IsSubCrystal(facility)) continue;
            if (!FacilityData.Table.TryGetValue(facility, out var info))
            {
                Debug.Log($"[AI Build] {facility}: FacilityData未登録 → スキップ");
                continue;
            }

            int existing = board.GetBuildingCount(facility);
            int maxAllowed = GetMaxBuildingCount(facility);
            if (existing >= maxAllowed)
            {
                Debug.Log($"[AI Build] {facility}: 上限到達({existing}/{maxAllowed}) → スキップ");
                continue;
            }

            if (!board.HasUpstreamProducer(facility))
            {
                Debug.Log($"[AI Build] {facility}: 上流施設なし → スキップ");
                continue;
            }

            var positions = SelectBuildPositions(facility, board);
            int posCount = 0;

            foreach (var pos in positions)
            {
                results.Add(new AIAction
                {
                    ActionType = AIActionType.Build,
                    Facility = facility,
                    TargetPos = new Vector3(pos.x, pos.y, pos.z),
                    APCost = info.APCost
                });
                posCount++;
            }
            Debug.Log($"[AI Build] {facility}: AP={info.APCost} 候補{posCount}位置 既存={existing}");
        }

        int totalNew = results.Count - candidatesBefore;
        Debug.Log($"[AI Build] 建築候補合計: {totalNew}件 (建築可能位置={board.BuildablePositions.Count} 購入可能={board.AffordableBuildings.Count})");
    }

    static IEnumerable<Vector3Int> SelectBuildPositions(FacilityKind facility, AIBoardState board)
    {
        if (FacilityData.IsWall(facility) || FacilityData.IsOffensive(facility))
        {
            Vector3 targetDir = board.CanUsePlayerCrystalAsTarget()
                ? board.PlayerCrystalPos
                : board.EnemyCrystalPos + board.GetUnexploredDirection() * 8f;
            Vector3 frontline = Vector3.Lerp(board.EnemyCrystalPos, targetDir, 0.35f);
            return board.BuildablePositions
                .OrderBy(p => Vector3.Distance(new Vector3(p.x, 0, p.z), frontline))
                .Take(3);
        }

        return board.BuildablePositions
            .OrderBy(p => Vector3.Distance(new Vector3(p.x, 0, p.z), board.EnemyCrystalPos))
            .Take(3);
    }

    // ================================================================
    //  候補生成: 召喚
    // ================================================================
    public static void GenerateSummonCandidates(AIBoardState board, List<AIAction> results)
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

            int existingOfKind = board.AliveEnemyUnits.Count(u => u.kind == kind);
            int maxOfKind = GetMaxUnitCount(kind);
            if (existingOfKind >= maxOfKind) continue;

            Vector3 summonTarget = board.CanUsePlayerCrystalAsTarget()
                ? board.PlayerCrystalPos
                : board.EnemyCrystalPos + board.GetUnexploredDirection() * 8f;
            var positions = board.SummonablePositions
                .OrderBy(p => Vector3.Distance(new Vector3(p.x, 0, p.z), summonTarget))
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
    //  候補生成: 防衛再配置
    // ================================================================
    public static void GenerateDefenseReposCandidates(Status unit, AIBoardState board, List<AIAction> results)
    {
        if (StatusEffectSystem.IsMovementBlocked(unit)) return;

        float unitToCrystal = Vector3.Distance(unit.transform.position, board.EnemyCrystalPos);
        if (unitToCrystal < 3f) return;

        bool crystalThreatened = false;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(pu.transform.position, board.EnemyCrystalPos);
            if (d < 5f) { crystalThreatened = true; break; }
        }

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
                { bestDist = dist; bestDest = dest; bestCost = cost; }
            }
        }

        if (bestDest != unit.transform.position)
        {
            results.Add(new AIAction
            {
                ActionType = AIActionType.DefenseRepos, Unit = unit,
                TargetPos = bestDest, APCost = bestCost
            });
        }
    }

    // ================================================================
    //  候補生成: サブクリスタル展開
    // ================================================================
    public static void GenerateSubCrystalCandidates(AIBoardState board, List<AIAction> results)
    {
        if (board.SubCrystalPlaceable.Count == 0) return;
        if (board.EnemySubCrystals <= 0) return;

        int apCost = 2;
        if (FacilityData.Table.TryGetValue(FacilityKind.SubCrystal, out var info) && info.APCost > 0)
            apCost = info.APCost;
        if (apCost > board.EnemyAP) return;

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
}
