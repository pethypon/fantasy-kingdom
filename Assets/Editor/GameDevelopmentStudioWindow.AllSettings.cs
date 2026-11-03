using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Discovers existing serialized gameplay settings; it never makes parallel copies of runtime data.</summary>
public sealed partial class GameDevelopmentStudioWindow
{
    readonly List<ScriptableObject> allSettings = new List<ScriptableObject>();
    Object settingTarget;
    SerializedObject settingObject;
    Vector2 settingListScroll, settingDetailScroll;
    string settingSearch = "", fieldSearch = "";
    int settingGroup;
    static readonly string[] SettingGroups = { "すべて", "共通ルール", "駒・建物", "AI・第三陣営", "地形・マップ", "見た目・音・UI", "その他" };

    internal static bool IsGameSetting(ScriptableObject asset) => asset != null
        && asset.GetType().Assembly == typeof(GameAuthoringRules).Assembly;
    internal static int SettingGroup(ScriptableObject asset)
    {
        string name = asset.GetType().Name;
        if (asset is GameplayTuning || asset is GameAuthoringRules || name.Contains("BarrierCombat") || name.Contains("EconomyBalance")) return 1;
        if (name.StartsWith("AI", StringComparison.Ordinal) || name.Contains("Exploration") || name.Contains("ThirdFaction")) return 3;
        if (name.Contains("Terrain") || name.Contains("Map") || name.Contains("Biome")) return 4;
        if (name.Contains("Visual") || name.Contains("Audio") || name.Contains("Theme") || name.Contains("Icon") || name.Contains("Presentation")) return 5;
        if (name.Contains("Unit") || name.Contains("Facility") || name.Contains("Skill") || name.Contains("Roster") || name.Contains("Building") || name.Contains("ActionProfile")) return 2;
        return 6;
    }
    void RefreshAllSettings()
    {
        allSettings.Clear();
        // Query only game-owned types, so updating a setting never loads every TMP font atlas.
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            if (!type.IsAbstract && type.Assembly == typeof(GameAuthoringRules).Assembly)
                foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets" }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (string path in paths)
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is ScriptableObject data && IsGameSetting(data)) allSettings.Add(data);
        allSettings.Sort((a,b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
        settingObject = null;
    }
    internal static void RefreshSettingsCaches()
    {
        GameplayTuning.ResetCache(); FacilityAuthoringCatalog.ResetCache(); GameAuthoringRules.ResetCache(); GameContentRevision.NotifyChanged();
    }
    void SelectSetting(Object target) { settingTarget = target; settingObject = null; settingDetailScroll = Vector2.zero; fieldSearch = ""; }
    void DrawAllSettings()
    {
        EditorGUILayout.HelpBox("ゲームの設定データを直接編集します。未登録データは参照先への登録が必要です。初期配置・地形・共通ルールは停止後に保存し、新規ゲームで確認してください。", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(250));
        settingSearch = EditorGUILayout.TextField("設定を検索", settingSearch);
        settingGroup = EditorGUILayout.Popup("分類", settingGroup, SettingGroups);
        if (GUILayout.Button("共通戦闘・召喚・クリスタル")) SelectSetting(EnsureGameplayTuning());
        if (GUILayout.Button("選択中の部品・マテリアルを編集")) SelectSetting(Selection.activeObject);
        if (GUILayout.Button("Unityの詳細設定（描画・音・入力）")) SettingsService.OpenProjectSettings();
        EditorGUILayout.LabelField("設定データ " + allSettings.Count + " 件", EditorStyles.miniLabel);
        settingListScroll = EditorGUILayout.BeginScrollView(settingListScroll);
        foreach (var asset in allSettings)
        {
            if (asset == null || settingGroup != 0 && SettingGroup(asset) != settingGroup) continue;
            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrWhiteSpace(settingSearch) && (asset.name + " " + path + " " + asset.GetType().Name).IndexOf(settingSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (GUILayout.Toggle(settingTarget == asset, new GUIContent(asset.name, path), "Button", GUILayout.Height(26)) && settingTarget != asset) SelectSetting(asset);
        }
        EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical();
        settingDetailScroll = EditorGUILayout.BeginScrollView(settingDetailScroll);
        float oldLabelWidth = EditorGUIUtility.labelWidth;
        try { EditorGUIUtility.labelWidth = Mathf.Clamp((position.width - 300) * .55f, 200, 360); DrawSettingDetails(); }
        finally { EditorGUIUtility.labelWidth = oldLabelWidth; }
        EditorGUILayout.EndScrollView(); EditorGUILayout.EndHorizontal();
    }
    internal static GameplayTuning EnsureGameplayTuning()
    {
        const string path = "Assets/Resources/GameContent/GameplayTuning.asset";
        var asset = AssetDatabase.LoadAssetAtPath<GameplayTuning>(path);
        if (asset != null) return asset;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/GameContent")) AssetDatabase.CreateFolder("Assets/Resources", "GameContent");
        asset = ScriptableObject.CreateInstance<GameplayTuning>();
        AssetDatabase.CreateAsset(asset, path); Undo.RegisterCreatedObjectUndo(asset, "共通ルールを作成");
        AssetDatabase.SaveAssetIfDirty(asset); RefreshSettingsCaches(); return asset;
    }
    void DrawSettingDetails()
    {
        if (settingTarget == null) { EditorGUILayout.HelpBox("左の一覧から設定を選択してください。駒や建物は専用タブでも編集できます。新しい種類の設定データも自動的に一覧へ追加されます。", MessageType.None); return; }
        if (settingTarget is GameObject go)
        {
            Heading(go.name + " の部品");
            if (EditorUtility.IsPersistent(go))
            {
                EditorGUILayout.HelpBox("Prefabは専用の編集モードで開き、Hierarchyで部品を選択してください。マテリアルはProjectで選択できます。", MessageType.Info);
                if (GUILayout.Button("Prefabを開く")) AssetDatabase.OpenAsset(go);
            }
            else foreach (var component in go.GetComponents<Component>())
                if (component != null && GUILayout.Button(component.GetType().Name)) SelectSetting(component);
            return;
        }
        if (!(settingTarget is ScriptableObject) && !(settingTarget is Component) && !(settingTarget is Material))
        { EditorGUILayout.HelpBox("設定データ、シーン内の部品、Prefab、マテリアルを選択してください。画像・モデル・音声ファイルのインポート設定はUnityのInspectorを使います。", MessageType.None); return; }
        if (settingTarget is Component && EditorUtility.IsPersistent(settingTarget))
        { EditorGUILayout.HelpBox("Prefab内の部品はPrefab編集モードで変更してください。", MessageType.Info); return; }
        Heading(settingTarget.name + " / " + settingTarget.GetType().Name);
        string path = AssetDatabase.GetAssetPath(settingTarget);
        EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(path) ? "現在のシーン／Prefab編集モード" : path, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(32));
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Project／Hierarchyで表示")) { Selection.activeObject = settingTarget; EditorGUIUtility.PingObject(settingTarget); }
        if (GUILayout.Button("元に戻す")) { Undo.PerformUndo(); settingObject = null; }
        if (GUILayout.Button("変更を保存")) SaveSettingTarget();
        EditorGUILayout.EndHorizontal();
        if (settingTarget == null) { EditorGUILayout.HelpBox("元に戻す操作により編集対象がなくなりました。設定を選び直してください。", MessageType.None); return; }
        fieldSearch = EditorGUILayout.TextField("項目名を検索", fieldSearch);
        if (settingObject == null || settingObject.targetObject != settingTarget) settingObject = new SerializedObject(settingTarget);
        settingObject.Update();
        var property = settingObject.GetIterator();
        bool children = true;
        while (property.NextVisible(children))
        {
            children = false;
            if (property.name == "m_Script") continue;
            string label = Labels.TryGetValue(property.name, out var jp) ? jp : property.displayName;
            if (!string.IsNullOrWhiteSpace(fieldSearch) && (label + " " + property.name).IndexOf(fieldSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
            // Honor InspectorName/Tooltip/Range and native property drawers for every serialized type.
            using (new EditorGUI.DisabledScope(property.name == "definitionId"))
                EditorGUILayout.PropertyField(property, new GUIContent(label, string.IsNullOrEmpty(property.tooltip) ? label : property.tooltip), true);
        }
        if (settingObject.ApplyModifiedProperties()) RefreshSettingsCaches();
        if (settingTarget is GameplayTuning)
            EditorGUILayout.HelpBox("スキル・自動反撃・反射は通常攻撃の回数に含みません。召喚直後の攻撃禁止は共通ルールとして維持します。", MessageType.None);
    }
    void SaveSettingTarget()
    {
        try
        {
            if (settingTarget is Component component)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.scene == component.gameObject.scene)
                {
                    if (PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath) == null) throw new InvalidOperationException("Prefabを保存できませんでした。");
                }
                else
                {
                    EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                    if (!EditorSceneManager.SaveScene(component.gameObject.scene)) throw new InvalidOperationException("シーンは保存されませんでした。");
                }
            }
            else
            {
                var validation = LongTermContentValidation.ValidateProject();
                if (!validation.IsValid) throw new ArgumentException(validation.Summary());
                AssetDatabase.SaveAssetIfDirty(settingTarget);
            }
            RefreshSettingsCaches(); notice = "保存しました。ゲームの初期設定は新規ゲームで確認してください。";
        }
        catch (Exception ex) { notice = "保存できませんでした：" + ex.Message; Debug.LogException(ex); }
    }
}
