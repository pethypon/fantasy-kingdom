using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AICommander — 全体指揮AI
//  将棋・チェスのように盤面全体を見て全駒をまとめて動かす
//  1ターンの行動列を組み立て、AP消費しながら順次実行する
//
//  【動作確認】Unity Console で以下のログを確認:
//    [AICommander] 初期化完了     → ゲーム開始時に1回出る
//    [AICommander] ターン開始     → 敵ターンごとに出る
//    [AICommander] 視界内敵駒     → 視界制限が効いているか確認
//    [AICommander] 候補行動       → 評価されたアクション一覧
//    [AICommander] 選択行動       → 実際に選ばれたアクション
//    [AICommander] 移動/攻撃/建築/召喚 → 各行動実行
//    [AICommander] ターン終了     → ターン終了時の統計
// =====================================================================
public class AICommander
{
    readonly AIPersonality _personality;
    readonly AILearning _learning;
    readonly TurnGenerater _turnGen;
    readonly MoveGererater _moveGen;
    readonly AttackPointt _attackPoint;
    readonly BattleSystem _battleSystem;
    readonly VisionGenerater _visionGen;
    readonly APSystem _apSystem;
    readonly UnitSetting _unitSet;
    readonly CrystalSystem _crystalSystem;
    readonly MapCreate _mapCreate;
    readonly BuildSystem _buildSystem;
    readonly SummonSystem _summonSystem;
    readonly FactionState _factionState;

    AIBoardState _board;

    // 1ターン内で既に行動した駒を追跡
    HashSet<Status> _actedUnits = new HashSet<Status>();

    // 今ターンの方針（ターン冒頭で決定）
    TurnStrategy _currentStrategy = TurnStrategy.Balanced;

    readonly SkillSystem _skillSystem;
    readonly SubCrystalSystem _subCrystalSystem;

    // 統計（動作確認用）
    int _totalMoves = 0;
    int _totalAttacks = 0;
    int _totalKills = 0;
    int _totalBuilds = 0;
    int _totalSummons = 0;
    int _totalSkills = 0;
    int _totalRetreats = 0;
    int _turnCount = 0;

    // ---- 生成（試合開始時に1回） ----
    public AICommander(
        TurnGenerater turnGen, MoveGererater moveGen, AttackPointt attackPoint,
        BattleSystem battleSystem, VisionGenerater visionGen,
        APSystem apSystem, UnitSetting unitSet, CrystalSystem crystalSystem,
        MapCreate mapCreate, MajorPersonality major,
        BuildSystem buildSystem = null, SummonSystem summonSystem = null,
        FactionState factionState = null, SkillSystem skillSystem = null,
        SubCrystalSystem subCrystalSystem = null)
    {
        _turnGen = turnGen;
        _moveGen = moveGen;
        _attackPoint = attackPoint;
        _battleSystem = battleSystem;
        _visionGen = visionGen;
        _apSystem = apSystem;
        _unitSet = unitSet;
        _crystalSystem = crystalSystem;
        _mapCreate = mapCreate;
        _buildSystem = buildSystem;
        _summonSystem = summonSystem;
        _factionState = factionState;
        _skillSystem = skillSystem;
        _subCrystalSystem = subCrystalSystem;

        _personality = new AIPersonality(major);
        _learning = new AILearning(major == MajorPersonality.Growth);

        Debug.Log("=== [AICommander] ==============================");
        Debug.Log($"[AICommander] 初期化完了");
        Debug.Log($"[AICommander] 大きい性格 = {major}");
        Debug.Log($"[AICommander] 慎重性={_personality.Traits.Caution}  " +
                  $"指揮性={_personality.Traits.Command}  " +
                  $"執着性={_personality.Traits.Obsession}");
        Debug.Log($"[AICommander] 防衛性={_personality.Traits.Defense}  " +
                  $"戦術性={_personality.Traits.Tactics}  " +
                  $"発展性={_personality.Traits.Development}");
        Debug.Log($"[AICommander] 合計={_personality.Traits.Total}pt  " +
                  $"学習={(_learning.IsActive ? "有効" : "無効")}  " +
                  $"建築={(_buildSystem != null ? "有効" : "無効")}  " +
                  $"召喚={(_summonSystem != null ? "有効" : "無効")}");
        Debug.Log("=== [AICommander] ==============================");
    }

