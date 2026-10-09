using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>One Japanese authoring surface. Assets remain ordinary Unity data, not an opaque database.</summary>
public sealed partial class GameDevelopmentStudioWindow : EditorWindow
{
    public const string ContentFolder = "Assets/_FantasyKingdom/GameContent";
    public const string RulesPath = "Assets/Resources/GameContent/Rules.asset";
    public const string FacilityCatalogPath = "Assets/Resources/GameContent/FacilityCatalog.asset";
    readonly List<UnitData> units = new List<UnitData>();
    readonly List<FacilityDefinitionData> buildings = new List<FacilityDefinitionData>();
    UnitData selectedUnit;
    FacilityDefinitionData selectedBuilding;
    ThirdFactionEventDefinition selectedEvent;
    GameAuthoringRules rules;
    ThirdFactionDirectorConfig directorConfig;
    GameObject visualModel;
    Vector2 listScroll, detailScroll;
    int tab;
    string search = "", notice = "";
    bool costFold, growthFold, abilityFold, upkeepFold;
    static readonly string[] Tabs = { "駒づくり", "建物づくり", "陣営・ゲームルール", "第三陣営のイベント" };

    [MenuItem("Fantasy Kingdom/開発スタジオ（日本語）", priority = 0)]
    public static void Open()
    {
        var window = GetWindow<GameDevelopmentStudioWindow>("開発スタジオ");
        window.minSize = new Vector2(930, 640); window.Show();
    }
    void OnEnable() { RefreshLists(); Undo.undoRedoPerformed += Repaint; }
    void OnDisable() => Undo.undoRedoPerformed -= Repaint;
    void OnProjectChange() { RefreshLists(); Repaint(); }
    void RefreshLists()
    {
        units.Clear(); buildings.Clear();
        foreach (var id in AssetDatabase.FindAssets("t:UnitData"))
        { var item = AssetDatabase.LoadAssetAtPath<UnitData>(AssetDatabase.GUIDToAssetPath(id)); if (item != null) units.Add(item); }
        foreach (var id in AssetDatabase.FindAssets("t:FacilityDefinitionData"))
        { var item = AssetDatabase.LoadAssetAtPath<FacilityDefinitionData>(AssetDatabase.GUIDToAssetPath(id)); if (item != null) buildings.Add(item); }
        units.Sort((a,b) => string.Compare(a.DisplayName,b.DisplayName,StringComparison.Ordinal));
        buildings.Sort((a,b) => string.Compare(a.displayName,b.displayName,StringComparison.Ordinal));
        rules = AssetDatabase.LoadAssetAtPath<GameAuthoringRules>(RulesPath);
        directorConfig = AssetDatabase.LoadAssetAtPath<ThirdFactionDirectorConfig>("Assets/Resources/AI/ThirdFaction/DirectorConfig.asset");
    }
    void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Fantasy Kingdom  開発スタジオ", new GUIStyle(EditorStyles.boldLabel) { fontSize = 19 }, GUILayout.Height(26));
        if (GUILayout.Button("一覧を更新", GUILayout.Width(90))) RefreshLists();
        if (GUILayout.Button("AI自己評価・日記", GUILayout.Width(130))) AIReflectionConfigEditor.OpenSettings();
        if (GUILayout.Button("AI探索の設定", GUILayout.Width(100))) ExplorationAISettingsEditor.OpenSettings();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("すべて保存", GUILayout.Width(110), GUILayout.Height(26))) SaveEverything();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("名前・能力・陣営・行動マスを、この画面から作成します。黄色の「駒」が現在地です。", EditorStyles.wordWrappedMiniLabel);
        tab = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(28));
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox("ゲーム再生中は閲覧のみです。停止して設定を保存し、次のゲームで確認してください。", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (tab <= 1)
            {
                EditorGUILayout.BeginHorizontal(); DrawList();
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                if (tab == 0) DrawUnit(); else DrawBuilding();
                EditorGUILayout.EndScrollView(); EditorGUILayout.EndHorizontal();
            }
            else
            {
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                if (tab == 2) DrawRules(); else DrawThirdFaction();
                EditorGUILayout.EndScrollView();
            }
        }
        if (!string.IsNullOrEmpty(notice)) EditorGUILayout.HelpBox(notice, MessageType.Info);
    }
    void DrawList()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(190));
        search = EditorGUILayout.TextField("絞り込み", search);
        if (GUILayout.Button(tab == 0 ? "＋ 新しい駒" : "＋ 新しい建物", GUILayout.Height(28)))
            TryAction(() => { if (tab == 0) selectedUnit = CreateUnitDefinition("新しい駒"); else selectedBuilding = CreateBuildingDefinition("新しい建物"); visualModel = null; RefreshLists(); });
        if (GUILayout.Button("選択した設定を複製"))
            TryAction(() => { DuplicateSelected(); RefreshLists(); });
        if (tab == 1 && GUILayout.Button("既存の建物データを取り込む"))
            TryAction(() => { ImportLegacyBuildings(); RefreshLists(); });
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        if (tab == 0) foreach (var item in units)
        {
            if (!Matches(item.DisplayName, item.category)) continue;
            var old = GUI.backgroundColor; if (item == selectedUnit) GUI.backgroundColor = new Color(.55f,.85f,1);
            if (GUILayout.Button(item.DisplayName, GUILayout.Height(26))) { selectedUnit = item; visualModel = null; detailScroll = Vector2.zero; }
            GUI.backgroundColor = old;
        }
        else foreach (var item in buildings)
        {
            if (!Matches(item.displayName, item.category)) continue;
            var old = GUI.backgroundColor; if (item == selectedBuilding) GUI.backgroundColor = new Color(.55f,.85f,1);
            if (GUILayout.Button(string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName, GUILayout.Height(26)))
            { selectedBuilding = item; visualModel = null; detailScroll = Vector2.zero; }
            GUI.backgroundColor = old;
        }
        EditorGUILayout.EndScrollView(); EditorGUILayout.EndVertical();
    }
    bool Matches(string name, string category) => string.IsNullOrWhiteSpace(search)
        || (name ?? "").IndexOf(search,StringComparison.OrdinalIgnoreCase) >= 0 || (category ?? "").IndexOf(search,StringComparison.OrdinalIgnoreCase) >= 0;

    void DrawUnit()
    {
        if (selectedUnit == null) { EditorGUILayout.HelpBox("左の「＋ 新しい駒」で作成するか、既存の駒を選択してください。新しい種類は独立したIDで登録され、同じ役割の駒を複数作れます。",MessageType.Info); return; }
        var so = new SerializedObject(selectedUnit); so.Update();
        Heading("駒の基本設定");
        Fields(so, "displayName", "category", "defaultTeam", "kind", "availableToPlayer", "availableToEnemy", "prefab");
        EditorGUILayout.HelpBox("基礎役割はAIの判断と既存の固有処理に使います。移動・通常攻撃・視界は下のタイルで個別に設定できます。駒と行動タイプの名前、種類の分類は自由です。スキルの範囲・効果は選んだスキルに従います。",MessageType.None);
        Heading("能力値"); Fields(so,"baseHP","baseATK","baseDEF");
        growthFold = EditorGUILayout.Foldout(growthFold,"レベルごとの成長",true); if (growthFold) Fields(so,"hpGrowth","atkGrowth","defGrowth");
        costFold = EditorGUILayout.Foldout(costFold,"作成に必要な資材・AP",true);
        if (costFold) Fields(so,"costWood","costStone","costIron","costMagic","costWater","costBread","costCitizen","costAP");
        upkeepFold = EditorGUILayout.Foldout(upkeepFold,"毎ターンの維持費",true);
        if (upkeepFold)
        {
            Field(so.FindProperty("upkeepExempt"));
            bool exempt = so.FindProperty("upkeepExempt").boolValue || selectedUnit.kind == Kind.King || selectedUnit.kind == Kind.Boss;
            EditorGUILayout.HelpBox(exempt ? "この駒の維持費はありません。王・異形の王は常に免除されます。"
                : "通常駒はLv1からパン1・鉄1を毎ターン消費します。Lv10で各2、Lv20で各3となります。以下の資源は追加の維持費です。", MessageType.Info);
            using (new EditorGUI.DisabledScope(exempt))
                Fields(so,"upkeepWood","upkeepStone","upkeepMagic","upkeepWater","upkeepMagicScales");
        }
        abilityFold = EditorGUILayout.Foldout(abilityFold,"スキル・常時能力",true);
        if (abilityFold)
        {
            Fields(so,"useAuthoredAbilities");
            if (so.FindProperty("useAuthoredAbilities").boolValue)
            { Fields(so,"authoredPassive","authoredSpecialAbility"); DrawSkill(so.FindProperty("authoredSkillId")); }
        }
        so.ApplyModifiedProperties();
        DrawVisual(selectedUnit, null);
        DrawProfile(selectedUnit.actionProfile, selectedUnit.kind, p => selectedUnit.actionProfile = p, selectedUnit);
        if (!selectedUnit.IsValidForAuthoring) EditorGUILayout.HelpBox("能力値・成長率・費用が不正です。HPを1以上、ほかの数値を0以上にしてください。",MessageType.Error);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("この駒をゲームに登録",GUILayout.Height(30))) TryAction(() => RegisterUnit(selectedUnit));
        if (GUILayout.Button("データをProjectで表示")) { Selection.activeObject = selectedUnit; EditorGUIUtility.PingObject(selectedUnit); }
        EditorGUILayout.EndHorizontal();
        DrawId(selectedUnit.definitionId);
    }
    void DrawBuilding()
    {
        if (selectedBuilding == null) { EditorGUILayout.HelpBox("左の「＋ 新しい建物」で作成するか、既存の建物データを選択してください。能力・建築費・生産・維持費・強化を設定できます。",MessageType.Info); return; }
        var so = new SerializedObject(selectedBuilding); so.Update();
        Heading("建物の基本設定");
        Fields(so,"displayName","category","defaultTeam","behaviourKind","availableToPlayer","availableToEnemy","prefab","buildAP");
        DrawBundle(so.FindProperty("buildCost"),"建築に必要な資材");
        var levels = so.FindProperty("levels");
        Heading("レベル別の能力・生産・強化");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("レベル数",levels.arraySize.ToString());
        if (GUILayout.Button("＋ レベルを追加",GUILayout.Width(130))) { levels.InsertArrayElementAtIndex(levels.arraySize); }
        using (new EditorGUI.DisabledScope(levels.arraySize <= 1))
            if (GUILayout.Button("最終レベルを削除",GUILayout.Width(135))) levels.DeleteArrayElementAtIndex(levels.arraySize-1);
        EditorGUILayout.EndHorizontal();
        for (int i=0;i<levels.arraySize;i++)
        {
            var level = levels.GetArrayElementAtIndex(i);
            level.isExpanded = EditorGUILayout.Foldout(level.isExpanded,"レベル " + (i+1),true);
            if (!level.isExpanded) continue;
            EditorGUI.indentLevel++;
            foreach (var key in new[] { "HP","ATK","DEF","UpgradeAP","SpecialValue" }) Field(level.FindPropertyRelative(key));
            DrawBundle(level.FindPropertyRelative("Input"),"生産時に消費する資材");
            Field(level.FindPropertyRelative("ProductionIntervalTurns"));
            EditorGUILayout.LabelField("生産周期", "1は毎ターン、2は2ターンごと。旧データの0は毎ターン。材料は生産時だけ消費します。", EditorStyles.wordWrappedMiniLabel);
            DrawBundle(level.FindPropertyRelative("Output"),"１回の生産量");
            DrawBundle(level.FindPropertyRelative("Maintenance"),"毎ターンの維持費");
            DrawBundle(level.FindPropertyRelative("UpgradeCost"),"このレベルへの強化費用");
            DrawBundle(level.FindPropertyRelative("BonusOutput1"),"確率生産１"); Field(level.FindPropertyRelative("BonusChance1"));
            DrawBundle(level.FindPropertyRelative("BonusOutput2"),"確率生産２"); Field(level.FindPropertyRelative("BonusChance2"));
            EditorGUI.indentLevel--;
        }
        so.ApplyModifiedProperties();
        DrawVisual(null,selectedBuilding);
        EditorGUILayout.HelpBox("既存データを取り込んだ建物は編集用の複製です。登録すると新しい建物として追加されます。元の建物はそのまま残ります。８×８のマス設定は「駒づくり」で設定します。",MessageType.None);
        if (GUILayout.Button("この建物をゲームに登録",GUILayout.Height(30))) TryAction(() => RegisterBuilding(selectedBuilding));
        DrawId(selectedBuilding.definitionId);
    }
    void DrawProfile(BoardActionProfile profile, Kind role, Action<BoardActionProfile> assign, UnityEngine.Object owner)
    {
        Heading("移動・攻撃・視界のマス設定");
        var replacement = (BoardActionProfile)EditorGUILayout.ObjectField("マス設定データ",profile,typeof(BoardActionProfile),false);
        if (replacement != profile) { Undo.RecordObject(owner,"マス設定を差し替え"); assign(replacement); EditorUtility.SetDirty(owner); profile=replacement; }
        if (profile == null && GUILayout.Button("専用のマス設定を作成"))
        {
            profile=CreateProfile(AssetDatabase.GetAssetPath(owner),role,owner is FacilityDefinitionData);
            Undo.RecordObject(owner,"マス設定作成"); assign(profile);EditorUtility.SetDirty(owner);
        }
        DevelopmentStudioTiles.Draw(profile,role,position.width-235);
    }
    void DrawVisual(UnitData unit,FacilityDefinitionData building)
    {
        Heading("見た目の作成");
        visualModel=(GameObject)EditorGUILayout.ObjectField("使いたいモデル／元Prefab",visualModel,typeof(GameObject),false);
        if (GUILayout.Button(visualModel != null ? "このモデルで専用Prefabを作成" : "基本形のPrefabを作成"))
            TryAction(() => { var prefab=CreateVisualPrefab(unit,building,visualModel); if(unit!=null){Undo.RecordObject(unit,"外見変更");unit.prefab=prefab;EditorUtility.SetDirty(unit);}else{Undo.RecordObject(building,"外見変更");building.prefab=prefab;EditorUtility.SetDirty(building);} });
    }
    static void Heading(string text) { EditorGUILayout.Space(9); EditorGUILayout.LabelField(text,EditorStyles.boldLabel); }
    static void DrawId(string id) => EditorGUILayout.LabelField("保存用ID（自動発行）",id ?? "旧形式：登録時に発行",EditorStyles.miniLabel);
    void TryAction(Action action)
    {
        try { action(); notice="設定を更新しました。「すべて保存」で保存し、ゲーム再生で確認してください。"; }
        catch(Exception ex) { Debug.LogException(ex); notice="設定を保存できませんでした："+ex.Message; }
    }
    void SaveEverything()
    {
        try
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UnitAuthoringCatalog>(UnitWorkshopWindow.CatalogPath);
            if (catalog != null)
            {
                Undo.RecordObject(catalog,"登録した駒の役割・見た目を更新");
                foreach(var entry in catalog.units)if(entry?.data != null)
                { entry.kind=entry.data.kind; if(entry.data.prefab!=null)entry.prefab=entry.data.prefab; }
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets(); notice="すべての設定を保存しました。次の新規ゲームに反映されます。";
        }
        catch(Exception ex) { Debug.LogException(ex); notice="設定を保存できませんでした："+ex.Message; }
    }
}
