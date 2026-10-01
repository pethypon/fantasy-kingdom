#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
[InitializeOnLoad]
public static class AIFrameRunner
{
    static bool initialized;
    static IEnumerator steps;
    static GameSystems systems;
    static int lastFrame=-1,frames;
    static double maxStep,totalStep;
    static System.Diagnostics.Stopwatch wall;
    static Slider heartbeat;
    static AIFrameRunner(){EditorApplication.update+=Tick;}
    public static void Run(){SessionState.SetBool("AIFrameRunner",true);EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");EditorApplication.EnterPlaymode();}
    static void Tick()
    {
        if(!SessionState.GetBool("AIFrameRunner",false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try {
            if(!initialized){
                var game=UnityEngine.Object.FindFirstObjectByType<GameGenerator>();if(game==null||TitleScreenUI.Instance==null)return;
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(GameGenerator).GetField("_waitingForTitle",flags).SetValue(game,false);
                UnityEngine.Object.DestroyImmediate(TitleScreenUI.Instance.gameObject);
                UnityEngine.Random.InitState(12345);
                typeof(GameGenerator).GetMethod("StartGameInit",flags).Invoke(game,new object[]{-1});
                systems=UnityEngine.Object.FindFirstObjectByType<TurnGenerator>().Systems;systems.TimerSystem.StopTurn();
                var threat=(AIThreatLevel)typeof(AICommander).GetField("_threatLevel",flags).GetValue(systems.AICommander);
                typeof(AIThreatLevel).GetProperty("Level").SetValue(threat,100);
                systems.AICommander.HierarchicalMode=false;systems.AICommander.TurnThinkingBudgetMs=1000;
                systems.FactionState.SetAP(Team.Enemy,50);
                var enemy=systems.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>().First(u=>u.IsAlive);
                var player=systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().First(u=>u.IsAlive);
                player.transform.position=systems.MapCreate.SetPos.Where(p=>!systems.MoveGenerator.IsOccupied(GridHelper.ToGridXZ(p))&&systems.MapCreate.CanTraverse(enemy.transform.position,p)).OrderBy(p=>Vector3.Distance(p,enemy.transform.position)).First();
                player.HP=player.MaxHP=100000;systems.MoveGenerator.UnitPointCore();systems.RefreshVision();
                heartbeat=new GameObject("AI heartbeat",typeof(RectTransform),typeof(Slider)).GetComponent<Slider>();
                steps=systems.AICommander.ExecuteTurnSteps();wall=System.Diagnostics.Stopwatch.StartNew();initialized=true;
            }
            if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;frames++;
            heartbeat.value=(frames%10)/10f;
            var timer=System.Diagnostics.Stopwatch.StartNew();bool more;
            try{AITurnBudget.Resume();more=steps.MoveNext();}finally{AITurnBudget.Pause();}
            timer.Stop();totalStep+=timer.Elapsed.TotalMilliseconds;maxStep=Math.Max(maxStep,timer.Elapsed.TotalMilliseconds);
            if(wall.Elapsed.TotalSeconds>4)throw new Exception("AI wall deadline exceeded");
            if(more)return;
            (steps as IDisposable)?.Dispose();
            if(frames<5||systems.AICommander.LastSearchMaxSliceMs<=0)throw new Exception("Fixture did not exercise sliced search across frames");
            Debug.Log($"[AIFrame] PASS frames={frames} wallMs={wall.Elapsed.TotalMilliseconds:F2} cpuMs={totalStep:F2} maxCommanderStepMs={maxStep:F2} maxSearchSliceMs={systems.AICommander.LastSearchMaxSliceMs:F2} heartbeat={heartbeat.value:F1}");
            SessionState.SetBool("AIFrameRunner",false);EditorApplication.Exit(0);
        }catch(Exception e){(steps as IDisposable)?.Dispose();Debug.LogException(e);SessionState.SetBool("AIFrameRunner",false);EditorApplication.Exit(1);}
    }
}
#endif
