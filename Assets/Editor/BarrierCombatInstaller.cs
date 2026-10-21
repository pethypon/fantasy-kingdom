#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Creates a reviewable, disabled draft. Runtime activation is deliberately explicit.</summary>
public static class BarrierCombatInstaller
{
    public const string Path = "Assets/Resources/GameContent/BarrierCombat.asset";
    [MenuItem("Fantasy Kingdom/バリア戦闘/未採用の調整案を準備")]
    public static void PrepareDraft()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BarrierCombatSettings>(Path);
        if (settings == null)
        {
            UnitWorkshopWindow.EnsureFolder("Assets/Resources/GameContent");
            settings = ScriptableObject.CreateInstance<BarrierCombatSettings>();
            settings.PopulateTrialValues(); settings.enabledForPlay = false;
            AssetDatabase.CreateAsset(settings, Path);
        }
        BarrierCombatSettings.InvalidateLoadedSettings();
        R1BarrierRoleContentInstaller.PrepareDrafts();
        AssetDatabase.SaveAssets();
        Debug.Log("[BarrierDraft] 試験値は下書きです。バリア戦闘の有効化と追加2職種の導入は未実施です。");
    }
    [MenuItem("Fantasy Kingdom/バリア戦闘/確定R1のバリア表と18職種を導入")]
    public static void InstallTrial()
    {
        PrepareDraft();
        var settings = AssetDatabase.LoadAssetAtPath<BarrierCombatSettings>(Path);
        settings.PopulateR1Values(); settings.enabledForPlay = true; settings.trialBalance = true;
        EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets(); BarrierCombatSettings.InvalidateLoadedSettings();
        R1BarrierRoleContentInstaller.InstallAuthorizedTrial();
        Debug.Log("[BarrierTrial] バリア戦闘と18職種を導入。未指定の能力値・費用・細則は調整用の暫定値です。");
    }
}
#endif
