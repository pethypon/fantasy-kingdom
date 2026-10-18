#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Japanese, opt-in authoring. No gameplay tuning is filled in for undecided quantities.</summary>
public sealed class TerrainEnvironmentEditor : EditorWindow
{
    const string AssetPath = "Assets/Resources/Terrain/EnvironmentCatalog.asset";
    TerrainEnvironmentCatalog catalog;
    TerrainEnvironmentDefinition brush;
    Vector2 scroll;
    Vector2Int origin;
    Vector2Int selectedCell;
    bool erase;
    Editor definitionEditor;

    [MenuItem("Fantasy Kingdom/開発スタジオ/環境オブジェクト設定")]
    public static void Open()
    {
        var window = GetWindow<TerrainEnvironmentEditor>("環境オブジェクト設定");
        window.minSize = new Vector2(520, 640);
    }
    [MenuItem("Fantasy Kingdom/開発スタジオ/環境の標準データを作成")]
    public static void InstallDefaults()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Terrain")) AssetDatabase.CreateFolder("Assets/Resources", "Terrain");
        var asset = AssetDatabase.LoadAssetAtPath<TerrainEnvironmentCatalog>(AssetPath);
        if (asset == null)
        {
            asset = CreateInstance<TerrainEnvironmentCatalog>();
            AssetDatabase.CreateAsset(asset, AssetPath);
        }
        if (asset.definitions == null) asset.definitions = new List<TerrainEnvironmentDefinition>();
        foreach (var existing in asset.definitions)
        {
            if (existing == null) continue;
            // Fill only values explicitly settled by R1; authored tuning and opt-in activation remain intact.
            if ((existing.kind == TerrainEnvironmentKind.Forest || existing.kind == TerrainEnvironmentKind.Smoke) && existing.burnSmokeRounds < 0)
                existing.burnSmokeRounds = 30;
            if (existing.kind == TerrainEnvironmentKind.Geyser && existing.maximumHpDamageFraction < 0)
                existing.maximumHpDamageFraction = TerrainEnvironmentDefinition.GeyserDamageFraction;
            if (existing.kind == TerrainEnvironmentKind.Smoke && existing.maximumHpDamageFraction < 0)
                existing.maximumHpDamageFraction = .10f;
            EditorUtility.SetDirty(existing);
        }
        foreach (TerrainEnvironmentKind kind in Enum.GetValues(typeof(TerrainEnvironmentKind)))
        {
            bool exists = false;
            foreach (var definition in asset.definitions) if (definition != null && definition.kind == kind) { exists = true; break; }
            if (exists) continue;
            var value = CreateInstance<TerrainEnvironmentDefinition>();
            value.name = kind.ToString(); value.kind = kind;
            value.definitionId = "environment." + kind.ToString().ToLowerInvariant();
            value.displayName = TerrainEnvironmentDefinition.NameJapanese(kind);
            value.blocksMovement = kind == TerrainEnvironmentKind.Rock;
            value.blocksShots = kind == TerrainEnvironmentKind.Rock;
            value.blocksVision = kind == TerrainEnvironmentKind.Forest || kind == TerrainEnvironmentKind.Rock || kind == TerrainEnvironmentKind.Smoke;
            value.concealsOccupants = kind == TerrainEnvironmentKind.Forest || kind == TerrainEnvironmentKind.Bush || kind == TerrainEnvironmentKind.Smoke;
            value.burnable = kind == TerrainEnvironmentKind.Forest;
            value.damageOnEntry = kind == TerrainEnvironmentKind.PoisonSwamp || kind == TerrainEnvironmentKind.Geyser;
            value.damageOnOwnerTurn = kind == TerrainEnvironmentKind.PoisonSwamp || kind == TerrainEnvironmentKind.Smoke;
            value.hidden = kind == TerrainEnvironmentKind.Geyser;
            if (kind == TerrainEnvironmentKind.Geyser) value.maximumHpDamageFraction = .25f;
            if (kind == TerrainEnvironmentKind.Smoke) value.maximumHpDamageFraction = .10f;
            AssetDatabase.AddObjectToAsset(value, asset); asset.definitions.Add(value);
        }
        EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets();
        Selection.activeObject = asset;
        Debug.Log("[環境設定] 10種類の標準データを準備しました。未確定値・有効化・配置は環境オブジェクト設定画面で指定してください。");
    }
    void OnDisable() { if (definitionEditor != null) DestroyImmediate(definitionEditor); }
    void OnGUI()
    {
        EditorGUILayout.LabelField("環境オブジェクト設定", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("環境は高さ・ブロック素材と別に配置します。必要な値と見た目を設定してから有効にしてください。設定変更は次のマップ生成で反映されます。洞窟は入口と出口を個別に指定します。", MessageType.Info);
        catalog = (TerrainEnvironmentCatalog)EditorGUILayout.ObjectField("配置カタログ", catalog, typeof(TerrainEnvironmentCatalog), false);
        if (catalog == null) catalog = AssetDatabase.LoadAssetAtPath<TerrainEnvironmentCatalog>(AssetPath);
        if (catalog == null)
        {
            if (GUILayout.Button("10種類の標準データを作成")) { InstallDefaults(); catalog = AssetDatabase.LoadAssetAtPath<TerrainEnvironmentCatalog>(AssetPath); }
            return;
        }
        EditorGUI.BeginChangeCheck();
        bool enabled = EditorGUILayout.Toggle("このカタログをゲームで使う", catalog.enabledForPlay);
        if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(catalog, "環境を有効化"); catalog.enabledForPlay = enabled; EditorUtility.SetDirty(catalog); }
        var catalogProperties = new SerializedObject(catalog);
        catalogProperties.Update(); TerrainEnvironmentGenerationEditorUI.Draw(catalogProperties); catalogProperties.ApplyModifiedProperties();
        brush = (TerrainEnvironmentDefinition)EditorGUILayout.ObjectField("配置する環境", brush, typeof(TerrainEnvironmentDefinition), false);
        for (int first = 0; first < catalog.definitions.Count; first += 5)
        {
            GUILayout.BeginHorizontal();
            for (int i = first; i < Mathf.Min(first + 5, catalog.definitions.Count); i++)
            {
                var definition = catalog.definitions[i];
                if (definition != null && GUILayout.Button(definition.displayName, GUILayout.MinWidth(32))) brush = definition;
            }
            GUILayout.EndHorizontal();
        }
        erase = EditorGUILayout.Toggle("消しゴム", erase);
        origin = EditorGUILayout.Vector2IntField("表示する8×8の左下座標（X / Z）", origin);
        for (int z = 7; z >= 0; z--)
        {
            GUILayout.BeginHorizontal();
            for (int x = 0; x < 8; x++)
            {
                var cell = origin + new Vector2Int(x, z);
                var existing = Find(cell);
                var old = GUI.backgroundColor;
                GUI.backgroundColor = cell == selectedCell ? new Color(.4f, .8f, 1) : existing != null ? new Color(.6f, .8f, .6f) : Color.white;
                string text = existing?.definition != null ? existing.definition.displayName : cell.x + "," + cell.y;
                if (GUILayout.Button(text, GUILayout.Height(32))) Paint(cell);
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
        }
        var selection = Find(selectedCell);
        if (selection?.definition != null && selection.definition.kind == TerrainEnvironmentKind.Cave)
        {
            EditorGUI.BeginChangeCheck();
            bool hasExit = EditorGUILayout.Toggle("選択した洞窟の出口を設定", selection.hasCaveExit);
            var exit = EditorGUILayout.Vector2IntField("出口のX / Z", selection.caveExit);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(catalog, "洞窟の接続先変更"); selection.hasCaveExit = hasExit; selection.caveExit = exit; EditorUtility.SetDirty(catalog);
            }
            EditorGUILayout.HelpBox("洞窟は入口・出口の2地点が互いに接続されて初めて使用できます。使用AP3・次の自軍ターンに到着・同時利用1体です。再利用待ちターン数を設定してください。AIは探索済みの接続だけを使います。", MessageType.None);
            if (selection.hasCaveExit && selection.caveExit != selection.cell && GUILayout.Button("出口にも洞窟を置いて相互接続する"))
            {
                var other = Find(selection.caveExit);
                if (other != null && other.definition != null && other.definition.kind != TerrainEnvironmentKind.Cave)
                    ShowNotification(new GUIContent("出口には別の環境があります。先に配置を変更してください。"));
                else
                {
                    Undo.RecordObject(catalog, "洞窟の相互接続");
                    if (other == null) { other = new TerrainEnvironmentPlacement { cell = selection.caveExit }; catalog.placements.Add(other); }
                    other.definition = selection.definition; other.hasCaveExit = true; other.caveExit = selection.cell;
                    EditorUtility.SetDirty(catalog);
                }
            }
        }
        EditorGUILayout.LabelField("配置数: " + catalog.placements.Count);
        if (brush != null)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            Editor.CreateCachedEditor(brush, null, ref definitionEditor); definitionEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }
        if (GUILayout.Button("保存")) AssetDatabase.SaveAssets();
    }
    TerrainEnvironmentPlacement Find(Vector2Int cell)
    {
        if (catalog?.placements == null) return null;
        foreach (var placement in catalog.placements) if (placement != null && placement.cell == cell) return placement;
        return null;
    }
    void Paint(Vector2Int cell)
    {
        selectedCell = cell;
        if (!erase && brush == null) return;
        Undo.RecordObject(catalog, "環境タイルの配置");
        if (catalog.placements == null) catalog.placements = new List<TerrainEnvironmentPlacement>();
        var previous = Find(cell);
        if (erase) { if (previous != null) catalog.placements.Remove(previous); }
        else if (previous == null) catalog.placements.Add(new TerrainEnvironmentPlacement { cell = cell, definition = brush });
        else if (previous.definition != brush) { previous.definition = brush; previous.hasCaveExit = false; }
        EditorUtility.SetDirty(catalog);
    }
}

