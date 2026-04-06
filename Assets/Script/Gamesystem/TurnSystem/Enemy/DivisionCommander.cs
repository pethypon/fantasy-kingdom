using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  DivisionCommander — 師団長
//
//  王(KingCommanderSystem)が選出し、最大4体の兵を割り当てる。
//  担当兵で行動を試行し、行動提案(DivisionProposal)を王に提出する。
//
//  性格:
//  ・既存のAIと同じ性格モデル (MajorPersonality + PersonalityTraits)
//  ・性格により評価重みを変更する
//    - 攻撃志向(Combat): 攻撃価値を高める
//    - 防衛志向(Intellect): 防衛価値を高める
//    - 発展志向(Growth): 経済価値を高める
//    - 慎重志向(Adaptive): リスク回避を強める
// =====================================================================
public class DivisionCommander
{
    public const int MaxUnits = 4;

    /// <summary>師団インデックス (0-2)</summary>
    public int Index { get; private set; }

    /// <summary>師団長の性格</summary>
    public AIPersonality Personality { get; private set; }

    /// <summary>割り当てられた兵</summary>
    public List<Status> AssignedUnits { get; private set; } = new List<Status>();

    /// <summary>師団名（ログ用）</summary>
    public string Name => $"師団{Index}({Personality.Major})";

    // ---- 生成 ----
    public DivisionCommander(int index, MajorPersonality major, int seed)
    {
        Index = index;
        Personality = new AIPersonality(major, seed);
        Debug.Log($"[DivisionCommander] {Name} 生成完了  性格={major}  " +
                  $"慎重={Personality.Traits.Caution} 指揮={Personality.Traits.Command} " +
                  $"執着={Personality.Traits.Obsession} 防衛={Personality.Traits.Defense} " +
                  $"戦術={Personality.Traits.Tactics} 発展={Personality.Traits.Development}");
    }

    /// <summary>セーブデータからの復元用</summary>
    public DivisionCommander(int index, MajorPersonality major, PersonalityTraits traits)
    {
        Index = index;
        Personality = new AIPersonality(major, traits);
    }

    /// <summary>兵を割り当てる（毎ターン呼び出し）</summary>
    public void AssignUnits(List<Status> units)
    {
        AssignedUnits.Clear();
        int count = Mathf.Min(units.Count, MaxUnits);
        for (int i = 0; i < count; i++)
            AssignedUnits.Add(units[i]);

        Debug.Log($"[DivisionCommander] {Name} に{AssignedUnits.Count}体を割当");
    }

    /// <summary>
    /// 担当兵で行動を試行し、行動提案を王に提出する。
    /// 師団長は自分の担当ユニットだけの候補行動を生成・評価する。
    /// </summary>
    public DivisionProposal GenerateProposal(
        AIBoardState board,
        AILearning learning,
        TurnStrategy kingStrategy,
        AIRoleAssigner roleAssigner,
        AIThreatLevel threatLevel)
    {
        var proposal = new DivisionProposal
        {
            DivisionIndex = Index,
            DivisionName = Name
        };

        if (AssignedUnits.Count == 0)
        {
            proposal.Reason = "兵なし";
            return proposal;
        }

        // 師団長の性格に基づいて行動候補を評価
        // 注意: EvaluateAll は全ユニットの候補を生成するので、
        //       師団長の担当ユニットのみフィルタリングする
        var allActions = AIActionEvaluator.EvaluateAll(Personality, board, learning, kingStrategy);

        // 担当ユニットの行動のみ抽出（建築・召喚はユニット不要なので含める）
        var myActions = new List<AIAction>();
        var assignedSet = new HashSet<Status>(AssignedUnits);
        foreach (var action in allActions)
        {
            if (action.Unit == null)
            {
                // 建築・召喚等のユニット不要行動は師団長も提案可能
                myActions.Add(action);
            }
            else if (assignedSet.Contains(action.Unit))
            {
                myActions.Add(action);
            }
        }

        // ロールボーナス適用
        if (threatLevel.UseRoleAssignment)
        {
            foreach (var action in myActions)
            {
                if (action.Unit != null)
                    action.Score += roleAssigner.GetRoleBonus(action.Unit, action, board);
            }
        }

        // 性格に基づくスコア調整
        ApplyDivisionPersonalityBonus(myActions, board);

        // スコア降順ソート
        myActions.Sort((a, b) => b.Score.CompareTo(a.Score));

        // 担当ユニットごとに最良の行動を1つずつ選択（貪欲法）
        var usedUnits = new HashSet<Status>();
        var usedTiles = new HashSet<Vector3Int>();

        foreach (var action in myActions)
        {
            // 各ユニットは1行動まで
            if (action.Unit != null && usedUnits.Contains(action.Unit))
                continue;

            // 同一マス衝突チェック
            var targetCell = AIBoardState.ToCell(action.TargetPos);
            if (action.ActionType == AIActionType.Move
                || action.ActionType == AIActionType.Retreat
                || action.ActionType == AIActionType.Support
                || action.ActionType == AIActionType.Surround
                || action.ActionType == AIActionType.DefenseRepos)
            {
                if (usedTiles.Contains(targetCell))
                    continue;
            }

            // 低スコアの行動は除外
            if (action.Score < 1f && action.ActionType != AIActionType.Build
                && action.ActionType != AIActionType.Summon)
                continue;

            proposal.Actions.Add(action);
            proposal.TotalAPCost += action.APCost;
            proposal.TotalScore += action.Score;

            if (action.Unit != null)
            {
                usedUnits.Add(action.Unit);
                proposal.UsedUnits.Add(action.Unit);
            }

            if (action.ActionType == AIActionType.Move
                || action.ActionType == AIActionType.Retreat
                || action.ActionType == AIActionType.Support
                || action.ActionType == AIActionType.Surround
                || action.ActionType == AIActionType.DefenseRepos)
            {
                usedTiles.Add(targetCell);
                proposal.UsedTiles.Add(targetCell);
            }

            // 最大行動数: 担当ユニット数 + 建築/召喚余地
            if (proposal.Actions.Count >= AssignedUnits.Count + 2)
                break;
        }

        // 提案理由を生成
        proposal.Reason = GenerateProposalReason(proposal, kingStrategy);

        Debug.Log($"[DivisionCommander] {Name} 提案: {proposal}");
        return proposal;
    }

