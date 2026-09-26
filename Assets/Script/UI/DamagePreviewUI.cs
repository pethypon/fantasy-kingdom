using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 攻撃ターゲットにマウスホバーした時にダメージ予測を表示するUI。
/// PlayerAttack ステート中に有効化される。
/// </summary>
public class DamagePreviewUI : MonoBehaviour
{
    [Header("参照")]
    public TurnGenerator turnGenerator;

    // ---- ランタイム生成UI ----
    private Canvas _canvas;
    private RectTransform _panelRect;
    private TextMeshProUGUI _damageText;
    private TextMeshProUGUI _hpText;
    private Image _bgImage;

    private bool _isActive;
    private Status _lastHovered;
    private float nextRefresh;

    // ---- 定数 ----
    private const float PanelWidth = 320f;
    private const float PanelHeight = 102f;
    private static readonly Color BgColor = new Color(0.05f, 0.05f, 0.08f, 0.94f);
    private static readonly Color KillColor = new Color(1f, 0.25f, 0.22f);
    private static readonly Color NormalColor = new Color(1f, 0.92f, 0.55f);
    private static readonly Color HpColor = new Color(0.65f, 0.88f, 1f);

    private void Awake()
    {
        BuildUI();
        Hide();
    }

    private void BuildUI()
    {
        // ScreenSpace Overlay Canvas
        var canvasGo = new GameObject("DamagePreviewCanvas");
        canvasGo.transform.SetParent(transform);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Panel
        var panelGo = new GameObject("DmgPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        _panelRect = panelGo.AddComponent<RectTransform>();
        _panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        _panelRect.pivot = new Vector2(0, 1); // 左上基点

        _bgImage = panelGo.AddComponent<Image>();
        _bgImage.color = BgColor;
        _bgImage.raycastTarget = false;

        // パネルボーダー
        BrandGuide.AddPanelBorder(_panelRect);

        // ダメージテキスト
        var dmgGo = new GameObject("DmgText");
        dmgGo.transform.SetParent(panelGo.transform, false);
        _damageText = dmgGo.AddComponent<TextMeshProUGUI>();
        _damageText.fontSize = 28;
        _damageText.font = UIFactory.LoadDefaultFont();
        _damageText.raycastTarget = false;
        _damageText.fontStyle = FontStyles.Bold;
        _damageText.alignment = TextAlignmentOptions.Left;
        _damageText.color = NormalColor;
        var dmgRect = _damageText.GetComponent<RectTransform>();
        dmgRect.anchorMin = new Vector2(0, 0.5f);
        dmgRect.anchorMax = new Vector2(1, 1);
        dmgRect.offsetMin = new Vector2(10, 0);
        dmgRect.offsetMax = new Vector2(-10, -5);

        // HP残量テキスト
        var hpGo = new GameObject("HpText");
        hpGo.transform.SetParent(panelGo.transform, false);
        _hpText = hpGo.AddComponent<TextMeshProUGUI>();
        _hpText.fontSize = 24;
        _hpText.font = _damageText.font;
        _hpText.raycastTarget = false;
        _hpText.alignment = TextAlignmentOptions.Left;
        _hpText.color = HpColor;
        var hpRect = _hpText.GetComponent<RectTransform>();
        hpRect.anchorMin = new Vector2(0, 0);
        hpRect.anchorMax = new Vector2(1, 0.5f);
        hpRect.offsetMin = new Vector2(10, 5);
        hpRect.offsetMax = new Vector2(-10, 0);
    }

    /// <summary>攻撃モードに入った時に呼ぶ</summary>
    public void Activate()
    {
        _isActive = true;
        _lastHovered = null;
    }

    /// <summary>攻撃モードを抜けた時に呼ぶ</summary>
    public void Hide()
    {
        _isActive = false;
        _lastHovered = null;
        if (_panelRect != null)
            _panelRect.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!_isActive) return;
        if (turnGenerator == null || turnGenerator.Context.SelectUnit == null) { Hide(); return; }
        var camera = Camera.main;
        if (Mouse.current == null || camera == null
            || !(turnGenerator.CurrentState is PlayerAttack attack) || attack.attackmode != PlayerMove.AttackMode.Normal
            || (GameMenuUI.Instance != null && GameMenuUI.Instance.IsOpen)
            || (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
        { ClearPreview(); return; }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = camera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, GameConstants.DefaultRayDistance))
        {
            Status target = hit.transform.GetComponentInParent<Status>();
            if (turnGenerator.Systems.UnitClick != null && turnGenerator.Systems.UnitClick.CanTargetNormalAttack(target))
            {
                if (target != _lastHovered || Time.unscaledTime >= nextRefresh)
                {
                    _lastHovered = target;
                    nextRefresh = Time.unscaledTime + 0.15f;
                    ShowPreview(turnGenerator.Context.SelectUnit, target, mousePos);
                }
                else UpdatePosition(mousePos);
                return;
            }
        }

        ClearPreview();
    }

    private void ClearPreview()
    {
        if (_lastHovered != null)
        {
            _lastHovered = null;
            _panelRect.gameObject.SetActive(false);
        }
    }

    private void ShowPreview(Status attacker, Status target, Vector2 mousePos)
    {
        int normalDmg = DamageCalculator.CalcNormal(attacker, target);
        int hpAfter = Mathf.Max(0, target.HP - normalDmg);
        bool isKill = hpAfter <= 0;

        _damageText.text = isKill
            ? $"予測 {normalDmg}  <color=#FF7065>撃破</color>"
            : $"予測ダメージ  {normalDmg}";
        _damageText.color = isKill ? KillColor : NormalColor;

        _hpText.text = $"HP: {target.HP} → {hpAfter}";

        // シールド中の表示
        if (target.ShieldTurns > 0)
        {
            _damageText.text = "シールドで防御";
            _damageText.color = new Color(0.5f, 0.8f, 1f);
            _hpText.text = $"残り {target.ShieldTurns} ターン";
        }

        UpdatePosition(mousePos);
        _panelRect.gameObject.SetActive(true);
    }

    private void UpdatePosition(Vector2 mousePos)
    {
        // マウスの右上にパネルを表示（画面外に出ないよう調整）
        var canvasRect = (RectTransform)_canvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mousePos, null, out var local);
        var bounds = canvasRect.rect;
        float x = local.x + 24;
        if (x + PanelWidth > bounds.xMax) x = local.x - PanelWidth - 24;
        _panelRect.anchoredPosition = new Vector2(
            Mathf.Clamp(x, bounds.xMin, bounds.xMax - PanelWidth),
            Mathf.Clamp(local.y + PanelHeight + 24, bounds.yMin + PanelHeight, bounds.yMax));
    }
}
