using System;
using UnityEditor;
using UnityEngine;

/// <summary>Opt-in migration creates editable definitions; existing abilities and undecided values are preserved.</summary>
public static class SpecialSkillAuthoringDefaults
{
    public const string Folder = "Assets/_FantasyKingdom/GameContent/SpecialAbilities";

    [MenuItem("Fantasy Kingdom/駒固有能力/既存の駒へ設定雛形を追加")]
    public static void InstallExistingDefinitions()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("ゲームを停止してから固有能力の雛形を追加してください。");
        UnitWorkshopWindow.EnsureFolder(Folder);
        int created = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:UnitData"))
        {
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null || data.specialSkill != null || StudioDeletion.IsDeleted(data) || !TryAbility(data.kind, out var ability)) continue;
            var definition = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
            definition.ability = ability; definition.displayName = SpecialSkillPresentation.Name(ability); definition.ApplySpecifiedAPCost();
            definition.enabledForPlay = false; // Author completes undecided values before opting this piece into play.
            string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + data.kind + "_" + guid.Substring(0, 8) + ".asset");
            AssetDatabase.CreateAsset(definition, path);
            Undo.RecordObject(data, "駒固有能力の設定雛形を追加"); data.specialSkill = definition; EditorUtility.SetDirty(data); created++;
        }
        AssetDatabase.SaveAssets(); GameContentRevision.NotifyChanged();
        Debug.Log("[SpecialSkillAuthoring] Added " + created + " independent definitions. Specified AP applied; undecided cooldowns and curves preserved.");
    }

    public static bool TryAbility(Kind kind, out UnitSpecialSkill ability)
    {
        ability = kind switch
        {
            Kind.Knight => UnitSpecialSkill.KnightBreakthrough, Kind.Archer => UnitSpecialSkill.ArcherStationary,
            Kind.Magic => UnitSpecialSkill.MagicRitual, Kind.Assassin => UnitSpecialSkill.AssassinVisionSeal,
            Kind.Scout => UnitSpecialSkill.ScoutVisionGuard, Kind.Priest => UnitSpecialSkill.PriestPurification,
            Kind.Guardian => UnitSpecialSkill.GuardianLifeLink, Kind.Crossbow => UnitSpecialSkill.CrossbowStakes,
            Kind.Magicsniper => UnitSpecialSkill.MagicSniperLock, Kind.Bomber => UnitSpecialSkill.LifeBomb,
            Kind.WhiteMage => UnitSpecialSkill.WhiteMageHeal, Kind.HolyKnight => UnitSpecialSkill.HolyKnightSanctuary,
            Kind.Samurai => UnitSpecialSkill.SamuraiCounter, Kind.Ninja => UnitSpecialSkill.NinjaSwap,
            Kind.Berserker => UnitSpecialSkill.BerserkerRage, Kind.Navigator => UnitSpecialSkill.NavigatorPull,
            _ => UnitSpecialSkill.None
        };
        return ability != UnitSpecialSkill.None;
    }
}
