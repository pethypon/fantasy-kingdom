#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Japanese authoring surface for every operation balance and goal-weight template.</summary>
[CustomEditor(typeof(AIOperationConfig))]
public sealed class AIOperationConfigEditor : Editor
{
    const string AssetPath = "Assets/Resources/AI/OperationConfig.asset";
    static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
    {
        { "Enabled", "複数ターン作戦を有効にする" },
        { "ApplyLearningToSelection", "作戦経験を候補の選択にも使用する" },
        { "EnablePersistentLearning", "作戦経験を試合後も保存する" },
        { "EnableDebug", "開発時に作戦の判断を詳しく記録する" },
        { "MinimumThreatLevel", "作戦を有効にする最低脅威度" },
        { "MinimumSamplesForSelection", "候補選択に使用する最低経験数" },
        { "FullConfidenceSamples", "信頼度が最大になる経験数" },
        { "LearningRate", "作戦経験の学習率" },
        { "MaxOperationExperienceModifier", "作戦選択への経験補正上限" },
        { "MaxActionBonus", "通常行動への作戦補正上限" },
        { "OperationLearningMin", "作戦学習値の下限" },
        { "OperationLearningMax", "作戦学習値の上限" },
        { "MaxLearningRewardWithoutPrimaryGoal", "主目標未達成時の学習報酬上限" },
        { "MaxOperationCreditPerAction", "１行動への作戦成功還元の累計上限" },
        { "OperationCreditDecay", "古い関連行動への還元倍率" },
        { "DirectGoalContribution", "主目標を直接達成した行動の寄与率" },
        { "PreparationContribution", "直前の有効な準備行動の寄与率" },
        { "ScoutContribution", "有効な偵察行動の寄与率" },
        { "SupportContribution", "有効な支援行動の寄与率" },
        { "MaxConcurrentOperations", "同時に実行する作戦数" },
        { "DefaultExpectedDuration", "標準の予定自軍ターン数" },
        { "DefaultMaximumDuration", "標準の最大自軍ターン数" },
        { "ReplanAfterStalledTurns", "再計画を検討する停滞自軍ターン数" },
        { "MaxKnowledgeEntries", "保持する作戦経験の最大数" },
        { "MaxActiveActionRecords", "１作戦で保持する行動記録数" },
        { "MaxCompletedOperations", "保持する完了作戦の最大数" },
        { "MaxRevisions", "１作戦の計画変更記録数" },
        { "MaxSteps", "１作戦の手順数上限" },
        { "MaxAssignments", "１作戦に割り当てる駒の数上限" },
        { "MaxCompletedOperationIds", "作戦学習の重複防止記録数" },
        { "MaxProfileFileBytes", "作戦経験ファイルのサイズ上限（バイト）" },
        { "MaxScoreWithoutPrimaryGoal", "主目標未達成時の最高点" },
        { "PerfectScore", "作戦の満点" },
        { "MaxScoreWithoutPerfectGate", "最高評価の条件を満たさない時の最高点" },
        { "MaxProvisionalScore", "進行中の暫定評価の最高点" },
        { "PerfectMinimumSurvival", "最高評価に必要な戦力生存率" },
        { "PartialGoalProgressCap", "部分達成の主目標進捗上限" },
        { "CrystalDefenseMinimumHpRatio", "防衛成功に必要なクリスタルHP比率" },
        { "CrystalDamageTolerance", "防衛時に許容するクリスタルHP比率の減少" },
        { "CatastrophicLossRatio", "壊滅と判断する戦力損失率" },
        { "LowLossRatio", "低損失と判断する戦力損失率" },
        { "GoodArmyUtilizationThreshold", "軍を活用したと判断する割合" },
        { "UnderutilizationThreshold", "軍の未活用と判断する割合" },
        { "StrongPreparationThreshold", "有効な準備と判断する寄与率" },
        { "SuccessfulExploitationThreshold", "優位を活用したと判断する進捗率" },
        { "DefaultScoutTargetTiles", "標準の探索目標マス数" },
        { "FullInformationEnemyContacts", "情報獲得の満点に必要な新規敵発見数" },
        { "DefaultTerritoryGain", "標準の領土獲得目標マス数" },
        { "DefaultTargetPowerReduction", "標準の敵戦力削減目標率" },
        { "DefaultExpectedApBudget", "標準の作戦AP予算" },
        { "EstimatedKillPower", "観測記録のない撃破の戦力評価値" },
        { "DiversionMinimumEnemies", "陽動成功に必要な観測敵の移動数" },
        { "DiversionMinimumDistanceGain", "陽動成功に必要な敵の距離増加" },
        { "ObjectiveDefenseRadius", "目標周辺の敵守備を調べる距離" },
        { "UnknownOperationRisk", "敵戦力が未確認のときの予想損失率" },
        { "ObservedPowerRiskScale", "観測戦力比から予想損失率を求める倍率" },
        { "PerfectRankThreshold", "最高評価に必要な点数" },
        { "ExcellentRankThreshold", "優秀評価に必要な点数" },
        { "SuccessRankThreshold", "成功評価に必要な点数" },
        { "PartialRankThreshold", "部分成功評価に必要な点数" },
        { "NeutralRankThreshold", "中立評価に必要な点数" },
        { "FailureRankThreshold", "失敗と重大失敗の境界点数" },
        { "PerfectLearningReward", "最高評価の学習報酬" },
        { "ExcellentLearningReward", "優秀評価の学習報酬" },
        { "SuccessLearningReward", "成功評価の学習報酬" },
        { "PartialLearningReward", "部分成功評価の学習報酬" },
        { "NeutralLearningReward", "中立評価の学習報酬" },
        { "FailureLearningReward", "失敗評価の学習報酬" },
        { "CriticalFailureLearningReward", "重大失敗評価の学習報酬" },
        { "StrategicAbortScore", "合理的な作戦中止の標準点" },
        { "StrategicAbortLearningReward", "合理的な作戦中止の学習報酬" },
        { "CrystalExposurePenalty", "クリスタルの危険増加の減点" },
        { "EconomicCollapsePenalty", "経済破綻の減点" },
        { "CatastrophicLossPenalty", "軍壊滅の減点" },
        { "StallPenalty", "作戦停滞の減点" },
        { "ArmyIdlePenalty", "軍の未活用の減点" },
        { "BadIntelPenalty", "情報不足の減点" },
        { "ResourceWastePenalty", "過大な資源消費の減点" },
        { "OvercommitmentPenalty", "危険な作戦への固執の減点" },
        { "MaximumPenalty", "作戦全体の減点上限" },
        { "MaxScoreDestroyedOwnCrystal", "自軍クリスタル破壊時の最高点" },
        { "GoalAchievement", "主目標達成" }, { "MilitaryOutcome", "軍事成果" },
        { "Survival", "生存" }, { "Efficiency", "効率" }, { "Position", "位置" },
        { "Territory", "領土" }, { "Economy", "経済" }, { "Information", "情報" },
        { "Defense", "防衛" }, { "TimeEfficiency", "時間効率" },
        { "ArmyUtilization", "軍の活用" }, { "Preparation", "有効な準備" },
        { "Exploitation", "成功後の優位活用" }, { "RiskControl", "危険管理" }
    };

