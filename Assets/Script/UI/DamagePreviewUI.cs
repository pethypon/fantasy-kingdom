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

    // ---- 定数 ----
    private const float PanelWidth = 200f;
    private const float PanelHeight = 76f;
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

        // パネルボーダー
        BrandGuide.AddPanelBorder(_panelRect);

        // ダメージテキスト
        var dmgGo = new GameObject("DmgText");
        dmgGo.transform.SetParent(panelGo.transform, false);
        _damageText = dmgGo.AddComponent<TextMeshProUGUI>();
        _damageText.fontSize = 22;
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
        _hpText.fontSize = 16;
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
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit hit, GameConstants.DefaultRayDistance))
        {
            Status target = hit.transform.GetComponent<Status>();
            if (target != null && target.team == Team.Enemy && target != _lastHovered)
            {
                _lastHovered = target;
                ShowPreview(turnGenerator.Context.SelectUnit, target, mousePos);
                return;
            }
            else if (target != null && target.team == Team.Enemy && target == _lastHovered)
            {
                // 同じターゲット → 位置だけ更新
                UpdatePosition(mousePos);
                return;
            }
        }

        // ターゲットなし → 非表示
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
            ? $"DMG: {normalDmg} <color=#FF3333>KILL!</color>"
            : $"DMG: {normalDmg}";
        _damageText.color = isKill ? KillColor : NormalColor;

        _hpText.text = $"HP: {target.HP} → {hpAfter}";

        // シールド中の表示
        if (target.ShieldTurns > 0)
        {
            _damageText.text = "SHIELD ACTIVE";
            _damageText.color = new Color(0.5f, 0.8f, 1f);
            _hpText.text = $"残り {target.ShieldTurns} ターン";
        }

        UpdatePosition(mousePos);
        _panelRect.gameObject.SetActive(true);
    }

    private void UpdatePosition(Vector2 mousePos)
    {
        // マウスの右上にパネルを表示（画面外に出ないよう調整）
        Vector2 offset = new Vector2(20, -20);
        Vector2 pos = mousePos + offset;

        // 画面右端チェック
        if (pos.x + PanelWidth > Screen.width)
            pos.x = mousePos.x - PanelWidth - 10;
        // 画面上端チェック
        if (pos.y > Screen.height)
            pos.y = Screen.height - 10;

        _panelRect.position = pos;
    }
}
