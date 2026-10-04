#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class TurnCycleRunner
{
 static GameSystems systems;static TurnGenerator turn;static bool initialized,waiting;static int round,lastFrame=-1,frames,startTurn;static double started;static StateCore oldState;
 static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 sealed class FaultActions : System.Collections.IEnumerator, IDisposable {
  readonly int mode; public int Disposals; public FaultActions(int mode){this.mode=mode;} public object Current=>null;
  public bool MoveNext(){if(mode==4)throw new Exception("[TurnCycle] injected MoveNext failure");return mode==3;}
  public void Reset(){throw new NotSupportedException();}
  public void Dispose(){Disposals++;if(mode==5)throw new Exception("[TurnCycle] injected Dispose failure");}
 }
 static FaultActions injected; static bool sawThird;
 static TurnCycleRunner(){EditorApplication.update+=Tick;}
 public static void Run(){SessionState.SetBool("TurnCycleRunner",true);EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");EditorApplication.EnterPlaymode();}
 static void Check(string label,bool ok){if(!ok)throw new Exception("[TurnCycle] "+label);Debug.Log("[TurnCycle] PASS "+label);}
 static void Tick(){
  if(!SessionState.GetBool("TurnCycleRunner",false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
  try{
   if(!initialized){
    var game=UnityEngine.Object.FindFirstObjectByType<GameGenerator>();if(game==null||TitleScreenUI.Instance==null)return;
    typeof(GameGenerator).GetField("_waitingForTitle",F).SetValue(game,false);UnityEngine.Object.DestroyImmediate(TitleScreenUI.Instance.gameObject);UnityEngine.Random.InitState(12345);
    typeof(GameGenerator).GetMethod("StartGameInit",F).Invoke(game,new object[]{-1});turn=UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();systems=turn.Systems;
    typeof(AIThreatLevel).GetProperty("Level").SetValue(systems.AICommander.ThreatLevel,100);
    systems.AICommander.TurnThinkingBudgetMs=1000;
    initialized=true;
   }
   if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
   if(oldState!=turn.CurrentState){Debug.Log($"[TurnCycle] state={turn.CurrentState.GetType().Name} turn={turn.Context.Turn} timerTeam={systems.TimerSystem.CurrentTeam} running={systems.TimerSystem.IsRunning}");oldState=turn.CurrentState;}
   if(!waiting){
    Check("player state before end "+round,turn.CurrentState is PlayerMove);
    startTurn=turn.Context.Turn;frames=0;sawThird=false;started=EditorApplication.timeSinceStartup;waiting=true;
    if(round%2==0)((PlayerMove)turn.CurrentState).ExecuteTurnEnd();
    else {systems.TimerSystem.RestoreTurnTimeRemaining(0);typeof(TimerSystem).GetMethod("Advance",F).Invoke(systems.TimerSystem,new object[]{.01f});}
    Check("enemy entered "+round,turn.CurrentState is EnemyMove&&systems.TimerSystem.CurrentTeam==Team.Enemy);
    if(round>=3){
     var enemy=(EnemyMove)turn.CurrentState;
     ((IDisposable)typeof(EnemyMove).GetField("actions",F).GetValue(enemy)).Dispose();
     injected=new FaultActions(round);typeof(EnemyMove).GetField("actions",F).SetValue(enemy,injected);
     if(round==3)typeof(EnemyMove).GetField("deadlineMs",F).SetValue(enemy,20d);
    }
    return;
   }
   frames++;
   if(turn.CurrentState is IndependentFactionState)sawThird=true;
   if(round==2&&frames==5){systems.TimerSystem.RestoreTurnTimeRemaining(0);typeof(TimerSystem).GetMethod("Advance",F).Invoke(systems.TimerSystem,new object[]{.01f});}
   if(turn.CurrentState is PlayerMove){
    Check("third faction follows completed enemy actions "+round,sawThird);
    Check("exactly one round advance "+round,turn.Context.Turn==startTurn+1);
    Check("player timer running "+round,systems.TimerSystem.CurrentTeam==Team.Player&&systems.TimerSystem.IsRunning);
    var group=(CanvasGroup)typeof(EnemyTurnBannerUI).GetField("_group",F).GetValue(EnemyTurnBannerUI.Instance);
    Check("enemy banner hidden "+round,group.alpha==0);
    EnemyTurnBannerUI.Show(turn);
    typeof(EnemyTurnBannerUI).GetMethod("Update",F).Invoke(EnemyTurnBannerUI.Instance,null);
    Check("stale banner self-corrects against player state "+round,!EnemyTurnBannerUI.IsShowing&&group.alpha==0);
    if(injected!=null){Check("fault cleanup exactly once "+round,injected.Disposals==1);injected=null;}
    Debug.Log($"[TurnCycle] elapsedMs={(EditorApplication.timeSinceStartup-started)*1000:F1} frames={frames}");
    waiting=false;round++;
    if(round==6){SessionState.SetBool("TurnCycleRunner",false);Debug.Log("[TurnCycle] ALL PASSED");EditorApplication.Exit(0);}return;
   }
   if(EditorApplication.timeSinceStartup-started>6)throw new Exception("stuck in "+turn.CurrentState.GetType().Name);
  }catch(Exception e){Debug.LogException(e);SessionState.SetBool("TurnCycleRunner",false);EditorApplication.Exit(1);}
 }
}
#endif
