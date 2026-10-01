using System.Collections.Generic;
using UnityEngine;

public partial class AIBoardState
{
    AIReconnaissance reconnaissance;
    AIOutpostPlanner outposts;
    public AIOutpostPlanner Outposts => outposts ?? (outposts = new AIOutpostPlanner(this));
    public bool ExpansionCommitted { get; set; }
    public void CollectOwnBuildings(List<Status> result)
    {
        result.Clear();
        var parent = _buildSystem != null ? _buildSystem.GetBuildingParent(Team.Enemy) : null;
        if (parent != null) parent.GetComponentsInChildren(false, result);
    }
    public int ReconThreatLevel { get; set; } = 1;
    public AIReconnaissance Recon => reconnaissance ?? (reconnaissance = new AIReconnaissance(this));
    public IEnumerable<KeyValuePair<int, LastKnownInfo>> ObservedHistory => _lastKnownPlayerPositions;
    public bool IsVisibleToEnemy(Vector3 position) => IsCellInEnemyVision(position);
    public bool IsExploredByEnemy(Vector3 position) => _visionGen != null && _visionGen.IsExplored(Team.Enemy, GridHelper.ToGridXZ(position));
    public MapCreate ReconMap => _moveGen.mapcreate;
}
