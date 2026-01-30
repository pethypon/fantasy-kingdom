using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class VisionGenerater : MonoBehaviour
{
    public List<Vector3> _setpos;
    public List<Status> unitbox;
    public List<Vector3> visionp;
    public RaycastHit visionhit;
    public Status VisionBox;

    [Header("マップクリエイト")]
    [SerializeField] MapCreate mapcreate;

    [Header("ムーブジェネレーター")]
    [SerializeField] MoveGererater movegenerater;

    [Header("プレイヤームーブ")]
    [SerializeField] PlayerMove playermove;

    [Header("ユニットボックス")]
    [SerializeField] Transform PlayerUnit;
    [SerializeField] Transform EnemyUnit;


    public void VisionPoint(MapCreate mapcreate,MoveGererater movegenerater,PlayerMove playermove)
    {
        this.mapcreate = mapcreate;
        this.movegenerater = movegenerater;
        this.playermove = playermove;

        //setposにSetPosを参照
        _setpos = mapcreate.SetPos;
        if(unitbox == null)
        {
            unitbox = new List<Status>();
        }
        else
        {
            unitbox.Clear();
        }

        //Player、Enemyの駒を入れる箱に入ってる駒のStatusを獲得する
        foreach (Transform child in PlayerUnit.transform)
        {
            Status childgetstatus = (child.gameObject.transform.GetComponentInChildren<Status>());
            if (childgetstatus != null)
            {
                unitbox.Add(childgetstatus);
            }

        }

        foreach(Transform child in EnemyUnit.transform)
        {
            if (child != null)
            {
                Status childgetstatus = (child.gameObject.transform.GetComponentInChildren<Status>());
                if (childgetstatus != null)
                {
                    unitbox.Add(childgetstatus);
                }
            }
                
        }

        //駒のVisionCellの中身を空にする
        foreach (Status status in unitbox)
        {
            if (status == null) continue;

            if (status.VisionCell == null)
               {
                   status.VisionCell = new HashSet<Vector3>();

               }
            
            status.VisionCell.Clear();
            VisionCreate(status);
        }


    }

    public void VisionCreate(Status status)
    {
        visionp = new List<Vector3>();
        switch (status.kind)
        {
         
            case Kind.King:
                Debug.Log("<color=#00ff00ff>[Controller]</color>King");
                visionp = _setpos.Where
                    (p => 
                    {
                        float px = Mathf.RoundToInt(p.x - status.transform.position.x);
                        float pz = Mathf.RoundToInt(p.z - status.transform.position.z);
                        bool visionx = px >= -1 && px <= 1;
                        bool visionz = (pz >= 1 && pz <= 3);
                        return visionx && visionz;
                    }
                    ).ToList();
                
                //foreach文でvisionpを見てRayを飛ばして障害物を割り出す
                foreach(Vector3 p in visionp)
                {
                    VisionBox = null;

                    //Physics.Raycastに使う開始位置、方向、結果を入れる箱、距離を求める
                    Vector3 Start = status.transform.position + Vector3.up * 0.5f;
                    Vector3 Goal = p + Vector3.up * 0.5f;
                    float pointx = Goal.x - Start.x;
                    float pointy = Goal.y - Start.y;
                    float pointz = Goal.z - Start.z;
                    Vector3 Point = new Vector3(pointx,pointy,pointz);
                    Point = Point.normalized;
                    Vector3 _distance = Goal - Start;
                    float Distance = _distance.magnitude;
                    //magnitude ベクトルの長さ

                    if (Physics.Raycast(Start, Point, out visionhit, Distance))
                    {
                        
                        if (visionhit.transform.GetComponent<Status>() != null)
                        {
                            VisionBox = visionhit.transform.GetComponent<Status>();
                            if (VisionBox.team == Team.Obstacle && VisionBox.type == Type.Obstacle)
                            {

                            }
                            else
                            {
                                status.VisionCell.Add(p);
                            }
                        }

                        
                        
                    }
                    else
                    {
                        status.VisionCell.Add(p);
                    }
                }
                break;

            case Kind.Knight:
                
                break;

            case Kind.Archer:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Archer");
                
                break;

            case Kind.Magic:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Archer");
               
                break;

            case Kind.Assassin:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Assassin");
               
                break;


            case Kind.Scout:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Scout");
               
                break;

            case Kind.Priest:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Priest");
               
                break;

            case Kind.Guardian:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Guardian");
                
                break;

            case Kind.Crossbow:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Crossbow");
               
                break;

            case Kind.Magicsniper:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Magicsniper");
               
                break;

            case Kind.Bomber:
                Debug.Log("<color=#00ff00ff>[Controller]</color>Bomber");
               
                break;
        }
    }
}
