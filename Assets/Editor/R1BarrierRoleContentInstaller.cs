#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Independent role prototypes. Their provisional tuning is separate from R1's confirmed barrier table.</summary>
public static class R1BarrierRoleContentInstaller
{
    public const string TrialVersion = "R1-barrier-role-prototypes-20261021";
    static readonly Kind[] Roles = { Kind.HeavyWarrior, Kind.DragonKnight };
    static string UnitFolder(Kind kind) => R1ProductionContentInstaller.Folder + "/Units/" + kind;
    static string DefaultsPath(Kind kind) => R1ProductionContentInstaller.Folder + "/Defaults/" + kind + ".asset";
    static string SourcePath(Kind kind) => kind == Kind.HeavyWarrior ? "Assets/Data/Unit/Knight.asset"
        : R1ProductionContentInstaller.Folder + "/Units/HolyKnight/Stats.asset";
    static UnitSpecialSkill Ability(Kind kind) => kind == Kind.HeavyWarrior
        ? UnitSpecialSkill.HeavyWarriorBreak : UnitSpecialSkill.DragonKnightPierce;
    static GameObject SourcePrefab(UnitData source) => source.prefab != null ? source.prefab
        : AssetDatabase.LoadAssetAtPath<GameObject>(R1UnitVisualInstaller.PrefabPath(source.kind));

    [MenuItem("Fantasy Kingdom/駒固有能力/重戦士・竜騎士の無効な下書きを作成")]
    public static void PrepareDrafts()
    {
        EnsureStopped();
        // Check every dependency before creating draft assets.
        BoardVisualThemeInstaller.LoadFactionMaterials(out var white, out _, out _);
        foreach (var kind in Roles)
        {
            var source = AssetDatabase.LoadAssetAtPath<UnitData>(SourcePath(kind));
            var prefab = source == null ? null : SourcePrefab(source);
            if (source == null || !source.IsValidForAuthoring || prefab == null
                || prefab.GetComponentsInChildren<Status>(true).Length != 1)
                throw new InvalidOperationException("先に継承元のナイト・聖騎士を導入してください: " + kind);
            ValidateOwnedDraft(kind);
        }
        foreach (var kind in Roles) CreateMissingDraft(kind, white);
        AssetDatabase.SaveAssets();
        Debug.Log("[R1BarrierRoles] 重戦士・竜騎士の独立した下書き。未導入の下書きは召喚不可、固有能力無効。既存データは保護しました。");
    }

