using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ゲーム終了UIパネル: 勝敗結果を画面中央に表示する。
/// UIBuilder によりランタイム生成される。
/// </summary>
public class GameEndUI : MonoBehaviour
{
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI detailText;
    private TextMeshProUGUI hintText;
    private Image overlay;
    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // 初期状態は非表示
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        gameObject.SetActive(false);

        // 子テキストを名前で探す
        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            switch (tmp.gameObject.name)
            {
                case "ResultTitle": titleText = tmp; break;
                case "ResultDetail": detailText = tmp; break;
                case "ResultHint": hintText = tmp; break;
            }
        }

        // オーバーレイ背景を探す
        foreach (var img in GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject.name == "Overlay")
            {
                overlay = img;
                break;
            }
        }
    }

    public void Show(string title, string detail, GameResult result)
    {
        gameObject.SetActive(true);
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;

        if (titleText != null)
        {
            titleText.text = title;
            titleText.color = result == GameResult.Win
                ? new Color(1f, 0.85f, 0.2f)    // 金色
                : result == GameResult.Lose
                    ? new Color(0.8f, 0.2f, 0.2f) // 赤
                    : new Color(0.7f, 0.7f, 0.7f); // グレー
        }

        if (detailText != null)
            detailText.text = detail;

        if (hintText != null)
            hintText.text = "Enter / Shift でリトライ";
    }

    public void Hide()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }
}
