#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
public static class VisualRegressionTests
{
    static void Check(string name,bool ok) {if(!ok) throw new Exception("[VisualRegression] "+name);Debug.Log("[VisualRegression] PASS "+name);}
    static void Capture(GameSystems s,string name,Vector3 position,Vector3 target)
    {
        var camera=Camera.main;var oldPos=camera.transform.position;var oldRot=camera.transform.rotation;var oldRT=camera.targetTexture;var active=RenderTexture.active;
        var rt=new RenderTexture(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
        camera.transform.position=position;camera.transform.LookAt(target);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();
        var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../visual-captures"));System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name),tex.EncodeToPNG());
        camera.targetTexture=oldRT;RenderTexture.active=active;camera.transform.SetPositionAndRotation(oldPos,oldRot);UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);
    }
    public static void Run(GameSystems s)
    {
        var pool=ObjectPool.Instance;
        var prefab=new GameObject("Pool fixture");var parent=new GameObject("Marker fixture");
        for(int cycle=0;cycle<100;cycle++) {
            var first=pool.Get(prefab,Vector3.zero,Quaternion.identity,parent.transform);
            var second=pool.Get(prefab,Vector3.one,Quaternion.identity,parent.transform);
            Check("distinct markers cycle "+cycle,first!=second&&first.activeSelf&&second.activeSelf&&parent.transform.childCount==2);
            pool.ReturnAllChildren(parent.transform);pool.Return(first);pool.ReturnAllChildren(parent.transform);
            Check("returned markers detached cycle "+cycle,parent.transform.childCount==0);
        }
        UnityEngine.Object.DestroyImmediate(parent);UnityEngine.Object.DestroyImmediate(prefab);
        var menu=GameMenuUI.Instance;menu.Open();
        var overlay=(GameObject)typeof(GameMenuUI).GetField("overlay",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(menu);
        Check("menu belongs to screen canvas",overlay.GetComponentInParent<Canvas>()==UIBuilder.ScreenCanvas&&UIBuilder.ScreenCanvas.renderMode==RenderMode.ScreenSpaceOverlay);
        menu.Close();
        var units=s.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>();
        var actor=units[0];var ally=units[1];var origin=actor.transform.position;var allyOrigin=ally.transform.position;var kind=actor.kind;
        actor.kind=Kind.King;
        var adjacent=s.MapCreate.SetPos.First(p=>AttackPatterns.NormalMap[Kind.King](p.x-origin.x,(p.z-origin.z)*MovePatterns.DirZ(actor.direction))&&s.MapCreate.CanTraverse(origin,p));
        ally.transform.position=adjacent;
        s.AttackGenerator.NormalAttackPData(actor,origin);
        Check("ally has no normal attack marker",!s.AttackGenerator.AttackP.Any(p=>GridHelper.MatchXZ(p,GridHelper.ToGridXZ(adjacent))));
        ally.team=Team.Enemy;s.AttackGenerator.NormalAttackPData(actor,origin);
        Check("hostile in same cell has attack marker",s.AttackGenerator.AttackP.Any(p=>GridHelper.MatchXZ(p,GridHelper.ToGridXZ(adjacent))));
        ally.team=Team.Player;ally.transform.position=allyOrigin;actor.kind=kind;
        for(int i=0;i<12;i++) {
            actor.transform.position=i%2==0?adjacent:origin;s.RefreshVision();
            var cell=GridHelper.ToGridXZ(actor.transform.position);
            Check("moved cell revealed immediately "+i,s.VisionGenerator.IsInVisionXZ(Team.Player,cell)&&s.MapCreate.FogChunks.GetState(cell.x,cell.z)==2);
            s.MoveGenerator.MoveReset();s.MoveGenerator.UnitPointCore();s.MoveGenerator.MoveCore(actor,actor.transform.position);
        }
        actor.transform.position=origin;s.MoveGenerator.MoveReset();s.AttackGenerator.AtkpDestroy();s.RefreshVision();
        var lowFog=s.MapCreate.FogChunks.GetComponentsInChildren<MeshFilter>();bool above=true;
        foreach(var f in lowFog) foreach(var v in f.sharedMesh.vertices) {
            int x=Mathf.Clamp(Mathf.RoundToInt(v.x),0,s.MapCreate.maxX-1),z=Mathf.Clamp(Mathf.RoundToInt(v.z),0,s.MapCreate.maxZ-1);
            // All fog now starts above the terrain foundation, never on its bottom plane.
            if(v.y<s.MapCreate.minY+0.5f) above=false;
        }
        Check("fog never overlaps terrain underside",above);
        if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null) {
            Capture(s,"map-low-angle.png",new Vector3(s.MapCreate.maxX/2f,-3,-20),new Vector3(s.MapCreate.maxX/2f,1,s.MapCreate.maxZ/2f));
            Capture(s,"map-playing.png",Camera.main.transform.position,Camera.main.transform.position+Camera.main.transform.forward*10);
        }
    }
}
#endif
