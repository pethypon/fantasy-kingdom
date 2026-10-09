#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Manual read-only inspection. Never serializes a map or reads telemetry files during OnGUI.</summary>
public sealed class AIExplorationDecisionWindow : EditorWindow
{
    Vector2 scroll;
    int teamIndex;
    string details = "再生中に「最新の判断を取得」を押すと、探索の目標と再計画の理由を表示します。";
    [MenuItem("Fantasy Kingdom/AI/探索判断の確認（日本語）", priority = 17)]
    public static void Open() => GetWindow<AIExplorationDecisionWindow>("AI探索の確認").Show();
    void OnGUI()
    {
        EditorGUILayout.HelpBox("現在のメモリ記録を読み取ります。更新ボタンを押すまで盤面の走査やファイルの読み込みは行いません。", MessageType.Info);
        teamIndex = EditorGUILayout.Popup("確認する陣営", teamIndex, new[] { "敵軍AI", "プレイヤーAI（開発用）" });
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("最新の判断を取得")) RefreshDetails();
        if (GUILayout.Button("探索の設定を開く")) ExplorationAISettingsEditor.OpenSettings();
        EditorGUILayout.EndHorizontal();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.SelectableLabel(details, EditorStyles.wordWrappedLabel,
            GUILayout.MinHeight(Mathf.Max(200, EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(details), Mathf.Max(300, position.width - 32)))));
        EditorGUILayout.EndScrollView();
    }
    void RefreshDetails()
    {
        var turn = Object.FindFirstObjectByType<TurnGenerator>();
        var commander = teamIndex == 0 ? turn?.Systems.AICommander : turn?.PlayerAI?.Commander;
        var planner = commander?.StrategicGovernor.Exploration;
        if (planner == null) { details = "対象のAIがまだ起動していません。ゲームを再生してから更新してください。"; return; }
        var text = new StringBuilder(2048);
        text.AppendLine(teamIndex == 0 ? "敵軍AIの探索" : "開発用プレイヤーAIの探索");
        text.Append("記憶したマス ").Append(planner.Memory.Count).Append("　地域候補 ").Append(planner.Frontiers.Count)
            .Append("　今回の候補数 ").Append(planner.LastCandidateCount).Append("　経路の計算回数 ").Append(planner.KnownRouteBuildCount).AppendLine();
        foreach (var objective in planner.Objectives)
            text.Append("駒ID ").Append(objective.AssignedUnitId).Append("　目標 ").Append(objective.ObjectiveId)
                .Append("　地域 ").Append(objective.FrontierRegionId).Append("　状態 ").Append(StateName(objective.State))
                .Append("　目的地 ").Append(objective.TargetCell).Append("　作成T ").Append(objective.CreatedTurn)
                .Append("　新規視界 ").Append(objective.NewlyRevealedSinceStart)
                .Append("　既知の迂回路 ").Append(objective.UsesKnownRouteDistance ? "あり" : "未確認")
                .Append("　次の通過点 ").Append(objective.RouteWaypoint).Append("　理由 ").Append(Reason(objective.LastReason)).AppendLine();
        var records = planner.Diagnostics;
        for (int i = Mathf.Max(0, records.Count - 12); i < records.Count; i++)
        {
            var record = records[i];
            text.AppendLine().Append("T ").Append(record.Turn).Append("　駒ID ").Append(record.Explorer)
                .Append("　目標の経過T ").Append(record.ObjectiveAge).Append("　目的地 ").Append(record.TargetFrontier).AppendLine();
            text.Append("目標への残り目安 ").Append(record.PreviousDistance.ToString("F1")).Append(" → ").Append(record.DistanceToTarget.ToString("F1"))
                .Append("　直近3Tの新規視界 ").Append(record.NewlyRevealedLast3T).Append("　異なる位置 ").Append(record.RecentUniqueCells)
                .Append("　ループ ").Append(record.LoopSuspected ? "あり" : "なし").Append("　").Append(Reason(record.Reason)).AppendLine();
            if (record.Candidates == null) continue;
            for (int j = 0; j < Mathf.Min(8, record.Candidates.Count); j++)
            {
                var c = record.Candidates[j];
                text.Append("地域 ").Append(c.RegionId).Append("：評価 ").Append(c.Score.ToString("F1"))
                    .Append("　未知 +").Append(c.UnknownGain.ToString("F1")).Append("　古い情報 +").Append(c.StaleIntel.ToString("F1"))
                    .Append("　距離 −").Append(c.TravelCost.ToString("F1")).Append("　危険 −").Append(c.DangerCost.ToString("F1"))
                    .Append("　最近の訪問 −").Append(c.RecentVisit.ToString("F1")).Append("　他のScout −").Append(c.OtherScout.ToString("F1"))
                    .Append("　").Append(Reason(c.RejectReason)).AppendLine();
            }
        }
        details = text.ToString();
    }
    static string StateName(ObjectiveState state) => state switch
    {
        ObjectiveState.Active => "探索中", ObjectiveState.Suspended => "中断中",
        ObjectiveState.Completed => "達成", _ => "再計画が必要"
    };
    static string Reason(string reason) => reason switch
    {
        "frontier_completed" => "開拓地域を確認済み", "objective_stalled" => "探索が停滞",
        "loop_detected" => "往復・周回を検出", "unreachable" => "経路を失った",
        "frontier_unreachable" => "到達できる経路なし", "emergency_suspend" => "緊急防衛で中断",
        "unit_lost" => "駒を失った／行動不能", "recently_explored" => "最近確認した地域",
        "frontier_already_assigned" => "別のScoutが担当中", "loop_area_penalty" => "最近ループした地域",
        "danger_too_high" => "危険が大きすぎる", "no_unknown_gain" => "新規視界の見込みなし",
        "objective_assigned" => "探索目標を設定", "objective_progress" => "探索の進捗を確認",
        "emergency_resume" => "防衛後に探索を再開", "no_safe_frontier" => "安全に進める地域なし",
        null => "", "" => "", _ => reason
    };
}
#endif
