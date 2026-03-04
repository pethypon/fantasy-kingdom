using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MoveGererater : MonoBehaviour
{
    [SerializeField] public MapCreate mapcreate;
    [SerializeField] public TurnGenerater turngenerater;

<<<<<<< HEAD
    [Header("ˆÚ“®ˆÊ’u•\¦‚ÌƒIƒuƒWƒFƒNƒg")]
    [SerializeField] public GameObject MovePoint;

    [Header("ƒ€[ƒueƒIƒuƒWƒFƒNƒg")]
    [SerializeField] public Transform Move;

    [Header("ƒ†ƒjƒbƒgÀ•W")]
    [SerializeField] public HashSet<Vector3> UnitPointData = new HashSet<Vector3>();

    [Header("ƒNƒŠƒXƒ^ƒ‹ƒVƒXƒeƒ€")]
    [SerializeField] CrystalSystem crystalsystem;

    [Header("ƒ†ƒjƒbƒgƒZƒbƒeƒBƒ“ƒO")]
    [SerializeField] UnitSetting unitsetting;

    [Header("ƒ†ƒjƒbƒgƒ{ƒbƒNƒX")]
=======
    [Header("ç§»å‹•ä½ç½®è¡¨ç¤ºã®ã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆ")]
    [SerializeField] public GameObject MovePoint;

    [Header("ãƒ ãƒ¼ãƒ–è¦ªã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆ")]
    [SerializeField] public Transform Move;

    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆåº§æ¨™")]
    [SerializeField] public HashSet<Vector3> UnitPointData = new HashSet<Vector3>();

    [Header("ãƒ—ãƒ¬ã‚¤ãƒ¤ãƒ¼ãƒ¦ãƒ‹ãƒƒãƒˆåº§æ¨™ï¼ˆPriestå›å¾©å¯¾è±¡æ¤œç´¢ç”¨ï¼‰")]
    public HashSet<Vector3> PlayerUnitPointData = new HashSet<Vector3>();

    [Header("ã‚¯ãƒªã‚¹ã‚¿ãƒ«ã‚·ã‚¹ãƒ†ãƒ ")]
    [SerializeField] CrystalSystem crystalsystem;

    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆã‚»ãƒƒãƒ†ã‚£ãƒ³ã‚°")]
    [SerializeField] UnitSetting unitsetting;

    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆãƒœãƒƒã‚¯ã‚¹")]
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    [SerializeField] Transform PlayerUnit;
    [SerializeField] Transform EnemyUnit;

    public List<Vector3> setpos;
    public List<Vector3> MoveUnitP;
    public Vector3 objp;
    private Status obj;
    public Vector3 pcp;
    public Vector3 ecp;
    public Vector3 usp;

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ‹îí‚²‚Æ‚ÌˆÚ“®”»’è „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    // dx = p.x - objp.xi•„†•t‚«j, dz = p.z - objp.zi•„†•t‚«j
    // V‚µ‚¢‹î‚ğ’Ç‰Á‚·‚éê‡‚Í‚±‚±‚É1s’Ç‰Á‚·‚é‚¾‚¯‚Å‚æ‚¢
    static readonly Dictionary<Kind, Func<float, float, bool>> MovePredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // ‘S•ûŒü1ƒ}ƒX
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1
                                     && Mathf.Abs(dz) <= 1 },

        // ã‰º¶‰E1ƒ}ƒX
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) + Mathf.Abs(dz) == 1 },

        // Î‚ß1ƒ}ƒX
        { Kind.Archer,      (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // Î‚ß1ƒ}ƒXiArcher‚Æ“¯‚¶ˆÚ“®j
        { Kind.Magic,       (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // Œj”n’µ‚Ñi2~1 or 1~2j
        { Kind.Assassin,    (dx, dz) => (Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 1)
                                     || (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) == 2) },

        // ‰¡}1{‘OŒã1 or ’¼i2
        { Kind.Scout,       (dx, dz) => (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) <= 1)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // ‘OÎ‚ß1 or Œã‚ë’¼i1iŒü‚«l—¶F•„†•t‚«j
        { Kind.Priest,      (dx, dz) => (Mathf.Abs(dx) == 1 && dz == 1)
                                     || (dx == 0 && dz == -1) },

        // ¶‰E2ƒ}ƒX or ‘OŒã1ƒ}ƒX
        { Kind.Guardian,    (dx, dz) => (Mathf.Abs(dx) <= 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 1) },

        // ‘O’¼i1-3ƒ}ƒX or Î‚ßŒã‚ë1ƒ}ƒXi•„†•t‚«j
        { Kind.Crossbow,    (dx, dz) => (dx == 0 && (dz == 1 || dz == 2 || dz == 3))
                                     || (Mathf.Abs(dx) == 1 && dz == -1) },

        // ‰E‘OÎ‚ß1 or ¶‰EŒã‚ë3ƒ}ƒXi•„†•t‚«j
        { Kind.Magicsniper, (dx, dz) => (dx == 1 && dz == 1)
                                     || (Mathf.Abs(dx) == 3 && dz == -1) },

        // Î‚ß‘OŒã2ƒpƒ^[ƒ“i•„†•t‚«j
=======
    // â”€â”€â”€ é§’ç¨®ã”ã¨ã®ç§»å‹•åˆ¤å®š â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // dx = p.x - objp.xï¼ˆç¬¦å·ä»˜ãï¼‰, dz = p.z - objp.zï¼ˆç¬¦å·ä»˜ãï¼‰
    // æ–°ã—ã„é§’ã‚’è¿½åŠ ã™ã‚‹å ´åˆã¯ã“ã“ã«1è¡Œè¿½åŠ ã™ã‚‹ã ã‘ã§ã‚ˆã„
    public static readonly Dictionary<Kind, Func<float, float, bool>> MovePredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // å…¨æ–¹å‘1ãƒã‚¹
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1
                                     && Mathf.Abs(dz) <= 1 },

        // ä¸Šä¸‹å·¦å³1ãƒã‚¹
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) + Mathf.Abs(dz) == 1 },

        // æ–œã‚1ãƒã‚¹
        { Kind.Archer,      (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // æ–œã‚1ãƒã‚¹ï¼ˆArcherã¨åŒã˜ç§»å‹•ï¼‰
        { Kind.Magic,       (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // æ¡‚é¦¬è·³ã³ï¼ˆ2Ã—1 or 1Ã—2ï¼‰
        { Kind.Assassin,    (dx, dz) => (Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 1)
                                     || (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) == 2) },

        // æ¨ªÂ±1ï¼‹å‰å¾Œ1 or ç›´é€²2
        { Kind.Scout,       (dx, dz) => (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) <= 1)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // å‰æ–œã‚1 or å¾Œã‚ç›´é€²1ï¼ˆå‘ãè€ƒæ…®ï¼šç¬¦å·ä»˜ãï¼‰
        { Kind.Priest,      (dx, dz) => (Mathf.Abs(dx) == 1 && dz == 1)
                                     || (dx == 0 && dz == -1) },

        // å·¦å³2ãƒã‚¹ or å‰å¾Œ1ãƒã‚¹
        { Kind.Guardian,    (dx, dz) => (Mathf.Abs(dx) <= 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 1) },

        // å‰ç›´é€²1-3ãƒã‚¹ or æ–œã‚å¾Œã‚1ãƒã‚¹ï¼ˆç¬¦å·ä»˜ãï¼‰
        { Kind.Crossbow,    (dx, dz) => (dx == 0 && (dz == 1 || dz == 2 || dz == 3))
                                     || (Mathf.Abs(dx) == 1 && dz == -1) },

        // å³å‰æ–œã‚1 or å·¦å³å¾Œã‚3ãƒã‚¹ï¼ˆç¬¦å·ä»˜ãï¼‰
        { Kind.Magicsniper, (dx, dz) => (dx == 1 && dz == 1)
                                     || (Mathf.Abs(dx) == 3 && dz == -1) },

        // æ–œã‚å‰å¾Œ2ãƒ‘ã‚¿ãƒ¼ãƒ³ï¼ˆç¬¦å·ä»˜ãï¼‰
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        { Kind.Bomber,      (dx, dz) => (dx == -1 && dz ==  1) || (dx ==  2 && dz ==  2)
                                     || (dx ==  1 && dz == -1) || (dx == -2 && dz == -2) },
    };

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ƒ†ƒjƒbƒgè—LÀ•W‚ÌXV „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ ãƒ¦ãƒ‹ãƒƒãƒˆå æœ‰åº§æ¨™ã®æ›´æ–° â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void UnitPointCore()
    {
        UnitPointData.Clear();
        PlayerUnitPointData.Clear();
        pcp = crystalsystem.PCP;
        ecp = crystalsystem.ECP;
        UnitPointData.Add(Cell(pcp));
        UnitPointData.Add(Cell(ecp));

        foreach (Status us in PlayerUnit.GetComponentsInChildren<Status>())
        {
            if (us.type != Type.Unit) continue;
            usp = us.transform.position;
            UnitPointData.Add(Cell(usp));
<<<<<<< HEAD
        }
        foreach (Status us in EnemyUnit.GetComponentsInChildren<Status>())
        {
            if (us.type != Type.Unit) continue;
            usp = us.transform.position;
            UnitPointData.Add(Cell(usp));
        }
    }

    // „Ÿ„Ÿ„Ÿ ƒOƒŠƒbƒhÀ•W‚Ö‚ÌŠÛ‚ß „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
            PlayerUnitPointData.Add(Cell(usp));
        }
        foreach (Status us in EnemyUnit.GetComponentsInChildren<Status>())
        {
            if (us.type != Type.Unit) continue;
            usp = us.transform.position;
            UnitPointData.Add(Cell(usp));
        }

        // Phase 2: å£ã‚’ãƒ¦ãƒ‹ãƒƒãƒˆã¨åŒæ§˜ã«ç§»å‹•ä¸å¯ãƒã‚¹ã¨ã—ã¦ç™»éŒ²ã™ã‚‹
        foreach (Transform parent in new Transform[] { PlayerUnit, EnemyUnit })
        {
            foreach (Status s in parent.GetComponentsInChildren<Status>())
            {
                if (s.type == Type.Wall && s.gameObject.activeSelf)
                    UnitPointData.Add(Cell(s.transform.position));
            }
        }
    }

    // â”€â”€â”€ ã‚°ãƒªãƒƒãƒ‰åº§æ¨™ã¸ã®ä¸¸ã‚ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public Vector3 Cell(Vector3 v)
    {
        return new Vector3(Mathf.RoundToInt(v.x), 0f, Mathf.RoundToInt(v.z));
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ˆÚ“®”ÍˆÍ‚ÌŒvZ‚ÆƒIƒuƒWƒFƒNƒg¶¬ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ ç§»å‹•ç¯„å›²ã®è¨ˆç®—ã¨ã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆç”Ÿæˆ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void MoveCore(Status Obj, Vector3 ObjP)
    {
        setpos = mapcreate.SetPos;
        MoveUnitP.Clear();
        obj = Obj;
        objp = ObjP;

        if (!MovePredicateMap.TryGetValue(obj.kind, out Func<float, float, bool> predicate))
        {
<<<<<<< HEAD
            Debug.LogWarning($"[MoveGererater] Kind '{obj.kind}' ‚ÌˆÚ“®ƒpƒ^[ƒ“‚ª–¢’è‹`‚Å‚·");
=======
            Debug.LogWarning($"[MoveGererater] Kind '{obj.kind}' ã®ç§»å‹•ãƒ‘ã‚¿ãƒ¼ãƒ³ãŒæœªå®šç¾©ã§ã™");
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
            return;
        }

        Debug.Log($"<color=#00ff00ff>[Controller]</color>{obj.kind}");

        MoveUnitP = setpos.Where(p =>
        {
            float dx = p.x - objp.x;
            float dz = p.z - objp.z;
<<<<<<< HEAD
            bool occupied = UnitPointData.Contains(Cell(p));
            return predicate(dx, dz) && !occupied;
=======
            // Phase 2: Direction.Sã®ã¨ãå‰å¾Œã‚’åè»¢ï¼ˆå—å‘ããƒ¦ãƒ‹ãƒƒãƒˆã®ç§»å‹•ç¯„å›²ã‚’æ­£ã—ãè¨ˆç®—ã™ã‚‹ãŸã‚ï¼‰
            float dirDx = obj.direction == Direction.S ? -dx : dx;
            float dirDz = obj.direction == Direction.S ? -dz : dz;
            bool occupied = UnitPointData.Contains(Cell(p));
            return predicate(dirDx, dirDz) && !occupied;
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        }).ToList();

        MoveCreate();
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ˆÚ“®ƒ|ƒCƒ“ƒgƒIƒuƒWƒFƒNƒg‚Ì¶¬ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ ç§»å‹•ãƒã‚¤ãƒ³ãƒˆã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆã®ç”Ÿæˆ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void MoveCreate()
    {
        for (int i = 0; i < MoveUnitP.Count; i++)
        {
            Vector3 pos = MoveUnitP[i];
            pos.y -= 0.47f;
            Instantiate(MovePoint, pos, Quaternion.identity, Move);
            Debug.Log("<color=#00ff00ff>[Controller]</color>MovePoint");
        }
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ˆÚ“®ƒ|ƒCƒ“ƒgƒIƒuƒWƒFƒNƒg‚Ìíœ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ ç§»å‹•ãƒã‚¤ãƒ³ãƒˆã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆã®å‰Šé™¤ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void MoveReset()
    {
        foreach (Transform child in Move.transform)
        {
            Destroy(child.gameObject);
        }
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ UnitPointData ‚ÌXV „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ UnitPointData ã®æ›´æ–° â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void MoveUpdate(Vector3 OldCell, Vector3 NewCell)
    {
        UnitPointData.Add(Cell(NewCell));
        UnitPointData.Remove(Cell(OldCell));
    }
}
