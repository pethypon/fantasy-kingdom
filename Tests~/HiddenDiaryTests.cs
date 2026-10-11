#if UNITY_EDITOR
using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Real overlay regression: hidden costs, memory-only opening, bounded pages and UI-only failure.</summary>
public static class HiddenDiaryTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string label, bool result)
    {
        if (!result) throw new InvalidOperationException("[HiddenDiary] FAIL " + label);
        passed++; Debug.Log("[HiddenDiary] PASS " + label);
    }

    static AIActionContextSnapshot Facts(int step) => new AIActionContextSnapshot
    {
        Turn = step, Strategy = "Balanced", ActorRole = "Scout", ActorHP = 20, ActorHpRatio = 1,
        ActorAp = 30, ActorX = step, ActorY = 1, OwnUnitCount = 1,
        OwnCrystalHP = 15000, OwnCrystalHpRatio = 1, Wood = 100, Stone = 100, Bread = 100, Water = 100,
        EconomyState = EconomicState.Healthy, ExploredTiles = step, CurrentThreatLevel = 15, MatchPhase = "Early"
    };

    static void Action(AIActionReflectionSystem reflection, int number)
    {
        var before = Facts(number); var after = Facts(number + 1);
        var action = new AIAction { ActionType = AIActionType.Move };
        var token = reflection.BeginAction(action, before);
        reflection.SetSelectedScores(token, 20, 1, 21);
        reflection.CompleteAction(token, true, after);
    }

    public static void PlayMode(GameSystems systems, TurnGenerator turn)
    {
        passed = 0;
        var commander = systems.AICommander;
        var reflectionField = typeof(AICommander).GetField("reflection", Private);
        var previous = reflectionField.GetValue(commander);
        var unavailableField = typeof(AICommander).GetField("reflectionUnavailable", Private);
        var previousUnavailable = unavailableField.GetValue(commander);
        var config = ScriptableObject.CreateInstance<AIReflectionConfig>();
        config.MaxRecentRecords = 256; config.EnableFileDiary = config.EnableConsoleDiary = false;
        var performance = AIPerformanceConfig.Active;
        int oldEvents = performance.MaxVisibleDiaryEvents, oldCharacters = performance.MaxDiaryVisibleCharacters;
        try
        {
            AIDiaryUI.Close(); Check("hidden at entry", !AIDiaryUI.IsOpen);
            var reflection = new AIActionReflectionSystem(Team.Enemy, config, persistent: false);
            reflection.BeginBattle("HiddenDiary", 15); reflection.BeginTurn(1);
            reflectionField.SetValue(commander, reflection); unavailableField.SetValue(commander, false);
            int rebuilds = AIDiaryUI.RebuildCount, layouts = AIDiaryUI.LayoutRebuildCount;
            long writes = AIPerformanceCounters.FileWriteCount;
            for (int i = 1; i <= 100; i++) { Action(reflection, i); AIDiaryUI.Tick(); }
            Check("100 hidden actions make zero diary text rebuilds", AIDiaryUI.RebuildCount == rebuilds);
            Check("100 hidden actions make zero diary layout rebuilds", AIDiaryUI.LayoutRebuildCount == layouts);
            Check("hidden actions retain real structured learning", reflection.DiaryEntryCount == 100 && reflection.KnowledgeCount > 0);
            Check("100 actions cause no file writes", AIPerformanceCounters.FileWriteCount == writes);

            long reads = AIPerformanceCounters.FileReadCount;
            performance.MaxVisibleDiaryEvents = 10; performance.MaxDiaryVisibleCharacters = 256;
            AIDiaryUI.Toggle(turn);
            Check("toggle opens the reused memory-only overlay", AIDiaryUI.IsOpen && AIPerformanceCounters.FileReadCount == reads);
            var overlay = UIBuilder.ScreenCanvas.transform.Find("AIDiaryOverlay");
            var group = overlay.GetComponent<CanvasGroup>();
            Check("open overlay accepts input", group.interactable && group.blocksRaycasts);
            Check("event and TMP character budgets are applied", AIDiaryUI.DisplayedEventCount <= 10
                && AIDiaryUI.DisplayedCharacterCount <= 256 && AIDiaryUI.PageCount > 1);
            int unchangedRebuilds = AIDiaryUI.RebuildCount;
            for (int i = 0; i < 100; i++) AIDiaryUI.Tick();
            Check("unchanged snapshot makes zero extra text rebuilds", AIDiaryUI.RebuildCount == unchangedRebuilds);

            var snapshot = reflection.CaptureDiarySnapshot(0, 10);
            Check("newest event page is bounded and ordered", snapshot.TotalEvents == 100 && snapshot.Events.Count == 10
                && snapshot.Events[0].ActionId == 91 && snapshot.Events[9].ActionId == 100 && snapshot.PageCount == 10);
            var oldPage = reflection.CaptureDiarySnapshot(9, 10);
            Check("older events are accessible without disk", oldPage.Events[0].ActionId == 1 && oldPage.Events[9].ActionId == 10);
            float capturedReward = snapshot.Events[9].Reward;
            reflection.LastRecord.Reward = -99;
            Check("UI values cannot mutate or track a mutable learning record", snapshot.Events[9].Reward == capturedReward);
            reflection.LastRecord.Reward = capturedReward;

            var slice = typeof(AIDiaryUI).GetMethod("PageSlice", BindingFlags.Static | BindingFlags.NonPublic);
            string unicode = new string('a', 254) + char.ConvertFromUtf32(0x1F30F) + new string('b', 520);
            string rebuilt = "";
            int count = (unicode.Length + 254) / 255;
            for (int i = 0; i < count; i++)
            {
                string page = (string)slice.Invoke(null, new object[] { unicode, i, 256 });
                Check("unicode page respects the exact TMP budget " + i, page.Length <= 256);
                rebuilt += page;
            }
            Check("character pagination preserves surrogate pairs and all text", rebuilt == unicode);

            AIDiaryUI.Toggle(turn);
            Check("toggle closes without destroying the root", !AIDiaryUI.IsOpen && overlay != null && !overlay.gameObject.activeSelf);
            Check("closed overlay releases raycasts", !group.interactable && !group.blocksRaycasts);
            Check("closed overlay clears retained snapshots", typeof(AIDiaryUI).GetField("snapshot", Private).GetValue(overlay.GetComponent<AIDiaryUI>()) == null);
            for (int battle = 0; battle < 10; battle++) { AIDiaryUI.Show(turn); AIDiaryUI.Close(); }
            int roots = 0; foreach (Transform child in UIBuilder.ScreenCanvas.transform) if (child.name == "AIDiaryOverlay") roots++;
            Check("ten open-close cycles retain exactly one scene root", roots == 1);

            AIDiaryUI.Show(turn);
            var ui = overlay.GetComponent<AIDiaryUI>();
            var bodyField = typeof(AIDiaryUI).GetField("body", Private); var text = bodyField.GetValue(ui);
            bodyField.SetValue(ui, null);
            overlay.Find("AIDiaryPanel/DiaryControls/RefreshDiary").GetComponent<Button>().onClick.Invoke();
            bodyField.SetValue(ui, text);
            Check("broken diary view closes only itself", !AIDiaryUI.IsOpen);
            long beforeAction = reflection.LastRecord.ActionId; Action(reflection, 101);
            Check("AI reflection and learning continue after UI exception", reflection.LastRecord.ActionId == beforeAction + 1);
            Check("failure and reopening still read zero files", AIPerformanceCounters.FileReadCount == reads);
        }
        finally
        {
            AIDiaryUI.Close(); reflectionField.SetValue(commander, previous); unavailableField.SetValue(commander, previousUnavailable);
            performance.MaxVisibleDiaryEvents = oldEvents; performance.MaxDiaryVisibleCharacters = oldCharacters;
            UnityEngine.Object.DestroyImmediate(config);
        }
        Debug.Log("[HiddenDiary] " + passed + " hidden/render/page/failure checks passed");
    }
}
#endif
