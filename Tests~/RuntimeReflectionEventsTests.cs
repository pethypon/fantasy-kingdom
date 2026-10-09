#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Real event/save/result/UI routes, isolated from the live match and persistent profiles.</summary>
public static class RuntimeReflectionEventsTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string label, bool result)
    {
        if (!result) throw new InvalidOperationException("[RuntimeReflection] FAIL " + label);
        passed++; Debug.Log("[RuntimeReflection] PASS " + label);
    }
    static bool Near(float first, float second) => Mathf.Abs(first - second) < .001f;
    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        MigrationAndPhase(systems);
        StrategyStatistics();
        RealDamageResultAndViewer(systems);
        Debug.Log("[RuntimeReflection] " + passed + " runtime continuation/event/UI checks passed");
    }

    static void MigrationAndPhase(GameSystems systems)
    {
        var migrate = typeof(SaveSystem).GetMethod("RunMigrations", BindingFlags.Static | BindingFlags.NonPublic);
        var legacy = new SaveSystem.GameSaveData { Version = 3, Turn = 8, AI = null,
            PlayerNationExtra = null, EnemyNationExtra = null };
        migrate.Invoke(null, new object[] { legacy });
        Check("v3 migration fills current format without invented AI experience", legacy.Version == SaveSystem.CurrentSaveVersion && legacy.AI != null
            && legacy.AI.Reflection == null && legacy.PlayerNationExtra.TurnsAlive == 7 && legacy.EnemyNationExtra.TurnsAlive == 7);
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            var extra = new SaveSystem.NationExtraSaveData { TurnsAlive = 23 };
            extra = JsonUtility.FromJson<SaveSystem.NationExtraSaveData>(JsonUtility.ToJson(extra));
            SaveSystem.RestoreNationExtra(extra, f.State.GetNation(f.Team));
            var recipe = new FacilityData.FacilityLevelData { ProductionIntervalTurns = 3 };
            Check("v4 load retains an off-cycle production phase", f.State.GetNation(f.Team).TurnsAlive == 23
                && !FacilityData.IsProductionTurn(f.State.GetNation(f.Team).TurnsAlive, recipe));
            f.State.GetNation(f.Team).TurnsAlive++;
            Check("v4 load resumes the next due production turn", FacilityData.IsProductionTurn(f.State.GetNation(f.Team).TurnsAlive, recipe));
            var current = new SaveSystem.GameSaveData { Version = 4, Turn = 80, PlayerNationExtra = extra,
                EnemyNationExtra = new SaveSystem.NationExtraSaveData { TurnsAlive = 21 } };
            migrate.Invoke(null, new object[] { current });
            Check("v4 migration is idempotent and does not replace explicit phase with global turn",
                current.PlayerNationExtra.TurnsAlive == 23 && current.EnemyNationExtra.TurnsAlive == 21);
        }
    }

    static void StrategyStatistics()
    {
        var config = ScriptableObject.CreateInstance<AIReflectionConfig>();
        config.EnableConsoleDiary = config.EnableFileDiary = false;
        try
        {
            var reflection = new AIActionReflectionSystem(Team.Enemy, config, null, false);
            reflection.BeginBattle("OperationWin", 15);
            reflection.BeginTurn(1, null, TurnStrategy.EconomyBuild, 15); reflection.BeginTurn(1, null, TurnStrategy.EconomyBuild, 15);
            reflection.BeginTurn(2, null, TurnStrategy.Assault, 15);
            var snapshot = new AIActionContextSnapshot { Turn = 2, EconomyState = EconomicState.Warning };
            reflection.EndBattle(true, snapshot); reflection.EndBattle(true, snapshot);
            var outcomes = reflection.Profile.StrategyOutcomes;
            Check("victory strategy statistics share battle-level +100", outcomes.Count == 2
                && Near(outcomes.Sum(item => item.CumulativeBattleReward), 100) && outcomes.All(item => item.Battles == 1 && item.Wins == 1));
            Check("duplicate BeginTurn does not inflate strategic own turns", outcomes.Sum(item => item.OwnTurns) == 2);
            var draw = new AIActionReflectionSystem(Team.Enemy, config, null, false);
            draw.BeginBattle("OperationDraw", 15); draw.BeginTurn(1, null, TurnStrategy.CrystalDefense, 15);
            var continuation = JsonUtility.FromJson<AIReflectionBattleState>(JsonUtility.ToJson(draw.CaptureBattleState()));
            var resumed = new AIActionReflectionSystem(Team.Enemy, config, null, false);
            resumed.BeginBattle(resume: continuation); resumed.EndDrawBattle(snapshot);
            Check("draw strategic result survives continuation and has no victory reward", resumed.Profile.StrategyOutcomes.Count == 1
                && resumed.Profile.StrategyOutcomes[0].Draws == 1 && Near(resumed.Profile.StrategyOutcomes[0].CumulativeBattleReward, 0));
            var repository = new AIReflectionSaveRepository(System.IO.Path.GetTempPath(), "StrategyBounds", config);
            var malformed = new AIActionLearningProfile { ProfileId = "StrategyBounds" };
            for (int i = 0; i < 33; i++) malformed.StrategyOutcomes.Add(new AIReflectionStrategyOutcome());
            Check("malformed learning strategy list is rejected before saving", !repository.IsValid(malformed));
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }

    static void RealDamageResultAndViewer(GameSystems systems)
    {
        using (var f = new EconomyR2Tests.Fixture(systems))
        {
            var config = ScriptableObject.CreateInstance<AIReflectionConfig>();
            config.EnableConsoleDiary = config.EnableFileDiary = false;
            var hubObject = new GameObject("Reflection event isolated turn"); hubObject.SetActive(false);
            var hub = hubObject.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch(); hub.Context.Turn = 7;
            var cache = typeof(Status).GetField("_cachedTurnGenerator", BindingFlags.Static | BindingFlags.NonPublic);
            object previousHub = cache.GetValue(null);
            var canvas = UIBuilder.ScreenCanvas;
            var originalOverlays = new HashSet<GameObject>(); foreach (Transform child in canvas.transform) originalOverlays.Add(child.gameObject);
            int originalStatTurn = MatchStats.Instance != null ? MatchStats.Instance.TurnsPlayed : 0;
            try
            {
                cache.SetValue(null, hub);
                hub.Systems.MoveGenerator = systems.MoveGenerator; hub.Systems.AttackGenerator = systems.AttackGenerator;
                hub.Systems.CrystalSystem = f.Crystals; hub.Systems.UnitSetting = f.Units; hub.Systems.FactionState = f.State;
                var commander = new AICommander(hub, systems.MoveGenerator, systems.AttackGenerator, systems.BattleSystem,
                    null, f.AP, f.Units, f.Crystals, systems.MapCreate, MajorPersonality.Combat, f.Builder,
                    null, f.State, systems.SkillSystem, null, 15, 1211, Team.Enemy);
                hub.Systems.AICommander = commander;
                var reflection = new AIActionReflectionSystem(Team.Enemy, config, null, false);
                reflection.BeginBattle("RealResult", 15); typeof(AICommander).GetField("reflection", Private).SetValue(commander, reflection);
                var own = f.Unit(); var target = f.Opponent(f.AdjacentCells()[2], 1); target.HP = target.MaxHP = 20;
                string lifeId = target.ReflectionLifeId;
                var saved = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(target)));
                var restored = f.Opponent(f.AdjacentCells()[2], 1); SaveGameApplier.ApplyStatusFields(restored, saved);
                Check("save/load preserves life identity for event deduplication", restored.ReflectionLifeId == lifeId);
                var board = f.Board(7); board.AlivePlayerUnits.Add(target);
                typeof(AICommander).GetField("_board", Private).SetValue(commander, board);
                commander.SaveTurnCount = 7; reflection.BeginTurn(7, board, TurnStrategy.Assault, 15);
                var action = new AIAction { ActionType = AIActionType.Attack, Unit = own, TargetUnit = target, TargetPos = target.transform.position };
                var token = reflection.BeginAction(action, board);
                int actualDamage = target.ApplyDamage(100);
                Status.AwardDamageExperience(own, target, actualDamage, f.State);
                reflection.OnEnemyKilled(lifeId); // A second death route must not duplicate the authoritative event.
                Check("real damage/XP hook records one authoritative kill", actualDamage == 20 && own.Experience >= 20
                    && reflection.CaptureBattleState().TotalKills == 1);
                hub.ChangeState(new GameEndState(hub, GameResult.Lose, target));
                Check("actual GameEndState defers diary until the finishing action is complete", hub.IsGameOver
                    && reflection.HasPendingActions && reflection.DiaryCount == 0);
                board.Refresh(); reflection.CompleteAction(token, true, board);
                Check("actual match-end path awards battle +100 and leaves finishing action at +1",
                    Near(reflection.BattleReward, 101) && Near(reflection.LastRecord.Reward, 1)
                    && reflection.DiaryCount == 1 && reflection.LastRecord.Outcome.EnemyKills == 1);
                target.gameObject.SetActive(false); target.HP = target.MaxHP; target.gameObject.SetActive(true);
                Check("pooled actor receives a distinct next life rather than duplicate death identity", target.ReflectionLifeId != lifeId);
                CheckDiaryViewer(hub, reflection);
            }
            finally
            {
                AIDiaryUI.Close();
                var generated = new List<GameObject>(); foreach (Transform child in canvas.transform)
                    if (!originalOverlays.Contains(child.gameObject) && (child.name == "GameEndOverlay" || child.name == "AIDiaryOverlay")) generated.Add(child.gameObject);
                foreach (var overlay in generated) UnityEngine.Object.DestroyImmediate(overlay);
                cache.SetValue(null, previousHub);
                if (MatchStats.Instance != null) MatchStats.Instance.TurnsPlayed = originalStatTurn;
                UnityEngine.Object.DestroyImmediate(hubObject); UnityEngine.Object.DestroyImmediate(config);
            }
        }
    }

    static Rect Bounds(RectTransform child, RectTransform parent)
    {
        var corners = new Vector3[4]; child.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners) { var local = (Vector2)parent.InverseTransformPoint(corner); min = Vector2.Min(min, local); max = Vector2.Max(max, local); }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    static bool Fits(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1 && inner.yMin >= outer.yMin - 1
        && inner.xMax <= outer.xMax + 1 && inner.yMax <= outer.yMax + 1;
    static void CheckDiaryViewer(TurnGenerator hub, AIActionReflectionSystem reflection)
    {
        var canvas = UIBuilder.ScreenCanvas;
        var result = canvas.transform.Find("GameEndOverlay/GameEndPanel");
        Check("result screen retains four controls including the diary button", result != null && result.GetComponentsInChildren<Button>().Length == 4
            && result.GetComponentsInChildren<TextMeshProUGUI>().Any(text => text.text == "AI日記を見る"));
        AIDiaryUI.Show(hub); Canvas.ForceUpdateCanvases();
        var overlay = canvas.transform.Find("AIDiaryOverlay"); var panel = (RectTransform)overlay.Find("AIDiaryPanel");
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel); Canvas.ForceUpdateCanvases();
        Check("diary panel fits inside the current screen", AIDiaryUI.IsOpen && Fits(((RectTransform)canvas.transform).rect,
            Bounds(panel, (RectTransform)canvas.transform)));
        var scroll = panel.GetComponentInChildren<ScrollRect>(); var text = scroll.content.GetComponent<TextMeshProUGUI>();
        Check("diary displays the latest structured game result without interpreting rich text", text.text == reflection.LatestDiaryText
            && !text.richText && text.fontSize >= 20 && scroll.vertical && !scroll.horizontal && scroll.viewport.GetComponent<RectMask2D>() != null);
        Check("diary reading region and scrollbar are usable", scroll.viewport.rect.width > 300 && scroll.viewport.rect.height > 100
            && scroll.verticalScrollbar.GetComponent<RectTransform>().rect.width >= 30);
        foreach (string name in new[] { "DiaryScroll", "DiaryStorageLocations", "DiaryControls", "DiarySources" })
            Check("diary section fits its frame: " + name, Fits(panel.rect, Bounds((RectTransform)panel.Find(name), panel)));
        var controls = panel.Find("DiaryControls");
        foreach (var button in controls.GetComponentsInChildren<Button>())
            Check("diary footer button stays inside its frame: " + button.name,
                Fits(((RectTransform)controls).rect, Bounds((RectTransform)button.transform, (RectTransform)controls)) && button.interactable);
        var bottom = controls.Find("DiaryBottom").GetComponent<Button>(); bottom.onClick.Invoke();
        Check("diary can jump to the end", Near(scroll.verticalNormalizedPosition, 0));
        controls.Find("DiaryTop").GetComponent<Button>().onClick.Invoke();
        Check("diary can jump back to the beginning", Near(scroll.verticalNormalizedPosition, 1));
        controls.Find("CloseDiary").GetComponent<Button>().onClick.Invoke();
        Check("diary close control releases its modal state", !AIDiaryUI.IsOpen);
    }
}
#endif
