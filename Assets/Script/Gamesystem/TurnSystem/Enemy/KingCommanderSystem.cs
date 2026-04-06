using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  KingCommanderSystem — 王 + 師団長の階層指揮システム
//
//  1ターン処理フロー:
//  1. 王が全体戦略を決定する。
//  2. 王が師団長を選出し、兵を割り当てる。
//     - 王直轄は最大10体
//     - 各師団は最大4体
//  3. 各師団長が担当兵で試行し、行動案を王へ提出する。
//  4. 王が全提案を受領し、以下を実施する。
//     - 競合解決（同ユニット・同マス）
//     - 予算予約（AP/資源）
//     - 王戦略との整合評価
//  5. 王が採択した行動のみ実行する。
//  6. 不採用案は理由をログ出力する。
// =====================================================================
public class KingCommanderSystem
{
    public const int MaxDivisions = 3;
    public const int MaxKingDirectUnits = 10;
    public const int MaxDivisionUnits = 4;

    /// <summary>王の性格（AICommander のものを参照）</summary>
    readonly AIPersonality _kingPersonality;

    /// <summary>師団長リスト（最大3体）</summary>
    readonly List<DivisionCommander> _divisions = new List<DivisionCommander>();

    /// <summary>王の直轄兵</summary>
    readonly List<Status> _kingDirectUnits = new List<Status>();

    /// <summary>乱数シード管理</summary>
    readonly AIDeterministicRandom _rng;

    /// <summary>今ターンの全提案ログ</summary>
    readonly List<DivisionProposal> _turnProposals = new List<DivisionProposal>();

    /// <summary>採択済みユニット（競合解決用）</summary>
    readonly HashSet<Status> _reservedUnits = new HashSet<Status>();

    /// <summary>採択済みマス（競合解決用）</summary>
    readonly HashSet<Vector3Int> _reservedTiles = new HashSet<Vector3Int>();

    /// <summary>予約済みAPコスト</summary>
    int _reservedAP;

    /// <summary>師団長が有効か</summary>
    public bool HasDivisions => _divisions.Count > 0;

    /// <summary>師団長リスト（読取専用）</summary>
    public IReadOnlyList<DivisionCommander> Divisions => _divisions;

    /// <summary>王の直轄兵（読取専用）</summary>
    public IReadOnlyList<Status> KingDirectUnits => _kingDirectUnits;

    /// <summary>今ターンの提案ログ（読取専用）</summary>
    public IReadOnlyList<DivisionProposal> TurnProposals => _turnProposals;

    // ---- 生成 ----
    public KingCommanderSystem(AIPersonality kingPersonality, AIDeterministicRandom rng)
    {
        _kingPersonality = kingPersonality;
        _rng = rng;
        Debug.Log("[KingCommanderSystem] 階層指揮システム初期化完了");
    }

    // ================================================================
    //  師団長の選出
    //  王が師団長を選出する。性格はランダムに決定。
    //  ユニット数が少ない場合は師団数を減らす。
    // ================================================================
    public void SelectDivisionCommanders(List<Status> allEnemyUnits, int turnCount)
    {
        _divisions.Clear();

        // ユニット数に基づいて師団数を決定
        // 少なくとも5体(王直轄1+師団1の最低限)以上が必要
        int availableUnits = CountAvailableUnits(allEnemyUnits);
        if (availableUnits < 5)
        {
            Debug.Log($"[KingCommanderSystem] ユニット{availableUnits}体 → 師団なし（全て王直轄）");
            return;
        }

        // 師団数: ユニット数に応じて 1-3
        int divisionCount;
        if (availableUnits >= 14) divisionCount = 3;       // 14体以上: 3師団
        else if (availableUnits >= 9) divisionCount = 2;   // 9体以上: 2師団
        else divisionCount = 1;                             // 5体以上: 1師団

        divisionCount = Mathf.Min(divisionCount, MaxDivisions);

        for (int i = 0; i < divisionCount; i++)
        {
            int seed = _rng.Next() + i * 1000 + turnCount;
            var major = AIPersonality.RandomMajor(seed);
            var division = new DivisionCommander(i, major, seed + 7);
            _divisions.Add(division);
        }

        Debug.Log($"[KingCommanderSystem] 師団長{_divisions.Count}体を選出");
    }

