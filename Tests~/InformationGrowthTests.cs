#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
public static class InformationGrowthTests
{
    static void Check(string label, bool result) { if(!result) throw new Exception("[InformationGrowth] " + label); Debug.Log("[InformationGrowth] PASS " + label); }
    public static void Run(GameSystems s)
    {
        var expected = new Dictionary<Kind,Vector3Int> {
            {Kind.King,new Vector3Int(40,5,3)}, {Kind.Knight,new Vector3Int(6,2,3)},
            {Kind.Archer,new Vector3Int(5,3,2)}, {Kind.Magic,new Vector3Int(3,3,2)},
            {Kind.Assassin,new Vector3Int(2,5,2)}, {Kind.Scout,new Vector3Int(5,2,2)},
            {Kind.Priest,new Vector3Int(6,2,2)}, {Kind.Guardian,new Vector3Int(15,2,2)},
            {Kind.Crossbow,new Vector3Int(3,3,2)}, {Kind.Magicsniper,new Vector3Int(2,6,2)},
            {Kind.Bomber,new Vector3Int(5,2,2)}, {Kind.Boss,new Vector3Int(17000,77,52)} };
        var go = new GameObject("Growth fixture");go.SetActive(false);var unit=go.AddComponent<Status>();
        try {
            foreach(var pair in expected) {
                var data=UnitStaticData.CreateUnitData(pair.Key);
                try {
                    Check("fixed increments "+pair.Key, new Vector3Int(UnitData.CalcGrowthPerLevel(data.baseHP,data.hpGrowth),UnitData.CalcGrowthPerLevel(data.baseATK,data.atkGrowth),UnitData.CalcGrowthPerLevel(data.baseDEF,data.defGrowth)) == pair.Value);
                    unit.kind=pair.Key;unit.Experience=0;data.ApplyToStatus(unit,1);unit.HP-=1;unit.ATK+=7;
                    bool equal=true;
                    for(int level=2;level<=GameConstants.MaxUnitLevel;level++) {
                        unit.GainExperience(Status.XPRequiredForLevel(level)-unit.Experience);
                        equal &= unit.Level==level && unit.MaxHP==UnitData.CalcStat(data.baseHP,data.hpGrowth,level) && unit.HP==unit.MaxHP-1
                            && unit.ATK==UnitData.CalcStat(data.baseATK,data.atkGrowth,level)+7 && unit.DEF==UnitData.CalcStat(data.baseDEF,data.defGrowth,level);
                    }
                    Check("Lv1-100 creation and growth agree, damage/bonuses preserved "+pair.Key,equal);
                    int hp=unit.MaxHP;unit.GainExperience(int.MaxValue);unit.GainExperience(int.MaxValue);
                    Check("Lv100 cap and XP overflow "+pair.Key,unit.Level==100&&unit.MaxHP==hp&&unit.Experience==int.MaxValue);
                } finally {UnityEngine.Object.DestroyImmediate(data);}
            }
            var saved = SaveSystem.CaptureUnit(unit);
            string json = JsonUtility.ToJson(saved);
            unit.Level=1;unit.Experience=0;
            SaveGameApplier.ApplyStatusFields(unit,JsonUtility.FromJson<SaveSystem.UnitSaveData>(json));
            Check("Lv100 save/load roundtrip",unit.Level==100&&unit.Experience==int.MaxValue&&unit.MaxHP==saved.MaxHP&&unit.ATK==saved.ATK);
            Check("half rounds upward, minimum 2",UnitData.CalcGrowthPerLevel(10,.25f)==3&&UnitData.CalcGrowthPerLevel(5,.7f)==4&&UnitData.CalcGrowthPerLevel(1,0)==2);
            var custom=ScriptableObject.CreateInstance<UnitData>();custom.baseHP=5;custom.baseATK=2;custom.baseDEF=1;custom.kind=Kind.None;
            try {
                unit.kind=Kind.None;unit.Experience=0;custom.ApplyToStatus(unit,1);unit.GainExperience(Status.XPRequiredForLevel(2));
                Check("custom unit grows without static table entry",unit.MaxHP==7&&unit.ATK==4&&unit.DEF==3);
                unit.HP=0;unit.ResetToLv1();Check("custom respawn baseline preserved",unit.Level==1&&unit.MaxHP==5&&unit.HP==0&&unit.ATK==2);
            }finally{UnityEngine.Object.DestroyImmediate(custom);}
            unit.kind=Kind.Knight;unit.Level=1;unit.Experience=0;unit.HP=0;unit.MaxHP=30;unit.GrowthData=null;
            unit.GainExperience(Status.XPRequiredForLevel(2));Check("level up does not revive",unit.HP==0&&unit.Level==2);
        } finally {UnityEngine.Object.DestroyImmediate(go);}
        Recon(s);
    }
    static void Recon(GameSystems s)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var seen=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField("_enemyVisionBox",flags).GetValue(s.VisionGenerator);
        var explored=(HashSet<Vector3Int>)typeof(VisionGenerator).GetField("_enemyExplored",flags).GetValue(s.VisionGenerator);
        var oldSeen=seen.ToArray();var oldExplored=explored.ToArray();
        string savedAP=JsonUtility.ToJson(s.FactionState.EnemyAP);
        s.FactionState.EnemyAP.Reset=35;s.FactionState.EnemyAP.Plus=0;s.FactionState.EnemyAP.Minus=0;
        s.FactionState.EnemyAP.ResetForTurn();
        var created=new List<GameObject>();
        Status Make(string name,Kind kind,Team team,Vector3 pos) {
            var g=new GameObject(name);g.SetActive(false);g.transform.SetParent(team==Team.Enemy?s.UnitSetting.EnemyUnit:s.UnitSetting.PlayerUnit);g.transform.position=pos;
            var u=g.AddComponent<Status>();u.kind=kind;u.team=team;u.type=Type.Unit;u.HP=u.MaxHP=100;u.direction=Direction.N;g.SetActive(true);created.Add(g);return u;
        }
        try {
            seen.Clear();explored.Clear();
            var scout=Make("Recon A",Kind.Scout,Team.Enemy,new Vector3(10,0,10));
            var scout2=Make("Recon B",Kind.Scout,Team.Enemy,new Vector3(16,0,16));
            var hidden=Make("Hidden target",Kind.Knight,Team.Player,new Vector3(12,0,10));
            var memories=AIBoardState.CreateSharedMemory();
            var b=new AIBoardState(s.MoveGenerator,s.AttackGenerator,s.APSystem,s.UnitSetting,s.CrystalSystem,s.VisionGenerator,s.BuildSystem,s.SummonSystem,s.FactionState,s.SubCrystalSystem,4,memories);
            b.ReconThreatLevel=100;
            float score=b.Recon.ScoreMove(scout,new Vector3(10,0,12));
            hidden.transform.position=new Vector3(20,0,20);hidden.kind=Kind.Boss;hidden.HP=1;b.Refresh();
            Check("hidden actual position/kind/HP cannot change reconnaissance score",Mathf.Abs(score-b.Recon.ScoreMove(scout,new Vector3(10,0,12)))<.001f);
            Check("unseen targets absent from observation history",memories.Count==0);
            Check("hidden enemy cannot create deployment target",!b.Recon.TryGetContactTarget(out _));
            Check("scout sectors differ",b.Recon.TryGetAssignedTarget(scout,out var a)&&b.Recon.TryGetAssignedTarget(scout2,out var c)&&a!=c);
            Check("unexplored scouting still valuable",score>12);
            var ranged=Make("Recon archer",Kind.Archer,Team.Enemy,new Vector3(10,0,8));
            b.Refresh();Check("ranged firing lanes increase scouting utility",b.Recon.ScoreMove(scout,new Vector3(10,0,12))>score);
            ranged.gameObject.SetActive(false);b.Refresh();
            foreach(TurnStrategy strategy in Enum.GetValues(typeof(TurnStrategy))) {
                var actions=AIActionEvaluator.EvaluateAll(new AIPersonality(MajorPersonality.Growth),b,new AILearning(false),strategy);
                Check("scout movement candidates retained in "+strategy,actions.Any(v=>v.Unit==scout&&v.ActionType==AIActionType.Move));
            }

            seen.Add(GridHelper.ToGrid(hidden.transform.position));b.Refresh();
            Check("visible enemy kind and position recorded",memories[hidden.GetInstanceID()].Kind==Kind.Boss&&memories[hidden.GetInstanceID()].Position==GridHelper.ToGrid(hidden.transform.position));
            Check("deployment targets visible contact",b.Recon.TryGetContactTarget(out var visibleContact)&&visibleContact==hidden.transform.position);
            hidden.transform.position+=Vector3.right;seen.Add(GridHelper.ToGrid(hidden.transform.position));b.Refresh();
            Check("movement direction history recorded",memories[hidden.GetInstanceID()].HasPrevious);
            seen.Clear();b.Refresh();var last=memories[hidden.GetInstanceID()];
            hidden.transform.position+=Vector3.forward*3;b.Refresh();Check("hidden movement leaves last known position intact",memories[hidden.GetInstanceID()].Position==last.Position);
            Check("hidden movement cannot steer deployment",b.Recon.TryGetContactTarget(out var rememberedContact)&&rememberedContact==(Vector3)last.Position);
            memories.Clear();memories[123456]=new AIBoardState.LastKnownInfo{Valid=true,Turn=3,Kind=Kind.Knight,Type=Type.Unit,Position=new Vector3Int(12,0,12),Direction=Direction.N};
            b.ReconThreatLevel=21;b.Refresh();
            Check("known Knight pattern predicts orthogonal not diagonal one-step",b.Recon.InformationAt(new Vector3Int(13,0,12))>0&&b.Recon.InformationAt(new Vector3Int(13,0,13))==0);
            b.ReconThreatLevel=100;b.Refresh();Check("high threat expands uncertainty envelope",b.Recon.InformationAt(new Vector3Int(14,0,12))>0);
            var remembered=memories[123456];remembered.HasPrevious=true;remembered.PreviousPosition=new Vector3Int(11,0,12);memories[123456]=remembered;b.Refresh();
            Check("deployment anticipates observed heading",b.Recon.TryGetContactTarget(out var predicted)&&predicted==new Vector3(13,0,12));
            seen.Add(new Vector3Int(13,0,12));b.Refresh();Check("searched empty prediction is discarded",!b.Recon.TryGetContactTarget(out _));seen.Clear();b.Refresh();
            var summon=new AIAction{ActionType=AIActionType.Summon,SummonKind=Kind.Knight,TargetPos=s.CrystalSystem.ECP};
            float contactSummon=(float)typeof(AIActionEvaluator).Assembly.GetType("AIBuildEvaluator").GetMethod("CalcSummonBaseScore",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{summon,b});
            memories.Clear();b.Refresh();
            float peacefulSummon=(float)typeof(AIActionEvaluator).Assembly.GetType("AIBuildEvaluator").GetMethod("CalcSummonBaseScore",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{summon,b});
            Check("recent contact increases combat recruitment priority",contactSummon>peacefulSummon);
            remembered.Turn=-4;memories[123456]=remembered;b.Refresh();Check("expired contact cannot drive recruitment or deployment",!b.Recon.TryGetContactTarget(out _));
            remembered.Turn=3;memories[123456]=remembered;b.Refresh();
            seen.Add(new Vector3Int(14,0,12));b.Refresh();Check("visible empty cells excluded from hypothesis",b.Recon.InformationAt(new Vector3Int(14,0,12))==0);
            b.ReconThreatLevel=1;b.Refresh();Check("low threat does not use position prediction",b.Recon.InformationAt(new Vector3Int(12,0,12))==0);
            // Exercise the real reserved scouting phase in every strategic mode.
            var commander=s.AICommander;
            var boardField=typeof(AICommander).GetField("_board",flags);
            var strategyField=typeof(AICommander).GetField("_currentStrategy",flags);
            var previousBoard=boardField.GetValue(commander);var previousStrategy=strategyField.GetValue(commander);
            var method=typeof(AICommander).GetMethod("ExecuteReconnaissancePhase",flags);
            var statsType=typeof(AICommander).GetNestedType("TurnStats",BindingFlags.NonPublic);
            int oldAP=s.APSystem.GetAP(Team.Enemy);
            int oldReset=s.FactionState.EnemyAP.Reset, oldPlus=s.FactionState.EnemyAP.Plus, oldMinus=s.FactionState.EnemyAP.Minus;
            s.FactionState.EnemyAP.Reset=35;s.FactionState.EnemyAP.Plus=0;s.FactionState.EnemyAP.Minus=0;
            try {
                boardField.SetValue(commander,b);b.ReconThreatLevel=100;
                seen.Clear();explored.Clear();memories.Clear();b.Refresh();
                Vector3 origin=s.MapCreate.SetPos.First(pos=>!s.MoveGenerator.IsOccupied(GridHelper.ToGridXZ(pos))&&s.MapCreate.SetPos.Any(q=>MovePatterns.CanMove(Kind.Scout,Direction.N,q.x-pos.x,q.z-pos.z)&&s.MapCreate.CanTraverse(pos,q)&&!s.MoveGenerator.IsOccupied(GridHelper.ToGridXZ(q))));
                scout.transform.position=origin;
                foreach(TurnStrategy strategy in Enum.GetValues(typeof(TurnStrategy))) {
                    scout.transform.position=origin;scout.Fatigue=0;scout.HasMovedThisTurn=false;scout.HP=100;
                    seen.Clear();explored.Clear();memories.Clear();s.FactionState.SetAP(Team.Enemy,35);b.Refresh();b.AliveEnemyUnits.RemoveAll(u=>u!=scout);
                    strategyField.SetValue(commander,strategy);AITurnBudget.Begin(3000);
                    var stats=Activator.CreateInstance(statsType,true);
                    var phase=(System.Collections.IEnumerator)method.Invoke(commander,new object[]{stats});
                    try {while(phase.MoveNext()) {}} finally {(phase as IDisposable)?.Dispose();}
                    int moves=(int)statsType.GetField("Moves").GetValue(stats);
                    Check("reserved safe scouting executes within action/AP cap in "+strategy,moves>=1&&moves<=3&&s.APSystem.GetAP(Team.Enemy)>=24);
                }
                var resources=s.FactionState.EnemyResources;
                string savedResources=JsonUtility.ToJson(resources);
                var beforeRecruit=new HashSet<Status>(s.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>());
                try {
                    foreach(var field in typeof(FactionState.ResourceData).GetFields())if(field.FieldType==typeof(int))field.SetValue(resources,999);
                    s.FactionState.SetAP(Team.Enemy,35);seen.Clear();memories.Clear();
                    memories[123456]=new AIBoardState.LastKnownInfo{Valid=true,Turn=4,Kind=Kind.Knight,Type=Type.Unit,Position=new Vector3Int(12,0,12)};
                    b.Refresh();Check("reinforcement fixture has legal positions",b.SummonablePositions.Count>0);
                    AITurnBudget.Begin(3000);var stats=Activator.CreateInstance(statsType,true);
                    var recruit=(System.Collections.IEnumerator)typeof(AICommander).GetMethod("ExecuteReinforcementPhase",flags).Invoke(commander,new object[]{stats});
                    try {while(recruit.MoveNext()){}}finally{(recruit as IDisposable)?.Dispose();}
                    var added=s.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>().Where(u=>!beforeRecruit.Contains(u)).ToArray();
                    Check("contact reserves and executes one combat recruitment",added.Length==1&&added[0].kind!=Kind.Scout&&added[0].kind!=Kind.Priest&&(int)statsType.GetField("Summons").GetValue(stats)==1);
                    Check("reinforcement pays AP and respects territory",s.APSystem.GetAP(Team.Enemy)<35&&s.TerritorySystem.IsInTerritory(GridHelper.ToGridXZ(added[0].transform.position),Team.Enemy));
                } finally {
                    foreach(var u in s.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>())if(!beforeRecruit.Contains(u))UnityEngine.Object.DestroyImmediate(u.gameObject);
                    JsonUtility.FromJsonOverwrite(savedResources,resources);s.MoveGenerator.UnitPointCore();
                }
            } finally {boardField.SetValue(commander,previousBoard);strategyField.SetValue(commander,previousStrategy);s.FactionState.EnemyAP.Reset=oldReset;s.FactionState.EnemyAP.Plus=oldPlus;s.FactionState.EnemyAP.Minus=oldMinus;s.FactionState.SetAP(Team.Enemy,oldAP);}

        } finally {
            foreach(var g in created)UnityEngine.Object.DestroyImmediate(g);
            JsonUtility.FromJsonOverwrite(savedAP,s.FactionState.EnemyAP);
            seen.Clear();seen.UnionWith(oldSeen);explored.Clear();explored.UnionWith(oldExplored);s.MoveGenerator.UnitPointCore();
        }
    }
}
#endif