    /// <summary>Explicitly installs adjustable prototype values. Repeat calls preserve installed tuning.</summary>
    [MenuItem("Fantasy Kingdom/駒固有能力/重戦士・竜騎士へ暫定調整値を導入")]
    public static void InstallAuthorizedTrial()
    {
        PrepareDrafts();
        var settings = AssetDatabase.LoadAssetAtPath<R1ProductionContentSettings>(R1ProductionContentInstaller.SettingsPath);
        if (settings == null) throw new InvalidOperationException("R1本番導入設定がありません。");
        if (settings.installed == null) settings.installed = new List<R1ProductionContentSettings.InstalledRecord>();
        if (settings.entries == null) settings.entries = new List<R1ProductionContentSettings.Entry>();
        foreach (var kind in Roles)
        {
            if (settings.installed.Exists(record => record != null && record.kind == kind)) continue;
            var source = AssetDatabase.LoadAssetAtPath<UnitData>(SourcePath(kind));
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(UnitFolder(kind) + "/Stats.asset");
            var defaults = AssetDatabase.LoadAssetAtPath<SpecialSkillDefinition>(DefaultsPath(kind));
            if (defaults == null || defaults.ability != Ability(kind) || data.specialSkill == null
                || data.specialSkill.ability != Ability(kind))
                throw new InvalidOperationException("別の固有能力で編集済みの下書きは上書きしません: " + kind);
            var entry = settings.entries.Find(value => value != null && value.kind == kind);
            if (entry != null && (!entry.createPlayableUnit || entry.sourceData != source || entry.skillDefaults != defaults))
                throw new InvalidOperationException("別の導入設定が登録されています: " + kind);
        }
        foreach (var kind in Roles)
        {
            if (settings.installed.Exists(record => record != null && record.kind == kind)) continue;
            var source = AssetDatabase.LoadAssetAtPath<UnitData>(SourcePath(kind));
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(UnitFolder(kind) + "/Stats.asset");
            var defaults = AssetDatabase.LoadAssetAtPath<SpecialSkillDefinition>(DefaultsPath(kind));
            ApplyTrialStats(data, source);
            ApplyTrialAbility(defaults, kind); ApplyTrialAbility(data.specialSkill, kind);
            if (!settings.entries.Exists(value => value != null && value.kind == kind))
                settings.entries.Add(new R1ProductionContentSettings.Entry
                {
                    kind = kind, sourceData = source, sourcePrefab = SourcePrefab(source), skillDefaults = defaults,
                    createPlayableUnit = true,
                    balanceNote = "Editable role prototype, separate from the confirmed R1 barrier table. Independent " + source.kind
                        + " movement/attack/vision; HP/ATK, recruitment costs and skill tuning are provisional."
                });
        }
        settings.version = TrialVersion;
        settings.balanceStatus = "New-role HP/ATK, recruitment costs and skill tuning are editable prototypes. The confirmed R1 barrier table is configured separately.";
        EditorUtility.SetDirty(settings);
        R1ProductionContentInstaller.Install(settings);
        foreach (var kind in Roles)
        {
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(UnitFolder(kind) + "/Stats.asset");
            SyncPrefabStats(data);
        }
        AssetDatabase.SaveAssets(); GameContentRevision.NotifyChanged();
        Debug.Log("[R1BarrierRoles] 重戦士・竜騎士の暫定調整値を導入。バリア初期値の確定表とは別設定です。既存16職種のHP・ATK・クリスタル・材質は変更していません。");
    }

