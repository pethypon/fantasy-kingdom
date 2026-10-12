using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI候補行動のデータクラス。
/// AIActionEvaluator から分離して独立管理。
/// </summary>
public class AIAction
{
    public AIActionType ActionType;
    public Status Unit;              // 行動する駒（移動/攻撃時）
    public Vector3 TargetPos;        // 移動先 or 配置位置
    public Status TargetUnit;        // 攻撃対象（あれば）
    public Direction TargetDirection; // Rotate only; free under current player rules.
    public int APCost;               // 消費AP
    public float Score;              // 最終評価点
    public FacilityKind Facility;    // 建築の種類
    public FacilityDefinitionData FacilityDefinition;
    public Kind SummonKind;          // 召喚するユニット種
    public UnitData SummonDefinition;
    public int StrategicPriority = 4;
    public string StrategicRejectReason;
    public SkillData Skill;          // 使用スキル
    public List<Status> AreaTargets; // 範囲スキルの対象リスト

    // Only an executed housing action with verbose diagnostics enabled allocates this immutable snapshot.
    // It survives the paid transaction's board refresh, but is cleared at every execution attempt.
    HousingExecutionSnapshot housingExecutionSnapshot;
    sealed class HousingExecutionSnapshot
    {
        public readonly AIStrategicGovernor Governor;
        public readonly AIBoardState Board;
        public readonly AIHousingAssessment Assessment;
        public HousingExecutionSnapshot(AIStrategicGovernor governor, AIBoardState board, AIHousingAssessment assessment)
        { Governor = governor; Board = board; Assessment = assessment; }
    }
    internal void ResetHousingExecutionAssessment() => housingExecutionSnapshot = null;
    internal void CaptureHousingExecutionAssessment(AIBoardState board)
    {
        if (board?.Governor == null || !AIHousingConfig.Active.EnableDecisionLogs
            || !DevelopmentLog.IsEnabled(AILogLevel.Verbose)) return;
        var assessment = board.Governor.HousingAssessment(this, board);
        if (assessment.IsHousing) housingExecutionSnapshot = new HousingExecutionSnapshot(board.Governor, board, assessment);
    }
    public bool TryGetHousingExecutionAssessment(AIStrategicGovernor governor, AIBoardState board, out AIHousingAssessment assessment)
    {
        var snapshot = housingExecutionSnapshot;
        if (snapshot != null && ReferenceEquals(snapshot.Governor, governor) && ReferenceEquals(snapshot.Board, board))
        { assessment = snapshot.Assessment; return true; }
        assessment = default; return false;
    }

    public static int ComparePriorityThenScore(AIAction a, AIAction b)
    {
        int priority = a.StrategicPriority.CompareTo(b.StrategicPriority);
        return priority != 0 ? priority : b.Score.CompareTo(a.Score);
    }

    // One actor's blocked route must not suppress another actor's valid move.
    public string FailureGroupKey => $"{ActionType}_{Facility}_{FacilityDefinition?.definitionId}_{SummonKind}_{SummonDefinition?.definitionId}_{(Unit != null ? Unit.GetInstanceID() : 0)}";
    public string FailureKey => $"{FailureGroupKey}|{TargetPos}|{(TargetUnit != null ? TargetUnit.GetInstanceID() : 0)}|{(Skill != null ? Skill.Id : -1)}";

    public override string ToString()
        => $"{ActionType}({Unit?.kind}/{Facility}/{SummonKind}) → {TargetPos} score={Score:F1}";
}
