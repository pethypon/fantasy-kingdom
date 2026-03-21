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

    // 駒ごとの直近位置履歴（振動防止用）
    Dictionary<Status, List<Vector3Int>> _unitPositionHistory = new Dictionary<Status, List<Vector3Int>>();

    // 今ターンの方針（ターン冒頭で決定）
    TurnStrategy _currentStrategy = TurnStrategy.Balanced;

    // 戦略フォールバック用: 既に試した戦略を記録
    HashSet<TurnStrategy> _triedStrategies = new HashSet<TurnStrategy>();

    readonly SkillSystem _skillSystem;
    readonly SubCrystalSystem _subCrystalSystem;

    // 統計（動作確認用）
    struct TurnStats
    {
        public int Moves, Attacks, Skills, Retreats, Builds, Summons;

        public void Record(AIActionType type)
        {
            switch (type)
            {
                case AIActionType.Move:
                case AIActionType.Support:
                case AIActionType.Surround:     Moves++; break;
                case AIActionType.Attack:       Attacks++; break;
                case AIActionType.SkillUse:     Skills++; break;
                case AIActionType.Retreat:
                case AIActionType.DefenseRepos: Retreats++; break;
                case AIActionType.Build:
                case AIActionType.SubCrystal:   Builds++; break;
                case AIActionType.Summon:        Summons++; break;
            }
        }

        public override string ToString()
            => $"移動{Moves} 攻撃{Attacks} スキル{Skills} 撤退{Retreats} 建築{Builds} 召喚{Summons}";
    }

    TurnStats _totalStats;
    int _totalKills = 0;
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

    /// <summary>原料生産施設(Well,LoggingCamp,Quarry,Field,Mine)の合計棟数</summary>
    static int CountEconBuildings(AIBoardState board)
    {
        return board.GetBuildingCount(FacilityKind.Well)
             + board.GetBuildingCount(FacilityKind.LoggingCamp)
             + board.GetBuildingCount(FacilityKind.Quarry)
             + board.GetBuildingCount(FacilityKind.Field)
             + board.GetBuildingCount(FacilityKind.Mine);
    }

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

        // 経済基盤の充実度で判断（ターン数だけでなく施設数も考慮）
        int econBuildingCount = CountEconBuildings(board);
        bool hasBasicEconomy = econBuildingCount >= 2;

        int processingCount = board.GetBuildingCount(FacilityKind.Smelter)
                            + board.GetBuildingCount(FacilityKind.Bakery);
        bool hasMatureEconomy = hasBasicEconomy && processingCount >= 1;

        // 基礎施設が揃うまで経済最優先（目標: T1で基礎4棟一気建て）
        if (!hasBasicEconomy)
            return TurnStrategy.EconomyBuild;

        // 基礎施設は揃ったが加工施設が不足 → 経済拡張を継続
        if (!hasMatureEconomy && board.TurnCount <= 8)
            return TurnStrategy.EconomyBuild;

        // 経済はあるが軍が少ない → Balanced（召喚しながら追加建築も）
        if (board.AliveEnemyUnits.Count <= 6 && board.TurnCount <= 15)
            return TurnStrategy.Balanced;

        // 中盤以降は敵が見えなくても Balanced に移行（攻めの準備）
        if (board.TurnCount > 10 && board.AlivePlayerUnits.Count == 0)
            return TurnStrategy.Balanced;

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
                    if (!hasMatureEconomy)
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
        _triedStrategies.Clear();
        _turnCount++;
        _board = new AIBoardState(_moveGen, _attackPoint, _apSystem, _unitSet,
            _crystalSystem, _visionGen, _buildSystem, _summonSystem, _factionState,
            _subCrystalSystem, _turnCount);

        // スキルクールダウンを全敵駒で減少
        TickSkillCooldowns();

        // 死亡ユニットの位置履歴を掃除（メモリリーク防止）
        CleanupDeadUnitHistory();

        // BOSS駒の参照を更新
        _personality.UpdateBossReference(_board.AliveEnemyUnits);

        // ターン方針を決定
        _currentStrategy = DecideStrategy(_board);
        _triedStrategies.Add(_currentStrategy);

        int maxIterations = 50;
        int iteration = 0;
        int consecutiveFailures = 0;
        const int maxConsecutiveFailures = 8;
        int strategyFailures = 0;
        var turnStats = new TurnStats();

        Debug.Log($"--- [AICommander] ターン{_turnCount}開始 ---");
        Debug.Log($"[AICommander] 方針={_currentStrategy}  AP={_board.EnemyAP}  " +
                  $"自軍駒数={_board.AliveEnemyUnits.Count}  " +
                  $"視界内敵駒数={_board.AlivePlayerUnits.Count}  " +
                  $"BOSS={(_personality.HasBoss ? _personality.BossUnit.kind.ToString() : "なし")}");
        Debug.Log($"[AICommander] 建築可能位置={_board.BuildablePositions.Count}  " +
                  $"召喚可能位置={_board.SummonablePositions.Count}  " +
                  $"購入可能建物={_board.AffordableBuildings.Count}  " +
                  $"召喚可能駒種={_board.AffordableUnits.Count}");
        if (_board.EnemyResources != null)
        {
            var r = _board.EnemyResources;
            Debug.Log($"[AICommander] 資源: 木={r.Wood} 石={r.Stone} 鉄={r.Iron} 魔={r.MagicOre} " +
                      $"水={r.Water} 板={r.Plank} 石材={r.CutStone} パン={r.Bread} " +
                      $"市民={r.Citizen} 鉄鉱={r.IronOre} 石炭={r.Coal}");
        }
        if (_board.AffordableUnits.Count > 0)
        {
            Debug.Log($"[AICommander] 召喚可能: {string.Join(", ", _board.AffordableUnits)}");
        }

        if (_board.AlivePlayerUnits.Count > 0)
        {
            string visibleUnits = string.Join(", ",
                _board.AlivePlayerUnits.Select(u =>
                    $"{u.kind}(HP{u.HP} @{_moveGen.Cell(u.transform.position)})"));
            Debug.Log($"[AICommander] 視界内敵駒: {visibleUnits}");
        }

        // 失敗した行動タイプ+対象を記録し、同じ行動を繰り返さない
        var failedActions = new HashSet<string>();
        // 同種の行動が全位置で失敗する場合に備え、種類単位でもブロック
        var failedActionTypes = new HashSet<string>();

        // AP予算: 建築/召喚が可能なら最低限のAPを予約する
        int reservedAP = CalcReservedAP();

        while (_board.EnemyAP > 0 && iteration < maxIterations)
        {
            iteration++;

            _board.Refresh();
            if (_board.EnemyAP <= 0) break;

            // AP予約を再計算（建築/召喚した後は予約を解除）
            reservedAP = CalcReservedAP();

            var actions = AIActionEvaluator.EvaluateAll(_personality, _board, _learning, _currentStrategy);
            if (actions.Count == 0)
            {
                // 戦略フォールバック: 別の戦略を試す
                if (TryFallbackStrategy())
                {
                    Debug.Log($"[AICommander] 候補行動なし → 戦略を{_currentStrategy}に切替");
                    continue;
                }
                Debug.Log("[AICommander] 候補行動なし＆全戦略試行済み → ターン終了");
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

            // 振動防止: 直近の位置に戻る移動を減点
            ApplyAntiOscillationPenalty(actions);

            // AP予約: 移動系アクションがAP予約を食い込む場合は減点
            ApplyAPReservationPenalty(actions, reservedAP);

            AIAction bestAction = SelectBestAction(actions, failedActions, failedActionTypes);
            if (bestAction == null || bestAction.ActionType == AIActionType.Wait)
            {
                // 戦略フォールバック: 別の戦略を試す
                if (TryFallbackStrategy())
                {
                    Debug.Log($"[AICommander] 有効行動なし → 戦略を{_currentStrategy}に切替");
                    strategyFailures = 0;
                    consecutiveFailures = 0;
                    continue;
                }
                Debug.Log("[AICommander] 有効な行動なし＆全戦略試行済み → ターン終了");
                break;
            }

            bool success = ExecuteAction(bestAction);
            if (!success)
            {
                consecutiveFailures++;
                strategyFailures++;
                // この行動を失敗リストに追加して二度と選ばない
                string failKey = $"{bestAction.ActionType}_{bestAction.Facility}_{bestAction.SummonKind}_{bestAction.TargetPos}";
                failedActions.Add(failKey);
                // 同じ種類（ActionType+Facility or ActionType+SummonKind）の失敗が2回以上なら種類ごとブロック
                string typeKey = $"{bestAction.ActionType}_{bestAction.Facility}_{bestAction.SummonKind}";
                int sameTypeFailCount = 0;
                foreach (var fk in failedActions)
                {
                    if (fk.StartsWith(typeKey)) sameTypeFailCount++;
                }
                if (sameTypeFailCount >= 2)
                {
                    failedActionTypes.Add(typeKey);
                    Debug.Log($"[AICommander] 同種行動2回失敗 → {typeKey} を種類ごとブロック");
                }

                // 現戦略で3回失敗したら戦略切替を試みる（連続失敗上限に達する前に回復）
                if (strategyFailures >= 3 && TryFallbackStrategy())
                {
                    Debug.Log($"[AICommander] 戦略失敗{strategyFailures}回 → 戦略を{_currentStrategy}に切替");
                    strategyFailures = 0;
                    consecutiveFailures = Mathf.Max(0, consecutiveFailures - 2); // 少しリセット
                    continue;
                }

                Debug.Log($"[AICommander] 行動実行失敗 ({consecutiveFailures}/{maxConsecutiveFailures}) → 次の候補へ");
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    Debug.Log("[AICommander] 連続失敗上限 → ターン終了");
                    break;
                }
                continue;
            }

            consecutiveFailures = 0; // 成功したらリセット
            strategyFailures = 0;

            turnStats.Record(bestAction.ActionType);

            if (bestAction.Unit != null)
                _actedUnits.Add(bestAction.Unit);
        }

        _totalStats.Moves += turnStats.Moves;
        _totalStats.Attacks += turnStats.Attacks;
        _totalStats.Skills += turnStats.Skills;
        _totalStats.Retreats += turnStats.Retreats;
        _totalStats.Builds += turnStats.Builds;
        _totalStats.Summons += turnStats.Summons;
        Debug.Log($"--- [AICommander] ターン{_turnCount}終了: {turnStats}  " +
                  $"残AP={_board.EnemyAP}  累計({_totalStats}/撃破{_totalKills}) ---");
    }

    // ================================================================
    //  戦略フォールバック: 現戦略が行き詰まった時に別の戦略を試す
    // ================================================================
    bool TryFallbackStrategy()
    {
        // フォールバック優先順: Balanced → EconomyBuild → Assault → RetreatRegroup
        TurnStrategy[] fallbackOrder = {
            TurnStrategy.Balanced,
            TurnStrategy.EconomyBuild,
            TurnStrategy.Assault,
            TurnStrategy.RetreatRegroup,
            TurnStrategy.CrystalDefense
        };

        foreach (var strategy in fallbackOrder)
        {
            if (_triedStrategies.Contains(strategy)) continue;
            _currentStrategy = strategy;
            _triedStrategies.Add(strategy);
            return true;
        }
        return false;
    }

    // ================================================================
    //  AP予約計算: 建築/召喚用にAPを確保する
    //  移動でAPを使い果たして建築/召喚できなくなるのを防ぐ
    // ================================================================
    int CalcReservedAP()
    {
        if (_board == null) return 0;

        int reserved = 0;

        // 建築可能なら最も安い建築コストを予約
        if (_board.BuildablePositions.Count > 0 && _board.AffordableBuildings.Count > 0)
        {
            int cheapestBuild = int.MaxValue;
            foreach (var fk in _board.AffordableBuildings)
            {
                if (FacilityData.Table.TryGetValue(fk, out var info))
                    cheapestBuild = Mathf.Min(cheapestBuild, info.APCost);
            }
            if (cheapestBuild < int.MaxValue)
                reserved = Mathf.Max(reserved, cheapestBuild);
        }

        // 召喚可能なら召喚コストも考慮
        if (_board.SummonablePositions.Count > 0 && _board.AffordableUnits.Count > 0)
        {
            int cheapestSummon = int.MaxValue;
            foreach (var k in _board.AffordableUnits)
            {
                if (UnitStaticData.Table.TryGetValue(k, out var info))
                    cheapestSummon = Mathf.Min(cheapestSummon, info.CostAP);
            }
            if (cheapestSummon < int.MaxValue)
                reserved = Mathf.Max(reserved, cheapestSummon);
        }

        return reserved;
    }

    // ================================================================
    //  AP予約ペナルティ: 移動系が予約APを食い込む場合に減点
    // ================================================================
    void ApplyAPReservationPenalty(List<AIAction> actions, int reservedAP)
    {
        if (reservedAP <= 0) return;

        foreach (var action in actions)
        {
            // 建築・召喚・サブクリスタルは予約対象なのでペナルティなし
            if (action.ActionType == AIActionType.Build
                || action.ActionType == AIActionType.Summon
                || action.ActionType == AIActionType.SubCrystal)
                continue;

            // 攻撃は高価値なので軽いペナルティのみ
            if (action.ActionType == AIActionType.Attack
                || action.ActionType == AIActionType.SkillUse)
            {
                if (_board.EnemyAP - action.APCost < reservedAP)
                    action.Score -= 5f;
                continue;
            }

            // 移動系: AP予約を食い込む場合は減点
            if (_board.EnemyAP - action.APCost < reservedAP)
                action.Score -= 15f;
        }
    }

    // ================================================================
    //  死亡ユニットの位置履歴を掃除
    // ================================================================
    void CleanupDeadUnitHistory()
    {
        var deadUnits = new List<Status>();
        foreach (var kvp in _unitPositionHistory)
        {
            if (kvp.Key == null || !kvp.Key.gameObject.activeInHierarchy || kvp.Key.HP <= 0)
                deadUnits.Add(kvp.Key);
        }
        foreach (var unit in deadUnits)
            _unitPositionHistory.Remove(unit);
    }

    // ================================================================
    //  行動選択
    // ================================================================
    AIAction SelectBestAction(List<AIAction> actions, HashSet<string> failedActions,
        HashSet<string> failedActionTypes = null)
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

            // 種類ごとブロック済みの行動をスキップ
            if (failedActionTypes != null)
            {
                string typeKey = $"{action.ActionType}_{action.Facility}_{action.SummonKind}";
                if (failedActionTypes.Contains(typeKey)) continue;
            }

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
    //  振動防止ペナルティ
    //  直近に訪れたマスへ戻る移動を大きく減点し、同じ2マスを往復するのを防ぐ
    // ================================================================
    void ApplyAntiOscillationPenalty(List<AIAction> actions)
    {
        foreach (var action in actions)
        {
            if (action.Unit == null) continue;
            if (action.ActionType != AIActionType.Move
                && action.ActionType != AIActionType.Retreat
                && action.ActionType != AIActionType.Support
                && action.ActionType != AIActionType.Surround) continue;

            if (!_unitPositionHistory.TryGetValue(action.Unit, out var history)) continue;
            if (history.Count == 0) continue;

            var destCell = AIBoardState.ToCell(action.TargetPos);

            // 直近の位置と一致 → 大ペナルティ（往復防止）
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].x == destCell.x && history[i].z == destCell.z)
                {
                    float recency = history.Count - i; // 1=直前, 2=2ターン前...
                    float penalty = 30f / recency;     // 直前なら-30, 2ターン前なら-15
                    action.Score -= penalty;
                    break;
                }
            }
        }
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
                actualDest = new Vector3(sp.x, sp.y, sp.z);
                break;
            }
        }

        _board.ConsumeMove(unit, actualDest);
        unit.transform.position = actualDest;
        _moveGen.MoveUpdate(oldCell, _moveGen.Cell(actualDest));

        // 位置履歴を記録（振動防止用）
        var cellInt = AIBoardState.ToCell(actualDest);
        if (!_unitPositionHistory.ContainsKey(unit))
            _unitPositionHistory[unit] = new List<Vector3Int>();
        _unitPositionHistory[unit].Add(cellInt);
        if (_unitPositionHistory[unit].Count > 4)
            _unitPositionHistory[unit].RemoveAt(0);

        string moveType = GetMoveTypeName(action.ActionType);
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

        var pos = AIBoardState.ToCell(action.TargetPos);

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

        var pos = AIBoardState.ToCell(action.TargetPos);

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

        var pos = AIBoardState.ToCell(action.TargetPos);

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
    //  表示ヘルパー
    // ================================================================
    static string GetMoveTypeName(AIActionType type)
    {
        switch (type)
        {
            case AIActionType.Retreat:     return "撤退";
            case AIActionType.Support:     return "援護";
            case AIActionType.Surround:    return "包囲";
            case AIActionType.DefenseRepos:return "防衛再配置";
            default:                       return "移動";
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
