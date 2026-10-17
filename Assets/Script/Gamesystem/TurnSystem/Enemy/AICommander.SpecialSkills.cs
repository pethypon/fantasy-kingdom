using UnityEngine;

public partial class AICommander
{
    /// <summary>Successful commands only. No event object, identity or coordinates enter persistent learning.</summary>
    public void ObserveSpecialSkill(SpecialSkillCommand command)
    {
        if(!CanLearn||_playerModel==null||command?.Caster==null||command.Caster.team!=Team.Player)return;
        var systems=_turnGen?.Systems;
        var vision=systems?.VisionGenerator;
        if(vision==null)return;
        bool Observable(Status actor)=>actor!=null&&vision.IsActorVisibleTo(_actorTeam,actor);
        if(!Observable(command.Caster))return;
        Vector3 origin=systems.CrystalSystem==null?new Vector3(float.NaN,0,float.NaN)
            :_actorTeam==Team.Enemy?systems.CrystalSystem.ECP:systems.CrystalSystem.PCP;
        _playerModel.ObserveSpecialCommand(command,Observable,origin);
        if(AIConfig.IsMLEnabled)_mlIntegration?.ObservePlayerSkill(command.Caster,_turnGen.Context.Turn);
    }
}
