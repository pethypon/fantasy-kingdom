using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapCreate : MonoBehaviour
{
    [Header("土ブロック")]
    [SerializeField] public GameObject dirtPrefab;

    [Header("石ブロック")]
    [SerializeField] GameObject stonePrefab;

    [Header("マップ親オブジェクト")]
    [SerializeField] Transform MapBox;

    [Header("マップサイズ")]
    [SerializeField] public int maxX = 80;
    [SerializeField] public int maxY = 20;
    [SerializeField] public int maxZ = 80;

    [Header("シード値")]
    [SerializeField] private float seedx , seedz;

    [Header("ノイズ設定")]
    [SerializeField] public float noiseScale = 0.1f;

    [Header("マップの平均的な高さ")]
    [SerializeField] public int AverageFoundation = 2;

    [Header("振れ幅")]
    [SerializeField] public int Amplitude = 6;

    private int[,] topY;
    private Vector3 pos;
    private int y;
    private int DownY;
    private Vector3 DownPos;
    public List<Vector3> SetPos;
    
    //public bool SetMap;
    private void Awake()
    {
        SetPos = new List<Vector3>();
        
    }

    public void noisegenerater() 
    {
        seedx = Random.Range(0f, 1000000f);
        seedz = Random.Range(0f, 1000000f);

        topY = new int[maxX, maxZ];
        //maxX,maxZの値までx,zを増やしてノイズを作って整数にして綺麗な並びにする
        for (int x = 0; x < maxX;x ++) 
        {
            for (int z = 0;z < maxZ;z ++) 
            {
                
                //perlinnoiseの計算
                //perlinnoise（（x+seedx）*noisescale,(z+seedz)*noisescale）
                int PerlinN = Mathf.RoundToInt(Mathf.PerlinNoise(x * noiseScale + seedx, z * noiseScale + seedz) * Amplitude);
                //PerlinNに土台の平均をプラスする。
                int high = AverageFoundation + PerlinN;
                high = Mathf.Clamp(high, 0, maxY - 1);
                topY[x, z] = high;
            }
        }
        
    }

    public void BuildTop() 
    {
        for (int x = 0; x <= maxX-1; x ++)
        {
            for (int z = 0; z <= maxZ-1; z++) 
            {
                y = topY[x, z];
                pos = new Vector3(x, y, z);
                Instantiate(dirtPrefab,pos,Quaternion.identity,MapBox);
                SetPos.Add(new Vector3Int(x,y+1,z));
                DownY = y - 1;
                DownPos = new Vector3(x, DownY, z);
                Instantiate(dirtPrefab, DownPos, Quaternion.identity, MapBox);
                Debug.Log("<color=#ffff00ff>[StartSetting]</color>マップ完成");
            }

        }
        //bool SetMap = true;
      
    }
    
}
