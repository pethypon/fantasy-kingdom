using UnityEngine;

/// <summary>Immutable formal recruitment values shared by one search's clones; no actor or prefab is retained.</summary>
public readonly struct SimRecruitmentDefinition
{
    public readonly int HP, ATK, DEF, APCost, SkillId;
    public readonly bool AvailableToPlayer, AvailableToEnemy;
    public readonly PassiveSkill Passive;
    public readonly BoardActionProfile ActionProfile;
    public SimRecruitmentDefinition(UnitData definition)
    {
        HP = definition.baseHP; ATK = definition.baseATK; DEF = definition.baseDEF; APCost = definition.costAP;
        AvailableToPlayer = definition.availableToPlayer; AvailableToEnemy = definition.availableToEnemy;
        Passive = definition.useAuthoredAbilities ? definition.authoredPassive : PassiveSkill.None;
        SkillId = definition.useAuthoredAbilities ? definition.authoredSkillId
            : SkillData.FixedSkillIds.TryGetValue(definition.kind, out int skill) ? skill : -1;
        ActionProfile = definition.actionProfile;
    }
}
