using UnityEngine;

// =====================================================================
//  AIMinimaxEngine.Convert — AIAction <-> SimAction 変換
// =====================================================================
public partial class AIMinimaxEngine
{
    // ================================================================
    //  AIAction -> SimAction 変換
    // ================================================================
    SimAction ConvertToSimAction(AIAction aiAction, SimBoardState board)
    {
        var sim = new SimAction();
        sim.APCost = aiAction.APCost;
        sim.ActorTeam = Team.Enemy; // AICommanderから呼ばれるので常にEnemy

        switch (aiAction.ActionType)
        {
            case AIActionType.Move:
            case AIActionType.Retreat:
            case AIActionType.Support:
            case AIActionType.Surround:
            case AIActionType.DefenseRepos:
                sim.Type = SimActionType.Move;
                sim.UnitId = FindSimUnitId(aiAction.Unit, board);
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                if (sim.UnitId < 0) return null;
                return sim;

            case AIActionType.Attack:
                sim.Type = SimActionType.Attack;
                sim.UnitId = FindSimUnitId(aiAction.Unit, board);
                sim.TargetUnitId = FindSimUnitId(aiAction.TargetUnit, board);
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                if (sim.UnitId < 0 || sim.TargetUnitId < 0) return null;
                return sim;

            case AIActionType.SkillUse:
                sim.Type = SimActionType.SkillUse;
                sim.UnitId = FindSimUnitId(aiAction.Unit, board);
                sim.TargetUnitId = aiAction.TargetUnit != null
                    ? FindSimUnitId(aiAction.TargetUnit, board) : -1;
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                sim.SkillId = aiAction.Skill != null ? aiAction.Skill.Id : -1;
                if (sim.UnitId < 0) return null;
                return sim;

            case AIActionType.Build:
            case AIActionType.SubCrystal:
                sim.Type = SimActionType.Build;
                sim.Facility = aiAction.Facility;
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                return sim;

            case AIActionType.Summon:
                sim.Type = SimActionType.Summon;
                sim.SummonKind = aiAction.SummonKind;
                sim.TargetPos = SimBoardState.ToCell(aiAction.TargetPos);
                return sim;

            case AIActionType.Wait:
                sim.Type = SimActionType.Wait;
                return sim;

            default:
                return null;
        }
    }

    // ================================================================
    //  Status (実ゲーム) -> SimUnit ID のマッピング
    // ================================================================
    static int FindSimUnitId(Status realUnit, SimBoardState board)
    {
        if (realUnit == null) return -1;

        var pos = new Vector3Int(
            Mathf.RoundToInt(realUnit.transform.position.x), 0,
            Mathf.RoundToInt(realUnit.transform.position.z));

        // 位置 + チーム + Kind で完全一致
        for (int i = 0; i < board.Units.Count; i++)
        {
            var su = board.Units[i];
            if (su.Team == realUnit.team && su.Position == pos && su.Kind == realUnit.kind)
                return su.Id;
        }

        // 位置が合わない場合はKind+Teamのみで探索（移動済みの場合）
        for (int i = 0; i < board.Units.Count; i++)
        {
            var su = board.Units[i];
            if (su.Team == realUnit.team && su.Kind == realUnit.kind && su.IsAlive)
                return su.Id;
        }

        return -1;
    }
}
