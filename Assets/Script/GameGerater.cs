using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameGerater : MonoBehaviour
{
    [SerializeField] MapCreate _MapCreate;
    [SerializeField] CrystalSystem _CrystalSystem;
    [SerializeField] TerritorySystem _TerritorySystem;
    [SerializeField] UnitSetting _UnitSetting;
    [SerializeField] MoveGererater _MoveGenerater;
    
   
    // Start is called before the first frame update
    void Start()
    {
        _MapCreate.noisegenerater();
        _MapCreate.BuildTop();
        _CrystalSystem.CrystalCore();
        _UnitSetting.UnitSet();
        _TerritorySystem.Territory();
        _MoveGenerater.UnitPointCore();
     
    }

}

