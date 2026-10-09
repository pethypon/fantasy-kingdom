#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AIReflectionConfig))]
public sealed class AIReflectionConfigEditor : Editor
{
    const string AssetPath = "Assets/Resources/AI/ReflectionConfig.asset";
    readonly HashSet<string> drawn = new HashSet<string>();
    [MenuItem("Fantasy Kingdom/AI/自己評価・日記の設定", priority = 15)]
    public static void OpenSettings()
    {
        var asset = AssetDatabase.LoadAssetAtPath<AIReflectionConfig>(AssetPath);
        if (asset == null)
        {
            UnitWorkshopWindow.EnsureFolder("Assets/Resources/AI");
            asset = CreateInstance<AIReflectionConfig>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
        }
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        drawn.Clear();
        EditorGUILayout.LabelField("AIの自己評価・学習・日記", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("既存AIの判断を、過去の実際の成果から小さく補正します。王の生存・緊急防衛の優先順位とFogの制限は維持します。開発用プレイヤーAIは、通常の学習データに保存しません。", MessageType.Info);
        Field("EnableReflection", "自己評価と記録を有効にする");
        Field("ApplyLearningToSelection", "学習結果を行動の選択にも使う");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("AI日記", EditorStyles.boldLabel);
        Field("DiaryIntervalOwnTurns", "自軍の何ターンごとに書くか");
        Field("EnableFileDiary", "日記をテキストファイルに保存する");
        Field("EnableConsoleDiary", "開発時に日記の要約をConsoleへ表示する");
        Field("EnableCandidateDebug", "候補の評価を詳しくログに出す");
        EditorGUILayout.HelpBox("標準は自軍15ターンごとと勝敗確定時です。ゲームのメニューから「AI日記を見る」で最新の日記を読めます。詳しい候補ログは必要な時だけ有効にしてください。", MessageType.Info);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("学習の強さと保存量", EditorStyles.boldLabel);
        Field("ReflectionLearningRate", "学習率"); Field("MinimumLearningRate", "学習率の下限");
        Field("MinLearnedValue", "評価補正の下限"); Field("MaxLearnedValue", "評価補正の上限");
        Field("LearningInfluenceMultiplier", "選択への影響倍率");
        Field("MinimumSamplesForSelection", "選択に使う最低サンプル数");
        Field("FullConfidenceSamples", "信頼度が最大になるサンプル数");
        Field("MaxKnowledgeEntries", "保持する学習パターンの最大数");
        Field("MaxRecentRecords", "保持する最近の行動記録数");
        Field("MaxRewardEvents", "１戦で保持する報酬の重複防止記録数");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("失敗の評価（調整対象）", EditorStyles.boldLabel);
        Field("RepeatedNoProgressPenalty", "進展のない同一行動の減点");
        Field("OscillationPenalty", "意味のない往復移動の減点");
        Field("FailedActionPenalty", "行動実行失敗の減点"); Field("DefeatReward", "敗北時の戦闘報酬（標準０）");
        EditorGUILayout.HelpBox("標準の成果報酬は通常撃破＋1、陣形撃破＋2の合計、Artifact＋1、経済回復の１エピソードにつき＋1です。Healthyの維持だけでは報酬を与えません。勝利＋100は戦闘全体だけに記録します。以下の調整値はBALANCE_TODOです。", MessageType.Info);
        Section("成果と生存の評価（調整対象）");
        Fields("NormalKillRewardValue", "通常撃破の報酬", "FormationKillRewardValue", "陣形撃破の合計報酬",
            "ArtifactRewardValue", "Artifact取得の報酬", "EconomyRecoveryReward", "経済回復１回の報酬",
            "GoodTradeSurvivalReward", "良い交換で戦力を温存した報酬", "OwnLossSurvivalPenalty", "成果のない自軍損失の減点",
            "GoodTradeLightDamageRatio", "良い交換と判断する被害のHP比上限",
            "RetreatSurvivalReward", "安全な撤退の生存報酬", "RetreatPositionReward", "安全な撤退の位置報酬",
            "RetreatLowHpThreshold", "撤退を評価する低HP比率", "RetreatMaximumPowerRatio", "撤退を評価する局所戦力比の上限",
            "RewardMaximumIncomingFraction", "安全と判断する予想被害のHP比上限",
            "MinimumRiskImprovement", "被害軽減と判断する最小差", "CrystalRetreatAllowance", "撤退時に許容するCrystalからの距離増加");
        Section("位置・情報・準備の評価（調整対象）");
        Fields("HighGroundPositionReward", "有利な高所への移動報酬", "RangePositionReward", "射程を活かした位置報酬",
            "RangeSurvivalReward", "射程を活かした生存報酬", "BadRangePositionPenalty", "不利な近距離への移動減点",
            "RangedMinimumAttackRange", "近距離進入と判断する距離", "OverextensionBeforePowerRatio", "単独前進前の戦力比の判定値",
            "OverextensionAfterPowerRatio", "単独前進後の危険な戦力比", "LocalPowerReward", "局所戦力比が改善した報酬",
            "LocalPowerPenalty", "局所戦力比が悪化した減点", "LocalPowerDeltaThreshold", "戦力比の変化を評価する最小差",
            "ObjectiveProgressReward", "安全な目標接近の報酬", "DefenseReward", "Crystalの危険軽減の報酬",
            "ScoutTileReward", "新規地形の観測報酬", "ScoutEnemyDiscoveryReward", "敵を新たに発見した報酬",
            "ScoutUncertaintyReward", "敵情報の不確実性を減らした報酬", "PreparationReward", "有効な状態効果などの準備報酬");
        Section("行動報酬の上限（調整対象）");
        Fields("MinActionReward", "１行動の報酬下限", "MaxActionReward", "１行動の報酬上限",
            "CombatRewardCap", "戦闘報酬のカテゴリ上限", "ArtifactRewardCap", "Artifact報酬のカテゴリ上限",
            "SurvivalRewardCap", "生存評価のカテゴリ上限", "PositionRewardCap", "位置評価のカテゴリ上限",
            "LocalPowerRewardCap", "局所戦力評価のカテゴリ上限", "ObjectiveRewardCap", "目標評価のカテゴリ上限",
            "EconomyRewardCap", "経済評価のカテゴリ上限", "InformationRewardCap", "情報評価のカテゴリ上限",
            "DefenseRewardCap", "防衛評価のカテゴリ上限", "EfficiencyRewardCap", "効率評価のカテゴリ上限",
            "PreparationRewardCap", "準備評価のカテゴリ上限", "PenaltyCategoryCap", "各減点カテゴリの上限");
        Section("後続成果の還元と経済エピソード（調整対象）");
        Fields("DelayedCreditWindow", "因果関係を調べる直近行動数", "DelayedCreditDecay", "古い準備行動への還元倍率",
            "DelayedCreditBaseReward", "後続成果の基本還元値", "MaxDelayedRewardPerAction", "１行動への後続成果還元の累計上限",
            "EconomyRecoveryResetTurns", "経済回復を再評価するために必要な悪化自軍ターン数");
        EditorGUILayout.HelpBox("後続成果は関連する準備行動だけに還元します。同じ成果イベントを二重に付与せず、日記文章は学習に使いません。", MessageType.Info);
        Section("軍の活用・過剰生産・機会損失（調整対象）");
        Fields("IdleArmyTurnThreshold", "軍の停滞を評価する自軍ターン数", "MissedOpportunityTurnThreshold", "好機を逃したと判断する自軍ターン数",
            "IdleArmyPenalty", "軍全体の未活用減点", "ExcessiveProductionPenalty", "過剰生産の減点",
            "MissedOpportunityPenalty", "機会損失の減点", "UnnecessaryTurtlePenalty", "不要な本拠地停滞の減点",
            "MaxArmyTurnPenalty", "１自軍ターンの軍全体減点上限", "ArmyStrongPowerRatio", "優勢と判断する戦力比",
            "ArmyProductionWindowTurns", "増産と維持費を調べる自軍ターン幅", "ArmyOverproductionUnitGrowth", "急増と判断する追加ユニット数",
            "ArmyLowUtilizationThreshold", "低い活用率と判断する割合", "ArmyDefenseRadius", "合理的な守備と判断するCrystal周辺の距離",
            "ArmyEnemyFarDistance", "観測した敵が遠いと判断する距離", "ArmyAtBaseFraction", "軍が本拠地に留まると判断する割合");
        EditorGUILayout.HelpBox("守備・予備兵力・AP温存を自動的に減点しません。観測した敵より優勢で、安全な状況でも進展がない場合に評価します。必要な増産は罰しません。", MessageType.Info);
        Section("学習状況を分ける戦闘段階（調整対象）");
        Fields("EarlyPhaseMaxTurns", "序盤と判断する最大戦闘ターン", "LatePhaseMinTurns", "終盤と判断する最小戦闘ターン",
            "LatePhaseUnitThreshold", "終盤と判断する自軍ユニット数", "LatePhaseTerritoryThreshold", "終盤と判断する領土数",
            "LatePhaseCrystalHpRatio", "終盤と判断するCrystalのHP比率");
        // Keep subsequently added public settings editable even before a dedicated grouping is added.
        var remaining = serializedObject.GetIterator();
        bool enter = true;
        while (remaining.NextVisible(enter))
        {
            enter = false;
            if (remaining.name == "m_Script" || drawn.Contains(remaining.name)) continue;
            EditorGUILayout.PropertyField(remaining, new GUIContent("追加の調整項目: " + remaining.displayName), true);
        }
        serializedObject.ApplyModifiedProperties();
    }
    static void Section(string title) { EditorGUILayout.Space(); EditorGUILayout.LabelField(title, EditorStyles.boldLabel); }
    void Fields(params string[] fields) { for (int i = 0; i + 1 < fields.Length; i += 2) Field(fields[i], fields[i + 1]); }
    void Field(string name, string label)
    {
        var property = serializedObject.FindProperty(name);
        if (property == null) return;
        drawn.Add(name); EditorGUILayout.PropertyField(property, new GUIContent(label));
    }
}
#endif
