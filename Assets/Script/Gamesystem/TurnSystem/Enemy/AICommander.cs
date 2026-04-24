using System.Collections.Generic;
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
public partial class AICommander
{
    readonly AIPersonality _personality;
    readonly AILearning _learning;
    readonly TurnGenerator _turnGen;
    readonly MoveGenerator _moveGen;
    readonly AttackGenerator _attackPoint;
    readonly BattleSystem _battleSystem;
    readonly VisionGenerator _visionGen;
    readonly APSystem _apSystem;
    readonly UnitSetting _unitSet;
    readonly CrystalSystem _crystalSystem;
    readonly MapCreate _mapCreate;
    BuildSystem _buildSystem;          // readonly 解除: ExecuteTurn() での遅延取得を可能にする
    SummonSystem _summonSystem;        // readonly 解除: 同上
    readonly FactionState _factionState;

    // ---- 新システム ----
    readonly AIRoleAssigner _roleAssigner;
    readonly AIThreatLevel _threatLevel;
    readonly AIDeterministicRandom _rng;
    readonly TurnStrategyPlanner _strategyPlanner;

    // ---- 分離クラス ----
    readonly AIActionExecutor _actionExecutor;
    readonly AIBuildPlanner _buildPlanner;

    // AP予算配分（TurnStrategyPlannerが毎ターン計画）
    TurnStrategyPlanner.APBudget _apBudget;

    AIBoardState _board;

    // 1ターン内で既に行動した駒を追跡
    HashSet<Status> _actedUnits = new HashSet<Status>();

    // 駒ごとの直近位置履歴（振動防止用）
    Dictionary<Status, List<Vector3Int>> _unitPositionHistory = new Dictionary<Status, List<Vector3Int>>();

    // 索敵メモリ（AIBoardState間で共有、試合中永続）
    readonly Dictionary<int, AIBoardState.LastKnownInfo> _sharedMemory = AIBoardState.CreateSharedMemory();

    // 今ターンの方針（ターン冒頭で決定）
    TurnStrategy _currentStrategy = TurnStrategy.Balanced;

    // 戦略フォールバック用: 既に試した戦略を記録
    HashSet<TurnStrategy> _triedStrategies = new HashSet<TurnStrategy>();

    readonly SkillSystem _skillSystem;
    readonly SubCrystalSystem _subCrystalSystem;

    // ---- 師団長制AI（階層指揮システム） ----
    readonly KingCommanderSystem _kingCommanderSystem;
    bool _hierarchicalMode = true; // 師団長制を有効にするフラグ

