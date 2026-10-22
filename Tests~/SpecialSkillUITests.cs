#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public static class SpecialSkillUITests
{
    static int passed;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(string label, bool condition)
    { if (!condition) throw new Exception("[SpecialSkillUI] FAIL " + label); passed++; Debug.Log("[SpecialSkillUI] PASS " + label); }
    static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
    static SpecialSkillSelectionUI Current => (SpecialSkillSelectionUI)typeof(SpecialSkillSelectionUI)
        .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        var created = new List<UnityEngine.Object>();
        var previousSelection = systems.TurnGenerator.Context.SelectUnit;
        int previousAP = systems.FactionState.GetAP(Team.Player);
        try
        {
            var definition = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); created.Add(definition);
            definition.ability = UnitSpecialSkill.MagicRitual; definition.apCost = 1; definition.cooldown = 0; definition.sacrificeLimit = 12;
            var data = ScriptableObject.CreateInstance<UnitData>(); created.Add(data); data.specialSkill = definition;
            Status Actor(string name, Team team, int x, int z)
            {
                var go = new GameObject(name); created.Add(go);
                var unit = go.AddComponent<Status>(); unit.team = team; unit.type = Type.Unit; unit.kind = Kind.Knight;
                unit.HP = unit.MaxHP = 100; unit.transform.position = new Vector3(x, 1, z); unit.VisionCell = new HashSet<Vector3Int>();
                CombatRegistry.Register(unit); return unit;
            }
            var caster = Actor("UI ritual caster", Team.Player, 1000, 1000); caster.kind = Kind.Magic; caster.GrowthData = data;
            var visibleA = Actor("UI visible target A", Team.Enemy, 1003, 1003);
            var visibleB = Actor("UI visible target B", Team.Enemy, 1004, 1003);
            var hidden = Actor("UI hidden target", Team.Enemy, 1005, 1003);
            caster.VisionCell.Add(visibleA.GridPosition); caster.VisionCell.Add(visibleB.GridPosition);
            for (int index = 0; index < 8; index++) Actor("UI sacrifice " + index, Team.Player, 998 + index % 4, 998 + index / 4);
            systems.FactionState.SetAP(Team.Player, 30);
            systems.TurnGenerator.Context.SelectUnit = caster;
            caster.AssignedSkillId = 1;
            systems.UnitPanelUI.Show(caster);
            var skill = systems.UnitPanelUI.transform.Find("RightCommands/SkillBtn").gameObject;
            var special = systems.UnitPanelUI.transform.Find("RightCommands/SpecialSkillBtn").gameObject;
            Check("fixed special skill replaces historical skill action", !skill.activeSelf && special.activeSelf);
            var attackRect = systems.UnitPanelUI.transform.Find("RightCommands/AttackBtn").GetComponent<RectTransform>();
            var specialRect = special.GetComponent<RectTransform>();
            var waitRect = systems.UnitPanelUI.transform.Find("RightCommands/WaitBtn").GetComponent<RectTransform>();
            Check("three command rows do not overlap", attackRect.anchorMin.y >= specialRect.anchorMax.y && specialRect.anchorMin.y >= waitRect.anchorMax.y);

            SpecialSkillSelectionUI.Show(caster, systems);
            var modal = Current;
            var overlay = Read<GameObject>(modal, "overlay"); var canvas = overlay.GetComponent<Canvas>();
            Check("modal renders and receives input above independent HUD canvases", canvas != null && canvas.overrideSorting
                && canvas.sortingOrder > 200 && overlay.GetComponent<GraphicRaycaster>() != null);
            Check("modal includes both legal visible enemies only", Read<Dictionary<Status, Image>>(modal, "targetImages").Count == 2);
            var options = Read<Dictionary<Status, TextMeshProUGUI>>(modal, "sacrificeLabels");
            Check("all nine legal sacrifices remain available beyond AI representative subsets", options.Count == 9);
            foreach (var option in options)
                option.Value.GetComponentInParent<Button>().onClick.Invoke();
            Check("player can select every legal sacrifice", Read<List<Status>>(modal, "sacrifices").Count == 9);
            Read<Dictionary<Status, Image>>(modal, "targetImages")[visibleA].GetComponent<Button>().onClick.Invoke();
            Check("legal selected target enables confirmation", Read<Button>(modal, "confirm").interactable);
            definition.sacrificeLimit = 1;
            typeof(SpecialSkillSelectionUI).GetMethod("RefreshSelection", Private).Invoke(modal, null);
            Check("changed authoring limit invalidates pending selection", !Read<Button>(modal, "confirm").interactable);
            definition.apCost = -1;
            typeof(SpecialSkillSelectionUI).GetMethod("RefreshSelection", Private).Invoke(modal, null);
            Check("unconfigured AP explains refusal without executing", !Read<Button>(modal, "confirm").interactable && Read<TextMeshProUGUI>(modal, "feedback").text.Contains("未設定"));
            definition.apCost = 1; definition.cooldown = -1;
            Check("unconfigured cooldown also refuses use", !SpecialSkillRuntime.CanUse(caster, systems, out var cooldownReason) && cooldownReason.Contains("未設定"));

            foreach (UnitSpecialSkill ability in Enum.GetValues(typeof(UnitSpecialSkill)))
                Check("Japanese ability name " + ability, ability == UnitSpecialSkill.None || SpecialSkillPresentation.Name(ability) != ability.ToString());
            caster.Special.ritualTargetId = "missing ritual target"; caster.Special.storedSacrificeDamage = 45;
            caster.Special.ritualFireTurn = 2; caster.Special.ownerTurn = 1; caster.Special.nextDamageDouble = true;
            caster.Special.damageNullTurns = 2; caster.Special.damageNullUnused = true;
            caster.Special.guardianPartnerId = visibleA.ReflectionLifeId; caster.Special.bombCasterId = caster.ReflectionLifeId; caster.Special.bombDueRound = 8;
            string details = SpecialSkillPresentation.Details(caster, 3);
            Check("details display ritual amount and reservation", details.Contains("供儀合計：45") && details.Contains("自軍1ターン"));
            Check("details show persistent double damage and one hit protection", details.Contains("被弾まで持続") && details.Contains("1回無効"));
            Check("details show life link and maximum HP based bomb", details.Contains("生命接続先") && details.Contains("あと5ターン") && details.Contains("予定威力：80"));
            Check("missing saved target displays safely", details.Contains("対象不在"));
            var ordinary = Actor("UI ordinary unit", Team.Player, 1010, 1010);
            systems.UnitPanelUI.Show(ordinary);
            Check("ordinary actors restore original two row layout", !special.activeSelf && waitRect.anchorMax.y == .5f);
            definition.ability = UnitSpecialSkill.CrossbowStakes; definition.lightningFactionMask = SpecialFactionMask.Hostile; definition.crossbowInitialAmmo = 0;
            caster.Special = new SpecialState { ownerTurn = 1, stakeAId = ordinary.ReflectionLifeId, stakeBId = hidden.ReflectionLifeId, stakeAAppliedTurn = 1, stakeBAppliedTurn = 1 };
            var world = systems.UnitPanelUI.GetComponent<SpecialSkillWorldView>();
            void DrawLines() { typeof(SpecialSkillWorldView).GetField("nextRefresh", Private).SetValue(world, 0f); typeof(SpecialSkillWorldView).GetMethod("LateUpdate", Private).Invoke(world, null); }
            DrawLines();
            var worldViews = (System.Collections.IDictionary)typeof(SpecialSkillWorldView).GetField("views", Private).GetValue(world);
            Check("hidden stake endpoint never produces a revealing line", !worldViews.Contains(caster));
            hidden.team = Team.Player; DrawLines();
            Check("both observable endpoints display the actual lightning pair", worldViews.Contains(caster));
            hidden.team = Team.Enemy; DrawLines();
            Check("losing endpoint visibility removes the line immediately", !worldViews.Contains(caster));
            data.baseHP = 90; data.baseATK = 12; data.baseDEF = 8; data.costWood = 17; data.costAP = 6;
            data.useAuthoredUpkeep = true; data.upkeepIron = 4; data.upkeepBread = 2;
            data.useAuthoredAbilities = true; data.authoredSkillId = 1; data.authoredPassive = PassiveSkill.Impregnable;
            data.authoredSpecialAbility = SpecialAbility.Tenacity;
            string tooltipText = SummonUnitTooltip.Description(data);
            Check("summon hover uses authored level-one stats and production costs without obsolete DEF", tooltipText.Contains("HP 90") && tooltipText.Contains("攻撃力 12") && !tooltipText.Contains("防御力") && tooltipText.Contains("木材 17") && tooltipText.Contains("AP 6"));
            Check("summon hover uses canonical upkeep and fixed ability instead of a legacy random choice", tooltipText.Contains(BuildSummonUIBuilder.FormatProductionBundle(data.GetUpkeep(1)))
                && tooltipText.Contains("<b>スキル</b>") && tooltipText.Contains("<b>アビリティ</b>")
                && tooltipText.Contains(SpecialSkillPresentation.Name(data.specialSkill)) && !tooltipText.Contains(SkillData.Table[1].Name));
            var scrollGo = new GameObject("Tooltip lifecycle fixture", typeof(RectTransform), typeof(ScrollRect)); created.Add(scrollGo);
            scrollGo.transform.SetParent(UIBuilder.ScreenCanvas.transform, false);
            var tooltip = scrollGo.AddComponent<SummonUnitTooltip>(); tooltip.Definition = data;
            var eventData = new PointerEventData(EventSystem.current) { position = new Vector2(320, 500) };
            tooltip.OnPointerEnter(eventData);
            var popup = UIBuilder.ScreenCanvas.transform.Find("SummonUnitDetailsTooltip").gameObject;
            Check("hover popup belongs to real canvas and blocks no commands", popup.activeSelf && !popup.GetComponent<CanvasGroup>().blocksRaycasts);
            scrollGo.GetComponent<ScrollRect>().onValueChanged.Invoke(Vector2.one);
            Check("scroll immediately hides summon tooltip", !popup.activeSelf);
            tooltip.OnPointerEnter(eventData); tooltip.enabled = false;
            Check("disabled row cannot leave a tooltip behind", !popup.activeSelf);
            tooltip.enabled = true; tooltip.OnPointerEnter(eventData); SummonUnitTooltip.HideCurrent();
            Check("closing panel explicitly dismisses shared tooltip", !popup.activeSelf);
            string fullDetails = SpecialSkillPresentation.Details(caster, 3);
            Check("status details show fixed skill and ability without a random skill section", fullDetails.Contains("固有スキル") && fullDetails.Contains("固有アビリティ") && !fullDetails.Contains("通常スキル"));
            SpecialSkillSelectionUI.CloseCurrent();
            definition.ability = UnitSpecialSkill.BerserkerRage; definition.cooldown = 0; definition.ApplySpecifiedAPCost();
            definition.berserkerAttackCurve = AnimationCurve.Linear(0, 1, 5, 2.5f);
            definition.berserkerDefenceCurve = AnimationCurve.Linear(0, 1, 5, .5f);
            definition.berserkerAttackAPCurve = AnimationCurve.Linear(0, 1, 1, 5);
            caster.Special.berserkerStage = 3;
            systems.FactionState.SetAP(Team.Player, 16);
            SpecialSkillSelectionUI.Show(caster, systems); modal = Current;
            Check("Berserker modal previews only the next stage without stage-choice buttons", Read<Dictionary<Status, Image>>(modal, "targetImages").Count == 0
                && Read<int>(modal, "berserkerStage") == 4 && Read<TextMeshProUGUI>(modal, "ragePreview").text.Contains("現在3段階 → 次は4段階"));
            Check("Berserker next-stage preview displays its exact AP before confirming", Read<int>(modal, "berserkerStage") == 4
                && Read<Button>(modal, "confirm").interactable && Read<TextMeshProUGUI>(modal, "feedback").text.Contains("使用AP：15"));
            systems.FactionState.SetAP(Team.Player, 14);
            typeof(SpecialSkillSelectionUI).GetMethod("RefreshSelection", Private).Invoke(modal, null);
            Check("AP changes invalidate a pending Berserker increment", !Read<Button>(modal, "confirm").interactable);
            systems.FactionState.SetAP(Team.Player, 20);caster.Special.berserkerStage=4;
            typeof(SpecialSkillSelectionUI).GetMethod("RefreshSelection", Private).Invoke(modal, null);
            Check("a changed Berserker state invalidates the old pending increment", !Read<Button>(modal, "confirm").interactable
                && Read<TextMeshProUGUI>(modal, "feedback").text.Contains("段階が変わりました"));
            SpecialSkillSelectionUI.CloseCurrent();caster.Special.berserkerStage=5;SpecialSkillSelectionUI.Show(caster,systems);modal=Current;
            Check("maximum Berserker stage disables another skill use", !Read<Button>(modal,"confirm").interactable
                &&Read<TextMeshProUGUI>(modal,"ragePreview").text.Contains("最大"));
            definition.ability = UnitSpecialSkill.NinjaSwap; definition.ninjaChargeCurve = AnimationCurve.Linear(0, 1, 5, 2);
            Check("Ninja skill and passive descriptions remain separate", SpecialSkillPresentation.Description(definition).Contains("マップ上")
                && !SpecialSkillPresentation.Description(definition).Contains("クナイ") && SpecialSkillPresentation.PassiveDescription(definition).Contains("クナイ"));
            definition.ability = UnitSpecialSkill.NavigatorPull; definition.navigatorAPDamageCurve = AnimationCurve.Linear(0, 1, APSystem.MaximumAP, 1.5f);
            Check("Navigator skill and passive descriptions remain separate", SpecialSkillPresentation.Description(definition).Contains("強制移動")
                && !SpecialSkillPresentation.Description(definition).Contains("残りAP") && SpecialSkillPresentation.PassiveDescription(definition).Contains("残りAP"));
            Debug.Log("[SpecialSkillUI] SUMMARY passed=" + passed);
        }
        finally
        {
            SpecialSkillSelectionUI.CloseCurrent();
            systems.UnitPanelUI.Hide();
            systems.TurnGenerator.Context.SelectUnit = previousSelection;
            systems.FactionState.SetAP(Team.Player, previousAP);
            for (int index = created.Count - 1; index >= 0; index--)
                if (created[index] != null) UnityEngine.Object.DestroyImmediate(created[index]);
        }
    }
}
#endif
