#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Runs against initialized systems without saving fixture assets or placing actors.</summary>
public static class BuildSummonUIRegressionTests
{
    static void Check(string name, bool ok)
    {
        if (!ok) throw new Exception("[BuildSummonUI] FAIL " + name);
        Debug.Log("[BuildSummonUI] PASS " + name);
    }

    static Transform Content(GameObject scroll)
    {
        var content = scroll.transform.Find("Viewport/Content");
        Check("scroll content exists: " + scroll.name, content != null);
        return content;
    }

    static Transform Row(Transform content, string name)
    {
        var row = content.Find(name);
        Check("row exists: " + name, row != null);
        return row;
    }

    static Button ButtonIn(Transform row, string name)
    {
        var child = row.Find(name);
        var button = child != null ? child.GetComponent<Button>() : null;
        Check("button exists: " + row.name + "/" + name, button != null);
        return button;
    }

    static TextMeshProUGUI CostIn(Transform row, string name)
    {
        var child = row.Find(name);
        var text = child != null ? child.GetComponent<TextMeshProUGUI>() : null;
        Check("cost exists: " + row.name + "/" + name, text != null);
        return text;
    }

    static string Compact(string text) => Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);

    static string ExpectedCost(UnitData data)
    {
        var parts = new List<string>();
        if (data.costWood > 0) parts.Add("木材 " + data.costWood);
        if (data.costStone > 0) parts.Add("石材 " + data.costStone);
        if (data.costIron > 0) parts.Add("鉄 " + data.costIron);
        if (data.costMagic > 0) parts.Add("魔石 " + data.costMagic);
        if (data.costWater > 0) parts.Add("水 " + data.costWater);
        if (data.costBread > 0) parts.Add("パン " + data.costBread);
        if (data.costCitizen > 0) parts.Add("市民 " + data.costCitizen);
        return parts.Count == 0 ? "資材の消費なし" : string.Join(" / ", parts);
    }

    static void CheckUnitCost(string name, Button button, TextMeshProUGUI cost, UnitData data)
    {
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        Check(name + " shows the effective definition's AP", label != null
            && Compact(label.text).EndsWith("AP:" + data.costAP, StringComparison.Ordinal));
        Check(name + " shows every required resource and amount", Compact(cost.text.Split('\n')[0]) == Compact(ExpectedCost(data)));
    }

    static void CheckCostLayout(string name, RectTransform parent, Transform content, Button button, TextMeshProUGUI cost)
    {
        Canvas.ForceUpdateCanvases();
        // Text wrapping depends on the horizontal pass; later passes propagate its height to the row and content.
        for (int pass = 0; pass < 3; pass++)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content);
        }
        cost.ForceMeshUpdate();
        var row = (RectTransform)button.transform.parent;
        var costRect = cost.rectTransform;
        Check(name + " uses the requested panel width", Mathf.Abs(parent.rect.width - parent.sizeDelta.x) < 1f
            && costRect.rect.width > parent.rect.width - 100f && costRect.rect.width <= parent.rect.width);
        Check(name + " wraps all resources and the shortage message", cost.textInfo.lineCount >= 3);
        Check(name + " allocates the full wrapped text height", cost.preferredHeight <= costRect.rect.height + 1f);
        var buttonCorners = new Vector3[4]; var costCorners = new Vector3[4];
        ((RectTransform)button.transform).GetWorldCorners(buttonCorners); costRect.GetWorldCorners(costCorners);
        float buttonBottom = row.InverseTransformPoint(buttonCorners[0]).y;
        float costBottom = row.InverseTransformPoint(costCorners[0]).y;
        float costTop = row.InverseTransformPoint(costCorners[1]).y;
        Check(name + " keeps the cost below the button and inside the row", costTop <= buttonBottom + 1f
            && costBottom >= row.rect.yMin - 1f && costTop <= row.rect.yMax + 1f);
    }

    static UnitData Definition(string id, GameObject model, int wood)
    {
        var data = ScriptableObject.CreateInstance<UnitData>();
        data.definitionId = id; data.displayName = "召喚UI検証の駒";
        data.kind = Kind.Knight; data.baseHP = 80; data.baseATK = 7; data.baseDEF = 3;
        data.prefab = model; data.availableToPlayer = true; data.availableToEnemy = true;
        data.costAP = 3; data.costWood = wood; data.costStone = 4; data.costIron = 5;
        data.costMagic = 6; data.costWater = 7; data.costBread = 8; data.costCitizen = 9;
        return data;
    }

    static void Fund(FactionState state)
    {
        var resources = state.PlayerResources;
        resources.Wood = resources.Stone = resources.Iron = resources.MagicOre = 100000;
        resources.Water = resources.Bread = resources.Citizen = 100000;
        state.PlayerAP.Current = 50;
    }

    public static void Run(GameSystems systems)
    {
        Check("initialized economy, building and summon systems are available", systems != null
            && systems.UnitSetting != null && systems.UnitSetting.UnitDataMap != null
            && systems.FactionState != null && systems.FactionState.PlayerNation != null
            && systems.APSystem != null && systems.BuildSystem != null && systems.SummonSystem != null);
        var state = systems.FactionState;
        var catalog = UnitAuthoringCatalog.Load();
        Check("unit resource catalog is available for an in-memory standalone fixture", catalog != null && catalog.units != null);
        string originalCatalog = EditorJsonUtility.ToJson(catalog);
        var originalEntries = catalog.units;
        string originalResources = JsonUtility.ToJson(state.PlayerResources);
        int originalAP = state.PlayerAP.Current;
        bool hadKnight = systems.UnitSetting.UnitDataMap.TryGetValue(Kind.Knight, out var originalKnight);
        var loadedField = typeof(FacilityAuthoringCatalog).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic);
        var didLoadField = typeof(FacilityAuthoringCatalog).GetField("didLoad", BindingFlags.Static | BindingFlags.NonPublic);
        Check("facility catalog cache can be isolated", loadedField != null && didLoadField != null);
        var previousLoaded = loadedField.GetValue(null);
        var previousDidLoad = didLoadField.GetValue(null);
        var fixtureCatalog = ScriptableObject.CreateInstance<FacilityAuthoringCatalog>();
        var fixtureBuilding = ScriptableObject.CreateInstance<FacilityDefinitionData>();
        var model = new GameObject("BuildSummonUI inactive prefab fixture");
        model.SetActive(false);
        var actor = model.AddComponent<Status>(); actor.kind = Kind.Knight; actor.type = global::Type.Unit;
        string fixtureId = Guid.NewGuid().ToString("N");
        var authoredUnit = Definition("test.summon-ui.authored." + fixtureId, model, 12);
        var roleUnit = Definition("test.summon-ui.role." + fixtureId, model, 3);
        var root = new GameObject("BuildSummonUI fixture", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var panel = new GameObject("Fixed-width BuildSummonUI panel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        var parent = panel.GetComponent<RectTransform>();
        parent.anchorMin = parent.anchorMax = new Vector2(.5f, .5f); parent.sizeDelta = new Vector2(420, 960);
        try
        {
            // Copy the list so restoring the original list also restores all existing entry identities.
            catalog.units = new List<UnitAuthoringCatalog.Entry>(originalEntries);
            catalog.units.Add(new UnitAuthoringCatalog.Entry { kind = authoredUnit.kind, data = authoredUnit,
                prefab = model, standaloneDefinition = true });
            fixtureBuilding.definitionId = "test.summon-ui.building." + fixtureId;
            fixtureBuilding.displayName = "建築UI検証の建物"; fixtureBuilding.behaviourKind = FacilityKind.LoggingCamp;
            fixtureBuilding.buildAP = 4; fixtureBuilding.buildCost = new FacilityData.ResourceCost(wood: 11, water: 13, citizen: 1);
            fixtureCatalog.buildings.Add(fixtureBuilding);
            loadedField.SetValue(null, fixtureCatalog); didLoadField.SetValue(null, true);
            Fund(state);

            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-VariableFont_wght SDF")
                ?? TMP_Settings.defaultFontAsset;
            var builder = new BuildSummonUIBuilder(font);
            builder.InitBuildButtons(systems.BuildSystem, systems.APSystem, state);
            builder.InitSummonButtons(systems.SummonSystem, systems.APSystem, state, systems.UnitSetting);
            var buildScroll = builder.CreateBuildScrollView("Build UI fixture", parent);
            var summonScroll = builder.CreateScrollView("Summon UI fixture", parent);
            var buildContent = Content(buildScroll); var summonContent = Content(summonScroll);
            string buildingRowName = "AuthoredBuilding_" + fixtureBuilding.definitionId;
            var buildingRow = Row(buildContent, buildingRowName);
            var buildingButton = ButtonIn(buildingRow, "Build");
            var buildingCost = CostIn(buildingRow, "Cost");
            Check("registered building appears only in the building list", summonContent.Find(buildingRowName) == null);
            foreach (Transform child in summonContent)
                Check("summon list contains no authored building row: " + child.name, !child.name.StartsWith("AuthoredBuilding_", StringComparison.Ordinal));

            builder.RefreshBuildButtons(); builder.RefreshSummonButtons();
            Check("all ten standard unit kinds are covered", BuildSummonUIBuilder.SummonableKinds.Length == 10);
            foreach (var kind in BuildSummonUIBuilder.SummonableKinds)
            {
                Check("effective summon definition exists: " + kind,
                    systems.UnitSetting.UnitDataMap.TryGetValue(kind, out var definition) && definition != null);
                var row = Row(summonContent, "Row_" + kind);
                var button = ButtonIn(row, "Summon_" + kind);
                var cost = CostIn(row, "Cost_" + kind);
                CheckUnitCost("standard " + kind, button, cost, definition);
            }
            var authoredRow = Row(summonContent, "AuthoredUnit_" + authoredUnit.definitionId);
            var authoredButton = ButtonIn(authoredRow, "召喚");
            var authoredCost = CostIn(authoredRow, "必要な資材");
            CheckUnitCost("independent authored unit", authoredButton, authoredCost, authoredUnit);
            Check("funded authored building and unit are enabled", buildingButton.interactable && authoredButton.interactable);

            // A changed role definition must update the legacy row, including resources absent in default data.
            systems.UnitSetting.UnitDataMap[Kind.Knight] = roleUnit;
            builder.RefreshSummonButtons();
            var knightRow = Row(summonContent, "Row_" + Kind.Knight);
            var knightButton = ButtonIn(knightRow, "Summon_" + Kind.Knight);
            var knightCost = CostIn(knightRow, "Cost_" + Kind.Knight);
            CheckUnitCost("updated role override", knightButton, knightCost, roleUnit);
            CheckUnitCost("authored costs remain independent of the role override", authoredButton, authoredCost, authoredUnit);

            var shortages = new KeyValuePair<string, Action<FactionState.ResourceData>>[]
            {
                new KeyValuePair<string, Action<FactionState.ResourceData>>("木材不足", r => r.Wood = 0),
                new KeyValuePair<string, Action<FactionState.ResourceData>>("石材不足", r => r.Stone = 0),
                new KeyValuePair<string, Action<FactionState.ResourceData>>("鉄不足", r => r.Iron = 0),
                new KeyValuePair<string, Action<FactionState.ResourceData>>("魔石不足", r => r.MagicOre = 0),
                new KeyValuePair<string, Action<FactionState.ResourceData>>("水不足", r => r.Water = 0),
                new KeyValuePair<string, Action<FactionState.ResourceData>>("パン不足", r => r.Bread = 0),
                new KeyValuePair<string, Action<FactionState.ResourceData>>("市民不足", r => r.Citizen = 0),
            };
            foreach (var shortage in shortages)
            {
                Fund(state); shortage.Value(state.PlayerResources); builder.RefreshSummonButtons();
                Check("resource shortage disables standard and authored units: " + shortage.Key,
                    !knightButton.interactable && !authoredButton.interactable
                    && knightCost.text.Contains(shortage.Key) && authoredCost.text.Contains(shortage.Key));
                if (shortage.Key == "木材不足")
                {
                    foreach (float width in new[] { 420f, 360f })
                    {
                        parent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                        CheckCostLayout("standard seven-resource shortage at " + width + "px", parent, summonContent, knightButton, knightCost);
                        CheckCostLayout("authored seven-resource shortage at " + width + "px", parent, summonContent, authoredButton, authoredCost);
                    }
                    parent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 420f);
                }
                Fund(state); builder.RefreshSummonButtons();
                Check("resource recovery clears shortage and enables both units: " + shortage.Key,
                    knightButton.interactable && authoredButton.interactable
                    && !knightCost.text.Contains("不足") && !authoredCost.text.Contains("不足"));
            }
            state.PlayerAP.Current = 0; builder.RefreshSummonButtons();
            Check("AP shortage is visible for both kinds of unit", !knightButton.interactable && !authoredButton.interactable
                && knightCost.text.Contains("不足") && authoredCost.text.Contains("不足"));
            Fund(state); builder.RefreshSummonButtons();
            Check("AP recovery clears the failure message", knightButton.interactable && authoredButton.interactable
                && !knightCost.text.Contains("不足") && !authoredCost.text.Contains("不足"));

            state.PlayerResources.Wood = 0; builder.RefreshBuildButtons();
            Check("building refresh displays missing resources", !buildingButton.interactable && buildingCost.text.Contains("木材不足"));
            Fund(state); builder.RefreshBuildButtons();
            Check("building refresh restores the independent building", buildingButton.interactable && !buildingCost.text.Contains("不足"));
            state.PlayerAP.Current = 0; builder.RefreshBuildButtons();
            Check("building refresh displays missing AP", !buildingButton.interactable && buildingCost.text.Contains("AP不足"));
            Fund(state); builder.RefreshBuildButtons();
            Check("building AP recovery restores availability", buildingButton.interactable && !buildingCost.text.Contains("不足"));

            UnityEngine.Object.DestroyImmediate(summonScroll);
            summonScroll = builder.CreateScrollView("Regenerated summon UI fixture", parent);
            summonContent = Content(summonScroll);
            Check("regenerating summon UI keeps buildings out of the new content", summonContent.Find(buildingRowName) == null);
            state.PlayerResources.Wood = 0;
            // Deliberately refresh only the building side after creating the new summon side.
            builder.RefreshBuildButtons();
            Check("summon regeneration retains independent building button references", !buildingButton.interactable
                && buildingCost.text.Contains("木材不足") && buildContent.Find(buildingRowName) == buildingRow);
            Fund(state); builder.RefreshBuildButtons();
            Check("building-only refresh still recovers after summon regeneration", buildingButton.interactable && !buildingCost.text.Contains("不足"));
            builder.RefreshSummonButtons();
            var regeneratedKnight = Row(summonContent, "Row_" + Kind.Knight);
            CheckUnitCost("regenerated standard row", ButtonIn(regeneratedKnight, "Summon_" + Kind.Knight),
                CostIn(regeneratedKnight, "Cost_" + Kind.Knight), roleUnit);
            var regeneratedAuthored = Row(summonContent, "AuthoredUnit_" + authoredUnit.definitionId);
            CheckUnitCost("regenerated independent row", ButtonIn(regeneratedAuthored, "召喚"),
                CostIn(regeneratedAuthored, "必要な資材"), authoredUnit);
        }
        finally
        {
            catalog.units = originalEntries;
            if (hadKnight) systems.UnitSetting.UnitDataMap[Kind.Knight] = originalKnight;
            else systems.UnitSetting.UnitDataMap.Remove(Kind.Knight);
            JsonUtility.FromJsonOverwrite(originalResources, state.PlayerResources); state.PlayerAP.Current = originalAP;
            loadedField.SetValue(null, previousLoaded); didLoadField.SetValue(null, previousDidLoad);
            UnityEngine.Object.DestroyImmediate(root);
            UnitRegistry.Instance?.Unregister(actor);
            UnityEngine.Object.DestroyImmediate(model); UnityEngine.Object.DestroyImmediate(authoredUnit);
            UnityEngine.Object.DestroyImmediate(roleUnit); UnityEngine.Object.DestroyImmediate(fixtureBuilding);
            UnityEngine.Object.DestroyImmediate(fixtureCatalog);
            Check("user unit catalog contents are restored without saving assets", EditorJsonUtility.ToJson(catalog) == originalCatalog);
        }
    }
}
#endif
