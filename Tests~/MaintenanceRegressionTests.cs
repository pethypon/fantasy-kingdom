#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class MaintenanceRegressionTests
{
    static void Check(string name, bool value)
    {
        if (!value) throw new Exception("[Maintenance] FAIL " + name);
        Debug.Log("[Maintenance] PASS " + name);
    }
    public static void Run()
    {
        var root = new GameObject("Maintenance fixture");
        var prefab = new GameObject("Pool fixture");
        var pool = ObjectPool.Instance;
        try
        {
            var checkedOut = pool.Get(prefab, Vector3.zero, Quaternion.identity, root.transform);
            pool.ClearAll();
            pool.Return(checkedOut);
            Check("clear preserves checked-out identity", pool.Get(prefab, Vector3.zero, Quaternion.identity, root.transform) == checkedOut);
            pool.Return(checkedOut);
            var second = pool.Get(prefab, Vector3.zero, Quaternion.identity, root.transform);
            var third = pool.Get(prefab, Vector3.zero, Quaternion.identity, root.transform);
            pool.Return(second); pool.Return(third);
            UnityEngine.Object.DestroyImmediate(second);
            Check("destroyed queue entry skips to reusable item", pool.Get(prefab, Vector3.zero, Quaternion.identity, root.transform) == third);
            pool.Return(third); pool.Return(third);
            var a = pool.Get(prefab, Vector3.zero, Quaternion.identity, root.transform);
            var b = pool.Get(prefab, Vector3.zero, Quaternion.identity, root.transform);
            Check("duplicate return never lends same object twice", a != b);
            pool.Return(a); pool.Return(b); pool.ClearAll();

            var moves = root.AddComponent<MoveGenerator>();
            moves.Move = new GameObject("Markers").transform; moves.Move.SetParent(root.transform);
            var marker = new GameObject("Marker"); marker.transform.SetParent(moves.Move);
            marker.transform.position = new Vector3(10000, .53f, 10000);
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube); child.transform.SetParent(marker.transform,false);
            child.transform.localScale = new Vector3(1,.05f,1);
            var positions = (List<Vector3>)typeof(MoveGenerator).GetField("_movePositions",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(moves);
            positions.Add(new Vector3(10000,1,10000));
            var ray = new Ray(new Vector3(10000,10,10000),Vector3.down);
            Physics.SyncTransforms();
            Check("child collider resolves canonical destination", moves.TryGetMoveDestination(ray,out _,out var destination) && destination == positions[0]);
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.transform.SetParent(root.transform);
            blocker.transform.position = new Vector3(10000,3,10000); Physics.SyncTransforms();
            Check("occluded marker cannot be clicked or previewed", !moves.TryGetMoveDestination(ray,out _,out _));
            blocker.SetActive(false); positions.Clear(); Physics.SyncTransforms();
            Check("stale marker has no legal destination", !moves.TryGetMoveDestination(ray,out _,out _));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(prefab); }
    }
}
#endif
