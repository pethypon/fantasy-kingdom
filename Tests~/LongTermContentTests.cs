#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Content/build boundary regression, using the existing isolated Editor harness.</summary>
public static class LongTermContentTests
{
    static int checks;
    static void Check(string name, bool value)
    {
        if (!value) throw new Exception("[LongTermContent] FAIL " + name);
        checks++; Debug.Log("[LongTermContent] PASS " + name);
    }
    static bool Has(LongTermContentValidation.Result result, string fragment) => result.Errors.Any(e => e.Contains(fragment));

    public static void EditMode()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new Exception("Content regression requires isolated validation project.");
        checks = 0;
        var owned = new List<UnityEngine.Object>();
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "FantasyKingdomBuildInfo-" + Guid.NewGuid().ToString("N"));
        var units = ScriptableObject.CreateInstance<UnitAuthoringCatalog>(); owned.Add(units);
        var buildings = ScriptableObject.CreateInstance<FacilityAuthoringCatalog>(); owned.Add(buildings);
        var encounters = ScriptableObject.CreateInstance<R1ContentCatalog>(); owned.Add(encounters);
        var director = ScriptableObject.CreateInstance<ThirdFactionDirectorConfig>(); owned.Add(director);
        var prefab = new GameObject("content fixture"); owned.Add(prefab); prefab.AddComponent<Status>();
        var unit = UnitStaticData.CreateUnitData(Kind.Knight); owned.Add(unit);
        unit.definitionId = "unit.fixture.one"; unit.prefab = prefab;
        var entry = new UnitAuthoringCatalog.Entry { kind = unit.kind, data = unit, prefab = prefab, standaloneDefinition = true };
        units.units.Add(entry);
        var building = ScriptableObject.CreateInstance<FacilityDefinitionData>(); owned.Add(building);
        building.definitionId = "building.fixture.one"; building.prefab = prefab; buildings.buildings.Add(building);
        try
        {
            Check("valid authored registrations", LongTermContentValidation.Validate(units, buildings, encounters, director).IsValid);
            units.units.Add(entry);
            Check("duplicate unit IDs fail", Has(LongTermContentValidation.Validate(units, null, null, null), "保存用IDが重複"));
            units.units.RemoveAt(1);
            unit.definitionId = "";
            Check("standalone empty ID fails", !LongTermContentValidation.Validate(units, null, null, null).IsValid);
            entry.standaloneDefinition = false;
            Check("legacy override accepts empty definition ID", LongTermContentValidation.Validate(units, null, null, null).IsValid);
            units.units.Add(entry);
            Check("duplicate legacy role fails", Has(LongTermContentValidation.Validate(units, null, null, null), "上書きが重複"));
            units.units.RemoveAt(1); entry.standaloneDefinition = true; unit.definitionId = "unit.fixture.one";
            unit.displayName = "名前を変更";
            Check("display rename preserves stable ID", unit.definitionId == "unit.fixture.one" && LongTermContentValidation.Validate(units, null, null, null).IsValid);
            unit.prefab = null; entry.prefab = null;
            Check("missing registered prefab fails", Has(LongTermContentValidation.Validate(units, null, null, null), "Prefab参照"));
            unit.prefab = prefab; entry.prefab = prefab;
            var child = new GameObject("duplicate status"); owned.Add(child); child.transform.SetParent(prefab.transform); child.AddComponent<Status>();
            Check("multiple actors in one prefab fail", Has(LongTermContentValidation.Validate(units, null, null, null), "駒1体分"));
            UnityEngine.Object.DestroyImmediate(child);
            unit.useAuthoredAbilities = true; unit.authoredSkillId = int.MaxValue;
            Check("unknown authored skill ID fails", Has(LongTermContentValidation.Validate(units, null, null, null), "固有スキルID"));
            unit.authoredSkillId = -1;
            Check("explicit no-skill remains valid", LongTermContentValidation.Validate(units, null, null, null).IsValid);
            var mask = ScriptableObject.CreateInstance<BoardActionProfile>(); owned.Add(mask); unit.actionProfile = mask;
            mask.movement.useCustom = true; mask.movement.originX = 8;
            Check("out of bounds tile origin fails", Has(LongTermContentValidation.Validate(units, null, null, null), "原点が範囲外"));
            mask.movement.originX = 3; mask.movement.SetCell(7, 7, true);
            Check("bit 63 mask remains supported", LongTermContentValidation.Validate(units, null, null, null).IsValid);
            mask.vision = null;
            Check("missing pattern fails", Has(LongTermContentValidation.Validate(units, null, null, null), "マス設定がありません"));
            mask.vision = new BoardTilePattern();
            buildings.buildings.Add(building);
            Check("duplicate building IDs fail", Has(LongTermContentValidation.Validate(null, buildings, null, null), "保存用IDが重複"));
            buildings.buildings.RemoveAt(1);
            building.levels[0].BonusChance1 = float.NaN;
            Check("invalid production probability fails", !LongTermContentValidation.Validate(null, buildings, null, null).IsValid);
            building.levels[0].BonusChance1 = 0;
            var encounter = new R1ContentCatalog.Encounter { Id = "encounter.one", Prefab = prefab, Stats = unit };
            encounters.Monsters.Add(encounter); encounters.Intruders.Add(encounter);
            Check("encounter IDs unique across lookup lists", Has(LongTermContentValidation.Validate(null, null, encounters, null), "保存用IDが重複"));
            encounters.Intruders.Clear();
            var raid = ScriptableObject.CreateInstance<ThirdFactionEventDefinition>(); owned.Add(raid);
            raid.EventId = "event.one"; raid.Category = ThirdFactionEventCategory.DungeonRaid;
            raid.SpawnTeam = Team.Monster; raid.EncounterId = "missing"; director.Events = new[] { raid };
            Check("missing encounter reference fails", Has(LongTermContentValidation.Validate(null, null, encounters, director), "遭遇IDがありません"));
            raid.EncounterId = encounter.Id; raid.Reward = new R1ContentCatalog.UniqueReward { DisplayName = "未設定の任意報酬" };
            Check("known encounter and unset optional reward valid", LongTermContentValidation.Validate(null, null, encounters, director).IsValid);
            director.Events = new[] { raid, raid };
            Check("duplicate event ID fails", Has(LongTermContentValidation.Validate(null, null, encounters, director), "保存用IDが重複"));
            director.Events = new[] { raid }; raid.Category = ThirdFactionEventCategory.StrongEnemyExpansion;
            raid.EncounterId = null; raid.Prefab = null; raid.Stats = null;
            Check("territory expansion needs no spawn prefab", LongTermContentValidation.Validate(null, null, null, director).IsValid);
            raid.EventId = " event.one";
            Check("ID whitespace rejected without rewriting", !LongTermContentValidation.Validate(null, null, null, director).IsValid && raid.EventId == " event.one");
            Check("missing optional catalogs preserve legacy", LongTermContentValidation.Validate(null, null, null, null).IsValid);

            var info = FKBuildInfo.Create("1.2.3", new string('a', 40), "6000.3.8f1", "2026-10-11T00:00:00.0000000Z", true, SaveSystem.CurrentSaveVersion, new string('b', 64));
            string json = info.ToJson();
            Check("build identity JSON round trip", FKBuildInfo.TryParse(json, out var parsed) && parsed.GameVersion == info.GameVersion && parsed.Commit == info.Commit
                && parsed.UnityVersion == info.UnityVersion && parsed.Dirty && parsed.SaveSchema == SaveSystem.CurrentSaveVersion && parsed.PackageLockHash == info.PackageLockHash);
            Check("missing build metadata rejected", !FKBuildInfo.TryParse("{}", out _));
            Check("oversized build metadata rejected", !FKBuildInfo.TryParse(new string('x', FKBuildInfo.MaximumJsonCharacters + 1), out _));
            Check("runtime fallback identity available", !string.IsNullOrEmpty(FKBuildInfo.Current.GameVersion) && FKBuildInfo.Current.SaveSchema == SaveSystem.CurrentSaveVersion);
            string priorCommit = Environment.GetEnvironmentVariable("FK_BUILD_COMMIT");
            string priorDirty = Environment.GetEnvironmentVariable("FK_BUILD_DIRTY");
            string priorEpoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
            try
            {
                Directory.CreateDirectory(Path.Combine(temporaryRoot, "Packages"));
                File.WriteAllText(Path.Combine(temporaryRoot, "Packages/packages-lock.json"), "{\"dependencies\":{}}");
                Environment.SetEnvironmentVariable("FK_BUILD_COMMIT", new string('a', 40));
                Environment.SetEnvironmentVariable("FK_BUILD_DIRTY", "true");
                Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "0");
                var reproducible = FKLongTermBuildProcessor.CaptureIdentity(temporaryRoot, "1.2.3", "6000.3.8f1", 7);
                Check("supplied source identity and package hash retained", reproducible.Commit == new string('a', 40) && reproducible.Dirty && reproducible.PackageLockHash.Length == 64);
                Check("SOURCE_DATE_EPOCH produces repeatable build identity", reproducible.BuildUtc.StartsWith("1970-01-01T00:00:00", StringComparison.Ordinal)
                    && reproducible.ToJson() == FKLongTermBuildProcessor.CaptureIdentity(temporaryRoot, "1.2.3", "6000.3.8f1", 7).ToJson());
                Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "invalid");
                bool invalidEpoch = false;
                try { FKLongTermBuildProcessor.CaptureIdentity(temporaryRoot, "1.2.3", "6000.3.8f1", 7); } catch (InvalidDataException) { invalidEpoch = true; }
                Check("invalid reproducible timestamp fails before staging", invalidEpoch);
            }
            finally
            {
                Environment.SetEnvironmentVariable("FK_BUILD_COMMIT", priorCommit);
                Environment.SetEnvironmentVariable("FK_BUILD_DIRTY", priorDirty);
                Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", priorEpoch);
            }
            var stage = new FKBuildMetadataStage(temporaryRoot);
            stage.Stage(json);
            Check("build metadata staged with recovery journal", stage.HasPendingRestore && stage.ReadStagedJson() == json);
            stage.Restore();
            Check("temporary asset cleaned after build", !stage.HasPendingRestore && !File.Exists(Path.Combine(temporaryRoot, FKBuildMetadataStage.AssetPath)));
            string asset = Path.Combine(temporaryRoot, FKBuildMetadataStage.AssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(asset));
            byte[] previous = Encoding.UTF8.GetBytes("previous user metadata"); byte[] previousMeta = Encoding.UTF8.GetBytes("guid: previous-guid");
            File.WriteAllBytes(asset, previous); File.WriteAllBytes(asset + ".meta", previousMeta);
            stage.Stage(json); stage.RecordImportedMetadata(); stage.Restore();
            Check("existing metadata and GUID restored byte for byte", File.ReadAllBytes(asset).SequenceEqual(previous) && File.ReadAllBytes(asset + ".meta").SequenceEqual(previousMeta));
            stage.Stage(json);
            new FKBuildMetadataStage(temporaryRoot).Restore();
            Check("new process recovers interrupted build", File.ReadAllBytes(asset).SequenceEqual(previous));
            stage.Stage(json); File.WriteAllText(asset, "changed while building");
            bool protectedEdit = false;
            try { stage.Restore(); } catch (IOException) { protectedEdit = true; }
            Check("cleanup protects edits made during build", protectedEdit && File.ReadAllText(asset) == "changed while building" && stage.HasPendingRestore);
            File.WriteAllText(asset, json); stage.Restore();
            Check("restore resumes after conflict resolved", File.ReadAllBytes(asset).SequenceEqual(previous) && !stage.HasPendingRestore);
            stage.Stage(json); stage.RecordImportedMetadata(); File.WriteAllText(asset + ".meta", "guid: changed-during-build");
            bool protectedGuid = false;
            try { stage.Restore(); } catch (IOException) { protectedGuid = true; }
            Check("cleanup protects GUID edits before restoring any asset", protectedGuid && File.ReadAllText(asset + ".meta") == "guid: changed-during-build"
                && File.ReadAllText(asset) == json && stage.HasPendingRestore);
            File.WriteAllBytes(asset + ".meta", previousMeta); stage.Restore();
            Check("original asset and GUID recover after metadata conflict resolved", File.ReadAllBytes(asset).SequenceEqual(previous)
                && File.ReadAllBytes(asset + ".meta").SequenceEqual(previousMeta) && !stage.HasPendingRestore);

            var bounded = new LongTermContentValidation.Result();
            for (int i = 0; i < 1000; i++) bounded.Error("fixture", "error");
            Check("diagnostic buffer bounded without losing failure count", bounded.ErrorCount == 1000 && bounded.Errors.Count == 256 && !bounded.IsValid);
            var production = LongTermContentValidation.ValidateProject();
            Check("production registrations and legacy data valid: " + production.Summary(), production.IsValid);
            Debug.Log("[LongTermContent] " + checks + " checks passed");
        }
        finally
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            // A test-only temp directory, checked before recursive cleanup.
            string resolved = Path.GetFullPath(temporaryRoot), temp = Path.GetFullPath(Path.GetTempPath());
            if (resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolved).StartsWith("FantasyKingdomBuildInfo-", StringComparison.Ordinal)
                && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
}
#endif
