using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class BuildSummonUIBuilder
{
    readonly List<(Button button, Image image, TextMeshProUGUI label, FacilityDefinitionData definition)> authoredBuildButtons
        = new List<(Button, Image, TextMeshProUGUI, FacilityDefinitionData)>();

    void CreateAuthoredBuildRows(GameObject content)
    {
        authoredBuildButtons.Clear();
        var catalog = FacilityAuthoringCatalog.Loaded;
        if (catalog == null || catalog.buildings == null) return;
        foreach (var definition in catalog.buildings)
        {
            if (definition == null || !definition.IsAvailable(Team.Player) || catalog.Find(definition.definitionId) != definition) continue;
            var row = new GameObject("AuthoredBuilding_" + definition.definitionId, typeof(RectTransform));
            row.transform.SetParent(content.transform, false);
            row.AddComponent<LayoutElement>().minHeight = 110;
            var layout = row.AddComponent<VerticalLayoutGroup>(); layout.spacing = 3;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var info = definition.GetInfo();
            var button = UIFactory.CreateButton("Build", row.transform, definition.displayName + "  AP:" + info.APCost,
                BrandGuide.FontHud, BrandGuide.BtnBuildEnabled, defaultFont);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            var label = UIFactory.CreateTMP("Cost", row.transform, FormatBuildCost(info.BuildCost), BrandGuide.FontHudCaption, defaultFont);
            label.color = BrandGuide.TextSecondary; label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal; label.enableAutoSizing = true;
            label.fontSizeMin = 20; label.fontSizeMax = BrandGuide.FontHudCaption;
            label.gameObject.AddComponent<LayoutElement>().minHeight = 48;
            var captured = definition;
            button.onClick.AddListener(() => OnAuthoredBuildButtonClicked(captured));
            authoredBuildButtons.Add((button, button.GetComponent<Image>(), label, definition));
        }
    }

    void RefreshAuthoredBuildButtons()
    {
        foreach (var row in authoredBuildButtons)
        {
            if (row.button == null || row.definition == null) continue;
            string reason = BuildMenuFailure(row.definition);
            row.button.interactable = reason == null;
            row.image.color = reason == null ? BrandGuide.BtnBuildEnabled : BrandGuide.BtnDisabled;
            string cost = FormatBuildCost(BuildCostForMenu(row.definition));
            if (engineerMenuRequested) cost = "工兵建築：資源費2倍\n" + cost;
            cost += FormatBuildingDetails(row.definition.behaviourKind, row.definition.GetLevel(1));
            var title = row.button.GetComponentInChildren<TextMeshProUGUI>();
            if (title != null) title.text = row.definition.displayName + "  AP:" + BuildAPForMenu(row.definition);
            if (FacilityData.IsSubCrystal(row.definition.behaviourKind)) cost += " 副晶1消費";
            row.label.text = reason == null ? cost : cost + "\n<color=#FFB7A3>" + reason + "</color>";
        }
    }
}
