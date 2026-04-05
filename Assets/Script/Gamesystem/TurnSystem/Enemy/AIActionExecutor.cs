using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  AIActionExecutor — 行動実行の責務を担当
//  AICommander から分離: Move/Attack/Skill/Build/Summon/SubCrystal の実行
// =====================================================================
public class AIActionExecutor
{
    readonly TurnGenerator _turnGen;
    readonly MoveGenerator _moveGen;
    readonly AttackGenerator _attackPoint;
    readonly BattleSystem _battleSystem;
    readonly APSystem _apSystem;
    readonly SkillSystem _skillSystem;
    readonly SubCrystalSystem _subCrystalSystem;
    BuildSystem _buildSystem;
    SummonSystem _summonSystem;

    // 外部から参照される学習・統計
    readonly AILearning _learning;
    int _totalKills;

    public int TotalKills { get => _totalKills; set => _totalKills = value; }
    public BuildSystem BuildSystem { get => _buildSystem; set => _buildSystem = value; }
    public SummonSystem SummonSystem { get => _summonSystem; set => _summonSystem = value; }

    public AIActionExecutor(
        TurnGenerator turnGen, MoveGenerator moveGen, AttackGenerator attackPoint,
        BattleSystem battleSystem, APSystem apSystem,
        SkillSystem skillSystem, SubCrystalSystem subCrystalSystem,
        BuildSystem buildSystem, SummonSystem summonSystem,
        AILearning learning)
    {
        _turnGen = turnGen;
        _moveGen = moveGen;
        _attackPoint = attackPoint;
        _battleSystem = battleSystem;
        _apSystem = apSystem;
        _skillSystem = skillSystem;
        _subCrystalSystem = subCrystalSystem;
        _buildSystem = buildSystem;
        _summonSystem = summonSystem;
        _learning = learning;
    }

    // ================================================================
    //  行動ディスパッチ
    // ================================================================
    public bool Execute(AIAction action, AIBoardState board)
    {
        switch (action.ActionType)
        {
            case AIActionType.Move:
                return ExecuteMove(action, board);
            case AIActionType.Attack:
                return ExecuteAttack(action, board);
            case AIActionType.SkillUse:
                return ExecuteSkill(action, board);
            case AIActionType.Retreat:
            case AIActionType.Support:
            case AIActionType.Surround:
            case AIActionType.DefenseRepos:
                return ExecuteMove(action, board);
            case AIActionType.Build:
                return ExecuteBuild(action, board);
            case AIActionType.Summon:
                return ExecuteSummon(action, board);
            case AIActionType.SubCrystal:
                return ExecuteSubCrystal(action, board);
            case AIActionType.Wait:
                return true;
            default:
                Debug.Log($"[AIActionExecutor] 未知のアクション: {action.ActionType}");
                return false;
        }
    }

    // ================================================================
    //  移動実行
    // ================================================================
    bool ExecuteMove(AIAction action, AIBoardState board)
    {
        var unit = action.Unit;
        var dest = action.TargetPos;

        if (!_apSystem.CanAct(Team.Enemy, APSystem.ActionType.Move, unit,
                unit.transform.position, dest))
            return false;

        Vector3 oldPos = unit.transform.position;
        Vector3 oldCell = _moveGen.Cell(oldPos);

        Vector3 actualDest = ResolveDestination(dest);

        board.ConsumeMove(unit, actualDest);
        unit.transform.position = actualDest;
        _moveGen.MoveUpdate(oldCell, _moveGen.Cell(actualDest));

        unit.HasMovedThisTurn = true;

        string moveType = GetMoveTypeName(action.ActionType);
        Debug.Log($"[AIActionExecutor] {moveType}: {unit.kind} {oldCell}→{_moveGen.Cell(actualDest)}  残AP={board.EnemyAP}");

        // 学習記録
        if (_learning.IsActive && board.CanUsePlayerCrystalAsTarget())
        {
            float distBefore = Vector3.Distance(oldPos, board.PlayerCrystalPos);
            float distAfter = Vector3.Distance(actualDest, board.PlayerCrystalPos);
            if (distAfter < distBefore)
                _learning.RecordRouteResult(actualDest, true);
        }

        return true;
    }

    /// <summary>SetPosからY座標を含む正確な位置を解決する</summary>
    Vector3 ResolveDestination(Vector3 dest)
    {
        foreach (var sp in _moveGen.mapcreate.SetPos)
        {
            if (Mathf.RoundToInt(sp.x) == Mathf.RoundToInt(dest.x) &&
                Mathf.RoundToInt(sp.z) == Mathf.RoundToInt(dest.z))
            {
                return new Vector3(sp.x, sp.y, sp.z);
            }
        }
        return dest;
    }

