using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Rows retain only their definition; one reusable scene-owned popup handles hover.</summary>
public sealed class SummonUnitTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    static SummonTooltipView view;
    public UnitData Definition { get; set; }
    internal SlidePanelUI Panel { get; private set; }
    internal ScrollRect Scroll { get; private set; }

    public void OnPointerEnter(PointerEventData data)
    {
        if (Definition == null || !isActiveAndEnabled) return;
        Panel = GetComponentInParent<SlidePanelUI>(); Scroll = GetComponentInParent<ScrollRect>();
        if (Panel != null && !Panel.IsOpen) return;
        var canvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null) return;
        if (view == null)
        {
            var popup = UIFactory.CreatePanel("SummonUnitDetailsTooltip", canvas.transform, new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), new Vector2(0, 1), new Vector2(600, 400));
            UIFactory.AddImage(popup.gameObject, BrandGuide.PanelBg).raycastTarget = false;
            BrandGuide.AddPanelBorder(popup);
            var group = popup.gameObject.AddComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            view = popup.gameObject.AddComponent<SummonTooltipView>();
        }
        view.Show(this, canvas, data != null ? data.position : Vector2.zero);
    }
    public void OnPointerExit(PointerEventData data) { if (view != null) view.Hide(this); }
    void OnDisable() { if (view != null) view.Hide(this); }
    void OnDestroy() { if (view != null) view.Hide(this); }
    public static void HideCurrent() { if (view != null) view.Hide(); }

    public static string Description(UnitData data)
    {
        if (data == null) return "駒のデータが未登録です。";
        var prefab = data.prefab != null ? data.prefab.GetComponentInChildren<Status>(true) : null;
        bool usesBarrier = BarrierSystem.UsesBarrier(data.kind, Type.Unit, prefab != null && prefab.isWildBoss);
        int hp = UnitData.CalcStat(data.baseHP, data.hpGrowth, 1);
        var text = new StringBuilder("<b>").Append(data.DisplayName).Append("</b>　Lv1\nHP ").Append(hp).Append(" / ").Append(hp);
        if (usesBarrier)
        {
            int barrier = BarrierSystem.Maximum(data.baseBarrier < 0 ? BarrierSystem.Initial(data.kind) : data.baseBarrier, 1);
            text.Append("\nバリア ").Append(barrier).Append(" / ").Append(barrier);
        }
        text.Append("\n攻撃力 ").Append(UnitData.CalcStat(data.baseATK, data.atkGrowth, 1));
        text.Append("\n\n<b>制作費</b>　AP ").Append(data.costAP).Append("\n").Append(BuildSummonUIBuilder.FormatAuthoredUnitCost(data));
        var upkeep = data.GetUpkeep(1);
        text.Append("\n<b>維持費 / 毎ターン</b>\n").Append(upkeep.IsEmpty ? "なし" : BuildSummonUIBuilder.FormatProductionBundle(upkeep));
        if (data.specialSkill != null && data.specialSkill.ability != UnitSpecialSkill.None)
        {
            text.Append("\n\n<b>スキル</b>　").Append(data.specialSkill.IsPassive ? "なし" : SpecialSkillPresentation.Name(data.specialSkill));
            if (!data.specialSkill.IsPassive) text.Append('\n').Append(SpecialSkillPresentation.Description(data.specialSkill));
            text.Append("\n\n<b>アビリティ</b>\n").Append(SpecialSkillPresentation.PassiveDescription(data.specialSkill));
            return text.ToString();
        }
        int skillId = data.useAuthoredAbilities ? data.authoredSkillId : SkillData.FixedSkillIds.TryGetValue(data.kind, out int fixedId) ? fixedId : -1;
        var passive = data.useAuthoredAbilities ? data.authoredPassive : data.kind == Kind.Boss ? PassiveSkill.StrangeKingAura : prefab != null ? prefab.passiveskill : PassiveSkill.None;
        if (skillId >= 0 && SkillData.Table.TryGetValue(skillId, out var skill))
            text.Append("\n\n<b>スキル</b>\n").Append(SpecialSkillPresentation.SkillDescription(skillId));
        if (passive != PassiveSkill.None)
            text.Append("\n\n<b>アビリティ</b>\n").Append(SpecialSkillPresentation.PassiveDescription(passive));
        if (data.useAuthoredAbilities && data.authoredSpecialAbility != SpecialAbility.None
            && SpecialAbilityData.Table.TryGetValue(data.authoredSpecialAbility, out var ability))
            text.Append("\n<b>追加能力</b>　").Append(SpecialSkillPresentation.AbilityDescription(data.authoredSpecialAbility));
        return text.ToString();
    }
}

