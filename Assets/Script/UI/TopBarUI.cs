using TMPro;
using UnityEngine;

/// <summary>
/// 上バーUI：ターン数・AP・メニューボタンを常時表示する
/// Canvas > SafeAreaRoot > TopBar に付ける
/// </summary>
public class TopBarUI : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private TextMeshProUGUI apText;

    [Header("参照")]
    [SerializeField] private TurnGenerater turnGenerater;
    [SerializeField] private APSystem apSystem;

    private int lastTurn = -1;
    private int lastAP = -1;

    private void Update()
    {
        UpdateTurn();
        UpdateAP();
    }

    private void UpdateTurn()
    {
        if (turnGenerater == null || turnText == null) return;

        int current = turnGenerater.Turn;
        if (current == lastTurn) return;

        lastTurn = current;
        turnText.text = "Turn " + current;
    }

    private void UpdateAP()
    {
        if (apSystem == null || apText == null) return;

        int current = apSystem.GetAP(Team.Player);
        if (current == lastAP) return;

        lastAP = current;
        apText.text = "AP " + current;
    }
}
