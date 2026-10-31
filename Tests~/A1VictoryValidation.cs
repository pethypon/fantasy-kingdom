#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Controlled finishing position, not a natural-game balance/win-rate measurement.</summary>
[InitializeOnLoad]
public static class A1VictoryValidation
{
    const string Key = "FK.A1Victory";
    static bool clicked, prepared;
    static int startFrame, resultFrame = -1;
    static int initialAP, quotedAP;
    static double started;
    static Status target;
    static TurnGenerator turn;
    static readonly List<Status> occupied = new List<Status>();
    static A1VictoryValidation() => EditorApplication.update += Tick;
    public static void Run()
    {
        if (new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name != "UnityValidation")
            throw new InvalidOperationException("Isolated clone required");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "A1Victory_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!clicked)
            {
                var button = UIBuilder.ScreenCanvas?.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null) return;
                UnityEngine.Random.InitState(161803); button.onClick.Invoke();
                clicked = true; startFrame = Time.frameCount; started = EditorApplication.timeSinceStartup; return;
            }
            if (EditorApplication.timeSinceStartup - started > 60) throw new InvalidOperationException("Finishing position did not end within 60 seconds");
            if (turn == null) turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (turn == null || Time.frameCount - startFrame < 20) return;
            if (!prepared) { Prepare(); prepared = true; return; }
            if (!turn.IsGameOver) return;
            if (!(turn.CurrentState is GameEndState end) || target.HP > 0
                || !end.OutcomeReason.Contains("敵のメインクリスタル"))
                throw new InvalidOperationException("Unexpected end state: " + (turn.CurrentState as GameEndState)?.OutcomeReason);
            if (turn.Systems.FactionState.GetAP(Team.Player) != initialAP - quotedAP)
                throw new InvalidOperationException("Finishing attack did not pay the exact quoted AP");
            if (resultFrame < 0)
            {
                resultFrame = Time.frameCount;
                Debug.Log("[A1Victory] PASS actual Player AI chose and executed the observed finishing attack; " + end.OutcomeReason);
                Debug.Log("[A1Victory] PASS exact AP payment " + initialAP + " -> " + turn.Systems.FactionState.GetAP(Team.Player) + " quote=" + quotedAP);
                Debug.Log("[A1Victory] SCOPE controlled setup: a starting combat unit relocated to a legal attack lane, opposing main crystal HP=1/barrier=0. Normal damage/AP/Fog/selection/executor/end-state rules unchanged. This is not natural-match victory evidence.");
                string output = Environment.GetEnvironmentVariable("FK_A1_VICTORY_SCREENSHOT");
                if (!string.IsNullOrEmpty(output))
                {
                    const string name = "a1-victory-controlled.png";
                    VisualRegressionTests.CaptureHUD(name);
                    File.Copy(Path.GetFullPath(Path.Combine(Application.dataPath, "../../visual-captures", name)), output, true);
                }
            }
            if (Time.frameCount - resultFrame < 5) return;
            SessionState.SetBool(Key, false); Debug.Log("[A1Victory] COMPLETE"); EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            SessionState.SetBool(Key, false); Debug.LogException(e); EditorApplication.Exit(1);
        }
    }
    static void Prepare()
    {
        if (!(turn.CurrentState is PlayerMove)) throw new InvalidOperationException("Expected the normal starting player turn");
        var s = turn.Systems;
        target = s.CrystalSystem.Enemycrystal.GetComponentInChildren<Status>();
        if (target == null || target.kind != Kind.Crystal) throw new InvalidOperationException("Normal opposing main crystal missing");
        CombatRegistry.Collect(occupied);
        Status attacker = null;
        foreach (var actor in s.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>())
        {
            if (actor == null || !actor.IsAlive || actor.type != Type.Unit || MatchObjectiveRules.IsCommander(actor)
                || !AICapabilityRoles.For(actor).CanAttack) continue;
            foreach (var cell in s.MapCreate.SetPos)
            {
                bool blocked = false;
                foreach (var other in occupied)
                    if (other != actor && other != null && other.IsAlive && GridHelper.MatchXZ(other.transform.position, GridHelper.ToGridXZ(cell))) { blocked = true; break; }
                if (blocked || GridHelper.ChebyshevDistance(cell, target.transform.position) > 4) continue;
                var original = actor.transform.position; actor.transform.position = cell;
                if (s.MapCreate.CanAttackAcrossTerrain(actor, target.transform.position)
                    && AttackPatterns.CanAttack(actor.kind, actor.direction,
                        target.transform.position.x - cell.x, target.transform.position.z - cell.z, BoardActionProfile.For(actor)))
                {
                    s.VisionGenerator.RefreshActorVision(actor, s.MapCreate, s.CrystalSystem);
                    if (actor.VisionCell.Contains(GridHelper.ToGrid(target.transform.position))) { attacker = actor; break; }
                }
                actor.transform.position = original;
            }
            if (attacker != null) break;
        }
        if (attacker == null) throw new InvalidOperationException("No legal attack lane for an actual starting unit");
        target.HP = 1; target.Barrier = 0;
        s.MoveGenerator.UnitPointCore();
        s.VisionGenerator.MarkVisionDirty(); s.VisionGenerator.VisionPoint(s.MapCreate, s.MoveGenerator, s.CrystalSystem);
        if (!s.VisionGenerator.IsActorVisibleTo(Team.Player, target)) throw new InvalidOperationException("Target not revealed by official individual vision");
        initialAP = s.FactionState.GetAP(Team.Player);
        quotedAP = s.APSystem.CalcCost(APSystem.ActionType.Attack, attacker);
        turn.MarkDeveloperMatch(); turn.PlayerAI.SetEnabled(true, false);
        Debug.Log("[A1Victory] PREPARED actual Player AI enabled; attacker=" + attacker.kind + " AP=" + s.FactionState.GetAP(Team.Player));
    }
}
#endif
