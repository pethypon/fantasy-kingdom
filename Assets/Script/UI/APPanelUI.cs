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
    // 画面最下部の InputHintUI (高さ 32px) と被らないよう 40 に設定
    [SerializeField] private float marginBottom = 40f;
    [SerializeField] private float panelWidth = 160f;
    [SerializeField] private float panelHeight = 80f;

    private int lastAP = -1;
    private int lastMax = -1;

    private void Awake()
    {
        // テキスト自動検索（"APText" を名前で探す。先頭の TMP は "AP" ラベルのため、
        // 単純な GetComponentInChildren ではラベル側に数値が書き込まれ、
        // 本来の APText が初期値 "0 / 0" のまま残って二重表示になる）
        if (apText == null)
        {
            foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp.gameObject.name == "APText")
                {
                    apText = tmp;
                    break;
                }
            }
            if (apText == null)
                apText = GetComponentInChildren<TextMeshProUGUI>(true);
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
        if (factionState == null) return 0;
        var ap = displayTeam == Team.Player ? factionState.PlayerAP : factionState.EnemyAP;
        if (ap == null) return 0;
        return ap.Reset + ap.Plus - ap.Minus;
    }
}
