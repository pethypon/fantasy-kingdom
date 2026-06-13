using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 敵ターン中に画面中央に大きく「敵のターン」バナーを表示する。
/// フェードイン → 表示 → 自動縮小で画面上部へ移動。
/// </summary>
public class EnemyTurnBannerUI : MonoBehaviour
{
    public static EnemyTurnBannerUI Instance { get; private set; }
    public static bool IsShowing => Instance != null && Instance._phase != Phase.Hidden;

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _panelRect;
    private TextMeshProUGUI _text;
    private Image _bg;

    private enum Phase { Hidden, FadeIn, Hold, SlideUp }
    private Phase _phase = Phase.Hidden;
    private float _timer;

    private const float FadeInDuration = 0.3f;
    private const float HoldDuration = 1.0f;
    private const float SlideUpDuration = 0.4f;
    private const float BannerHeight = 80f;
    private const float TopY = 460f; // 画面上部寄りの位置

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void BuildUI()
    {
        var canvasGo = new GameObject("EnemyTurnBannerCanvas");
        canvasGo.transform.SetParent(transform);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // バナーパネル
        var panelGo = new GameObject("BannerPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        _panelRect = panelGo.AddComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(0, 0.5f);
        _panelRect.anchorMax = new Vector2(1, 0.5f);
        _panelRect.pivot = new Vector2(0.5f, 0.5f);
        _panelRect.anchoredPosition = Vector2.zero;
        _panelRect.sizeDelta = new Vector2(0, BannerHeight);

        _bg = panelGo.AddComponent<Image>();
        _bg.color = new Color(0.65f, 0.12f, 0.12f, 0.88f);

        _group = panelGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;

        // テキスト
        var textGo = new GameObject("BannerText");
        textGo.transform.SetParent(panelGo.transform, false);
        _text = textGo.AddComponent<TextMeshProUGUI>();
        _text.text = "敵のターン";
        _text.fontSize = BrandGuide.FontTitle;
        _text.color = BrandGuide.TextPrimary;
        _text.alignment = TextAlignmentOptions.Center;
        _text.fontStyle = FontStyles.Bold;

        var textRect = _text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }

    /// <summary>敵ターンバナーを表示する</summary>
    public static void Show()
    {
        if (Instance == null) return;
        Instance._phase = Phase.FadeIn;
        Instance._timer = 0f;
        Instance._panelRect.anchoredPosition = Vector2.zero;
        Instance._group.alpha = 0f;
    }

    /// <summary>即座に非表示にする</summary>
    public static void Hide()
    {
        if (Instance == null) return;
        Instance._phase = Phase.Hidden;
        Instance._group.alpha = 0f;
    }

    private void Update()
    {
        if (_group == null) return;

        switch (_phase)
        {
            case Phase.Hidden:
                return;

            case Phase.FadeIn:
                _timer += Time.deltaTime;
                _group.alpha = Mathf.Clamp01(_timer / FadeInDuration);
                if (_timer >= FadeInDuration)
                {
                    _group.alpha = 1f;
                    _phase = Phase.Hold;
                    _timer = 0f;
                }
                break;

            case Phase.Hold:
                _timer += Time.deltaTime;
                if (_timer >= HoldDuration)
                {
                    _phase = Phase.SlideUp;
                    _timer = 0f;
                }
                break;

            case Phase.SlideUp:
                _timer += Time.deltaTime;
                float t = Mathf.Clamp01(_timer / SlideUpDuration);
                float eased = t * t; // ease-in
                _panelRect.anchoredPosition = new Vector2(0, Mathf.Lerp(0, TopY, eased));
                _panelRect.sizeDelta = new Vector2(0, Mathf.Lerp(BannerHeight, 36f, eased));
                _group.alpha = Mathf.Lerp(1f, 0.6f, eased);
                if (t >= 1f)
                {
                    // 上部に小さく残留（敵ターン中ずっと表示）
                }
                break;
        }
    }
}
