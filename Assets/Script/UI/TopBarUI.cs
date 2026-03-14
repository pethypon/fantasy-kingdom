using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// InfoBar UI：ターン数 + タイマー表示
/// </summary>
public class TopBarUI : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private TextMeshProUGUI timerText;

    [Header("タイマーバー")]
    [SerializeField] private RectTransform timerFillRT;
    [SerializeField] private Image timerFillImage;

    [Header("参照")]
    [SerializeField] private TurnGenerater turnGenerater;

    private int lastTurn = -1;
    private TimeLimitSystem timeLimitSystem;

    private void Awake()
    {
        if (turnGenerater == null)
            turnGenerater = Object.FindFirstObjectByType<TurnGenerater>();

        // 名前でテキスト要素を検索
        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            switch (tmp.gameObject.name)
            {
                case "TurnText": turnText = tmp; break;
                case "TimerText": timerText = tmp; break;
            }
        }

        // TimerFill を検索
        foreach (var img in GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject.name == "TimerFill")
            {
                timerFillRT = img.GetComponent<RectTransform>();
                timerFillImage = img;
                break;
            }
        }
    }

    private void Update()
    {
        UpdateTurn();
        UpdateTimer();
    }

    private void UpdateTurn()
    {
        if (turnGenerater == null || turnText == null) return;

        int current = turnGenerater.Turn;
        if (current == lastTurn) return;

        lastTurn = current;
        turnText.text = "Turn " + current;
    }

    private void UpdateTimer()
    {
        if (turnGenerater == null) return;

        // 遅延取得
        if (timeLimitSystem == null)
            timeLimitSystem = turnGenerater.timeLimitSystem;

        if (timeLimitSystem == null) return;

        // タイマーテキスト更新
        if (timerText != null)
        {
            string turnTime = timeLimitSystem.GetTurnTimeText();
            string teamLabel = timeLimitSystem.CurrentTeam == Team.Player ? "P" : "E";
            string totalTime = timeLimitSystem.CurrentTeam == Team.Player
                ? timeLimitSystem.GetPlayerTotalTimeText()
                : timeLimitSystem.GetEnemyTotalTimeText();

            timerText.text = $"{turnTime}  [{teamLabel} {totalTime}]";
        }

        // タイマーバー更新（ターン時間の残り比率）
        if (timerFillRT != null)
        {
            float ratio = timeLimitSystem.GetTurnTimeRatio();
            timerFillRT.anchorMax = new Vector2(ratio, 1f);

            // 色変更：残り30秒以下で赤
            if (timerFillImage != null)
            {
                if (timeLimitSystem.TurnTimeRemaining <= 30f)
                    timerFillImage.color = new Color(0.85f, 0.2f, 0.2f, 0.9f);
                else if (timeLimitSystem.TurnTimeRemaining <= 60f)
                    timerFillImage.color = new Color(0.85f, 0.6f, 0.15f, 0.9f);
                else
                    timerFillImage.color = new Color(0.2f, 0.55f, 0.8f, 0.9f);
            }
        }
    }
}