    /// <summary>師団長の性格に基づく追加スコア調整</summary>
    void ApplyDivisionPersonalityBonus(List<AIAction> actions, AIBoardState board)
    {
        foreach (var action in actions)
        {
            float bonus = 0f;

            switch (Personality.Major)
            {
                case MajorPersonality.Combat:
                    // 攻撃志向: 攻撃価値を高める
                    if (action.ActionType == AIActionType.Attack) bonus += 10f;
                    if (action.ActionType == AIActionType.SkillUse) bonus += 8f;
                    if (action.ActionType == AIActionType.Surround) bonus += 6f;
                    if (action.ActionType == AIActionType.Retreat) bonus -= 5f;
                    break;

                case MajorPersonality.Intellect:
                    // 防衛志向: 防衛価値を高める
                    if (action.ActionType == AIActionType.DefenseRepos) bonus += 10f;
                    if (action.ActionType == AIActionType.Retreat) bonus += 5f;
                    if (action.ActionType == AIActionType.Support) bonus += 8f;
                    if (action.ActionType == AIActionType.Attack) bonus -= 3f;
                    break;

                case MajorPersonality.Growth:
                    // 発展志向: 経済価値を高める
                    if (action.ActionType == AIActionType.Build) bonus += 12f;
                    if (action.ActionType == AIActionType.Summon) bonus += 10f;
                    break;

                case MajorPersonality.Adaptive:
                    // 慎重志向: リスク回避を強める
                    if (action.ActionType == AIActionType.Retreat) bonus += 8f;
                    if (action.ActionType == AIActionType.DefenseRepos) bonus += 6f;
                    if (action.ActionType == AIActionType.Support) bonus += 5f;
                    // 高リスク攻撃にはペナルティ
                    if (action.ActionType == AIActionType.Attack && action.Unit != null)
                    {
                        float hpRatio = action.Unit.MaxHP > 0
                            ? (float)action.Unit.HP / action.Unit.MaxHP : 1f;
                        if (hpRatio < 0.4f) bonus -= 10f;
                    }
                    break;
            }

            // 細かい性格による微調整
            bonus += Personality.TacticsRate * 3f *
                (action.ActionType == AIActionType.Attack ? 1f : 0f);
            bonus += Personality.DefenseRate * 3f *
                (action.ActionType == AIActionType.DefenseRepos ? 1f : 0f);
            bonus += Personality.DevelopRate * 3f *
                (action.ActionType == AIActionType.Build ? 1f : 0f);
            bonus += Personality.CautionRate * 3f *
                (action.ActionType == AIActionType.Retreat ? 1f : 0f);

            action.Score += bonus;
        }
    }

    /// <summary>提案理由を生成</summary>
    string GenerateProposalReason(DivisionProposal proposal, TurnStrategy kingStrategy)
    {
        if (proposal.Actions.Count == 0)
            return "行動候補なし";

        int attacks = 0, moves = 0, builds = 0, retreats = 0;
        foreach (var a in proposal.Actions)
        {
            switch (a.ActionType)
            {
                case AIActionType.Attack:
                case AIActionType.SkillUse: attacks++; break;
                case AIActionType.Move:
                case AIActionType.Support:
                case AIActionType.Surround: moves++; break;
                case AIActionType.Build:
                case AIActionType.Summon: builds++; break;
                case AIActionType.Retreat:
                case AIActionType.DefenseRepos: retreats++; break;
            }
        }

        string focus;
        if (attacks > moves && attacks > builds) focus = "攻撃重視";
        else if (builds > 0) focus = "建築含む";
        else if (retreats > moves) focus = "防衛重視";
        else focus = "展開重視";

        return $"{Personality.Major}性格・{focus}(攻{attacks}/動{moves}/建{builds}/退{retreats})";
    }
}
