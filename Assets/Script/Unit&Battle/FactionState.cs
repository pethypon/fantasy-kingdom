using UnityEngine;

public class FactionState : MonoBehaviour
{
    // ─── AP データ ────────────────────────────────────────────────────
    [System.Serializable]
    public class APData
    {
        [Header("現在の AP")] public int Current = 15;
        [Header("リセット値")] public int Reset = 15;
        [Header("ボーナス")] public int Plus = 0;
        [Header("ペナルティ")] public int Minus = 0;

        public void ResetForTurn() => Current = Reset + Plus - Minus;
    }

    // ─── 資源データ ──────────────────────────────────────────────────
    [System.Serializable]
    public class ResourceData
    {
        public int Wood;
        public int Stone;
        public int Coal;
        public int IronOre;
        public int Iron;
        public int MagicOre;
        public int Wheat;
        public int Bread;
        public int Water;
        public int Plank;       // 追加（GameReference 初期配布資源）
        public int CutStone;    // 追加（GameReference 初期配布資源）
        public int Citizen;
    }

    // ─── Inspector 設定 ──────────────────────────────────────────────
    [Header("Player")]
    [SerializeField] public APData PlayerAP = new APData();
    [SerializeField] public ResourceData PlayerResources = new ResourceData();

    [Header("Enemy")]
    [SerializeField] public APData EnemyAP = new APData();
    [SerializeField] public ResourceData EnemyResources = new ResourceData();

    // ─── AP 取得 / 設定 ──────────────────────────────────────────────
    private APData GetAPData(Team team) => team == Team.Player ? PlayerAP : EnemyAP;

    public int GetAP(Team team) => GetAPData(team).Current;
    public void SetAP(Team team, int value) => GetAPData(team).Current = value;
    public void ModifyAP(Team team, int delta) => GetAPData(team).Current += delta;

    // ─── ターン開始時 AP リセット ─────────────────────────────────────
    public void ResetAPForTurn(Team team) => GetAPData(team).ResetForTurn();
}
