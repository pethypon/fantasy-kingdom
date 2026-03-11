using UnityEngine;

public class GameTimerSystem : MonoBehaviour
{
    [Header("時間設定（分）")]
    [SerializeField] private float perTeamTimeMinutes = 600f;
    [SerializeField] private float maxTeamTimeMinutes = 3600f;
    [SerializeField] private float perTurnLimitMinutes = 3f;

    [Header("無敵シールド設定")]
    [SerializeField] private int mainCrystalShieldTurns = 5;

    private TurnGenerater turnGenerater;
    private bool isGameEnded;
    private int playerKingMaxHp = 1;
    private int enemyKingMaxHp = 1;

    public float PlayerRemainSeconds { get; private set; }
    public float EnemyRemainSeconds { get; private set; }
    public float TurnRemainSeconds { get; private set; }
    public Team ActiveTeam { get; private set; } = Team.Player;

    public float MaxTeamSeconds => maxTeamTimeMinutes * 60f;

    public void Init(TurnGenerater turn)
    {
        turnGenerater = turn;
        PlayerRemainSeconds = Mathf.Min(perTeamTimeMinutes * 60f, MaxTeamSeconds);
        EnemyRemainSeconds = Mathf.Min(perTeamTimeMinutes * 60f, MaxTeamSeconds);
        TurnRemainSeconds = perTurnLimitMinutes * 60f;
        isGameEnded = false;

        CacheKingMaxHp();
    }

    public void StartTurn(Team team)
    {
        if (isGameEnded) return;
        ActiveTeam = team;
        TurnRemainSeconds = perTurnLimitMinutes * 60f;

        TickInvincible(team);
        TryApplyMainCrystalShield(team);
    }

    private void Update()
    {
        if (turnGenerater == null || isGameEnded) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        TurnRemainSeconds = Mathf.Max(0f, TurnRemainSeconds - dt);

        if (ActiveTeam == Team.Player)
            PlayerRemainSeconds = Mathf.Max(0f, PlayerRemainSeconds - dt);
        else
            EnemyRemainSeconds = Mathf.Max(0f, EnemyRemainSeconds - dt);

        if (PlayerRemainSeconds <= 0f || EnemyRemainSeconds <= 0f)
        {
            EndByTotalTimeLimit();
            return;
        }

        if (TurnRemainSeconds <= 0f)
            AutoEndTurn();
    }


    private void CacheKingMaxHp()
    {
        if (turnGenerater?.crystalsystem == null) return;

        var playerKing = turnGenerater.crystalsystem.GetMainKingStatus(Team.Player);
        var enemyKing = turnGenerater.crystalsystem.GetMainKingStatus(Team.Enemy);

        if (playerKing != null) playerKingMaxHp = Mathf.Max(1, playerKing.HP);
        if (enemyKing != null) enemyKingMaxHp = Mathf.Max(1, enemyKing.HP);
    }

    private void AutoEndTurn()
    {
        if (turnGenerater == null || turnGenerater.StateManager == null) return;

        if (turnGenerater.StateManager is PlayerMove)
            turnGenerater.TurnEndDown = true;
    }

    private void TickInvincible(Team team)
    {
        var crystal = turnGenerater.crystalsystem != null
            ? turnGenerater.crystalsystem.GetMainCrystalStatus(team)
            : null;
        if (crystal != null && crystal.InvincibleTurns > 0)
            crystal.InvincibleTurns--;
    }

    private void TryApplyMainCrystalShield(Team team)
    {
        if (turnGenerater?.crystalsystem == null) return;

        Status crystal = turnGenerater.crystalsystem.GetMainCrystalStatus(team);
        EvaluateMainCrystalShieldNow(crystal);
    }

    // 攻撃直後にも呼べるように公開
    public void EvaluateMainCrystalShieldNow(Status crystal)
    {
        if (crystal == null) return;
        if (crystal.kind != Kind.Crystal) return;

        int threshold = CrystalSystem.CrystalHP / 2;
        if (!crystal.MainCrystalShieldTriggered && crystal.HP <= threshold)
        {
            crystal.MainCrystalShieldTriggered = true;
            crystal.InvincibleTurns = mainCrystalShieldTurns;
            Debug.Log($"[Timer] {crystal.team} メインクリスタル無敵シールド発動 ({mainCrystalShieldTurns}ターン)");
        }
    }
    private void EndByTotalTimeLimit()
    {
        isGameEnded = true;
        GameResult result = JudgeByHp();
        turnGenerater.ChangeState(new GameEndState(turnGenerater, result, "総合持ち時間切れ"));
    }

    private GameResult JudgeByHp()
    {
        var playerCrystal = turnGenerater.crystalsystem.GetMainCrystalStatus(Team.Player);
        var enemyCrystal = turnGenerater.crystalsystem.GetMainCrystalStatus(Team.Enemy);

        int playerCrystalHp = playerCrystal != null ? playerCrystal.HP : 0;
        int enemyCrystalHp = enemyCrystal != null ? enemyCrystal.HP : 0;

        if (playerCrystalHp > enemyCrystalHp) return GameResult.Win;
        if (playerCrystalHp < enemyCrystalHp) return GameResult.Lose;

        var playerKing = turnGenerater.crystalsystem.GetMainKingStatus(Team.Player);
        var enemyKing = turnGenerater.crystalsystem.GetMainKingStatus(Team.Enemy);

        float playerKingRate = playerKing != null && playerKing.HP > 0 ? (float)playerKing.HP / playerKingMaxHp : 0f;
        float enemyKingRate = enemyKing != null && enemyKing.HP > 0 ? (float)enemyKing.HP / enemyKingMaxHp : 0f;

        if (playerKingRate > enemyKingRate) return GameResult.Win;
        if (playerKingRate < enemyKingRate) return GameResult.Lose;

        return GameResult.Draw;
    }

    public bool IsGameEnded => isGameEnded;
}
