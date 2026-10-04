#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class ThirdFactionDirectorWindow : EditorWindow
{
    Vector2 scroll;
    [MenuItem("Fantasy Kingdom/第三陣営/判断状況を確認")]
    static void Open() => GetWindow<ThirdFactionDirectorWindow>("第三陣営の判断状況");
    [MenuItem("Fantasy Kingdom/第三陣営/設定データを選択")]
    static void EditConfig() => Selection.activeObject = Resources.Load<ThirdFactionDirectorConfig>("AI/ThirdFaction/DirectorConfig");
    void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }
    void OnGUI()
    {
        var third = FindFirstObjectByType<ThirdFactionSystem>();
        if (third == null || third.State == null) { EditorGUILayout.HelpBox("ゲーム再生中に第三陣営の判断状況を表示します。設定は「開発スタジオ → 第三陣営のイベント」で変更できます。", MessageType.Info); return; }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        var state = third.State; var w = third.LastSnapshot; var d = third.LastDecision;
        EditorGUILayout.LabelField($"介入ポイント {third.IP.Current}/50 IP  毎ターン＋5 IP", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"判断前 {third.LastIPBefore} → 回復後 {third.LastIPAfterRegen} → 判断後 {third.IP.Current}");
        EditorGUILayout.LabelField($"ターン {state.LastProcessedTurn}   判断モード {d?.Mode ?? state.Mode}");
        EditorGUILayout.LabelField("選んだイベント", d?.Event?.Definition.DisplayName ?? "介入なし");
        EditorGUILayout.LabelField("判断理由（診断用ID）", d?.Reason ?? "次のターンを待機中");
        if (w != null)
        {
            EditorGUILayout.LabelField($"戦力   プレイヤー {w.Player.MilitaryPower:F1} / 敵軍 {w.Enemy.MilitaryPower:F1} / 第三陣営 {w.Third.MilitaryPower:F1}");
            EditorGUILayout.LabelField($"交戦度 {w.ActiveWar:F2}   膠着度 {w.Stagnation:F2}   成長度 {w.Growth:F2}");
            EditorGUILayout.LabelField($"互角の戦闘 {(w.BalancedWar?"はい":"いいえ")}   決着間近 {(w.Decisive?"はい":"いいえ")}");
            EditorGUILayout.LabelField($"予測 {w.Prediction?.Kind}   確率 {w.Prediction?.Probability:P0}   {w.Prediction?.EstimatedTurns}ターン後");
        }
        EditorGUILayout.Space(); EditorGUILayout.LabelField("イベントの候補", EditorStyles.boldLabel);
        if (d != null) foreach (var a in d.Candidates)
        {
            EditorGUILayout.LabelField($"{a.Definition.DisplayName}   必要IP {a.Definition.IpCost}   評価 {a.Score:F1}");
            EditorGUILayout.LabelField("  " + (a.Rejection ?? "実行条件を満たす"));
        }
        EditorGUILayout.LabelField("進行中のイベント数", state.ActiveEvents.Count.ToString());
        foreach (var o in state.Objectives) EditorGUILayout.LabelField($"駒ID {o.ActorId}: {o.Kind} 目標{o.TargetCell} ／ ターン{o.ExpireTurn}まで");
        EditorGUILayout.EndScrollView();
    }
}
#endif
