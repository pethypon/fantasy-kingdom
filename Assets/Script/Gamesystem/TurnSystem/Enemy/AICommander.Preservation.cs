public partial class AICommander
{
    public AIUnitPreservationState CapturePreservationState() => _governor.CapturePreservationState(_actorTeam);
    public void RestorePreservationState(AIUnitPreservationState state) => _governor.RestorePreservationState(state, _actorTeam);
}
