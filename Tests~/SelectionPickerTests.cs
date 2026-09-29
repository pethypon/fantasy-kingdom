#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
public static class SelectionPickerTests
{
 static void Check(string label,bool ok){if(!ok)throw new Exception("[Picker] "+label);Debug.Log("[Picker] PASS "+label);}
 public static void Run()
 {
  var root=new GameObject("Picker fixtures");
  try {
   Status Make(string name,float z){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root.transform);go.transform.position=new Vector3(500,0,z);var s=go.AddComponent<Status>();s.HP=s.MaxHP=20;s.team=Team.Player;s.kind=Kind.Knight;return s;}
   var front=Make("Front",2);var back=Make("Back",5);
   var duplicate=new GameObject("Extra collider",typeof(BoxCollider));duplicate.transform.SetParent(front.transform,false);
   Physics.SyncTransforms();
   var ray=new Ray(new Vector3(500,0,-10),Vector3.forward);var pointer=new Vector2(100,100);
   var picker=new UnitSelectionPicker();Predicate<Status> visible=s=>UnitSelectionPicker.IsVisibleActor(s,null);
   Check("first click chooses nearest actor",picker.Pick(ray,pointer,null,visible,out var selected,out _)&&selected==front);
   Check("second click chooses rear actor once despite duplicate colliders",picker.Pick(ray,pointer,selected,visible,out selected,out _)&&selected==back);
   Check("third click wraps to front",picker.Pick(ray,pointer,selected,visible,out selected,out _)&&selected==front);
   Check("different click location restarts front",picker.Pick(ray,pointer+Vector2.right*20,selected,visible,out selected,out _)&&selected==front);
   picker.Reset();picker.Pick(ray,pointer,null,visible,out selected,out _);
   back.team=Team.Enemy;
   Check("hidden enemy is excluded",picker.Pick(ray,pointer,selected,visible,out selected,out _)&&selected==front);
   back.team=Team.Player;
   Check("cancelled selection restarts front",picker.Pick(ray,pointer,null,visible,out selected,out _)&&selected==front);
   Check("camera move restarts front",picker.Pick(new Ray(ray.origin+Vector3.back,ray.direction),pointer,selected,visible,out selected,out _)&&selected==front);
   for(int i=0;i<70;i++){var c=new GameObject("Additional collider",typeof(BoxCollider));c.transform.SetParent(front.transform,false);}
   Physics.SyncTransforms();picker.Reset();picker.Pick(ray,pointer,null,visible,out selected,out _);
   Check("dense collider buffer grows without losing rear actor",picker.Pick(ray,pointer,selected,visible,out selected,out _)&&selected==back);
   back.HP=0;
   Check("dead rear actor ignored",picker.Pick(ray,pointer,selected,visible,out selected,out _)&&selected==front);
   Check("ordinary click is not treated as a repeat",!picker.Pick(ray,pointer+Vector2.right*20,selected,visible,out _,out _,true));
   Check("resource count keeps ordinary values",ResourceBarUI.FormatCount(9999)=="9999");
   Check("resource count abbreviates large values",ResourceBarUI.FormatCount(123456)=="12.3万"&&ResourceBarUI.FormatCount(int.MaxValue)=="21.5億");
   Debug.Log("[SelectionPickerTests] ALL PASSED");EditorApplication.Exit(0);
  } catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);} finally{UnityEngine.Object.DestroyImmediate(root);}
 }
}
#endif
