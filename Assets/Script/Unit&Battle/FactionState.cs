using UnityEngine;

public class FactionState : MonoBehaviour
{
<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ AP ƒf[ƒ^ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
    [System.Serializable]
    public class APData
    {
        [Header("Œ»Ý‚Ì AP")] public int Current = 15;
        [Header("ƒŠƒZƒbƒg’l")] public int Reset = 15;
        [Header("ƒ{[ƒiƒX")] public int Plus = 0;
        [Header("ƒyƒiƒ‹ƒeƒB")] public int Minus = 0;
=======
    // â”€â”€â”€ AP ãƒ‡ãƒ¼ã‚¿ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [System.Serializable]
    public class APData
    {
        [Header("ç¾åœ¨ã® AP")] public int Current = 15;
        [Header("ãƒªã‚»ãƒƒãƒˆå€¤")] public int Reset = 15;
        [Header("ãƒœãƒ¼ãƒŠã‚¹")] public int Plus = 0;
        [Header("ãƒšãƒŠãƒ«ãƒ†ã‚£")] public int Minus = 0;
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13

        public void ResetForTurn() => Current = Reset + Plus - Minus;
    }

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ Ž‘Œ¹ƒf[ƒ^ „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ è³‡æºãƒ‡ãƒ¼ã‚¿ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    [System.Serializable]
    public class ResourceData
    {
        public int Wood;
        public int Stone;
        public int Coal;
        public int IronOre;
        public int MagicOre;
        public int Wheat;
        public int Bread;
        public int Water;
<<<<<<< HEAD
<<<<<<< HEAD
<<<<<<< HEAD
        public int Plank;       // ’Ç‰ÁiGameReference ‰Šú”z•zŽ‘Œ¹j
        public int CutStone;    // ’Ç‰ÁiGameReference ‰Šú”z•zŽ‘Œ¹j
        public int Citizen;
    }

    // „Ÿ„Ÿ„Ÿ Inspector Ý’è „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
        public int Plank;
        public int CutStone;
        public int Citizen;
    }

    // â”€â”€â”€ Inspector è¨­å®š â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
=======
        public int Citizen;
    }

    // „Ÿ„Ÿ„Ÿ ƒCƒ“ƒXƒyƒNƒ^[Ý’è „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
>>>>>>> parent of d903d2d (2)
=======
        public int Citizen;
    }

    // „Ÿ„Ÿ„Ÿ ƒCƒ“ƒXƒyƒNƒ^[Ý’è „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
>>>>>>> parent of d903d2d (2)
    [Header("Player")]
    [SerializeField] public APData PlayerAP = new APData();
    [SerializeField] public ResourceData PlayerResources = new ResourceData();

    [Header("Enemy")]
    [SerializeField] public APData EnemyAP = new APData();
    [SerializeField] public ResourceData EnemyResources = new ResourceData();

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ AP Žæ“¾ / Ý’è „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ AP å–å¾— / è¨­å®š â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    private APData GetAPData(Team team) => team == Team.Player ? PlayerAP : EnemyAP;

    public int GetAP(Team team) => GetAPData(team).Current;
    public void SetAP(Team team, int value) => GetAPData(team).Current = value;
    public void ModifyAP(Team team, int delta) => GetAPData(team).Current += delta;

<<<<<<< HEAD
    // „Ÿ„Ÿ„Ÿ ƒ^[ƒ“ŠJŽnŽž AP ƒŠƒZƒbƒg „Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ„Ÿ
=======
    // â”€â”€â”€ ã‚¿ãƒ¼ãƒ³é–‹å§‹æ™‚ AP ãƒªã‚»ãƒƒãƒˆ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
    public void ResetAPForTurn(Team team) => GetAPData(team).ResetForTurn();
}
