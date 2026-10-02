#if UNITY_EDITOR
using System;
using UnityEngine;
using Unity.Profiling;
public static class BoardRefreshPerformanceTests
{
 public static void Run(GameSystems s)
 {
  var b=new AIBoardState(s.MoveGenerator,s.AttackGenerator,s.APSystem,s.UnitSetting,s.CrystalSystem,s.VisionGenerator,s.BuildSystem,s.SummonSystem,s.FactionState,s.SubCrystalSystem);
  for(int i=0;i<5;i++)b.Refresh();
  var capture=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1000000,ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
  GC.KeepAlive(new byte[8192]);capture.Stop();long calibration=0;for(int i=0;i<capture.Count;i++)calibration+=capture.GetSample(i).Value;capture.Dispose();
  Debug.Log($"[BoardRefreshPerf] calibrationBytes={calibration}");
  var watch=System.Diagnostics.Stopwatch.StartNew();
  capture=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1000000,ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
  for(int i=0;i<50;i++)b.Refresh();
  watch.Stop();capture.Stop();long bytes=0;for(int i=0;i<capture.Count;i++)bytes+=capture.GetSample(i).Value;capture.Dispose();
  Debug.Log($"[BoardRefreshPerf] count=50 ms={watch.Elapsed.TotalMilliseconds:F3} bytes={bytes} tiles={s.MapCreate.SetPos.Count}");
 }
}
#endif
