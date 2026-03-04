using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AttackPointt : MonoBehaviour
{
    public PlayerMove.AttackMode attackmode;
    public List<Vector3> AttackP;
    public List<Vector3> setpos;

<<<<<<< HEAD
    [Header("ƒ†ƒjƒbƒgÀ•W")]
=======
    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆåº§æ¨E)]
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    [SerializeField] public HashSet<Vector3> unitdata;

    public Status obj;
    public Vector3 objp;
    public Vector3 attackpos;
    public RaycastHit targethit;

    [Header("ãƒãƒƒãƒ—ã‚¯ãƒªã‚¨ã‚¤ãƒE)]
    [SerializeField] public MapCreate mapcreate;

<<<<<<< HEAD
    [Header("ƒvƒŒƒCƒ„[ƒ€[ƒu")]
    [SerializeField] public PlayerMove move;

    [Header("ƒ€[ƒuƒWƒFƒlƒŒ[ƒ^[")]
    [SerializeField] public MoveGererater movegenerater;

    [Header("ƒAƒ^ƒbƒNƒ|ƒCƒ“ƒg")]
    [SerializeField] public GameObject AttackPoint;

    [Header("ƒAƒ^ƒbƒNƒ|ƒCƒ“ƒge")]
    [SerializeField] public Transform APparent;

    // „Ÿ„Ÿ„Ÿ ‹îí‚²‚Æ‚ÌUŒ‚”ÍˆÍ”»’è „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    // dx = p.x - objp.xi•„†•t‚«j, dz = p.z - objp.zi•„†•t‚«j
    // Priest ‚Í–¢À‘•‚Ì‚½‚ßƒGƒ“ƒgƒŠ‚È‚µi¡Œã’Ç‰Á—\’èj
    // V‚µ‚¢‹î‚ğ’Ç‰Á‚·‚éê‡‚Í‚±‚±‚É1s’Ç‰Á‚·‚é‚¾‚¯‚Å‚æ‚¢
    static readonly Dictionary<Kind, Func<float, float, bool>> AttackPredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // ‘O•û3ƒ}ƒXi‰¡}1E’¼ij
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // ‘O•û3ƒ}ƒXiKing‚Æ“¯‚¶UŒ‚”ÍˆÍj
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // ‘O•û’¼i2E3ƒ}ƒX
        { Kind.Archer,      (dx, dz) => dx == 0 && (dz == 2 || dz == 3) },

        // \š‰“‹——£2ƒ}ƒX
        { Kind.Magic,       (dx, dz) => (Mathf.Abs(dx) == 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // ‘OÎ‚ß}1ƒ}ƒX
        { Kind.Assassin,    (dx, dz) => Mathf.Abs(dx) == 1 && dz == 1 },

        // ¶‰E‰¡1ƒ}ƒX
        { Kind.Scout,       (dx, dz) => Mathf.Abs(dx) == 1 && dz == 0 },

        // ‘O’¼i1ƒ}ƒX
        { Kind.Guardian,    (dx, dz) => dx == 0 && dz == 1 },

        // ‘O’¼i1E2ƒ}ƒX
        { Kind.Crossbow,    (dx, dz) => dx == 0 && (dz == 1 || dz == 2) },

        // ¶‰E‰¡4ƒ}ƒX
        { Kind.Magicsniper, (dx, dz) => Mathf.Abs(dx) == 4 && dz == 0 },

        // ‘O’¼i3ƒ}ƒX
        { Kind.Bomber,      (dx, dz) => dx == 0 && dz == 3 },
    };

    // „Ÿ„Ÿ„Ÿ UŒ‚ƒ‚[ƒh‚É‰‚¶‚½ƒ|ƒCƒ“ƒg¶¬ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    [Header("ãƒ—ãƒ¬ã‚¤ãƒ¤ãƒ¼ãƒ ãƒ¼ãƒE)]
    [SerializeField] public PlayerMove move;

    [Header("ãƒ ãƒ¼ãƒ–ã‚¸ã‚§ãƒãƒ¬ãƒ¼ã‚¿ãƒ¼")]
    [SerializeField] public MoveGererater movegenerater;

    [Header("ã‚¢ã‚¿ãƒE‚¯ãƒã‚¤ãƒ³ãƒE)]
    [SerializeField] public GameObject AttackPoint;

    [Header("ã‚¢ã‚¿ãƒE‚¯ãƒã‚¤ãƒ³ãƒˆè¦ª")]
    [SerializeField] public Transform APparent;

    // â”€â”€â”€ é§’ç¨®ã”ã¨ã®æ”»æ’E¯E›²åˆ¤å®Eâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // dx = p.x - objp.xEˆç¬¦å·ä»˜ãEE dz = p.z - objp.zEˆç¬¦å·ä»˜ãEE
    // æ–°ã—ã„é§’ã‚’è¿½åŠ ã™ã‚‹å ´åˆãEã“ã“ã«1è¡Œè¿½åŠ ã™ã‚‹ã ã‘ã§ã‚ˆã„
    public static readonly Dictionary<Kind, Func<float, float, bool>> AttackPredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // å‰æ–¹3ãƒã‚¹Eˆæ¨ªÂ±1ãƒ»ç›´é€²EE
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // å‰æ–¹3ãƒã‚¹EEingã¨åŒã˜æ”»æ’E¯E›²EE
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // å‰æ–¹ç›´é€²2ãƒ»3ãƒã‚¹
        { Kind.Archer,      (dx, dz) => dx == 0 && (dz == 2 || dz == 3) },

        // åå­—é è·é›¢2ãƒã‚¹
        { Kind.Magic,       (dx, dz) => (Mathf.Abs(dx) == 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // å‰æ–œã‚E±1ãƒã‚¹
        { Kind.Assassin,    (dx, dz) => Mathf.Abs(dx) == 1 && dz == 1 },

        // å·¦å³æ¨ª1ãƒã‚¹
        { Kind.Scout,       (dx, dz) => Mathf.Abs(dx) == 1 && dz == 0 },

        // Phase 2: éš£æ¥4ãƒã‚¹Eˆå‰å¾Œå·¦å³E‰ãEå‘³æ–¹ã‚’å›å¾©å¯¾è±¡ã¨ã™ã‚‹
        { Kind.Priest,      (dx, dz) => (Mathf.Abs(dx) == 1 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 1) },

        // å‰ç›´é€²1ãƒã‚¹
        { Kind.Guardian,    (dx, dz) => dx == 0 && dz == 1 },

        // å‰ç›´é€²1ãƒ»2ãƒã‚¹
        { Kind.Crossbow,    (dx, dz) => dx == 0 && (dz == 1 || dz == 2) },

        // å·¦å³æ¨ª4ãƒã‚¹
        { Kind.Magicsniper, (dx, dz) => Mathf.Abs(dx) == 4 && dz == 0 },

        // å‰ç›´é€²3ãƒã‚¹
        { Kind.Bomber,      (dx, dz) => dx == 0 && dz == 3 },
    };

    // â”€â”€â”€ æ”»æ’Eƒ¢ãƒ¼ãƒ‰ã«å¿œã˜ãŸãEã‚¤ãƒ³ãƒˆç”ŸæˆEâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void AttackPointCall(Status Obj, Vector3 ObjP, PlayerMove move)
    {
        this.move = move;
        setpos = mapcreate.SetPos;
        attackmode = move.attackmode;

        switch (attackmode)
        {
            case PlayerMove.AttackMode.Normal:
                NormalAttackPData(Obj, ObjP);
                PointCreate();
                break;
            case PlayerMove.AttackMode.Skill:
<<<<<<< HEAD
                // ¡ŒãÀ‘•—\’è
=======
                // ä»Šå¾Œå®Ÿè£Eºˆå®E
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
                break;
        }
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ UŒ‚ƒ|ƒCƒ“ƒgƒIƒuƒWƒFƒNƒg‚Ì¶¬ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ æ”»æ’EEã‚¤ãƒ³ãƒˆã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆãEç”ŸæE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void PointCreate()
    {
        for (int i = 0; i < AttackP.Count; i++)
        {
            Vector3 pos = AttackP[i];
            pos.y -= 0.17f;
            Instantiate(AttackPoint, pos, Quaternion.identity, APparent);
        }
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ UŒ‚ƒ|ƒCƒ“ƒgƒIƒuƒWƒFƒNƒg‚Ìíœ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ æ”»æ’EEã‚¤ãƒ³ãƒˆã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆãEå‰Šé™¤ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void AtkpDestroy()
    {
        foreach (Transform child in APparent)
        {
            Destroy(child.gameObject);
        }
        AttackP?.Clear();
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ’ÊíUŒ‚‚ÌUŒ‚”ÍˆÍŒvZ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ é€šå¸¸æ”»æ’EEæ”»æ’E¯E›²è¨ˆç®Eâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void NormalAttackPData(Status Obj, Vector3 ObjP)
    {
        AttackP?.Clear();
        obj = Obj;
        objp = ObjP;
        movegenerater.UnitPointCore();
        unitdata = movegenerater.UnitPointData;

        if (!AttackPredicateMap.TryGetValue(obj.kind, out Func<float, float, bool> predicate))
        {
<<<<<<< HEAD
            Debug.Log($"[AttackPointt] Kind '{obj.kind}' ‚ÌUŒ‚ƒpƒ^[ƒ“‚Í–¢À‘•‚Å‚·");
            return;
=======
            Debug.Log($"[AttackPointt] Kind '{obj.kind}' ã®æ”»æ’Eƒ‘ã‚¿ãƒ¼ãƒ³ã¯æœªå®Ÿè£E§ãE);
            return;
        }

        Vector3 ownCell = movegenerater.Cell(objp);
        Vector3 pcpCell = movegenerater.Cell(movegenerater.pcp);

        // Phase 2: Priestã ã‘å‘³æ–¹ãƒ¦ãƒ‹ãƒƒãƒˆã‚’å¯¾è±¡ã«ã™ã‚‹Eˆãã‚Œä»¥å¤–ãEæ•µãƒ»å»ºç‰©ãƒ»å£ã‚’å¯¾è±¡EE
        bool isPriest = obj.kind == Kind.Priest;

        if (isPriest)
        {
            // Priestã®ã¨ãï¼šå‘³æ–¹ãƒãEãƒ ã®ã‚»ãƒ«ã‚’å¯¾è±¡EElayerUnitPointData ã‚’å‚ç…§EE
            HashSet<Vector3> allyData = movegenerater.PlayerUnitPointData;
            AttackP = setpos.Where(p =>
            {
                float dx = Mathf.RoundToInt(p.x - objp.x);
                float dz = Mathf.RoundToInt(p.z - objp.z);
                // Phase 2: å‘ãå¯¾å¿E
                float dirDx = obj.direction == Direction.S ? -dx : dx;
                float dirDz = obj.direction == Direction.S ? -dz : dz;
                Vector3 cell = movegenerater.Cell(p);
                bool notSelf = cell != ownCell;
                bool hasFriendly = allyData.Contains(cell);
                return notSelf && hasFriendly && predicate(dirDx, dirDz);
            }).ToList();
        }
        else
        {
            // ãã‚Œä»¥å¤–ï¼šæ•µãƒãEãƒ ã®ã‚»ãƒ« OR å£ãEã‚»ãƒ«EEnitdataã«å«ã¾ã‚Œã‚‹è‡ªåˆE»¥å¤–ï¼E
            AttackP = setpos.Where(p =>
            {
                float dx = Mathf.RoundToInt(p.x - objp.x);
                float dz = Mathf.RoundToInt(p.z - objp.z);
                // Phase 2: å‘ãå¯¾å¿E
                float dirDx = obj.direction == Direction.S ? -dx : dx;
                float dirDz = obj.direction == Direction.S ? -dz : dz;
                Vector3 cell = movegenerater.Cell(p);
                bool occupied = unitdata.Contains(cell);
                bool notSelf = cell != ownCell && cell != pcpCell;
                return occupied && notSelf && predicate(dirDx, dirDz);
            }).ToList();
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        }

        Vector3 ownCell = movegenerater.Cell(objp);
        Vector3 pcpCell = movegenerater.Cell(movegenerater.pcp);

        AttackP = setpos.Where(p =>
        {
            float dx = Mathf.RoundToInt(p.x - objp.x);
            float dz = Mathf.RoundToInt(p.z - objp.z);
            Vector3 cell = movegenerater.Cell(p);
            bool occupied = unitdata.Contains(cell);
            bool notSelf = cell != ownCell && cell != pcpCell;
            return occupied && notSelf && predicate(dx, dz);
        }).ToList();
    }
}
