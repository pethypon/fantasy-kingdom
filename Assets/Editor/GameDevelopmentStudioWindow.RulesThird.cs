using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class GameDevelopmentStudioWindow
{
    void DrawRules()
    {
        Heading("陣営・ゲームルール");
        if (rules == null)
        {
            EditorGUILayout.HelpBox("現在は既存のゲームルールを使っています。設定データを作成しても、有効化するまではゲームの内容を変えません。",MessageType.Info);
            if(GUILayout.Button("ゲームルールの設定を作成")) TryAction(() => { rules=CreateRules(); });
            return;
        }
        var so = new SerializedObject(rules);so.Update();Field(so.FindProperty("applyRules"));
        EditorGUILayout.HelpBox("初期資材・初期AP・時間・地形は、新規ゲームで適用します。消費APやAP上限などの共通ルールは読み込んだゲームにも適用します。陣営の操作方式とターン順は既存のゲームシステムに従います。",MessageType.Info);
        Heading("時間とAP"); Fields(so,"turnSeconds","totalSeconds","timeBonusSeconds","maximumAP","moveAP","attackAP","heightAP","citizenAP");
        EditorGUILayout.LabelField("毎ターンのAP", "初期AP ＋ 市民による増加 − ペナルティ（AP上限まで）", EditorStyles.wordWrappedMiniLabel);
        Heading("維持・経済");Fields(so,"breadPerCitizen","starvationGraceTurns","aiBreadReserveTurns");
        DrawAIEconomyPolicy(so.FindProperty("aiEconomy"));
        Heading("新規マップ");Fields(so,"mapWidth","mapDepth","noiseScale","riverHalfWidth");
        Heading("陣営の名前・色・初期状態");
        var factions=so.FindProperty("factions");
        for(int i=0;i<factions.arraySize;i++)
        {
            var faction=factions.GetArrayElementAtIndex(i); var team=(Team)faction.FindPropertyRelative("team").intValue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(TeamLabel(team),EditorStyles.boldLabel);
            Field(faction.FindPropertyRelative("displayName"));Field(faction.FindPropertyRelative("color"));
            if(team!=Team.Obstacle)
            {
                Field(faction.FindPropertyRelative("initialAP"));
                if(faction.FindPropertyRelative("initialAP").intValue>so.FindProperty("maximumAP").intValue)
                    EditorGUILayout.LabelField("実際の初期APは、設定したAP上限までになります。",EditorStyles.wordWrappedMiniLabel);
            }
            else EditorGUILayout.LabelField("行動ポイント","強敵ごとの専用APを使用");
            if(team==Team.Player||team==Team.Enemy)DrawBundle(faction.FindPropertyRelative("initialResources"),"新規ゲームの初期資材");
            else EditorGUILayout.LabelField("資材・生産","第三陣営は通常の経済資材を使用しません。",EditorStyles.wordWrappedMiniLabel);
            string controller = team == Team.Player ? "手動操作／開発用AI（F8）" : team == Team.Enemy ? "敵軍AI"
                : team == Team.Obstacle ? "強敵AI（第三陣営の行動後）" : "第三陣営AI（敵軍の行動後）";
            EditorGUILayout.LabelField("操作とターン順",controller,EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }
        so.ApplyModifiedProperties();
    }
    static void DrawAIEconomyPolicy(SerializedProperty policy)
    {
        if (policy == null) return;
        policy.isExpanded = EditorGUILayout.Foldout(policy.isExpanded, "AIの経済・生産判断（R2）", true);
        if (!policy.isExpanded) return;
        EditorGUILayout.HelpBox("AIは資源ごとの生産量・維持費・将来の需要から増設を判断します。経済危機では通常の軍事建築を控え、視認した重大な脅威への緊急防衛を優先します。判断ログは必要な時だけ有効にすると軽く動作します。", MessageType.Info);
        EditorGUI.indentLevel++;
        foreach (var name in new[] {
            "enableDecisionLogs", "forecastTurns", "reserveTurns", "recoveryWindowTurns",
            "plannedDemandWeight", "plannedUnitLevel", "plannedBuildActions", "plannedSummonActions", "explorationSummonWeight",
            "needWeight", "coverageWeight", "chainRecoveryWeight", "reserveRecoveryWeight", "overstockPenalty",
            "warningMilitaryPenalty", "localMilitarySaturationPenalty", "uncoveredWallThreatPenalty",
            "emergencyKingDamageFraction", "emergencyCrystalDamageFraction", "criticalReserveFraction",
            "sustainedDeficitUrgency", "deficitEpsilon" }) Field(policy.FindPropertyRelative(name));
        DrawBundle(policy.FindPropertyRelative("minimumOperationalBuffer"), "資源ごとの最低予備量");
        DrawBundle(policy.FindPropertyRelative("plannedBuildCost"), "次の建築で消費すると見込む資源");
        EditorGUI.indentLevel--;
    }

    public static GameAuthoringRules CreateRules()
    {
        var existing=AssetDatabase.LoadAssetAtPath<GameAuthoringRules>(RulesPath);if(existing!=null)return existing;
        UnitWorkshopWindow.EnsureFolder("Assets/Resources/GameContent");
        var created=CreateInstance<GameAuthoringRules>();AssetDatabase.CreateAsset(created,RulesPath);AssetDatabase.SaveAssets();return created;
    }
    void DrawThirdFaction()
    {
        Heading("第三陣営のイベントと駒");
        EditorGUILayout.HelpBox("ターン順：プレイヤー → 敵軍 → 第三陣営 → 強敵。イベント判断は介入ポイント（IP）、出現した駒の行動は各陣営のAPを使います。襲撃の駒は「駒づくり」で作成したものも選べます。",MessageType.Info);
        if(directorConfig==null)
        {
            if(GUILayout.Button("第三陣営の設定を作成"))TryAction(() => directorConfig=EnsureThirdEvents());
            return;
        }
        if(GUILayout.Button("３種類のイベントを登録・表示名を日本語にする"))TryAction(() => directorConfig=EnsureThirdEvents());
        var options=new List<ThirdFactionEventDefinition>();
        foreach(var item in directorConfig.Events)if(item!=null)options.Add(item);
        if(options.Count>0)
        {
            if(selectedEvent==null||!options.Contains(selectedEvent))selectedEvent=options[0];
            var names=options.ConvertAll(e=>string.IsNullOrEmpty(e.DisplayName)?EventName(e.Category):e.DisplayName).ToArray();
            selectedEvent=options[EditorGUILayout.Popup("編集するイベント",options.IndexOf(selectedEvent),names)];
            DrawEvent(selectedEvent);
        }
        Heading("全体の調整");EditorGUILayout.LabelField("介入ポイント","上限50 IP／各ターン＋5 IP");
        var so=new SerializedObject(directorConfig);so.Update();
        Fields(so,"InitialIP","MaximumStrongEnemyExpansion","GlobalEventCooldown","MinimumTurnsBetweenMajorEvents","ResponseTurns");
        so.FindProperty("HistoryTurns").isExpanded=EditorGUILayout.Foldout(so.FindProperty("HistoryTurns").isExpanded,"戦況判断・安全条件",true);
        if(so.FindProperty("HistoryTurns").isExpanded)
            Fields(so,"HistoryTurns","PredictionTurns","ReserveProbability","ActiveWarThreshold","BalancedPowerRatio","StagnationThreshold","DecisiveHPRatio","MaximumAdvantageEraseRatio","MaximumPowerSwingRatio","PreventLeaderFlip","MinimumObjectiveSpawnDistance");
        so.FindProperty("MaxCandidates").isExpanded=EditorGUILayout.Foldout(so.FindProperty("MaxCandidates").isExpanded,"処理の上限（軽量化）",true);
        if(so.FindProperty("MaxCandidates").isExpanded)Fields(so,"MaxCandidates","MaxSpawnCells","DirectorBudgetMs","TacticalSliceMs","TacticalMaxActions","TacticalPathExpansions","TacticalPathBudgetMs");
        so.ApplyModifiedProperties();
    }
    void DrawEvent(ThirdFactionEventDefinition definition)
    {
        var so=new SerializedObject(definition);so.Update();Heading(EventName(definition.Category));
        Fields(so,"DisplayName","IpCost","MinThreatLevel","MaxThreatLevel","CooldownTurns","MajorCooldownTurns");
        Heading("対象と継続時間");Fields(so,"TargetPlayer","TargetEnemy","TargetThirdFaction","ObjectiveTurns");
        if(definition.Category==ThirdFactionEventCategory.StrongEnemyExpansion)
        {Field(so.FindProperty("LocalExpansionRadius"));EditorGUILayout.HelpBox("マップにいる強敵の領土を拡大します。新しい駒を出現させるイベントではありません。",MessageType.None);}
        else
        {
            Heading("襲撃に使用する駒");
            var selectable=new List<UnitData>();
            foreach(var unit in units)if(unit.IsValidForAuthoring&&unit.prefab!=null)selectable.Add(unit);
            int current=selectable.IndexOf(definition.Stats)+1;
            var names=new List<string>{"個別にデータ／Prefabを指定"};foreach(var unit in selectable)names.Add(unit.DisplayName);
            int chosen=EditorGUILayout.Popup("登録済みの駒を選ぶ",current,names.ToArray());
            if(chosen>0&&chosen!=current)
            {var unit=selectable[chosen-1];so.FindProperty("Stats").objectReferenceValue=unit;so.FindProperty("Prefab").objectReferenceValue=unit.prefab;so.FindProperty("EncounterId").stringValue="";}
            Fields(so,"Stats","Prefab");
            var spawnTeam=so.FindProperty("SpawnTeam");
            spawnTeam.intValue=EditorGUILayout.IntPopup("出現する陣営",spawnTeam.intValue,new[]{"魔物","乱入者"},new[]{(int)Team.Monster,(int)Team.Intruder});
            Fields(so,"SpawnCount","CanActImmediately");
            if(definition.Stats==null||definition.Prefab==null)EditorGUILayout.HelpBox("使用する駒を選んでください。既存の遭遇データが指定されている場合はそちらを使用します。",MessageType.Warning);
        }
        Heading("介入の条件");Fields(so,"MinimumStagnation","EstimatedPowerSwing","EstimatedDisruption","AllowedDuringPreserveWar","AllowedDuringDecisivePhase");
        so.ApplyModifiedProperties();
        EditorGUILayout.LabelField("イベントの保存用ID",definition.EventId,EditorStyles.miniLabel);
    }
    static string EventName(ThirdFactionEventCategory category) => category==ThirdFactionEventCategory.DungeonRaid?"ダンジョン襲撃"
        :category==ThirdFactionEventCategory.TerritoryRaid?"領土襲撃":category==ThirdFactionEventCategory.StrongEnemyExpansion?"強敵の領土拡大":"第三陣営イベント";
    public static ThirdFactionDirectorConfig EnsureThirdEvents()
    {
        const string folder="Assets/Resources/AI/ThirdFaction";UnitWorkshopWindow.EnsureFolder(folder);
        var config=AssetDatabase.LoadAssetAtPath<ThirdFactionDirectorConfig>(folder+"/DirectorConfig.asset");
        if(config==null){config=CreateInstance<ThirdFactionDirectorConfig>();AssetDatabase.CreateAsset(config,folder+"/DirectorConfig.asset");}
        var events=new List<ThirdFactionEventDefinition>(config.Events??Array.Empty<ThirdFactionEventDefinition>());
        var categories=new[]{ThirdFactionEventCategory.DungeonRaid,ThirdFactionEventCategory.TerritoryRaid,ThirdFactionEventCategory.StrongEnemyExpansion};
        var filenames=new[]{"DungeonRaid","TerritoryRaid","StrongEnemyExpansion"};
        for(int i=0;i<categories.Length;i++)
        {
            var category=categories[i];var data=events.Find(e=>e!=null&&e.Category==category);
            if(data==null)data=AssetDatabase.LoadAssetAtPath<ThirdFactionEventDefinition>(folder+"/"+filenames[i]+".asset");
            if(data==null)
            {
                data=CreateInstance<ThirdFactionEventDefinition>();data.EventId="third-"+filenames[i].ToLowerInvariant();data.Category=category;
                data.IpCost=i==2?40:25;data.Scale=i==2?ThirdFactionEventScale.Major:ThirdFactionEventScale.Medium;
                data.SpawnPolicy=i==0?ThirdFactionSpawnPolicy.NearDungeon:i==1?ThirdFactionSpawnPolicy.NearTerritory:ThirdFactionSpawnPolicy.NearStrongEnemy;
                data.Objective=i==0?ThirdFactionObjectiveKind.RaidDungeon:ThirdFactionObjectiveKind.RaidTerritory;
                if(i==2){data.MinThreatLevel=15;data.CooldownTurns=10;}
                AssetDatabase.CreateAsset(data,folder+"/"+filenames[i]+".asset");
            }
            Undo.RecordObject(data,"イベントの表示名を日本語に変更");data.DisplayName=EventName(category);EditorUtility.SetDirty(data);
            if(!events.Contains(data))events.Add(data);
        }
        Undo.RecordObject(config,"第三陣営イベントを登録");config.Events=events.ToArray();EditorUtility.SetDirty(config);AssetDatabase.SaveAssets();return config;
    }
}
