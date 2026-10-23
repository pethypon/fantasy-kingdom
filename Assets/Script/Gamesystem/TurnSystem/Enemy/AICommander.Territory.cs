/// <summary>Per-match, per-faction territorial evidence survives transient turn boards.</summary>
public partial class AICommander
{
    public EnemyTerritoryIntel TerritoryIntel => _governor.TerritoryIntel;
    public EnemyTerritoryIntelState CaptureTerritoryState()
    {
        var state = TerritoryIntel.CaptureState();
        // Saving before this commander has taken its first turn must not invent a faction or map.
        return state != null && state.Width > 0 && state.Depth > 0 ? state : null;
    }
    public void RestoreTerritoryState(EnemyTerritoryIntelState state) => TerritoryIntel.RestoreState(state, _actorTeam);
}