    // ================================================================
    //  兵の割り当て
    //  王直轄は最大10体、各師団は最大4体。
    //  割り当てはユニットの種類・位置に基づく。
    // ================================================================
    public void AssignUnits(List<Status> allEnemyUnits, AIBoardState board)
    {
        _kingDirectUnits.Clear();

        // 有効なユニットを収集
        var available = new List<Status>();
        foreach (var u in allEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (u.type != Type.Unit) continue;
            if (StatusEffectSystem.IsStunned(u)) continue;
            available.Add(u);
        }

        if (_divisions.Count == 0)
        {
            // 師団なし: 全て王直轄（最大10体）
            int count = Mathf.Min(available.Count, MaxKingDirectUnits);
            for (int i = 0; i < count; i++)
                _kingDirectUnits.Add(available[i]);
            return;
        }

        // ---- 戦略的な兵の割り当て ----
        // BOSSと重要ユニットは王直轄に保持
        var forKing = new List<Status>();
        var forDivisions = new List<Status>();

        foreach (var u in available)
        {
            // BOSS、King、Crystal は王直轄
            if (u.IsBoss || u.kind == Kind.King || u.kind == Kind.Crystal)
            {
                forKing.Add(u);
            }
            // Priest(回復役) は王直轄で管理
            else if (u.kind == Kind.Priest)
            {
                forKing.Add(u);
            }
            else
            {
                forDivisions.Add(u);
            }
        }

        // 師団への割り当て
        int unitsPerDivision = Mathf.Min(MaxDivisionUnits,
            Mathf.Max(1, forDivisions.Count / _divisions.Count));

        int assignedIndex = 0;
        foreach (var division in _divisions)
        {
            var divUnits = new List<Status>();
            int toAssign = Mathf.Min(unitsPerDivision, MaxDivisionUnits);
            for (int i = 0; i < toAssign && assignedIndex < forDivisions.Count; i++)
            {
                divUnits.Add(forDivisions[assignedIndex]);
                assignedIndex++;
            }
            division.AssignUnits(divUnits);
        }

        // 残りは王直轄
        for (int i = assignedIndex; i < forDivisions.Count; i++)
            forKing.Add(forDivisions[i]);

        // 王直轄の上限チェック
        int kingCount = Mathf.Min(forKing.Count, MaxKingDirectUnits);
        for (int i = 0; i < kingCount; i++)
            _kingDirectUnits.Add(forKing[i]);

        Debug.Log($"[KingCommanderSystem] 兵割当: 王直轄={_kingDirectUnits.Count}  " +
                  $"師団={string.Join(", ", GetDivisionUnitCounts())}");
    }

    // ================================================================
    //  師団長の提案を収集
    // ================================================================
    public List<DivisionProposal> CollectProposals(
        AIBoardState board,
        AILearning learning,
        TurnStrategy kingStrategy,
        AIRoleAssigner roleAssigner,
        AIThreatLevel threatLevel)
    {
        _turnProposals.Clear();

        foreach (var division in _divisions)
        {
            var proposal = division.GenerateProposal(
                board, learning, kingStrategy, roleAssigner, threatLevel);
            _turnProposals.Add(proposal);
        }

        Debug.Log($"[KingCommanderSystem] {_turnProposals.Count}件の提案を受領");
        return _turnProposals;
    }

