#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEngine;

/// <summary>Native Editor contracts. Full turn/victory and rendered-UI acceptance require Play Mode separately.</summary>
public static class R2TimerUIRulesTests
{
    static int passed;
    static void Check(string label, bool value)
    {
        if (!value) throw new InvalidOperationException("[R2TimerUI] FAIL " + label);
        passed++; Debug.Log("[R2TimerUI] PASS " + label);
    }

    public static void Run()
    {
        passed = 0;
        var root = new GameObject("R2 legacy timer contracts"); root.SetActive(false);
        var definition = ScriptableObject.CreateInstance<UnitData>();
        try
        {
            var clock = root.AddComponent<TimerSystem>();
            int turnExpired = 0, matchExpired = 0;
            clock.OnTurnTimeExpired += () => turnExpired++;
            clock.OnTotalTimeExpired += _ => matchExpired++;
            clock.TurnTimeLimit = 180; clock.PlayerTotalTime = 36000; clock.EnemyTotalTime = 36000;
            clock.RestoreTurnTimeRemaining(180); clock.StartTurn(Team.Player);
            clock.Advance(181);
            Check("TM01 no forced player turn end after 180 seconds", turnExpired == 0 && clock.IsRunning);
            clock.Advance(601);
            Check("TM02 no forced player turn end after 600 seconds", turnExpired == 0 && clock.IsRunning);
            Check("elapsed time does not deduct legacy totals", clock.PlayerTotalTime == 36000 && clock.EnemyTotalTime == 36000);
            clock.PlayerTotalTime = 0; clock.EnemyTotalTime = 0; clock.RestoreTurnTimeRemaining(0);
            clock.ResumeTurn(Team.Player); clock.Advance(1000000);
            Check("TM03 zero legacy clocks do not end the game", matchExpired == 0 && clock.LastTimeUpResult == null);
            clock.StartTurn(Team.Enemy); clock.Advance(1000000);
            Check("TM04 no historical timeout callback for either side", turnExpired == 0 && matchExpired == 0);
            Check("no time-up result or reason is retained", clock.LastTimeUpResult == null && clock.TimeUpReasonText == string.Empty);
            Check("R2 advertises no gameplay clock limit", !clock.TimeLimitsEnabled);
            Check("AI receives no gameplay deadline", double.IsPositiveInfinity(clock.AvailableAIThinkingMilliseconds(Team.Player))
                && double.IsPositiveInfinity(clock.AvailableAIThinkingMilliseconds(Team.Enemy)));
            var policy = ScriptableObject.CreateInstance<AIThinkingBudgetConfig>();
            try
            {
                var bounded = policy.Decide(new AIThinkingBudgetFacts(true,true,true),Team.Enemy,1,
                    clock.AvailableAIThinkingMilliseconds(Team.Enemy));
                Check("TM08 no gameplay timer still keeps the finite technical limit", bounded.Milliseconds>0
                    &&bounded.Milliseconds<=AIThinkingBudgetConfig.MaximumMilliseconds
                    &&AITurnBudget.ClampCompletionDeadline(bounded.CompletionDeadlineMilliseconds)<=8500);
            }
            finally { UnityEngine.Object.DestroyImmediate(policy); }
            clock.StopTurn();
            Check("manual stop keeps the lifecycle seam and adds no clock bonus", !clock.IsRunning
                && clock.PlayerTotalTime == 0 && clock.EnemyTotalTime == 0);
            clock.StartTurn(Team.Player);
            Check("manual restart retains active team", clock.IsRunning && clock.CurrentTeam == Team.Player);
            clock.Advance(float.NaN); clock.Advance(float.PositiveInfinity); clock.Advance(-1);
            Check("invalid elapsed values cannot create a timeout", turnExpired == 0 && matchExpired == 0 && clock.IsRunning);
            Check("restoring invalid historical clock values is finite", Restore(clock));

            definition.kind = Kind.Knight; definition.baseHP = 3; definition.baseATK = 1;
            definition.displayName = "固定能力テスト兵";
            string tooltip = SummonUnitTooltip.Description(definition);
            Check("E03 summon tooltip contains no unit growth labels", !tooltip.Contains("Lv") && !tooltip.Contains("XP") && !tooltip.Contains("成長"));
            Check("E04 summon tooltip uses canonical fixed HP and ATK", tooltip.Contains("HP 5 / 5") && tooltip.Contains("攻撃力 2"));
            Check("E05 summon upkeep comes from the fixed API", tooltip.Contains("維持費 / 毎ターン")
                && tooltip.Contains(BuildSummonUIBuilder.FormatProductionBundle(definition.GetFixedUpkeep())));
            Check("R2 barrier explanation uses full HP rather than old damage-free threshold",
                SpecialSkillPresentation.BarrierRecoveryDescription().Contains("HPが最大")
                && !SpecialSkillPresentation.BarrierRecoveryDescription().Contains("70%")
                && !SpecialSkillPresentation.BarrierRecoveryDescription().Contains("無被弾"));
            PanelReflow(root);
            EngineerContext(root);
            MiningExplanation(root);
            Check("E01 no runtime level badge text cache remains", typeof(UnitHeadUI).GetField("lvText", BindingFlags.Instance | BindingFlags.NonPublic) == null
                && typeof(UnitHeadUI).GetField("cachedLevel", BindingFlags.Instance | BindingFlags.NonPublic) == null);
            Debug.Log("[R2TimerUI] RESULT checks=" + passed);
        }
        finally { UnityEngine.Object.DestroyImmediate(definition); UnityEngine.Object.DestroyImmediate(root); }
    }

