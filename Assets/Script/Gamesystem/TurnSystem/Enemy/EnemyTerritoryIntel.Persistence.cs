using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class EnemyTerritoryIntel
{
    public EnemyTerritoryIntelState CaptureState()
    {
        if(width<=0||depth<=0||actorTeam!=Team.Player&&actorTeam!=Team.Enemy)return null;
        var result=new EnemyTerritoryIntelState { ActorTeam=actorTeam,OpponentTeam=actorTeam==Team.Player?Team.Enemy:Team.Player,
            Width=width,Depth=depth,FirstObservedTurn=FirstObservedTurn,LastConfirmedTurn=LastConfirmedTurn,
            LastProgressTurn=LastProgressTurn,LastObservationTurn=observationTurn,
            HasWaypoint=HasWaypoint,Waypoint=Waypoint,Suspended=Suspended,Reason=Reason,PausedTurn=pausedTurn };
        foreach(var item in regions.Values)
            result.Regions.Add(new EnemyTerritoryIntelRegion { OriginX=item.OriginX,OriginZ=item.OriginZ,
                FirstObservedTurn=item.FirstObservedTurn,LastConfirmedTurn=item.LastConfirmedTurn,Cells=(int[])item.Cells.Clone() });
        result.Regions.Sort((a,b)=>a.OriginX!=b.OriginX?a.OriginX.CompareTo(b.OriginX):a.OriginZ.CompareTo(b.OriginZ));
        foreach(var item in visits)result.Visits.Add(new EnemyTerritoryIntelVisit{Cell=item.Key,UntilTurn=item.Value,Completed=completedApproaches.Contains(item.Key)});
        result.Visits.Sort((a,b)=>CompareCell(a.Cell,b.Cell));
        return result;
    }

    public void RestoreState(EnemyTerritoryIntelState saved,Team owner)
    {
        Reset(owner);
        if(saved==null||!IsValidState(saved,saved.Width,saved.Depth,owner))return;
        width=saved.Width;depth=saved.Depth;
        FirstObservedTurn=saved.FirstObservedTurn;LastConfirmedTurn=saved.LastConfirmedTurn;LastProgressTurn=saved.LastProgressTurn;
        HasWaypoint=saved.HasWaypoint;Waypoint=saved.Waypoint;Suspended=saved.Suspended;pausedTurn=saved.PausedTurn;
        observationTurn=saved.LastObservationTurn;
        Reason=saved.Reason??(HasObservedTerritory?"territory_restored":"territory_unobserved");
        if(saved.Regions!=null)foreach(var item in saved.Regions)
        {
            var copy=new EnemyTerritoryIntelRegion { OriginX=item.OriginX,OriginZ=item.OriginZ,FirstObservedTurn=item.FirstObservedTurn,
                LastConfirmedTurn=item.LastConfirmedTurn,Cells=(int[])item.Cells.Clone() };
            regions.Add(new Vector2Int(item.OriginX/RegionSize,item.OriginZ/RegionSize),copy);
            foreach(int word in copy.Cells) { uint bits=(uint)word;while(bits!=0){observedCellCount++;bits&=bits-1;} }
        }
        if(saved.Visits!=null)foreach(var item in saved.Visits)
        { visits[item.Cell]=item.UntilTurn; if(item.Completed)completedApproaches.Add(item.Cell); }
    }

    public void Reset(Team owner)
    {
        ClearReconTarget();
        actorTeam=owner;width=depth=observedCellCount=terrainRevision=failedMoves=0;
        observationTurn=observationGeneration=preparedGeneration=pausedTurn=-1;FirstObservedTurn=LastConfirmedTurn=LastProgressTurn=-1;
        HasWaypoint=Suspended=importedExplored=false;Waypoint=default;Reason="territory_unobserved";
        hasSelectionCursor=false;selectionCursor=default;leadActorCursor=0;
        regions.Clear();terrain.Clear();terrainBlocked.Clear();previousVision.Clear();occupied.Clear();visits.Clear();completedApproaches.Clear();
        removalScratch.Clear();candidates.Clear();leadActors.Clear();mobileActors.Clear();sourceMap=null;preparedBoard=observationBoard=null;
        LastObservationQueries=0;TotalObservationQueries=0;ClearRoutes();
    }

    /// <summary>Null is the compatible old-save form. A malformed present payload is never trusted.</summary>
    public static bool IsValidState(EnemyTerritoryIntelState state,int mapWidth,int mapDepth,Team owner)
    {
        if(state==null)return true;
        if(owner!=Team.Player&&owner!=Team.Enemy||state.Version!=1||state.ActorTeam!=owner
            ||state.OpponentTeam!=(owner==Team.Player?Team.Enemy:Team.Player)
            ||mapWidth<=0||mapDepth<=0||mapWidth>32768||mapDepth>32768
            ||state.Width!=mapWidth||state.Depth!=mapDepth
            ||state.FirstObservedTurn < -1||state.LastConfirmedTurn < -1||state.LastProgressTurn < -1
            ||state.FirstObservedTurn==int.MaxValue||state.LastConfirmedTurn==int.MaxValue||state.LastProgressTurn==int.MaxValue
            ||state.PausedTurn < -1||state.PausedTurn==int.MaxValue||state.Suspended&&state.PausedTurn<0
            ||state.LastObservationTurn < -1||state.LastObservationTurn==int.MaxValue
            ||state.Reason!=null&&state.Reason.Length>128)return false;
        if(state.FirstObservedTurn<0 && (state.LastConfirmedTurn!=-1||state.LastProgressTurn!=-1||state.HasWaypoint
            ||state.Regions!=null&&state.Regions.Count>0))return false;
        if(state.FirstObservedTurn>=0&&state.LastConfirmedTurn<state.FirstObservedTurn)return false;
        long regionSlots=((long)mapWidth+RegionSize-1)/RegionSize*((mapDepth+RegionSize-1)/RegionSize);
        if(state.Regions!=null&&state.Regions.Count>Math.Min(MaximumRegions,regionSlots)
            ||state.Visits!=null&&state.Visits.Count>MaximumVisits)return false;
        var keys=new HashSet<Vector2Int>();
        if(state.Regions!=null)foreach(var region in state.Regions)
        {
            if(region==null||region.OriginX<0||region.OriginZ<0||region.OriginX>=mapWidth||region.OriginZ>=mapDepth
                ||region.OriginX%RegionSize!=0||region.OriginZ%RegionSize!=0||region.Cells==null||region.Cells.Length!=8
                ||region.FirstObservedTurn<state.FirstObservedTurn||region.LastConfirmedTurn<region.FirstObservedTurn
                ||region.LastConfirmedTurn>state.LastConfirmedTurn||!keys.Add(new Vector2Int(region.OriginX,region.OriginZ)))return false;
            for(int word=0;word<8;word++)for(int bit=0;bit<32;bit++)if(((uint)region.Cells[word]&(1u<<bit))!=0)
            {
                int index=word*32+bit;
                if(region.OriginX+index%RegionSize>=mapWidth||region.OriginZ+index/RegionSize>=mapDepth)return false;
            }
        }
        bool Inside(Vector3Int cell)=>cell.x>=0&&cell.z>=0&&cell.x<mapWidth&&cell.z<mapDepth;
        bool Observed(Vector3Int cell)
        {
            if(!Inside(cell)||state.Regions==null)return false;
            foreach(var region in state.Regions)
                if(cell.x/RegionSize*RegionSize==region.OriginX&&cell.z/RegionSize*RegionSize==region.OriginZ)
                {int bit=BitIndex(cell);return(region.Cells[bit>>5]&(1<<(bit&31)))!=0;}
            return false;
        }
        if(state.HasWaypoint)
        {
            if(!Inside(state.Waypoint)||state.Waypoint.y < -65536||state.Waypoint.y>65536)return false;
            bool hasEvidence=false;
            for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                if(Observed(state.Waypoint+new Vector3Int(dx,0,dz)))hasEvidence=true;
            if(!hasEvidence)return false;
        }
        var visitKeys=new HashSet<Vector3Int>();
        if(state.Visits!=null)foreach(var visit in state.Visits)
        {
            if(visit==null||!Inside(visit.Cell)||visit.Cell.y!=0||visit.UntilTurn<0||visit.UntilTurn==int.MaxValue
                ||!visitKeys.Add(visit.Cell))return false;
            if(visit.Completed)
            {
                if(state.HasWaypoint&&visit.Cell==XZ(state.Waypoint))return false;
                bool evidence=false;
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                    if(Observed(visit.Cell+new Vector3Int(dx,0,dz)))evidence=true;
                if(!evidence)return false;
            }
        }
        return true;
    }
}
