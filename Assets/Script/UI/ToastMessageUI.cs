using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// ゲーム内トーストメッセージ表示システム。
/// Debug.Log の代わりにプレイヤーに重要な情報を画面上に表示する。
/// シングルトンパターンで、どこからでも ToastMessageUI.Show() で呼び出し可能。
/// </summary>
public class ToastMessageUI : MonoBehaviour
{
    public static ToastMessageUI Instance { get; private set; }

    private Canvas _canvas;
    private RectTransform _containerRect;
    private readonly List<ToastEntry> _activeToasts = new List<ToastEntry>();

    // ---- 設定 ----
    private const int MaxToasts = 5;
    private const float DefaultDuration = 3f;
    private const float FadeOutTime = 0.5f;
    private const float ToastHeight = 36f;
    private const float ToastSpacing = 4f;
    private const float ToastWidth = 450f;

    private struct ToastEntry
    {
        public GameObject Go;
        public RectTransform Rect;
        public CanvasGroup Group;
        public float ExpireTime;
        public float FadeStart;
    }

    // ---- メッセージ種別 ----
    public enum MessageType { Info, Warning, Success, Error }

    private static readonly Dictionary<MessageType, Color> TypeColors = new Dictionary<MessageType, Color>
    {
        { MessageType.Info,    BrandGuide.Secondary },
        { MessageType.Warning, BrandGuide.Warning },
        { MessageType.Success, BrandGuide.Accent },
        { MessageType.Error,   BrandGuide.Danger },
    };

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildUI()
    {
        // Canvas
        var canvasGo = new GameObject("ToastCanvas");
        canvasGo.transform.SetParent(transform);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 300;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Container（画面右下）
        var containerGo = new GameObject("ToastContainer");
        containerGo.transform.SetParent(canvasGo.transform, false);
        _containerRect = containerGo.AddComponent<RectTransform>();
        _containerRect.anchorMin = new Vector2(1, 0);
        _containerRect.anchorMax = new Vector2(1, 0);
        _containerRect.pivot = new Vector2(1, 0);
        _containerRect.anchoredPosition = new Vector2(-20, 120);
        _containerRect.sizeDelta = new Vector2(ToastWidth, 300);
    }

    /// <summary>トーストメッセージを表示する（静的ヘルパー）。
    /// duration 省略時は種別に応じた表示時間（警告4秒・エラー5秒・他3秒）を使う。</summary>
    public static void Show(string message, MessageType type = MessageType.Info, float duration = -1f)
    {
        if (duration <= 0f) duration = GetDefaultDuration(type);
        if (Instance != null)
            Instance.ShowInternal(message, type, duration);
        else
            Debug.Log($"[Toast] {message}");
    }

    private static float GetDefaultDuration(MessageType type)
    {
        switch (type)
        {
            case MessageType.Warning: return 4f;
            case MessageType.Error:   return 5f;
            default:                  return DefaultDuration;
        }
    }

    private void ShowInternal(string message, MessageType type, float duration)
    {
        // 上限超え → 最古を即座に削除
        while (_activeToasts.Count >= MaxToasts)
        {
            RemoveToast(0);
        }

        // トーストオブジェクト生成
        var toastGo = new GameObject("Toast");
        toastGo.transform.SetParent(_containerRect, false);

        var rect = toastGo.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(ToastWidth, ToastHeight);
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(0.5f, 0);

        // 背景
        var bg = toastGo.AddComponent<Image>();
        bg.color = BrandGuide.PanelBg;

        // CanvasGroup（フェードアウト用）
        var group = toastGo.AddComponent<CanvasGroup>();
        group.alpha = 1f;

        // テキスト
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(toastGo.transform, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = message;
        text.fontSize = BrandGuide.FontCaption;
        text.color = TypeColors.TryGetValue(type, out var c) ? c : Color.white;
        text.alignment = TextAlignmentOptions.Left;
        text.overflowMode = TextOverflowModes.Ellipsis;

        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12, 2);
        textRect.offsetMax = new Vector2(-12, -2);

        // 左端にカラーバー
        var barGo = new GameObject("Bar");
        barGo.transform.SetParent(toastGo.transform, false);
        var barImg = barGo.AddComponent<Image>();
        barImg.color = TypeColors.TryGetValue(type, out var bc) ? bc : Color.white;
        var barRect = barGo.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0, 0);
        barRect.anchorMax = new Vector2(0, 1);
        barRect.sizeDelta = new Vector2(3, 0);
        barRect.anchoredPosition = new Vector2(1.5f, 0);

        float now = Time.time;
        _activeToasts.Add(new ToastEntry
        {
            Go = toastGo,
            Rect = rect,
            Group = group,
            ExpireTime = now + duration,
            FadeStart = now + duration - FadeOutTime
        });

        RepositionToasts();
    }

    private void Update()
    {
        float now = Time.time;

        for (int i = _activeToasts.Count - 1; i >= 0; i--)
        {
            var entry = _activeToasts[i];

            if (now >= entry.ExpireTime)
            {
                RemoveToast(i);
                continue;
            }

            // フェードアウト
            if (now >= entry.FadeStart && entry.Group != null)
            {
                float remaining = entry.ExpireTime - now;
                entry.Group.alpha = Mathf.Clamp01(remaining / FadeOutTime);
            }
        }
    }

    private void RemoveToast(int index)
    {
        if (index < 0 || index >= _activeToasts.Count) return;
        var entry = _activeToasts[index];
        if (entry.Go != null) Destroy(entry.Go);
        _activeToasts.RemoveAt(index);
        RepositionToasts();
    }

    private void RepositionToasts()
    {
        for (int i = 0; i < _activeToasts.Count; i++)
        {
            _activeToasts[i].Rect.anchoredPosition = new Vector2(0, i * (ToastHeight + ToastSpacing));
        }
    }
}
