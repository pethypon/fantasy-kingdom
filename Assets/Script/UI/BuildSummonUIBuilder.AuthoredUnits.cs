using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class BuildSummonUIBuilder
{
    readonly List<(Button button, Image background, TextMeshProUGUI title, TextMeshProUGUI cost, UnitData data)> authoredSummonButtons
        = new List<(Button, Image, TextMeshProUGUI, TextMeshProUGUI, UnitData)>();

    // 表示費用も召喚処理と同じ実行時定義から取得する。
    UnitData ResolveSummonDefinition(Kind kind)
    {
        return cachedUnitSetting != null && cachedUnitSetting.UnitDataMap != null
            && cachedUnitSetting.UnitDataMap.TryGetValue(kind, out var data) ? data : null;
    }

    (Button button, TextMeshProUGUI title, TextMeshProUGUI cost) CreateSummonRow(
        string rowName, Transform content, string buttonName, string costName, string displayName, UnitData data)
    {
        var row = new GameObject(rowName, typeof(RectTransform));
        row.transform.SetParent(content, false);
        // 資源の折り返しや不足理由に合わせて行を広げる。固定高で文字を切らない。
        row.AddComponent<LayoutElement>().minHeight = 104;
        var layout = row.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4;
        layout.padding = new RectOffset(0, 0, 0, 6);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var button = UIFactory.CreateButton(buttonName, row.transform,
            SummonTitle(displayName, data), BrandGuide.FontHud, BrandGuide.BtnSummonEnabled, defaultFont);
        button.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
        var title = button.GetComponentInChildren<TextMeshProUGUI>();
        title.enableAutoSizing = true;
        title.fontSizeMin = BrandGuide.FontBody;
        title.fontSizeMax = BrandGuide.FontHud;
        title.margin = new Vector4(18, 0, 18, 0);

        var cost = UIFactory.CreateTMP(costName, row.transform, FormatAuthoredUnitCost(data), BrandGuide.FontHudCaption, defaultFont);
        cost.color = BrandGuide.TextSecondary;
        cost.alignment = TextAlignmentOptions.MidlineLeft;
        cost.textWrappingMode = TextWrappingModes.Normal;
        cost.raycastTarget = false;
        cost.margin = new Vector4(8, 0, 8, 0);
        cost.gameObject.AddComponent<LayoutElement>().minHeight = 38;
        return (button, title, cost);
    }

    static string SummonTitle(string displayName, UnitData data)
        => data != null ? displayName + "  AP:" + data.costAP : displayName;

    void RefreshSummonRow(Button button, Image background, TextMeshProUGUI title,
        TextMeshProUGUI cost, UnitData data, string displayName)
    {
        bool allowed = data != null && summonSystem.CanSummon(Team.Player, data);
        button.interactable = allowed;
        background.color = allowed ? BrandGuide.BtnSummonEnabled : BrandGuide.BtnDisabled;
        title.text = SummonTitle(displayName, data);
        string reason = allowed ? null : AuthoredUnitCostFailure(data);
        cost.text = FormatAuthoredUnitCost(data)
            + (reason == null ? string.Empty : "\n<color=#FFB7A3>" + reason + "</color>");
    }

    void CreateAuthoredSummonButtons(Transform content)
    {
        var catalog = UnitAuthoringCatalog.Load();
        if (catalog == null) return;
        foreach (var data in catalog.EnumerateStandaloneDefinitions(Team.Player))
        {
            var row = CreateSummonRow("AuthoredUnit_" + data.definitionId, content,
                "召喚", "必要な資材", data.DisplayName, data);
            UnitData selected = data;
            row.button.onClick.AddListener(() =>
            {
                if (summonSystem == null) return;
                slidePanel?.ClosePanel();
                summonSystem.StartSummonMode(selected);
            });
            authoredSummonButtons.Add((row.button, row.button.GetComponent<Image>(), row.title, row.cost, data));
        }
    }

    void RefreshAuthoredSummonButtons()
    {
        foreach (var item in authoredSummonButtons)
        {
            if (item.button == null || item.data == null) continue;
            RefreshSummonRow(item.button, item.background, item.title, item.cost, item.data, item.data.DisplayName);
        }
    }

    string AuthoredUnitCostFailure(UnitData data)
    {
        if (data == null) return "召喚データ未登録";
        if (cachedFactionState == null) return "準備中";
        if (!data.AvailableFor(Team.Player)) return "この陣営では召喚不可";
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
        if (data == null) return "準備中";
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
