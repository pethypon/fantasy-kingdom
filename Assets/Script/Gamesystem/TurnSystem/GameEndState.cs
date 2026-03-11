using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameEndState : StateCore
{
    private TurnGenerater _turn;
    private GameResult _result;
    private string _reason;

    public GameEndState(TurnGenerater turn, GameResult result, string reason = "撃破")
    {
        _turn = turn;
        _result = result;
        _reason = reason;
    }

    public void Entry()
    {
        Debug.Log($"[GameEnd] ゲーム終了 結果: {_result} / 理由: {_reason}");
        ShowGameEndUI();
    }

    private void ShowGameEndUI()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject panel = new GameObject("GameEndPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas.transform, false);

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        GameObject textObj = new GameObject("GameEndText", typeof(RectTransform));
        textObj.transform.SetParent(panel.transform, false);

        var txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontSize = 48;
        txt.text = $"GAME END\n{_result}\n理由: {_reason}";

        var txtRt = textObj.GetComponent<RectTransform>();
        txtRt.anchorMin = new Vector2(0.1f, 0.2f);
        txtRt.anchorMax = new Vector2(0.9f, 0.8f);
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;
    }

    public void Update() { }
    public void Exit() { }
}
