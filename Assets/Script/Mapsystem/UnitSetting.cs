using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UnitSetting : MonoBehaviour
{
<<<<<<< HEAD
<<<<<<< HEAD
=======

>>>>>>> parent of d903d2d (2)
    [Header("ƒLƒ“ƒO")]
    [SerializeField] GameObject KingPiece;

    [Header("ˆÙŒ`")]
    [SerializeField] GameObject StrangePiece;

    [Header("ƒ†ƒjƒbƒg”z’ueƒIƒuƒWƒFƒNƒg")]
    [SerializeField] public Transform PlayerUnit;
    [SerializeField] public Transform EnemyUnit;

    private Vector3 pcp;
    private Vector3 ecp;
    private List<Vector3> setpos;
    private List<Vector3> KingPoint;
    private List<Vector3> StrangePoint;
    private int kp;
    private int sp;
    private Vector3 KP;
    private Vector3 SP;
    public void UnitSet() 
    {
<<<<<<< HEAD
        public Kind kind;
        public UnitData data;
    }

    [Header("ƒ†ƒjƒbƒgƒf[ƒ^iKind•Êj")]
    [SerializeField] private List<UnitDataEntry> _unitDataList;

    // ŠO•”‚©‚ç‚Ì“Ç‚İæ‚èê—piGameGeneraterEBattleSystemEPlayerSummon ‚ªQÆj
    public Dictionary<Kind, UnitData> UnitDataMap { get; private set; }

    // „Ÿ„Ÿ„Ÿ ‰Šú‰» „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    private void Awake()
    {
        UnitDataMap = new Dictionary<Kind, UnitData>();
        foreach (var entry in _unitDataList)
        {
            if (entry.data != null)
                UnitDataMap[entry.kind] = entry.data;
            else
                Debug.LogWarning($"[UnitSetting] Kind:{entry.kind} ‚ÌUnitData‚ªnull‚Å‚·");
        }
    }

    // „Ÿ„Ÿ„Ÿ ‹¤’Ê¶¬ƒƒ\ƒbƒh „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ

    /// <summary>
    /// ƒ†ƒjƒbƒg‚ğ¶¬‚µ‚ÄƒXƒe[ƒ^ƒX‚ğ‘¦À‚É“K—p‚·‚éB
    /// ƒQ[ƒ€’†‚ÌV‹K¶¬‚Í‚·‚×‚Ä‚±‚Ìƒƒ\ƒbƒhŒo—R‚Ås‚¤i‹î‚Ì¶¬‚Ì“K—p‚ğ’S“–jB
    /// </summary>
    public GameObject SpawnUnit(GameObject prefab, Vector3 pos,
                                Transform parent, int level = 1)
    {
        var obj = Instantiate(prefab, pos, Quaternion.identity, parent);

        var status = obj.GetComponentInChildren<Status>();
        if (status == null)
        {
            Debug.LogWarning($"[UnitSetting] {prefab.name} ‚ÉStatus‚ªŒ©‚Â‚©‚è‚Ü‚¹‚ñ");
            return obj;
        }

        if (UnitDataMap.TryGetValue(status.kind, out UnitData data))
            data.ApplyToStatus(status, level);
        else
            Debug.LogWarning($"[UnitSetting] Kind:{status.kind} ‚ÌUnitData‚ª–¢“o˜^‚Å‚·");

        return obj;
    }

    // „Ÿ„Ÿ„Ÿ ƒQ[ƒ€ŠJn‚Ìƒ†ƒjƒbƒg”z’u „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    public void UnitSet()
    {
        // PCPAECPASetPos ‚ğæ‚èo‚·
=======
    [Header("ã‚­ãƒ³ã‚°")]
    [SerializeField] GameObject KingPiece;
    [Header("ç•°å½¢")]
    [SerializeField] GameObject StrangePiece;

    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆé…ç½®è¦ªã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆ")]
    [SerializeField] public Transform PlayerUnit;
    [SerializeField] public Transform EnemyUnit;

    // SerializeField ã‚’11å€‹ä¸¦ã¹ãªã„ç†ç”±ï¼š
    // æ–°ã—ã„ Kind ã‚’è¿½åŠ ã™ã‚‹ãŸã³ã«ãƒ•ã‚£ãƒ¼ãƒ«ãƒ‰è¿½åŠ ã¨Inspectorè¨­å®šã®2æ‰‹é–“ãŒç™ºç”Ÿã™ã‚‹ã€‚
    // Dictionary ãªã‚‰ãƒªã‚¹ãƒˆã«1ã‚¨ãƒ³ãƒˆãƒªè¿½åŠ ã™ã‚‹ã ã‘ã§æ¸ˆã‚€ï¼ˆè¨­è¨ˆåŸå‰‡2ï¼‰ã€‚
    [System.Serializable]
    public class UnitDataEntry
    {
        public Kind kind;
        public UnitData data;
    }

    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆãƒ‡ãƒ¼ã‚¿ï¼ˆKindåˆ¥ï¼‰")]
    [SerializeField] private List<UnitDataEntry> _unitDataList;

    // å¤–éƒ¨ã‹ã‚‰ã®èª­ã¿å–ã‚Šå°‚ç”¨ï¼ˆGameGeneraterãƒ»BattleSystemãƒ»PlayerSummon ãŒå‚ç…§ï¼‰
    public Dictionary<Kind, UnitData> UnitDataMap { get; private set; }

    // â”€â”€â”€ åˆæœŸåŒ– â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private void Awake()
    {
        UnitDataMap = new Dictionary<Kind, UnitData>();
        if (_unitDataList == null) return;
        foreach (var entry in _unitDataList)
        {
            if (entry.data != null)
                UnitDataMap[entry.kind] = entry.data;
            else
                Debug.LogWarning($"[UnitSetting] Kind:{entry.kind} ã®UnitDataãŒnullã§ã™");
        }
    }

    // â”€â”€â”€ å…±é€šç”Ÿæˆãƒ¡ã‚½ãƒƒãƒ‰ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// ãƒ¦ãƒ‹ãƒƒãƒˆã‚’ç”Ÿæˆã—ã¦ã‚¹ãƒ†ãƒ¼ã‚¿ã‚¹ã‚’å³åº§ã«é©ç”¨ã™ã‚‹ã€‚
    /// ã‚²ãƒ¼ãƒ ä¸­ã®æ–°è¦ç”Ÿæˆã¯ã™ã¹ã¦ã“ã®ãƒ¡ã‚½ãƒƒãƒ‰çµŒç”±ã§è¡Œã†ï¼ˆé§’ã®ç”Ÿæˆæ™‚ã®é©ç”¨ã‚’æ‹…å½“ï¼‰ã€‚
    /// </summary>
    public GameObject SpawnUnit(GameObject prefab, Vector3 pos,
                                Transform parent, int level = 1)
    {
        var obj = Instantiate(prefab, pos, Quaternion.identity, parent);

        var status = obj.GetComponentInChildren<Status>();
        if (status == null)
        {
            Debug.LogWarning($"[UnitSetting] {prefab.name} ã«StatusãŒè¦‹ã¤ã‹ã‚Šã¾ã›ã‚“");
            return obj;
        }

        if (UnitDataMap.TryGetValue(status.kind, out UnitData data))
            data.ApplyToStatus(status, level);
        else
            Debug.LogWarning($"[UnitSetting] Kind:{status.kind} ã®UnitDataãŒæœªç™»éŒ²ã§ã™");

        return obj;
    }

    // â”€â”€â”€ ã‚²ãƒ¼ãƒ é–‹å§‹æ™‚ã®ãƒ¦ãƒ‹ãƒƒãƒˆé…ç½® â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public void UnitSet()
    {
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
=======
        //PCPAECPASetPos‚ğæ‚èo‚·
>>>>>>> parent of d903d2d (2)
        CrystalSystem crystalsystem = GetComponent<CrystalSystem>();
        MapCreate mapcreate = GetComponent<MapCreate>();
        pcp = crystalsystem.PCP;
        ecp = crystalsystem.ECP;
        setpos = mapcreate.SetPos;

<<<<<<< HEAD
<<<<<<< HEAD
        // ”z’uˆÊ’u‚ği‚éiKingPointFPCPü•Ó1ƒ}ƒXˆÈ“àj
=======
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        var KingPoint = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - pcp.x);
            float pz = Mathf.Abs(p.z - pcp.z);
            return px <= 1 && pz <= 1 && p != pcp;
        }).ToList();

