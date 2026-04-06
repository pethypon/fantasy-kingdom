using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  RejectionReason — 不採用理由コード
//  王が師団長の提案を不採用にした理由を示す。
//  ログ出力で調整速度を大幅に向上させる。
// =====================================================================
public enum RejectionReason
{
    ConflictUnit,       // 同一ユニット重複利用
    ConflictTile,       // 同一マス移動衝突
    BudgetAP,           // AP予算超過
    BudgetResource,     // 資源予算超過
    StrategyMismatch,   // 王戦略との不整合
    LowScore            // 期待スコアが低い
}

// =====================================================================
//  DivisionProposal — 師団長が王に提出する行動提案
//  師団長は担当兵で試行し、行動案を王へ提出する。
// =====================================================================
public class DivisionProposal
{
    /// <summary>提案した師団長のインデックス (0-2)</summary>
    public int DivisionIndex;

    /// <summary>提案した師団長の名前（ログ用）</summary>
    public string DivisionName;

    /// <summary>行動列</summary>
    public List<AIAction> Actions = new List<AIAction>();

    /// <summary>必要AP合計</summary>
    public int TotalAPCost;

    /// <summary>想定スコア合計</summary>
    public float TotalScore;

    /// <summary>提案理由</summary>
    public string Reason;

    /// <summary>使用するユニット一覧</summary>
    public HashSet<Status> UsedUnits = new HashSet<Status>();

    /// <summary>使用するマス一覧</summary>
    public HashSet<Vector3Int> UsedTiles = new HashSet<Vector3Int>();

    /// <summary>採択されたか</summary>
    public bool Accepted;

    /// <summary>不採用理由（不採用時のみ設定）</summary>
    public RejectionReason? Rejection;

    /// <summary>不採用理由の詳細メッセージ</summary>
    public string RejectionDetail;

    public override string ToString()
        => $"師団{DivisionIndex}({DivisionName}): actions={Actions.Count} AP={TotalAPCost} score={TotalScore:F1} reason=\"{Reason}\"";
}
