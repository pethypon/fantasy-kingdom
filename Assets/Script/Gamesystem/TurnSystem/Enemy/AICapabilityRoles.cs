using UnityEngine;

/// <summary>Read-only role evidence from the same authored masks used by the board rules.
/// These are capabilities, never permission to attack, reveal terrain or spend resources.</summary>
public readonly struct AICapabilityRole
{
    public readonly int MinimumAttackReach, MaximumAttackReach, MovementReach, VisionReach, VisionFootprint;
    public readonly bool CanAttack, CanMove, Healing, Reconnaissance, Mobile, Engineer;
    internal AICapabilityRole(int minimumAttack, int maximumAttack, int movement, int vision, int footprint,
        bool healing, bool engineer)
    {
        MinimumAttackReach = minimumAttack; MaximumAttackReach = maximumAttack;
        MovementReach = movement; VisionReach = vision; VisionFootprint = footprint;
        CanAttack = maximumAttack > 0; CanMove = movement > 0; Healing = healing; Engineer = engineer;
        Mobile = movement >= 2;
        // A broad observation footprint plus mobility, rather than a Kind name. Legacy
        // vision contains two height layers; count its ground layer only, like a custom mask.
        // Long-range fire and a matching sight mask make a firing role, not an
        // automatic scout. Recon needs useful observation beyond its own weapons.
        Reconnaissance = Mobile && vision >= 2 && footprint >= 16 && vision > maximumAttack;
    }
    public AIOperationSquadRole PrimarySquadRole => Healing ? AIOperationSquadRole.Healer
        : Reconnaissance ? AIOperationSquadRole.Scout
        : MaximumAttackReach >= 2 ? AIOperationSquadRole.Ranged : AIOperationSquadRole.Frontline;
    public string RecruitmentRole => Healing ? "healer" : Engineer ? "engineer"
        : Reconnaissance ? "scout" : CanAttack ? MaximumAttackReach >= 2 ? "ranged" : "frontline" : null;
}

public static class AICapabilityRoles
{
    public static AICapabilityRole For(Status actor)
        => actor == null || actor.type != Type.Unit ? default
            : Read(actor.kind, BoardActionProfile.For(actor), AIHealingCapability.HasHealing(actor),
                actor.kind == Kind.Engineer && actor.GrowthData?.IsValidForAuthoring == true);
    public static AICapabilityRole For(UnitData definition, Kind fallbackKind)
        => Read(definition != null ? definition.kind : fallbackKind, definition?.actionProfile,
            AIHealingCapability.HasHealing(definition), definition?.kind == Kind.Engineer && definition.IsValidForAuthoring);

    static AICapabilityRole Read(Kind kind, BoardActionProfile profile, bool healing, bool engineer)
    {
        int minimum = int.MaxValue, maximum = 0, movement = 0, vision = 0, footprint = 0;
        // The serialized masks are 8x8. Legacy normal attacks are bounded by four cells;
        // seven covers every authored origin without a map scan or allocating offsets.
        if (profile?.CanAttack != false)
            for (int x = -7; x <= 7; x++) for (int z = -7; z <= 7; z++)
                if ((x != 0 || z != 0) && AttackPatterns.CanAttack(kind, Direction.N, x, z, profile))
                { int reach = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)); minimum = Mathf.Min(minimum, reach); maximum = Mathf.Max(maximum, reach); }
        foreach (var offset in MovePatterns.Offsets(kind, profile))
            if (offset.x != 0 || offset.y != 0) movement = Mathf.Max(movement, Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)));
        foreach (var offset in VisionGenerator.BaseVisionOffsets(kind, Direction.N, profile))
        {
            vision = Mathf.Max(vision, Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.z)));
            if (offset.y == 0) footprint++;
        }
        return new AICapabilityRole(maximum > 0 ? minimum : 0, maximum, movement, vision, footprint,
            healing, engineer && profile?.actionType != ActorActionType.Passive);
    }

    public static string RecruitmentRole(UnitData definition, Kind kind)
    {
        kind = definition != null ? definition.kind : kind;
        if (kind == Kind.King || kind == Kind.Boss || kind == Kind.Crystal || kind == Kind.SubCrystal) return null;
        return For(definition, kind).RecruitmentRole;
    }
    public static string RecruitmentRole(Status actor)
        => actor == null || MatchObjectiveRules.IsCommander(actor) ? null : For(actor).RecruitmentRole;
}
