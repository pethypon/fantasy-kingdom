using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TopBarUI : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private TextMeshProUGUI timerText;

    [Header("参照")]
    [SerializeField] private TurnGenerater turnGenerater;

    [Header("制限時間バー")]
    [SerializeField] private Image timerFill;

    private int lastTurn = -1;

    private void Awake()
    {
        if (turnGenerater == null)
            turnGenerater = Object.FindFirstObjectByType<TurnGenerater>();

        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (turnText == null && tmp.gameObject.name == "TurnText")
                turnText = tmp;
            if (timerText == null && tmp.gameObject.name == "TimerText")
                timerText = tmp;
        }

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
        if (turnGenerater?.gameTimerSystem == null) return;

        var timer = turnGenerater.gameTimerSystem;
        if (timerText != null)
        {
            int turnSec = Mathf.CeilToInt(timer.TurnRemainSeconds);
            int teamSec = Mathf.CeilToInt(timer.ActiveTeam == Team.Player ? timer.PlayerRemainSeconds : timer.EnemyRemainSeconds);
            timerText.text = $"{timer.ActiveTeam} T:{ToClock(turnSec)} / All:{ToClock(teamSec)}";
        }

        if (timerFill != null)
        {
            float maxTurn = 3f * 60f;
            timerFill.fillAmount = Mathf.Clamp01(timer.TurnRemainSeconds / maxTurn);
        }
    }

    private string ToClock(int seconds)
    {
        int m = seconds / 60;
        int s = seconds % 60;
        return $"{m:00}:{s:00}";
    }
}
