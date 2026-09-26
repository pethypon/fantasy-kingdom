using UnityEngine;
using UnityEngine.UI;

/// <summary>References the imported HONETI sprites without copying or modifying vendor assets.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/Wooden UI Theme")]
public sealed class WoodenUITheme : ScriptableObject
{
    public static readonly Color ButtonInk = new Color(0.98f, 0.90f, 0.73f);
    public Sprite panel;
    public Sprite frame;
    public Sprite title;
    public Sprite yellow, yellowHover, yellowDown;
    public Sprite red, redHover, redDown;
    public Sprite green, greenHover, greenDown;
    public Sprite inactive;
    public Sprite swordIcon, skillIcon, waitIcon, cancelIcon, crestIcon;
    private static WoodenUITheme cached;
    public static WoodenUITheme Current => cached != null ? cached : cached = Resources.Load<WoodenUITheme>("UI/WoodenUITheme");

    private static void SetSprite(Image image, Sprite sprite, Color tint)
    {
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 4;
        image.color = tint;
    }

    public bool StyleButton(Button button, Color role)
    {
        if (yellow == null || button == null || !(button.targetGraphic is Image image)) return false;
        bool danger = role.r > role.g * 1.35f && role.r > role.b * 1.2f;
        bool positive = role.g > role.r * 1.15f && role.g > role.b * 1.1f;
        SetSprite(image, danger ? red : positive ? green : yellow, new Color(0.52f, 0.46f, 0.38f));
        button.transition = Selectable.Transition.SpriteSwap;
        button.spriteState = new SpriteState
        {
            highlightedSprite = danger ? redHover : positive ? greenHover : yellowHover,
            selectedSprite = danger ? redHover : positive ? greenHover : yellowHover,
            pressedSprite = danger ? redDown : positive ? greenDown : yellowDown,
            disabledSprite = inactive
        };
        var label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (label != null) label.color = WoodenUITheme.ButtonInk;
        return true;
    }

    public void StylePanel(Image image, bool heading = false)
    {
        if (image == null || panel == null) return;
        SetSprite(image, heading && title != null ? title : panel,
            heading ? new Color(0.40f, 0.34f, 0.25f, 1) : new Color(0.085f, 0.060f, 0.042f, 0.99f));
        if (heading || frame == null || image.transform.Find("WoodenFrame") != null) return;
        var edge = new GameObject("WoodenFrame", typeof(RectTransform), typeof(Image));
        edge.transform.SetParent(image.transform, false);
        UIFactory.StretchFill((RectTransform)edge.transform);
        var border = edge.GetComponent<Image>();
        SetSprite(border, frame, new Color(0.85f, 0.77f, 0.64f));
        border.raycastTarget = false;
        if (image.name == "BottomUnitPanel")
        {
            AddDivider(image.transform, 0.30f);
            AddDivider(image.transform, 0.60f);
        }
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
        if (icon == null || button.transform.Find("WoodenIcon") != null) return;
        AddIcon(button.transform, icon, 22, 36);
        var label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (label != null) label.rectTransform.offsetMin = new Vector2(52, 0);
    }

    public void AddCrest(Transform heading)
    {
        if (crestIcon == null || heading.Find("WoodenIcon") != null) return;
        AddIcon(heading, crestIcon, 18, 28);
        var label = heading.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (label != null) label.rectTransform.offsetMin = new Vector2(56, 0);
    }

    private static void AddIcon(Transform parent, Sprite sprite, float left, float size)
    {
        var go = new GameObject("WoodenIcon", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
        rect.pivot = new Vector2(0, 0.5f);
        rect.sizeDelta = new Vector2(size, size); rect.anchoredPosition = new Vector2(left, 0);
        var image = go.GetComponent<Image>(); image.sprite = sprite;
        image.preserveAspect = true; image.raycastTarget = false;
    }
}
