using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>On-demand, scene-owned viewer for the latest in-memory diary. It never scans or reads saved files.</summary>
public sealed class AIDiaryUI : MonoBehaviour
{
    public const int MaximumDisplayedCharacters = 24000;
    static AIDiaryUI instance;
    public static bool IsOpen => instance != null && instance.gameObject.activeInHierarchy;
    TurnGenerator turn;
    bool showPlayer;
    TextMeshProUGUI body, locations, sourceLabel;
    ScrollRect scroll;
    Scrollbar scrollbar;
    TMP_FontAsset font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static void Show(TurnGenerator generator)
    {
        var canvas = UIBuilder.ScreenCanvas;
        if (canvas == null || generator == null) return;
        if (instance != null)
        {
            instance.turn = generator;
            instance.transform.SetAsLastSibling();
            instance.Refresh();
            return;
        }
        var overlay = new GameObject("AIDiaryOverlay", typeof(RectTransform), typeof(Image), typeof(AIDiaryUI));
        overlay.transform.SetParent(canvas.transform, false);
        UIFactory.StretchFill((RectTransform)overlay.transform);
        overlay.GetComponent<Image>().color = BrandGuide.OverlayBg;
        instance = overlay.GetComponent<AIDiaryUI>();
        instance.turn = generator;
        instance.Build();
        instance.Refresh();
    }

    public static void Close()
    {
        var current = instance;
        instance = null;
        if (current == null) return;
        current.gameObject.SetActive(false);
        Destroy(current.gameObject);
    }

    void OnDestroy() { if (instance == this) instance = null; }

    void Update()
    {
        // The menu owns Escape while it is open, so one press cannot close two stacked panels.
        if ((GameMenuUI.Instance == null || !GameMenuUI.Instance.IsOpen)
            && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        if (turn == null) Close();
    }

    void Build()
    {
        font = UIFactory.LoadDefaultFont();
        var panel = Rect("AIDiaryPanel", transform, new Vector2(.04f, .04f), new Vector2(.96f, .96f));
        var panelImage = panel.gameObject.AddComponent<Image>(); panelImage.color = BrandGuide.PanelBgLight;
        GameUITheme.Current?.StylePanel(panelImage);
        var title = Text("AIDiaryTitle", panel, "AI日記・行動の振り返り", 30, BrandGuide.Primary);
        title.fontStyle = FontStyles.Bold;
        Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -68), new Vector2(-24, -14));

        var controls = Rect("DiarySources", panel, new Vector2(0, 1), new Vector2(1, 1));
        controls.offsetMin = new Vector2(24, -126); controls.offsetMax = new Vector2(-24, -74);
        var layout = controls.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12; layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
        var enemy = Button("EnemyDiary", controls, "敵軍AIの日記", 230);
        enemy.onClick.AddListener(() => { showPlayer = false; Refresh(); });
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var player = Button("PlayerDiary", controls, "プレイヤーAI（開発用）", 300);
        player.interactable = turn.PlayerAI?.Commander != null;
        player.onClick.AddListener(() => { showPlayer = true; Refresh(); });
#endif
        sourceLabel = Text("DiarySourceLabel", controls, "", 19, BrandGuide.TextPrimary);
        sourceLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        var scrollRect = Rect("DiaryScroll", panel, Vector2.zero, Vector2.one);
        scrollRect.offsetMin = new Vector2(24, 194); scrollRect.offsetMax = new Vector2(-24, -142);
        scrollRect.gameObject.AddComponent<Image>().color = new Color(.02f, .025f, .035f, .9f);
        scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.inertia = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 42;
        var viewport = Rect("Viewport", scrollRect, Vector2.zero, Vector2.one);
        viewport.offsetMax = new Vector2(-40, 0);
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", viewport, new Vector2(0, 1), new Vector2(1, 1));
        content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        body = content.gameObject.AddComponent<TextMeshProUGUI>();
        StyleText(body, 21, BrandGuide.TextPrimary);
        body.alignment = TextAlignmentOptions.TopLeft; body.lineSpacing = 7;
        body.margin = new Vector4(20, 18, 20, 18);
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = viewport;
        scrollbar = UIFactory.CreateVerticalScrollbar(scrollRect);
        scrollbar.GetComponent<RectTransform>().sizeDelta = new Vector2(34, 0);
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.onValueChanged.AddListener(_ => scrollbar.size = Mathf.Max(.08f, scrollbar.size));

