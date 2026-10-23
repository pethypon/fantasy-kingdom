using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class EnemyTerritoryIntel
{
    sealed class RouteContext
    {
        public AIBoardState Board;
        public int Generation = -1, TerrainRevision = -1, OccupancyRevision;
        public Vector3Int Target, Start;
        public float Remaining;
        public bool Proved;
        public readonly HashSet<Vector3Int> Occupants = new HashSet<Vector3Int>();
        public CombinedBlocked Blocked;
    }
    // The existing frontier planner retains its own routes. Neither purpose overwrites the other's target.
    ExplorationKnownRoute routes;
    readonly Dictionary<int, RouteContext> routeContexts = new Dictionary<int, RouteContext>();
    readonly Dictionary<int, Status> routeActors = new Dictionary<int, Status>();
    readonly HashSet<Vector3Int> occupancyScratch = new HashSet<Vector3Int>();
    readonly List<int> actorRemovalScratch = new List<int>();

    /// <summary>A bounded union view avoids duplicating the terrain-blocked set for every marching unit.</summary>
    sealed class CombinedBlocked : ISet<Vector3Int>, IReadOnlyCollection<Vector3Int>
    {
        readonly HashSet<Vector3Int> terrain, occupants;
        public CombinedBlocked(HashSet<Vector3Int> terrain, HashSet<Vector3Int> occupants)
        { this.terrain = terrain; this.occupants = occupants; }
        public bool Contains(Vector3Int cell) => terrain.Contains(cell) || occupants.Contains(cell);
        public int Count { get { int result = terrain.Count; foreach(var cell in occupants) if(!terrain.Contains(cell))result++; return result; } }
        public bool IsReadOnly => true;
        public IEnumerator<Vector3Int> GetEnumerator()
        { foreach(var cell in terrain)yield return cell;foreach(var cell in occupants)if(!terrain.Contains(cell))yield return cell; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void CopyTo(Vector3Int[] array,int index) { foreach(var cell in this)array[index++]=cell; }
        public bool Add(Vector3Int value) => throw new NotSupportedException();
        void ICollection<Vector3Int>.Add(Vector3Int value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Remove(Vector3Int value) => throw new NotSupportedException();
        public void ExceptWith(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
        public void IntersectWith(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
        public void SymmetricExceptWith(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
        public void UnionWith(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
        public bool IsProperSubsetOf(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
        public bool IsProperSupersetOf(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
        public bool IsSubsetOf(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
        public bool IsSupersetOf(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
        public bool Overlaps(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
        public bool SetEquals(IEnumerable<Vector3Int> other) => throw new NotSupportedException();
    }

    void ClearRoutes()
    { routes?.Clear(); routeContexts.Clear(); routeActors.Clear(); }
    void PruneRouteActors(AIBoardState board)
    {
        actorRemovalScratch.Clear();
        foreach(var pair in routeActors)
            if(!Eligible(pair.Value,board))actorRemovalScratch.Add(pair.Key);
        foreach(int id in actorRemovalScratch) { routeContexts.Remove(id);routeActors.Remove(id);routes?.Remove(id); }
    }

    bool TryRoute(Status actor,Vector3Int target,AIBoardState board,out RouteContext context)
    {
        context=null;
        if(!Eligible(actor,board)||!Passable(XZ(actor.GridPosition))||!Passable(target))return false;
        int id=actor.GetInstanceID();
        if(!routeContexts.TryGetValue(id,out context))
        {
            int limit=Mathf.Clamp(ExplorationAISettings.Active.MaxCachedRoutes,1,256);
            if(routeContexts.Count>=limit)
            {
                // Runtime cache eviction never removes an observation or a strategic direction.
                int remove=0,oldest=int.MaxValue;bool found=false;
                foreach(var pair in routeContexts)if(!found||pair.Value.Generation<oldest)
                {remove=pair.Key;oldest=pair.Value.Generation;found=true;}
                if(found){routeContexts.Remove(remove);routeActors.Remove(remove);routes?.Remove(remove);}
            }
            context=new RouteContext();context.Blocked=new CombinedBlocked(terrainBlocked,context.Occupants);
            routeContexts.Add(id,context);routeActors[id]=actor;
        }
        var start=XZ(actor.GridPosition);
        if(context.Board==board&&context.Generation==board.Generation&&context.Target==target&&context.Start==start
            &&context.TerrainRevision==terrainRevision)return context.Proved;
        occupancyScratch.Clear();
        foreach(var cell in occupied)if(cell!=start)occupancyScratch.Add(cell);
        if(!context.Occupants.SetEquals(occupancyScratch))
        { context.Occupants.Clear();context.Occupants.UnionWith(occupancyScratch);context.OccupancyRevision++; }
        context.Board=board;context.Generation=board.Generation;context.Target=target;context.Start=start;context.TerrainRevision=terrainRevision;
        if(routes==null)routes=new ExplorationKnownRoute();
        var snapshot=new ExplorationActor(id,GridHelper.ToGrid(actor.transform.position),actor.kind,actor.HP,true,actor.ReflectionLifeId);
        int evidence;
        unchecked { evidence=board.BeliefEvidenceStamp*397^context.OccupancyRevision; }
        context.Proved=routes.TryMeasure(snapshot,target,terrain,context.Blocked,MovePatterns.Offsets(actor),
            !MovePatterns.IsDirectionIndependent(actor),MovePatterns.DirZ(actor.direction),terrainRevision,
            board.TurnCount,evidence,EstimateDanger,out float remaining,out _,width,depth,requireEnabled:false);
        context.Remaining=remaining;
        return context.Proved;
    }

    float EstimateDanger(int id,Vector3Int cell)
        => preparedBoard!=null&&routeActors.TryGetValue(id,out var actor)
            ? preparedBoard.EstimateCounterDamageAt(cell,actor) : 0;

    public bool IsProgress(Status actor,Vector3 destination,AIBoardState board)
        => ProgressScore(actor,destination,board)>0;

    public float ProgressScore(Status actor,Vector3 destination,AIBoardState board)
    {
        if(board==null||!HasWaypoint||Suspended||!Eligible(actor,board))return 0;
        Observe(board);PrepareBoard(board);
        var target=GridHelper.ToGridXZ(destination);
        if(!Passable(target)||!terrain.TryGetValue(target,out var height)||Mathf.Abs(destination.y-height.y)>.01f
            ||occupied.Contains(target)&&target!=actor.GridPosition
            ||!MovePatterns.CanMove(actor,actor.direction,destination.x-actor.transform.position.x,destination.z-actor.transform.position.z)
            ||board.CalcMoveCost(actor,destination)>board.EnemyAP)return 0;
        if(!TryRoute(actor,XZ(Waypoint),board,out var context)
            ||!routes.TryMeasureDestination(actor.GetInstanceID(),target,out float after))return 0;
        float progress=context.Remaining-after;
        if(progress<=.01f||sourceMap==null||!sourceMap.CanTraverse(actor.transform.position,destination))return 0;
        var settings=ExplorationAISettings.Active;
        float weight=float.IsNaN(settings.TargetDistanceWeight)||float.IsInfinity(settings.TargetDistanceWeight)?0:Mathf.Max(0,settings.TargetDistanceWeight);
        float maximum=float.IsNaN(settings.MaximumDistanceProgressBonus)||float.IsInfinity(settings.MaximumDistanceProgressBonus)?0:Mathf.Max(0,settings.MaximumDistanceProgressBonus);
        return Mathf.Min(progress,maximum)*weight;
    }
}
