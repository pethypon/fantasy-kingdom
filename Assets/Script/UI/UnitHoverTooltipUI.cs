using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// マウスホバーでユニットの簡易情報をツールチップ表示する。
/// PlayerMove中に常時動作する軽量UI。
/// </summary>
public class UnitHoverTooltipUI : MonoBehaviour
{
    public static UnitHoverTooltipUI Instance { get; private set; }

    private RectTransform _panelRect;
    private TextMeshProUGUI _text;
    private CanvasGroup _group;
    private Status _lastHovered;
    private Vector2 _lastMousePos = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
    private bool _lastFrameHadHit;

    private const float PanelWidth = 270f;
    private const float PanelHeight = 62f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void BuildUI()
    {
        var canvasGo = new GameObject("HoverTooltipCanvas");
        canvasGo.transform.SetParent(transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 180;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var panelGo = new GameObject("TooltipPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        _panelRect = panelGo.AddComponent<RectTransform>();
        _panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        _panelRect.pivot = new Vector2(0, 1);

        var bg = panelGo.AddComponent<Image>();
        bg.color = BrandGuide.PanelBg;

        _group = panelGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;

        var textGo = new GameObject("TooltipText");
        textGo.transform.SetParent(panelGo.transform, false);
        _text = textGo.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 16;
        _text.color = BrandGuide.TextPrimary;
        _text.alignment = TextAlignmentOptions.Left;
        _text.richText = true;

        var textRT = _text.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(8, 4);
        textRT.offsetMax = new Vector2(-8, -4);
    }

    private void Update()
    {
        if (Mouse.current == null || Camera.main == null)
        {
            _group.alpha = 0f;
            return;
        }

        // 敵ターン中は非表示
        if (EnemyTurnBannerUI.IsShowing)
        {
            _group.alpha = 0f;
            _lastHovered = null;
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();

        // パフォーマンス最適化: マウスが動いていなければ Raycast をスキップし、
        // 前回の判定結果を使い回す（毎フレーム Physics.Raycast を抑制）。
        bool mouseMoved = mousePos != _lastMousePos;
        _lastMousePos = mousePos;

        if (!mouseMoved && _lastHovered != null && _lastHovered.gameObject.activeInHierarchy)
        {
            // 既存ホバー状態を維持（位置・透明度のみ更新）
            UpdatePosition(mousePos);
            _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, 8f * Time.deltaTime);
            return;
        }
        if (!mouseMoved && !_lastFrameHadHit)
        {
            // 前回ヒットなしのまま静止 → フェードアウトのみ
            _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, 8f * Time.deltaTime);
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(mousePos);
        if (Physics.Raycast(ray, out RaycastHit hit, GameConstants.DefaultRayDistance))
        {
            Status s = hit.transform.GetComponent<Status>();
            if (s != null && (s.type == Type.Unit || s.type == Type.Building))
            {
                if (s != _lastHovered)
                {
                    _lastHovered = s;
                    UpdateTooltip(s);
                }
                _lastFrameHadHit = true;
                UpdatePosition(mousePos);
                _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, 8f * Time.deltaTime);
                return;
            }
        }

        // ホバー解除
        _lastFrameHadHit = false;
        if (_lastHovered != null)
        {
            _lastHovered = null;
        }
        _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, 8f * Time.deltaTime);
    }

    private void UpdateTooltip(Status s)
    {
        string teamColor = s.team == Team.Player
            ? "#" + ColorUtility.ToHtmlStringRGB(BrandGuide.TeamPlayer)
            : "#" + ColorUtility.ToHtmlStringRGB(BrandGuide.TeamEnemy);
        string teamStr = s.team == Team.Player ? "味方" : "敵";
        string name = KindNameJP.Get(s.kind);
        string dir = s.direction == Direction.N ? "▲N" : "▼S";

        float hpRatio = s.MaxHP > 0 ? (float)s.HP / s.MaxHP : 1f;
        string hpColor = BrandGuide.HPColorHex(hpRatio);

        _text.text = $"<color={teamColor}>[{teamStr}]</color> {name} {dir}\n" +
                     $"HP:<color={hpColor}>{s.HP}/{s.MaxHP}</color>  ATK:{s.ATK}  DEF:{s.DEF}";
    }

    private void UpdatePosition(Vector2 mousePos)
    {
        Vector2 pos = mousePos + new Vector2(16, -16);
        if (pos.x + PanelWidth > Screen.width) pos.x = mousePos.x - PanelWidth - 8;
        if (pos.y < PanelHeight) pos.y = mousePos.y + 16;
        _panelRect.position = pos;
    }
}
