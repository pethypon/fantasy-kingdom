using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Responsibility: read-only diary overlay; keyboard input belongs to TurnInputHandler.
/// Lifetime/Owner: scene / ScreenCanvas. Maximum: one root, one bounded event page and report.
/// Cleanup: release snapshot/string when closed, root destroyed with canvas. Failure: close UI only.
/// </summary>
public sealed class AIDiaryUI : MonoBehaviour
{
    public const int MaximumDisplayedCharacters = 30000;
    static AIDiaryUI instance;
    public static bool IsOpen => instance != null && instance.gameObject.activeInHierarchy;
    public static int RebuildCount => instance != null ? instance.rebuildCount : 0;
    public static int LayoutRebuildCount => instance != null ? instance.layoutRebuildCount : 0;
    public static int DisplayedEventCount => IsOpen ? instance.snapshot?.Events.Count ?? 0 : 0;
    public static int DisplayedCharacterCount => IsOpen && instance.body != null ? instance.body.text.Length : 0;
    public static int PageCount => instance != null ? instance.characterPages : 0;
    TurnGenerator turn;
    bool showPlayer, showReport = true, built, forceRefresh, warningLogged;
    int eventPage, characterPage, characterPages = 1, rebuildCount, layoutRebuildCount;
    long renderedRevision = -1;
    float nextRefreshTime;
    AIDiarySnapshot snapshot;
    AICommander renderedCommander;
    string contentText, renderedReport;
    TextMeshProUGUI body, locations, sourceLabel, pageLabel;
    ScrollRect scroll;
    Scrollbar scrollbar;
    TMP_FontAsset font;
    CanvasGroup raycasts;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    /// <summary>Central input router calls this once per key-down.</summary>
    public static void Toggle(TurnGenerator generator)
    { if (IsOpen) Close(); else Show(generator); }

