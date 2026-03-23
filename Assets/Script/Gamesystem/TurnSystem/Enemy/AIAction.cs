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
    public int APCost;               // 消費AP
    public float Score;              // 最終評価点
    public FacilityKind Facility;    // 建築の種類
    public Kind SummonKind;          // 召喚するユニット種
    public SkillData Skill;          // 使用スキル
    public List<Status> AreaTargets; // 範囲スキルの対象リスト

    public override string ToString()
        => $"{ActionType}({Unit?.kind}/{Facility}/{SummonKind}) → {TargetPos} score={Score:F1}";
}
