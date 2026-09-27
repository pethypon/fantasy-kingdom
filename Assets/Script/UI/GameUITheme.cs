using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared visual theme. Sprite references and colors are editable in the theme asset.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/Game UI Theme")]
public sealed class GameUITheme : ScriptableObject
{
    public static Color ButtonInk => Current != null ? Current.textTint : new Color(0.98f, 0.90f, 0.73f);
    [Header("Colors / 色")]
    public Color textTint = new Color(0.98f, 0.90f, 0.73f);
    public Color buttonTint = Color.white;
    public Color panelTint = new Color(0.48f, 0.52f, 0.60f, 1);
    public Color headingTint = new Color(0.75f, 0.82f, 0.92f, 1);
    public Color frameTint = Color.white;
    [Header("Border scale / 枠の縮尺")]
    [Min(0.1f)] public float borderScale = 3;
    [Header("Sprites / 画像")]
    public Sprite levelBadge, healthFrame, healthFill;
    public Sprite cornerLeft, cornerRight;
    public Sprite panel;
    public Sprite frame;
    public Sprite title;
    public Sprite yellow, yellowHover, yellowDown;
    public Sprite red, redHover, redDown;
    public Sprite green, greenHover, greenDown;
    public Sprite inactive;
    public Sprite swordIcon, skillIcon, waitIcon, cancelIcon, crestIcon;
    private static GameUITheme cached;
    public static GameUITheme Current => cached != null ? cached : cached = Resources.Load<GameUITheme>("UI/SteampunkUITheme");

    private void SetSprite(Image image, Sprite sprite, Color tint)
    {
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = borderScale;
        image.color = tint;
    }

    public bool StyleButton(Button button, Color role)
    {
        if (yellow == null || button == null || !(button.targetGraphic is Image image)) return false;
        bool danger = role.r > role.g * 1.35f && role.r > role.b * 1.2f;
        bool positive = role.g > role.r * 1.15f && role.g > role.b * 1.1f;
        SetSprite(image, danger ? red : positive ? green : yellow, buttonTint);
        button.transition = Selectable.Transition.SpriteSwap;
        button.spriteState = new SpriteState
        {
            highlightedSprite = danger ? redHover : positive ? greenHover : yellowHover,
            selectedSprite = danger ? redHover : positive ? greenHover : yellowHover,
            pressedSprite = danger ? redDown : positive ? greenDown : yellowDown,
            disabledSprite = inactive
        };
        var label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (label != null) label.color = GameUITheme.ButtonInk;
        return true;
    }

    public void StylePanel(Image image, bool heading = false)
    {
        if (image == null || panel == null) return;
        SetSprite(image, heading && title != null ? title : panel,
            heading ? headingTint : panelTint);
        if (heading || frame == null || image.transform.Find("ThemeFrame") != null) return;
        var edge = new GameObject("ThemeFrame", typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(image.transform, false);
        edge.transform.SetAsFirstSibling();
        UIFactory.StretchFill((RectTransform)edge.transform);
        var border = edge.GetComponent<Image>();
        SetSprite(border, frame, frameTint);
        border.raycastTarget = false;
        if (image.name == "MenuPanel" || image.name == "SlotPanel" || image.name == "BottomUnitPanel")
        {
            AddCorner(image.transform, cornerLeft, false);
            AddCorner(image.transform, cornerRight, true);
        }
        if (image.name == "BottomUnitPanel")
        {
            AddDivider(image.transform, 0.30f);
            AddDivider(image.transform, 0.60f);
        }
    }

    private static void AddCorner(Transform parent, Sprite sprite, bool right)
    {
        if (sprite == null) return;
        var go = new GameObject(right ? "CornerRight" : "CornerLeft", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(right ? 1 : 0, 1);
        rect.sizeDelta = new Vector2(42, 28);
        rect.anchoredPosition = new Vector2(right ? -2 : 2, -2);
        var decoration = go.GetComponent<Image>();
        decoration.sprite = sprite;
        decoration.preserveAspect = true;
        decoration.raycastTarget = false;
        go.transform.SetAsFirstSibling();
    }

    // Compact plates use a single sliced sprite, avoiding extra frames per unit/resource.
    public void StylePlate(Image image, Color tint)
    {
        if (image == null || title == null) return;
        SetSprite(image, title, tint);
        image.raycastTarget = false;
    }

    private static void AddDivider(Transform parent, float position)
    {
        var go = new GameObject("ColumnRule", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(position, 0.12f); rect.anchorMax = new Vector2(position, 0.88f);
        rect.sizeDelta = new Vector2(1, 0); rect.anchoredPosition = Vector2.zero;
        var rule = go.GetComponent<Image>(); rule.color = new Color(0.75f, 0.58f, 0.33f, 0.35f);
        rule.raycastTarget = false;
    }

    public void AddCommandIcon(Button button)
    {
        Sprite icon = button.name switch
        {
            "AttackBtn" => swordIcon, "SkillBtn" => skillIcon,
            "WaitBtn" => waitIcon, "CancelBtn" => cancelIcon, _ => null
        };
        if (icon == null || button.transform.Find("ThemeIcon") != null) return;
        AddIcon(button.transform, icon, 22, 36);
        var label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (label != null) label.rectTransform.offsetMin = new Vector2(52, 0);
    }

    public void AddCrest(Transform heading)
    {
        if (crestIcon == null || heading.Find("ThemeIcon") != null) return;
        AddIcon(heading, crestIcon, 18, 28);
        var label = heading.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (label != null) label.rectTransform.offsetMin = new Vector2(56, 0);
    }

    private static void AddIcon(Transform parent, Sprite sprite, float left, float size)
    {
        var go = new GameObject("ThemeIcon", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
        rect.pivot = new Vector2(0, 0.5f);
        rect.sizeDelta = new Vector2(size, size); rect.anchoredPosition = new Vector2(left, 0);
        var image = go.GetComponent<Image>(); image.sprite = sprite;
        image.preserveAspect = true; image.raycastTarget = false;
    }
}
