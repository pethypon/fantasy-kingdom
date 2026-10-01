#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
public static class AIExplorationTests
{
 static void Check(string text,bool ok){if(!ok)throw new Exception("[AIExploration] "+text);Debug.Log("[AIExploration] PASS "+text);}
 static HashSet<Vector3Int> Cells(VisionGenerator v,string field)=>(HashSet<Vector3Int>)typeof(VisionGenerator).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(v);
 public static void Run(GameSystems s,TurnGenerator turn)
 {
  var vision=s.VisionGenerator;var seen=Cells(vision,"_enemyVisionBox");var explored=Cells(vision,"_enemyExplored");
  var oldSeen=seen.ToArray();var oldExplored=explored.ToArray();int ap=s.APSystem.GetAP(Team.Enemy);
  var scoutGo=new GameObject("Exploration scout");scoutGo.SetActive(false);scoutGo.transform.SetParent(s.UnitSetting.EnemyUnit);
  var scout=scoutGo.AddComponent<Status>();scout.kind=Kind.Scout;scout.team=Team.Enemy;scout.HP=scout.MaxHP=100;scout.direction=Direction.N;
  var targetGo=new GameObject("Unobserved target");targetGo.SetActive(false);targetGo.transform.SetParent(s.UnitSetting.PlayerUnit);
  var target=targetGo.AddComponent<Status>();target.kind=Kind.Knight;target.team=Team.Player;target.HP=target.MaxHP=100;
  try {
   s.MoveGenerator.UnitPointCore();
   Vector3 from=default,dest=default,targetPos=default;bool found=false;
   foreach(var p in s.MapCreate.SetPos) {
    var d=s.MapCreate.SetPos.FirstOrDefault(q=>q.x==p.x&&q.z==p.z+2&&q.y==p.y);
    var t=s.MapCreate.SetPos.FirstOrDefault(q=>q.x==p.x+1&&q.z==p.z+2&&q.y==p.y);
    if(d==default||t==default||!s.MapCreate.CanTraverse(p,d)||s.TerritorySystem.IsInTerritory(GridHelper.ToGrid(d),Team.Enemy))continue;
    if(s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(p))||s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(d))||s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(t)))continue;
    from=p;dest=d;targetPos=t;found=true;break;
   }
   Check("fixture has legal unexplored destination outside territory",found);
   scoutGo.transform.position=from;targetGo.transform.position=targetPos;scoutGo.SetActive(true);targetGo.SetActive(true);
   // Controlled fog snapshot: only the scout's starting cell is known. A real
   // VisionPoint call earlier in this frame exercises dirty-cache invalidation.
   vision.MarkVisionDirty();vision.VisionPoint(s.MapCreate,s.MoveGenerator,s.CrystalSystem);
   seen.Clear();explored.Clear();seen.Add(GridHelper.ToGrid(from));explored.Add(GridHelper.ToGrid(from));
   s.FactionState.SetAP(Team.Enemy,35);
   var board=new AIBoardState(s.MoveGenerator,s.AttackGenerator,s.APSystem,s.UnitSetting,s.CrystalSystem,vision,s.BuildSystem,s.SummonSystem,s.FactionState,s.SubCrystalSystem);
   Check("destination is not known",!board.IsTerrainKnown(dest));
   Check("unknown destination becomes a move candidate",board.GetValidMoves(scout).Contains(dest));
   Check("hidden player is not an attack target",!board.AlivePlayerUnits.Contains(target)&&!board.GetAttackTargets(scout).Contains(target));
   var buildBefore=new HashSet<Vector3Int>(board.BuildablePositions);var summonBefore=new HashSet<Vector3Int>(board.SummonablePositions);
   var executor=new AIActionExecutor(turn,s.MoveGenerator,s.AttackGenerator,s.BattleSystem,s.APSystem,s.SkillSystem,s.SubCrystalSystem,s.BuildSystem,s.SummonSystem,new AILearning(false));
   var blockerGo=new GameObject("Hidden blocker");blockerGo.SetActive(false);blockerGo.transform.SetParent(s.UnitSetting.PlayerUnit);blockerGo.transform.position=dest;
   var blocker=blockerGo.AddComponent<Status>();blocker.team=Team.Player;blocker.kind=Kind.Knight;blocker.HP=blocker.MaxHP=100;blockerGo.SetActive(true);
   try {
    int before=s.APSystem.GetAP(Team.Enemy);
    Check("hidden occupied destination rejected without AP cost",!executor.Execute(new AIAction{ActionType=AIActionType.Move,Unit=scout,TargetPos=dest},board)&&scout.transform.position==from&&s.APSystem.GetAP(Team.Enemy)==before);
   } finally {UnityEngine.Object.DestroyImmediate(blockerGo);}
   Check("scout moves outside territory",executor.Execute(new AIAction{ActionType=AIActionType.Move,Unit=scout,TargetPos=dest},board)&&scout.transform.position==dest);
   Check("movement reveals target immediately in same frame",vision.EnemyVisionBox.Contains(GridHelper.ToGrid(targetPos)));
   board.Refresh();Check("refreshed board observes discovered unit",board.AlivePlayerUnits.Contains(target));
   var actions=new List<AIAction>();AIActionGenerator.GenerateAttackCandidates(scout,board,actions);
   Check("remaining AP permits an attack candidate this turn",board.EnemyAP>0&&actions.Any(a=>a.TargetUnit==target));
   int oldAttack=scout.ATK;target.HP=1;scout.ATK=100;board.AliveEnemyUnits.Clear();board.AliveEnemyUnits.Add(scout);
   Check("finishing attack AP reserved before optional phases",AITacticalPriorities.FinishingAttackReserve(board)==board.CalcAttackCost(scout));
   target.ShieldTurns=1;Check("shielded target does not create false finishing reserve",AITacticalPriorities.FinishingAttackReserve(board)==0);target.ShieldTurns=0;
   int attackAP=board.CalcAttackCost(scout);s.FactionState.SetAP(Team.Enemy,attackAP);board.RefreshAP();
   var commander=s.AICommander;var f=BindingFlags.Instance|BindingFlags.NonPublic;
   var commanderBoard=typeof(AICommander).GetField("_board",f);var previousBoard=commanderBoard.GetValue(commander);commanderBoard.SetValue(commander,board);
   try {
    var statsType=typeof(AICommander).GetNestedType("TurnStats",BindingFlags.NonPublic);var stats=Activator.CreateInstance(statsType,true);AITurnBudget.Begin(3000);
    var phase=(System.Collections.IEnumerator)typeof(AICommander).GetMethod("ExecuteReinforcementPhase",f).Invoke(commander,new object[]{stats});
    try{while(phase.MoveNext()){}}finally{(phase as IDisposable)?.Dispose();}
    Check("reinforcement cannot consume last finishing attack AP",s.APSystem.GetAP(Team.Enemy)==attackAP&&(int)statsType.GetField("Summons").GetValue(stats)==0);
   }finally{commanderBoard.SetValue(commander,previousBoard);}
   Check("nearby observed opponent counts as local threat",AITacticalPriorities.HasLocalThreat(board));
   var observed=board.AlivePlayerUnits.ToArray();board.AlivePlayerUnits.Clear();Check("memory alone does not freeze construction",!AITacticalPriorities.HasLocalThreat(board));board.AlivePlayerUnits.AddRange(observed);
   target.HP=100;scout.ATK=oldAttack;s.FactionState.SetAP(Team.Enemy,35);board.Refresh();
   var search=new AISearchEngine(20,14);search.SetSimulationReferences(s.MoveGenerator,s.UnitSetting,s.CrystalSystem,s.APSystem);
   var evaluated=new Dictionary<AIAction,float>();AITurnBudget.Begin(8000);
   var searchClock=System.Diagnostics.Stopwatch.StartNew();var searchWork=search.EvaluateWithLookaheadSteps(actions,board,evaluated);
   try{while(searchWork.MoveNext()){}}finally{(searchWork as IDisposable)?.Dispose();}
   Check("one decision leaves turn budget available for execution",searchClock.Elapsed.TotalMilliseconds<1000&&AITurnBudget.RemainingMs>6500);
   Debug.Log($"[AIExploration] decisionMs={searchClock.Elapsed.TotalMilliseconds:F2} remainingTurnMs={AITurnBudget.RemainingMs:F2}");
   var firstMove=new AIAction{ActionType=AIActionType.Move,Unit=scout,TargetPos=dest,Score=10};
   var otherMove=new AIAction{ActionType=AIActionType.Move,Unit=target,TargetPos=dest,Score=10};
   var boardField=typeof(AICommander).GetField("_board",BindingFlags.Instance|BindingFlags.NonPublic);var savedBoard=boardField.GetValue(s.AICommander);boardField.SetValue(s.AICommander,board);
   try {
    var selected=typeof(AICommander).GetMethod("SelectBestAction",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s.AICommander,new object[]{new List<AIAction>{firstMove,otherMove},new HashSet<string>{firstMove.FailureKey},new HashSet<string>{firstMove.FailureGroupKey}});
    Check("one actor failure does not block another actor",ReferenceEquals(selected,otherMove));
   } finally {boardField.SetValue(s.AICommander,savedBoard);}
   int beforeRejected=s.APSystem.GetAP(Team.Enemy);
   Check("missing actor is rejected",!executor.Execute(new AIAction{ActionType=AIActionType.Move,TargetPos=dest},board));
   Check("illegal movement pattern is rejected",!executor.Execute(new AIAction{ActionType=AIActionType.Move,Unit=scout,TargetPos=dest+Vector3.right*8},board));
   target.transform.position=targetPos+Vector3.right*8;
   Check("stale out-of-range attack is rejected",!executor.Execute(new AIAction{ActionType=AIActionType.Attack,Unit=scout,TargetUnit=target},board));
   target.transform.position=targetPos;target.team=Team.Enemy;
   Check("friendly target is rejected",!executor.Execute(new AIAction{ActionType=AIActionType.Attack,Unit=scout,TargetUnit=target},board));target.team=Team.Player;
   Check("rejected actions consume no AP",s.APSystem.GetAP(Team.Enemy)==beforeRejected);
   var skillPair=SkillData.Table.First(x=>x.Value.Target==SkillTarget.Self&&x.Value.APCost>1&&x.Value.GrantBuff!=BuffType.None);
   scout.AssignedSkillId=skillPair.Key;scout.specialAbility=SpecialAbility.Efficiency;scout.FirstSkillUsedThisTurn=false;scout.SkillCooldown=0;
   int reduced=s.APSystem.CalcSkillCost(skillPair.Value.APCost,scout);s.FactionState.SetAP(Team.Enemy,reduced);board.Refresh();actions.Clear();
   AIActionGenerator.GenerateSkillCandidates(scout,board,actions);
   Check("discounted skill candidate uses actual AP cost",reduced<skillPair.Value.APCost&&actions.Any(a=>a.APCost==reduced));
   var oldSelected=turn.Context.SelectUnit;var oldTarget=s.BattleSystem.Target;
   Check("discounted skill executes with exact remaining AP",executor.Execute(actions.First(),board)&&s.APSystem.GetAP(Team.Enemy)==0);
   Check("skill restores inspection context",turn.Context.SelectUnit==oldSelected&&s.BattleSystem.Target==oldTarget);
   s.FactionState.SetAP(Team.Enemy,35);
   Check("cooldown blocks a repeated skill without spending AP",!executor.Execute(actions.First(),board)&&s.APSystem.GetAP(Team.Enemy)==35);
   Check("building rules unchanged",buildBefore.SetEquals(board.BuildablePositions)&&!board.BuildablePositions.Contains(GridHelper.ToGrid(dest)));
   Check("summoning remains inside territory",board.SummonablePositions.All(p=>s.TerritorySystem.IsInTerritory(p,Team.Enemy)));
  } finally {
   UnityEngine.Object.DestroyImmediate(scoutGo);UnityEngine.Object.DestroyImmediate(targetGo);s.FactionState.SetAP(Team.Enemy,ap);
   s.MoveGenerator.UnitPointCore();vision.MarkVisionDirty();vision.VisionPoint(s.MapCreate,s.MoveGenerator,s.CrystalSystem);
   explored.Clear();explored.UnionWith(oldExplored);seen.Clear();seen.UnionWith(oldSeen);
  }
 }
}
#endif