    static void EnsureStopped()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("ゲームを停止してから追加職種を準備してください。");
    }
    static void ValidateOwnedDraft(Kind kind)
    {
        var data = AssetDatabase.LoadAssetAtPath<UnitData>(UnitFolder(kind) + "/Stats.asset");
        if (data != null && (data.kind != kind || data.definitionId != DefinitionId(kind)
            || data.actionProfile == null || data.prefab == null
            || AssetDatabase.GetAssetPath(data.prefab) != UnitFolder(kind) + "/Visual.prefab"))
            throw new InvalidOperationException("導入先に独自の駒があります。上書きしません: " + kind);
    }
    static string DefinitionId(Kind kind) => "r1.unit." + kind.ToString().ToLowerInvariant();
    static void CreateMissingDraft(Kind kind, Material white)
    {
        string folder = UnitFolder(kind);
        UnitWorkshopWindow.EnsureFolder(folder);
        UnitWorkshopWindow.EnsureFolder(R1ProductionContentInstaller.Folder + "/Defaults");
        CreateMissingAbility(DefaultsPath(kind), kind);
        var data = AssetDatabase.LoadAssetAtPath<UnitData>(folder + "/Stats.asset");
        if (data != null) return;
        // An incomplete foreign asset set is not a safe destination for an automatic clone.
        foreach (string leaf in new[] { "ActionTiles.asset", "Visual.prefab", "Ability.asset" })
            if (AssetDatabase.LoadMainAssetAtPath(folder + "/" + leaf) != null)
                throw new InvalidOperationException("導入先に既存の下書き部品があります: " + folder + "/" + leaf);
        var source = AssetDatabase.LoadAssetAtPath<UnitData>(SourcePath(kind));
        var ability = CreateMissingAbility(folder + "/Ability.asset", kind);
        data = UnityEngine.Object.Instantiate(source);
        data.name = kind.ToString(); data.kind = kind; data.definitionId = DefinitionId(kind);
        data.displayName = KindNameJP.Get(kind); data.category = "R1 Barrier Trial";
        // The new role owns its barrier table entry even if the source later gains an explicit override.
        data.baseBarrier = -1;
        data.defaultTeam = Team.Player; data.availableToPlayer = data.availableToEnemy = false;
        data.specialSkill = ability; data.prefab = null;
        data.actionProfile = R1ProductionContentInstaller.CaptureProfile(source);
        data.actionProfile.name = kind + "ActionTiles";
        AssetDatabase.CreateAsset(data.actionProfile, folder + "/ActionTiles.asset");
        AssetDatabase.CreateAsset(data, folder + "/Stats.asset");
        var root = UnityEngine.Object.Instantiate(SourcePrefab(source));
        try
        {
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = data.DisplayName; root.transform.localPosition = Vector3.zero;
            var actor = root.GetComponentInChildren<Status>(true);
            actor.kind = kind; actor.team = Team.Player; actor.type = Type.Unit; actor.direction = Direction.N;
            data.ApplyToStatus(actor, 1); data.InitializeAbilities(actor);
            var filter = root.GetComponent<MeshFilter>(); var renderer = root.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null || root.GetComponentsInChildren<MeshFilter>(true).Length != 1)
                throw new InvalidOperationException("継承元の標準モデルを確認してください: " + kind);
            filter.sharedMesh = R1UnitVisualInstaller.LoadOrCreateRoleMesh(kind);
            renderer.sharedMaterial = white;
            root.SetActive(true); data.prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Visual.prefab");
            if (data.prefab == null) throw new InvalidOperationException("下書きPrefabを保存できません: " + kind);
            EditorUtility.SetDirty(data);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static SpecialSkillDefinition CreateMissingAbility(string path, Kind kind)
    {
        var existing = AssetDatabase.LoadAssetAtPath<SpecialSkillDefinition>(path);
        if (existing != null)
        {
            if (existing.ability != Ability(kind)) throw new InvalidOperationException("別の能力設定は上書きしません: " + path);
            return existing;
        }
        var ability = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        ability.name = kind + "BarrierTrial"; ability.ability = Ability(kind);
        ability.enabledForPlay = false; ability.apCost = ability.cooldown = -1;
        ability.heavyBarrierMultiplier = ability.dragonKnightSkillMultiplier = -1;
        AssetDatabase.CreateAsset(ability, path); return ability;
    }
    static void ApplyTrialStats(UnitData data, UnitData source)
    {
        Undo.RecordObject(data, "追加職種の暫定調整値を導入");
        // Preserve values changed during draft review; only untouched inherited stats receive the proposal.
        if (data.baseHP == source.baseHP && data.baseATK == source.baseATK && data.baseDEF == source.baseDEF)
        {
            data.baseHP = data.kind == Kind.HeavyWarrior ? 44 : 36;
            data.baseATK = data.kind == Kind.HeavyWarrior ? 12 : 11;
        }
        if (data.costWood == source.costWood && data.costStone == source.costStone && data.costIron == source.costIron
            && data.costMagic == source.costMagic && data.costWater == source.costWater && data.costBread == source.costBread
            && data.costCitizen == source.costCitizen && data.costAP == source.costAP)
        {
            data.costWood = 6; data.costStone = 4; data.costIron = 16; data.costMagic = data.costWater = 0;
            data.costBread = 8; data.costCitizen = 1; data.costAP = 5;
        }
        data.availableToPlayer = data.availableToEnemy = true;
        EditorUtility.SetDirty(data);
    }
    static void ApplyTrialAbility(SpecialSkillDefinition ability, Kind kind)
    {
        Undo.RecordObject(ability, "追加職種スキルの暫定調整値を導入");
        if (ability.apCost < 0) ability.apCost = kind == Kind.HeavyWarrior ? 14 : 16;
        if (ability.cooldown < 0) ability.cooldown = 0;
        if (ability.heavyBarrierMultiplier < 0) ability.heavyBarrierMultiplier = 2;
        if (ability.dragonKnightSkillMultiplier < 0) ability.dragonKnightSkillMultiplier = 1.5f;
        ability.enabledForPlay = true; EditorUtility.SetDirty(ability);
    }
    static void SyncPrefabStats(UnitData data)
    {
        string path = AssetDatabase.GetAssetPath(data.prefab);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var actor = root.GetComponentInChildren<Status>(true);
            data.ApplyToStatus(actor, 1); data.InitializeAbilities(actor);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
#endif
