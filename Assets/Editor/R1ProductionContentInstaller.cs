using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit one-time migration. No initialization hooks or runtime asset mutation.</summary>
public static class R1ProductionContentInstaller
{
    public const string Folder = "Assets/_FantasyKingdom/GameContent/R1Production";
    public const string SettingsPath = Folder + "/R1ProductionSettings.asset";

    [MenuItem("Fantasy Kingdom/駒固有能力/R1本番データを導入（未設定値のみ）")]
    public static void Install()
    {
        var settings = AssetDatabase.LoadAssetAtPath<R1ProductionContentSettings>(SettingsPath);
        if (settings == null) throw new InvalidOperationException("R1本番導入設定がありません: " + SettingsPath);
        Install(settings);
    }

    [MenuItem("Fantasy Kingdom/駒固有能力/R1本番導入設定を開く")]
    public static void OpenSettings() => Selection.activeObject = AssetDatabase.LoadAssetAtPath<R1ProductionContentSettings>(SettingsPath);

    public static void Install(R1ProductionContentSettings settings)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("ゲームを停止してからR1を導入してください。");
        ValidateSettings(settings);
        var catalog = UnitWorkshopWindow.GetCatalog();
        if (settings.installed == null) settings.installed = new List<R1ProductionContentSettings.InstalledRecord>();
        ValidateTargets(settings, catalog);
        int installed = 0, preserved = 0;
        foreach (var entry in settings.entries)
        {
            var previous = settings.installed.Find(record => record != null && record.kind == entry.kind);
            // An explicit record protects later tuning, intentional disabling, and catalog replacements.
            if (previous != null) { preserved++; continue; }
            UnitData data = entry.createPlayableUnit ? CreateProductionUnit(entry) : entry.sourceData;
            var ability = data.specialSkill;
            if (ability == null)
            {
                string path = UnitFolder(entry.kind) + "/Ability.asset";
                ability = LoadOrCreateAbility(path, entry.skillDefaults);
                Undo.RecordObject(data, "R1固有能力を接続"); data.specialSkill = ability; EditorUtility.SetDirty(data);
            }
            if (ability.ability != entry.skillDefaults.ability)
                throw new InvalidOperationException(data.name + " は別の固有能力を使用しています。既存設定を保護するため導入を停止しました。");
            // The old disabled Archer template stored 0 for its former passive.
            // Recognize that exact unfinished shape; preserve an authored active 0 AP.
            if (IsLegacyArcherDraft(ability))
            {
                Undo.RecordObject(ability, "弓兵の旧常時能力APを能動スキルAPへ移行");
                ability.apCost = entry.skillDefaults.apCost;
            }
            FillUnconfiguredValues(ability, entry.skillDefaults);
            Undo.RecordObject(ability, "R1固有能力を有効化");
            ability.enabledForPlay = true; EditorUtility.SetDirty(ability);
            if (ability.ConfigurationError != null) throw new InvalidOperationException(data.name + ": " + ability.ConfigurationError);
            if (entry.createPlayableUnit) RegisterNewRole(catalog, data);
            settings.installed.Add(new R1ProductionContentSettings.InstalledRecord
            { kind = entry.kind, data = data, ability = ability, version = settings.version });
            EditorUtility.SetDirty(settings); installed++;
        }
        AssetDatabase.SaveAssets(); GameContentRevision.NotifyChanged();
        Debug.Log($"[R1Production] Installed {installed}; preserved {preserved} previously installed roles. Tuning: {settings.balanceStatus}");
    }

    public static void ValidateSettings(R1ProductionContentSettings settings)
    {
        if (settings == null || settings.entries == null || settings.entries.Count == 0) throw new ArgumentException("R1導入設定が空です。");
        var kinds = new HashSet<Kind>();
        foreach (var entry in settings.entries)
        {
            if (entry == null || !kinds.Add(entry.kind) || entry.sourceData == null || !entry.sourceData.IsValidForAuthoring
                || entry.skillDefaults == null || !SpecialSkillAuthoringDefaults.TryAbility(entry.kind, out var ability)
                || entry.skillDefaults.ability != ability || entry.skillDefaults.ConfigurationError != null)
                throw new ArgumentException("R1導入設定の駒・能力・値が不正です: " + (entry == null ? "null" : entry.kind.ToString()));
            if (entry.createPlayableUnit && (entry.sourcePrefab == null || entry.sourcePrefab.GetComponentsInChildren<Status>(true).Length != 1
                || entry.sourcePrefab.GetComponentInChildren<Renderer>(true) == null || entry.sourcePrefab.GetComponentInChildren<Collider>(true) == null))
                throw new ArgumentException("新しい駒には表示・選択可能な駒1体のPrefabが必要です: " + entry.kind);
            if (!entry.createPlayableUnit && entry.sourceData.kind != entry.kind) throw new ArgumentException("導入対象の兵種が一致しません: " + entry.kind);
            if (!entry.createPlayableUnit && entry.sourceData.specialSkill != null && entry.sourceData.specialSkill.ability != ability)
                throw new ArgumentException("導入対象には異なる固有能力が設定されています: " + entry.kind);
        }
    }

    static string UnitFolder(Kind kind) => Folder + "/Units/" + kind;

    static bool IsLegacyArcherDraft(SpecialSkillDefinition ability) => !ability.enabledForPlay
        && ability.ability == UnitSpecialSkill.ArcherStationary && ability.apCost == 0
        && ability.cooldown < 0 && !ability.HasCurve(ability.stationaryAttackCurve);

    static void ValidateTargets(R1ProductionContentSettings settings, UnitAuthoringCatalog catalog)
    {
        foreach (var entry in settings.entries)
        {
            if (settings.installed.Exists(record => record != null && record.kind == entry.kind)) continue;
            UnitData data = entry.createPlayableUnit
                ? AssetDatabase.LoadAssetAtPath<UnitData>(UnitFolder(entry.kind) + "/Stats.asset") : entry.sourceData;
            if (entry.createPlayableUnit)
            {
                var previous = catalog.units.Find(item => item != null && !item.standaloneDefinition && item.kind == entry.kind);
                if (previous != null && previous.data != data)
                    throw new InvalidOperationException("既に別の駒が登録されています。カタログを上書きしません: " + entry.kind);
                if (data != null && (data.kind != entry.kind || data.definitionId != "r1.unit." + entry.kind.ToString().ToLowerInvariant()
                    || data.actionProfile == null || data.prefab == null))
                    throw new InvalidOperationException("既存の導入先データを保護するため停止しました: " + entry.kind);
                var profile = CaptureProfile(entry.sourceData);
                try
                {
                    if (entry.attackPatternSource != null)
                        profile.attack = CapturePattern(entry.attackPatternSource.actionProfile?.attack, entry.attackPatternSource.kind, 1);
                }
                finally { UnityEngine.Object.DestroyImmediate(profile); }
            }
            var original = data != null ? data.specialSkill : null;
            if (original != null && original.ability != entry.skillDefaults.ability)
                throw new InvalidOperationException("既存の固有能力を保護するため停止しました: " + entry.kind);
            var candidate = UnityEngine.Object.Instantiate(original != null ? original : entry.skillDefaults);
            try
            {
                if (IsLegacyArcherDraft(candidate)) candidate.apCost = entry.skillDefaults.apCost;
                FillUnconfiguredValues(candidate, entry.skillDefaults); candidate.enabledForPlay = true;
                if (candidate.ConfigurationError != null)
                    throw new InvalidOperationException("既存の調整値を保護するため停止しました: " + entry.kind + ": " + candidate.ConfigurationError);
            }
            finally { UnityEngine.Object.DestroyImmediate(candidate); }
        }
    }

    static UnitData CreateProductionUnit(R1ProductionContentSettings.Entry entry)
    {
        string folder = UnitFolder(entry.kind), path = folder + "/Stats.asset";
        UnitWorkshopWindow.EnsureFolder(folder);
        var existing = AssetDatabase.LoadAssetAtPath<UnitData>(path);
        if (existing != null)
        {
            if (existing.kind != entry.kind || existing.definitionId != "r1.unit." + entry.kind.ToString().ToLowerInvariant())
                throw new InvalidOperationException("導入先に既存の駒があります。上書きしません: " + path);
            return existing;
        }
        var data = UnityEngine.Object.Instantiate(entry.sourceData);
        data.name = entry.kind.ToString(); data.kind = entry.kind;
        data.displayName = KindNameJP.Get(entry.kind); data.category = "R1";
        data.definitionId = "r1.unit." + entry.kind.ToString().ToLowerInvariant();
        data.specialSkill = null; data.prefab = null;
        data.actionProfile = CaptureProfile(entry.sourceData);
        if (entry.attackPatternSource != null)
            data.actionProfile.attack = CapturePattern(entry.attackPatternSource.actionProfile?.attack, entry.attackPatternSource.kind, 1);
        data.actionProfile.name = entry.kind + "ActionTiles";
        AssetDatabase.CreateAsset(data.actionProfile, folder + "/ActionTiles.asset");
        AssetDatabase.CreateAsset(data, path);
        var root = UnityEngine.Object.Instantiate(entry.sourcePrefab);
        try
        {
            root.name = data.DisplayName;
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.transform.localPosition = Vector3.zero;
            var actor = root.GetComponentInChildren<Status>(true);
            actor.kind = entry.kind; actor.team = Team.Player; actor.type = Type.Unit; actor.direction = Direction.N;
            data.ApplyToStatus(actor, 1); data.InitializeAbilities(actor);
            root.SetActive(true);
            data.prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Visual.prefab");
            if (data.prefab == null) throw new InvalidOperationException("R1駒のPrefabを保存できません: " + entry.kind);
            EditorUtility.SetDirty(data);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        return data;
    }

    static SpecialSkillDefinition LoadOrCreateAbility(string path, SpecialSkillDefinition defaults)
    {
        var existing = AssetDatabase.LoadAssetAtPath<SpecialSkillDefinition>(path);
        if (existing != null) return existing;
        UnitWorkshopWindow.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        var ability = UnityEngine.Object.Instantiate(defaults); ability.name = defaults.ability.ToString();
        AssetDatabase.CreateAsset(ability, path); return ability;
    }

    static void RegisterNewRole(UnitAuthoringCatalog catalog, UnitData data)
    {
        var previous = catalog.units.Find(item => item != null && !item.standaloneDefinition && item.kind == data.kind);
        if (previous != null)
        {
            if (previous.data != data) throw new InvalidOperationException("既に別の駒が登録されています。カタログを上書きしません: " + data.kind);
            return;
        }
        Undo.RecordObject(catalog, "R1新兵種を召喚へ登録");
        catalog.units.Add(new UnitAuthoringCatalog.Entry { kind = data.kind, data = data, prefab = data.prefab });
        EditorUtility.SetDirty(catalog);
    }

    /// <summary>Only serialized sentinel values are filled; authored non-sentinel values are retained.</summary>
    public static void FillUnconfiguredValues(SpecialSkillDefinition target, SpecialSkillDefinition defaults)
    {
        Undo.RecordObject(target, "R1未設定値を導入");
        var destination = new SerializedObject(target); var source = new SerializedObject(defaults);
        foreach (string field in new[] { "apCost", "cooldown", "knightVisibleEnemyAtkPenalty", "sacrificeLimit", "bombExplosionRadius",
            "samuraiGaugeGain", "samuraiGaugeMax", "assassinBoostDuration", "bomberAutoExplosionRadius", "crossbowInitialAmmo",
            "heavyBarrierMultiplier", "dragonKnightSkillMultiplier" })
        {
            var value = destination.FindProperty(field); var supplied = source.FindProperty(field);
            if (value.propertyType == SerializedPropertyType.Float ? value.floatValue < 0 : value.intValue < 0)
                destination.CopyFromSerializedProperty(supplied);
        }
        foreach (string field in new[] { "knightPenaltyStackRule", "assassinBoostStackRule", "scoutFreeMoveScope", "scoutFreeAttackScope",
            "guardianAttackCostReset", "bombFactionMask", "bombOnHolderDeathPolicy", "lightningFactionMask" })
            if (destination.FindProperty(field).intValue == 0) destination.CopyFromSerializedProperty(source.FindProperty(field));
        foreach (string field in new[] { "stationaryAttackCurve", "sniperRecoilCurve", "ninjaChargeCurve", "berserkerAttackCurve",
            "berserkerDefenceCurve", "berserkerAttackAPCurve", "navigatorAPDamageCurve" })
            if (destination.FindProperty(field).animationCurveValue.length == 0) destination.CopyFromSerializedProperty(source.FindProperty(field));
        if (destination.FindProperty("holyKnightArea").arraySize == 0) destination.CopyFromSerializedProperty(source.FindProperty("holyKnightArea"));
        if (!destination.FindProperty("holyKnightInterruptionConfigured").boolValue)
        {
            destination.CopyFromSerializedProperty(source.FindProperty("holyKnightInterruptionConfigured"));
            destination.CopyFromSerializedProperty(source.FindProperty("holyKnightInterruptOnDamage"));
        }
        destination.ApplyModifiedProperties(); EditorUtility.SetDirty(target);
    }

    public static BoardActionProfile CaptureProfile(UnitData source)
    {
        var profile = source.actionProfile != null ? UnityEngine.Object.Instantiate(source.actionProfile) : ScriptableObject.CreateInstance<BoardActionProfile>();
        try
        {
            profile.movement = CapturePattern(source.actionProfile?.movement, source.kind, 0);
            profile.attack = CapturePattern(source.actionProfile?.attack, source.kind, 1);
            profile.vision = CapturePattern(source.actionProfile?.vision, source.kind, 2);
            return profile;
        }
        catch { UnityEngine.Object.DestroyImmediate(profile); throw; }
    }

    static BoardTilePattern CapturePattern(BoardTilePattern original, Kind kind, int mode)
    {
        if (original != null && original.useCustom) return JsonUtility.FromJson<BoardTilePattern>(JsonUtility.ToJson(original));
        var offsets = new HashSet<Vector2Int>();
        if (mode == 2)
        {
            foreach (var point in VisionGenerator.BaseVisionOffsets(kind)) offsets.Add(new Vector2Int(point.x, point.z));
            offsets.Add(Vector2Int.zero);
        }
        else
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
                if ((x != 0 || z != 0) && (mode == 0 ? MovePatterns.CanMove(kind, Direction.N, x, z) : AttackPatterns.CanAttack(kind, Direction.N, x, z)))
                    offsets.Add(new Vector2Int(x, z));
        int minX = 0, maxX = 0, minZ = 0, maxZ = 0;
        foreach (var point in offsets)
        { minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x); minZ = Mathf.Min(minZ, point.y); maxZ = Mathf.Max(maxZ, point.y); }
        if (maxX - minX >= BoardTilePattern.Size || maxZ - minZ >= BoardTilePattern.Size)
            throw new InvalidOperationException("継承する行動範囲は8×8へ切り捨てずに保存できません: " + kind);
        var result = new BoardTilePattern { useCustom = true, originX = Mathf.Clamp(3, -minX, 7 - maxX), originZ = Mathf.Clamp(3, -minZ, 7 - maxZ),
            directionIndependent = mode == 0 ? MovePatterns.DirectionIndependent.Contains(kind) : mode == 1 && AttackPatterns.DirectionIndependent.Contains(kind) };
        foreach (var point in offsets) result.SetCell(point.x + result.originX, point.y + result.originZ, true);
        return result;
    }
}
