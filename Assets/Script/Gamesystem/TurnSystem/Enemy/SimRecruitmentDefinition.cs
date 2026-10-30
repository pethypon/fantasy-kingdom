using UnityEngine;

/// <summary>Immutable formal recruitment values shared by one search's clones; no actor or prefab is retained.</summary>
public readonly struct SimRecruitmentDefinition
{
    public readonly int HP, ATK, DEF, Barrier, APCost, SkillId;
    public readonly bool AvailableToPlayer, AvailableToEnemy;
    public readonly bool EngineerBarrierConfigured;
    public readonly PassiveSkill Passive;
    public readonly bool AssassinPassive;
    public readonly BoardActionProfile ActionProfile;
    public SimRecruitmentDefinition(UnitData definition)
    {
        HP = definition.FixedMaxHP; ATK = definition.FixedATK; DEF = definition.FixedDEF; APCost = definition.costAP;
        EngineerBarrierConfigured = definition.kind == Kind.Engineer && definition.baseBarrier >= 0;
        Barrier = BarrierSystem.UsesBarrier(definition,Type.Unit)
            ? BarrierSystem.Maximum(definition.baseBarrier >= 0 ? definition.baseBarrier : BarrierSystem.Initial(definition.kind), 1) : 0;
        AvailableToPlayer = definition.availableToPlayer; AvailableToEnemy = definition.availableToEnemy;
        // Match UnitData.ApplyAbilityIdentity: a fixed special definition replaces legacy abilities.
        bool special = definition.specialSkill != null && definition.specialSkill.ability != UnitSpecialSkill.None;
        AssassinPassive = definition.specialSkill != null && definition.specialSkill.enabledForPlay
            && definition.specialSkill.ability == UnitSpecialSkill.AssassinVisionSeal;
        Passive = !special && definition.useAuthoredAbilities ? definition.authoredPassive : PassiveSkill.None;
        SkillId = special ? -1 : definition.useAuthoredAbilities ? definition.authoredSkillId
            : SkillData.FixedSkillIds.TryGetValue(definition.kind, out int skill) ? skill : -1;
        ActionProfile = definition.actionProfile;
    }
}
