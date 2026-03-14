using UnityEngine;

/// <summary>
/// 時間制限システム:
/// - 1ターン上限（デフォルト3分）
/// - 持ち時間（Player/Enemy各600分、最大3600分、設定で変更可）
/// - ターン制限時間終了で自動ターン切り替え
/// - 持ち時間終了でクリスタルHP比較による勝敗判定
/// </summary>
public class TimerSystem : MonoBehaviour
{
    [Header("1ターン上限（秒）")]
    public float TurnTimeLimit = 180f;

    [Header("持ち時間（秒） ※デフォルト600分=36000秒")]
    public float PlayerTotalTime = 36000f;
    public float EnemyTotalTime = 36000f;

    [Header("最大持ち時間（秒） ※デフォルト3600分=216000秒")]
    public float MaxTotalTime = 216000f;

    [Header("1ターン待ち時間ボーナス（秒）")]
    public float TurnTimeBonus = 60f;

    // ---- 現在値 ----
    private float turnTimeRemaining;
    private bool isRunning;
    private Team currentTeam;

    // ---- 外部参照 ----
    private TurnGenerater turngenerater;
    private CrystalSystem crystalsystem;

    // ---- コールバック: ターン自動終了 ----
    public System.Action OnTurnTimeExpired;
    public System.Action<GameResult> OnTotalTimeExpired;

    // ---- UI用プロパティ ----
    public float TurnTimeRemaining => turnTimeRemaining;
    public float PlayerTimeRemaining => PlayerTotalTime;
    public float EnemyTimeRemaining => EnemyTotalTime;
    public Team CurrentTeam => currentTeam;
    public bool IsRunning => isRunning;

    public void Init(TurnGenerater turngenerater, CrystalSystem crystalsystem)
    {
        this.turngenerater = turngenerater;
        this.crystalsystem = crystalsystem;
    }

    /// <summary>ターン開始時に呼ぶ</summary>
    public void StartTurn(Team team)
    {
        currentTeam = team;
        turnTimeRemaining = TurnTimeLimit;
        isRunning = true;
    }

    /// <summary>ターン終了時に呼ぶ（手動終了時）</summary>
    public void StopTurn()
    {
        isRunning = false;
    }

    private void Update()
    {
        if (!isRunning) return;

        float dt = Time.deltaTime;

        // 1ターン制限時間を減算
        turnTimeRemaining -= dt;

        // 持ち時間を減算
        if (currentTeam == Team.Player)
            PlayerTotalTime -= dt;
        else
            EnemyTotalTime -= dt;

        // 持ち時間が切れたらゲーム終了
        if (PlayerTotalTime <= 0f || EnemyTotalTime <= 0f)
        {
            isRunning = false;
            GameResult result = DetermineTimeUpResult();
            OnTotalTimeExpired?.Invoke(result);
            return;
        }

        // 1ターン制限時間が切れたら自動ターン終了
        if (turnTimeRemaining <= 0f)
        {
            isRunning = false;
            OnTurnTimeExpired?.Invoke();
        }
    }

    /// <summary>持ち時間切れ時の勝敗判定</summary>
    private GameResult DetermineTimeUpResult()
    {
        // クリスタルのHP比較
        Status playerCrystal = GetCrystalStatus(crystalsystem.Playercrystal);
        Status enemyCrystal = GetCrystalStatus(crystalsystem.Enemycrystal);

        if (playerCrystal == null || enemyCrystal == null)
        {
            Debug.LogError("[TimerSystem] クリスタルStatus取得失敗");
            return GameResult.TimeUpDraw;
        }

        float playerCrystalRatio = playerCrystal.MaxHP > 0
            ? (float)playerCrystal.HP / playerCrystal.MaxHP : 0f;
        float enemyCrystalRatio = enemyCrystal.MaxHP > 0
            ? (float)enemyCrystal.HP / enemyCrystal.MaxHP : 0f;

        Debug.Log($"[TimerSystem] 時間切れ判定: Pクリスタル={playerCrystalRatio:P1} Eクリスタル={enemyCrystalRatio:P1}");

        if (playerCrystalRatio > enemyCrystalRatio)
            return GameResult.TimeUpWin;
        if (enemyCrystalRatio > playerCrystalRatio)
            return GameResult.TimeUpLose;

        // クリスタル同率 → 王のHP%で比較
        Status playerKing = FindKing(Team.Player);
        Status enemyKing = FindKing(Team.Enemy);

        float playerKingRatio = (playerKing != null && playerKing.MaxHP > 0)
            ? (float)playerKing.HP / playerKing.MaxHP : 0f;
        float enemyKingRatio = (enemyKing != null && enemyKing.MaxHP > 0)
            ? (float)enemyKing.HP / enemyKing.MaxHP : 0f;

        Debug.Log($"[TimerSystem] 王HP比較: P王={playerKingRatio:P1} E王={enemyKingRatio:P1}");

        if (playerKingRatio > enemyKingRatio)
            return GameResult.TimeUpWin;
        if (enemyKingRatio > playerKingRatio)
            return GameResult.TimeUpLose;

        return GameResult.TimeUpDraw;
    }

    private Status GetCrystalStatus(Transform crystalParent)
    {
        if (crystalParent == null) return null;
        return crystalParent.GetComponentInChildren<Status>();
    }

    private Status FindKing(Team team)
    {
        if (turngenerater == null) return null;
        Transform parent = team == Team.Player
            ? turngenerater.unitset.PlayerUnit
            : turngenerater.unitset.EnemyUnit;
        if (parent == null) return null;
        foreach (Status s in parent.GetComponentsInChildren<Status>())
        {
            if (s.kind == Kind.King && s.gameObject.activeSelf)
                return s;
        }
        return null;
    }

    /// <summary>表示用: 秒を mm:ss に変換</summary>
    public static string FormatTime(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int min = (int)(seconds / 60f);
        int sec = (int)(seconds % 60f);
        return $"{min:D2}:{sec:D2}";
    }
}