[CustomEditor(typeof(TerrainEnvironmentDefinition))]
public sealed class TerrainEnvironmentDefinitionInspector : Editor
{
    static readonly string[] Fields = { "definitionId", "displayName", "kind", "enabledForPlay", "visualPrefab", "blocksMovement", "blocksShots", "blocksVision", "concealsOccupants", "revealDistance", "moveAPAdjustment", "hidden", "attackRevealRounds", "caveCooldownTurns", "damageOnEntry", "damageOnOwnerTurn", "flatDamage", "maximumHpDamageFraction", "maximumHpHealFraction", "burnable", "burnSmokeRounds", "burnSmokeDamageFraction", "burnSmokeRevealDistance" };
    static readonly string[] Labels = { "識別ID（保存後は変更しない）", "表示名", "環境の種類", "ゲームで使用する", "見た目のプレハブ", "移動を遮る", "射線を遮る", "奥の視界を遮る", "マス内の駒を隠す", "隠密を確認できる距離", "移動APの補正", "未発見時に隠す", "森の攻撃後露見ラウンド数", "洞窟の再利用待ちターン数", "侵入時にダメージ", "所有ターン開始時にダメージ", "固定ダメージ", "最大HPに対するダメージ割合", "毎ターンの回復割合", "焼却可能", "煙の持続ラウンド数（標準30）", "煙の毎ターンダメージ割合（標準10%）", "焼却後の煙の確認距離" };
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        for (int i = 0; i < Fields.Length; i++)
        {
            var property = serializedObject.FindProperty(Fields[i]);
            if (Fields[i] == "kind")
            {
                var names = new string[10];
                for (int kind = 0; kind < names.Length; kind++) names[kind] = TerrainEnvironmentDefinition.NameJapanese((TerrainEnvironmentKind)kind);
                property.enumValueIndex = EditorGUILayout.Popup(Labels[i], property.enumValueIndex, names);
            }
            else EditorGUILayout.PropertyField(property, new GUIContent(Labels[i]));
        }
        serializedObject.ApplyModifiedProperties();
        var definition = (TerrainEnvironmentDefinition)target;
        if (definition.ConfigurationError != null) EditorGUILayout.HelpBox(definition.ConfigurationError + "。この環境の効果はまだ有効になりません。", MessageType.Warning);
        if (definition.kind == TerrainEnvironmentKind.Geyser) EditorGUILayout.HelpBox("隠し間欠泉は侵入時（強制移動を含む）に最大HP25%ダメージ。作動した陣営にだけ発見情報が残ります。", MessageType.Info);
        if (definition.kind == TerrainEnvironmentKind.Rock) EditorGUILayout.HelpBox("岩場の駒は遠距離ダメージ×0.75、Stoneタグの攻撃ダメージ×1.25。名前の文字列では判定しません。", MessageType.Info);
        if (definition.kind == TerrainEnvironmentKind.Forest) EditorGUILayout.HelpBox("森内部の視界-1。近距離1マスなら確認可能。攻撃後の露見時間はUNRESOLVED_BALANCEです。", MessageType.Info);
    }
}

