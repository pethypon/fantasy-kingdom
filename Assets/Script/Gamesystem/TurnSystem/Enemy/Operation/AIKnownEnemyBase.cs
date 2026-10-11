using System;

public enum AIEnemyBaseType { Unknown, MainCrystal, SubCrystal, MilitaryFacility, EconomyFacility, ResourceFacility, SupportFacility }
public enum AIBaseAssaultPattern { None, DirectAssault, ScoutThenAssault, FlankAssault, DiversionAssault, SiegeAssault, RaidAndWithdraw }

/// <summary>Battle-only observation memory. Contains no Unity objects or cross-battle learning coordinates.</summary>
[Serializable]
public sealed class AIKnownEnemyBase
{
    public string StableTargetId;
    public AIEnemyBaseType Type;
    public int X, Z, FirstSeenTurn, LastSeenTurn, RediscoveryCount;
    public bool CurrentlyVisible, ConfirmedDestroyed, DefenseIntelKnown;
    public float IntelConfidence, LastKnownHpRatio, EstimatedDefense, EstimatedStrategicValue;
    public AIKnownEnemyBase Copy() => (AIKnownEnemyBase)MemberwiseClone();
}
