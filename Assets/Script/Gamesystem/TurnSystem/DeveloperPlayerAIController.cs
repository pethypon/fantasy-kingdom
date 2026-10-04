#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using UnityEngine;

/// <summary>Editor/development-only driver. Uses the ordinary player turn and legal AI actions.</summary>
public sealed class DeveloperPlayerAIController : IDisposable
{
    public const int ThreatLevel = 15;
    const string Hints = "開発者: プレイヤーAI 脅威度15　[F8]手動に戻す　[中ボタンドラッグ]カメラ移動";
    readonly TurnGenerator turn;
    AICommander commander;
    IEnumerator steps;
    PlayerMove owner;
    bool stepping, stopPending, menuPaused;
    float activeSeconds;
    public bool Enabled { get; private set; }
    public bool WasUsed { get; private set; }
    public AICommander Commander => commander;

    public DeveloperPlayerAIController(TurnGenerator value) { turn = value; }

    public void SetEnabled(bool enabled, bool notify = true)
    {
        if (enabled && (turn.IsGameOver || turn.Systems.UnitSetting == null || turn.CurrentState == null)) return;
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (enabled)
        {
            WasUsed = true; turn.MarkDeveloperMatch();
            if (turn.CurrentState is PlayerAttack attack)
            {
                attack.Reset();
                turn.ChangeState(attack.move);
            }
            ClearInteraction();
        }
        else
        {
            Stop();
            turn.Context.ConsumeUICommand();
            if (turn.CurrentState is PlayerMove)
                turn.Systems.InputHintUI?.SetHints(InputHintUI.Hints.PlayerMove);
        }
        if (notify) ToastMessageUI.Show(enabled ? "開発者用プレイヤーAI ON（脅威度15）" : "プレイヤーAI OFF・手動操作に戻りました", ToastMessageUI.MessageType.Info);
    }

    void ClearInteraction()
    {
        if (turn.CurrentState is PlayerMove move) move.Reset();
        turn.Systems.BuildSystem?.CancelBuildMode();
        turn.Systems.SummonSystem?.CancelSummonMode();
        turn.Systems.MoveGenerator?.MoveReset();
        turn.Systems.AttackGenerator?.AtkpDestroy();
        turn.Systems.UnitPanelUI?.Hide();
        turn.Context.ClearSelection();
        turn.Context.ConsumeUICommand();
    }

    public void PauseForMenu()
    {
        if (steps == null || menuPaused) return;
        menuPaused = true;
        AITurnBudget.SuspendWaitLimit();
    }

    public bool Tick(float deltaSeconds)
    {
        if (!Enabled || turn.IsGameOver) { Stop(); return false; }
        if (!(turn.CurrentState is PlayerMove move)) { Stop(); return false; }
        if (GameMenuUI.Instance != null && GameMenuUI.Instance.IsOpen) return true;
        if (menuPaused) { menuPaused = false; AITurnBudget.ResumeWaitLimit(); }
        turn.Context.ConsumeUICommand();
        turn.Systems.InputHintUI?.SetHints(Hints);
        if (steps == null)
        {
            ClearInteraction();
            var s = turn.Systems;
            if (commander == null)
                commander = new AICommander(turn, s.MoveGenerator, s.AttackGenerator, s.BattleSystem,
                    s.VisionGenerator, s.APSystem, s.UnitSetting, s.CrystalSystem, s.MapCreate,
                    MajorPersonality.Intellect, s.BuildSystem, s.SummonSystem, s.FactionState,
                    s.SkillSystem, s.SubCrystalSystem, ThreatLevel, 15015, Team.Player);
            commander.TurnThinkingBudgetMs = 3000;
            commander.SearchSliceBudgetMs = 3;
            owner = move;
            activeSeconds = 0;
            steps = commander.ExecuteTurnSteps();
        }
        activeSeconds += Mathf.Clamp(deltaSeconds, 0, 1);
        bool done = activeSeconds >= 12;
        try
        {
            if (!done)
            {
                stepping = true;
                AITurnBudget.Resume();
                done = !steps.MoveNext();
            }
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            SetEnabled(false);
        }
        finally
        {
            AITurnBudget.Pause();
            stepping = false;
            if (stopPending) Stop();
        }
        if (done && Enabled && turn.CurrentState == owner)
        {
            var endingMove = owner;
            Stop();
            endingMove.ExecuteTurnEnd();
        }
        return true;
    }

    public void OnStateChanging() { Stop(); }
    void Stop()
    {
        if (stepping) { stopPending = true; return; }
        stopPending = false;
        if (menuPaused) { menuPaused = false; AITurnBudget.ResumeWaitLimit(); }
        var current = steps;
        steps = null;
        owner = null;
        try { (current as IDisposable)?.Dispose(); }
        catch (Exception error) { Debug.LogException(error); }
    }
    public void RestoreUsedFlag(bool used) { WasUsed |= used; turn.MarkDeveloperMatch(used); }
    public void Dispose() { Enabled = false; Stop(); }
}
#endif