    // ================================================================
    //  攻撃実行
    // ================================================================
    bool ExecuteAttack(AIAction action, AIBoardState board)
    {
        var unit = action.Unit;
        var target = action.TargetUnit;

        if (target == null || !target.gameObject.activeInHierarchy) return false;
        if (!_apSystem.CanAct(Team.Enemy, APSystem.ActionType.Attack, unit)) return false;

        var prevSelect = _turnGen.Context.SelectUnit;
        _turnGen.Context.SelectUnit = unit;
        _battleSystem.target = target;

        int hpBefore = target.HP;
        board.ConsumeAttack(unit);
        _battleSystem.DamageGenerater(_turnGen);

        int hpAfter = target.HP;
        bool killed = hpAfter <= 0;

        if (killed)
        {
            _totalKills++;
            Debug.Log($"[AIActionExecutor] ★撃破! {unit.kind}→{target.kind}  DMG={hpBefore - hpAfter}");
        }
        else
        {
            Debug.Log($"[AIActionExecutor] 攻撃: {unit.kind}→{target.kind}  DMG={hpBefore - hpAfter}  残HP={hpAfter}");
        }

        RecordAttackLearning(unit, target, hpBefore, hpAfter, killed);
        _turnGen.Context.SelectUnit = prevSelect;
        return true;
    }

    void RecordAttackLearning(Status unit, Status target, int hpBefore, int hpAfter, bool killed)
    {
        if (!_learning.IsActive) return;

        if (killed)
        {
            Vector3 diff = unit.transform.position - target.transform.position;
            if (Mathf.Abs(diff.x) > Mathf.Abs(diff.z))
                _learning.RecordFlankSuccess(target.transform.position);
        }
        else
        {
            int dmgDealt = hpBefore - hpAfter;
            int expectedDmg = Mathf.Max(0, 1 + (unit.ATK / 6) + ((unit.ATK / 2) - (target.DEF / 4)));
            if (dmgDealt < expectedDmg * 0.5f)
                _learning.RecordFrontalFailure(target.transform.position);
        }
    }

    // ================================================================
    //  スキル実行
    // ================================================================
    bool ExecuteSkill(AIAction action, AIBoardState board)
    {
        var unit = action.Unit;
        var skill = action.Skill;
        if (unit == null || skill == null) return false;
        if (!_apSystem.CanUseSkill(Team.Enemy, skill.APCost)) return false;

        var prevSelect = _turnGen.Context.SelectUnit;
        _turnGen.Context.SelectUnit = unit;

        bool success = ExecuteSkillByTarget(action, board, unit, skill);

        if (success)
        {
            int cooldown = GetSkillCooldown(skill);
            unit.SkillCooldown = cooldown;
            Debug.Log($"[AIActionExecutor] スキルクールダウン設定: {unit.kind} '{skill.Name}' → {cooldown}ターン");
        }

        _turnGen.Context.SelectUnit = prevSelect;
        return success;
    }

    bool ExecuteSkillByTarget(AIAction action, AIBoardState board, Status unit, SkillData skill)
    {
        switch (skill.Target)
        {
            case SkillTarget.Self:
                board.ConsumeSkill(unit, skill.APCost);
                _skillSystem.ExecuteSkill(unit, unit, skill);
                Debug.Log($"[AIActionExecutor] スキル(自己): {unit.kind} '{skill.Name}'  残AP={board.EnemyAP}");
                return true;

            case SkillTarget.SelfArea:
                board.ConsumeSkill(unit, skill.APCost);
                if (skill.Multiplier > 0)
                {
                    var enemies = board.GetEnemiesInSkillArea(unit, skill, unit.transform.position);
                    _skillSystem.ExecuteAreaSkill(unit, skill, enemies);
                    Debug.Log($"[AIActionExecutor] スキル(自己範囲攻撃): {unit.kind} '{skill.Name}' 対象{enemies.Count}体  残AP={board.EnemyAP}");
                }
                else
                {
                    var allies = board.GetAlliesInSkillArea(unit, skill, unit.transform.position);
                    _skillSystem.ExecuteAreaSupportSkill(unit, skill, allies);
                    Debug.Log($"[AIActionExecutor] スキル(自己範囲支援): {unit.kind} '{skill.Name}' 対象{allies.Count}体  残AP={board.EnemyAP}");
                }
                return true;

            case SkillTarget.AllySingle:
                if (action.TargetUnit == null) return false;
                board.ConsumeSkill(unit, skill.APCost);
                _skillSystem.ExecuteSkill(unit, action.TargetUnit, skill);
                Debug.Log($"[AIActionExecutor] スキル(味方): {unit.kind}→{action.TargetUnit.kind} '{skill.Name}'  残AP={board.EnemyAP}");
                return true;

            case SkillTarget.EnemySingle:
            case SkillTarget.EnemyOrBuilding:
            case SkillTarget.LowHPEnemy:
            case SkillTarget.FlyingEnemy:
                return ExecuteEnemyTargetSkill(action, board, unit, skill);

            case SkillTarget.DesignatedTile:
            case SkillTarget.AdjacentCenter:
            case SkillTarget.DirectionLine:
            case SkillTarget.DesignatedRow:
                return ExecuteAreaTargetSkill(action, board, unit, skill);

            default:
                return false;
        }
    }