        locations = Text("DiaryStorageLocations", panel, "", 16, BrandGuide.TextSecondary);
        locations.alignment = TextAlignmentOptions.TopLeft;
        locations.overflowMode = TextOverflowModes.Ellipsis;
        Place(locations.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(28, 68), new Vector2(-28, 184));

        var footer = Rect("DiaryControls", panel, Vector2.zero, new Vector2(1, 0));
        footer.offsetMin = new Vector2(24, 16); footer.offsetMax = new Vector2(-24, 58);
        var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 12; footerLayout.childControlWidth = true; footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = true; footerLayout.childForceExpandHeight = true;
        Button("RefreshDiary", footer, "最新の表示に更新", 220).onClick.AddListener(Refresh);
        Button("DiaryTop", footer, "先頭へ", 180).onClick.AddListener(() => scroll.verticalNormalizedPosition = 1);
        Button("DiaryBottom", footer, "末尾へ", 180).onClick.AddListener(() => scroll.verticalNormalizedPosition = 0);
        Button("CopyDiaryLocations", footer, "保存先をコピー", 220).onClick.AddListener(() => GUIUtility.systemCopyBuffer = locations.text);
        Button("CloseDiary", footer, "閉じる [Esc]", 220).onClick.AddListener(Close);
    }

    AICommander Commander()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (showPlayer) return turn != null ? turn.PlayerAI?.Commander : null;
#endif
        return turn != null ? turn.Systems.AICommander : null;
    }

    void Refresh()
    {
        if (body == null) return;
        var commander = Commander();
        sourceLabel.text = showPlayer ? "プレイヤーAIの最新記録" : "敵軍AIの最新記録";
        string diary = commander?.LatestDiaryText;
        if (string.IsNullOrEmpty(diary))
            body.text = "AI日記はまだありません。\n\nAIが設定した間隔（初期設定は15ターン）まで行動すると、また勝利・敗北が確定した時に作成します。\n自己評価が無効になっている場合は記録されません。\n\n日記には実際の行動結果、報酬、失敗理由、学習の変化を表示します。";
        else body.text = Bounded(diary, MaximumDisplayedCharacters,
            "\n\n表示文字数の上限に達しました。全文は保存された日記を確認してください。");
        string directory = commander?.ReflectionStorageDirectory
            ?? Path.Combine(Application.persistentDataPath, "FantasyKingdom", "AI");
        string diaryPath = commander?.LatestDiaryPath;
        locations.text = "保存フォルダー：" + Bounded(Path.Combine(directory, "Diary"), 1200)
            + "\n最新の日記：" + (string.IsNullOrEmpty(diaryPath) ? "ファイル保存なし（画面の記録のみ）" : Bounded(diaryPath, 1200))
            + "\n学習データ：" + (string.IsNullOrEmpty(commander?.ReflectionProfilePath) ? "まだ作成されていません" : Bounded(commander.ReflectionProfilePath, 1200));
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.verticalNormalizedPosition = 1;
        scrollbar.size = Mathf.Max(.08f, scrollbar.size);
    }

    static string Bounded(string text, int limit, string suffix = "…")
    {
        if (string.IsNullOrEmpty(text) || text.Length <= limit) return text;
        limit -= suffix.Length;
        if (char.IsHighSurrogate(text[limit - 1]) && char.IsLowSurrogate(text[limit])) limit--;
        return text.Substring(0, limit) + suffix;
    }

    RectTransform Rect(string name, Transform parent, Vector2 minimum, Vector2 maximum)
    {
        var rect = UIFactory.CreatePanel(name, parent, minimum, maximum, new Vector2(.5f, .5f), Vector2.zero);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color)
    {
        var text = UIFactory.CreateTMP(name, parent, value, size, font);
        StyleText(text, size, color); return text;
    }
    void StyleText(TextMeshProUGUI text, float size, Color color)
    {
        text.font = font; text.fontSize = size; text.color = color;
        text.richText = false; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
    }
    Button Button(string name, Transform parent, string label, float width)
    {
        var button = UIFactory.CreateButton(name, parent, label, 20, BrandGuide.BtnUnit, font);
        var layout = button.gameObject.AddComponent<LayoutElement>(); layout.preferredWidth = width;
        button.GetComponentInChildren<TextMeshProUGUI>().raycastTarget = false;
        return button;
    }
    static void Place(RectTransform rect, Vector2 minimum, Vector2 maximum, Vector2 from, Vector2 to)
    {
        rect.anchorMin = minimum; rect.anchorMax = maximum; rect.offsetMin = from; rect.offsetMax = to;
    }
}
