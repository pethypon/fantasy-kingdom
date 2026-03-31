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

    public void Init(FactionState fs) => factionState = fs;

    [Header("設定")]
    [SerializeField] private Team displayTeam = Team.Player;

    [Header("右下配置")]
    [SerializeField] private float marginRight = 20f;
    [SerializeField] private float marginBottom = 20f;
    [SerializeField] private float panelWidth = 160f;
    [SerializeField] private float panelHeight = 80f;

    private int lastAP = -1;
    private int lastMax = -1;

    private void Awake()
    {
        // テキスト自動検索
        if (apText == null)
        {
            var tmp = GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) apText = tmp;
        }

        // RectTransform を右下にアンカー
        RectTransform rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(panelWidth, panelHeight);
            rt.anchoredPosition = new Vector2(-marginRight, marginBottom);
        }

        // APテキストを大きめに
        if (apText != null)
        {
            apText.fontSize = 30f;
            apText.alignment = TextAlignmentOptions.Center;
            apText.fontStyle = TMPro.FontStyles.Bold;
        }
    }

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
