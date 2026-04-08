using UnityEngine;

// =====================================================================
//  SimAction — シミュレーション上の行動
// =====================================================================
public enum SimActionType
{
    Move,
    Attack,
    Build,
    Summon,
    SkillUse,
    Wait
}

public class SimAction
{
    public SimActionType Type;
    public int UnitId;           // 行動するユニットのID
    public Vector3Int TargetPos; // 移動先 or 建築位置
    public int TargetUnitId;     // 攻撃対象のID (-1 = なし)
    public int APCost;
    public FacilityKind Facility;
    public Kind SummonKind;
    public int SkillId;          // 使用スキルID (-1 = なし)
    public Team ActorTeam;       // 行動者のチーム

    public SimAction()
    {
        TargetUnitId = -1;
        SkillId = -1;
        ActorTeam = Team.Enemy;
    }
}
