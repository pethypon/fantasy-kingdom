using TMPro;
using UnityEngine;

/// <summary>
/// 上バーUI：ターン数・メニューボタンを常時表示する
/// AP表示はAPPanelUIに移動済み
/// Canvas > SafeAreaRoot > TopBar に付ける
/// </summary>
public class TopBarUI : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TextMeshProUGUI turnText;

    [Header("参照")]
    [SerializeField] private TurnGenerater turnGenerater;

    private int lastTurn = -1;

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