[CustomEditor(typeof(TerrainEnvironmentCatalog))]
public sealed class TerrainEnvironmentCatalogInspector : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enabledForPlay"), new GUIContent("このカタログをゲームで使う"));
        TerrainEnvironmentGenerationEditorUI.Draw(serializedObject);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("definitions"), new GUIContent("登録した環境の種類"), true);
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.LabelField("配置済みマス", ((TerrainEnvironmentCatalog)target).placements?.Count.ToString() ?? "0");
        EditorGUILayout.HelpBox("配置と洞窟の接続先は8×8タイル画面で編集できます。", MessageType.Info);
        if (GUILayout.Button("環境オブジェクト設定を開く")) TerrainEnvironmentEditor.Open();
    }
}

static class TerrainEnvironmentGenerationEditorUI
{
    public static void Draw(SerializedObject serialized)
    {
        var generation = serialized.FindProperty("generation");
        if (generation == null) return;
        generation.isExpanded = EditorGUILayout.Foldout(generation.isExpanded, "開始時の自動生成", true);
        if (!generation.isExpanded) return;
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(generation.FindPropertyRelative("enabled"), new GUIContent("新規ゲームで自動配置する"));
        EditorGUILayout.PropertyField(generation.FindPropertyRelative("seedOffset"), new GUIContent("環境専用シード"));
        EditorGUILayout.Slider(generation.FindPropertyRelative("coverage"), 0, .4f, new GUIContent("環境の目標割合"));
        EditorGUILayout.PropertyField(generation.FindPropertyRelative("maximumCells"), new GUIContent("手動配置を含む総マス数の上限"));
        EditorGUILayout.PropertyField(generation.FindPropertyRelative("maximumAttempts"), new GUIContent("配置の試行回数上限"));
        EditorGUILayout.PropertyField(generation.FindPropertyRelative("initialSafeRadius"), new GUIContent("クリスタル周辺の安全距離（5以上）"));
        EditorGUILayout.HelpBox("森と草むらは同じ高さの3～4マスで、不定形につながります。別の群れとは1マス以上離します。初期部隊・川・高山・狭路を避け、洞窟は相互接続した2入口を配置します。ロードでは保存した配置を使います。", MessageType.None);
        var rules = generation.FindPropertyRelative("rules");
        EditorGUILayout.PropertyField(rules, new GUIContent("種類ごとの生成設定"), true);
        EditorGUI.indentLevel--;
    }
}

