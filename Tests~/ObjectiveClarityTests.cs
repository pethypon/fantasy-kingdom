#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Checks result reasons and their actual result-screen layout without completing or saving a match.</summary>
public static class ObjectiveClarityTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Check(string label, bool ok)
    {
        if (!ok) throw new InvalidOperationException("[ObjectiveClarity] FAIL " + label);
        Debug.Log("[ObjectiveClarity] PASS " + label);
    }

    static Status Target(List<GameObject> owned, Kind kind, Team team)
    {
        var go = new GameObject("Result reason fixture " + team + " " + kind);
        go.SetActive(false);
        owned.Add(go);
        var target = go.AddComponent<Status>();
        target.kind = kind; target.team = team;
        target.type = kind == Kind.Crystal ? global::Type.Building : global::Type.Unit;
        target.HP = 0; target.MaxHP = 100;
        return target;
    }

    static Rect InParent(RectTransform child, RectTransform parent)
    {
        var corners = new Vector3[4]; child.GetWorldCorners(corners);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            var point = parent.InverseTransformPoint(corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static bool Contains(Rect outer, Rect inner, float tolerance = 1)
        => inner.xMin >= outer.xMin - tolerance && inner.xMax <= outer.xMax + tolerance
            && inner.yMin >= outer.yMin - tolerance && inner.yMax <= outer.yMax + tolerance;

    static TextMeshProUGUI Text(Transform root, string name)
        => root.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(text => text.name == name);

    static void CheckResultUI(GameEndState state, string label)
    {
        var canvas = UIBuilder.ScreenCanvas;
        var originals = new HashSet<GameObject>();
        foreach (Transform child in canvas.transform) originals.Add(child.gameObject);
        var build = typeof(GameEndState).GetMethod("BuildGameEndUI", PrivateInstance);
        Check("result UI can be rendered without applying match-end side effects", build != null);
        try
        {
            build.Invoke(state, null);
            var overlays = new List<Transform>();
            foreach (Transform child in canvas.transform)
                if (!originals.Contains(child.gameObject) && child.name == "GameEndOverlay") overlays.Add(child);
            Check(label + " creates one result overlay", overlays.Count == 1);
            var panel = overlays[0].Find("GameEndPanel") as RectTransform;
            var reason = Text(panel, "OutcomeReasonText");
            Check(label + " prominently displays the exact captured outcome reason", reason != null && reason.text == state.OutcomeReason
                && reason.gameObject.activeInHierarchy);
            Check(label + " keeps the existing result heading and replay controls", Text(panel, "ResultTitle") != null
                && panel.GetComponentsInChildren<Button>(true).Count(button => button.gameObject.activeInHierarchy && button.interactable) == 4);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            reason.ForceMeshUpdate();
            var reasonRect = InParent(reason.rectTransform, panel);
            Check(label + " fits the reason inside the result panel", Contains(panel.rect, reasonRect)
                && !reason.isTextTruncated && reason.textBounds.size.x <= reason.rectTransform.rect.width + 1
                && reason.textBounds.size.y <= reason.rectTransform.rect.height + 1);
            foreach (string otherName in new[] { "ResultTitle", "ResultDetail", "ThreatText" })
            {
                var other = Text(panel, otherName);
                if (other != null)
                    Check(label + " separates the reason from " + otherName, !reasonRect.Overlaps(InParent(other.rectTransform, panel)));
            }
            foreach (var button in panel.GetComponentsInChildren<Button>(true))
                Check(label + " keeps the reason above " + button.name, !reasonRect.Overlaps(InParent((RectTransform)button.transform, panel)));
        }
        finally
        {
            var generated = new List<GameObject>();
            foreach (Transform child in canvas.transform)
                if (!originals.Contains(child.gameObject) && child.name == "GameEndOverlay") generated.Add(child.gameObject);
            foreach (var overlay in generated) UnityEngine.Object.DestroyImmediate(overlay);
        }
    }

    public static void PlayMode(GameSystems systems)
    {
        var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
        Check("initialized screen canvas and turn systems are available", systems != null && turn != null && UIBuilder.ScreenCanvas != null);
        var owned = new List<GameObject>();
        var previousState = turn.CurrentState;
        int playerAP = systems.APSystem.GetAP(Team.Player), enemyAP = systems.APSystem.GetAP(Team.Enemy);
        try
        {
            foreach (var team in new[] { Team.Player, Team.Enemy })
            foreach (var kind in new[] { Kind.King, Kind.Crystal })
            {
                var target = Target(owned, kind, team);
                var result = team == Team.Player ? GameResult.Lose : GameResult.Win;
                var state = new GameEndState(turn, result, target);
                string outcome = state.OutcomeReason;
                string label = team + " " + kind;
                Check(label + " names the actual defeated target and outcome", !string.IsNullOrWhiteSpace(outcome)
                    && outcome.Contains(kind == Kind.King ? "王" : "クリスタル")
                    && outcome.Contains(team == Team.Player ? "敗北" : "勝利"));
                Check(label + " does not conflate the other defeat condition", !outcome.Contains(kind == Kind.King ? "クリスタル" : "王")
                    && !outcome.Contains("または") && !outcome.Contains("どちらか"));
                target.kind = Kind.Knight; target.team = Team.None;
                Check(label + " retains its reason when the original actor changes", state.OutcomeReason == outcome);
                UnityEngine.Object.DestroyImmediate(target.gameObject);
                Check(label + " retains its reason after the original actor is removed", state.OutcomeReason == outcome);
                CheckResultUI(state, label);
            }
            Check("rendering results does not end the live test match or consume actions", turn.CurrentState == previousState
                && systems.APSystem.GetAP(Team.Player) == playerAP && systems.APSystem.GetAP(Team.Enemy) == enemyAP);
        }
        finally
        {
            foreach (var go in owned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
#endif
