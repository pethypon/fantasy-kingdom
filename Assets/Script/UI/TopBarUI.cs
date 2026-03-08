using TMPro;
using UnityEngine;

/// <summary>
/// InfoBar UI：ターン数を表示する
/// TurnText を名前で自動検出する
/// </summary>
public class TopBarUI : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TextMeshProUGUI turnText;

    [Header("参照")]
    [SerializeField] private TurnGenerater turnGenerater;

    private int lastTurn = -1;

    private void Awake()
    {
        if (turnGenerater == null)
            turnGenerater = Object.FindFirstObjectByType<TurnGenerater>();

        if (turnText == null)
        {
            // 名前で TurnText を検索
            foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp.gameObject.name == "TurnText")
                {
                    turnText = tmp;
                    break;
                }
            }
        }
    }

    private void Update()
    {
        UpdateTurn();
    }

    private void UpdateTurn()
    {
        if (turnGenerater == null || turnText == null) return;

        int current = turnGenerater.Turn;
        if (current == lastTurn) return;

        lastTurn = current;
        turnText.text = "Turn " + current;
    }
}