    // ================================================================
    //  王による採択処理
    //  採択前の必須処理:
    //  1. 競合解決（同一ユニット重複利用不可、同一マス移動衝突不可）
    //  2. 予算予約（AP予算を先に確保、資源予算を先に確保）
    //
    //  採択の優先順位:
    //  1. 防衛成立（敗北回避）
    //  2. 戦略整合（王方針への一致）
    //  3. AP/資源効率
    //  4. 期待スコア
    // ================================================================
    public List<AIAction> EvaluateAndAcceptProposals(
        List<DivisionProposal> proposals,
        AIBoardState board,
        TurnStrategy kingStrategy,
        int totalAvailableAP)
    {
        _reservedUnits.Clear();
        _reservedTiles.Clear();
        _reservedAP = 0;

        var acceptedActions = new List<AIAction>();

        // ---- 提案をスコア順にソート（高スコア優先） ----
        var sortedProposals = new List<DivisionProposal>(proposals);
        sortedProposals.Sort((a, b) =>
        {
            // 防衛提案を最優先
            float aPriority = GetStrategyAlignmentScore(a, kingStrategy);
            float bPriority = GetStrategyAlignmentScore(b, kingStrategy);
            int cmp = bPriority.CompareTo(aPriority);
            if (cmp != 0) return cmp;
            return b.TotalScore.CompareTo(a.TotalScore);
        });

        Debug.Log($"[KingCommander] === 採択処理開始 === 利用可能AP={totalAvailableAP}  " +
                  $"戦略={kingStrategy}  提案数={sortedProposals.Count}");

        foreach (var proposal in sortedProposals)
        {
            // ---- 1. 戦略整合チェック ----
            float alignmentScore = GetStrategyAlignmentScore(proposal, kingStrategy);
            if (alignmentScore < -10f)
            {
                RejectProposal(proposal, RejectionReason.StrategyMismatch,
                    $"王戦略{kingStrategy}に不適合(整合度={alignmentScore:F1})");
                continue;
            }

            // ---- 2. AP予算チェック ----
            if (_reservedAP + proposal.TotalAPCost > totalAvailableAP)
            {
                // 個別アクション単位で部分採択を試みる
                AcceptPartialProposal(proposal, board, kingStrategy, totalAvailableAP);
                continue;
            }

            // ---- 3. 競合解決 ----
            var conflictResult = CheckConflicts(proposal);
            if (conflictResult.HasConflict)
            {
                if (conflictResult.IsUnitConflict)
                {
                    // 部分採択: 競合しないアクションのみ採択
                    AcceptPartialProposal(proposal, board, kingStrategy, totalAvailableAP);
                }
                else
                {
                    RejectProposal(proposal, RejectionReason.ConflictTile,
                        $"マス衝突: {conflictResult.Detail}");
                    // タイル衝突でも部分採択を試みる
                    AcceptPartialProposal(proposal, board, kingStrategy, totalAvailableAP);
                }
                continue;
            }

            // ---- 4. 期待スコアチェック ----
            if (proposal.TotalScore < 5f && proposal.Actions.Count > 0)
            {
                RejectProposal(proposal, RejectionReason.LowScore,
                    $"期待スコア低(score={proposal.TotalScore:F1})");
                continue;
            }

            // ---- 採択 ----
            AcceptProposal(proposal, acceptedActions);

            Debug.Log($"[KingCommander] 採択: {proposal}  整合度={alignmentScore:F1}");
        }

        Debug.Log($"[KingCommander] === 採択処理完了 === " +
                  $"採択行動数={acceptedActions.Count}  予約AP={_reservedAP}/{totalAvailableAP}");

        return acceptedActions;
    }

