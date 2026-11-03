#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class StudioAllSettingsTests
{
    static int checks;
    static void Check(string text,bool ok) { if(!ok)throw new InvalidOperationException("[StudioSettings] FAIL "+text);checks++;Debug.Log("[StudioSettings] PASS "+text); }
    public static void Run()
    {
        checks=0;
        var data=GameDevelopmentStudioWindow.EnsureGameplayTuning();
        Check("one persistent Resources asset",data!=null && AssetDatabase.GetAssetPath(data)=="Assets/Resources/GameContent/GameplayTuning.asset");
        string original=EditorJsonUtility.ToJson(data);
        try
        {
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("共通ルールを試験編集");
            var so=new SerializedObject(data);so.FindProperty("mainCrystalHP").intValue=1300;so.FindProperty("mainCrystalCap").intValue=2600;
            so.FindProperty("subCrystalHP").intValue=650;so.FindProperty("summonsPerTurn").intValue=2;so.FindProperty("normalAttacksPerTurn").intValue=2;
            so.ApplyModifiedProperties();GameDevelopmentStudioWindow.RefreshSettingsCaches();
            Check("edited crystal data reaches the live formal rule",MainCrystalRules.MaximumHP==1300 && MainCrystalRules.EarnedMaximumHP==2600 && CrystalSystem.CrystalHP==1300);
            Check("edited sub-crystal reaches both recipes and menu data",FacilityData.GetLevel(FacilityKind.SubCrystal,1).HP==650 && FacilityData.Table[FacilityKind.SubCrystal].HP==650);
            Check("edited quotas reach player and AI shared rules",SummonTurnRules.Limit==2 && NormalAttackTurnRules.Limit==2);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();GameDevelopmentStudioWindow.RefreshSettingsCaches();
            Check("serialized changes support Undo without deleting the asset",data!=null && MainCrystalRules.MaximumHP==1000 && FacilityData.SubCrystalMaximumHP==500);
            int found=0,ai=0,terrain=0;
            foreach(var id in AssetDatabase.FindAssets("t:ScriptableObject",new[]{"Assets"}))
            {
                var asset=AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(id));
                if(!GameDevelopmentStudioWindow.IsGameSetting(asset))continue;found++;
                int group=GameDevelopmentStudioWindow.SettingGroup(asset);if(group==3)ai++;if(group==4)terrain++;
            }
            Check("settings discovery includes gameplay, AI and terrain",found>30 && ai>0 && terrain>0);
        }
        finally { if(data==null)data=GameDevelopmentStudioWindow.EnsureGameplayTuning(); EditorJsonUtility.FromJsonOverwrite(original,data);EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);GameDevelopmentStudioWindow.RefreshSettingsCaches(); }
        Check("saved defaults retain the requested balance",MainCrystalRules.MaximumHP==1000 && MainCrystalRules.EarnedMaximumHP==2000 && FacilityData.SubCrystalMaximumHP==500 && SummonTurnRules.Limit==1 && NormalAttackTurnRules.Limit==1);
        CrystalPrefabEditor.ApplyMainCrystalRules();
        Debug.Log("[StudioSettings] RESULT checks="+checks);
    }
}
#endif
