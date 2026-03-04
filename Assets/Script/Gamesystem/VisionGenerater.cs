using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisionGenerater : MonoBehaviour
{
    public List<Vector3> _setpos;

<<<<<<< HEAD
    //Player‚ª¡Œ©‚¦‚éƒ}ƒX
    public HashSet<Vector3Int> PlayerVisionBox;
    //Player‚ªˆê“xŒ©‚½ƒ}ƒX
=======
    // PlayerãŒä»Šè¦‹ãˆã‚‹ãƒã‚¹
    public HashSet<Vector3Int> PlayerVisionBox;
    // PlayerãŒä¸€åº¦è¦‹ãŸãƒã‚¹
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public HashSet<Vector3Int> PlayerExploard;

    public List<Status> playerunitbox;

<<<<<<< HEAD
    //Enemy‚ª¡Œ©‚¦‚éƒ}ƒX
    public HashSet<Vector3Int> EnemyVisionBox;
    //Enemy‚ªˆê“xŒ©‚½ƒ}ƒX
=======
    // EnemyãŒä»Šè¦‹ãˆã‚‹ãƒã‚¹
    public HashSet<Vector3Int> EnemyVisionBox;
    // EnemyãŒä¸€åº¦è¦‹ãŸãƒã‚¹
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public HashSet<Vector3Int> EnemyExploard;

    public List<Status> enemyunitbox;

<<<<<<< HEAD
    [Header("ƒ}ƒbƒvƒNƒŠƒGƒCƒg")]
    [SerializeField] MapCreate mapcreate;

    [Header("ƒ€[ƒuƒWƒFƒlƒŒ[ƒ^[")]
    [SerializeField] MoveGererater movegenerater;

    [Header("ƒvƒŒƒCƒ„[ƒ€[ƒu")]
    [SerializeField] PlayerMove playermove;

    [Header("ƒNƒŠƒXƒ^ƒ‹ƒVƒXƒeƒ€")]
    [SerializeField] CrystalSystem crystalsystem;

    [Header("ƒeƒŠƒgƒŠ[ƒVƒXƒeƒ€")]
    [SerializeField] TerritorySystem territorysystem;

    [Header("ƒ†ƒjƒbƒgƒ{ƒbƒNƒX")]
=======
    [Header("ãƒãƒƒãƒ—ã‚¯ãƒªã‚¨ã‚¤ãƒˆ")]
    [SerializeField] MapCreate mapcreate;

    [Header("ãƒ ãƒ¼ãƒ–ã‚¸ã‚§ãƒãƒ¬ãƒ¼ã‚¿ãƒ¼")]
    [SerializeField] MoveGererater movegenerater;

    [Header("ãƒ—ãƒ¬ã‚¤ãƒ¤ãƒ¼ãƒ ãƒ¼ãƒ–")]
    [SerializeField] PlayerMove playermove;

    [Header("ã‚¯ãƒªã‚¹ã‚¿ãƒ«ã‚·ã‚¹ãƒ†ãƒ ")]
    [SerializeField] CrystalSystem crystalsystem;

    [Header("ãƒ†ãƒªãƒˆãƒªãƒ¼ã‚·ã‚¹ãƒ†ãƒ ")]
    [SerializeField] TerritorySystem territorysystem;

    [Header("ãƒ¦ãƒ‹ãƒƒãƒˆãƒœãƒƒã‚¯ã‚¹")]
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    [SerializeField] Transform PlayerUnit;
    [SerializeField] Transform EnemyUnit;

    int blockLayerMask;

<<<<<<< HEAD
    //‹î‚Ìí—Ş‚²‚Æ‚Ì‹ŠEƒf[ƒ^iDictionary‚ÅˆêŒ³ŠÇ—j
=======
    // é§’ã®ç¨®é¡ã”ã¨ã®è¦–ç•Œãƒ‡ãƒ¼ã‚¿ï¼ˆDictionaryã§ä¸€å…ƒç®¡ç†ï¼‰
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    static readonly Dictionary<Kind, Vector3Int[]> VisionDataMap = new Dictionary<Kind, Vector3Int[]>
    {
        { Kind.Crystal,     RangeVisionBox(-3, 3, -1, 0, -3, 3, true) },
        { Kind.King,        VisionBox(-1, 1, -1, 0, 0, 2, true) },
        { Kind.Knight,      VisionBox(-1, 1, -1, 0, 0, 2, true) },
        { Kind.Archer,      VisionBox(-1, 1, -1, 0, 0, 3, true) },
        { Kind.Magic,       RangeVisionBox(-2, 2, -1, 0, -2, 2, true) },
        { Kind.Assassin,    VisionBox(-1, 1, -1, 0, -1, 2, true) },
        { Kind.Scout,       RangeVisionBox(-2, 2, -1, 0, -2, 2, true) },
        { Kind.Priest,      RangeVisionBox(-1, 1, -1, 0, -1, 1, true) },
        { Kind.Guardian,    RangeVisionBox(-1, 1, -1, 0, 0, 2, true) },
        { Kind.Crossbow,    RangeVisionBox(-1, 1, -1, 0, 0, 2, true) },
        { Kind.Magicsniper, RangeVisionBox(-4, 4, -1, 0, -1, 1, true) },
        { Kind.Bomber,      VisionBox(-1, 1, -1, 0, 0, 3, true) },
    };

<<<<<<< HEAD
    static Vector3Int[] VisionBox
        (
            int minx, int maxx,
            int miny, int maxy,
            int minz, int maxz,
            bool PiecePosition
        )
    {
        var list = new List<Vector3Int>();
        for (int visionpointx = minx; visionpointx <= maxx; visionpointx++)
            for (int visionpointy = miny; visionpointy <= maxy; visionpointy++)
                for (int visionpointz = minz; visionpointz <= maxz; visionpointz++)
                {
                    if (!PiecePosition && visionpointx == 0 && visionpointy == 0 && visionpointz == 0) continue;
                    if (visionpointz == 0 && (visionpointx == -1 || visionpointx == 1)) continue;
                    list.Add(new Vector3Int(visionpointx, visionpointy, visionpointz));
=======
    static Vector3Int[] VisionBox(int minx, int maxx, int miny, int maxy,
                                   int minz, int maxz, bool PiecePosition)
    {
        var list = new List<Vector3Int>();
        for (int vx = minx; vx <= maxx; vx++)
            for (int vy = miny; vy <= maxy; vy++)
                for (int vz = minz; vz <= maxz; vz++)
                {
                    if (!PiecePosition && vx == 0 && vy == 0 && vz == 0) continue;
                    if (vz == 0 && (vx == -1 || vx == 1)) continue;
                    list.Add(new Vector3Int(vx, vy, vz));
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
                }
        return list.ToArray();
    }

<<<<<<< HEAD
    static Vector3Int[] RangeVisionBox
        (
            int minx, int maxx,
            int miny, int maxy,
            int minz, int maxz,
            bool PiecePosition
        )
    {
        var list = new List<Vector3Int>();
        for (int visionpointx = minx; visionpointx <= maxx; visionpointx++)
            for (int visionpointy = miny; visionpointy <= maxy; visionpointy++)
                for (int visionpointz = minz; visionpointz <= maxz; visionpointz++)
                {
                    if (!PiecePosition && visionpointx == 0 && visionpointy == 0 && visionpointz == 0) continue;
                    list.Add(new Vector3Int(visionpointx, visionpointy, visionpointz));
=======
    static Vector3Int[] RangeVisionBox(int minx, int maxx, int miny, int maxy,
                                        int minz, int maxz, bool PiecePosition)
    {
        var list = new List<Vector3Int>();
        for (int vx = minx; vx <= maxx; vx++)
            for (int vy = miny; vy <= maxy; vy++)
                for (int vz = minz; vz <= maxz; vz++)
                {
                    if (!PiecePosition && vx == 0 && vy == 0 && vz == 0) continue;
                    list.Add(new Vector3Int(vx, vy, vz));
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
                }
        return list.ToArray();
    }

    public void Awake()
    {
        blockLayerMask = LayerMask.GetMask("Block");

        if (PlayerVisionBox == null) PlayerVisionBox = new HashSet<Vector3Int>();
        if (PlayerExploard == null) PlayerExploard = new HashSet<Vector3Int>();
        if (EnemyVisionBox == null) EnemyVisionBox = new HashSet<Vector3Int>();
        if (EnemyExploard == null) EnemyExploard = new HashSet<Vector3Int>();
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ƒwƒ‹ƒp[ƒƒ\ƒbƒh „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ

    /// <summary>
    /// Transform‚Ìq‚©‚çStatusƒRƒ“ƒ|[ƒlƒ“ƒg‚ğûW‚µ‚ÄƒŠƒXƒg‚É“ü‚ê‚é
    /// </summary>
=======
    // â”€â”€â”€ ãƒ˜ãƒ«ãƒ‘ãƒ¼ãƒ¡ã‚½ãƒƒãƒ‰ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    void CollectStatuses(Transform parent, List<Status> result)
    {
        result.Clear();
        foreach (Transform child in parent)
        {
            if (child == null) continue;
            Status status = child.GetComponentInChildren<Status>();
            if (status != null)
<<<<<<< HEAD
            {
                result.Add(status);
            }
        }
    }

    /// <summary>
    /// Status‚ÌVisionCell‚ğ‰Šú‰»EŒvZ‚µAŒ‹‰Ê‚ğtargetSet‚Éƒ}[ƒW‚·‚é
    /// </summary>
    void CalculateAndMergeVision(Status status, MapCreate mapcreate, CrystalSystem crystalsystem, HashSet<Vector3Int> targetSet)
=======
                result.Add(status);
        }
    }

    void CalculateAndMergeVision(Status status, MapCreate mapcreate,
                                  CrystalSystem crystalsystem, HashSet<Vector3Int> targetSet)
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    {
        if (status == null) return;

        if (status.VisionCell == null)
<<<<<<< HEAD
        {
            status.VisionCell = new HashSet<Vector3Int>();
        }
=======
            status.VisionCell = new HashSet<Vector3Int>();
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13

        status.VisionCell.Clear();
        VisionCreate(status, mapcreate, crystalsystem);
        targetSet.UnionWith(status.VisionCell);
    }

<<<<<<< HEAD
    /// <summary>
    /// ‹î‚ÌŒü‚«‚É‰‚¶‚ÄƒIƒtƒZƒbƒg‚ğ•ÏŠ·‚·‚é
    /// </summary>
=======
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    static Vector3Int ApplyDirection(Vector3Int offset, Direction direction)
    {
        switch (direction)
        {
            case Direction.S:
                return new Vector3Int(-offset.x, offset.y, -offset.z);
            case Direction.N:
            default:
                return offset;
        }
    }

<<<<<<< HEAD
    /// <summary>
    /// Crystal—pFã•û‚©‚ç‚ÌRaycast‚Å’n•\‚Ì‚‚³‚ğæ“¾‚µ‹ŠEƒZƒ‹‚É’Ç‰Á‚·‚é
    /// </summary>
=======
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    void RaycastCrystalVision(Status status, MapCreate mapcreate, int px, int py, int pz)
    {
        Vector3 goal = new Vector3(px, py, pz) + Vector3.up * 0.5f;
        float startHigh = mapcreate.maxY + 10f;
        Vector3 visionstart = goal + Vector3.up * startHigh;
        float distance = startHigh + 20f;

        if (Physics.Raycast(visionstart, Vector3.down, out var hit, distance, blockLayerMask))
        {
            int hitx = Mathf.RoundToInt(hit.collider.transform.position.x);
            int hitz = Mathf.RoundToInt(hit.collider.transform.position.z);

            if (hitx == px && hitz == pz)
            {
                int hity = Mathf.RoundToInt(hit.collider.transform.position.y);
                status.VisionCell.Add(new Vector3Int(px, hity, pz));
            }
            else
            {
                status.VisionCell.Add(new Vector3Int(px, 0, pz));
            }
        }
        else
        {
            status.VisionCell.Add(new Vector3Int(px, 0, pz));
        }
    }

<<<<<<< HEAD
    /// <summary>
    /// CrystalˆÈŠOF‹îˆÊ’u‚©‚ç–Ú•W‚Ö’¼üRaycast‚µáŠQ•¨‚ª‚È‚¯‚ê‚Î‹ŠEƒZƒ‹‚É’Ç‰Á‚·‚é
    /// </summary>
    void RaycastDirectVision(Status status, int statusX, int statusY, int statusZ, int px, int py, int pz)
=======
    void RaycastDirectVision(Status status, int statusX, int statusY, int statusZ,
                              int px, int py, int pz)
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    {
        Vector3 start = new Vector3(statusX, statusY, statusZ) + Vector3.up * 0.5f;
        Vector3 goal = new Vector3(px, py, pz) + Vector3.up * 0.5f;
        Vector3 direction = goal - start;
        float distance = direction.magnitude;

        if (distance <= 0.001f)
        {
            status.VisionCell.Add(new Vector3Int(px, py, pz));
            return;
        }

        if (Physics.Raycast(start, direction.normalized, out var hit, distance, blockLayerMask))
        {
            int hitx = Mathf.RoundToInt(hit.collider.transform.position.x);
            int hity = Mathf.RoundToInt(hit.collider.transform.position.y);
            int hitz = Mathf.RoundToInt(hit.collider.transform.position.z);

            if (px == hitx && py == hity && pz == hitz)
<<<<<<< HEAD
            {
                status.VisionCell.Add(new Vector3Int(px, py, pz));
            }
=======
                status.VisionCell.Add(new Vector3Int(px, py, pz));
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        }
        else
        {
            status.VisionCell.Add(new Vector3Int(px, py, pz));
        }
    }

<<<<<<< HEAD
    /// <summary>
    /// FogƒIƒuƒWƒFƒNƒg‚Ì•\¦/”ñ•\¦‚ğ§Œä‚·‚é
    /// showOnExploard=false: –¢‹ŠE‚©‚Â–¢’Tõ‚Ì‚Æ‚«•\¦iŠ®‘S‚È–¶j
    /// showOnExploard=true:  –¢‹ŠE‚©‚Â’TõÏ‚İ‚Ì‚Æ‚«•\¦i’TõÏ‚İ–¶j
    /// </summary>
    void SetFogVisibility(Transform parent, HashSet<Vector3Int> visionXZ, HashSet<Vector3Int> exploardXZ, bool showOnExploard)
=======
    void SetFogVisibility(Transform parent, HashSet<Vector3Int> visionXZ,
                           HashSet<Vector3Int> exploardXZ, bool showOnExploard)
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    {
        if (parent == null) return;

        foreach (Transform Temporary in parent)
        {
            int x = Mathf.RoundToInt(Temporary.position.x);
            int z = Mathf.RoundToInt(Temporary.position.z);
            var data = new Vector3Int(x, 0, z);

            bool nowVision = visionXZ.Contains(data);
            bool nowExploard = exploardXZ.Contains(data);

            if (showOnExploard)
<<<<<<< HEAD
            {
                Temporary.gameObject.SetActive(!nowVision && nowExploard);
            }
            else
            {
                Temporary.gameObject.SetActive(!nowVision && !nowExploard);
            }
        }
    }

    /// <summary>
    /// ‹ŠE“à‚©‚Ç‚¤‚©‚ÅRenderer‚Ì—LŒø/–³Œø‚ğØ‚è‘Ö‚¦‚é
    /// </summary>
=======
                Temporary.gameObject.SetActive(!nowVision && nowExploard);
            else
                Temporary.gameObject.SetActive(!nowVision && !nowExploard);
        }
    }

>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    void SetRendererVisibility(IEnumerable targets, HashSet<Vector3Int> visionXZ)
    {
        if (targets == null) return;

        foreach (Transform Temporary in targets)
        {
            int x = Mathf.RoundToInt(Temporary.position.x);
            int z = Mathf.RoundToInt(Temporary.position.z);
            var data = new Vector3Int(x, 0, z);

            bool visible = visionXZ.Contains(data);
            foreach (var renderer in Temporary.GetComponentsInChildren<Renderer>(true))
<<<<<<< HEAD
            {
                renderer.enabled = visible;
            }
        }
    }

    // „Ÿ„Ÿ„Ÿ ƒƒCƒ“ƒƒ\ƒbƒh „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ

    public void VisionPoint(MapCreate mapcreate, MoveGererater movegenerater, CrystalSystem crystalsystem)
=======
                renderer.enabled = visible;
        }
    }

    // â”€â”€â”€ ãƒ¡ã‚¤ãƒ³ãƒ¡ã‚½ãƒƒãƒ‰ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void VisionPoint(MapCreate mapcreate, MoveGererater movegenerater,
                             CrystalSystem crystalsystem)
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    {
        this.mapcreate = mapcreate;
        this.movegenerater = movegenerater;
        this.crystalsystem = crystalsystem;

        if (PlayerVisionBox != null) PlayerVisionBox.Clear();
        if (EnemyVisionBox != null) EnemyVisionBox.Clear();

        _setpos = mapcreate.SetPos;

<<<<<<< HEAD
        //PlayerAEnemy‚Ì‹î‚ÌStatus‚ğûW‚·‚é
=======
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        if (playerunitbox == null) playerunitbox = new List<Status>();
        CollectStatuses(PlayerUnit, playerunitbox);

        if (enemyunitbox == null) enemyunitbox = new List<Status>();
        CollectStatuses(EnemyUnit, enemyunitbox);

<<<<<<< HEAD
        //Player‹î‚Ì‹ŠEŒvZ
        foreach (Status status in playerunitbox)
        {
            CalculateAndMergeVision(status, mapcreate, crystalsystem, PlayerVisionBox);
        }

        //PlayerƒNƒŠƒXƒ^ƒ‹‚Ì‹ŠEŒvZ
=======
        // Playeré§’ã®è¦–ç•Œè¨ˆç®—
        foreach (Status status in playerunitbox)
            CalculateAndMergeVision(status, mapcreate, crystalsystem, PlayerVisionBox);

        // Playerã‚¯ãƒªã‚¹ã‚¿ãƒ«ã®è¦–ç•Œè¨ˆç®—
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        foreach (Transform Temporary in crystalsystem.Playercrystal)
        {
            Status status = Temporary.GetComponentInChildren<Status>();
            CalculateAndMergeVision(status, mapcreate, crystalsystem, PlayerVisionBox);
        }

<<<<<<< HEAD
        /*foreach (Status status in enemyunitbox)
        {
            CalculateAndMergeVision(status, mapcreate, crystalsystem, EnemyVisionBox);
        }

        foreach (Transform Temporary in crystalsystem.Enemycrystal)
        {
            Status status = Temporary.GetComponentInChildren<Status>();
            CalculateAndMergeVision(status, mapcreate, crystalsystem, PlayerVisionBox);
        }*/

        //VisionBox‚ğExploard‚É“ü‚ê‚é
=======
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        PlayerExploard.UnionWith(PlayerVisionBox);
        EnemyExploard.UnionWith(EnemyVisionBox);
        VisionSetting(mapcreate);
    }

    public void VisionCreate(Status status, MapCreate mapcreate, CrystalSystem crystalsystem)
    {
        if (!VisionDataMap.TryGetValue(status.kind, out Vector3Int[] visionData))
<<<<<<< HEAD
        {
            return;
        }
=======
            return;
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13

        Debug.Log($"<color=#00ff00ff>[Controller]</color>{status.kind}");

        int statusX = Mathf.RoundToInt(status.transform.position.x);
        int statusY = Mathf.RoundToInt(status.transform.position.y);
        int statusZ = Mathf.RoundToInt(status.transform.position.z);

        foreach (Vector3Int p in visionData)
        {
            Vector3Int directionP = ApplyDirection(p, status.direction);

            int px = statusX + directionP.x;
            int py = statusY + directionP.y;
            int pz = statusZ + directionP.z;

            if (px < 0 || px >= mapcreate.maxX) continue;
            if (pz < 0 || pz >= mapcreate.maxZ) continue;
            if (py < mapcreate.minY || py > mapcreate.maxY) continue;

            if (status.kind == Kind.Crystal)
<<<<<<< HEAD
            {
                RaycastCrystalVision(status, mapcreate, px, py, pz);
            }
            else
            {
                RaycastDirectVision(status, statusX, statusY, statusZ, px, py, pz);
            }
=======
                RaycastCrystalVision(status, mapcreate, px, py, pz);
            else
                RaycastDirectVision(status, statusX, statusY, statusZ, px, py, pz);
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        }
    }

    public void VisionSetting(MapCreate mapcreate)
    {
<<<<<<< HEAD
        //PlayerVisionBox‚ÆPlayerExploard‚ÌXZ‚¾‚¯‚ğ‚Æ‚é‚½‚ß‚Ì” ‚ğì‚é
=======
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        var playervisionXZ = new HashSet<Vector3Int>();
        var playerexploardXZ = new HashSet<Vector3Int>();

        foreach (var Temporary in PlayerVisionBox)
<<<<<<< HEAD
        {
            playervisionXZ.Add(new Vector3Int(Temporary.x, 0, Temporary.z));
        }
        foreach (var Temporary in PlayerExploard)
        {
            playerexploardXZ.Add(new Vector3Int(Temporary.x, 0, Temporary.z));
        }

        //Fog•\¦§Œäi–¢‹ŠE•–¢’Tõ ¨ Š®‘S‚È–¶‚ğ•\¦j
        SetFogVisibility(mapcreate.FogParent, playervisionXZ, playerexploardXZ, false);
        SetFogVisibility(mapcreate.FogBoardParent, playervisionXZ, playerexploardXZ, false);

        //Fog•\¦§Œäi–¢‹ŠE•’TõÏ‚İ ¨ ’TõÏ‚İ–¶‚ğ•\¦j
        SetFogVisibility(mapcreate.FogExploardBoardParent, playervisionXZ, playerexploardXZ, true);
        SetFogVisibility(mapcreate.FogExploardParent, playervisionXZ, playerexploardXZ, true);

        //‹î‚Ì‹ŠEŠO‚Ì“G‚ğŒ©‚¦‚È‚¢‚æ‚¤‚É‚·‚é
=======
            playervisionXZ.Add(new Vector3Int(Temporary.x, 0, Temporary.z));
        foreach (var Temporary in PlayerExploard)
            playerexploardXZ.Add(new Vector3Int(Temporary.x, 0, Temporary.z));

        SetFogVisibility(mapcreate.FogParent, playervisionXZ, playerexploardXZ, false);
        SetFogVisibility(mapcreate.FogBoardParent, playervisionXZ, playerexploardXZ, false);
        SetFogVisibility(mapcreate.FogExploardBoardParent, playervisionXZ, playerexploardXZ, true);
        SetFogVisibility(mapcreate.FogExploardParent, playervisionXZ, playerexploardXZ, true);

>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        SetRendererVisibility(EnemyUnit, playervisionXZ);
        SetRendererVisibility(crystalsystem.Enemycrystal, playervisionXZ);
        SetRendererVisibility(territorysystem.Enemyterritory, playervisionXZ);
    }
}
