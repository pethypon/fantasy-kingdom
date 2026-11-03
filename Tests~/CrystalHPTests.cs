#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class CrystalHPTests
{
    static void Check(string message, bool ok)
    { if (!ok) throw new InvalidOperationException("[CrystalHP] FAIL " + message); Debug.Log("[CrystalHP] PASS " + message); }
    public static void Core()
    {
        Check("main 1000 and standard sub 500", MainCrystalRules.MaximumHP == 1000
            && FacilityData.GetLevel(FacilityKind.SubCrystal, 1).HP == 500);
        var go = new GameObject("Crystal HP save fixture"); go.SetActive(false);
        try
        {
            var actor = go.AddComponent<Status>(); actor.kind = Kind.SubCrystal; actor.type = Type.Building;
            actor.facilityKind = FacilityKind.SubCrystal;
            foreach (int oldMax in new[] { 2000, 5000, 1000 })
            {
                var saved = new SaveSystem.UnitSaveData { Kind = "SubCrystal", Type = "Building", Team = "Player",
                    FacilityKind = "SubCrystal", HP = oldMax / 2, MaxHP = oldMax, Level = 1, ATK = 17, DEF = 3 };
                SaveGameApplier.ApplyStatusFields(actor, saved);
                Check("legacy sub " + oldMax + " preserves half health and unrelated stats", actor.MaxHP == 500 && actor.HP == 250
                    && actor.ATK == 17 && actor.DEF == 3);
                var roundTrip = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(actor)));
                SaveGameApplier.ApplyStatusFields(actor, roundTrip);
                Check("sub " + oldMax + " migration does not repeat", actor.MaxHP == 500 && actor.HP == 250);
                saved.HP = 0; SaveGameApplier.ApplyStatusFields(actor, saved);
                Check("dead sub " + oldMax + " stays dead", actor.HP == 0 && actor.MaxHP == 500);
            }
            actor.kind = Kind.Crystal;
            var previous = new SaveSystem.UnitSaveData { Kind = "Crystal", Type = "Building", Team = "Player", HP = 3000,
                MaxHP = 6000, MainCrystalRuleVersion = 1, Level = 1 };
            SaveGameApplier.ApplyStatusFields(actor, previous);
            Check("actual main restore keeps earned 1000 and half health", actor.MaxHP == 2000 && actor.HP == 1000);
            var current = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(actor)));
            Check("main save stamps new HP-rule version", current.MainCrystalRuleVersion == MainCrystalRules.Version);
            SaveGameApplier.ApplyStatusFields(actor, current);
            Check("main HP migration does not repeat", actor.MaxHP == 2000 && actor.HP == 1000);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    public static void PlayMode(GameSystems systems)
    {
        foreach (var side in new[] { Team.Player, Team.Enemy })
        {
            var parent = side == Team.Player ? systems.CrystalSystem.Playercrystal : systems.CrystalSystem.Enemycrystal;
            var main = parent.GetComponentInChildren<Status>(true);
            Check(side + " actual new-game main crystal is 1000/1000", main != null && main.HP == 1000 && main.MaxHP == 1000);
            using (var fixture = new EconomyR2Tests.Fixture(systems, side))
            {
                var sub = fixture.Builder.PlaceBuildingForLoad(GridHelper.ToGrid(systems.MapCreate.SetPos[0]), FacilityKind.SubCrystal, side);
                Check(side + " actual build system creates sub 500/500", sub != null && sub.HP == 500 && sub.MaxHP == 500);
            }
        }
        var data = AssetDatabase.LoadAssetAtPath<FacilityDefinitionData>(
            "Assets/_FantasyKingdom/GameContent/Buildings/既存の建物データ/SubCrystal/Building.asset");
        Check("existing authored sub definition is also 500", data != null && data.GetLevel(1).HP == 500);
        foreach (string path in new[] { "Assets/Prefabs/Map/プレイヤー　クリスタル Variant.prefab", "Assets/Prefabs/Map/敵　クリスタル Variant.prefab" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var main = prefab.GetComponentInChildren<Status>(true);
            Check("Editor prefab matches 1000: " + path, main != null && main.MaxHP == 1000 && main.HP == 1000);
        }
        Debug.Log("[CrystalHP] PLAYMODE COMPLETE");
    }
}
#endif