    static bool Restore(TimerSystem clock)
    {
        clock.RestoreTurnTimeRemaining(float.NaN);
        return !float.IsNaN(clock.TurnTimeRemaining) && !float.IsInfinity(clock.TurnTimeRemaining);
    }

    static void PanelReflow(GameObject root)
    {
        var panel = root.AddComponent<UnitPanelUI>();
        TextMeshProUGUI Label(string name)
        {
            var child = new GameObject(name, typeof(RectTransform)); child.transform.SetParent(root.transform, false);
            return child.AddComponent<TextMeshProUGUI>();
        }
        var level = Label("LevelText"); var nameText = Label("NameText"); var hp = Label("HPText");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(UnitPanelUI).GetField("levelText", flags).SetValue(panel, level);
        typeof(UnitPanelUI).GetField("nameText", flags).SetValue(panel, nameText);
        typeof(UnitPanelUI).GetField("hpText", flags).SetValue(panel, hp);
        var reflow = typeof(UnitPanelUI).GetMethod("SetUnitInformationLayout", flags);
        level.text = "強化段階 3"; reflow.Invoke(panel, new object[] { false });
        Check("E02/E09 units hide the shared facility level and close the empty row", !level.gameObject.activeSelf
            && level.text.Length == 0 && Mathf.Approximately(hp.rectTransform.anchorMax.y, .68f));
        reflow.Invoke(panel, new object[] { true });
        Check("E08/E09 facility selection restores its upgrade row and original HP layout", level.gameObject.activeSelf
            && Mathf.Approximately(hp.rectTransform.anchorMax.y, .60f));
        reflow.Invoke(panel, new object[] { false });
        Check("E09 repeated facility-to-unit switching never leaves a stale row", !level.gameObject.activeSelf && level.text.Length == 0);
    }

    static void EngineerContext(GameObject root)
    {
        var menu = new BuildSummonUIBuilder(null);
        var actorObject = new GameObject("test-owned engineer"); actorObject.SetActive(false);
        var actor = actorObject.AddComponent<Status>(); actor.kind = Kind.Engineer; actor.type = Type.Unit;
        actor.team = Team.Player; actor.HP = actor.MaxHP = 1;
        try
        {
            actorObject.SetActive(true);
            Check("retired engineer cannot open a construction menu", !menu.UseEngineerConstruction(actor));
            UnityEngine.Object.DestroyImmediate(actorObject);
            Check("ordinary construction context remains active after rejected engineer request", !menu.UseNormalConstruction());
            Check("ordinary construction context switch is explicit and idempotent", !menu.UseNormalConstruction());
            Check("a null engineer cannot create a new remote construction context", !menu.UseEngineerConstruction(null));
        }
        finally { if (actorObject != null) UnityEngine.Object.DestroyImmediate(actorObject); }
    }

    static void MiningExplanation(GameObject root)
    {
        var territory = root.AddComponent<TerritorySystem>();
        territory.PTSetPos = new System.Collections.Generic.List<Vector3> { new Vector3(2, 0, 2) };
        territory.ETSetPos = new System.Collections.Generic.List<Vector3>();
        var mineObject = new GameObject("test-owned mine"); mineObject.SetActive(false); mineObject.transform.SetParent(root.transform, false);
        var mine = mineObject.AddComponent<Status>(); mine.type = Type.Building; mine.facilityKind = FacilityKind.Mine;
        mine.team = Team.Player; mine.Level = 1; mine.BuildingOperationAvailable = true;
        var method = typeof(UnitPanelUI).GetMethod("BuildingEffectText", BindingFlags.Static | BindingFlags.NonPublic);
        mine.transform.position = new Vector3(2, 0, 2);
        string inside = (string)method.Invoke(null, new object[] { mine, territory });
        Check("MN12 own-territory mine shows ordinary production", inside.Contains("採掘：稼働")
            && inside.Contains(FormatExpectedMineOutput(mine, territory)) && !inside.Contains("領土外"));
        mine.transform.position = new Vector3(3, 0, 2);
        string outside = (string)method.Invoke(null, new object[] { mine, territory });
        Check("MN12 existing mine presents effective recipe and operation state", outside.Contains("採掘：稼働")
            && outside.Contains(FormatExpectedMineOutput(mine, territory)) && !outside.Contains("領土外でのみ採掘可能"));
    }

    static string FormatExpectedMineOutput(Status mine, TerritorySystem territory)
        => BuildSummonUIBuilder.FormatProductionBundle(FacilityData.GetProductionLevel(mine, mine.Level, territory).Output);
}
#endif
