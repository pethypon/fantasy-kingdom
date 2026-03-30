using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// InfoBar UI：ターン数 + タイマー情報を表示する
/// </summary>
public class TopBarUI : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TextMeshProUGUI turnText;

    [Header("タイマーUI")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Image timerFill;

    [Header("参照")]
    [SerializeField] private TurnGenerater turnGenerater;

    private int lastTurn = -1;

    private void Awake()
    {
        if (turnGenerater == null)
            turnGenerater = Object.FindFirstObjectByType<TurnGenerater>();

        if (turnText == null || timerText == null || timerFill == null)
        {
            FindUIElements();
        }
    }

    private void FindUIElements()
    {
        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (tmp.gameObject.name == "TurnText")
                turnText = tmp;
            if (tmp.gameObject.name == "TimerText")
                timerText = tmp;
        }

        foreach (var img in GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject.name == "TimerFill")
                timerFill = img;
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

        var timer = turnGenerater.timerSystem;
        if (timer == null)
        {
            if (timerText != null) timerText.text = "制限時間";
            return;
        }

        // タイマーテキスト: 1ターン残り時間のみ表示
        if (timerText != null)
        {
            string turnTime = TimerSystem.FormatTime(timer.TurnTimeRemaining);
            timerText.text = turnTime;
        }

        // タイマーバー: 1ターン制限時間の残り割合
        if (timerFill != null)
        {
            float ratio = timer.TurnTimeLimit > 0f
                ? Mathf.Clamp01(timer.TurnTimeRemaining / timer.TurnTimeLimit)
                : 0f;
            timerFill.fillAmount = ratio;

            // 残り時間に応じて色を変更（BrandGuide参照）
            if (ratio < 0.2f)
                timerFill.color = BrandGuide.TimerDanger;
            else if (ratio < 0.5f)
                timerFill.color = BrandGuide.TimerWarning;
            else
                timerFill.color = BrandGuide.TimerNormal;

            // fillAmount を反映するために Image.type を Filled に設定
            if (timerFill.type != Image.Type.Filled)
            {
                timerFill.type = Image.Type.Filled;
                timerFill.fillMethod = Image.FillMethod.Horizontal;
                timerFill.fillOrigin = 0;
            }
        }
    }
}
