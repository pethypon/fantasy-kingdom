using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitClick : MonoBehaviour
{
    [SerializeField] PlayerMove playermove;
    [SerializeField] TurnGenerater turngenerater;
    public void UC(PlayerMove playermove,TurnGenerater turngenerater)
    {
        this.playermove = playermove;
        this.turngenerater = turngenerater;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void Click1()
    {
        //OldReset();

        //スクリーン座標を求める
        Vector3 pos = Input.mousePosition;
        //スクリーン座標にむかってカメラからRayを飛ばす
        Ray ray = Camera.main.ScreenPointToRay(pos);
        //もしもRayを飛ばしたら
        //Objが空じゃなかったら、ObjのTeamがPlayerだったら、ObjのTypeがUnitだったら
        if (Physics.Raycast(ray, out playermove.hit, 100f))
        {
            playermove.Obj = playermove.hit.transform.GetComponent<Status>();
            playermove.ObjP = playermove.hit.transform.position;
            
            if (playermove.Obj != null)
            {
                if (playermove.Obj.team == Team.Player)
                {
                    if (playermove.Obj.type == Type.Unit)
                    {
                        turngenerater.movegenerater.MoveCore(playermove.Obj, playermove.ObjP);
                        Debug.Log("<color=#00ff00ff>[Controller]<color>  OK ");
                        turngenerater.SelectUnit = playermove.Obj;
                        turngenerater.OldCell = turngenerater.SelectUnit.transform.position;
                        playermove.MenuSwitch = true;
                    }
                }
            }
        }
    }
    public void Click2()
    {
        //スクリーン座標を求める
        Vector3 pos = Input.mousePosition;
        //スクリーン座標にむかってカメラからRayを飛ばす
        Ray ray = Camera.main.ScreenPointToRay(pos);
        //もしもRayを飛ばしたら
        //Objが空じゃなかったら、ObjのTeamがPlayerだったら、ObjのTypeがUnitだったら
        if (Physics.Raycast(ray, out playermove.hit, 100f))
        {

            playermove.MP = playermove.hit.transform.GetComponent<Status>();
            if (playermove.MP != null)
            {
                if (playermove.MP.team == Team.None)
                {
                    if (playermove.MP.type == Type.MovePoint)
                    {
                        Debug.Log("<color=#00ff00ff>[Controller]</color>OK2");

                        //MPの位置をMPTに入れてSelectUnitの位置を新しくVector3を更新する
                        Vector3 MPT = playermove.MP.transform.position;
                        MPT.y += 0.47f;
                        turngenerater.SelectUnit.transform.position = new Vector3(MPT.x, MPT.y, MPT.z);
                        turngenerater.NewCell = turngenerater.SelectUnit.transform.position;
                        turngenerater.movegenerater.MoveUpdate(turngenerater.OldCell, turngenerater.NewCell);
                        //SelectUnitは駒の情報メニュー画面を出すのでけさない
                        Transform moves = turngenerater.movegenerater.Move;
                        turngenerater.movegenerater.MoveReset();
                        playermove.MP = null;
                        playermove.Obj = null;
                        playermove.MenuSwitch = false;
                    }
                }
                else if (playermove.MP.team == Team.Player)
                {
                    if (playermove.MP.type == Type.Unit)
                    {
                        //OldReset();
                        turngenerater.movegenerater.MoveReset();
                        playermove.Obj = playermove.hit.transform.GetComponent<Status>();
                        playermove.ObjP = playermove.hit.transform.position;
                        turngenerater.movegenerater.MoveCore(playermove.Obj, playermove.ObjP);
                        Debug.Log("<color=#00ff00ff>[Controller]<color>  OK ");
                        turngenerater.SelectUnit = playermove.Obj;
                        turngenerater.movegenerater.UnitPointData.Remove(turngenerater.movegenerater.Cell(turngenerater.OldCell));
                        turngenerater.OldCell = turngenerater.SelectUnit.transform.position;
                    }
                }
            }
        }
    }

 

    
}