    /// <summary>Reuse the scene root and read existing memory. Does not initialize AI or read a file.</summary>
    public static void Show(TurnGenerator generator)
    {
        var canvas = UIBuilder.ScreenCanvas;
        if (canvas == null || generator == null) return;
        try
        {
            if (instance == null)
            {
                var overlay = new GameObject("AIDiaryOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(AIDiaryUI));
                overlay.SetActive(false);
                overlay.transform.SetParent(canvas.transform, false);
                UIFactory.StretchFill((RectTransform)overlay.transform);
                overlay.GetComponent<Image>().color = BrandGuide.OverlayBg;
                instance = overlay.GetComponent<AIDiaryUI>();
                instance.raycasts = overlay.GetComponent<CanvasGroup>();
            }
            if (instance.turn != generator) { instance.showPlayer = false; instance.showReport = true; }
            instance.turn = generator;
            if (!instance.built) { instance.Build(); instance.built = true; }
            instance.eventPage = instance.characterPage = 0;
            instance.renderedRevision = -1; instance.forceRefresh = true;
            instance.transform.SetAsLastSibling();
            instance.raycasts.interactable = instance.raycasts.blocksRaycasts = true;
            instance.gameObject.SetActive(true);
            instance.Refresh();
        }
        catch (Exception error) { instance?.Fail(error); }
    }

    /// <summary>Disable all presentation work and raycasts; structured AI recording continues.</summary>
    public static void Close()
    {
        var current = instance;
        if (current == null) return;
        if (current.raycasts != null) current.raycasts.interactable = current.raycasts.blocksRaycasts = false;
        current.gameObject.SetActive(false);
        current.snapshot = null; current.contentText = current.renderedReport = null;
        current.renderedCommander = null; current.renderedRevision = -1;
        AIPerformanceCounters.SetDiaryVisibleLines(0);
    }

    void OnDestroy() { if (instance == this) { instance = null; AIPerformanceCounters.SetDiaryVisibleLines(0); } }

    /// <summary>Hidden ticks perform no formatting, layout work or collection reads.</summary>
    public static void Tick()
    {
        if (!IsOpen) return;
        var current = instance;
        if (current.turn == null) { Close(); return; }
        if (Time.unscaledTime < current.nextRefreshTime) return;
        try
        {
            current.nextRefreshTime = Time.unscaledTime + 1f / AIPerformanceConfig.Active.SafeDiaryRefreshHz;
            var commander = current.Commander();
            if (current.forceRefresh || current.renderedCommander != commander
                || current.renderedRevision != (commander?.DiaryRevision ?? 0)
                || !ReferenceEquals(current.renderedReport, commander?.LatestDiaryText)) current.Refresh();
        }
        catch (Exception error) { current.Fail(error); }
    }

    void Fail(Exception error)
    {
        Close();
        if (warningLogged) return;
        warningLogged = true;
        Debug.LogWarning("[AI日記UI] 表示を閉じてAI処理を続行します: " + error.Message);
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
        enemy.onClick.AddListener(() => SelectSource(false));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var player = Button("PlayerDiary", controls, "プレイヤーAI（開発用）", 300);
        player.onClick.AddListener(() => SelectSource(true));
#endif
        sourceLabel = Text("DiarySourceLabel", controls, "", 19, BrandGuide.TextPrimary);
        sourceLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        var scrollRect = Rect("DiaryScroll", panel, Vector2.zero, Vector2.one);
        scrollRect.offsetMin = new Vector2(24, 240); scrollRect.offsetMax = new Vector2(-24, -142);
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
        scroll.onValueChanged.AddListener(UpdateScrollbar);

        var paging = Rect("DiaryPages", panel, Vector2.zero, new Vector2(1, 0));
        paging.offsetMin = new Vector2(24, 190); paging.offsetMax = new Vector2(-24, 234);
        var pagingLayout = paging.gameObject.AddComponent<HorizontalLayoutGroup>();
        pagingLayout.spacing = 10; pagingLayout.childControlWidth = pagingLayout.childControlHeight = true;
        pagingLayout.childForceExpandWidth = pagingLayout.childForceExpandHeight = true;
        Button("DiaryReport", paging, "期間報告", 160).onClick.AddListener(() => SelectReport(true));
        Button("DiaryActions", paging, "最近の行動", 160).onClick.AddListener(() => SelectReport(false));
        Button("DiaryPreviousPage", paging, "前の記録", 170).onClick.AddListener(() => ChangePage(-1));
        pageLabel = Text("DiaryPageNumber", paging, "", 17, BrandGuide.TextSecondary);
        pageLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 170;
        Button("DiaryNextPage", paging, "次の記録", 170).onClick.AddListener(() => ChangePage(1));

        locations = Text("DiaryStorageLocations", panel, "", 16, BrandGuide.TextSecondary);
        locations.alignment = TextAlignmentOptions.TopLeft;
        locations.overflowMode = TextOverflowModes.Ellipsis;
        Place(locations.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(28, 68), new Vector2(-28, 184));

        var footer = Rect("DiaryControls", panel, Vector2.zero, new Vector2(1, 0));
        footer.offsetMin = new Vector2(24, 16); footer.offsetMax = new Vector2(-24, 58);
        var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 12; footerLayout.childControlWidth = true; footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = true; footerLayout.childForceExpandHeight = true;
        Button("RefreshDiary", footer, "最新の表示に更新", 220).onClick.AddListener(RequestRefresh);
        Button("DiaryTop", footer, "先頭へ", 180).onClick.AddListener(() => ScrollTo(1));
        Button("DiaryBottom", footer, "末尾へ", 180).onClick.AddListener(() => ScrollTo(0));
        Button("CopyDiaryLocations", footer, "保存先をコピー", 220).onClick.AddListener(CopyLocations);
        Button("CloseDiary", footer, "閉じる [Esc]", 220).onClick.AddListener(Close);
    }

    AICommander Commander()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (showPlayer) return turn != null ? turn.PlayerAI?.Commander : null;
#endif
        return turn != null ? turn.Systems?.AICommander : null;
    }

