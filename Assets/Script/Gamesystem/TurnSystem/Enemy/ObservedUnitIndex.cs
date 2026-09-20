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
    public Nearby Near(Vector3 p, float radius) => new Nearby(buckets,p,radius);
    public struct Nearby
    {
        readonly Dictionary<Vector2Int,List<Status>> buckets;
        readonly int minX,maxX,minZ,maxZ;
        int x,z,index; List<Status> bucket;
        public Status Current { get; private set; }
        internal Nearby(Dictionary<Vector2Int,List<Status>> source,Vector3 p,float radius)
        {
            buckets=source;minX=Mathf.FloorToInt((p.x-radius)/Size);maxX=Mathf.FloorToInt((p.x+radius)/Size);
            minZ=Mathf.FloorToInt((p.z-radius)/Size);maxZ=Mathf.FloorToInt((p.z+radius)/Size);
            x=minX;z=minZ;index=0;bucket=null;Current=null;
        }
        public Nearby GetEnumerator() => this;
        public bool MoveNext()
        {
            while(true) {
                if(bucket != null && index<bucket.Count) {Current=bucket[index++];return true;}
                if(x>maxX) return false;
                buckets.TryGetValue(new Vector2Int(x,z),out bucket);index=0;
                if(++z>maxZ) {z=minZ;x++;}
            }
        }
    }
}
