using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 画面下部にキーバインドのヒントを表示するUI。
/// 現在のゲームステートに応じて表示内容が変化する。
/// </summary>
public class InputHintUI : MonoBehaviour
{
    private Canvas _canvas;
    private RectTransform _panelRect;
    private TextMeshProUGUI _hintText;
    private CanvasGroup _group;

    private string _currentHints = "";
    private float _fadeTarget = 1f;
    private const float FadeSpeed = 4f;

    // ---- ステート別ヒント定義 ----
    public static class Hints
    {
        public const string PlayerMove =
            "<color=#AAD4FF>[LClick]</color> ユニット選択  " +
            "<color=#AAD4FF>[RClick]</color> キャンセル  " +
            "<color=#AAD4FF>[1]</color> 通常攻撃  " +
            "<color=#AAD4FF>[2]</color> スキル  " +
            "<color=#AAD4FF>[Q/E]</color> 向き反転  " +
            "<color=#AAD4FF>[Enter/Shift]</color> ターン終了  " +
            "<color=#AAD4FF>[C]</color> フォーカス  " +
            "<color=#AAD4FF>[Tab]</color> 次のユニット";

        public const string PlayerAttack =
            "<color=#FFD4AA>[LClick]</color> 攻撃対象選択  " +
            "<color=#FFD4AA>[RClick]</color> キャンセル  " +
            "<color=#FFD4AA>[1]</color> 通常攻撃  " +
            "<color=#FFD4AA>[2]</color> スキル";

        public const string EnemyTurn =
            "<color=#888888>敵のターン...</color>";

        public const string BuildMode =
            "<color=#AAFFAA>[LClick]</color> 設置  " +
            "<color=#AAFFAA>[RClick]</color> キャンセル  " +
            "<color=#AAFFAA>[Enter/Shift]</color> ターン終了";

        public const string SummonMode =
            "<color=#DDAAFF>[LClick]</color> 召喚  " +
            "<color=#DDAAFF>[RClick]</color> キャンセル  " +
            "<color=#DDAAFF>[Enter/Shift]</color> ターン終了";

        public const string GameEnd =
            "";
    }

    private void Awake()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        // Canvas
        var canvasGo = new GameObject("InputHintCanvas");
        canvasGo.transform.SetParent(transform);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 150;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel（画面下部中央）
        var panelGo = new GameObject("HintPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        _panelRect = panelGo.AddComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(0.1f, 0);
        _panelRect.anchorMax = new Vector2(0.9f, 0);
        _panelRect.pivot = new Vector2(0.5f, 0);
        _panelRect.anchoredPosition = new Vector2(0, 8);
        _panelRect.sizeDelta = new Vector2(0, 34);

        var bg = panelGo.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.04f, 0.06f, 0.75f);

        _group = panelGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        // テキスト
        var textGo = new GameObject("HintText");
        textGo.transform.SetParent(panelGo.transform, false);
        _hintText = textGo.AddComponent<TextMeshProUGUI>();
        _hintText.fontSize = 14;
        _hintText.color = Color.white;
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.richText = true;

        var textRect = _hintText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(16, 2);
        textRect.offsetMax = new Vector2(-16, -2);
    }

    /// <summary>ヒントテキストを更新する</summary>
    public void SetHints(string hints)
    {
        if (hints == _currentHints) return;
        _currentHints = hints;

        if (string.IsNullOrEmpty(hints))
        {
            _fadeTarget = 0f;
        }
        else
        {
            _hintText.text = hints;
            _fadeTarget = 1f;
        }
    }

    /// <summary>非表示にする</summary>
    public void Hide()
    {
        _fadeTarget = 0f;
    }

    private void Update()
    {
        if (_group == null) return;

        // スムーズフェード
        if (!Mathf.Approximately(_group.alpha, _fadeTarget))
        {
            _group.alpha = Mathf.MoveTowards(_group.alpha, _fadeTarget, FadeSpeed * Time.deltaTime);
        }
    }
}