    bool ExecuteEnemyTargetSkill(AIAction action, AIBoardState board, Status unit, SkillData skill)
    {
        if (action.TargetUnit == null) return false;

        board.ConsumeSkill(unit, skill.APCost);
        int hpBefore = action.TargetUnit.HP;
        _battleSystem.target = action.TargetUnit;
        _skillSystem.ExecuteSkill(unit, action.TargetUnit, skill);
        int hpAfter = action.TargetUnit.HP;
        bool killed = hpAfter <= 0;

        if (killed)
        {
            _totalKills++;
            Debug.Log($"[AIActionExecutor] ★スキル撃破! {unit.kind}→{action.TargetUnit.kind} '{skill.Name}' DMG={hpBefore - hpAfter}");
        }
        else
        {
            Debug.Log($"[AIActionExecutor] スキル攻撃: {unit.kind}→{action.TargetUnit.kind} '{skill.Name}' DMG={hpBefore - hpAfter} 残HP={hpAfter}  残AP={board.EnemyAP}");
        }
        return true;
    }

    bool ExecuteAreaTargetSkill(AIAction action, AIBoardState board, Status unit, SkillData skill)
    {
        var enemies = board.GetEnemiesInSkillArea(unit, skill, action.TargetPos);
        if (enemies.Count == 0) return false;

        board.ConsumeSkill(unit, skill.APCost);
        _skillSystem.ExecuteAreaSkill(unit, skill, enemies);
        Debug.Log($"[AIActionExecutor] スキル(範囲): {unit.kind} '{skill.Name}' @{action.TargetPos} 対象{enemies.Count}体  残AP={board.EnemyAP}");
        return true;
    }

    // ================================================================
    //  建築実行
    // ================================================================
    public bool ExecuteBuild(AIAction action, AIBoardState board)
    {
        if (_buildSystem == null)
        {
            Debug.LogWarning("[AIActionExecutor] ExecuteBuild: _buildSystem==null!");
            return false;
        }

        var pos = AIBoardState.ToCell(action.TargetPos);
        Debug.Log($"[AIActionExecutor] ExecuteBuild: {action.Facility} @({pos.x},{pos.y},{pos.z}) AP={_apSystem.GetAP(Team.Enemy)}");

        bool success = _buildSystem.AIPlaceBuilding(pos, action.Facility, Team.Enemy);
        if (success)
        {
            board.RefreshAP();
            Debug.Log($"[AIActionExecutor] ★建築成功: {action.Facility} @({pos.x},{pos.y},{pos.z})  残AP={board.EnemyAP}");
        }
        else
        {
            Debug.LogWarning($"[AIActionExecutor] ✗建築失敗: {action.Facility} @({pos.x},{pos.y},{pos.z})");
        }
        return success;
    }

    // ================================================================
    //  召喚実行
    // ================================================================
    public bool ExecuteSummon(AIAction action, AIBoardState board)
    {
        if (_summonSystem == null) return false;

        var pos = AIBoardState.ToCell(action.TargetPos);
        bool success = _summonSystem.AISummonUnit(pos, action.SummonKind, Team.Enemy);
        if (success)
        {
            board.RefreshAP();
            Debug.Log($"[AIActionExecutor] 召喚: {action.SummonKind} @({pos.x},{pos.y},{pos.z})  残AP={board.EnemyAP}");
        }
        return success;
    }

    // ================================================================
    //  サブクリスタル展開実行
    // ================================================================
    bool ExecuteSubCrystal(AIAction action, AIBoardState board)
    {
        if (_subCrystalSystem == null || _buildSystem == null) return false;

        var pos = AIBoardState.ToCell(action.TargetPos);
        if (!_subCrystalSystem.CanPlaceSubCrystal(pos, Team.Enemy))
            return false;

        bool success = _buildSystem.AIPlaceBuilding(pos, FacilityKind.SubCrystal, Team.Enemy);
        if (success)
        {
            board.RefreshAP();
            Debug.Log($"[AIActionExecutor] サブクリ展開: @({pos.x},{pos.y},{pos.z})  残AP={board.EnemyAP}");
        }
        return success;
    }

    // ================================================================
    //  ヘルパー
    // ================================================================
    static int GetSkillCooldown(SkillData skill)
    {
        switch (skill.Rarity)
        {
            case SkillRarity.Normal:    return 1;
            case SkillRarity.Rare:      return 2;
            case SkillRarity.SuperRare: return 3;
            case SkillRarity.Legendary: return 4;
            default: return 2;
        }
    }

    static string GetMoveTypeName(AIActionType type)
    {
        switch (type)
        {
            case AIActionType.Retreat:      return "撤退";
            case AIActionType.Support:      return "援護";
            case AIActionType.Surround:     return "包囲";
            case AIActionType.DefenseRepos: return "防衛再配置";
            default:                        return "移動";
        }
    }
}
