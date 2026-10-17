using UnityEngine;

public enum AbilityResetScope { Unconfigured, OwnerTurn, Lifetime }
public enum AbilityStackRule { Unconfigured, Strongest, Multiply }

public sealed partial class SpecialSkillDefinition
{
    [Header("R1 常時能力：未確定値は設定後に有効")]
    public AbilityStackRule knightPenaltyStackRule;
    [Min(-1)] public int assassinBoostDuration = -1;
    public AbilityStackRule assassinBoostStackRule;
    public AbilityResetScope scoutFreeMoveScope, scoutFreeAttackScope;
    public AbilityResetScope guardianAttackCostReset;
    [Min(-1)] public int bomberAutoExplosionRadius = -1;
    public int BomberAutoRadius => bomberAutoExplosionRadius >= 0 ? bomberAutoExplosionRadius : bombExplosionRadius;
    public const float MagicLowHealthThreshold = .25f;
    public const float MagicStrongTargetMultiplier = 1.5f, MagicOrdinaryTargetMultiplier = .75f;
    public const float AssassinAllyThreshold = .2f, AssassinAttackBoost = 1.75f;
    public const float ArcherEnemyAttackBoost = 1.2f, PriestAuraFraction = .05f;
    public const float PaladinStealFraction = .25f, PaladinReturnFraction = .8f;
    public const float SniperNormalRecoilFraction = .2f;
    public const int ArcherEscapeDuration = 2, SamuraiTauntDuration = 3, BomberIdleLimit = 10;
}
