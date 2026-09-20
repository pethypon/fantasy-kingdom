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
    private float _nextProbeTime;
    private Matrix4x4 _lastCameraMatrix;
    private (Kind, Team, Direction, int, int, int, int) _displayedValues;

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

        var camera = Camera.main;
        bool probe = mousePos != _lastMousePos || camera.worldToCameraMatrix != _lastCameraMatrix
            || Time.unscaledTime >= _nextProbeTime;
        _lastMousePos = mousePos;
        _lastCameraMatrix = camera.worldToCameraMatrix;
        if (!probe)
        {
            UpdatePosition(mousePos);
            _group.alpha = Mathf.MoveTowards(_group.alpha, _lastHovered != null ? 1f : 0f, 8f * Time.unscaledDeltaTime);
            return;
        }
        _nextProbeTime = Time.unscaledTime + 0.1f;
        if (UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            _lastHovered = null;
            _group.alpha = 0f;
            return;
        }

        Ray ray = camera.ScreenPointToRay(mousePos);
        if (Physics.Raycast(ray, out RaycastHit hit, GameConstants.DefaultRayDistance))
        {
            Status s = hit.transform.GetComponentInParent<Status>();
            if (s != null && s.IsAlive && (s.type == Type.Unit || s.type == Type.Building))
            {
                var values = (s.kind, s.team, s.direction, s.HP, s.MaxHP, s.ATK, s.DEF);
                if (s != _lastHovered || !values.Equals(_displayedValues))
                {
                    _displayedValues = values;
                    UpdateTooltip(s);
                }
                _lastHovered = s;
                UpdatePosition(mousePos);
                _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, 8f * Time.deltaTime);
                return;
            }
        }

        // ホバー解除
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
        string teamStr = s.team switch
        {
            Team.Player => "味方", Team.Enemy => "異形の軍勢", Team.Monster => "魔物",
            Team.Intruder => "乱入者", Team.Obstacle => "強敵", _ => "中立"
        };
        string name = (s.type == Type.Building || s.type == Type.Wall)
            && FacilityData.Table.TryGetValue(s.facilityKind, out var facility)
            ? facility.DisplayName : KindNameJP.Get(s.kind);
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