    void Refresh()
    {
        if (!IsOpen) return;
        if (body == null || scroll == null || sourceLabel == null || locations == null || pageLabel == null)
            throw new InvalidOperationException("日記の表示要素が破棄されています。");
        using var measured = AIPerformanceCounters.Measure(AIPerformanceStage.UI);
        var commander = Commander();
        var config = AIPerformanceConfig.Active;
        if (forceRefresh || snapshot == null || renderedCommander != commander || renderedRevision != (commander?.DiaryRevision ?? 0)
            || !ReferenceEquals(renderedReport, commander?.LatestDiaryText))
        {
            snapshot = commander?.CaptureDiarySnapshot(eventPage, config.DiaryEventLimit)
                ?? new AIDiarySnapshot(0, showPlayer ? Team.Player : Team.Enemy, null, null, eventPage, config.DiaryEventLimit);
            eventPage = snapshot.PageIndex;
            // Until the first interval report, finalized observed actions are already available in memory.
            contentText = showReport && !string.IsNullOrEmpty(snapshot.ReportText) ? snapshot.ReportText : snapshot.RenderEvents();
            renderedCommander = commander; renderedRevision = snapshot.Revision; renderedReport = commander?.LatestDiaryText;
            forceRefresh = false;
        }
        // One character of headroom keeps a surrogate pair together without exceeding the TMP budget.
        int pageStride = config.DiaryCharacterLimit - 1;
        characterPages = Math.Max(1, (contentText.Length + pageStride - 1) / pageStride);
        characterPage = Mathf.Clamp(characterPage, 0, characterPages - 1);
        string text = PageSlice(contentText, characterPage, config.DiaryCharacterLimit);
        sourceLabel.text = showPlayer ? "プレイヤーAIの記録" : "敵軍AIの記録";
        pageLabel.text = (HasReport ? "期間報告" : "行動 " + (snapshot.PageCount - eventPage) + "/" + snapshot.PageCount)
            + "　" + (characterPage + 1) + "/" + characterPages;
        string directory = commander?.ReflectionStorageDirectory
            ?? Path.Combine(Application.persistentDataPath, "FantasyKingdom", "AI");
        string diaryPath = commander?.LatestDiaryPath;
        locations.text = "保存フォルダー：" + Bounded(Path.Combine(directory, "Diary"), 1200)
            + "\n最新の日記：" + (string.IsNullOrEmpty(diaryPath) ? "ファイル保存なし（画面の記録のみ）" : Bounded(diaryPath, 1200))
            + "\n学習データ：" + (string.IsNullOrEmpty(commander?.ReflectionProfilePath) ? "まだ作成されていません" : Bounded(commander.ReflectionProfilePath, 1200));
        if (body.text != text)
        {
            body.text = text; rebuildCount++; AIPerformanceCounters.MarkDiaryUIRebuild();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content); layoutRebuildCount++;
            scroll.verticalNormalizedPosition = 1; scrollbar.size = Mathf.Max(.08f, scrollbar.size);
        }
        int lines = text.Length == 0 ? 0 : 1;
        for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
        AIPerformanceCounters.SetDiaryVisibleLines(lines);
        nextRefreshTime = Time.unscaledTime + 1f / config.SafeDiaryRefreshHz;
    }

    void SelectSource(bool player) { showPlayer = player; eventPage = characterPage = 0; RequestRefresh(); }
    bool HasReport => showReport && snapshot != null && !string.IsNullOrEmpty(snapshot.ReportText);
    void SelectReport(bool report) { showReport = report; eventPage = characterPage = 0; RequestRefresh(); }
    void RequestRefresh() { forceRefresh = true; SafeRefresh(); }
    void SafeRefresh() { try { Refresh(); } catch (Exception error) { Fail(error); } }
    void ScrollTo(float position)
    { try { scroll.verticalNormalizedPosition = position; } catch (Exception error) { Fail(error); } }
    void CopyLocations()
    { try { GUIUtility.systemCopyBuffer = locations.text; } catch (Exception error) { Fail(error); } }
    void UpdateScrollbar(Vector2 position)
    {
        if (!IsOpen) return;
        try { scrollbar.size = Mathf.Max(.08f, scrollbar.size); } catch (Exception error) { Fail(error); }
    }

    void ChangePage(int delta)
    {
        if (delta < 0)
        {
            if (characterPage > 0) characterPage--;
            else if (!HasReport && snapshot != null && eventPage < snapshot.PageCount - 1)
            { eventPage++; characterPage = 0; forceRefresh = true; }
            else return;
        }
        else
        {
            if (characterPage < characterPages - 1) characterPage++;
            else if (!HasReport && eventPage > 0) { eventPage--; characterPage = 0; forceRefresh = true; }
            else return;
        }
        SafeRefresh();
    }

    // Keep split surrogate pairs on the following page so pagination never drops a character.
    static string PageSlice(string text, int page, int limit)
    {
        int stride = Math.Max(1, limit - 1);
        int start = Math.Min(text.Length, page * stride), end = Math.Min(text.Length, (page + 1) * stride);
        if (start > 0 && start < text.Length && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1])) start--;
        if (end > 0 && end < text.Length && char.IsLowSurrogate(text[end]) && char.IsHighSurrogate(text[end - 1])) end--;
        return start == 0 && end == text.Length ? text : text.Substring(start, end - start);
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
