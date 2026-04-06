using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 画面左上にターン数・AP残量・現在フェーズを常時表示するUI。
/// </summary>
public class TurnAPIndicatorUI : MonoBehaviour
{
    public static TurnAPIndicatorUI Instance { get; private set; }

    private TextMeshProUGUI _turnText;
    private TextMeshProUGUI _apText;
    private TextMeshProUGUI _phaseText;
    private Image _phaseBg;

    private TurnGenerator _turn;
    private APSystem _ap;

    private int _lastTurn = -1;
    private int _lastAP = -1;
    private string _lastPhase = "";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    public void Init(TurnGenerator turn, APSystem ap)
    {
        _turn = turn;
        _ap = ap;
    }

    private void BuildUI()
    {
        var canvasGo = new GameObject("TurnAPCanvas");
        canvasGo.transform.SetParent(transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // パネル（左上）
        var panelGo = new GameObject("TurnAPPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelRT = panelGo.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0, 1);
        panelRT.anchorMax = new Vector2(0, 1);
        panelRT.pivot = new Vector2(0, 1);
        panelRT.anchoredPosition = new Vector2(12, -12);
        panelRT.sizeDelta = new Vector2(200, 90);

        var bg = panelGo.AddComponent<Image>();
        bg.color = BrandGuide.PanelBg;

        // フェーズ表示（上部バー）
        var phaseBarGo = new GameObject("PhaseBar");
        phaseBarGo.transform.SetParent(panelGo.transform, false);
        var phaseBarRT = phaseBarGo.AddComponent<RectTransform>();
        phaseBarRT.anchorMin = new Vector2(0, 0.7f);
        phaseBarRT.anchorMax = new Vector2(1, 1);
        phaseBarRT.offsetMin = Vector2.zero;
        phaseBarRT.offsetMax = Vector2.zero;

        _phaseBg = phaseBarGo.AddComponent<Image>();
        _phaseBg.color = BrandGuide.Secondary;

        var phaseTextGo = new GameObject("PhaseText");
        phaseTextGo.transform.SetParent(phaseBarGo.transform, false);
        _phaseText = phaseTextGo.AddComponent<TextMeshProUGUI>();
        _phaseText.fontSize = 16;
        _phaseText.fontStyle = FontStyles.Bold;
        _phaseText.color = Color.white;
        _phaseText.alignment = TextAlignmentOptions.Center;
        var ptRect = _phaseText.GetComponent<RectTransform>();
        ptRect.anchorMin = Vector2.zero;
        ptRect.anchorMax = Vector2.one;
        ptRect.offsetMin = Vector2.zero;
        ptRect.offsetMax = Vector2.zero;

        // ターン数
        var turnGo = new GameObject("TurnText");
        turnGo.transform.SetParent(panelGo.transform, false);
        _turnText = turnGo.AddComponent<TextMeshProUGUI>();
        _turnText.fontSize = 18;
        _turnText.color = BrandGuide.TextLabel;
        _turnText.alignment = TextAlignmentOptions.Left;
        var turnRT = _turnText.GetComponent<RectTransform>();
        turnRT.anchorMin = new Vector2(0, 0.3f);
        turnRT.anchorMax = new Vector2(0.5f, 0.7f);
        turnRT.offsetMin = new Vector2(10, 0);
        turnRT.offsetMax = Vector2.zero;

        // AP表示
        var apGo = new GameObject("APText");
        apGo.transform.SetParent(panelGo.transform, false);
        _apText = apGo.AddComponent<TextMeshProUGUI>();
        _apText.fontSize = 18;
        _apText.color = BrandGuide.Secondary;
        _apText.alignment = TextAlignmentOptions.Right;
        var apRT = _apText.GetComponent<RectTransform>();
        apRT.anchorMin = new Vector2(0.5f, 0.3f);
        apRT.anchorMax = new Vector2(1, 0.7f);
        apRT.offsetMin = Vector2.zero;
        apRT.offsetMax = new Vector2(-10, 0);
    }

    private void Update()
    {
        if (_turn == null) return;

        // ターン数
        if (_turn.Context.Turn != _lastTurn)
        {
            _lastTurn = _turn.Context.Turn;
            _turnText.text = $"Turn {_lastTurn}";
        }

        // AP
        if (_ap != null)
        {
            int ap = _ap.GetAP(Team.Player);
            if (ap != _lastAP)
            {
                _lastAP = ap;
                _apText.text = $"AP: {ap}";
                _apText.color = ap <= 2
                    ? BrandGuide.Danger
                    : BrandGuide.Secondary;
            }
        }

        // フェーズ
        string phase = GetCurrentPhase();
        if (phase != _lastPhase)
        {
            _lastPhase = phase;
            _phaseText.text = phase;
            UpdatePhaseColor(phase);
        }
    }

    private string GetCurrentPhase()
    {
        if (_turn.Context.SelectUnit != null)
            return "Player - 行動中";
        // Check simple approach: if Turn is updating and no enemy banner
        if (EnemyTurnBannerUI.Instance != null && EnemyTurnBannerUI.IsShowing)
            return "Enemy Turn";
        return "Player Turn";
    }

    private void UpdatePhaseColor(string phase)
    {
        if (phase.Contains("Enemy"))
            _phaseBg.color = BrandGuide.Danger;
        else
            _phaseBg.color = BrandGuide.Secondary;
    }
}
