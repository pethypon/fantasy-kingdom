using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TurnGenerater : MonoBehaviour
{
    public Status SelectUnit;
    public Vector3 OldCell;
    public Vector3 NewCell;
    [Header("保持するステート")]
    [SerializeField] StateCore StateManager;

    [Header("アタックポイント")]
    [SerializeField] public int PlayerAP = 15;
    [SerializeField] public int EnemyAP = 15;
    [Header("プレイヤーアタックポイント　増減用")]
    [SerializeField] public int PlayerPlusAP = 0;
    [SerializeField] public int PlayerMinusAP = 0;
    [Header("エネミーアタックポイント　増減用")]
    [SerializeField] public int EnemyPlusAP = 0;
    [SerializeField] public int EnemyMinusAP = 0;
    [Header("アタックポイントリセット用")]
    [SerializeField] public int ResetAP = 15;
    [Header("ターン管理")]
    [SerializeField] public int Turn = 0;

    //PlayerMove用
    [Header("ユニットステータス")]
    [SerializeField] public Status status;

    [Header("ムーブ")]
    [SerializeField] public MoveGererater movegenerater;

    [Header("ユニットクリック")]
    [SerializeField] public UnitClick unitclick;

    [Header("マップクリエイト")]
    [SerializeField] public MapCreate mapcreate;

    [Header("アタックポイント")]
    [SerializeField] public AttackPointt attackpoint;

    [Header("カメラ（動かす対象）")]
    [SerializeField] public Transform CameraObject;

    public void ChangeState(StateCore next)
    {
        StateManager?.Exit();
        StateManager = next;
        StateManager?.Entry();
    }
    void Start()
    {
        PlayerAP = ResetAP;
        EnemyAP = ResetAP;
        ChangeState(new PlayerStart(this,unitclick,attackpoint));
        //このコードがpublic void ChangeState(StateCore next)のnextを指定する
    }


    void Update()
    {
        StateManager?.Update();
    }
}
