using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 画面右下に直近の行動ログを表示するUI。
/// 移動・攻撃・建築・召喚などの行動を時系列で表示。
/// 最大表示数を超えると古い行動が消える。
/// </summary>
public class ActionLogUI : MonoBehaviour
{
    public static ActionLogUI Instance { get; private set; }

    const int MaxEntries = 8;
    const float EntryLifetime = 12f;
    const float FadeSpeed = 3f;

    struct LogEntry
    {
        public string Text;
        public float TimeAdded;
    }

    readonly List<LogEntry> _entries = new List<LogEntry>();
    TextMeshProUGUI _logText;
    CanvasGroup _group;
    bool _dirty = true;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("ActionLogCanvas");
        canvasGo.transform.SetParent(transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // パネル（右下）
        var panelGo = new GameObject("LogPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panelRT = panelGo.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(1f, 0f);
        panelRT.anchorMax = new Vector2(1f, 0f);
        panelRT.pivot = new Vector2(1f, 0f);
        panelRT.anchoredPosition = new Vector2(-10, 45);
        panelRT.sizeDelta = new Vector2(320, 200);

        var bg = panelGo.AddComponent<Image>();
        bg.color = new Color(0.02f, 0.02f, 0.04f, 0.5f);

        _group = panelGo.AddComponent<CanvasGroup>();
        _group.alpha = 0.8f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        // テキスト
        var textGo = new GameObject("LogText");
        textGo.transform.SetParent(panelGo.transform, false);
        _logText = textGo.AddComponent<TextMeshProUGUI>();
        _logText.fontSize = 12;
        _logText.color = new Color(0.85f, 0.85f, 0.85f);
        _logText.alignment = TextAlignmentOptions.BottomLeft;
        _logText.richText = true;
        _logText.textWrappingMode = TextWrappingModes.Normal;

        var textRT = _logText.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(8, 4);
        textRT.offsetMax = new Vector2(-8, -4);

        // フォント
        var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-VariableFont_wght SDF");
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null)
            _logText.font = font;
    }

    void Update()
    {
        // 古いエントリを削除
        float now = Time.time;
        bool removed = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (now - _entries[i].TimeAdded > EntryLifetime)
            {
                _entries.RemoveAt(i);
                removed = true;
            }
        }
        if (removed) _dirty = true;

        // フェード
        float targetAlpha = _entries.Count > 0 ? 0.8f : 0f;
        if (!Mathf.Approximately(_group.alpha, targetAlpha))
            _group.alpha = Mathf.MoveTowards(_group.alpha, targetAlpha, FadeSpeed * Time.deltaTime);

        // テキスト更新
        if (_dirty)
        {
            _dirty = false;
            RefreshText();
        }
    }

    void RefreshText()
    {
        if (_logText == null) return;
        if (_entries.Count == 0) { _logText.text = ""; return; }

        var sb = new System.Text.StringBuilder();
        int start = Mathf.Max(0, _entries.Count - MaxEntries);
        for (int i = start; i < _entries.Count; i++)
        {
            if (sb.Length > 0) sb.Append("\n");
            sb.Append(_entries[i].Text);
        }
        _logText.text = sb.ToString();
    }

    /// <summary>ログエントリを追加</summary>
    public static void Log(string message)
    {
        if (Instance == null) return;
        Instance._entries.Add(new LogEntry { Text = message, TimeAdded = Time.time });
        if (Instance._entries.Count > MaxEntries * 2)
            Instance._entries.RemoveRange(0, Instance._entries.Count - MaxEntries);
        Instance._dirty = true;
    }

    /// <summary>Playerの移動ログ</summary>
    public static void LogMove(string unitName, Vector3 from, Vector3 to)
    {
        Log($"<color=#AAD4FF>▶</color> {unitName} 移動 ({from.x:F0},{from.z:F0})→({to.x:F0},{to.z:F0})");
    }

    /// <summary>攻撃ログ</summary>
    public static void LogAttack(string attackerName, string targetName, int damage, bool killed)
    {
        string killMark = killed ? " <color=#FF4444>✕撃破</color>" : "";
        Log($"<color=#FF8844>⚔</color> {attackerName}→{targetName} {damage}ダメージ{killMark}");
    }

    /// <summary>建築ログ</summary>
    public static void LogBuild(string facilityName, Team team)
    {
        string teamColor = team == Team.Player ? "#AAFFAA" : "#FFAAAA";
        Log($"<color={teamColor}>🏠</color> {facilityName} 建設");
    }

    /// <summary>召喚ログ</summary>
    public static void LogSummon(string unitName, Team team)
    {
        string teamColor = team == Team.Player ? "#DDAAFF" : "#FFAADD";
        Log($"<color={teamColor}>★</color> {unitName} 召喚");
    }

    /// <summary>ターン開始ログ</summary>
    public static void LogTurnStart(int turn, Team team)
    {
        string teamName = team == Team.Player ? "プレイヤー" : "敵";
        Log($"<color=#888888>── ターン{turn} {teamName} ──</color>");
    }
}
