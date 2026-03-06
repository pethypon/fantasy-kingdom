using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 右下AP表示UI：円形背景 + 数字
/// Canvas > SafeAreaRoot > APPanel に付ける
///
/// 構成:
///   APPanel
///     ├─ BackCircle  (Image: 丸背景)
///     ├─ FillCircle  (Image: Filled で残量表示)
///     └─ APText      (TextMeshProUGUI: "12 / 15")
/// </summary>
public class APPanelUI : MonoBehaviour
{
    [Header("テキスト")]
    [SerializeField] private TextMeshProUGUI apText;

    [Header("ゲージ (任意)")]
    [SerializeField] private Image fillImage;

    [Header("参照")]
    [SerializeField] private FactionState factionState;

    [Header("設定")]
    [SerializeField] private Team displayTeam = Team.Player;

    private int lastAP = -1;
    private int lastMax = -1;

    private void Update()
    {
        if (factionState == null) return;

        int current = factionState.GetAP(displayTeam);
        int max = GetMaxAP();

        if (current == lastAP && max == lastMax) return;

        lastAP = current;
        lastMax = max;

        if (apText != null)
            apText.text = current + " / " + max;

        if (fillImage != null && max > 0)
            fillImage.fillAmount = (float)current / max;
    }

    private int GetMaxAP()
    {
        if (displayTeam == Team.Player)
            return factionState.PlayerAP.Reset
                 + factionState.PlayerAP.Plus
                 - factionState.PlayerAP.Minus;
        else
            return factionState.EnemyAP.Reset
                 + factionState.EnemyAP.Plus
                 - factionState.EnemyAP.Minus;
    }
}
