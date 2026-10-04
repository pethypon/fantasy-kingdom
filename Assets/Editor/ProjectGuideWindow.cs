using UnityEditor;
using UnityEngine;

public sealed class ProjectGuideWindow : EditorWindow
{
    [MenuItem("Fantasy Kingdom/プロジェクト案内")]
    public static void Open() => GetWindow<ProjectGuideWindow>("プロジェクト案内");
    void OnGUI()
    {
        GUILayout.Label("Fantasy Kingdom",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("開発スタジオで駒・建物・行動マス・陣営・ルール・第三陣営を日本語で設定できます。新しい制作データは _FantasyKingdom/GameContent にまとまります。",MessageType.Info);
        if(GUILayout.Button("開発スタジオを開く",GUILayout.Height(32))) GameDevelopmentStudioWindow.Open();
        Link("開発スタジオで作成した駒・建物",GameDevelopmentStudioWindow.ContentFolder);
        Link("作成したユニット",UnitWorkshopWindow.UnitsFolder);
        if(GUILayout.Button("有効なユニット登録")) {Selection.activeObject=UnitWorkshopWindow.GetCatalog();EditorGUIUtility.PingObject(Selection.activeObject);}
        Link("シーン","Assets/Scenes");
        Link("既存の駒・建物Prefab","Assets/Prefabs");
        Link("既存の能力値データ","Assets/Data");
        Link("地形・霧・バリエーション","Assets/Resources/Terrain");
        Link("UIテーマ","Assets/Resources/UI");
        Link("マテリアル","Assets/Materials");
        Link("ゲーム処理","Assets/Script/Gamesystem");
        Link("UI処理","Assets/Script/UI");
        Link("マップ処理","Assets/Script/Mapsystem");
        Link("開発者用Editor機能","Assets/Editor");
    }
    static void Link(string label,string path)
    {
        if(!GUILayout.Button(label))return;
        var asset=AssetDatabase.LoadMainAssetAtPath(path);
        if(asset!=null){Selection.activeObject=asset;EditorGUIUtility.PingObject(asset);EditorUtility.FocusProjectWindow();}
    }
    [MenuItem("Fantasy Kingdom/管理フォルダを準備")]
    public static void Prepare()
    {
        UnitWorkshopWindow.EnsureFolder(UnitWorkshopWindow.UnitsFolder);
        UnitWorkshopWindow.GetCatalog();AssetDatabase.SaveAssets();
    }
}