[CustomPropertyDrawer(typeof(TerrainEnvironmentGenerationRule))]
public sealed class TerrainEnvironmentGenerationRuleDrawer : PropertyDrawer
{
    static string[] names;
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight * 2 + 4;
    public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
    {
        if (names == null)
        {
            names = new string[10];
            for (int i = 0; i < names.Length; i++) names[i] = TerrainEnvironmentDefinition.NameJapanese((TerrainEnvironmentKind)i);
        }
        EditorGUI.BeginProperty(rect, label, property);
        float lineHeight = EditorGUIUtility.singleLineHeight;
        var kind = property.FindPropertyRelative("kind");
        var first = new Rect(rect.x, rect.y, rect.width, lineHeight);
        var toggle = new Rect(first.x, first.y, 22, lineHeight);
        EditorGUI.PropertyField(toggle, property.FindPropertyRelative("enabled"), GUIContent.none);
        var popup = new Rect(first.x + 24, first.y, first.width - 24, lineHeight);
        kind.enumValueIndex = EditorGUI.Popup(popup, "生成する環境", kind.enumValueIndex, names);
        float width = (rect.width - 8) * .5f;
        var second = new Rect(rect.x, rect.y + lineHeight + 2, width, lineHeight);
        float previousWidth = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = Math.Min(100, width * .65f);
        EditorGUI.PropertyField(second, property.FindPropertyRelative("weight"), new GUIContent("出現の重み"));
        second.x += width + 8;
        EditorGUI.PropertyField(second, property.FindPropertyRelative("maximumCells"), new GUIContent("種類の上限マス数"));
        EditorGUIUtility.labelWidth = previousWidth;
        EditorGUI.EndProperty();
    }
}
#endif