    // ================================================================
    //  提案の部分採択（競合やAP超過時、アクション単位で採択）
    // ================================================================
    void AcceptPartialProposal(DivisionProposal proposal, AIBoardState board,
        TurnStrategy kingStrategy, int totalAvailableAP)
    {
        int acceptedCount = 0;

        foreach (var action in proposal.Actions)
        {
            // ユニット競合チェック
            if (action.Unit != null && _reservedUnits.Contains(action.Unit))
            {
                Debug.Log($"[KingCommander] 部分不採用(ConflictUnit): {action}");
                continue;
            }

            // マス競合チェック
            var targetCell = AIBoardState.ToCell(action.TargetPos);
            bool isMoveAction = action.ActionType == AIActionType.Move
                || action.ActionType == AIActionType.Retreat
                || action.ActionType == AIActionType.Support
                || action.ActionType == AIActionType.Surround
                || action.ActionType == AIActionType.DefenseRepos;
            if (isMoveAction && _reservedTiles.Contains(targetCell))
            {
                Debug.Log($"[KingCommander] 部分不採用(ConflictTile): {action}");
                continue;
            }

            // AP予算チェック
            if (_reservedAP + action.APCost > totalAvailableAP)
            {
                Debug.Log($"[KingCommander] 部分不採用(BudgetAP): {action} " +
                          $"(予約AP={_reservedAP}+{action.APCost}>{totalAvailableAP})");
                continue;
            }

            // 予約登録
            if (action.Unit != null)
                _reservedUnits.Add(action.Unit);
            if (isMoveAction)
                _reservedTiles.Add(targetCell);
            _reservedAP += action.APCost;
            acceptedCount++;
        }

        if (acceptedCount == 0)
        {
            if (!proposal.Accepted && proposal.Rejection == null)
            {
                RejectProposal(proposal, RejectionReason.ConflictUnit,
                    "全アクションが競合/予算超過で不採用");
            }
        }
        else
        {
            proposal.Accepted = true;
            Debug.Log($"[KingCommander] 部分採択: {proposal.DivisionName} " +
                      $"{acceptedCount}/{proposal.Actions.Count}件");
        }
    }

    // ================================================================
    //  提案の完全採択
    // ================================================================
    void AcceptProposal(DivisionProposal proposal, List<AIAction> acceptedActions)
    {
        proposal.Accepted = true;

        foreach (var action in proposal.Actions)
        {
            acceptedActions.Add(action);

            if (action.Unit != null)
                _reservedUnits.Add(action.Unit);

            var targetCell = AIBoardState.ToCell(action.TargetPos);
            bool isMoveAction = action.ActionType == AIActionType.Move
                || action.ActionType == AIActionType.Retreat
                || action.ActionType == AIActionType.Support
                || action.ActionType == AIActionType.Surround
                || action.ActionType == AIActionType.DefenseRepos;
            if (isMoveAction)
                _reservedTiles.Add(targetCell);

            _reservedAP += action.APCost;
        }
    }

    // ================================================================
    //  不採用処理 + ログ出力
    // ================================================================
    void RejectProposal(DivisionProposal proposal, RejectionReason reason, string detail)
    {
        proposal.Accepted = false;
        proposal.Rejection = reason;
        proposal.RejectionDetail = detail;

        Debug.Log($"[KingCommander] 不採用: {proposal.DivisionName}  " +
                  $"理由={reason}  詳細=\"{detail}\"  " +
                  $"提案内容=[{proposal}]");
    }

    // ================================================================
    //  競合チェック
    // ================================================================
    struct ConflictCheckResult
    {
        public bool HasConflict;
        public bool IsUnitConflict;
        public string Detail;
    }

    ConflictCheckResult CheckConflicts(DivisionProposal proposal)
    {
        var result = new ConflictCheckResult();

        // ユニット競合
        foreach (var unit in proposal.UsedUnits)
        {
            if (_reservedUnits.Contains(unit))
            {
                result.HasConflict = true;
                result.IsUnitConflict = true;
                result.Detail = $"ユニット{unit.kind}が既に予約済み";
                return result;
            }
        }

        // マス競合
        foreach (var tile in proposal.UsedTiles)
        {
            if (_reservedTiles.Contains(tile))
            {
                result.HasConflict = true;
                result.IsUnitConflict = false;
                result.Detail = $"マス{tile}が既に予約済み";
                return result;
            }
        }

        return result;
    }

