using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>Editor/build boundary: validate registered content without changing assets or stable IDs.</summary>
public static class LongTermContentValidation
{
    public sealed class Result
    {
        const int MaximumMessages = 256;
        readonly List<string> messages = new List<string>();
        public IReadOnlyList<string> Errors => messages;
        public int ErrorCount { get; private set; }
        public bool IsValid => ErrorCount == 0;
        public void Error(string location, string message)
        {
            ErrorCount++;
            if (messages.Count < MaximumMessages) messages.Add(location + ": " + message);
        }
        public string Summary()
        {
            int visible = Math.Min(20, messages.Count);
            var text = new System.Text.StringBuilder("登録コンテンツの検証: " + ErrorCount + " 件のエラー");
            for (int i = 0; i < visible; i++) text.Append('\n').Append(messages[i]);
            if (ErrorCount > visible) text.Append("\nほか ").Append(ErrorCount - visible).Append(" 件。検証メニューで確認してください。");
            return text.ToString();
        }
    }

    [MenuItem("Fantasy Kingdom/長期運用/全コンテンツと保存用IDを検証")]
    public static void RunAll()
    {
        var result = ValidateProject();
        if (result.IsValid) Debug.Log("[ContentValidation] 登録コンテンツ・保存用IDの検証に成功しました。");
        else
        {
            foreach (string message in result.Errors) Debug.LogError("[ContentValidation] " + message);
            Debug.LogError(result.Summary());
        }
    }

    public static void RequireValidForBuild()
    {
        var result = ValidateProject();
        if (!result.IsValid) throw new BuildFailedException(result.Summary());
    }

    public static Result ValidateProject()
    {
        var result = new Result();
        var units = ResolveResource<UnitAuthoringCatalog>(UnitAuthoringCatalog.ResourcePath, result);
        var buildings = ResolveResource<FacilityAuthoringCatalog>(FacilityAuthoringCatalog.ResourcesPath, result);
        var encounters = ResolveResource<R1ContentCatalog>("R1ContentCatalog", result);
        var director = ResolveResource<ThirdFactionDirectorConfig>("AI/ThirdFaction/DirectorConfig", result);
        var rules = ResolveResource<GameAuthoringRules>(GameAuthoringRules.ResourcesPath, result);
        Validate(units, buildings, encounters, director, rules, result);
        return result;
    }

