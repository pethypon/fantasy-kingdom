#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Real AssetDatabase creation and persistence, isolated clone only.</summary>
public static class AuthoringEditorTests
{
    static void Check(string name,bool value)
    {if(!value)throw new Exception("[AuthoringEditor] FAIL "+name);Debug.Log("[AuthoringEditor] PASS "+name);}
    public static void EditMode()
    {
        if(!Application.dataPath.Contains("UnityValidation"))throw new Exception("Authoring tests require isolated validation project.");
        var unitCatalog=UnitWorkshopWindow.GetCatalog();string originalUnits=EditorJsonUtility.ToJson(unitCatalog);
        var buildingCatalog=AssetDatabase.LoadAssetAtPath<FacilityAuthoringCatalog>(GameDevelopmentStudioWindow.FacilityCatalogPath);
        string originalBuildings=buildingCatalog!=null?EditorJsonUtility.ToJson(buildingCatalog):null;
        var folders=new List<string>();GameDevelopmentStudioWindow window=null;
        try
        {
            var unit=GameDevelopmentStudioWindow.CreateUnitDefinition("検証用 駒");folders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(unit)).Replace('\\','/'));
            Check("unit factory registers independent stable ID",unitCatalog.GetById(unit.definitionId)==unit&&unitCatalog.units.Find(e=>e.data==unit).standaloneDefinition);
            Check("generated prefab contains exact data and collider",unit.prefab.GetComponentInChildren<Status>().GrowthData==unit&&unit.prefab.GetComponentInChildren<Collider>()!=null);
            unit.actionProfile.movement.SetCell(7,7,true);EditorUtility.SetDirty(unit.actionProfile);AssetDatabase.SaveAssets();
            string profilePath=AssetDatabase.GetAssetPath(unit.actionProfile);AssetDatabase.ImportAsset(profilePath,ImportAssetOptions.ForceUpdate);
            Check("mask bit 63 persists in Unity asset",AssetDatabase.LoadAssetAtPath<BoardActionProfile>(profilePath).movement.IsCellSet(7,7));
            window=ScriptableObject.CreateInstance<GameDevelopmentStudioWindow>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).SetValue(window,unit);
            typeof(GameDevelopmentStudioWindow).GetMethod("DuplicateSelected",flags).Invoke(window,null);
            var copy=(UnitData)typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).GetValue(window);
            folders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(copy)).Replace('\\','/'));
            Check("duplicate shares role but not ID or profile",copy.kind==unit.kind&&copy.definitionId!=unit.definitionId&&copy.actionProfile!=unit.actionProfile);
            Check("duplicate prefab references its own data",copy.prefab.GetComponentInChildren<Status>().GrowthData==copy);
            copy.kind=Kind.Scout;EditorUtility.SetDirty(copy);
            typeof(GameDevelopmentStudioWindow).GetMethod("SaveEverything",flags).Invoke(window,null);
            Check("saving role edits keeps ID resolvable",unitCatalog.GetById(copy.definitionId)==copy&&unitCatalog.units.Find(e=>e.data==copy).kind==Kind.Scout);
            var building=GameDevelopmentStudioWindow.CreateBuildingDefinition("検証用 建物");folders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(building)).Replace('\\','/'));
            Check("building prefab keeps exact ID data",building.prefab.GetComponentInChildren<Status>().AuthoredFacility==building);
            Check("building default stationary but vision exists",!building.actionProfile.CanMove&&building.actionProfile.vision.Offsets.Count>0);
            var config=GameDevelopmentStudioWindow.EnsureThirdEvents();
            foreach(var category in new[]{ThirdFactionEventCategory.DungeonRaid,ThirdFactionEventCategory.TerritoryRaid,ThirdFactionEventCategory.StrongEnemyExpansion})
            {
                var item=Array.Find(config.Events,e=>e!=null&&e.Category==category);
                Check("required Japanese event "+category,item!=null&&item.DisplayName!=category.ToString()&&item.DisplayName.Length>0);
            }
            Check("default rules preserve legacy unless enabled",!GameDevelopmentStudioWindow.CreateRules().applyRules);
        }
        finally
        {
            if(window!=null)UnityEngine.Object.DestroyImmediate(window);
            EditorJsonUtility.FromJsonOverwrite(originalUnits,unitCatalog);EditorUtility.SetDirty(unitCatalog);
            buildingCatalog=AssetDatabase.LoadAssetAtPath<FacilityAuthoringCatalog>(GameDevelopmentStudioWindow.FacilityCatalogPath);
            if(originalBuildings!=null){EditorJsonUtility.FromJsonOverwrite(originalBuildings,buildingCatalog);EditorUtility.SetDirty(buildingCatalog);}
            else if(buildingCatalog!=null)AssetDatabase.DeleteAsset(GameDevelopmentStudioWindow.FacilityCatalogPath);
            foreach(var folder in folders)AssetDatabase.DeleteAsset(folder);
            AssetDatabase.SaveAssets();GameAuthoringRules.ResetCache();
        }
    }
}
#endif
