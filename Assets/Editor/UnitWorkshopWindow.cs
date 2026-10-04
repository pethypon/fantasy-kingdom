using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only unit authoring. No scene modification or enum migration.</summary>
public sealed class UnitWorkshopWindow : EditorWindow
{
    public const string UnitsFolder = "Assets/_FantasyKingdom/Units";
    public const string CatalogPath = "Assets/Resources/GameContent/UnitCatalog.asset";
    string unitName = "NewUnit";
    GameObject model;
    Kind kind = Kind.Knight;
    UnitData draft;
    Editor dataEditor;
    Vector2 scroll;
    bool register = true;

    [MenuItem("Fantasy Kingdom/ユニット工房")]
    public static void Open() => GameDevelopmentStudioWindow.Open();
    void OnEnable() => ResetDraft();
    void OnDisable() { if (dataEditor != null) DestroyImmediate(dataEditor); if (draft != null) DestroyImmediate(draft); }
    void ResetDraft()
    {
        if (dataEditor != null) DestroyImmediate(dataEditor);
        if (draft != null) DestroyImmediate(draft);
        draft = UnitStaticData.CreateUnitData(kind);
        if (draft == null) { draft = CreateInstance<UnitData>(); draft.kind = kind; draft.baseHP = 100; }
        draft.hideFlags = HideFlags.HideAndDontSave;
    }
    void OnGUI()
    {
        if (GUILayout.Button("新しい開発スタジオを開く（行動マス・複数種類・建物）",GUILayout.Height(30))) GameDevelopmentStudioWindow.Open();
        EditorGUILayout.HelpBox("モデル・能力値・成長率・コストからPrefabとUnitDataを作成します。行動タイプは既存の移動・攻撃ルールです。登録すると、そのタイプの初期配置・召喚・ロードに適用されます。", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            unitName = EditorGUILayout.TextField("名前 / 保存フォルダ", unitName);
            model = (GameObject)EditorGUILayout.ObjectField("モデル / 元Prefab（任意）", model, typeof(GameObject), false);
            var next = (Kind)EditorGUILayout.EnumPopup("行動タイプ", kind);
            if (next != kind) { kind = next; ResetDraft(); }
            register = EditorGUILayout.Toggle("ゲームに登録", register);
            EditorGUILayout.HelpBox("この旧工房は既存兵種の差し替え用です。複数の駒を追加したり行動マスを設定する場合は、新しい開発スタジオを使ってください。", MessageType.None);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (draft != null)
            {
                Editor.CreateCachedEditor(draft, null, ref dataEditor);
                dataEditor.OnInspectorGUI();
            }
            EditorGUILayout.EndScrollView();
            bool valid = draft != null && draft.IsValidForAuthoring && !string.IsNullOrWhiteSpace(unitName)
                && Array.IndexOf(BuildSummonUIBuilder.SummonableKinds,kind) >= 0;
            if (!valid) EditorGUILayout.HelpBox("HPは1以上、他の数値は0以上、名前は空欄不可。行動タイプは召喚可能なユニットを選択してください。", MessageType.Warning);
            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button("Prefab とデータを作成", GUILayout.Height(34)))
                {
                    try { Selection.activeObject = CreateUnit(unitName, model, kind, draft, register); }
                    catch (Exception ex) { Debug.LogException(ex); EditorUtility.DisplayDialog("作成エラー",ex.Message,"閉じる"); }
                }
        }
        if (GUILayout.Button("登録カタログを開く")) Selection.activeObject = GetCatalog();
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\','/');
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    public static UnitAuthoringCatalog GetCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<UnitAuthoringCatalog>(CatalogPath);
        if (catalog != null) return catalog;
        EnsureFolder("Assets/Resources/GameContent");
        catalog = CreateInstance<UnitAuthoringCatalog>(); AssetDatabase.CreateAsset(catalog,CatalogPath);
        AssetDatabase.SaveAssets(); return catalog;
    }
    public static GameObject CreateUnit(string name, GameObject model, Kind kind, UnitData source, bool register)
    {
        if (source == null || !source.IsValidForAuthoring || string.IsNullOrWhiteSpace(name)
            || Array.IndexOf(BuildSummonUIBuilder.SummonableKinds,kind)<0) throw new ArgumentException("ユニット設定が不正です。");
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c,'_');
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0 || name == "." || name == "..") throw new ArgumentException("保存名が不正です。");
        EnsureFolder(UnitsFolder);
        string folder = AssetDatabase.GenerateUniqueAssetPath(UnitsFolder+"/"+name);
        EnsureFolder(folder);
        var data = Instantiate(source); data.hideFlags=HideFlags.None; data.kind=kind;
        AssetDatabase.CreateAsset(data,folder+"/Stats.asset");
        GameObject root = null;
        try
        {
            root = model != null ? Instantiate(model) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = name; root.SetActive(false);
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var statuses = root.GetComponentsInChildren<Status>(true);
            if (statuses.Length > 1) throw new ArgumentException("Statusが複数あります。駒1体のPrefabまたはモデルを選択してください。");
            var status = statuses.Length == 1 ? statuses[0] : root.AddComponent<Status>();
            status.kind=kind;status.type=Type.Unit;status.team=Team.Player;status.direction=Direction.N;
            data.ApplyToStatus(status,1);
            if (root.GetComponentInChildren<Collider>(true)==null) status.gameObject.AddComponent<BoxCollider>();
            root.SetActive(true);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Unit.prefab");
            if (prefab == null) throw new InvalidOperationException("Prefabの保存に失敗しました。");
            if (register)
            {
                var catalog=GetCatalog();Undo.RecordObject(catalog,"Register unit");
                catalog.units.RemoveAll(e=>e!=null&&e.kind==kind);
                catalog.units.Add(new UnitAuthoringCatalog.Entry {kind=kind,prefab=prefab,data=data});
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();EditorGUIUtility.PingObject(prefab);return prefab;
        }
        finally { if(root!=null) DestroyImmediate(root); }
    }
}
