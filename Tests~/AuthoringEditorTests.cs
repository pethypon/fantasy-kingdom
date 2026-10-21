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
            var model=new GameObject("Nested actor model");
            try
            {
                var child=new GameObject("Actor child");child.transform.SetParent(model.transform,false);child.AddComponent<Status>();
                unit.prefab=GameDevelopmentStudioWindow.CreateVisualPrefab(unit,null,model);GameDevelopmentStudioWindow.RegisterUnit(unit);
                Check("generated nested model normalizes actor root",unit.prefab.GetComponent<Status>()?.GrowthData==unit);
                Check("prefab generation preserves original model",model.GetComponent<Status>()==null&&model.GetComponentsInChildren<Status>().Length==1);
            }
            finally { UnityEngine.Object.DestroyImmediate(model); }
            unit.actionProfile.movement.SetCell(7,7,true);EditorUtility.SetDirty(unit.actionProfile);AssetDatabase.SaveAssets();
            string profilePath=AssetDatabase.GetAssetPath(unit.actionProfile);AssetDatabase.ImportAsset(profilePath,ImportAssetOptions.ForceUpdate);
            Check("mask bit 63 persists in Unity asset",AssetDatabase.LoadAssetAtPath<BoardActionProfile>(profilePath).movement.IsCellSet(7,7));
            window=ScriptableObject.CreateInstance<GameDevelopmentStudioWindow>();
            var special = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); special.ability = UnitSpecialSkill.MagicRitual;
            special.apCost = -1; special.cooldown = -1; special.sacrificeLimit = -1;
            AssetDatabase.CreateAsset(special, folders[0] + "/SpecialSkill.asset"); unit.specialSkill = special; EditorUtility.SetDirty(unit);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).SetValue(window,unit);
            typeof(GameDevelopmentStudioWindow).GetMethod("DuplicateSelected",flags).Invoke(window,null);
            var copy=(UnitData)typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).GetValue(window);
            folders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(copy)).Replace('\\','/'));
            Check("duplicate shares role but not ID or profile",copy.kind==unit.kind&&copy.definitionId!=unit.definitionId&&copy.actionProfile!=unit.actionProfile);
            Check("duplicate special configuration has independent asset",copy.specialSkill!=null&&copy.specialSkill!=unit.specialSkill&&copy.specialSkill.ability==special.ability);
            copy.specialSkill.apCost=4;
            Check("editing duplicate special AP preserves original unspecified setting",unit.specialSkill.apCost==-1&&copy.specialSkill.cooldown==-1&&copy.specialSkill.sacrificeLimit==-1);
            Check("duplicate prefab references its own data",copy.prefab.GetComponentInChildren<Status>().GrowthData==copy);
            copy.kind=Kind.Scout;EditorUtility.SetDirty(copy);
            typeof(GameDevelopmentStudioWindow).GetMethod("SaveEverything",flags).Invoke(window,null);
            Check("saving role edits keeps ID resolvable",unitCatalog.GetById(copy.definitionId)==copy&&unitCatalog.units.Find(e=>e.data==copy).kind==Kind.Scout);
            var newRoles = new[] { Kind.WhiteMage, Kind.HolyKnight, Kind.Samurai, Kind.Ninja, Kind.Berserker, Kind.Navigator,
                Kind.HeavyWarrior, Kind.DragonKnight };
            for (int index = 0; index < newRoles.Length; index++)
            {
                typeof(GameDevelopmentStudioWindow).GetMethod("CreateSpecialRoleCopy",flags).Invoke(window,new object[] { index });
                var specialCopy=(UnitData)typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).GetValue(window);
                folders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(specialCopy)).Replace('\\','/'));
                var specified = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); specified.ability = specialCopy.specialSkill.ability; specified.ApplySpecifiedAPCost();
                Check("special role copy preserves stats and leaves unspecified costs "+newRoles[index],specialCopy.kind==newRoles[index]
                    &&specialCopy.baseHP==copy.baseHP&&specialCopy.specialSkill.apCost==specified.apCost&&specialCopy.specialSkill.cooldown==-1
                    &&specialCopy.prefab.GetComponentInChildren<Status>().kind==newRoles[index]
                    &&unitCatalog.units.Find(e=>e.data==specialCopy).kind==newRoles[index]);
                Check("special role copy preserves independent authored action masks "+newRoles[index],specialCopy.actionProfile!=copy.actionProfile
                    &&specialCopy.actionProfile.movement!=copy.actionProfile.movement
                    &&specialCopy.actionProfile.movement.cells==copy.actionProfile.movement.cells
                    &&specialCopy.actionProfile.attack.cells==copy.actionProfile.attack.cells
                    &&specialCopy.actionProfile.vision.cells==copy.actionProfile.vision.cells);
                UnityEngine.Object.DestroyImmediate(specified);
            }
            var sourceProfile=copy.actionProfile;var sourceKind=copy.kind;string sourceProfileJson=EditorJsonUtility.ToJson(sourceProfile);
            try
            {
                copy.kind=Kind.Knight;copy.actionProfile=null;
                foreach(int index in new[]{0,2,4,6,7})
                {
                    typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).SetValue(window,copy);
                    typeof(GameDevelopmentStudioWindow).GetMethod("CreateSpecialRoleCopy",flags).Invoke(window,new object[]{index});
                    var role=(UnitData)typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).GetValue(window);
                    folders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(role)).Replace('\\','/'));
                    bool matches=true;
                    for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
                    {
                        if(x==0&&z==0)continue;
                        foreach(var facing in new[]{Direction.N,Direction.S})
                            matches&=AttackPatterns.CanAttack(role.kind,facing,x,z,role.actionProfile)==AttackPatterns.CanAttack(Kind.Knight,facing,x,z)
                                &&MovePatterns.CanMove(role.kind,facing,x,z,role.actionProfile)==MovePatterns.CanMove(Kind.Knight,facing,x,z);
                    }
                    Check("legacy Knight template retains normal attack and movement for "+role.kind,matches
                        &&role.actionProfile.CanAttack&&role.actionProfile.attack.useCustom&&role.actionProfile.attack.Offsets.Count==3
                        &&copy.actionProfile==null);
                    var expectedVision=new HashSet<Vector2Int>();var actualVision=new HashSet<Vector2Int>();
                    foreach(var point in VisionGenerator.BaseVisionOffsets(Kind.Knight))expectedVision.Add(new Vector2Int(point.x,point.z));
                    foreach(var point in VisionGenerator.BaseVisionOffsets(role.kind,Direction.N,role.actionProfile))actualVision.Add(new Vector2Int(point.x,point.z));
                    Check("legacy template vision survives the new Kind "+role.kind,expectedVision.SetEquals(actualVision));
                    long beforeAttack=role.actionProfile.attack.cells;role.actionProfile.attack.SetCell(0,0,true);
                    Check("changing a new role mask cannot mutate the original template "+role.kind,sourceProfile.attack.cells!=role.actionProfile.attack.cells
                        &&role.actionProfile.attack.cells!=beforeAttack);
                }
                copy.kind=Kind.Magicsniper;
                typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).SetValue(window,copy);
                int countBefore=unitCatalog.units.Count;bool refusesTruncation=false;
                try{typeof(GameDevelopmentStudioWindow).GetMethod("CreateSpecialRoleCopy",flags).Invoke(window,new object[]{0});}
                catch(TargetInvocationException error){refusesTruncation=error.InnerException is ArgumentException&&error.InnerException.Message.Contains("8×8");}
                Check("out-of-grid legacy ranges refuse silent truncation before creating assets",refusesTruncation&&unitCatalog.units.Count==countBefore
                    &&ReferenceEquals(typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).GetValue(window),copy));
                copy.kind=Kind.Archer;copy.actionProfile=sourceProfile;sourceProfile.attack.useCustom=false;sourceProfile.vision.useCustom=false;
                typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).SetValue(window,copy);
                typeof(GameDevelopmentStudioWindow).GetMethod("CreateSpecialRoleCopy",flags).Invoke(window,new object[]{3});
                var rangedNinja=(UnitData)typeof(GameDevelopmentStudioWindow).GetField("selectedUnit",flags).GetValue(window);
                folders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(rangedNinja)).Replace('\\','/'));
                Check("Ninja can retain the chosen template's ranged attack without inventing a range",rangedNinja.kind==Kind.Ninja
                    &&AttackPatterns.CanAttack(rangedNinja.kind,Direction.N,0,3,rangedNinja.actionProfile)
                    &&!AttackPatterns.CanAttack(rangedNinja.kind,Direction.N,1,1,rangedNinja.actionProfile)
                    &&rangedNinja.actionProfile.movement.cells==sourceProfile.movement.cells
                    &&!sourceProfile.attack.useCustom&&!sourceProfile.vision.useCustom);
            }
            finally{copy.kind=sourceKind;copy.actionProfile=sourceProfile;EditorJsonUtility.FromJsonOverwrite(sourceProfileJson,sourceProfile);EditorUtility.SetDirty(sourceProfile);EditorUtility.SetDirty(copy);}
            var building=GameDevelopmentStudioWindow.CreateBuildingDefinition("検証用 建物");folders.Add(Path.GetDirectoryName(AssetDatabase.GetAssetPath(building)).Replace('\\','/'));
            Check("building prefab keeps exact ID data",building.prefab.GetComponentInChildren<Status>().AuthoredFacility==building);
            Check("building default stationary but vision exists",!building.actionProfile.CanMove&&building.actionProfile.vision.Offsets.Count>0);
            var config=GameDevelopmentStudioWindow.EnsureThirdEvents();
            foreach(var category in new[]{ThirdFactionEventCategory.DungeonRaid,ThirdFactionEventCategory.TerritoryRaid,ThirdFactionEventCategory.StrongEnemyExpansion})
            {
                var item=Array.Find(config.Events,e=>e!=null&&e.Category==category);
                Check("required Japanese event "+category,item!=null&&item.DisplayName!=category.ToString()&&item.DisplayName.Length>0);
            }
            var existingRules = Resources.Load<GameAuthoringRules>(GameAuthoringRules.ResourcesPath);
            bool previouslyEnabled = existingRules != null && existingRules.applyRules;
            var configuredRules = GameDevelopmentStudioWindow.CreateRules();
            Check("opening studio preserves the user's existing rule activation", configuredRules.applyRules == previouslyEnabled);
            var freshDefaults = ScriptableObject.CreateInstance<GameAuthoringRules>();
            try { Check("new default rules preserve legacy unless enabled", !freshDefaults.applyRules); }
            finally { UnityEngine.Object.DestroyImmediate(freshDefaults); }
            GameDevelopmentStudioWindow.ImportLegacyBuildings();
            foreach(var entry in FacilityData.Table)
            {
                var definition=AssetDatabase.LoadAssetAtPath<FacilityDefinitionData>(GameDevelopmentStudioWindow.ContentFolder+"/Buildings/既存の建物データ/"+entry.Key+"/Building.asset");
                Check("existing building data imported "+entry.Key,definition!=null&&definition.buildAP==entry.Value.APCost&&definition.levels.Length==entry.Value.MaxLevel);
            }
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