<<<<<<< HEAD
        // ”z’uˆÊ’u‚ği‚éiStrangePointFECPü•Ó1ƒ}ƒXˆÈ“àj
=======
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        var StrangePoint = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - ecp.x);
            float pz = Mathf.Abs(p.z - ecp.z);
            return px <= 1 && pz <= 1 && p != ecp;
        }).ToList();

<<<<<<< HEAD
        // Instantiate ¨ SpawnUnit ‚É’u‚«Š·‚¦iƒXƒe[ƒ^ƒX“K—p‚İj
        Vector3 KP = KingPoint[Random.Range(0, KingPoint.Count)];
        SpawnUnit(KingPiece, KP, PlayerUnit);
=======
        //Where‚Å”z’uˆÊ’u‚ği‚é
        KingPoint = setpos.Where
            (p =>
            {
                float px = Mathf.Abs(p.x - pcp.x);
                float pz = Mathf.Abs(p.z - pcp.z);
                bool truex = px <= 1;
                bool truez = pz <= 1 && p != pcp;

                return truex && truez;
            }
            ).ToList();


        StrangePoint = setpos.Where
            (p =>
            {
               float px = Mathf.Abs(p.x - ecp.x);
               float pz = Mathf.Abs(p.z - ecp.z);
               bool truex = px <= 1;
               bool truez = pz <= 1 && p != ecp;

               return truex && truez;
            }
            ).ToList();

        kp = Random.Range(0,KingPoint.Count);
        KP = KingPoint[kp];
        Instantiate(KingPiece, KP, Quaternion.identity,PlayerUnit);
>>>>>>> parent of d903d2d (2)
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>‰¤İ’u");

        sp = Random.Range(0,StrangePoint.Count);
        SP = StrangePoint[sp];
        Instantiate(StrangePiece,SP,Quaternion.identity,EnemyUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>ˆÙŒ`‚Ì‰¤İ’u");
<<<<<<< HEAD
=======
        Vector3 KP = KingPoint[Random.Range(0, KingPoint.Count)];
        SpawnUnit(KingPiece, KP, PlayerUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>ç‹è¨­ç½®");

        Vector3 SP = StrangePoint[Random.Range(0, StrangePoint.Count)];
        SpawnUnit(StrangePiece, SP, EnemyUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>ç•°å½¢ã®ç‹è¨­ç½®");
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
=======

>>>>>>> parent of d903d2d (2)
    }
}
