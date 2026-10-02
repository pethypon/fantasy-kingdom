#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
public static class AIEvolutionTests
{
    static void Check(string label, bool ok) { if (!ok) throw new Exception("[Evolution] " + label); Debug.Log("[Evolution] PASS " + label); }
    public static void Run(GameSystems s, TurnGenerator turn)
    {
        for (int i=1;i<=10;i++) Check("level retained " + i,new AIThreatLevel(i).Level==i);
        Check("role coordination begins at five",!new AIThreatLevel(4).UseRoleAssignment && new AIThreatLevel(5).UseRoleAssignment);
        Check("serialized Wait unchanged",(int)AIActionType.Wait==10 && (int)AIActionType.Rotate==11);
        string dir=Path.Combine(Path.GetTempPath(),"FantasyKingdom-AI-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir); string path=Path.Combine(dir,"profile.json");
        try {
            float prior=0;
            for(int i=1;i<=10;i++) {
                var m=new AIPlayerModel(path);m.RecordObservation(1,0,0,1);
                Check("save match " + i,m.CompleteMatch());
                var loaded=new AIPlayerModel(path);float value=loaded.Snapshot.Ranged;
                Check("EWMA match " + i,Mathf.Abs(value-(prior*.8f+.2f))<.0001f && loaded.Snapshot.Matches==i);
                Check("finish idempotent " + i,m.CompleteMatch() && new AIPlayerModel(path).Snapshot.Matches==i);prior=value;
            }
            var model=new AIPlayerModel(path);var a=new AIAction{ActionType=AIActionType.Summon,SummonKind=Kind.Knight};
            Check("low level collects without using prior",model.Bonus(a,9)==0 && model.Bonus(a,20)>0);
            string json=File.ReadAllText(path);Check("profile excludes positions and live stats",!json.Contains("Position")&&!json.Contains("HP")&&!json.Contains("Kind"));
            File.WriteAllText(path,"broken");Check("corruption falls back to backup",new AIPlayerModel(path).Snapshot.Matches==9);
            File.WriteAllText(path+".bak","broken");Check("both corrupt reset safely",new AIPlayerModel(path).Snapshot.Matches==0);
            var invalid=new AIPlayerModel(path);invalid.RecordObservation(float.NaN,0,0,0);invalid.CompleteMatch();Check("invalid observations rejected",invalid.Snapshot.Matches==0);
        } finally { foreach(var f in Directory.GetFiles(dir))File.Delete(f);Directory.Delete(dir); }

        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var seen=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField("_enemyVisionBox",flags).GetValue(s.VisionGenerator);
        var oldSeen=seen.ToArray();
        var ug=new GameObject("Evolution actor");ug.SetActive(false);
        var tg=new GameObject("Evolution target");tg.SetActive(false);
        var u=ug.AddComponent<Status>();var t=tg.AddComponent<Status>();
        try {
            u.kind=Kind.Knight;u.type=global::Type.Unit;u.team=Team.Enemy;u.direction=Direction.N;u.HP=u.MaxHP=100;u.ATK=30;
            t.kind=Kind.Scout;t.type=global::Type.Unit;t.team=Team.Player;t.HP=t.MaxHP=20;t.DEF=0;
            var b=new AIBoardState(s.MoveGenerator,s.AttackGenerator,s.APSystem,s.UnitSetting,s.CrystalSystem,s.VisionGenerator,s.BuildSystem,s.SummonSystem,s.FactionState,s.SubCrystalSystem,10);
            b.ReconThreatLevel=10;
            Vector3 origin=default,destination=default;bool found=false;
            foreach(var p0 in s.MapCreate.SetPos) {
                foreach(var p1 in s.MapCreate.SetPos) {
                    var d=p1-p0;ug.transform.position=p0;
                    if(AttackPatterns.CanAttack(u.kind,Direction.S,d.x,d.z)&&!AttackPatterns.CanAttack(u.kind,Direction.N,d.x,d.z)&&s.MapCreate.CanAttackAcrossTerrain(u,p1))
                    {origin=p0;destination=p1;found=true;break;}
                } if(found)break;
            }
            Check("directional fixture",found);
            ug.transform.position=origin;tg.transform.position=destination;
            ug.SetActive(true);tg.SetActive(true);b.AliveEnemyUnits.Clear();b.AliveEnemyUnits.Add(u);b.AlivePlayerUnits.Clear();b.AlivePlayerUnits.Add(t);
            seen.Clear();seen.Add(GridHelper.ToGridXZ(destination));
            var actions=new List<AIAction>();AIOrientation.AppendCandidates(b,actions);
            Check("rotation enables attack",actions.Count==1 && actions[0].TargetDirection==Direction.S && actions[0].APCost==0);
            Check("evaluation never mutates facing",u.direction==Direction.N);
            seen.Clear();actions.Clear();AIOrientation.AppendCandidates(b,actions);Check("hidden target cannot justify rotation",actions.Count==0);
            seen.Add(GridHelper.ToGridXZ(destination));actions.Clear();AIOrientation.AppendCandidates(b,actions);
            var rotate=actions[0];int ap=s.APSystem.GetAP(Team.Enemy);
            var exec=new AIActionExecutor(turn,s.MoveGenerator,s.AttackGenerator,s.BattleSystem,s.APSystem,s.SkillSystem,s.SubCrystalSystem,s.BuildSystem,s.SummonSystem,new AILearning(false));
            Check("execute facing change",exec.Execute(rotate,b) && u.direction==Direction.S);
            Check("rotation preserves AP",s.APSystem.GetAP(Team.Enemy)==ap);
            rotate.TargetDirection=Direction.N;Check("rotation cannot oscillate",!exec.Execute(rotate,b));
            Check("vision marked and board refreshed",b.Generation>1);
            b.AlivePlayerUnits.Clear();b.AlivePlayerUnits.Add(t);b.AliveEnemyUnits.Clear();b.AliveEnemyUnits.Add(u);
            // Use a remote observed objective, away from emergency-defense radius.
            tg.transform.position=b.EnemyCrystalPos+new Vector3(10,0,0);seen.Clear();seen.Add(GridHelper.ToGridXZ(tg.transform.position));
            var policy=new AIEvolutionPolicy();policy.BeginTurn(b);Check("observed offensive objective acquired",policy.HasObjective);
            Vector3 remembered=policy.Objective;seen.Clear();b.AlivePlayerUnits.Clear();tg.transform.position+=Vector3.forward*5;
            policy.UpdateObjective(b);Check("hidden transform cannot update objective",policy.HasObjective&&policy.Objective==remembered);
            seen.Add(GridHelper.ToGridXZ(remembered));policy.UpdateObjective(b);Check("searched empty objective invalidated",!policy.HasObjective);
            b.AlivePlayerUnits.Add(t);seen.Add(GridHelper.ToGridXZ(tg.transform.position));policy.BeginTurn(b);
            b.AlivePlayerUnits.Clear();seen.Clear();typeof(AIBoardState).GetProperty("TurnCount").SetValue(b,15);
            policy.UpdateObjective(b);Check("offense expires after five turns",!policy.HasObjective);
            b.AlivePlayerUnits.Add(t);b.ReconThreatLevel=10;u.Level=10;u.Experience=0;u.HP=u.MaxHP=100;
            var scorer=new AIEvolutionPolicy();scorer.BeginTurn(b);
            var learner=new AIPlayerModel(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+"-unused.json"));
            var attack=new AIAction{ActionType=AIActionType.Attack,Unit=u,TargetUnit=t,APCost=1};
            var scored=new List<AIAction>{attack};scorer.Score(scored,b,learner);float normal=attack.Score;
            u.Experience=Status.XPRequiredForLevel(11)-1;attack.Score=0;scorer.Score(scored,b,learner);
            Check("safe level-up attack preference",Mathf.Abs(attack.Score-normal-8)<.01f);
            u.Experience=0;attack.Score=0;t.type=global::Type.Building;t.facilityKind=FacilityKind.SubCrystal;
            scorer.Score(scored,b,learner);Check("subcrystal disruption preference",attack.Score>=normal+35);
            t.ShieldTurns=1;attack.Score=0;scorer.Score(scored,b,learner);Check("shield does not earn finishing bonus",attack.Score<normal+35);

        } finally { UnityEngine.Object.DestroyImmediate(ug);UnityEngine.Object.DestroyImmediate(tg);seen.Clear();seen.UnionWith(oldSeen); }
    }
}
#endif