/// <summary>Only this shared popup ticks while visible; no per-row update or scene search.</summary>
public sealed class SummonTooltipView : MonoBehaviour
{
    SummonUnitTooltip owner;
    TextMeshProUGUI label;
    ScrollRect scroll;
    Canvas ownerCanvas;
    Vector2 screenPoint, lastCanvasSize;
    RenderMode lastRenderMode;
    bool arranging;
    public void Show(SummonUnitTooltip source, Canvas canvas, Vector2 screenPoint)
    {
        Hide(); owner = source;
        var root = (RectTransform)canvas.transform; var popup = (RectTransform)transform;
        ownerCanvas = canvas; this.screenPoint = screenPoint;
        popup.SetParent(root, false); popup.SetAsLastSibling();
        if (label == null)
        {
            label = UIFactory.CreateTMP("Details", popup, string.Empty, 20, UIFactory.LoadDefaultFont());
            label.alignment = TextAlignmentOptions.TopLeft; label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false; UIFactory.StretchFill(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(18, 14); label.rectTransform.offsetMax = new Vector2(-18, -14);
        }
        label.text = SummonUnitTooltip.Description(source.Definition);
        Reflow();
        scroll = source.Scroll;
        if (scroll != null) scroll.onValueChanged.AddListener(OnScroll);
        gameObject.SetActive(true);
        Canvas.willRenderCanvases += BeforeRender;
    }
    void BeforeRender()
    {
        if (ownerCanvas == null || owner == null || arranging) return;
        if (((RectTransform)ownerCanvas.transform).rect.size != lastCanvasSize || ownerCanvas.renderMode != lastRenderMode) Reflow();
    }
    void Reflow()
    {
        if (ownerCanvas == null || label == null || arranging) return;
        arranging = true;
        var root = (RectTransform)ownerCanvas.transform; var popup = (RectTransform)transform;
        lastCanvasSize = root.rect.size; lastRenderMode = ownerCanvas.renderMode;
        label.enableAutoSizing = false; label.fontSize = 20;
        float width = Mathf.Min(460, root.rect.width - 32);
        float height = Mathf.Clamp(label.GetPreferredValues(label.text, width - 36, 0).y + 28, 100, Mathf.Max(100, root.rect.height - 32));
        popup.sizeDelta = new Vector2(width, height);
        label.enableAutoSizing = true; label.fontSizeMin = 15; label.fontSizeMax = 20;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint,
            ownerCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : ownerCanvas.worldCamera, out var point);
        var position = new Vector3(Mathf.Clamp(point.x + 24, root.rect.xMin + 16, root.rect.xMax - width - 16),
            Mathf.Clamp(point.y + 18, root.rect.yMin + height + 16, root.rect.yMax - 16), 0);
        popup.position = root.TransformPoint(position);
        arranging = false;
    }
    void OnScroll(Vector2 value) => Hide();
    void LateUpdate()
    {
        if (owner == null || !owner.isActiveAndEnabled || !owner.gameObject.activeInHierarchy || owner.Panel != null && !owner.Panel.IsOpen) Hide();
    }
    public void Hide(SummonUnitTooltip source = null)
    {
        if (source != null && owner != source) return;
        Canvas.willRenderCanvases -= BeforeRender;
        if (scroll != null) scroll.onValueChanged.RemoveListener(OnScroll);
        scroll = null; owner = null; ownerCanvas = null; gameObject.SetActive(false);
    }
    void OnDestroy() { Canvas.willRenderCanvases -= BeforeRender; if (scroll != null) scroll.onValueChanged.RemoveListener(OnScroll); }
}
