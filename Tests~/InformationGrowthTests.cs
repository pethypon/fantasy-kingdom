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
        // The board owns a collection view whose refreshes can only find fixture actors.
        var viewObject=new GameObject("Recon fixture UnitSetting view");viewObject.SetActive(false);created.Add(viewObject);
        var fixtureUnits=viewObject.AddComponent<UnitSetting>();
        var ownGroup=new GameObject("Recon fixture own actors");ownGroup.transform.SetParent(s.UnitSetting.EnemyUnit);created.Add(ownGroup);
        fixtureUnits.EnemyUnit=ownGroup.transform;fixtureUnits.PlayerUnit=s.UnitSetting.PlayerUnit;
        typeof(UnitSetting).GetProperty("UnitDataMap").GetSetMethod(true).Invoke(fixtureUnits,
            new object[]{new Dictionary<Kind,UnitData>(s.UnitSetting.UnitDataMap)});
        var fixtureOwnActors=new HashSet<Status>();
        Status phaseScout=null;
        Status Make(string name,Kind kind,Team team,Vector3 pos) {
            var g=new GameObject(name);g.SetActive(false);g.transform.SetParent(team==Team.Enemy?ownGroup.transform:s.UnitSetting.PlayerUnit);g.transform.position=pos;
            var u=g.AddComponent<Status>();u.kind=kind;u.team=team;u.type=Type.Unit;u.HP=u.MaxHP=100;u.direction=Direction.N;g.SetActive(true);created.Add(g);
            if(team==Team.Enemy)fixtureOwnActors.Add(u);
            return u;
        }
        void RefreshFixtureBoard(AIBoardState board) {
            board.Refresh();
            board.AliveEnemyUnits.RemoveAll(actor=>!fixtureOwnActors.Contains(actor)||(phaseScout!=null&&actor!=phaseScout));
            board.Allies.Rebuild(board.AliveEnemyUnits);
            foreach(string name in new[]{"NearestAllyCache","AllyDensityCache","HealerCache","CounterCache"})
                ((System.Collections.IDictionary)typeof(AIBoardState).GetField(name,flags).GetValue(board)).Clear();
        }
        void ObserveScouts(params Status[] scouts) {
            seen.Clear();explored.Clear();
            foreach(var actor in scouts) {
                if(actor.VisionCell!=null)actor.VisionCell.Clear();
                s.VisionGenerator.VisionCreate(actor,s.MapCreate,s.CrystalSystem);
                foreach(var cell in actor.VisionCell)seen.Add(GridHelper.ToGridXZ(cell));
            }
            explored.UnionWith(seen);
        }
        bool Empty(Vector3 pos) => !s.MoveGenerator.IsOccupied(GridHelper.ToGridXZ(pos));
        bool HasScoutExit(Vector3 pos) => s.MapCreate.SetPos.Any(q=>MovePatterns.CanMove(Kind.Scout,Direction.N,q.x-pos.x,q.z-pos.z)
            &&s.MapCreate.CanTraverse(pos,q)&&Empty(q));
        try {
            s.MoveGenerator.UnitPointCore();
            int regionSize=Mathf.Clamp(ExplorationAISettings.Active.RegionSize,2,128);
            var hostileParents=new[]{s.UnitSetting.PlayerUnit,s.MoveGenerator.NeutralParent,s.MoveGenerator.ObstacleParent,s.BuildSystem.GetBuildingParent(Team.Player)};
            var hostiles=hostileParents.Where(parent=>parent!=null).SelectMany(parent=>parent.GetComponentsInChildren<Status>()).Where(u=>u.IsAlive).Select(u=>u.transform.position)
                .Concat(new[]{s.CrystalSystem.PCP}).ToArray();
            var origins=s.MapCreate.SetPos.Where(pos=>pos.x>=3&&pos.z>=3&&pos.x<s.MapCreate.maxX-3&&pos.z<s.MapCreate.maxZ-3
                &&Empty(pos)&&HasScoutExit(pos)&&hostiles.All(foe=>GridHelper.ChebyshevDistance(pos,foe)>5)).ToArray();
            Check("recon fixture has legal separated Scout origins",origins.Length>1);
            var near=new Vector3(s.MapCreate.maxX*.25f,0,s.MapCreate.maxZ*.25f);
            var far=new Vector3(s.MapCreate.maxX*.75f,0,s.MapCreate.maxZ*.75f);
            Vector3 scoutOrigin=origins.OrderBy(pos=>GridHelper.ChebyshevDistance(pos,near)).ThenBy(pos=>pos.x).ThenBy(pos=>pos.z).First();
            var otherOrigins=origins.Where(pos=>GridHelper.ChebyshevDistance(pos,scoutOrigin)>8
                &&((int)pos.x/regionSize!=(int)scoutOrigin.x/regionSize||(int)pos.z/regionSize!=(int)scoutOrigin.z/regionSize)).ToArray();
            Check("two legal Scout patches occupy different exploration regions",otherOrigins.Length>0);
            Vector3 scoutOrigin2=otherOrigins.OrderBy(pos=>GridHelper.ChebyshevDistance(pos,far)).ThenBy(pos=>pos.x).ThenBy(pos=>pos.z).First();
            var scout=Make("Recon A",Kind.Scout,Team.Enemy,scoutOrigin);
            var scout2=Make("Recon B",Kind.Scout,Team.Enemy,scoutOrigin2);
            ObserveScouts(scout,scout2);s.MoveGenerator.UnitPointCore();
            var hiddenCells=s.MapCreate.SetPos.Where(pos=>Empty(pos)&&!seen.Contains(GridHelper.ToGridXZ(pos))
                &&s.MapCreate.HasTileAt((int)pos.x+1,(int)pos.z)&&s.MapCreate.HasTileAt((int)pos.x+1,(int)pos.z+3)).ToArray();
            Check("hidden fixture has real unobserved terrain",hiddenCells.Length>1);
            var hidden=Make("Hidden target",Kind.Knight,Team.Player,hiddenCells[0]);
            var memories=AIBoardState.CreateSharedMemory();
            var b=new AIBoardState(s.MoveGenerator,s.AttackGenerator,s.APSystem,fixtureUnits,s.CrystalSystem,s.VisionGenerator,s.BuildSystem,s.SummonSystem,s.FactionState,s.SubCrystalSystem,4,memories);
            b.MapCreate=s.MapCreate;b.ReconThreatLevel=100;
            RefreshFixtureBoard(b);
            var legalMoves=b.GetValidMoves(scout);
            Check("scout scoring uses an actual legal move from observed terrain",legalMoves.Count>0&&seen.Count>0&&explored.Count==seen.Count);
            Vector3 scoredMove=legalMoves.OrderByDescending(pos=>b.Recon.ScoreMove(scout,pos)).First();
            float score=b.Recon.ScoreMove(scout,scoredMove);
            hidden.transform.position=hiddenCells[hiddenCells.Length-1];hidden.kind=Kind.Boss;hidden.HP=1;RefreshFixtureBoard(b);
            Check("hidden actual position/kind/HP cannot change reconnaissance score",Mathf.Abs(score-b.Recon.ScoreMove(scout,scoredMove))<.001f);
            Check("unseen targets absent from observation history",memories.Count==0);
            Check("hidden enemy cannot create deployment target",!b.Recon.TryGetContactTarget(out _));
            Debug.Log($"[InformationGrowthReconFixture] scout={scoutOrigin} scout2={scoutOrigin2} known={explored.Count} legalMoves={legalMoves.Count} scoredMove={scoredMove} score={score:F2} frontierA={b.Exploration.GetObjective(scout.GetInstanceID())?.TargetCell} frontierB={b.Exploration.GetObjective(scout2.GetInstanceID())?.TargetCell}");
            foreach(var actor in s.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>().Where(u=>u.IsAlive&&u.kind==Kind.Scout)) {
                var objective=b.Exploration.GetObjective(actor.GetInstanceID());
                Debug.Log($"[InformationGrowthScoutObjective] name={actor.name} id={actor.GetInstanceID()} fixture={fixtureOwnActors.Contains(actor)} included={b.AliveEnemyUnits.Contains(actor)} cell={actor.transform.position} objective={objective?.ObjectiveId} region={objective?.FrontierRegionId} target={objective?.TargetCell}");
            }
            Debug.Log("[InformationGrowthFrontierRegions] "+string.Join(";",b.Exploration.Frontiers.Select(frontier=>$"region={frontier.RegionId} target={frontier.RepresentativeCell} unknown={frontier.UnknownCellsBehind}")));
            Check("recon board actor collection contains exactly its two synthetic Scouts",b.AliveEnemyUnits.Count==2
                &&b.AliveEnemyUnits.All(actor=>fixtureOwnActors.Contains(actor)&&actor.kind==Kind.Scout));
            Check("scout sectors differ",b.Recon.TryGetAssignedTarget(scout,out var a)&&b.Recon.TryGetAssignedTarget(scout2,out var c)&&a!=c
                &&b.Exploration.GetObjective(scout.GetInstanceID()).FrontierRegionId!=b.Exploration.GetObjective(scout2.GetInstanceID()).FrontierRegionId);
            Check("unexplored scouting still valuable",score>12);
            var scoutingFootprint=VisionGenerator.BaseVisionOffsets(scout).Select(offset=>GridHelper.ToGridXZ(scoredMove)+GridHelper.ToGridXZ(offset))
                .Where(cell=>cell.x>=0&&cell.z>=0&&cell.x<s.MapCreate.maxX&&cell.z<s.MapCreate.maxZ&&!seen.Contains(cell)
                    &&s.MapCreate.HasClearTerrainLine(scoredMove,(Vector3)cell,true)).Distinct().ToArray();
            var rangedCells=s.MapCreate.SetPos.Where(pos=>Empty(pos)&&scoutingFootprint.Any(cell=>AttackPatterns.CanAttack(Kind.Archer,Direction.N,cell.x-pos.x,cell.z-pos.z))).ToArray();
            Check("ranged fixture has a legal firing lane into the unexplored scouting footprint",rangedCells.Length>0);
            var ranged=Make("Recon archer",Kind.Archer,Team.Enemy,rangedCells[0]);
            RefreshFixtureBoard(b);Check("ranged firing lanes increase scouting utility",b.Recon.ScoreMove(scout,scoredMove)>score);
            ranged.gameObject.SetActive(false);RefreshFixtureBoard(b);
            foreach(TurnStrategy strategy in Enum.GetValues(typeof(TurnStrategy))) {
                var actions=AIActionEvaluator.EvaluateAll(new AIPersonality(MajorPersonality.Growth),b,new AILearning(false),strategy);
                var strategyMoves=b.GetValidMoves(scout);
                Check("scout movement candidates retained in "+strategy,actions.Any(v=>v.Unit==scout&&v.ActionType==AIActionType.Move&&strategyMoves.Contains(v.TargetPos)));
            }

            seen.Add(GridHelper.ToGrid(hidden.transform.position));RefreshFixtureBoard(b);
            Check("visible enemy kind and position recorded",memories[hidden.GetInstanceID()].Kind==Kind.Boss&&memories[hidden.GetInstanceID()].Position==GridHelper.ToGrid(hidden.transform.position));
            Check("deployment targets visible contact",b.Recon.TryGetContactTarget(out var visibleContact)&&visibleContact==hidden.transform.position);
            hidden.transform.position+=Vector3.right;seen.Add(GridHelper.ToGrid(hidden.transform.position));RefreshFixtureBoard(b);
            Check("movement direction history recorded",memories[hidden.GetInstanceID()].HasPrevious);
            seen.Clear();RefreshFixtureBoard(b);var last=memories[hidden.GetInstanceID()];
            hidden.transform.position+=Vector3.forward*3;RefreshFixtureBoard(b);Check("hidden movement leaves last known position intact",memories[hidden.GetInstanceID()].Position==last.Position);
            Check("hidden movement cannot steer deployment",b.Recon.TryGetContactTarget(out var rememberedContact)&&rememberedContact==(Vector3)last.Position);
            var memoryOrigin=s.MapCreate.SetPos.First(pos=>pos.x>=1&&Empty(pos)&&s.MapCreate.HasTileAt((int)pos.x-1,(int)pos.z)
                &&s.MapCreate.HasTileAt((int)pos.x+1,(int)pos.z)&&Empty(new Vector3(pos.x+1,0,pos.z))
                &&s.MapCreate.TryGetHeight((int)pos.x+2,(int)pos.z,out var y)&&Empty(new Vector3(pos.x+2,0,pos.z))
                &&s.MapCreate.CanTraverse(pos,new Vector3(pos.x-1,pos.y,pos.z))&&s.MapCreate.CanTraverse(pos,new Vector3(pos.x+2,y,pos.z)));
            var memoryCell=GridHelper.ToGrid(memoryOrigin);
            memories.Clear();memories[123456]=new AIBoardState.LastKnownInfo{Valid=true,Turn=3,Kind=Kind.Knight,Type=Type.Unit,Position=memoryCell,Direction=Direction.N};
            b.ReconThreatLevel=21;RefreshFixtureBoard(b);
            Check("known Knight pattern predicts orthogonal not diagonal one-step",b.Recon.InformationAt(memoryCell+Vector3Int.right)>0&&b.Recon.InformationAt(memoryCell+Vector3Int.right+new Vector3Int(0,0,1))==0);
            b.ReconThreatLevel=100;RefreshFixtureBoard(b);Check("high threat expands uncertainty envelope",b.Recon.InformationAt(memoryCell+Vector3Int.right*2)>0);
            var remembered=memories[123456];remembered.HasPrevious=true;remembered.PreviousPosition=memoryCell-Vector3Int.right;memories[123456]=remembered;RefreshFixtureBoard(b);
            Check("deployment anticipates observed heading",b.Recon.TryGetContactTarget(out var predicted)&&predicted==(Vector3)(memoryCell+Vector3Int.right));
            seen.Add(GridHelper.ToGridXZ(memoryCell+Vector3Int.right));RefreshFixtureBoard(b);Check("searched empty prediction is discarded",!b.Recon.TryGetContactTarget(out _));seen.Clear();RefreshFixtureBoard(b);
            var summon=new AIAction{ActionType=AIActionType.Summon,SummonKind=Kind.Knight,TargetPos=s.CrystalSystem.ECP};
            float contactSummon=(float)typeof(AIActionEvaluator).Assembly.GetType("AIBuildEvaluator").GetMethod("CalcSummonBaseScore",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{summon,b});
            memories.Clear();RefreshFixtureBoard(b);
            float peacefulSummon=(float)typeof(AIActionEvaluator).Assembly.GetType("AIBuildEvaluator").GetMethod("CalcSummonBaseScore",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{summon,b});
            Check("recent contact increases combat recruitment priority",contactSummon>peacefulSummon);
            remembered.Turn=-4;memories[123456]=remembered;RefreshFixtureBoard(b);Check("expired contact cannot drive recruitment or deployment",!b.Recon.TryGetContactTarget(out _));
            remembered.Turn=3;memories[123456]=remembered;RefreshFixtureBoard(b);
            seen.Add(GridHelper.ToGridXZ(memoryCell+Vector3Int.right*2));RefreshFixtureBoard(b);Check("visible empty cells excluded from hypothesis",b.Recon.InformationAt(memoryCell+Vector3Int.right*2)==0);
            b.ReconThreatLevel=1;RefreshFixtureBoard(b);Check("low threat does not use position prediction",b.Recon.InformationAt(memoryCell)==0);
            // Exercise the real reserved scouting phase in every strategic mode.
            var commander=s.AICommander;
            var boardField=typeof(AICommander).GetField("_board",flags);
            var strategyField=typeof(AICommander).GetField("_currentStrategy",flags);
            var previousBoard=boardField.GetValue(commander);var previousStrategy=strategyField.GetValue(commander);
            var previousExploration=commander.CaptureExplorationState();
            var method=typeof(AICommander).GetMethod("ExecuteReconnaissancePhase",flags);
            var statsType=typeof(AICommander).GetNestedType("TurnStats",BindingFlags.NonPublic);
            int oldAP=s.APSystem.GetAP(Team.Enemy);
            int oldReset=s.FactionState.EnemyAP.Reset, oldPlus=s.FactionState.EnemyAP.Plus, oldMinus=s.FactionState.EnemyAP.Minus;
            s.FactionState.EnemyAP.Reset=35;s.FactionState.EnemyAP.Plus=0;s.FactionState.EnemyAP.Minus=0;
            try {
                boardField.SetValue(commander,b);b.ReconThreatLevel=100;
                Vector3 origin=scoutOrigin;
                scout.transform.position=origin;
                phaseScout=scout;
                fixtureUnits.EnemyUnit=scout.transform;
                foreach(TurnStrategy strategy in Enum.GetValues(typeof(TurnStrategy))) {
                    scout.transform.position=origin;scout.Fatigue=0;scout.HasMovedThisTurn=false;scout.HP=100;
                    ObserveScouts(scout);memories.Clear();s.FactionState.SetAP(Team.Enemy,35);RefreshFixtureBoard(b);
                    // Each mode starts from the same legal sight, rather than inheriting earlier test moves.
                    b.Exploration.RestoreState(new AIExplorationState{Width=s.MapCreate.maxX,Depth=s.MapCreate.maxZ,Turn=4,ActorTeam=Team.Enemy});
                    strategyField.SetValue(commander,strategy);AITurnBudget.Begin(3000);
                    var stats=Activator.CreateInstance(statsType,true);
                    var phase=(System.Collections.IEnumerator)method.Invoke(commander,new object[]{stats});
                    try {while(phase.MoveNext()) {}} finally {(phase as IDisposable)?.Dispose();}
                    int moves=(int)statsType.GetField("Moves").GetValue(stats);
                    Check("reserved safe scouting executes within action/AP cap in "+strategy,moves>=1&&moves<=3&&s.APSystem.GetAP(Team.Enemy)>=24);
                }
                phaseScout=null;
                fixtureUnits.EnemyUnit=ownGroup.transform;
                var resources=s.FactionState.EnemyResources;
                string savedResources=JsonUtility.ToJson(resources);
                var beforeRecruit=new HashSet<Status>(s.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>());
                try {
                    foreach(var field in typeof(FactionState.ResourceData).GetFields())if(field.FieldType==typeof(int))field.SetValue(resources,999);
                    s.FactionState.SetAP(Team.Enemy,35);seen.Clear();memories.Clear();
                    memories[123456]=new AIBoardState.LastKnownInfo{Valid=true,Turn=4,Kind=Kind.Knight,Type=Type.Unit,Position=memoryCell};
                    RefreshFixtureBoard(b);Check("reinforcement fixture has legal positions",b.SummonablePositions.Count>0);
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
            } finally {phaseScout=null;fixtureUnits.EnemyUnit=ownGroup.transform;boardField.SetValue(commander,previousBoard);strategyField.SetValue(commander,previousStrategy);commander.RestoreExplorationState(previousExploration);s.FactionState.EnemyAP.Reset=oldReset;s.FactionState.EnemyAP.Plus=oldPlus;s.FactionState.EnemyAP.Minus=oldMinus;s.FactionState.SetAP(Team.Enemy,oldAP);}

        } finally {
            foreach(var g in created)if(g!=null)UnityEngine.Object.DestroyImmediate(g);
            JsonUtility.FromJsonOverwrite(savedAP,s.FactionState.EnemyAP);
            seen.Clear();seen.UnionWith(oldSeen);explored.Clear();explored.UnionWith(oldExplored);s.MoveGenerator.UnitPointCore();
        }
    }
}
#endif
