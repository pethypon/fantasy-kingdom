using System.Collections.Generic;
/// <summary>Reusable candidates owned by one search depth. Never share across recursive levels.</summary>
public sealed class SimActionBuffer
{
    public readonly List<SimAction> Actions = new List<SimAction>(128);
    readonly List<SimAction> storage = new List<SimAction>(128);
    public void Clear() => Actions.Clear();
    public SimAction Next()
    {
        int index=Actions.Count;
        if(index==storage.Count) storage.Add(new SimAction());
        var action=storage[index];
        action.Type=default;action.UnitId=0;action.TargetPos=default;action.TargetUnitId=-1;
        action.APCost=0;action.Facility=default;action.SummonKind=default;action.SkillId=-1;action.ActorTeam=Team.Enemy;
        Actions.Add(action);return action;
    }
}
