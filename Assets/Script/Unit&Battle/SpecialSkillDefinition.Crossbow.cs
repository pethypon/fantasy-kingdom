using UnityEngine;

public sealed partial class SpecialSkillDefinition
{
    public const int CrossbowMagazineCapacity = 3;
    [Min(-1), Tooltip("UNRESOLVED_BALANCE：初期弾数は未指定です。0～3発を設定してください。通常攻撃しなかったターンに通常弾を1発補充します。")]
    public int crossbowInitialAmmo = -1;

    public string CrossbowConfigurationError => ability == UnitSpecialSkill.CrossbowStakes
        && (crossbowInitialAmmo < 0 || crossbowInitialAmmo > CrossbowMagazineCapacity)
        ? "UNRESOLVED_BALANCE：クロスボウの初期弾数（0～3）が未設定です" : null;
}