    // ================================================================
    //  戦略整合度スコア
    //  提案が王の戦略にどれだけ合致するかを-20〜+20の範囲で評価
    // ================================================================
    float GetStrategyAlignmentScore(DivisionProposal proposal, TurnStrategy kingStrategy)
    {
        if (proposal.Actions.Count == 0) return 0f;

        float score = 0f;
        int attacks = 0, defenses = 0, builds = 0, moves = 0;

        foreach (var a in proposal.Actions)
        {
            switch (a.ActionType)
            {
                case AIActionType.Attack:
                case AIActionType.SkillUse:
                case AIActionType.Surround: attacks++; break;
                case AIActionType.DefenseRepos:
                case AIActionType.Retreat: defenses++; break;
                case AIActionType.Build:
                case AIActionType.Summon:
                case AIActionType.SubCrystal: builds++; break;
                default: moves++; break;
            }
        }

        int total = proposal.Actions.Count;
        float attackRatio = total > 0 ? (float)attacks / total : 0f;
        float defenseRatio = total > 0 ? (float)defenses / total : 0f;
        float buildRatio = total > 0 ? (float)builds / total : 0f;

        switch (kingStrategy)
        {
            case TurnStrategy.Assault:
            case TurnStrategy.ContactEngage:
                score += attackRatio * 15f;
                score -= defenseRatio * 5f;
                break;

            case TurnStrategy.CrystalDefense:
                score += defenseRatio * 15f;
                score += attackRatio * 5f;  // 防衛中も攻撃は有用
                score -= buildRatio * 3f;
                break;

            case TurnStrategy.RetreatRegroup:
                score += defenseRatio * 15f;
                score -= attackRatio * 8f;
                break;

            case TurnStrategy.EconomyBuild:
                score += buildRatio * 15f;
                score += defenseRatio * 5f;
                break;

            case TurnStrategy.ScoutSearch:
                score += (float)moves / Mathf.Max(1, total) * 12f;
                break;

            default: // Balanced
                score += 5f; // バランス時は何でも受け入れやすい
                break;
        }

        return score;
    }

    // ================================================================
    //  全提案ログ出力（ターン終了時に呼び出し）
    // ================================================================
    public void LogTurnSummary(int turnCount)
    {
        Debug.Log($"=== [KingCommander] ターン{turnCount} 師団長制ログ ===");
        Debug.Log($"[KingCommander] 師団数={_divisions.Count}  王直轄={_kingDirectUnits.Count}体");

        int accepted = 0, rejected = 0;
        foreach (var proposal in _turnProposals)
        {
            if (proposal.Accepted)
            {
                accepted++;
                Debug.Log($"[KingCommander] 採択: {proposal}");
            }
            else
            {
                rejected++;
                Debug.Log($"[KingCommander] 不採用: {proposal.DivisionName}  " +
                          $"reason={proposal.Rejection}  detail=\"{proposal.RejectionDetail}\"");
            }
        }

        Debug.Log($"[KingCommander] 採択率: {accepted}/{accepted + rejected}  " +
                  $"予約AP={_reservedAP}");
        Debug.Log($"=== [KingCommander] ターン{turnCount} ログ終了 ===");
    }

    // ================================================================
    //  王の直轄ユニットセットを返す（AICommander の既存ループ用）
    // ================================================================
    public HashSet<Status> GetKingDirectUnitSet()
    {
        return new HashSet<Status>(_kingDirectUnits);
    }

    // ================================================================
    //  ヘルパー
    // ================================================================

    int CountAvailableUnits(List<Status> units)
    {
        int count = 0;
        foreach (var u in units)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (u.type != Type.Unit) continue;
            count++;
        }
        return count;
    }

    string[] GetDivisionUnitCounts()
    {
        var counts = new string[_divisions.Count];
        for (int i = 0; i < _divisions.Count; i++)
            counts[i] = $"{_divisions[i].Name}={_divisions[i].AssignedUnits.Count}";
        return counts;
    }
}
