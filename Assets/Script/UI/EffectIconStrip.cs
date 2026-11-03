using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Six pooled status badges at most; no actor searches, per-frame Update or new gameplay state.</summary>
public sealed class EffectIconStrip : MonoBehaviour
{
    const int Capacity = 6;
    readonly GameObject[] slots = new GameObject[Capacity];
    readonly Image[] icons = new Image[Capacity];
    readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[Capacity];
    readonly ResourceValueTooltip[] tips = new ResourceValueTooltip[Capacity];
    readonly StringBuilder overflow = new StringBuilder();
    int count;
    public bool HasEffects => count > 0;

    public static EffectIconStrip Create(TextMeshProUGUI owner)
    {
        var go = new GameObject("EffectIcons", typeof(RectTransform)); go.transform.SetParent(owner.transform, false);
        var rect = go.GetComponent<RectTransform>(); UIFactory.StretchFill(rect);
        var layout = go.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 3;
        layout.childAlignment = TextAnchor.MiddleCenter; layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return go.AddComponent<EffectIconStrip>();
    }
    public void Refresh(Status unit)
    {
        count = 0; overflow.Clear();
        if (unit != null)
        {
            if (unit.ShieldTurns > 0) Add("state.Shield", "完全防御", unit.ShieldTurns);
            foreach (var effect in unit.ActiveEffects)
            {
                if (effect.remainingTurns <= 0 || effect.debuffType == StatusEffectType.ArmorBreak || effect.buffType == BuffType.Defensive) continue;
                if (effect.IsDebuff) Add("debuff." + effect.debuffType, SpecialSkillPresentation.DebuffName(effect.debuffType), effect.remainingTurns);
                else if (effect.IsBuff) Add("buff." + effect.buffType, SpecialSkillPresentation.BuffName(effect.buffType), effect.remainingTurns);
            }
        }
        for (int i = Mathf.Min(count, Capacity); i < Capacity; i++) if (slots[i] != null) slots[i].SetActive(false);
        if (count > Capacity)
        {
            icons[Capacity-1].enabled = false; labels[Capacity-1].text = "+" + (count - Capacity + 1);
            tips[Capacity-1].Value = overflow.ToString();
        }
        gameObject.SetActive(count > 0);
    }
    void Add(string key, string name, int turns)
    {
        int index = count++;
        if (index >= Capacity-1) overflow.AppendLine(name + "　残り" + turns + "ターン");
        if (index >= Capacity) return;
        if (slots[index] == null)
        {
            var slot = new GameObject("StatusBadge", typeof(RectTransform), typeof(Image)); slot.transform.SetParent(transform, false);
            ((RectTransform)slot.transform).sizeDelta = new Vector2(46, 54);
            slot.GetComponent<Image>().color = new Color(.08f,.11f,.16f,.9f);
            tips[index] = slot.AddComponent<ResourceValueTooltip>();
            var image = new GameObject("Icon",typeof(RectTransform),typeof(Image)); image.transform.SetParent(slot.transform,false);
            icons[index] = image.GetComponent<Image>(); icons[index].preserveAspect = true; icons[index].raycastTarget = false;
            UIFactory.SetAnchors(icons[index].rectTransform, .14f, .35f, .86f, .96f);
            labels[index] = UIFactory.CreateTMP("Turns",slot.transform,"",16,UIFactory.LoadDefaultFont());
            UIFactory.SetAnchors(labels[index].rectTransform,0,0,1,.36f); labels[index].alignment=TextAlignmentOptions.Center;
            labels[index].raycastTarget=false;
            slots[index]=slot;
        }
        slots[index].SetActive(true);
        icons[index].sprite = GameIconCatalog.Get(key); icons[index].enabled = icons[index].sprite != null;
        labels[index].text = icons[index].enabled ? turns + "T" : name + " " + turns;
        labels[index].enableAutoSizing = true; labels[index].fontSizeMin = 12; labels[index].fontSizeMax = 16;
        tips[index].Value = name + "　残り" + turns + "ターン\n効果の詳細は「詳細」で確認";
    }
}
