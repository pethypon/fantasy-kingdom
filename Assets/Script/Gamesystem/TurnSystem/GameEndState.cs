using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameEndState : StateCore
{
    private readonly TurnGenerater turn;
    private readonly GameResult result;
    private readonly GameEndReason reason;

    public GameEndState(TurnGenerater turn, GameResult result, GameEndReason reason)
    {
        this.turn = turn;
        this.result = result;
        this.reason = reason;
    }

    public void Entry()
    {
        Debug.Log($"[GameEnd] ゲーム終了 結果: {result} / 理由: {reason}");
        ShowResultOverlay();
    }

    public void Update() { }
    public void Exit() { }

    private void ShowResultOverlay()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        var root = new GameObject("GameEndOverlay", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);

        var textGo = new GameObject("GameEndText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(root.transform, false);
        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 56;
        text.text = $"{(result == GameResult.Win ? "WIN" : "LOSE")}\n<size=32>{ReasonLabel(reason)}</size>";

        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0.5f, 0.5f);
        textRt.anchorMax = new Vector2(0.5f, 0.5f);
        textRt.sizeDelta = new Vector2(1200f, 300f);
        textRt.anchoredPosition = Vector2.zero;
    }

    private static string ReasonLabel(GameEndReason reason)
    {
        return reason == GameEndReason.TimeLimit ? "時間切れ判定" : "クリスタル / 王の撃破";
    }
}
