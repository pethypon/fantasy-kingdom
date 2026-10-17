using System;
using System.Collections.Generic;

// Values belong to the new magazine only; existing serialized skill enums are unchanged.
public enum CrossbowRoundType { None, Normal, Stake }

[Serializable]
public sealed class CrossbowLineStayStamp
{
    public string targetId, stakeAId, stakeBId;
    public int round, stakeAAppliedTurn, stakeBAppliedTurn;
}

public sealed partial class SpecialState
{
    // Oldest round is index zero. UI reverses this order so shots leave from the right.
    public List<CrossbowRoundType> crossbowMagazine = new List<CrossbowRoundType>();
    public bool crossbowMagazineInitialized, crossbowNormalAttacked;
    public int crossbowLoadSequence, crossbowShotSequence, crossbowReplayKey;
    public List<CrossbowLineStayStamp> crossbowStayHits = new List<CrossbowLineStayStamp>();
    [NonSerialized] public CrossbowRoundType crossbowPendingRound;
}
