#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Creates the provisional, Inspector-editable R2 builder using Unity's native asset APIs.</summary>
public static class EngineerProductionAuthoring
{
    public const string Folder = "Assets/_FantasyKingdom/GameContent/R2Production/Units/Engineer";
    public const string DataPath = Folder + "/Stats.asset";
    public const string ProfilePath = Folder + "/ActionTiles.asset";
    public const string DefinitionId = "r2.unit.engineer";
    const string CatalogPath = "Assets/Resources/GameContent/UnitCatalog.asset";
    const string ScoutPrefabPath = "Assets/Prefabs/Units/Player/Scout.prefab";

    [MenuItem("Fantasy Kingdom/開発/工兵の暫定データを登録")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before installing Engineer production data.");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScoutPrefabPath);
        if (prefab == null || prefab.GetComponentInChildren<Status>(true) == null)
            throw new InvalidOperationException("Engineer requires the existing Scout prefab with a Status component.");
        var catalog = AssetDatabase.LoadAssetAtPath<UnitAuthoringCatalog>(CatalogPath);
        if (catalog == null) throw new InvalidOperationException("The shipped UnitCatalog must exist before installing Engineer.");
        var data = AssetDatabase.LoadAssetAtPath<UnitData>(DataPath);
        if (data != null && (data.kind != Kind.Engineer || data.definitionId != DefinitionId))
            throw new InvalidOperationException("The Engineer Stats path is occupied by a different definition.");

        UnitAuthoringCatalog.Entry registered = null;
        if (catalog.units != null)
        {
            foreach (var entry in catalog.units)
            {
                if (entry == null || entry.standaloneDefinition || entry.kind != Kind.Engineer) continue;
                if (registered != null || entry.data != null && entry.data != data)
                    throw new InvalidOperationException("An Engineer role override already exists; preserve it and resolve the catalog explicitly.");
                registered = entry;
            }
        }

        EnsureFolder(Folder);
        var profile = AssetDatabase.LoadAssetAtPath<BoardActionProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<BoardActionProfile>();
            profile.name = "Engineer ActionTiles";
            profile.displayName = "工兵（移動・建築支援）";
            profile.actionType = ActorActionType.Support;
            profile.movement = SquarePattern(1, false);
            profile.attack = SquarePattern(0, false);
            profile.vision = SquarePattern(2, true);
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<UnitData>();
            data.name = "Engineer Stats";
            data.definitionId = DefinitionId;
            data.displayName = "工兵";
            data.category = "R2";
            data.kind = Kind.Engineer;
            data.defaultTeam = Team.Player;
            data.availableToPlayer = data.availableToEnemy = true;
            data.baseHP = 24; // FixedUnitRules converts this authored base to 42 HP.
            data.baseATK = data.baseDEF = 0;
            data.baseBarrier = 6;
            data.hpGrowth = data.atkGrowth = data.defGrowth = 0;
            data.useAuthoredAbilities = true;
            data.authoredPassive = PassiveSkill.None;
            data.authoredSkillId = -1;
            data.authoredSpecialAbility = SpecialAbility.None;
            data.specialSkill = null;
            data.upkeepExempt = false;
            data.useAuthoredUpkeep = true;
            data.upkeepBread = 1; // Global extra upkeep and the mandatory factor of five still apply.
            data.upkeepMagicScales = false;
            data.costWood = 20;
            data.costStone = 10;
            data.costWater = 5;
            data.costBread = 5;
            data.costCitizen = 1;
            data.costAP = 3;
            data.prefab = prefab;
            data.actionProfile = profile;
            AssetDatabase.CreateAsset(data, DataPath);
        }

        // Rerunning the installer repairs references without overwriting balance or mask tuning.
        if (data.prefab == null) { data.prefab = prefab; EditorUtility.SetDirty(data); }
        if (data.actionProfile == null) { data.actionProfile = profile; EditorUtility.SetDirty(data); }
        if (catalog.units == null) catalog.units = new List<UnitAuthoringCatalog.Entry>();
        if (registered == null)
        {
            registered = new UnitAuthoringCatalog.Entry { kind = Kind.Engineer, standaloneDefinition = false };
            catalog.units.Add(registered);
        }
        registered.data = data;
        registered.prefab = data.prefab;
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        GameContentRevision.NotifyChanged();
        ValidateInstalled();
        Debug.Log("[EngineerProduction] Installed editable Engineer definition. Re-running preserves authored balance and masks.", data);
    }

    [MenuItem("Fantasy Kingdom/開発/工兵データを検証")]
    public static void ValidateInstalled()
    {
        var data = AssetDatabase.LoadAssetAtPath<UnitData>(DataPath);
        var catalog = AssetDatabase.LoadAssetAtPath<UnitAuthoringCatalog>(CatalogPath);
        var definitions = new Dictionary<Kind, UnitData>();
        var prefabs = new Dictionary<Kind, GameObject>();
        catalog?.Apply(definitions, prefabs);
        if (data == null || !data.IsValidForAuthoring || data.kind != Kind.Engineer
            || data.definitionId != DefinitionId || catalog == null || catalog.GetById(DefinitionId) != data
            || !definitions.TryGetValue(Kind.Engineer, out var role) || role != data
            || !prefabs.TryGetValue(Kind.Engineer, out var prefab) || prefab == null
            || !data.availableToPlayer || !data.availableToEnemy
            || Array.IndexOf(UnitRoster.SummonableKinds, Kind.Engineer) < 0)
            throw new InvalidOperationException("Engineer is not registered through the shared production catalog.");
        var profile = data.actionProfile;
        if (profile == null || profile.actionType != ActorActionType.Support || !profile.CanMove || profile.CanAttack
            || profile.movement == null || !profile.movement.useCustom || profile.movement.Offsets.Count == 0
            || profile.vision == null || !profile.vision.useCustom || profile.vision.Offsets.Count == 0)
            throw new InvalidOperationException("Engineer needs a mobile Support profile with its own movement and vision masks.");
        var upkeep = data.GetFixedUpkeep();
        Debug.Log($"[EngineerProduction] Valid: {DefinitionId}; HP={data.FixedMaxHP}, ATK={data.FixedATK}, DEF={data.FixedDEF}, barrier={data.baseBarrier}; "
            + $"recruit wood={data.costWood}, stone={data.costStone}, iron={data.costIron}, magic={data.costMagic}, water={data.costWater}, bread={data.costBread}, citizen={data.costCitizen}, AP={data.costAP}; "
            + $"upkeep wood={upkeep.Wood}, stone={upkeep.Stone}, iron={upkeep.Iron}, magic={upkeep.MagicOre}, wheat={upkeep.Wheat}, water={upkeep.Water}, bread={upkeep.Bread}, citizen={upkeep.Citizen}.", data);
    }

    static BoardTilePattern SquarePattern(int radius, bool includeOrigin)
    {
        var pattern = new BoardTilePattern { useCustom = true, originX = 3, originZ = 3, directionIndependent = true };
        for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
                if (includeOrigin || x != 0 || z != 0) pattern.SetCell(x + pattern.originX, z + pattern.originZ, true);
        return pattern;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
    }
}
#endif