    [MenuItem("Fantasy Kingdom/AI/複数ターン作戦の設定", priority = 16)]
    public static void OpenSettings()
    {
        var asset = AssetDatabase.LoadAssetAtPath<AIOperationConfig>(AssetPath);
        if (asset == null)
        {
            EnsureFolder("Assets/Resources/AI");
            asset = CreateInstance<AIOperationConfig>();
            AssetDatabase.CreateAsset(asset, AssetPath); AssetDatabase.SaveAssets();
        }
        Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.LabelField("複数ターンの作戦・作戦経験", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("作戦全体の目的達成を0～100点で採点し、小さな経験値へ変換します。通常の行動評価とは別です。緊急防衛の優先順位とFogの制限を守り、日記の文章を学習には使いません。", MessageType.Info);
        var iterator = serializedObject.GetIterator(); bool enter = true;
        while (iterator.NextVisible(enter))
        {
            enter = false;
            if (iterator.name == "m_Script") continue;
            if (iterator.name == "GoalWeights") { DrawWeights(iterator); continue; }
            EditorGUILayout.PropertyField(iterator, new GUIContent(Label(iterator.name)), true);
        }
        serializedObject.ApplyModifiedProperties();
        DrawValidation((AIOperationConfig)target);
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("BALANCE_TODO：配点・期間・寄与率・学習率は実際の対戦結果から調整してください。標準配点へ戻す操作はUnityの「元に戻す」で取り消せます。", MessageType.Info);
        if (GUILayout.Button("目的別配点を標準テンプレートに戻す")) RestoreWeightTemplates();
    }

    void DrawWeights(SerializedProperty property)
    {
        EditorGUILayout.Space();
        property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, "目的別の配点（合計100点）", true);
        if (!property.isExpanded) return;
        EditorGUI.indentLevel++;
        int size = Math.Max(0, EditorGUILayout.IntField("配点テンプレート数", property.arraySize));
        if (size != property.arraySize) property.arraySize = size;
        for (int i = 0; i < property.arraySize; i++)
        {
            var item = property.GetArrayElementAtIndex(i);
            var goal = item.FindPropertyRelative("Goal");
            string title = "配点 " + (i + 1) + "：" + AIOperationDiary.GoalLabel((AIOperationGoal)goal.intValue);
            item.isExpanded = EditorGUILayout.Foldout(item.isExpanded, title, true);
            if (!item.isExpanded) continue;
            EditorGUI.indentLevel++;
            var values = (AIOperationGoal[])Enum.GetValues(typeof(AIOperationGoal));
            var labels = new string[values.Length];
            for (int index = 0; index < labels.Length; index++) labels[index] = AIOperationDiary.GoalLabel(values[index]);
            goal.intValue = (int)values[EditorGUILayout.Popup("作戦の目的", Math.Max(0, Array.IndexOf(values, (AIOperationGoal)goal.intValue)), labels)];
            float sum = 0;
            var child = item.Copy(); var end = item.GetEndProperty(); bool childEnter = true;
            while (child.NextVisible(childEnter) && !SerializedProperty.EqualContents(child, end))
            {
                childEnter = false;
                if (child.name == "Goal") continue;
                EditorGUILayout.PropertyField(child, new GUIContent(Label(child.name)));
                if (child.propertyType == SerializedPropertyType.Float) sum += AIOperationConfig.NonNegative(child.floatValue);
            }
            EditorGUILayout.LabelField("配点合計", sum.ToString("0.##") + " / 100");
            if (Mathf.Abs(sum - 100) > .05f)
                EditorGUILayout.HelpBox("配点の合計が100ではありません。採点時は100点に正規化しますが、意図した配点に調整してください。", MessageType.Warning);
            EditorGUI.indentLevel--;
        }
        EditorGUI.indentLevel--;
    }

