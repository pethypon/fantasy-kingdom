using System;
using System.Collections.Generic;

public sealed partial class SpecialState
{
    public List<string> archerSnapshotIds = new List<string>();
    public int archerEscapeTurns, lastEndRound = -1;
    public int assassinBoostTurns, assassinBoostStacks;
    public bool scoutFreeMoveUsed, scoutFreeAttackUsed;
    public AbilityResetScope scoutMoveUsedScope, scoutAttackUsedScope;
    public int guardianAttackCount;
    public string tauntedById;
    public int tauntTurns;
    public int bomberIdleTurns;
    public bool dealtDamage;
    public List<PaladinAttackCycle> paladinCycles = new List<PaladinAttackCycle>();
}

[Serializable]
public sealed class PaladinAttackCycle
{
    public string sourceId, targetId;
    public int stolenAttack, returnedAttack, startOwnerTurn;
    public bool returned;
}
