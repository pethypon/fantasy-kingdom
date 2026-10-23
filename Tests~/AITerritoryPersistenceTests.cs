#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class AITerritoryPersistenceTests
{
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    static int checks;
    static void Check(string message,bool valid)
    { if(!valid)throw new InvalidOperationException("[TerritorySave] FAIL "+message);checks++;Debug.Log("[TerritorySave] PASS "+message); }

    // Serialization fixture only. These explicit observations are not a played-game claim.
    static EnemyTerritoryIntelState Sample(Team owner,int x)
    {
        var state=new EnemyTerritoryIntelState { ActorTeam=owner, OpponentTeam=owner==Team.Player?Team.Enemy:Team.Player,
            Width=40,Depth=40,FirstObservedTurn=3,LastConfirmedTurn=4,LastProgressTurn=4,
            HasWaypoint=true,Waypoint=new Vector3Int(x,0,5),Reason="serialization_fixture" };
        var region=new EnemyTerritoryIntelRegion { OriginX=0,OriginZ=0,FirstObservedTurn=3,LastConfirmedTurn=4 };
        int bit=5*16+x;region.Cells[bit/32]|=1<<(bit%32);state.Regions.Add(region);return state;
    }
    public static void Run(GameSystems systems,TurnGenerator turn)
    {
        checks=0;
        Check("v11 complete migration chain",SaveSystem.CurrentSaveVersion==11&&SaveSystem.ValidateMigrationChain(out _));
        var enemy=Sample(Team.Enemy,5);var player=Sample(Team.Player,6);
        Check("per-faction fixture snapshots valid",EnemyTerritoryIntel.IsValidState(enemy,40,40,Team.Enemy)
            &&EnemyTerritoryIntel.IsValidState(player,40,40,Team.Player));
        Check("opposing faction payload rejected",!EnemyTerritoryIntel.IsValidState(enemy,40,40,Team.Player));
        var data=new SaveSystem.GameSaveData { Turn=8,ThreatLevel=4,MapWidth=40,MapDepth=40,
            PCPx=2,PCPy=1,PCPz=2,ECPx=35,ECPy=1,ECPz=35,HasExplicitMapSnapshot=true,
            PlayerAP=new SaveSystem.APSaveData {Current=12,Reset=30}, EnemyAP=new SaveSystem.APSaveData {Current=8,Reset=30},
            Timer=new SaveSystem.TimerSaveData {TurnTimeRemaining=43,TurnTimeLimit=180,PlayerTotalTime=36000,EnemyTotalTime=35000},
            AI=new SaveSystem.AISaveData { TerritoryIntel=enemy,HasTerritoryIntelSnapshot=true,HasDeterministicSeed=true,RngSeed=56192,AITurnCount=7 },
            PlayerTerritoryIntel=player,HasPlayerTerritoryIntelSnapshot=true };
        string root=Path.Combine(Path.GetTempPath(),"FK_TerritorySave_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using((IDisposable)typeof(SaveSystem).GetMethod("UseTestStorageRoot",Private).Invoke(null,new object[]{root}))
            {
                bool saved=SaveSystem.TrySaveGame(0,data,out string reason);
                Check("actual atomic disk save accepts both factions: "+reason,saved);
                bool read=SaveSystem.TryLoadGame(0,out var loaded,out reason);
                Check("actual disk reload retains both waypoints: "+reason,read&&loaded.AI.TerritoryIntel.Waypoint.x==5
                    &&loaded.PlayerTerritoryIntel.Waypoint.x==6&&loaded.AI.TerritoryIntel.FirstObservedTurn==3);
                var intel=new EnemyTerritoryIntel();intel.RestoreState(loaded.AI.TerritoryIntel,Team.Enemy);
                Check("restored observed evidence not empty",intel.HasObservedTerritory&&intel.ObservedCellCount==1&&intel.HasWaypoint);
                intel.RestoreState(null,Team.Enemy);
                Check("new match/absent payload never fabricates discovery",!intel.HasObservedTerritory&&intel.ObservedCellCount==0&&!intel.HasWaypoint);
                var old=JsonUtility.FromJson<SaveSystem.GameSaveData>(JsonUtility.ToJson(data));
                old.Version=old.SchemaVersion=10;
                old=(SaveSystem.GameSaveData)typeof(SaveSystem).GetMethod("RunMigrations",Private).Invoke(null,new object[]{old});
                Check("v10 migration defaults both ownership memories to unknown",old.Version==11&&old.AI.TerritoryIntel==null&&old.PlayerTerritoryIntel==null);
                var malformed=Sample(Team.Enemy,5);malformed.Regions[0].Cells=new int[1];
                Check("malformed compressed region rejected",!EnemyTerritoryIntel.IsValidState(malformed,40,40,Team.Enemy));
            }
        }
        finally
        {
            string resolved=Path.GetFullPath(root);
            string expectedParent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(Path.GetDirectoryName(resolved)==expectedParent&&Path.GetFileName(resolved).StartsWith("FK_TerritorySave_",StringComparison.Ordinal))
                Directory.Delete(resolved,true);
        }
        var originalEnemy=systems.AICommander.CaptureTerritoryState();
        var originalPlayer=turn.CapturePlayerTerritoryIntel();
        bool wasEnabled=turn.DeveloperPlayerAIEnabled;
        try
        {
            systems.AICommander.RestoreTerritoryState(enemy);turn.RestorePlayerTerritoryIntel(player);
            var collected=SaveSystem.CollectGameState(turn,systems.FactionState,systems.TimerSystem,systems.VisionGenerator,systems.AICommander);
            Check("production collector includes separate enemy/player evidence",collected.AI.TerritoryIntel?.Waypoint.x==5
                &&collected.PlayerTerritoryIntel?.Waypoint.x==6 && collected.AI.HasTerritoryIntelSnapshot && collected.HasPlayerTerritoryIntelSnapshot);
            Check("loading player evidence does not enable automatic control",turn.DeveloperPlayerAIEnabled==wasEnabled);
        }
        finally {systems.AICommander.RestoreTerritoryState(originalEnemy);turn.RestorePlayerTerritoryIntel(originalPlayer);}
        Debug.Log("[TerritorySave] ALL PASSED "+checks);
    }
}
#endif
