using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TopBar UI：ターン数と制限時間を表示する。
/// TurnText / TimerText / TimerFill を名前で自動検出する。
/// </summary>
public class TopBarUI : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Image timerFill;

    [Header("参照")]
    [SerializeField] private TurnGenerater turnGenerater;

    private int lastTurn = -1;

    private void Awake()
    {
        if (turnGenerater == null)
            turnGenerater = Object.FindFirstObjectByType<TurnGenerater>();

        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (turnText == null && tmp.gameObject.name == "TurnText") turnText = tmp;
            if (timerText == null && tmp.gameObject.name == "TimerText") timerText = tmp;
        }

        if (timerFill == null)
        {
            var fillObj = transform.Find("TopBar/RightArea/TimerArea/TimerFill");
            if (fillObj != null) timerFill = fillObj.GetComponent<Image>();
            if (timerFill == null)
            {
                foreach (var img in GetComponentsInChildren<Image>(true))
                {
                    if (img.gameObject.name == "TimerFill")
                    {
                        timerFill = img;
                        break;
                    }
                }
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
        if (turnGenerater == null || turnGenerater.timeLimitSystem == null) return;

        float turnRemain = turnGenerater.timeLimitSystem.TurnRemaining;
        float teamRemain = turnGenerater.timeLimitSystem.TeamRemaining(turnGenerater.CurrentTeam);

        if (timerText != null)
            timerText.text = $"T:{FormatTime(turnRemain)}  持:{FormatTime(teamRemain)}";

        if (timerFill != null)
        {
            float ratio = Mathf.Clamp01(turnRemain / Mathf.Max(1f, turnGenerater.timeLimitSystem.TurnLimitSeconds));
            timerFill.fillAmount = ratio;
            timerFill.type = Image.Type.Filled;
            timerFill.fillMethod = Image.FillMethod.Horizontal;
            timerFill.fillOrigin = 0;
        }
    }

    private static string FormatTime(float sec)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(sec));
        int m = s / 60;
        int r = s % 60;
        return $"{m:00}:{r:00}";
    }
}
