using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class BuildSummonUIBuilder
{
    readonly List<(Button button, Image background, TextMeshProUGUI cost, UnitData data)> authoredSummonButtons
        = new List<(Button, Image, TextMeshProUGUI, UnitData)>();

    void CreateAuthoredSummonButtons(Transform content)
    {
        var catalog = UnitAuthoringCatalog.Load();
        if (catalog == null) return;
        foreach (var data in catalog.EnumerateStandaloneDefinitions(Team.Player))
        {
            var row = new GameObject("AuthoredUnit_" + data.definitionId, typeof(RectTransform));
            row.transform.SetParent(content, false);
            row.AddComponent<LayoutElement>().preferredHeight = 104;
            var layout = row.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2; layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var button = UIFactory.CreateButton("召喚", row.transform,
                data.DisplayName + "  AP:" + data.costAP, BrandGuide.FontHud, BrandGuide.BtnSummonEnabled, defaultFont);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            UnitData selected = data;
            button.onClick.AddListener(() =>
            {
                if (summonSystem == null) return;
                slidePanel?.ClosePanel();
                summonSystem.StartSummonMode(selected);
            });
            var cost = UIFactory.CreateTMP("必要な資材", row.transform, FormatAuthoredUnitCost(data), BrandGuide.FontHudCaption, defaultFont);
            cost.color = BrandGuide.TextSecondary; cost.alignment = TextAlignmentOptions.MidlineLeft;
            cost.textWrappingMode = TextWrappingModes.Normal;
            cost.enableAutoSizing = true; cost.fontSizeMin = 20; cost.fontSizeMax = BrandGuide.FontHudCaption;
            cost.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
            authoredSummonButtons.Add((button, button.GetComponent<Image>(), cost, data));
        }
    }

    void RefreshAuthoredSummonButtons()
    {
        foreach (var item in authoredSummonButtons)
        {
            if (item.button == null || item.data == null) continue;
            bool allowed = summonSystem.CanSummon(Team.Player, item.data);
            item.button.interactable = allowed;
            item.background.color = allowed ? BrandGuide.BtnSummonEnabled : BrandGuide.BtnDisabled;
            string reason = allowed ? null : AuthoredUnitCostFailure(item.data);
            item.cost.text = FormatAuthoredUnitCost(item.data)
                + (reason == null ? string.Empty : "\n<color=#FFB7A3>" + reason + "</color>");
        }
    }

    string AuthoredUnitCostFailure(UnitData data)
    {
        if (cachedFactionState == null) return "準備中";
        if (cachedFactionState.GetAP(Team.Player) < data.costAP) return "行動ポイント不足";
        var resources = cachedFactionState.PlayerResources;
        if (resources.Wood < data.costWood) return "木材不足";
        if (resources.Stone < data.costStone) return "石材不足";
        if (resources.Iron < data.costIron) return "鉄不足";
        if (resources.MagicOre < data.costMagic) return "魔石不足";
        if (resources.Water < data.costWater) return "水不足";
        if (resources.Bread < data.costBread) return "パン不足";
        if (resources.Citizen < data.costCitizen) return "市民不足";
        return "召喚不可";
    }

    static string FormatAuthoredUnitCost(UnitData data)
    {
        var parts = new List<string>(7);
        if (data.costWood > 0) parts.Add("木材 " + data.costWood);
        if (data.costStone > 0) parts.Add("石材 " + data.costStone);
        if (data.costIron > 0) parts.Add("鉄 " + data.costIron);
        if (data.costMagic > 0) parts.Add("魔石 " + data.costMagic);
        if (data.costWater > 0) parts.Add("水 " + data.costWater);
        if (data.costBread > 0) parts.Add("パン " + data.costBread);
        if (data.costCitizen > 0) parts.Add("市民 " + data.costCitizen);
        return parts.Count == 0 ? "資材の消費なし" : string.Join(" / ", parts);
    }
}
