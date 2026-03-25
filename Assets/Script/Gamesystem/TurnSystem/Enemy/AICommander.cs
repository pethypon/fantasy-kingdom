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
    BuildSystem _buildSystem;          // readonly 解除: ExecuteTurn() での遅延取得を可能にする
    SummonSystem _summonSystem;        // readonly 解除: 同上
    readonly FactionState _factionState;

    // ---- 新システム ----
    readonly AIRoleAssigner _roleAssigner;
    readonly AIThreatLevel _threatLevel;
    readonly AIDeterministicRandom _rng;
    readonly TurnStrategyPlanner _strategyPlanner;

    // AP予算配分（TurnStrategyPlannerが毎ターン計画）
    TurnStrategyPlanner.APBudget _apBudget;

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

        // 新システム初期化
        _roleAssigner = new AIRoleAssigner();
        _threatLevel = new AIThreatLevel(initialThreatLevel);
        _rng = new AIDeterministicRandom(randomSeed >= 0 ? randomSeed : System.Environment.TickCount);
        _strategyPlanner = new TurnStrategyPlanner();

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
        Debug.Log("=== [AICommander] ==============================");
    }

    public AIPersonality Personality => _personality;
    public AILearning Learning => _learning;
    public TurnStrategy CurrentStrategy => _currentStrategy;
    public AIThreatLevel ThreatLevel => _threatLevel;
    public AIRoleAssigner RoleAssigner => _roleAssigner;
    public TurnStrategyPlanner StrategyPlanner => _strategyPlanner;

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

    /// <summary>原料生産施設(Well,LoggingCamp,Quarry,Field,Mine)の合計棟数</summary>
    static int CountEconBuildings(AIBoardState board)
    {
        return board.GetBuildingCount(FacilityKind.Well)
             + board.GetBuildingCount(FacilityKind.LoggingCamp)
             + board.GetBuildingCount(FacilityKind.Quarry)
             + board.GetBuildingCount(FacilityKind.Field)
             + board.GetBuildingCount(FacilityKind.Mine);
    }

    /// <summary>加工施設(LumberMill,StoneWorks,Smelter,Bakery)の合計棟数</summary>
    static int CountProcessingBuildings(AIBoardState board)
    {
        return board.GetBuildingCount(FacilityKind.LumberMill)
             + board.GetBuildingCount(FacilityKind.StoneWorks)
             + board.GetBuildingCount(FacilityKind.Smelter)
             + board.GetBuildingCount(FacilityKind.Bakery);
    }

    /// <summary>経済全体の充実度判定: 原料+加工+House</summary>
    static bool IsEconomySufficient(AIBoardState board)
    {
        int raw = CountEconBuildings(board);
        int proc = CountProcessingBuildings(board);
        int house = board.GetBuildingCount(FacilityKind.House);
        // 原料4棟以上 + 加工2棟以上 + 住宅1棟以上で充実とみなす
        return raw >= 4 && proc >= 2 && house >= 1;
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

        // 経済基盤の充実度で判断（原料 + 加工 + 住宅の全体で見る）
        int econBuildingCount = CountEconBuildings(board);
        int processingCount = CountProcessingBuildings(board);
        int houseCount = board.GetBuildingCount(FacilityKind.House);
        bool hasMinimalRaw = econBuildingCount >= 2;       // 最低限の原料施設
        bool hasBasicEconomy = econBuildingCount >= 4;     // 基礎原料が充実
        bool hasProcessing = processingCount >= 1;         // 加工施設あり
        bool hasMatureEconomy = hasBasicEconomy && processingCount >= 2 && houseCount >= 1;
        bool econSufficient = IsEconomySufficient(board);

        // 原料施設が最低限もない → 経済最優先
        if (!hasMinimalRaw)
            return TurnStrategy.EconomyBuild;

        // 基礎原料が不十分 → 経済優先（ターン制限なし）
        // ※ AffordableBuildings が空でも EconomyBuild を選ぶ
        //   （建築先行フェーズやフォールバックで補完する）
        if (!hasBasicEconomy && board.BuildablePositions.Count > 0)
            return TurnStrategy.EconomyBuild;

        // 加工施設が1棟もない → 加工施設を建てる
        if (!hasProcessing && board.BuildablePositions.Count > 0)
            return TurnStrategy.EconomyBuild;

        // 住宅がなく市民不足 → 経済優先
        if (houseCount == 0 && board.EnemyResources != null && board.EnemyResources.Citizen <= 1
            && board.BuildablePositions.Count > 0)
            return TurnStrategy.EconomyBuild;

        // 経済が十分に成熟するまでBalanced（建築も並行する）
        // 軍が少ない時期もBalancedで建築+召喚を両立
        if (!econSufficient && board.TurnCount <= 20)
            return TurnStrategy.Balanced;

        if (board.AliveEnemyUnits.Count <= 6 && board.TurnCount <= 15)
            return TurnStrategy.Balanced;

        // ★ 初接敵: 敵を見つけた直後は交戦開始を優先
        if (board.IsFirstContact && board.AlivePlayerUnits.Count > 0)
            return TurnStrategy.ContactEngage;

        // ★ 索敵戦略: 敵が見えず、探索率が低い場合
        if (board.AlivePlayerUnits.Count == 0 && board.GetExplorationRatio() < 0.6f)
        {
            // 経済基盤がある程度あれば索敵に出る
            if (hasBasicEconomy)
                return TurnStrategy.ScoutSearch;
        }

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

        // BuildSystem の遅延取得（SerializeField未設定対策）
        if (_buildSystem == null)
        {
            _buildSystem = _turnGen.buildsystem;
            if (_buildSystem == null)
                _buildSystem = Object.FindFirstObjectByType<BuildSystem>();
            if (_buildSystem != null)
                Debug.Log("[AICommander] BuildSystem を遅延取得しました");
            else
                Debug.LogWarning("[AICommander] BuildSystem が見つかりません — 建築不可");
        }

        // SummonSystem の遅延取得
        if (_summonSystem == null)
        {
            _summonSystem = _turnGen.summonsystem;
            if (_summonSystem == null)
                _summonSystem = Object.FindFirstObjectByType<SummonSystem>();
            if (_summonSystem != null)
                Debug.Log("[AICommander] SummonSystem を遅延取得しました");
        }

        _board = new AIBoardState(_moveGen, _attackPoint, _apSystem, _unitSet,
            _crystalSystem, _visionGen, _buildSystem, _summonSystem, _factionState,
            _subCrystalSystem, _turnCount);

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
        Debug.Log($"[AICommander] 経済: 原料施設={CountEconBuildings(_board)}  " +
                  $"加工施設={CountProcessingBuildings(_board)}  " +
                  $"住宅={_board.GetBuildingCount(FacilityKind.House)}  " +
                  $"経済充足={IsEconomySufficient(_board)}");
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
        TryEarlyBuildPhase(ref turnStats);

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
                // 脅威度に応じた候補数と探索深度
                int candidateLimit = _threatLevel.SearchCandidateLimit;
                var topCandidates = new List<AIAction>();
                for (int i = 0; i < Mathf.Min(candidateLimit, actions.Count); i++)
                {
                    if (actions[i].ActionType != AIActionType.Wait)
                        topCandidates.Add(actions[i]);
                }

                if (topCandidates.Count > 0)
                {
                    var searchEngine = new AISearchEngine(
                        _threatLevel.SearchDepth,
                        candidateLimit,
                        _rng);
                    // シミュレーション用参照を設定（完全シミュレーション有効化）
                    searchEngine.SetSimulationReferences(_moveGen, _unitSet, _crystalSystem, _apSystem);

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

        // ================================================================
        //  ★ 建築後手フェーズ: メインループ後にAPが残っていて
        //  まだ1棟も建てていない場合は再度建築を試みる
        //  30ターン以降は経済充足でも実行（上位施設を建てるため）
        // ================================================================
        bool shouldPostBuild = turnStats.Builds == 0 && _board.EnemyAP >= 3
            && (!IsEconomySufficient(_board) || _turnCount >= 30);
        if (shouldPostBuild)
        {
            Debug.Log($"[AI-Build] メインループで建築0棟 → 後手建築フェーズ (ターン{_turnCount})");
            _board.Refresh();
            int lateBuilds = ForceDirectBuild(ref turnStats);
            Debug.Log($"[AI-Build] 後手建築フェーズ: {lateBuilds}棟建築");
        }

        _totalStats.Moves += turnStats.Moves;
        _totalStats.Attacks += turnStats.Attacks;
        _totalStats.Skills += turnStats.Skills;
        _totalStats.Retreats += turnStats.Retreats;
        _totalStats.Builds += turnStats.Builds;
        _totalStats.Summons += turnStats.Summons;
        Debug.Log($"--- [AICommander] ターン{_turnCount}終了: {turnStats}  " +
                  $"残AP={_board.EnemyAP}  累計({_totalStats}/撃破{_totalKills})  " +
                  $"脅威度={_threatLevel.Level}({_threatLevel.GetTierName()}) ---");
    }

    /// <summary>
    /// 試合終了時に結果を記録（脅威度の進行と学習）。
    /// Player勝利時のみ脅威度が上がり、学習データが蓄積される。
    /// </summary>
    public void RecordMatchResult(bool playerWon, MatchAnalysis analysis)
    {
        _threatLevel.RecordMatchResult(playerWon, analysis);
    }

    // ================================================================
    //  戦略フォールバック: 現戦略が行き詰まった時に別の戦略を試す
    // ================================================================
    bool TryFallbackStrategy()
    {
        // フォールバック優先順
        TurnStrategy[] fallbackOrder = {
            TurnStrategy.Balanced,
            TurnStrategy.ContactEngage,
            TurnStrategy.ScoutSearch,
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

            // AP予算を新戦略に合わせて再計画
            var newDecision = _strategyPlanner.DecideStrategy(_board, _personality, _threatLevel, _turnCount);
            _apBudget = newDecision.Budget;

            // 戦略変更時にロール再割当
            if (_threatLevel.UseRoleAssignment)
                _roleAssigner.AssignRoles(_board, _currentStrategy, _personality);

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

        // TurnStrategyPlannerが計画したAP予約があればそれを基準にする
        int reserved = _apBudget.ReservedAP;

        // 追加チェック: 建てるべき建物のAPコストも考慮
        if (_board.BuildablePositions.Count > 0)
        {
            int cheapestNeeded = GetCheapestNeededBuildAP();
            if (cheapestNeeded > 0)
                reserved = Mathf.Max(reserved, cheapestNeeded);

            if (_board.AffordableBuildings.Count > 0)
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

    /// <summary>
    /// 経済状況に応じて「建てるべき建物」の最安APコストを返す。
    /// AffordableBuildings と独立して、施設不足を診断しAPを確保する。
    /// </summary>
    int GetCheapestNeededBuildAP()
    {
        if (_board.EnemyResources == null) return 0;

        int cheapest = int.MaxValue;

        // 原料施設が4棟未満 → 安い原料施設のAP分を予約
        int rawCount = CountEconBuildings(_board);
        if (rawCount < 4)
        {
            // Well(3), Field(3), LoggingCamp(4), Quarry(4)
            cheapest = Mathf.Min(cheapest, 3);
        }

        // 加工施設が0 → 加工施設のAP分を予約
        int procCount = CountProcessingBuildings(_board);
        if (procCount == 0 && rawCount >= 2)
        {
            // LumberMill(6), StoneWorks(6), Bakery(5)
            cheapest = Mathf.Min(cheapest, 5);
        }

        // 住宅なし & 市民不足
        if (_board.GetBuildingCount(FacilityKind.House) == 0
            && _board.EnemyResources.Citizen <= 1)
        {
            cheapest = Mathf.Min(cheapest, 7); // House costs 7 AP
        }

        return cheapest < int.MaxValue ? cheapest : 0;
    }

    // ================================================================
    //  建築先行フェーズ: 経済未成熟時、メインループの前に建築を優先実行
    //  移動でAPを使い切る前に、最低1棟は建てるようにする
    // ================================================================
    void TryEarlyBuildPhase(ref TurnStats turnStats)
    {
        Debug.Log($"[AI-Build] === 先行建築フェーズ開始 === AP={_board.EnemyAP} ターン={_turnCount}");

        // 30ターン以降は常に建築を試みる（経済充足でも上位施設が必要）
        bool forceByTurn = _turnCount >= 30;

        // 経済が十分に成熟していれば先行建築不要（30ターン以降は例外）
        if (IsEconomySufficient(_board) && !forceByTurn)
        {
            Debug.Log("[AI-Build] 経済充足 → 先行建築スキップ");
            return;
        }

        // 戦闘中は建築抑制（ただし30ターン以降は1棟だけ許可）
        if (_currentStrategy == TurnStrategy.CrystalDefense && !forceByTurn)
        {
            Debug.Log("[AI-Build] クリスタル防衛中 → 先行建築スキップ");
            return;
        }
        if (_currentStrategy == TurnStrategy.ContactEngage && !forceByTurn)
        {
            Debug.Log("[AI-Build] 交戦開始中 → 先行建築スキップ");
            return;
        }

        Debug.Log($"[AI-Build] BuildablePositions={_board.BuildablePositions.Count}  " +
                  $"AffordableBuildings={_board.AffordableBuildings.Count}  " +
                  $"({string.Join(",", _board.AffordableBuildings)})");

        // 建築可能条件チェック → スコア評価パスを試行
        int earlyBuilds = 0;
        const int maxEarlyBuilds = 2;

        if (_board.BuildablePositions.Count > 0 && _board.AffordableBuildings.Count > 0)
        {
            // 建築候補のみを生成・評価
            var buildActions = new List<AIAction>();
            AIActionEvaluator.GenerateBuildCandidatesPublic(_board, buildActions);
            AIActionEvaluator.GenerateSubCrystalCandidatesPublic(_board, buildActions);

            Debug.Log($"[AI-Build] 生成された建築候補数={buildActions.Count}");

            if (buildActions.Count > 0)
            {
                // スコア付け
                foreach (var action in buildActions)
                {
                    action.Score = AIActionEvaluator.CalcBuildScorePublic(action, _personality, _board, _learning);
                }

                // 戦略ボーナス適用
                foreach (var action in buildActions)
                {
                    if (_currentStrategy == TurnStrategy.EconomyBuild)
                    {
                        action.Score += 30f;
                        if (IsMissingCoreFacility(action.Facility))
                            action.Score += 40f;
                    }
                    else if (_currentStrategy == TurnStrategy.Balanced)
                    {
                        action.Score += 15f;
                    }
                }

                // スコア降順ソート
                buildActions.Sort((a, b) => b.Score.CompareTo(a.Score));

                // 上位3つをログ出力
                for (int i = 0; i < Mathf.Min(3, buildActions.Count); i++)
                {
                    var ba = buildActions[i];
                    Debug.Log($"[AI-Build] 候補{i+1}: {ba.Facility} score={ba.Score:F1} AP={ba.APCost} pos={ba.TargetPos}");
                }

                foreach (var action in buildActions)
                {
                    if (earlyBuilds >= maxEarlyBuilds) break;
                    if (action.APCost > _board.EnemyAP)
                    {
                        Debug.Log($"[AI-Build] AP不足でスキップ: {action.Facility} APコスト={action.APCost} 残AP={_board.EnemyAP}");
                        continue;
                    }

                    Debug.Log($"[AI-Build] 建築試行: {action.Facility} @{action.TargetPos} score={action.Score:F1}");
                    bool success = ExecuteAction(action);
                    if (success)
                    {
                        earlyBuilds++;
                        turnStats.Record(action.ActionType);
                        _board.Refresh();
                        Debug.Log($"[AI-Build] ★先行建築{earlyBuilds}成功: {action.Facility} 残AP={_board.EnemyAP}");
                    }
                    else
                    {
                        Debug.Log($"[AI-Build] ✗先行建築失敗: {action.Facility} @{action.TargetPos}");
                    }
                }
            }
        }

        // ================================================================
        //  直接建築フォールバック: スコアパイプラインが0棟しか建てられなかった場合
        //  BuildSystem.AIPlaceBuilding を直接呼び出して確実に建てる
        // ================================================================
        if (earlyBuilds == 0 && _buildSystem != null && _board.EnemyAP >= 3)
        {
            Debug.Log("[AI-Build] スコアパスで建築0棟 → 直接建築フォールバック開始");
            earlyBuilds += ForceDirectBuild(ref turnStats);
        }

        Debug.Log($"[AI-Build] === 先行建築フェーズ終了: {earlyBuilds}棟建築  残AP={_board.EnemyAP} ===");
    }

    /// <summary>
    /// 直接建築フォールバック: スコアリングを完全にバイパスし、
    /// 最も安価な建物を領地内の最初の空き位置に直接配置する。
    /// try-catch で例外が出ても安全に継続する。
    /// </summary>
    int ForceDirectBuild(ref TurnStats turnStats)
    {
        if (_buildSystem == null)
        {
            Debug.LogWarning("[AI-ForceBuild] _buildSystem==null → 建築不可");
            return 0;
        }
        if (_factionState == null)
        {
            Debug.LogWarning("[AI-ForceBuild] _factionState==null → 建築不可");
            return 0;
        }

        try
        {
            return ForceDirectBuildInternal(ref turnStats);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AI-ForceBuild] 例外発生: {e.Message}\n{e.StackTrace}");
            return 0;
        }
    }

    int ForceDirectBuildInternal(ref TurnStats turnStats)
    {
        int built = 0;

        // 建てるべき施設を優先順に列挙
        FacilityKind[] buildOrder = {
            FacilityKind.Well,          // AP=3  上流不要
            FacilityKind.LoggingCamp,   // AP=4  上流不要
            FacilityKind.Quarry,        // AP=4  上流不要
            FacilityKind.Field,         // AP=3  Well必要
            FacilityKind.Mine,          // AP=7  上流不要
            FacilityKind.House,         // AP=7  上流不要
            FacilityKind.LumberMill,    // AP=6  LoggingCamp必要
            FacilityKind.StoneWorks,    // AP=6  Quarry必要
            FacilityKind.Bakery,        // AP=5  Field+Well必要
            FacilityKind.Smelter,       // AP=7  Mine必要
            FacilityKind.Warehouse,     // AP=7  上流不要
            FacilityKind.Barracks,      // AP=10 上流不要
        };

        // 建築可能位置を直接取得
        var positions = _buildSystem.AIGetBuildablePositions(Team.Enemy);
        Debug.Log($"[AI-ForceBuild] 建築可能位置数={positions.Count}  AP={_apSystem.GetAP(Team.Enemy)}");

        if (positions.Count == 0)
        {
            Debug.LogWarning("[AI-ForceBuild] 建築可能位置がゼロ！ 領地が存在しないか全て埋まっている");
            return 0;
        }

        // 資源状態をログ出力
        var res = _factionState.EnemyResources;
        if (res != null)
        {
            Debug.Log($"[AI-ForceBuild] 資源: Wood={res.Wood} Stone={res.Stone} Water={res.Water} " +
                      $"Plank={res.Plank} CutStone={res.CutStone} Iron={res.Iron} " +
                      $"Bread={res.Bread} Citizen={res.Citizen}");
        }

        foreach (var facility in buildOrder)
        {
            if (built >= 3) break; // 最大3棟まで

            int currentAP = _apSystem.GetAP(Team.Enemy);
            if (currentAP < 3) break; // AP=3未満なら何も建てられない

            if (!FacilityData.Table.TryGetValue(facility, out var info)) continue;

            // AP足りるか
            if (currentAP < info.APCost) continue;

            // 資源足りるか（CanBuildはAP+資源の両方をチェック）
            if (!_apSystem.CanBuild(Team.Enemy, facility, _factionState)) continue;

            // 上流施設チェック（加工施設は上流が必要）
            if (_board != null && !_board.HasUpstreamProducer(facility)) continue;

            // 同種の既存数チェック（初回は上限緩く、ターン30以降は複数OK）
            int existing = _board != null ? _board.GetBuildingCount(facility) : 0;
            int maxForForce = _turnCount >= 30 ? 3 : 1;
            if (facility == FacilityKind.House) maxForForce = 3;
            if (existing >= maxForForce) continue;

            // 各位置で建築を試みる（最初の1位置だけログ詳細）
            bool placed = false;
            for (int i = 0; i < positions.Count; i++)
            {
                var pos = positions[i];
                bool success = _buildSystem.AIPlaceBuilding(pos, facility, Team.Enemy);
                if (success)
                {
                    built++;
                    turnStats.Record(AIActionType.Build);
                    if (_board != null) _board.Refresh();
                    Debug.Log($"[AI-ForceBuild] ★★ {facility} @({pos.x},{pos.y},{pos.z}) 建築成功! " +
                              $"残AP={_apSystem.GetAP(Team.Enemy)} (今ターン{built}棟目)");
                    placed = true;
                    // 成功後に位置リストを再取得
                    positions = _buildSystem.AIGetBuildablePositions(Team.Enemy);
                    break;
                }
            }

            if (!placed)
            {
                Debug.Log($"[AI-ForceBuild] {facility}: 全{positions.Count}位置で失敗");
            }
        }

        if (built == 0)
        {
            Debug.LogWarning("[AI-ForceBuild] 全施設の建築に失敗！ CanBuildまたはAICheckCanPlaceの問題");
        }

        return built;
    }

    /// <summary>コア施設（原料+加工+住宅）が不足しているかチェック</summary>
    bool IsMissingCoreFacility(FacilityKind facility)
    {
        switch (facility)
        {
            case FacilityKind.Well:
            case FacilityKind.LoggingCamp:
            case FacilityKind.Quarry:
            case FacilityKind.Field:
                return _board.GetBuildingCount(facility) == 0;
            case FacilityKind.LumberMill:
            case FacilityKind.StoneWorks:
            case FacilityKind.Bakery:
            case FacilityKind.Smelter:
                return _board.GetBuildingCount(facility) == 0;
            case FacilityKind.House:
                return _board.GetBuildingCount(FacilityKind.House) == 0;
            case FacilityKind.Mine:
                return _board.GetBuildingCount(FacilityKind.Mine) == 0;
            default:
                return false;
        }
    }

    // ================================================================
    //  AP予約ペナルティ: 移動系が予約APを食い込む場合に減点
    // ================================================================
    void ApplyAPReservationPenalty(List<AIAction> actions, int reservedAP)
    {
        if (reservedAP <= 0) return;

        // 経済が未成熟かどうかで重みを変える
        bool econWeak = !IsEconomySufficient(_board);

        foreach (var action in actions)
        {
            // 建築・召喚・サブクリスタルは予約対象なのでペナルティなし
            if (action.ActionType == AIActionType.Build
                || action.ActionType == AIActionType.Summon
                || action.ActionType == AIActionType.SubCrystal)
                continue;

            int apAfterAction = _board.EnemyAP - action.APCost;

            // 攻撃は高価値なので軽いペナルティのみ
            if (action.ActionType == AIActionType.Attack
                || action.ActionType == AIActionType.SkillUse)
            {
                if (apAfterAction < reservedAP)
                    action.Score -= 8f;
                continue;
            }

            // 移動系: AP予約を食い込む場合は強く減点
            if (apAfterAction < reservedAP)
            {
                // 経済未成熟時は非常に強いペナルティ（建築を移動より優先させる）
                float penalty = econWeak ? 60f : 20f;
                // 30ターン以降は更に厳しく
                if (_turnCount >= 30 && econWeak) penalty = 100f;
                action.Score -= penalty;
            }

            // 経済未成熟時: AP予約に関係なく全移動を減点（建築の相対的優位を確保）
            if (econWeak && _turnCount >= 10)
            {
                action.Score -= 15f;
                if (_turnCount >= 30) action.Score -= 30f;
            }
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
        // 有効な候補をスコア順に収集
        var validActions = new List<(AIAction action, float score)>();

        foreach (var action in actions)
        {
            if (action.ActionType == AIActionType.Wait) continue;
            if (action.APCost > _board.EnemyAP) continue;

            string failKey = $"{action.ActionType}_{action.Facility}_{action.SummonKind}_{action.TargetPos}";
            if (failedActions.Contains(failKey)) continue;

            if (failedActionTypes != null)
            {
                string typeKey = $"{action.ActionType}_{action.Facility}_{action.SummonKind}";
                if (failedActionTypes.Contains(typeKey)) continue;
            }

            float score = action.Score;
            if (action.Unit != null && _actedUnits.Contains(action.Unit))
                score *= 0.5f;

            validActions.Add((action, score));
        }

        if (validActions.Count == 0) return null;

        // ミス率: 一定確率で最善手以外を選択する（チュートリアル〜ノーマル帯）
        float mistakeRate = _threatLevel.MistakeRate;
        if (mistakeRate > 0f && validActions.Count > 1 && _rng != null && _rng.NextFloat() < mistakeRate)
        {
            // 上位25-75%の範囲からランダムに選択（完全ランダムではなくそこそこの手を選ぶ）
            validActions.Sort((a, b) => b.score.CompareTo(a.score));
            int minIdx = Mathf.Max(1, validActions.Count / 4);
            int maxIdx = Mathf.Min(validActions.Count - 1, validActions.Count * 3 / 4);
            int idx = _rng.Range(minIdx, maxIdx + 1);
            return validActions[idx].action;
        }

        // 通常: 最高スコアの行動を選択
        AIAction best = null;
        float bestScore = float.MinValue;
        foreach (var (action, score) in validActions)
        {
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

        // ★ 学習記録: PlayerCrystalVisible チェック必須
        if (_learning.IsActive && _board.CanUsePlayerCrystalAsTarget())
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
        if (_buildSystem == null)
        {
            Debug.LogWarning("[AI-Build] ExecuteBuild: _buildSystem==null!");
            return false;
        }

        var pos = AIBoardState.ToCell(action.TargetPos);
        int apBefore = _apSystem.GetAP(Team.Enemy);

        Debug.Log($"[AI-Build] ExecuteBuild: {action.Facility} @({pos.x},{pos.y},{pos.z}) AP={apBefore}");

        bool success = _buildSystem.AIPlaceBuilding(pos, action.Facility, Team.Enemy);
        if (success)
        {
            _board.RefreshAP();
            Debug.Log($"[AI-Build] ★建築成功: {action.Facility} @({pos.x},{pos.y},{pos.z})  残AP={_board.EnemyAP}");
        }
        else
        {
            Debug.LogWarning($"[AI-Build] ✗建築失敗: {action.Facility} @({pos.x},{pos.y},{pos.z})  " +
                             $"CanBuild={_apSystem.CanBuild(Team.Enemy, action.Facility, _factionState)}");
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
