using System;
using System.Collections.Generic;
using UnityEngine;

public enum HpChangeKind { NormalDamage, IgnoreDefenceDamage, DirectHpReduction, ForcedDeath, ExecutionDamage }

/// <summary>Durable actor state. IDs denote a life, not a prefab or a recycled instance.</summary>
[Serializable]
public sealed partial class SpecialState
{
    public int ownerTurn, lastRound = -1, cooldown, stationaryTurns;
    public bool moved, attacked;
    public string ritualTargetId;
    public int storedSacrificeDamage, ritualFireTurn;
    public string guardianPartnerId;
    public string stakeAId, stakeBId;
    public int stakeAAppliedTurn, stakeBAppliedTurn;
    public string sniperTargetId;
    public int sniperFireTurn;
    public string bombCasterId;
    public int bombDueRound;
    public int bombRadius = -1;
    public SpecialFactionMask bombMask;
    public BombDeathPolicy bombDeathPolicy;
    public Team bombCasterTeam;
    public bool nextDamageDouble;
    public int visionZeroTurns, scoutVisionPenaltyTurns, damageNullTurns;
    public bool damageNullUnused;
    public List<string> affectedUnitIds = new List<string>();
    public int samuraiGauge, samuraiMaxTurns, ninjaCharge, berserkerStage, holyKnightTurns;
    public bool holyKnightRecoveryLock;
    public bool ritualActive => !string.IsNullOrEmpty(ritualTargetId);
    public bool sniperActive => !string.IsNullOrEmpty(sniperTargetId);
}

public sealed class SpecialSkillCommand
{
    public Status Caster, Target;
    public List<Status> Sacrifices;
    // Expected next stage, used to reject stale UI/AI commands. Zero resolves it at execution.
    public int BerserkerStage;
    public SpecialSkillCommand(Status caster, Status target = null, List<Status> sacrifices = null, int berserkerStage = 0)
    { Caster = caster; Target = target; Sacrifices = sacrifices; BerserkerStage = berserkerStage; }
}
