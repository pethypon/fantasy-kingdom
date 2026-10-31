using UnityEngine;

/// <summary>Read-only healing identity shared by recruitment, squads and recovery positioning.</summary>
public static class AIHealingCapability
{
    static bool Valid(SpecialSkillDefinition special) => special != null && special.enabledForPlay
        && special.ConfigurationError == null;
    static bool Aura(SpecialSkillDefinition special) => Valid(special)
        && special.ability == UnitSpecialSkill.PriestPurification;
    static bool ActiveHealing(SpecialSkillDefinition special) => Valid(special)
        && (special.ability == UnitSpecialSkill.WhiteMageHeal || special.ability == UnitSpecialSkill.HolyKnightSanctuary);
    static bool HealingSkill(int id) => SkillData.Table.TryGetValue(id, out var skill) && skill.FixedHeal > 0;

    public static bool HasHealing(Status actor)
    {
        if (actor == null || actor.type != Type.Unit) return false;
        var special = SpecialSkillRuntime.Definition(actor);
        // Priest's owner-turn aura is passive; a Passive action profile does not disable it.
        return Aura(special) || AttackPatterns.CanUseSkills(actor)
            && (ActiveHealing(special) || HealingSkill(actor.AssignedSkillId));
    }

    public static bool HasHealing(UnitData definition)
    {
        if (definition == null || !definition.IsValidForAuthoring) return false;
        var special = definition.specialSkill;
        // Match UnitData.ApplyAbilityIdentity: a fixed special replaces historical skill IDs,
        // including when that authored special is currently disabled or invalid.
        if (special != null && special.ability != UnitSpecialSkill.None)
            return Aura(special) || definition.actionProfile?.CanUseSkills != false && ActiveHealing(special);
        if (definition.actionProfile?.CanUseSkills == false) return false;
        int id = -1;
        if (definition.useAuthoredAbilities) id = definition.authoredSkillId;
        else if (!SkillData.FixedSkillIds.TryGetValue(definition.kind, out id)) return false;
        return HealingSkill(id);
    }

    public static bool CanHealAt(Status actor, Vector3 position, float searchRange)
    {
        if (!HasHealing(actor) || float.IsNaN(searchRange) || searchRange < 0) return false;
        var delta = GridHelper.ToGrid(position) - GridHelper.ToGrid(actor.transform.position);
        int gridDistance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Max(Mathf.Abs(delta.y), Mathf.Abs(delta.z)));
        // Same 3D grid metric and radius as SpecialSkillRuntime's owner-turn Priest aura.
        // The caller's four-cell recovery search must never turn this into a four-cell heal.
        if (Aura(SpecialSkillRuntime.Definition(actor)) && gridDistance <= 1 && gridDistance <= searchRange) return true;
        // Preserve the existing bounded proximity heuristic for active healing skills.
        // This is neither an executable skill candidate nor an AP/cooldown/target waiver.
        if ((position - actor.transform.position).sqrMagnitude > searchRange * searchRange
            || !AttackPatterns.CanUseSkills(actor)) return false;
        return ActiveHealing(SpecialSkillRuntime.Definition(actor)) || HealingSkill(actor.AssignedSkillId);
    }
}