    // ---- 機械学習AI (脅威度20以降で有効、師団長制では無効) ----
    readonly MLIntegration _mlIntegration;

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
        TurnGenerator turnGen, MoveGenerator moveGen, AttackGenerator attackPoint,
        BattleSystem battleSystem, VisionGenerator visionGen,
        APSystem apSystem, UnitSetting unitSet, CrystalSystem crystalSystem,
        MapCreate mapCreate, MajorPersonality major,
        BuildSystem buildSystem = null, SummonSystem summonSystem = null,
        FactionState factionState = null, SkillSystem skillSystem = null,
        SubCrystalSystem subCrystalSystem = null,
        int initialThreatLevel = 1, int randomSeed = -1)
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
        _mlIntegration = new MLIntegration(initialThreatLevel, major, randomSeed);

        // 新システム初期化
        _roleAssigner = new AIRoleAssigner();
        _threatLevel = new AIThreatLevel(initialThreatLevel);
        _rng = new AIDeterministicRandom(randomSeed >= 0 ? randomSeed : System.Environment.TickCount);
        _strategyPlanner = new TurnStrategyPlanner();

        // 分離クラス初期化
        _actionExecutor = new AIActionExecutor(
            turnGen, moveGen, attackPoint, battleSystem, apSystem,
            skillSystem, subCrystalSystem, buildSystem, summonSystem, _learning);
        _buildPlanner = new AIBuildPlanner(apSystem, factionState, _actionExecutor, _personality, _learning);

        // 師団長制AI初期化
        _kingCommanderSystem = new KingCommanderSystem(_personality, _rng);

        Debug.Log("=== [AICommander] ==============================");
        Debug.Log($"[AICommander] 初期化完了");
        Debug.Log($"[AICommander] 大きい性格 = {major}  脅威度={_threatLevel.Level} ({_threatLevel.GetTierName()})");
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
        Debug.Log($"[AICommander] 探索={(_threatLevel.UseSearchEngine ? $"有効(深さ{_threatLevel.SearchDepth})" : "無効")}  " +
                  $"ロール={(_threatLevel.UseRoleAssignment ? "有効" : "無効")}  " +
                  $"学習率={_threatLevel.LearningRate:F1}  シード={_rng.Seed}");
        Debug.Log($"[AICommander] {_mlIntegration.GetDebugInfo()}");
        Debug.Log($"[AICommander] 師団長制={(_hierarchicalMode ? "有効" : "無効")}  " +
                  $"最大師団数={KingCommanderSystem.MaxDivisions}  " +
                  $"師団兵上限={KingCommanderSystem.MaxDivisionUnits}  " +
                  $"王直轄上限={KingCommanderSystem.MaxKingDirectUnits}");
        Debug.Log("=== [AICommander] ==============================");
    }

    public AIPersonality Personality => _personality;
    public AILearning Learning => _learning;
    public TurnStrategy CurrentStrategy => _currentStrategy;
    public AIThreatLevel ThreatLevel => _threatLevel;
    public AIRoleAssigner RoleAssigner => _roleAssigner;
    public TurnStrategyPlanner StrategyPlanner => _strategyPlanner;
    public MLIntegration MLIntegration => _mlIntegration;
    public KingCommanderSystem KingCommander => _kingCommanderSystem;
    public bool HierarchicalMode { get => _hierarchicalMode; set => _hierarchicalMode = value; }

    // ---- セーブ/ロード用アクセサ ----
    public int SaveTotalMoves    { get => _totalStats.Moves;    set => _totalStats.Moves = value; }
    public int SaveTotalAttacks  { get => _totalStats.Attacks;  set => _totalStats.Attacks = value; }
    public int SaveTotalSkills   { get => _totalStats.Skills;   set => _totalStats.Skills = value; }
    public int SaveTotalRetreats { get => _totalStats.Retreats; set => _totalStats.Retreats = value; }
    public int SaveTotalBuilds   { get => _totalStats.Builds;   set => _totalStats.Builds = value; }
    public int SaveTotalSummons  { get => _totalStats.Summons;  set => _totalStats.Summons = value; }
    public int SaveTotalKills    { get => _totalKills; set => _totalKills = value; }
    public int SaveTurnCount     { get => _turnCount;  set => _turnCount = value; }

    public void RestoreStrategy(TurnStrategy strategy) { _currentStrategy = strategy; }

    // ================================================================
    //  ExecuteTurn — 1ターン分の全行動を実行
    // ================================================================
    public void ExecuteTurn()
    {
        _actedUnits.Clear();
        _triedStrategies.Clear();
        _turnCount++;

        // BuildSystem の遅延取得（SerializeField未設定対策）
        if (_buildSystem == null)
        {
            _buildSystem = _turnGen.Systems.BuildSystem;
            if (_buildSystem == null)
                _buildSystem = Object.FindFirstObjectByType<BuildSystem>();
            if (_buildSystem != null)
            {
                _actionExecutor.BuildSystem = _buildSystem;
                Debug.Log("[AICommander] BuildSystem を遅延取得しました");
            }
            else
                Debug.LogWarning("[AICommander] BuildSystem が見つかりません — 建築不可");
        }

        // SummonSystem の遅延取得
        if (_summonSystem == null)
        {
            _summonSystem = _turnGen.Systems.SummonSystem;
            if (_summonSystem == null)
                _summonSystem = Object.FindFirstObjectByType<SummonSystem>();
            if (_summonSystem != null)
            {
                _actionExecutor.SummonSystem = _summonSystem;
                Debug.Log("[AICommander] SummonSystem を遅延取得しました");
            }
        }

        _board = new AIBoardState(_moveGen, _attackPoint, _apSystem, _unitSet,
            _crystalSystem, _visionGen, _buildSystem, _summonSystem, _factionState,
            _subCrystalSystem, _turnCount, _sharedMemory);
        _board.DungeonSystem = _turnGen?.Systems?.DungeonSystem;
        _board.MapCreate = _mapCreate;

        // スキルクールダウンを全敵駒で減少
        TickSkillCooldowns();

        // 死亡ユニットの位置履歴を掃除（メモリリーク防止）
        CleanupDeadUnitHistory();

        // BOSS駒の参照を更新
        _personality.UpdateBossReference(_board.AliveEnemyUnits);

        // ターン方針を決定（TurnStrategyPlannerが盤面を評価して方針+AP予算を計画）
        var strategyDecision = _strategyPlanner.DecideStrategy(_board, _personality, _threatLevel, _turnCount);
        _currentStrategy = strategyDecision.Strategy;
        _apBudget = strategyDecision.Budget;
        _triedStrategies.Add(_currentStrategy);

        // 決定論的乱数のターンシード設定
        _rng.SetTurnSeed(_turnCount);

        // 機械学習AIのターン開始通知（師団長制では無効）
        if (!_hierarchicalMode && AIConfig.IsMLEnabled)
        {
            _mlIntegration.ObservePlayerFormation(_board.AlivePlayerUnits, _board.EnemyCrystalPos, _turnCount);
            _mlIntegration.OnTurnStart(_currentStrategy, _threatLevel.Level, _board, _turnCount);
        }

        // ロール割当（脅威度が通常知能以上で有効）
        if (_threatLevel.UseRoleAssignment)
        {
            _roleAssigner.AssignRoles(_board, _currentStrategy, _personality);
        }

        int maxIterations = 50;
        int iteration = 0;
        int consecutiveFailures = 0;
        const int maxConsecutiveFailures = 8;
        int strategyFailures = 0;
        var turnStats = new TurnStats();

        // ================================================================
        //  ★ 師団長制AI: 師団長を選出→兵を割当→提案収集→採択→実行
        //  ML機能は師団長制モードでは無効化される。
        // ================================================================
        if (_hierarchicalMode)
        {
            ExecuteHierarchicalPhase(ref turnStats);
        }

        // 探索エンジンとtopCandidatesリストをループ外で事前確保（GC削減）
        var searchEngine = new AISearchEngine(_threatLevel.SearchDepth, _threatLevel.SearchCandidateLimit, _rng);
        searchEngine.SetSimulationReferences(_moveGen, _unitSet, _crystalSystem, _apSystem);
        var topCandidates = new List<AIAction>();

        Debug.Log($"--- [AICommander] ターン{_turnCount}開始 ---");
        Debug.Log($"[AICommander] 方針={_currentStrategy}  理由=\"{strategyDecision.Reason}\"  AP={_board.EnemyAP}  " +
                  $"自軍駒数={_board.AliveEnemyUnits.Count}  " +
                  $"視界内敵駒数={_board.AlivePlayerUnits.Count}  " +
                  $"BOSS={(_personality.HasBoss ? _personality.BossUnit.kind.ToString() : "なし")}  " +
                  $"脅威度={_threatLevel.Level}({_threatLevel.GetTierName()})");
        Debug.Log($"[AICommander] AP予算: {_apBudget}");
        Debug.Log($"[AICommander] 建築可能位置={_board.BuildablePositions.Count}  " +
                  $"召喚可能位置={_board.SummonablePositions.Count}  " +
                  $"購入可能建物={_board.AffordableBuildings.Count}  " +
                  $"召喚可能駒種={_board.AffordableUnits.Count}");
        if (_board.AffordableBuildings.Count > 0)
        {
            Debug.Log($"[AICommander] 建築可能: {string.Join(", ", _board.AffordableBuildings)}");
        }
        Debug.Log($"[AICommander] 経済: 原料施設={EconomyHelper.CountEconBuildings(_board)}  " +
                  $"加工施設={EconomyHelper.CountProcessingBuildings(_board)}  " +
                  $"住宅={_board.GetBuildingCount(FacilityKind.House)}  " +
                  $"経済充足={EconomyHelper.IsEconomySufficient(_board)}");
        if (_board.EnemyResources != null)
        {
            var r = _board.EnemyResources;
            Debug.Log($"[AICommander] 資源: 木={r.Wood} 石={r.Stone} 鉄={r.Iron} 魔={r.MagicOre} " +
                      $"水={r.Water} パン={r.Bread} 市民={r.Citizen}");
        }
        if (_board.AffordableUnits.Count > 0)
        {
            Debug.Log($"[AICommander] 召喚可能: {string.Join(", ", _board.AffordableUnits)}");
        }

        if (_board.AlivePlayerUnits.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            for (int vi = 0; vi < _board.AlivePlayerUnits.Count; vi++)
            {
                var u = _board.AlivePlayerUnits[vi];
                if (vi > 0) sb.Append(", ");
                sb.Append($"{u.kind}(HP{u.HP} @{_moveGen.Cell(u.transform.position)})");
            }
            Debug.Log($"[AICommander] 視界内敵駒: {sb}");
        }

        // 失敗した行動タイプ+対象を記録し、同じ行動を繰り返さない
        var failedActions = new HashSet<string>();
        // 同種の行動が全位置で失敗する場合に備え、種類単位でもブロック
        var failedActionTypes = new HashSet<string>();

        // ================================================================
        //  ★ 建築先行フェーズ: 経済未成熟時は移動の前に建築を試みる
        //  これにより移動でAPを使い切って建築不能になる問題を防止する
        // ================================================================
        int earlyBuilds = _buildPlanner.TryEarlyBuildPhase(_board, _currentStrategy, _turnCount);
        for (int eb = 0; eb < earlyBuilds; eb++) turnStats.Record(AIActionType.Build);

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

            // ---- 脅威度ボーナス適用 ----
            foreach (var action in actions)
            {
                action.Score += _threatLevel.GetThreatBonus(action, _board);
            }

            // ---- 機械学習AIスコア適用（脅威度20以上で有効、師団長制では無効） ----
            if (!_hierarchicalMode && AIConfig.IsMLEnabled)
                _mlIntegration.EvaluateActions(actions, _board);

            // ---- 師団長制: 王直轄ユニットのアクションのみに絞る ----
            if (_hierarchicalMode && _kingCommanderSystem.HasDivisions)
            {
                var kingUnits = _kingCommanderSystem.GetKingDirectUnitSet();
                actions.RemoveAll(a => a.Unit != null && !kingUnits.Contains(a.Unit));
            }

            // ---- ロールボーナス適用（脅威度が通常知能以上で有効） ----
            if (_threatLevel.UseRoleAssignment)
            {
                foreach (var action in actions)
                {
                    if (action.Unit != null)
                        action.Score += _roleAssigner.GetRoleBonus(action.Unit, action, _board);
                }
            }

            // ---- 3手先完全シミュレーション探索（常時有効） ----
            {
                // スコア降順ソートして上位候補を抽出
                actions.Sort((a, b) => b.Score.CompareTo(a.Score));
                int candidateLimit = _threatLevel.SearchCandidateLimit;
                topCandidates.Clear();
                for (int i = 0; i < Mathf.Min(candidateLimit, actions.Count); i++)
                {
                    if (actions[i].ActionType != AIActionType.Wait)
                        topCandidates.Add(actions[i]);
                }

                if (topCandidates.Count > 0)
                {
                    var lookaheadScores = searchEngine.EvaluateWithLookahead(
                        topCandidates, _board, _personality, _learning);

                    foreach (var kvp in lookaheadScores)
                    {
                        kvp.Key.Score += kvp.Value;
                    }
                }
            }

            // 再ソート（ボーナス適用後）
            actions.Sort((a, b) => b.Score.CompareTo(a.Score));

            int logCount = Mathf.Min(3, actions.Count);
            for (int i = 0; i < logCount; i++)
            {
                var a = actions[i];
                string info = a.ActionType == AIActionType.Build ? $"({a.Facility})"
                    : a.ActionType == AIActionType.Summon ? $"({a.SummonKind})"
                    : a.ActionType == AIActionType.SkillUse ? $"({a.Unit?.kind}'{a.Skill?.Name}')"
                    : a.Unit != null ? $"({a.Unit.kind})" : "";
                string targetInfo = a.TargetUnit != null ? $"→{a.TargetUnit.kind}" : "";
                string roleInfo = (_threatLevel.UseRoleAssignment && a.Unit != null)
                    ? $" role={_roleAssigner.GetRole(a.Unit)}" : "";
                Debug.Log($"[AICommander] 候補{i + 1}: {a.ActionType}{info}{targetInfo}{roleInfo}  " +
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

            bool success = _actionExecutor.Execute(bestAction, _board);
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

            // 機械学習AI: 成功した行動を記録（師団長制では無効）
            if (!_hierarchicalMode && AIConfig.IsMLEnabled)
                _mlIntegration.RecordAction(bestAction, _board, true, _turnCount);

            if (bestAction.Unit != null)
            {
                _actedUnits.Add(bestAction.Unit);

                // 位置履歴を記録（振動防止用）— 移動系行動のみ
                if (bestAction.ActionType == AIActionType.Move
                    || bestAction.ActionType == AIActionType.Retreat
                    || bestAction.ActionType == AIActionType.Support
                    || bestAction.ActionType == AIActionType.Surround
                    || bestAction.ActionType == AIActionType.DefenseRepos)
                {
                    var cellInt = AIBoardState.ToCell(bestAction.TargetPos);
                    if (!_unitPositionHistory.ContainsKey(bestAction.Unit))
                        _unitPositionHistory[bestAction.Unit] = new List<Vector3Int>();
                    _unitPositionHistory[bestAction.Unit].Add(cellInt);
                    if (_unitPositionHistory[bestAction.Unit].Count > 4)
                        _unitPositionHistory[bestAction.Unit].RemoveAt(0);
                }
            }
        }

        // ================================================================
        //  ★ 建築後手フェーズ: メインループ後にAPが残っていて
        //  まだ1棟も建てていない場合は再度建築を試みる
        //  30ターン以降は経済充足でも実行（上位施設を建てるため）
        // ================================================================
        int lateBuilds = _buildPlanner.TryLateBuildPhase(_board, turnStats.Builds, _turnCount);
        for (int lb = 0; lb < lateBuilds; lb++) turnStats.Record(AIActionType.Build);

        // 撃破数を executor から同期
        _totalKills = _actionExecutor.TotalKills;

        _totalStats.Moves += turnStats.Moves;
        _totalStats.Attacks += turnStats.Attacks;
        _totalStats.Skills += turnStats.Skills;
        _totalStats.Retreats += turnStats.Retreats;
        _totalStats.Builds += turnStats.Builds;
        _totalStats.Summons += turnStats.Summons;
        Debug.Log($"--- [AICommander] ターン{_turnCount}終了: {turnStats}  " +
                  $"残AP={_board.EnemyAP}  累計({_totalStats}/撃破{_totalKills})  " +
                  $"脅威度={_threatLevel.Level}({_threatLevel.GetTierName()}) ---");

        // 師団長制ログ出力
        if (_hierarchicalMode && _kingCommanderSystem.HasDivisions)
        {
            _kingCommanderSystem.LogTurnSummary(_turnCount);
        }
    }

    /// <summary>
    /// 試合終了時に結果を記録（脅威度の進行と学習）。
    /// Player勝利時のみ脅威度が上がり、学習データが蓄積される。
    /// </summary>
    public void RecordMatchResult(bool playerWon, MatchAnalysis analysis)
    {
        _threatLevel.RecordMatchResult(playerWon, analysis);

        // 機械学習AIの試合終了学習（師団長制では無効）
        if (!_hierarchicalMode && AIConfig.IsMLEnabled)
        {
            _mlIntegration.OnMatchEnd(playerWon, analysis);
            _mlIntegration.UpdateThreatLevel(_threatLevel.Level);
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
