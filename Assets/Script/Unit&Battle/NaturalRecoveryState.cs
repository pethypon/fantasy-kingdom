using System;

/// <summary>One actor's completed damage-free owner-turn intervals; no Unity references.</summary>
[Serializable]
public sealed class NaturalRecoveryState
{
    public int ConsecutiveSafeTurns;
    public int LastProcessedRound = -1;
    public bool HpLostSinceLastOwnTurn;
    public bool HasTurnBaseline;

    public void RecordHpLoss()
    {
        ConsecutiveSafeTurns = 0;
        HpLostSinceLastOwnTurn = true;
    }
}
