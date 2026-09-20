using System.Collections.Generic;
using UnityEngine;
/// <summary>Only observed actors are indexed; rebuilt after every board refresh.</summary>
public sealed class ObservedUnitIndex
{
    const int Size = 4;
    readonly Dictionary<Vector2Int,List<Status>> buckets = new Dictionary<Vector2Int,List<Status>>();
    public void Rebuild(List<Status> actors)
    {
        foreach (var bucket in buckets.Values) bucket.Clear();
        foreach (var actor in actors)
        {
            var p = actor.transform.position;
            var key = new Vector2Int(Mathf.FloorToInt(p.x/Size),Mathf.FloorToInt(p.z/Size));
            if (!buckets.TryGetValue(key,out var bucket)) buckets[key] = bucket = new List<Status>();
            bucket.Add(actor);
        }
    }
    public IEnumerable<Status> Near(Vector3 p, float radius)
    {
        for (int x=Mathf.FloorToInt((p.x-radius)/Size);x<=Mathf.FloorToInt((p.x+radius)/Size);x++)
        for (int z=Mathf.FloorToInt((p.z-radius)/Size);z<=Mathf.FloorToInt((p.z+radius)/Size);z++)
            if (buckets.TryGetValue(new Vector2Int(x,z),out var bucket))
                for (int i=0;i<bucket.Count;i++) yield return bucket[i];
    }
}
