using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// InfoBar UI：ターン数と手動ターン終了。R2は対局の制限時間を持たない。
/// </summary>
public class TopBarUI : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TextMeshProUGUI turnText;

    // Historical prefab references; R2 hides the old clock rather than displaying an empty frame.
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Image timerFill;

    [Header("参照")]
    [FormerlySerializedAs("turnGenerater")]
    [SerializeField] private TurnGenerator turnGenerator;

    private Button endTurnButton;
    private int lastTurn = -1;

    private void Awake()
    {
        if (turnGenerator == null)
            turnGenerator = Object.FindFirstObjectByType<TurnGenerator>();

        if (turnText == null)
        {
            FindUIElements();
        }
        var legacyClock = transform.Find("RightArea/TimerArea");
        if (legacyClock != null) legacyClock.gameObject.SetActive(false);
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
        if (endTurnButton == null)
            endTurnButton = transform.Find("RightArea/EndTurnButton")?.GetComponent<Button>();
        if (endTurnButton != null)
            endTurnButton.interactable = CanEndTurn();
        UpdateTurn();
    }

    private bool CanEndTurn() => turnGenerator != null
        && (turnGenerator.CurrentState is PlayerMove || turnGenerator.CurrentState is PlayerAttack)
        && (GameMenuUI.Instance == null || !GameMenuUI.Instance.IsOpen);

    public void OnEndTurn()
    {
        if (CanEndTurn()) turnGenerator.Context.QueueUICommand(GameContext.UICommand.EndTurn);
    }

    private void UpdateTurn()
    {
        if (turnGenerator == null || turnText == null) return;

        int current = turnGenerator.Context.Turn;
        if (current == lastTurn) return;

        lastTurn = current;
        turnText.text = "Turn " + current;
    }

}
