using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CrystalSystem : MonoBehaviour
{
<<<<<<< HEAD
    [Header("ƒNƒŠƒXƒ^ƒ‹")]
    [SerializeField] private GameObject PlayerCrystal;
    [SerializeField] private GameObject EnemyCrystal;

    [Header("ƒNƒŠƒXƒ^ƒ‹ŠÔ‹——£")]
    public int CrystalDistanceXmin = 1;
    public int CrystalDistanceXmax = 10;
    public int CrystalDistanceZmin = 1;
    public int CrystalDistanceZmax = 10;

    [Header("ƒNƒŠƒXƒ^ƒ‹eƒIƒuƒWƒFƒNƒg")]
    [SerializeField] public Transform Playercrystal;
    [SerializeField] public Transform Enemycrystal;

=======
    [Header("ã‚¯ãƒªã‚¹ã‚¿ãƒ«")]
    [SerializeField] private GameObject PlayerCrystal;
    [SerializeField] private GameObject EnemyCrystal;

    [Header("ã‚¯ãƒªã‚¹ã‚¿ãƒ«é–“è·é›¢")]
    public int CrystalDistanceXmin = 1;
    public int CrystalDistanceXmax = 10;
    public int CrystalDistanceZmin = 1;
    public int CrystalDistanceZmax = 10;

    [Header("ã‚¯ãƒªã‚¹ã‚¿ãƒ«è¦ªã‚ªãƒ–ã‚¸ã‚§ã‚¯ãƒˆ")]
    [SerializeField] public Transform Playercrystal;
    [SerializeField] public Transform Enemycrystal;

>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public Vector3 PCP;
    public Vector3 ECP;

    private List<Vector3> _SetPos;
    private int maxx;
    private int maxz;

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ƒtƒH[ƒ‹ƒoƒbƒNİ’è „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    // ”z’u‚É¸”s‚µ‚½ê‡A‹——£§–ñ‚ğ’iŠK“I‚ÉŠÉ˜a‚µ‚ÄÄs‚·‚é
    // ’iŠK‚ğ‘‚â‚µ‚½‚¢ê‡‚Í‚±‚±‚É’l‚ğ’Ç‰Á‚·‚é‚¾‚¯‚Å‚æ‚¢
    // ’l‚Í CrystalDistanceMax ‚©‚çˆø‚­ƒIƒtƒZƒbƒg
    private static readonly int[] DistanceRelaxation = { 0, 2, 4, 6 };

    // „Ÿ„Ÿ„Ÿ ƒƒCƒ“ƒGƒ“ƒgƒŠ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // é…ç½®ã«å¤±æ•—ã—ãŸå ´åˆã€è·é›¢åˆ¶ç´„ã‚’æ®µéšçš„ã«ç·©å’Œã—ã¦å†è©¦è¡Œã™ã‚‹
    private static readonly int[] DistanceRelaxation = { 0, 2, 4, 6 };

    // â”€â”€â”€ ãƒ¡ã‚¤ãƒ³ã‚¨ãƒ³ãƒˆãƒª â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void CrystalCore()
    {
        MapCreate mapcreate = GetComponent<MapCreate>();
        _SetPos = mapcreate.SetPos;
        maxx = mapcreate.maxX;
        maxz = mapcreate.maxZ;

        PlacePlayerCrystal();
        PlaceEnemyCrystal();
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ƒvƒŒƒCƒ„[ƒNƒŠƒXƒ^ƒ‹”z’u „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ ãƒ—ãƒ¬ã‚¤ãƒ¤ãƒ¼ã‚¯ãƒªã‚¹ã‚¿ãƒ«é…ç½® â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    private void PlacePlayerCrystal()
    {
        var candidates = _SetPos.Where(p =>
            p.x >= 6 && p.x <= maxx - 6 &&
            p.z >= 6 && p.z <= maxz - 6
        ).ToList();

        PCP = candidates[Random.Range(0, candidates.Count)];
        Instantiate(PlayerCrystal, PCP, Quaternion.identity, Playercrystal);
        _SetPos.Remove(PCP);
<<<<<<< HEAD
        Debug.Log("<color=#ffff00ff>[StartSetting]</color> ƒvƒŒƒCƒ„[ƒNƒŠƒXƒ^ƒ‹İ’uŠ®—¹");
    }

    // „Ÿ„Ÿ„Ÿ “GƒNƒŠƒXƒ^ƒ‹”z’ui‹——£§–ñ‚ğ’iŠK“I‚ÉŠÉ˜a‚µ‚ÄƒŠƒgƒ‰ƒCj „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
        Debug.Log("<color=#ffff00ff>[StartSetting]</color> ãƒ—ãƒ¬ã‚¤ãƒ¤ãƒ¼ã‚¯ãƒªã‚¹ã‚¿ãƒ«è¨­ç½®å®Œäº†");
    }

    // â”€â”€â”€ æ•µã‚¯ãƒªã‚¹ã‚¿ãƒ«é…ç½®ï¼ˆè·é›¢åˆ¶ç´„ã‚’æ®µéšçš„ã«ç·©å’Œã—ã¦ãƒªãƒˆãƒ©ã‚¤ï¼‰ â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    private void PlaceEnemyCrystal()
    {
        foreach (int relax in DistanceRelaxation)
        {
            int margin = relax == 0 ? 6 : 5;
            var candidates = GetEnemyCandidates(
                CrystalDistanceXmax - relax,
                CrystalDistanceZmax - relax,
                margin);

            if (candidates.Count == 0) continue;

            ECP = candidates[Random.Range(0, candidates.Count)];
            Instantiate(EnemyCrystal, ECP, Quaternion.identity, Enemycrystal);
<<<<<<< HEAD
            Debug.Log("<color=#ffff00ff>[StartSetting]</color> “GƒNƒŠƒXƒ^ƒ‹İ’uŠ®—¹");
            return;
        }

        Debug.LogError("[CrystalSystem] “GƒNƒŠƒXƒ^ƒ‹‚Ì”z’uŒó•â‚ªŒ©‚Â‚©‚è‚Ü‚¹‚ñ‚Å‚µ‚½");
=======
            Debug.Log("<color=#ffff00ff>[StartSetting]</color> æ•µã‚¯ãƒªã‚¹ã‚¿ãƒ«è¨­ç½®å®Œäº†");
            return;
        }

        Debug.LogError("[CrystalSystem] æ•µã‚¯ãƒªã‚¹ã‚¿ãƒ«ã®é…ç½®å€™è£œãŒè¦‹ã¤ã‹ã‚Šã¾ã›ã‚“ã§ã—ãŸ");
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    }

    private List<Vector3> GetEnemyCandidates(float minDistX, float minDistZ, int margin)
    {
        return _SetPos.Where(p =>
        {
            float dx = Mathf.Abs(p.x - PCP.x);
            float dz = Mathf.Abs(p.z - PCP.z);
            bool inBoundsX = p.x >= margin && p.x <= maxx - margin;
            bool inBoundsZ = p.z >= margin && p.z <= maxz - margin;
            return dx >= minDistX && dz >= minDistZ && inBoundsX && inBoundsZ;
        }).ToList();
    }
}