    // Only Resources registrations are live. Unregistered editing copies and legacy UnitData
    // assets with no definitionId are deliberately not treated as new persistent definitions.
    static T ResolveResource<T>(string key, Result result) where T : UnityEngine.Object
    {
        string suffix = "/Resources/" + key + ".asset";
        T found = null;
        foreach (string path in AssetDatabase.GetAllAssetPaths())
        {
            if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) { result.Error(path, "Resourcesの登録データの型が一致しません。"); continue; }
            if (found != null) result.Error(path, "Resourcesの読込名「" + key + "」が重複しています。");
            else found = asset;
        }
        return found;
    }

    public static Result Validate(UnitAuthoringCatalog units, FacilityAuthoringCatalog buildings,
        R1ContentCatalog encounters, ThirdFactionDirectorConfig director, GameAuthoringRules rules = null)
    {
        var result = new Result(); Validate(units, buildings, encounters, director, rules, result); return result;
    }

    static void Validate(UnitAuthoringCatalog units, FacilityAuthoringCatalog buildings,
        R1ContentCatalog encounters, ThirdFactionDirectorConfig director, GameAuthoringRules rules, Result result)
    {
        ValidateUnits(units, result); ValidateBuildings(buildings, result);
        var encounterIds = new HashSet<string>(StringComparer.Ordinal);
        var rewardIds = new Dictionary<string, R1ContentCatalog.UniqueReward>(StringComparer.Ordinal);
        if (encounters != null)
        {
            ValidateEncounters(encounters.Monsters, "魔物の遭遇", encounterIds, rewardIds, result);
            ValidateEncounters(encounters.Intruders, "乱入者の遭遇", encounterIds, rewardIds, result);
            if (encounters.BossArtifacts != null)
                foreach (var reward in encounters.BossArtifacts) ValidateReward(reward, "強敵の報酬", true, rewardIds, result);
            var kinds = new HashSet<Kind>();
            if (encounters.FixedSkills != null) foreach (var skill in encounters.FixedSkills)
            {
                if (skill == null) { result.Error("固定スキル", "空の登録があります。"); continue; }
                if (!Enum.IsDefined(typeof(Kind), skill.Kind) || skill.Kind == Kind.None) result.Error("固定スキル", "基礎役割が不正です。");
                if (!kinds.Add(skill.Kind)) result.Error("固定スキル", skill.Kind + " が重複しています。");
                if (!SkillData.Table.ContainsKey(skill.SkillId)) result.Error("固定スキル", "存在しないスキルID: " + skill.SkillId);
            }
        }
        ValidateDirector(director, encounterIds, rewardIds, result);
        ValidateRules(rules, result);
    }

    static void ValidateUnits(UnitAuthoringCatalog catalog, Result result)
    {
        if (catalog == null) return; // Optional catalogs preserve legacy scene settings.
        if (catalog.units == null) { result.Error(Location(catalog), "駒の登録一覧がありません。"); return; }
        var ids = new HashSet<string>(StringComparer.Ordinal); var legacyKinds = new HashSet<Kind>();
        var definitions = new List<UnitAuthoringCatalog.Entry>(catalog.units);
        if (catalog.retiredUnits != null) definitions.AddRange(catalog.retiredUnits);
        for (int i = 0; i < definitions.Count; i++)
        {
            var entry = definitions[i]; string at = Location(catalog) + (i < catalog.units.Count ? " / 駒 " : " / 保管済みの駒 ") + (i + 1);
            if (entry == null || entry.data == null) { result.Error(at, "能力データがありません。"); continue; }
            var data = entry.data;
            if (!Enum.IsDefined(typeof(Kind), entry.kind) || entry.kind == Kind.None || data.kind != entry.kind)
                result.Error(at, "基礎役割が能力データと一致しないか、不正です。");
            if (!data.IsValidForAuthoring) result.Error(at, "HP・費用・維持費に不正な値があります。");
            if (entry.standaloneDefinition || !string.IsNullOrEmpty(data.definitionId)) AddId(data.definitionId, at, ids, result);
            if (i < catalog.units.Count && !entry.standaloneDefinition && !legacyKinds.Add(entry.kind)) result.Error(at, "既存兵種の上書きが重複しています。");
            ValidatePrefab(data.prefab != null && entry.standaloneDefinition ? data.prefab : entry.prefab, at, result);
            ValidateAbilities(data, at, result); ValidateProfile(data.actionProfile, at, result);
        }
    }

    static void ValidateBuildings(FacilityAuthoringCatalog catalog, Result result)
    {
        if (catalog == null) return;
        if (catalog.buildings == null) { result.Error(Location(catalog), "建物の登録一覧がありません。"); return; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new List<FacilityDefinitionData>(catalog.buildings);
        var standardKinds = new HashSet<FacilityKind>();
        if (catalog.standardOverrides != null) foreach (var definition in catalog.standardOverrides)
        {
            definitions.Add(definition);
            if (definition != null && !standardKinds.Add(definition.behaviourKind)) result.Error(Location(catalog), "標準建物の上書き役割が重複しています。");
        }
        if (catalog.retiredDefinitions != null) definitions.AddRange(catalog.retiredDefinitions);
        for (int i = 0; i < definitions.Count; i++)
        {
            var data = definitions[i]; string at = Location(catalog) + " / 建物 " + (i + 1);
            if (data == null) { result.Error(at, "建物データがありません。"); continue; }
            AddId(data.definitionId, at, ids, result);
            if (!data.IsValid || !Enum.IsDefined(typeof(FacilityKind), data.behaviourKind)) result.Error(at, "基本動作またはレベル設定が不正です。");
            if (data.buildAP < 0 || data.buildAP > GameConstants.MaxAuthoredActionAPCost || !ValidCost(data.buildCost)) result.Error(at, "建築費またはAPが不正です。");
            ValidatePrefab(data.prefab, at, result); ValidateProfile(data.actionProfile, at, result);
            if (data.levels == null) continue;
            for (int level = 0; level < data.levels.Length; level++)
            {
                var value = data.levels[level];
                if (value.HP < 1 || value.ATK < 0 || value.DEF < 0 || value.UpgradeAP < 0
                    || value.UpgradeAP > GameConstants.MaxAuthoredActionAPCost || !ValidCost(value.UpgradeCost)
                    || !ValidBundle(value.Input) || !ValidBundle(value.Output) || !ValidBundle(value.Maintenance)
                    || !ValidBundle(value.BonusOutput1) || !ValidBundle(value.BonusOutput2)
                    || !Probability(value.BonusChance1) || !Probability(value.BonusChance2) || value.SpecialValue < 0)
                    result.Error(at + " / Lv " + (level + 1), "能力・材料・生産量・確率・強化費に不正な値があります。");
            }
        }
    }

    static void ValidateEncounters(List<R1ContentCatalog.Encounter> entries, string group, HashSet<string> ids,
        Dictionary<string, R1ContentCatalog.UniqueReward> rewards, Result result)
    {
        if (entries == null) return;
        for (int i = 0; i < entries.Count; i++)
        {
            var data = entries[i]; string at = group + " " + (i + 1);
            if (data == null) { result.Error(at, "遭遇データがありません。"); continue; }
            AddId(data.Id, at, ids, result); ValidatePrefab(data.Prefab, at, result);
            ValidateEncounterStats(data.Stats, at, result);
            if (data.Count < 1 || data.FirstRound < 1 || data.VisionRange < 1) result.Error(at, "出現数・開始ターン・視界が不正です。");
            ValidateReward(data.Relic, at, false, rewards, result);
            if (data.Retinue != null) foreach (var prefab in data.Retinue) ValidatePrefab(prefab, at + " / 随伴駒", result);
        }
    }

    static void ValidateDirector(ThirdFactionDirectorConfig config, HashSet<string> encounters,
        Dictionary<string, R1ContentCatalog.UniqueReward> rewards, Result result)
    {
        if (config == null) return;
        if (config.Events == null) { result.Error(Location(config), "イベントの登録一覧がありません。"); return; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var data in config.Events)
        {
            string at = data != null ? Location(data) : Location(config);
            if (data == null) { result.Error(at, "空のイベント登録があります。"); continue; }
            AddId(data.EventId, at, ids, result);
            if (!Enum.IsDefined(typeof(ThirdFactionEventCategory), data.Category)
                || !Enum.IsDefined(typeof(ThirdFactionEventScale), data.Scale)
                || !Enum.IsDefined(typeof(ThirdFactionSpawnPolicy), data.SpawnPolicy)
                || !Enum.IsDefined(typeof(ThirdFactionObjectiveKind), data.Objective)) result.Error(at, "イベントの種類が不正です。");
            if (data.IpCost < 0 || data.IpCost > ThirdFactionDirectorConfig.MaxIP || data.MinThreatLevel < 1
                || data.MaxThreatLevel < data.MinThreatLevel || data.CooldownTurns < 1 || data.MajorCooldownTurns < 0
                || data.ObjectiveTurns < 1 || !Probability(data.EstimatedPowerSwing)
                || !Probability(data.EstimatedDisruption) || !Probability(data.MinimumStagnation)) result.Error(at, "費用・発生条件・期間・確率が不正です。");
            if (data.Category != ThirdFactionEventCategory.StrongEnemyExpansion)
            {
                if (!string.IsNullOrWhiteSpace(data.EncounterId))
                {
                    if (!encounters.Contains(data.EncounterId)) result.Error(at, "参照先の遭遇IDがありません: " + data.EncounterId);
                }
                else { ValidatePrefab(data.Prefab, at, result); ValidateEncounterStats(data.Stats, at, result); }
                if (data.SpawnTeam != Team.Monster && data.SpawnTeam != Team.Intruder) result.Error(at, "襲撃の陣営は魔物または乱入者を指定してください。");
                if (data.SpawnCount < 1) result.Error(at, "出現数は1以上にしてください。");
            }
            else if (data.LocalExpansionRadius < 1) result.Error(at, "領土拡大の半径は1以上にしてください。");
            ValidateReward(data.Reward, at, false, rewards, result);
        }
    }

    static void ValidateEncounterStats(UnitData data, string at, Result result)
    {
        if (data == null) { result.Error(at, "出現する駒の能力データがありません。"); return; }
        if (!data.IsValidForAuthoring || !Enum.IsDefined(typeof(Kind), data.kind) || data.kind == Kind.None) result.Error(at, "出現する駒の能力・基礎役割が不正です。");
        ValidateAbilities(data, at, result); ValidateProfile(data.actionProfile, at, result);
    }

    static void ValidateAbilities(UnitData data, string at, Result result)
    {
        if (!data.useAuthoredAbilities) return;
        if (data.authoredSkillId != -1 && !SkillData.Table.ContainsKey(data.authoredSkillId)) result.Error(at, "存在しない固有スキルID: " + data.authoredSkillId);
        if (!Enum.IsDefined(typeof(PassiveSkill), data.authoredPassive) || !Enum.IsDefined(typeof(SpecialAbility), data.authoredSpecialAbility)) result.Error(at, "固有能力の種類が不正です。");
    }

    static void ValidateProfile(BoardActionProfile profile, string at, Result result)
    {
        if (profile == null) return; // Legacy patterns remain valid.
        if (!Enum.IsDefined(typeof(ActorActionType), profile.actionType)) result.Error(at, "行動タイプが不正です。");
        ValidatePattern(profile.movement, at + " / 移動", result); ValidatePattern(profile.attack, at + " / 攻撃", result); ValidatePattern(profile.vision, at + " / 視界", result);
    }
    static void ValidatePattern(BoardTilePattern pattern, string at, Result result)
    {
        if (pattern == null) { result.Error(at, "マス設定がありません。"); return; }
        if (pattern.useCustom && (pattern.originX < 0 || pattern.originX >= BoardTilePattern.Size
            || pattern.originZ < 0 || pattern.originZ >= BoardTilePattern.Size)) result.Error(at, "8×8マスの原点が範囲外です。");
    }

    static void ValidateReward(R1ContentCatalog.UniqueReward reward, string at, bool required,
        Dictionary<string, R1ContentCatalog.UniqueReward> ids, Result result)
    {
        if (reward == null || string.IsNullOrWhiteSpace(reward.Id))
        { if (required) result.Error(at, "報酬の保存用IDがありません。"); return; }
        if (!ValidId(reward.Id)) result.Error(at, "報酬の保存用IDに前後の空白や制御文字があります。");
        if (ids.TryGetValue(reward.Id, out var previous) && !ReferenceEquals(previous, reward)) result.Error(at, "別の報酬と保存用IDが重複しています: " + reward.Id);
        else ids[reward.Id] = reward;
    }

    static void ValidateRules(GameAuthoringRules rules, Result result)
    {
        if (rules == null || !rules.applyRules) return;
        string at = Location(rules);
        // Historical clock fields are inert; old zero clocks must not invalidate an R2 rules asset.
        if (!Finite(rules.noiseScale) || rules.noiseScale <= 0
            || !Finite(rules.riverHalfWidth) || rules.riverHalfWidth < 0 || rules.maximumAP < 3 || rules.maximumAP > GameConstants.MaxAP)
            result.Error(at, "有効なゲームルールの地形・AP設定が不正です。");
        var teams = new HashSet<Team>();
        if (rules.factions == null) { result.Error(at, "陣営一覧がありません。"); return; }
        foreach (var faction in rules.factions)
        {
            if (faction == null) { result.Error(at, "空の陣営設定があります。"); continue; }
            if (!Enum.IsDefined(typeof(Team), faction.team) || !teams.Add(faction.team)) result.Error(at, "陣営の種類が不正または重複しています。");
            if (faction.initialResources == null) { result.Error(at, "陣営の初期資源がありません。"); continue; }
            var value = faction.initialResources;
            if (value.Wood < 0 || value.Stone < 0 || value.Iron < 0 || value.MagicOre < 0
                || value.Wheat < 0 || value.Bread < 0 || value.Water < 0 || value.Citizen < 0) result.Error(at, "陣営の初期資源は0以上にしてください。");
        }
    }

    static void ValidatePrefab(GameObject prefab, string at, Result result)
    {
        if (prefab == null) result.Error(at, "Prefab参照がありません。");
        else if (prefab.GetComponentsInChildren<Status>(true).Length != 1) result.Error(at, "Prefabには駒1体分のStatusが必要です。");
    }
    static void AddId(string id, string at, HashSet<string> ids, Result result)
    {
        if (!ValidId(id)) { result.Error(at, "保存用IDが空欄か、前後の空白・制御文字があります。"); return; }
        if (!ids.Add(id)) result.Error(at, "保存用IDが重複しています: " + id);
    }
    static bool ValidId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !string.Equals(id, id.Trim(), StringComparison.Ordinal)) return false;
        foreach (char c in id) if (char.IsControl(c)) return false;
        return true;
    }
    static string Location(UnityEngine.Object value)
    { string path = AssetDatabase.GetAssetPath(value); return string.IsNullOrEmpty(path) ? value.name : path; }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Probability(float value) => Finite(value) && value >= 0 && value <= 1;
    static bool ValidCost(FacilityData.ResourceCost value) => value.Wood >= 0 && value.Stone >= 0 && value.Iron >= 0 && value.MagicOre >= 0 && value.Water >= 0 && value.Citizen >= 0;
    static bool ValidBundle(FacilityData.ProductionBundle value) => value.Wood >= 0 && value.Stone >= 0 && value.Iron >= 0 && value.MagicOre >= 0 && value.Wheat >= 0 && value.Bread >= 0 && value.Water >= 0 && value.Citizen >= 0;
}
