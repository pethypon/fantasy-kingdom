using System.Collections.Generic;
using UnityEngine;

public partial class AIBoardState
{
    public readonly HashSet<int> RotatedUnits = new HashSet<int>();
    int beliefStampGeneration=-1,beliefStamp;
    public int BeliefEvidenceStamp
    {
        get
        {
            if(beliefStampGeneration==Generation)return beliefStamp;
            beliefStampGeneration=Generation;
            unchecked
            {
                int hash=ReconThreatLevel*397;
                foreach(var pair in ObservedHistory)hash^=pair.Key*31+pair.Value.Position.GetHashCode()*17+pair.Value.Turn*7+(int)pair.Value.Kind+pair.Value.ObservedAttackPower*13;
                if(_visionGen!=null)foreach(var cell in _visionGen.EnemyVisionBox)hash^=cell.GetHashCode()*53;
                foreach(var unit in AlivePlayerUnits)if(unit!=null)hash^=unit.GetInstanceID()*71;
                beliefStamp=hash;
            }
            return beliefStamp;
        }
    }
    AIBeliefMap belief;
    public AIBeliefMap Belief => belief ?? (belief = new AIBeliefMap(this));
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
