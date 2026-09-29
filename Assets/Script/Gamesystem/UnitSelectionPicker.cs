using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Cycles distinct actors under repeated clicks without touching action-target raycasts.</summary>
public sealed class UnitSelectionPicker
{
    RaycastHit[] hits = new RaycastHit[32];
    readonly List<Status> actors = new List<Status>(16);
    readonly List<RaycastHit> actorHits = new List<RaycastHit>(16);
    static readonly IComparer<RaycastHit> DistanceOrder = Comparer<RaycastHit>.Create((a,b) =>
    {
        int order = a.distance.CompareTo(b.distance);
        return order != 0 ? order : a.collider.GetInstanceID().CompareTo(b.collider.GetInstanceID());
    });
    Status last;
    Vector2 pointer;
    Ray previousRay;

    public void Reset() { last = null; }

    public bool Pick(Ray ray, Vector2 screenPoint, Status current, Predicate<Status> eligible,
        out Status actor, out RaycastHit hit, bool repeatOnly = false)
    {
        actor = null; hit = default;
        int count;
        while ((count = Physics.RaycastNonAlloc(ray,hits,GameConstants.DefaultRayDistance)) == hits.Length)
            Array.Resize(ref hits,hits.Length*2);
        Array.Sort(hits,0,count,DistanceOrder);
        actors.Clear(); actorHits.Clear();
        for (int i=0;i<count;i++)
        {
            var candidate=hits[i].collider.GetComponentInParent<Status>();
            if (candidate==null || actors.Contains(candidate) || !eligible(candidate)) continue;
            actors.Add(candidate); actorHits.Add(hits[i]);
        }
        bool repeat = last != null && current == last && (screenPoint-pointer).sqrMagnitude <= 36f
            && (ray.origin-previousRay.origin).sqrMagnitude < .01f
            && Vector3.Dot(ray.direction,previousRay.direction) > .9999f;
        int previous = repeat ? actors.IndexOf(last) : -1;
        if (repeatOnly && (previous < 0 || actors.Count < 2)) return false;
        if (actors.Count == 0) { Reset(); return false; }
        int index = previous >= 0 ? (previous+1)%actors.Count : 0;
        actor=actors[index];hit=actorHits[index];last=actor;pointer=screenPoint;previousRay=ray;
        return true;
    }

    public static bool IsVisibleActor(Status actor, VisionGenerator vision) => actor != null && actor.IsAlive
        && actor.type <= Type.Wall && actor.team != Team.None && actor.gameObject.activeInHierarchy
        && (actor.team == Team.Player || (vision != null && vision.IsInVisionXZ(Team.Player,actor.transform.position)));
}
