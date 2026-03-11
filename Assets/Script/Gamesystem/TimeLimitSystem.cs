using UnityEngine;

/// <summary>
/// ターン制限時間・持ち時間・総合制限時間を管理する。
/// </summary>
public class TimeLimitSystem : MonoBehaviour
{
    [Header("時間設定(秒)")]
    [SerializeField] private float turnLimitSeconds = 180f;
    [SerializeField] private float playerTotalSeconds = 600f;
    [SerializeField] private float enemyTotalSeconds = 600f;
    [SerializeField] private float matchLimitSeconds = 3600f;

    private float turnRemaining;
    private float playerRemaining;
    private float enemyRemaining;
    private float matchRemaining;

    private TurnGenerater turn;
    private CrystalSystem crystalSystem;
    private UnitSetting unitSetting;
    private bool gameEnded;

    public float TurnRemaining => turnRemaining;
    public float TurnLimitSeconds => turnLimitSeconds;
    public float TeamRemaining(Team team) => team == Team.Player ? playerRemaining : enemyRemaining;

    public void Init(TurnGenerater turn, CrystalSystem crystalSystem, UnitSetting unitSetting)
    {
        this.turn = turn;
        this.crystalSystem = crystalSystem;
        this.unitSetting = unitSetting;

        playerRemaining = playerTotalSeconds;
        enemyRemaining = enemyTotalSeconds;
        matchRemaining = matchLimitSeconds;
        turnRemaining = turnLimitSeconds;
        gameEnded = false;
    }

    public void OnTurnStarted(Team team)
    {
        if (gameEnded) return;
        turnRemaining = turnLimitSeconds;
    }

    public void Tick(float deltaTime)
    {
        if (gameEnded || turn == null) return;

        Team current = turn.CurrentTeam;
        turnRemaining = Mathf.Max(0f, turnRemaining - deltaTime);

        if (current == Team.Player)
            playerRemaining = Mathf.Max(0f, playerRemaining - deltaTime);
        else
            enemyRemaining = Mathf.Max(0f, enemyRemaining - deltaTime);

        matchRemaining = Mathf.Max(0f, matchRemaining - deltaTime);

        if (turnRemaining <= 0f)
            turn.RequestForcedTurnEnd();

        if (playerRemaining <= 0f || enemyRemaining <= 0f || matchRemaining <= 0f)
            EndByTimeLimit();
    }

    private void EndByTimeLimit()
    {
        if (gameEnded) return;
        gameEnded = true;

        int playerCrystalHp = GetCrystalHp(Team.Player);
        int enemyCrystalHp = GetCrystalHp(Team.Enemy);

        GameResult result;
        if (playerCrystalHp != enemyCrystalHp)
        {
            result = playerCrystalHp > enemyCrystalHp ? GameResult.Win : GameResult.Lose;
        }
        else
        {
            float playerKing = GetKingHpRate(Team.Player);
            float enemyKing = GetKingHpRate(Team.Enemy);
            result = playerKing >= enemyKing ? GameResult.Win : GameResult.Lose;
        }

        turn.ChangeState(new GameEndState(turn, result, GameEndReason.TimeLimit));
    }

    private int GetCrystalHp(Team team)
    {
        Transform parent = team == Team.Player ? crystalSystem.Playercrystal : crystalSystem.Enemycrystal;
        if (parent == null || parent.childCount == 0) return 0;
        Status st = parent.GetChild(0).GetComponent<Status>();
        return st == null ? 0 : st.HP;
    }

    private float GetKingHpRate(Team team)
    {
        Transform unitParent = team == Team.Player ? unitSetting.PlayerUnit : unitSetting.EnemyUnit;
        if (unitParent == null) return 0f;

        foreach (Transform child in unitParent)
        {
            Status st = child.GetComponent<Status>();
            if (st == null || st.kind != Kind.King) continue;
            UnitData data;
            if (unitSetting.UnitDataMap != null && unitSetting.UnitDataMap.TryGetValue(Kind.King, out data))
            {
                int maxHp = data.GetHP(st.Level);
                if (maxHp > 0) return Mathf.Clamp01((float)st.HP / maxHp);
            }
            return st.HP > 0 ? 1f : 0f;
        }

        return 0f;
    }
}
