using System.Collections.Generic;
using UnityEngine;

public partial class AIBoardState
{
    AIReconnaissance reconnaissance;
    public int ReconThreatLevel { get; set; } = 1;
    public AIReconnaissance Recon => reconnaissance ?? (reconnaissance = new AIReconnaissance(this));
    public IEnumerable<KeyValuePair<int, LastKnownInfo>> ObservedHistory => _lastKnownPlayerPositions;
    public bool IsVisibleToEnemy(Vector3 position) => IsCellInEnemyVision(position);
    public bool IsExploredByEnemy(Vector3 position) => _visionGen != null && _visionGen.IsExplored(Team.Enemy, GridHelper.ToGridXZ(position));
    public MapCreate ReconMap => _moveGen.mapcreate;
}