    static void DrawValidation(AIOperationConfig config)
    {
        if (config.MinimumSamplesForSelection < 2)
            EditorGUILayout.HelpBox("１回の経験だけで作戦を選びやすくなります。最低経験数は標準の２以上を推奨します。", MessageType.Warning);
        if (config.DefaultExpectedDuration > config.DefaultMaximumDuration)
            EditorGUILayout.HelpBox("予定期間が最大期間を超えています。最大期間を予定以上に設定してください。", MessageType.Warning);
        if (!(config.PerfectRankThreshold >= config.ExcellentRankThreshold && config.ExcellentRankThreshold >= config.SuccessRankThreshold
            && config.SuccessRankThreshold >= config.PartialRankThreshold && config.PartialRankThreshold >= config.NeutralRankThreshold
            && config.NeutralRankThreshold >= config.FailureRankThreshold))
            EditorGUILayout.HelpBox("評価ランクの境界が順番通りではありません。上のランクほど必要点数を高くしてください。", MessageType.Warning);
        var seen = new HashSet<AIOperationGoal>();
        if (config.GoalWeights != null)
            foreach (var weights in config.GoalWeights)
                if (weights != null && !seen.Add(weights.Goal))
                    EditorGUILayout.HelpBox(AIOperationDiary.GoalLabel(weights.Goal) + "の配点が重複しています。先頭の配点を使用します。", MessageType.Warning);
    }

    void RestoreWeightTemplates()
    {
        Undo.RecordObject(target, "作戦配点を標準に戻す");
        var defaults = CreateInstance<AIOperationConfig>();
        var config = (AIOperationConfig)target;
        config.GoalWeights = new List<AIOperationGoalWeights>();
        foreach (var weights in defaults.GoalWeights) config.GoalWeights.Add(weights.Copy());
        DestroyImmediate(defaults); EditorUtility.SetDirty(config); serializedObject.Update();
    }
    static string Label(string name) => Labels.TryGetValue(name, out string label) ? label : "追加の調整項目：" + name;
    static void EnsureFolder(string path)
    {
        string[] pieces = path.Split('/'); string current = pieces[0];
        for (int i = 1; i < pieces.Length; i++)
        {
            string next = current + "/" + pieces[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, pieces[i]);
            current = next;
        }
    }
}
#endif
