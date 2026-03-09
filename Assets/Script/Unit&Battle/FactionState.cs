using UnityEngine;

public class FactionState : MonoBehaviour
{
    // ==== AP データ ====
    [System.Serializable]
    public class APData
    {
        [Header("現在の AP")] public int Current = 15;
        [Header("リセット値")] public int Reset = 15;
        [Header("ボーナス")] public int Plus = 0;
        [Header("ペナルティ")] public int Minus = 0;

        public void ResetForTurn() => Current = Reset + Plus - Minus;
    }

    // ==== 資源データ ====
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
        public int Plank;       // 追加（GameReference 準拠配布資源）
        public int CutStone;    // 追加（GameReference 準拠配布資源）
        public int Citizen;
    }

    // ==== Inspector 設定 ====
    [Header("Player")]
    [SerializeField] public APData PlayerAP = new APData();
    [SerializeField] public ResourceData PlayerResources = new ResourceData();

    [Header("Enemy")]
    [SerializeField] public APData EnemyAP = new APData();
    [SerializeField] public ResourceData EnemyResources = new ResourceData();

    // ==== AP 取得 / 設定 ====
    private APData GetAPData(Team team) => team == Team.Player ? PlayerAP : EnemyAP;

    public int GetAP(Team team) => GetAPData(team).Current;
    public void SetAP(Team team, int value) => GetAPData(team).Current = value;
    public void ModifyAP(Team team, int delta) => GetAPData(team).Current += delta;

    // ==== ターン開始時 AP リセット ====
    public void ResetAPForTurn(Team team) => GetAPData(team).ResetForTurn();

    // ==== 資源上限の基本値（倉庫なしの場合の上限） ====
    public const int BaseResourceCap = 200;

    // ==== 経済システムが毎ターン書き込む値 ====
    [HideInInspector] public int PlayerCitizenCapacity;
    [HideInInspector] public int PlayerResourceCapacity;
    [HideInInspector] public int PlayerBarracksXP;

    [HideInInspector] public int EnemyCitizenCapacity;
    [HideInInspector] public int EnemyResourceCapacity;
    [HideInInspector] public int EnemyBarracksXP;

    // ==== 実効資源上限 ====
    public int GetResourceCap(Team team)
    {
        int bonus = team == Team.Player ? PlayerResourceCapacity : EnemyResourceCapacity;
        return BaseResourceCap + bonus;
    }

    // ==== 実効市民収容 ====
    public int GetCitizenCap(Team team)
    {
        return team == Team.Player ? PlayerCitizenCapacity : EnemyCitizenCapacity;
    }

    // ==== 兵舎経験値ボーナス% ====
    public int GetBarracksXP(Team team)
    {
        return team == Team.Player ? PlayerBarracksXP : EnemyBarracksXP;
    }
}
