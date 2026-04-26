using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 召喚時の3択スキル選択UI（暫定版）。
/// SummonSystem が召喚完了後に Show(candidates, onPicked) を呼び、選択結果を返す。
/// 敵AIは SkillData.PickBestForAI を直接使ってUIを経由しない。
/// </summary>
public class SkillChoiceUI : MonoBehaviour
{
    public static SkillChoiceUI Instance { get; private set; }

    private GameObject overlay;
    private Action<int> pendingCallback;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public static void ShowOrCreate(List<int> candidateIds, Action<int> onPicked)
    {
        if (Instance == null)
        {
            var go = new GameObject("SkillChoiceUI");
            go.AddComponent<SkillChoiceUI>();
        }
        Instance.Show(candidateIds, onPicked);
    }

    public void Show(List<int> candidateIds, Action<int> onPicked)
    {
        if (candidateIds == null || candidateIds.Count == 0)
        {
            onPicked?.Invoke(-1);
            return;
        }

        pendingCallback = onPicked;
        if (overlay != null) Destroy(overlay);

        var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            // Canvasがない場合はUIを出さずに先頭を自動選択
            Resolve(candidateIds[0]);
            return;
        }

        overlay = new GameObject("SkillChoiceOverlay", typeof(RectTransform));
        overlay.transform.SetParent(canvas.transform, false);
        overlay.transform.SetAsLastSibling();
        var rt = overlay.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var bg = overlay.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.75f);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(overlay.transform, false);
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.15f, 0.25f);
        prt.anchorMax = new Vector2(0.85f, 0.75f);
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.95f);

        var header = CreateTMP("Header", panel.transform, "スキルを1つ選んでください", 26,
            new Color(0.95f, 0.88f, 0.55f));
        header.fontStyle = FontStyles.Bold;
        header.alignment = TextAlignmentOptions.Center;
        var hrt = header.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 0.85f); hrt.anchorMax = new Vector2(1, 1f);
        hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;

        var row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(panel.transform, false);
        var rrt = row.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0.03f, 0.08f);
        rrt.anchorMax = new Vector2(0.97f, 0.82f);
        rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        foreach (var id in candidateIds) AddCandidateCard(row.transform, id);
    }

    void AddCandidateCard(Transform parent, int skillId)
    {
        if (!SkillData.Table.TryGetValue(skillId, out var s)) return;

        var card = new GameObject($"Card_{skillId}", typeof(RectTransform));
        card.transform.SetParent(parent, false);
        card.AddComponent<Image>().color = RarityColor(s.Rarity);

        var btn = card.AddComponent<Button>();
        btn.onClick.AddListener(() => Resolve(skillId));

        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(card.transform, false);
        var trt = titleGO.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 0.75f); trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(8, 0); trt.offsetMax = new Vector2(-8, 0);
        var title = titleGO.AddComponent<TextMeshProUGUI>();
        title.text = $"<b>{s.Name}</b>\n<size=14>{s.Rarity}</size>";
        title.fontSize = 18;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;

        var bodyGO = new GameObject("Body", typeof(RectTransform));
        bodyGO.transform.SetParent(card.transform, false);
        var brt = bodyGO.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0.75f);
        brt.offsetMin = new Vector2(8, 8); brt.offsetMax = new Vector2(-8, -8);
        var body = bodyGO.AddComponent<TextMeshProUGUI>();
        body.text = FormatDescription(s);
        body.fontSize = 13;
        body.alignment = TextAlignmentOptions.TopLeft;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.color = new Color(0.95f, 0.93f, 0.85f);
    }

    static string FormatDescription(SkillData s)
    {
        var parts = new List<string>();
        parts.Add($"AP {s.APCost}");
        parts.Add($"Area {s.Area}");
        if (s.Multiplier > 0) parts.Add($"×{s.Multiplier:F2}");
        if (s.FixedDamage > 0) parts.Add($"固定Dmg {s.FixedDamage}");
        if (s.FixedHeal > 0) parts.Add($"回復 {s.FixedHeal}");
        if (s.InflictDebuff != StatusEffectType.None)
            parts.Add($"{s.InflictDebuff}付与 {Mathf.RoundToInt(s.DebuffChance * 100)}%");
        if (s.GrantBuff != BuffType.None) parts.Add($"{s.GrantBuff}バフ");
        if (!string.IsNullOrEmpty(s.SpecialEffect)) parts.Add($"特殊: {s.SpecialEffect}");
        return string.Join("\n", parts);
    }

    static Color RarityColor(SkillRarity r)
    {
        switch (r)
        {
            case SkillRarity.Legendary: return new Color(0.52f, 0.20f, 0.52f, 0.95f);
            case SkillRarity.SuperRare: return new Color(0.55f, 0.35f, 0.12f, 0.95f);
            case SkillRarity.Rare:      return new Color(0.18f, 0.38f, 0.55f, 0.95f);
            default:                    return new Color(0.22f, 0.28f, 0.32f, 0.95f);
        }
    }

    void Resolve(int pickedId)
    {
        var cb = pendingCallback;
        pendingCallback = null;
        if (overlay != null) { Destroy(overlay); overlay = null; }
        cb?.Invoke(pickedId);
    }

    static TextMeshProUGUI CreateTMP(string name, Transform parent, string text, int size, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = c;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }
}
