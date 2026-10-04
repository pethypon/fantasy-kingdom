#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class ThirdFactionDirectorWindow : EditorWindow
{
    Vector2 scroll;
    [MenuItem("Fantasy Kingdom/Third Faction/Director Debug")]
    static void Open() => GetWindow<ThirdFactionDirectorWindow>("第三陣営 Director");
    [MenuItem("Fantasy Kingdom/Third Faction/Edit Config")]
    static void EditConfig() => Selection.activeObject = Resources.Load<ThirdFactionDirectorConfig>("AI/ThirdFaction/DirectorConfig");
    void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }
    void OnGUI()
    {
        var third = FindFirstObjectByType<ThirdFactionSystem>();
        if (third == null || third.State == null) { EditorGUILayout.HelpBox("Play中の第三陣営を表示します。設定は Edit Config から変更できます。", MessageType.Info); return; }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        var state = third.State; var w = third.LastSnapshot; var d = third.LastDecision;
        EditorGUILayout.LabelField($"IP {third.IP.Current}/50  +5/turn", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"IP before {third.LastIPBefore} → regen {third.LastIPAfterRegen} → decision {third.IP.Current}");
        EditorGUILayout.LabelField($"Turn {state.LastProcessedTurn}   Mode {d?.Mode ?? state.Mode}");
        EditorGUILayout.LabelField("Decision", d?.Event?.Definition.DisplayName ?? "NoEvent");
        EditorGUILayout.LabelField("Reason", d?.Reason ?? "waiting_for_turn");
        if (w != null)
        {
            EditorGUILayout.LabelField($"Power   Player {w.Player.MilitaryPower:F1} / Enemy {w.Enemy.MilitaryPower:F1} / Third {w.Third.MilitaryPower:F1}");
            EditorGUILayout.LabelField($"War {w.ActiveWar:F2}   Stagnation {w.Stagnation:F2}   Growth {w.Growth:F2}");
            EditorGUILayout.LabelField($"Balanced war {w.BalancedWar}   Decisive phase {w.Decisive}");
            EditorGUILayout.LabelField($"Prediction {w.Prediction?.Kind}   {w.Prediction?.Probability:P0}   {w.Prediction?.EstimatedTurns} turns");
        }
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Candidates", EditorStyles.boldLabel);
        if (d != null) foreach (var a in d.Candidates)
        {
            EditorGUILayout.LabelField($"{a.Definition.EventId}   IP {a.Definition.IpCost}   Score {a.Score:F1}");
            EditorGUILayout.LabelField("  " + (a.Rejection ?? "eligible"));
        }
        EditorGUILayout.LabelField("Active events", state.ActiveEvents.Count.ToString());
        foreach (var o in state.Objectives) EditorGUILayout.LabelField($"Actor {o.ActorId}: {o.Kind} {o.TargetCell} until {o.ExpireTurn}");
        EditorGUILayout.EndScrollView();
    }
}
#endif
