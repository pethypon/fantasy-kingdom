#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ExplorationAISettings))]
public sealed class ExplorationAISettingsEditor : Editor
{
    const string AssetPath = "Assets/Resources/AI/ExplorationSettings.asset";
    [MenuItem("Fantasy Kingdom/AI/探索・ループ防止の設定", priority = 16)]
    public static void OpenSettings()
    {
        var asset = AssetDatabase.LoadAssetAtPath<ExplorationAISettings>(AssetPath);
        if (asset == null)
        {
            UnitWorkshopWindow.EnsureFolder("Assets/Resources/AI");
            asset = CreateInstance<ExplorationAISettings>();
            AssetDatabase.CreateAsset(asset, AssetPath); AssetDatabase.SaveAssets();
        }
        Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
    }
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Scoutが開拓する地域を選び、目標を複数ターン保持します。未知が減らない往復・停滞を検知して再計画します。設定はゲーム開始前に調整してください。", MessageType.Info);
        Field("Enabled", "探索R2を有効にする");
        Group("記憶・停滞・到達", new[] {
            "RecentHistoryTurns|位置履歴の自軍ターン数", "StallTurns|停滞と判断するターン数",
            "FreshIntelTurns|最近見た地域の再索敵を控えるターン数", "LowIntelTurns|情報価値が低い期間の終わり",
            "MediumIntelTurns|情報価値が中程度の期間の終わり", "StaleIntelTurns|古い情報と判断するターン数",
            "RecentVisitTurns|最近訪問した地域を避ける期間", "VisitCountCap|訪問回数の評価上限",
            "CompletionRevealCells|目標達成に必要な新規視界数", "CompletionDistance|到達と判断する距離",
            "RevealCreditRadius|新規視界をScoutの成果に含める距離", "FailedMoveLimit|移動失敗による再計画回数",
            "ProgressDistanceThreshold|進展と判断する距離の改善量", "RouteCacheTurns|既知の迂回経路を再利用するターン数" });
        Group("ループの検知", new[] {
            "LoopMinimumSamples|ループ検知に必要な履歴数", "LoopUniqueCellLimit|狭い範囲と判断する異なる位置の数",
            "LoopCooldownTurns|ループ地域を避けるターン数", "LoopAreaPenalty|ループ地域の減点" });
        Group("開拓地域の評価", new[] {
            "UnknownGainWeight|未知の量の評価", "StaleIntelWeight|古い重要情報を確認する評価",
            "DirectionDiversityWeight|別方向に進む評価", "StrategicRouteWeight|重要地点への経路の評価",
            "DistanceFromRecentAreaWeight|最近の活動域から離れる評価", "RecentVisitPenalty|最近見た地域の減点",
            "RepeatVisitPenalty|同じ地域を繰り返し訪問する減点", "OtherScoutPenalty|別Scoutが担当中の地域の減点",
            "DangerWeight|地域の危険度の減点", "TravelCostWeight|移動距離のコスト", "MinimumFrontierScore|目標を採用する最低評価" });
        Group("移動と安全性", new[] {
            "TargetDistanceWeight|遠方の目標へ近づく評価", "MaximumDistanceProgressBonus|距離改善の評価上限",
            "LocalRevealWeight|今回の移動で未知を開く評価", "MaximumLocalRevealBonus|新規視界の評価上限",
            "MoveDangerWeight|移動先の危険度の減点", "SuicideMovePenalty|致命的な移動先の減点",
            "RouteDangerWeight|迂回経路の危険コスト", "RouteDeviationPenalty|確認済み経路から外れる減点",
            "MaximumFrontierDangerFraction|地域選択を控える被害率", "LowIntelValue|古い情報の低い評価倍率",
            "MediumIntelValue|古い情報の中程度の評価倍率", "HighIntelValue|古い情報の高い評価倍率" });
        Group("計算量の上限", new[] {
            "RegionSize|１地域の辺のマス数", "MaxCandidateRegions|比較する候補地域数",
            "FrontierRefreshTurns|地域境界を定期更新するターン間隔", "MaxMapDimension|保存から読み込めるマップの最大辺",
            "MaxSavedCells|保存から読み込める記憶の最大マス数", "MaxSavedActors|保存から読み込める探索駒の最大数",
            "MaxRouteNodes|迂回探索で調べる位置の上限", "MaxRouteLength|保持する迂回経路の長さ上限",
            "MaxCachedRoutes|再利用する既知経路の数の上限" });
        Group("探索の診断ログ", new[] {
            "EnableJsonlTelemetry|詳細をJSONLファイルへ保存する", "TelemetryBufferRecords|まとめて書く記録数",
            "TelemetryMaxRecords|画面用に保持する記録数", "TelemetryMaxFileBytes|ログファイルの上限バイト数" });
        EditorGUILayout.HelpBox("詳細ログは標準OFFです。候補・目標・再計画の理由は、探索判断の確認画面から確認できます。王の生存・緊急防衛や合法な移動ルールは変更しません。", MessageType.Info);
        serializedObject.ApplyModifiedProperties();
    }
    void Group(string title, string[] fields)
    {
        EditorGUILayout.Space(); EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        foreach (string entry in fields)
        { int separator = entry.IndexOf('|'); Field(entry.Substring(0, separator), entry.Substring(separator + 1)); }
    }
    void Field(string name, string label)
    {
        var property = serializedObject.FindProperty(name);
        if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label));
    }
}
#endif