    public AIPersonality Personality => _personality;
    public AILearning Learning => _learning;
    public TurnStrategy CurrentStrategy => _currentStrategy;

    // ================================================================
    //  ターン方針の決定
    //  盤面を見て「今ターン何を重視するか」を1つ選ぶ
    // ================================================================
    TurnStrategy DecideStrategy(AIBoardState board)
    {
        float crystalHpRatio = board.EnemyCrystalMaxHP > 0
            ? (float)board.EnemyCrystalHP / board.EnemyCrystalMaxHP : 1f;

        // クリスタルが危険なら最優先で防衛
        if (crystalHpRatio < 0.4f)
            return TurnStrategy.CrystalDefense;

        // クリスタル付近に敵がいる場合も防衛
        bool crystalThreatened = false;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(pu.transform.position, board.EnemyCrystalPos);
            if (d < 4f) { crystalThreatened = true; break; }
        }
        if (crystalThreatened && crystalHpRatio < 0.6f)
            return TurnStrategy.CrystalDefense;

        // 味方に瀕死が多い → 再編
        int criticalCount = 0;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (u.MaxHP > 0 && (float)u.HP / u.MaxHP < 0.35f) criticalCount++;
        }
        if (criticalCount >= 2)
            return TurnStrategy.RetreatRegroup;

        // 味方が少なく経済余裕がある → 建築/召喚
        if (board.AliveEnemyUnits.Count <= 3 && board.GetEconomicSurplus() > 0.4f)
            return TurnStrategy.EconomyBuild;

        // 序盤は経済重視
        if (board.TurnCount <= 4)
            return TurnStrategy.EconomyBuild;

        // 敵が見えず探索率が低い → 経済・偵察重視（Balanced でスコアリングに任せる）
        if (board.AlivePlayerUnits.Count == 0 && board.GetExplorationRatio() < 0.4f)
            return TurnStrategy.EconomyBuild;

        // 有利時は攻勢
        float advantage = board.GetAdvantageRatio();
        if (advantage > 0.25f && board.AlivePlayerUnits.Count > 0)
            return TurnStrategy.Assault;

        // 大きい性格が影響
        if (_personality.ShouldApplyMajorBonus)
        {
            switch (_personality.Major)
            {
                case MajorPersonality.Combat:
                    if (advantage > 0f) return TurnStrategy.Assault;
                    break;
                case MajorPersonality.Intellect:
                    if (advantage < -0.1f) return TurnStrategy.RetreatRegroup;
                    break;
                case MajorPersonality.Growth:
                    if (board.GetEconomicSurplus() > 0.3f && board.TurnCount <= 10)
                        return TurnStrategy.EconomyBuild;
                    break;
            }
        }

        return TurnStrategy.Balanced;
    }

    // ================================================================
    //  ExecuteTurn — 1ターン分の全行動を実行
    // ================================================================
    public void ExecuteTurn()
    {
        _actedUnits.Clear();
        _turnCount++;
        _board = new AIBoardState(_moveGen, _attackPoint, _apSystem, _unitSet,
            _crystalSystem, _visionGen, _buildSystem, _summonSystem, _factionState,
            _subCrystalSystem, _turnCount);

        // スキルクールダウンを全敵駒で減少
        TickSkillCooldowns();

        // BOSS駒の参照を更新
        _personality.UpdateBossReference(_board.AliveEnemyUnits);

        // ターン方針を決定
        _currentStrategy = DecideStrategy(_board);

        int maxIterations = 50;
        int iteration = 0;
        int consecutiveFailures = 0;
        const int maxConsecutiveFailures = 3;
        int turnMoves = 0, turnAttacks = 0, turnBuilds = 0, turnSummons = 0, turnSkills = 0, turnRetreats = 0;

        Debug.Log($"--- [AICommander] ターン{_turnCount}開始 ---");
        Debug.Log($"[AICommander] 方針={_currentStrategy}  AP={_board.EnemyAP}  " +
                  $"自軍駒数={_board.AliveEnemyUnits.Count}  " +
                  $"視界内敵駒数={_board.AlivePlayerUnits.Count}  " +
                  $"BOSS={(_personality.HasBoss ? _personality.BossUnit.kind.ToString() : "なし")}");
        Debug.Log($"[AICommander] 建築可能位置={_board.BuildablePositions.Count}  " +
                  $"召喚可能位置={_board.SummonablePositions.Count}  " +
                  $"購入可能建物={_board.AffordableBuildings.Count}  " +
                  $"召喚可能駒種={_board.AffordableUnits.Count}");

        if (_board.AlivePlayerUnits.Count > 0)
        {
            string visibleUnits = string.Join(", ",
                _board.AlivePlayerUnits.Select(u =>
                    $"{u.kind}(HP{u.HP} @{_moveGen.Cell(u.transform.position)})"));
            Debug.Log($"[AICommander] 視界内敵駒: {visibleUnits}");
        }

        // 失敗した行動タイプ+対象を記録し、同じ行動を繰り返さない
        var failedActions = new HashSet<string>();

        while (_board.EnemyAP > 0 && iteration < maxIterations)
        {
            iteration++;

            _board.Refresh();
            if (_board.EnemyAP <= 0) break;

            var actions = AIActionEvaluator.EvaluateAll(_personality, _board, _learning, _currentStrategy);
            if (actions.Count == 0)
            {
                Debug.Log("[AICommander] 候補行動なし → ターン終了");
                break;
            }

            int logCount = Mathf.Min(3, actions.Count);
            for (int i = 0; i < logCount; i++)
            {
                var a = actions[i];
                string info = a.ActionType == AIActionType.Build ? $"({a.Facility})"
                    : a.ActionType == AIActionType.Summon ? $"({a.SummonKind})"
                    : a.ActionType == AIActionType.SkillUse ? $"({a.Unit?.kind}'{a.Skill?.Name}')"
                    : a.Unit != null ? $"({a.Unit.kind})" : "";
                string targetInfo = a.TargetUnit != null ? $"→{a.TargetUnit.kind}" : "";
                Debug.Log($"[AICommander] 候補{i + 1}: {a.ActionType}{info}{targetInfo}  " +
                          $"score={a.Score:F1}  AP={a.APCost}");
            }

            AIAction bestAction = SelectBestAction(actions, failedActions);
            if (bestAction == null || bestAction.ActionType == AIActionType.Wait)
            {
                Debug.Log("[AICommander] 有効な行動なし → ターン終了");
                break;
            }

            bool success = ExecuteAction(bestAction);
            if (!success)
            {
                consecutiveFailures++;
                // この行動を失敗リストに追加して二度と選ばない
                string failKey = $"{bestAction.ActionType}_{bestAction.Facility}_{bestAction.SummonKind}_{bestAction.TargetPos}";
                failedActions.Add(failKey);
                Debug.Log($"[AICommander] 行動実行失敗 ({consecutiveFailures}/{maxConsecutiveFailures}) → 次の候補へ");
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    Debug.Log("[AICommander] 連続失敗上限 → ターン終了");
                    break;
                }
                continue;
            }

            consecutiveFailures = 0; // 成功したらリセット

            switch (bestAction.ActionType)
            {
                case AIActionType.Move: turnMoves++; break;
                case AIActionType.Attack: turnAttacks++; break;
                case AIActionType.SkillUse: turnSkills++; break;
                case AIActionType.Retreat: turnRetreats++; break;
                case AIActionType.Support: turnMoves++; break;
                case AIActionType.Surround: turnMoves++; break;
                case AIActionType.DefenseRepos: turnRetreats++; break;
                case AIActionType.Build: turnBuilds++; break;
                case AIActionType.Summon: turnSummons++; break;
                case AIActionType.SubCrystal: turnBuilds++; break;
            }

            if (bestAction.Unit != null)
                _actedUnits.Add(bestAction.Unit);
        }

        _totalMoves += turnMoves;
        _totalAttacks += turnAttacks;
        _totalSkills += turnSkills;
        _totalRetreats += turnRetreats;
        _totalBuilds += turnBuilds;
        _totalSummons += turnSummons;
        Debug.Log($"--- [AICommander] ターン{_turnCount}終了: " +
                  $"移動{turnMoves} 攻撃{turnAttacks} スキル{turnSkills} 撤退{turnRetreats} 建築{turnBuilds} 召喚{turnSummons}  " +
                  $"残AP={_board.EnemyAP}  " +
                  $"累計(移動{_totalMoves}/攻撃{_totalAttacks}/スキル{_totalSkills}/撤退{_totalRetreats}/建築{_totalBuilds}/召喚{_totalSummons}/撃破{_totalKills}) ---");
    }

    // ================================================================
    //  行動選択
    // ================================================================
    AIAction SelectBestAction(List<AIAction> actions, HashSet<string> failedActions)
    {
        AIAction best = null;
        float bestScore = float.MinValue;

        foreach (var action in actions)
        {
            if (action.ActionType == AIActionType.Wait) continue;
            if (action.APCost > _board.EnemyAP) continue;

            // 失敗済みの行動をスキップ
            string failKey = $"{action.ActionType}_{action.Facility}_{action.SummonKind}_{action.TargetPos}";
            if (failedActions.Contains(failKey)) continue;

            float score = action.Score;

            if (action.Unit != null && _actedUnits.Contains(action.Unit))
                score *= 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                best = action;
            }
        }

        return best;
    }

    // ================================================================
    //  行動実行
    // ================================================================
    bool ExecuteAction(AIAction action)
    {
        switch (action.ActionType)
        {
            case AIActionType.Move:
                return ExecuteMove(action);
            case AIActionType.Attack:
                return ExecuteAttack(action);
            case AIActionType.SkillUse:
                return ExecuteSkill(action);
            case AIActionType.Retreat:
            case AIActionType.Support:
            case AIActionType.Surround:
            case AIActionType.DefenseRepos:
                return ExecuteMove(action); // 移動として実行
            case AIActionType.Build:
                return ExecuteBuild(action);
            case AIActionType.Summon:
                return ExecuteSummon(action);
            case AIActionType.SubCrystal:
                return ExecuteSubCrystal(action);
            default:
                Debug.Log($"[AICommander] 未実装アクション: {action.ActionType}");
                return false;
        }
    }

    // ---- 移動実行 ----
    bool ExecuteMove(AIAction action)
    {
        var unit = action.Unit;
        var dest = action.TargetPos;

        if (!_apSystem.CanAct(Team.Enemy, APSystem.ActionType.Move, unit,
                unit.transform.position, dest))
            return false;

        Vector3 oldPos = unit.transform.position;
        Vector3 oldCell = _moveGen.Cell(oldPos);

        Vector3 actualDest = dest;
        foreach (var sp in _moveGen.mapcreate.SetPos)
        {
            if (Mathf.RoundToInt(sp.x) == Mathf.RoundToInt(dest.x) &&
                Mathf.RoundToInt(sp.z) == Mathf.RoundToInt(dest.z))
            {
                actualDest = new Vector3(sp.x, sp.y - 1f, sp.z);
                break;
            }
        }

        _board.ConsumeMove(unit, actualDest);
        unit.transform.position = actualDest;
        _moveGen.MoveUpdate(oldCell, _moveGen.Cell(actualDest));

        string moveType = action.ActionType == AIActionType.Retreat ? "撤退"
            : action.ActionType == AIActionType.Support ? "援護"
            : action.ActionType == AIActionType.Surround ? "包囲"
            : "移動";
        Debug.Log($"[AICommander] {moveType}: {unit.kind} {oldCell}→{_moveGen.Cell(actualDest)}  残AP={_board.EnemyAP}");

        if (_learning.IsActive)
        {
            float distBefore = Vector3.Distance(oldPos, _board.PlayerCrystalPos);
            float distAfter = Vector3.Distance(actualDest, _board.PlayerCrystalPos);
            if (distAfter < distBefore)
                _learning.RecordRouteResult(actualDest, true);
        }

        return true;
    }

    // ---- 攻撃実行 ----
    bool ExecuteAttack(AIAction action)
    {
        var unit = action.Unit;
        var target = action.TargetUnit;

        if (target == null || !target.gameObject.activeInHierarchy) return false;
        if (!_apSystem.CanAct(Team.Enemy, APSystem.ActionType.Attack, unit)) return false;

        var prevSelect = _turnGen.SelectUnit;
        _turnGen.SelectUnit = unit;
        _battleSystem.target = target;

        int hpBefore = target.HP;
        _board.ConsumeAttack(unit);
        _battleSystem.DamageGenerater(_turnGen);

        int hpAfter = target.HP;
        bool killed = hpAfter <= 0;

        if (killed)
        {
            _totalKills++;
            Debug.Log($"[AICommander] ★撃破! {unit.kind}→{target.kind}  DMG={hpBefore - hpAfter}");
        }
        else
        {
            Debug.Log($"[AICommander] 攻撃: {unit.kind}→{target.kind}  DMG={hpBefore - hpAfter}  残HP={hpAfter}");
        }

        if (_learning.IsActive)
        {
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

        _turnGen.SelectUnit = prevSelect;
        return true;
    }

    // ---- スキル実行 ----
    bool ExecuteSkill(AIAction action)
    {
        var unit = action.Unit;
        var skill = action.Skill;
        if (unit == null || skill == null) return false;
        if (!_apSystem.CanUseSkill(Team.Enemy, skill.APCost)) return false;

        var prevSelect = _turnGen.SelectUnit;
        _turnGen.SelectUnit = unit;

        bool success = false;

        switch (skill.Target)
        {
            case SkillTarget.Self:
                _board.ConsumeSkill(unit, skill.APCost);
                _skillSystem.ExecuteSkill(unit, unit, skill);
                Debug.Log($"[AICommander] スキル(自己): {unit.kind} '{skill.Name}'  残AP={_board.EnemyAP}");
                success = true;
                break;

            case SkillTarget.SelfArea:
                _board.ConsumeSkill(unit, skill.APCost);
                if (skill.Multiplier > 0)
                {
                    // 攻撃範囲スキル
                    var enemies = _board.GetEnemiesInSkillArea(unit, skill, unit.transform.position);
                    _skillSystem.ExecuteAreaSkill(unit, skill, enemies);
                    Debug.Log($"[AICommander] スキル(自己範囲攻撃): {unit.kind} '{skill.Name}' 対象{enemies.Count}体  残AP={_board.EnemyAP}");
                }
                else
                {
                    // 支援範囲スキル
                    var allies = _board.GetAlliesInSkillArea(unit, skill, unit.transform.position);
                    _skillSystem.ExecuteAreaSupportSkill(unit, skill, allies);
                    Debug.Log($"[AICommander] スキル(自己範囲支援): {unit.kind} '{skill.Name}' 対象{allies.Count}体  残AP={_board.EnemyAP}");
                }
                success = true;
                break;

            case SkillTarget.AllySingle:
                if (action.TargetUnit != null)
                {
                    _board.ConsumeSkill(unit, skill.APCost);
                    _skillSystem.ExecuteSkill(unit, action.TargetUnit, skill);
                    Debug.Log($"[AICommander] スキル(味方): {unit.kind}→{action.TargetUnit.kind} '{skill.Name}'  残AP={_board.EnemyAP}");
                    success = true;
                }
                break;

            case SkillTarget.EnemySingle:
            case SkillTarget.EnemyOrBuilding:
            case SkillTarget.LowHPEnemy:
            case SkillTarget.FlyingEnemy:
                if (action.TargetUnit != null)
                {
                    _board.ConsumeSkill(unit, skill.APCost);
                    int hpBefore = action.TargetUnit.HP;
                    _battleSystem.target = action.TargetUnit;
                    _skillSystem.ExecuteSkill(unit, action.TargetUnit, skill);
                    int hpAfter = action.TargetUnit.HP;
                    bool killed = hpAfter <= 0;
                    if (killed)
                    {
                        _totalKills++;
                        Debug.Log($"[AICommander] ★スキル撃破! {unit.kind}→{action.TargetUnit.kind} '{skill.Name}' DMG={hpBefore - hpAfter}");
                    }
                    else
                    {
                        Debug.Log($"[AICommander] スキル攻撃: {unit.kind}→{action.TargetUnit.kind} '{skill.Name}' DMG={hpBefore - hpAfter} 残HP={hpAfter}  残AP={_board.EnemyAP}");
                    }
                    success = true;
                }
                break;

            case SkillTarget.DesignatedTile:
            case SkillTarget.AdjacentCenter:
            case SkillTarget.DirectionLine:
            case SkillTarget.DesignatedRow:
                // 範囲攻撃スキル
                {
                    var enemies = _board.GetEnemiesInSkillArea(unit, skill, action.TargetPos);
                    if (enemies.Count > 0)
                    {
                        _board.ConsumeSkill(unit, skill.APCost);
                        _skillSystem.ExecuteAreaSkill(unit, skill, enemies);
                        Debug.Log($"[AICommander] スキル(範囲): {unit.kind} '{skill.Name}' @{action.TargetPos} 対象{enemies.Count}体  残AP={_board.EnemyAP}");
                        success = true;
                    }
                }
                break;
        }

        // スキル使用成功時にクールダウン設定
        if (success)
        {
            int cooldown = GetSkillCooldown(skill);
            unit.SkillCooldown = cooldown;
            Debug.Log($"[AICommander] スキルクールダウン設定: {unit.kind} '{skill.Name}' → {cooldown}ターン");
        }

        _turnGen.SelectUnit = prevSelect;
        return success;
    }

    // ---- スキルクールダウン計算 ----
    int GetSkillCooldown(SkillData skill)
    {
        switch (skill.Rarity)
        {
            case SkillRarity.Normal:    return 1; // 1ターン後に再使用可能
            case SkillRarity.Rare:      return 2;
            case SkillRarity.SuperRare: return 3;
            case SkillRarity.Legendary: return 4;
            default: return 2;
        }
    }

    // ---- 建築実行 ----
    bool ExecuteBuild(AIAction action)
    {
        if (_buildSystem == null) return false;

        var pos = new Vector3Int(
            Mathf.RoundToInt(action.TargetPos.x),
            Mathf.RoundToInt(action.TargetPos.y),
            Mathf.RoundToInt(action.TargetPos.z));

        bool success = _buildSystem.AIPlaceBuilding(pos, action.Facility, Team.Enemy);
        if (success)
        {
            _board.RefreshAP();
            Debug.Log($"[AICommander] 建築: {action.Facility} @({pos.x},{pos.y},{pos.z})  残AP={_board.EnemyAP}");
        }
        return success;
    }

    // ---- 召喚実行 ----
    bool ExecuteSummon(AIAction action)
    {
        if (_summonSystem == null) return false;

        var pos = new Vector3Int(
            Mathf.RoundToInt(action.TargetPos.x),
            Mathf.RoundToInt(action.TargetPos.y),
            Mathf.RoundToInt(action.TargetPos.z));

        bool success = _summonSystem.AISummonUnit(pos, action.SummonKind, Team.Enemy);
        if (success)
        {
            _board.RefreshAP();
            Debug.Log($"[AICommander] 召喚: {action.SummonKind} @({pos.x},{pos.y},{pos.z})  残AP={_board.EnemyAP}");
        }
        return success;
    }

    // ---- サブクリスタル展開実行 ----
    bool ExecuteSubCrystal(AIAction action)
    {
        if (_subCrystalSystem == null || _buildSystem == null) return false;

        var pos = new Vector3Int(
            Mathf.RoundToInt(action.TargetPos.x),
            Mathf.RoundToInt(action.TargetPos.y),
            Mathf.RoundToInt(action.TargetPos.z));

        if (!_subCrystalSystem.CanPlaceSubCrystal(pos, Team.Enemy))
            return false;

        bool success = _buildSystem.AIPlaceBuilding(pos, FacilityKind.SubCrystal, Team.Enemy);
        if (success)
        {
            // 領地拡張は AIPlaceBuilding 内で既に実行済み（二重呼び出し防止）
            _board.RefreshAP();
            Debug.Log($"[AICommander] サブクリ展開: @({pos.x},{pos.y},{pos.z})  残AP={_board.EnemyAP}");
        }
        return success;
    }

    // ================================================================
    //  スキルクールダウン管理
    // ================================================================
    void TickSkillCooldowns()
    {
        foreach (var unit in _board.AliveEnemyUnits)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;
            if (unit.SkillCooldown > 0)
                unit.SkillCooldown--;
        }
    }

    // ================================================================
    //  外部からの学習イベント通知
    // ================================================================
    public void OnAllyUnitKilled(Status unit, AIBoardState board)
    {
        if (!_learning.IsActive || board == null) return;

        float nearestAlly = float.MaxValue;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy || u == unit) continue;
            float d = Vector3.Distance(unit.transform.position, u.transform.position);
            if (d < nearestAlly) nearestAlly = d;
        }

        if (nearestAlly > 4f)
            _learning.RecordIsolatedDeath(unit.transform.position);
    }
}
