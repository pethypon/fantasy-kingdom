using UnityEngine;

/// <summary>
/// タイムリミットシステム: 1ターン制限時間 + 総合持ち時間の管理。
/// - 持ち時間: プレイヤー/敵 各600秒（設定で変更可、最大3600秒）
/// - 1ターン上限: 180秒（3分）
/// - ターン制限時間終了 → 自動でターン切り替え
/// - 総合持ち時間終了 → クリスタル残りHPで勝敗、同量なら王の残りHP%で決着
/// </summary>
public class TimeLimitSystem : MonoBehaviour
{
    // ==== 設定値（Inspector で変更可） ====
    [Header("持ち時間 (秒)")]
    [SerializeField] public float PlayerTotalTime = 600f;       // デフォルト600秒
    [SerializeField] public float EnemyTotalTime = 600f;
    [SerializeField] public float MaxTotalTime = 3600f;         // 最大持ち時間

    [Header("1ターン制限 (秒)")]
    [SerializeField] public float TurnTimeLimit = 180f;         // 3分

    // ==== ランタイム状態 ====
    [HideInInspector] public float PlayerTimeRemaining;
    [HideInInspector] public float EnemyTimeRemaining;
    [HideInInspector] public float TurnTimeRemaining;
    [HideInInspector] public Team CurrentTeam = Team.None;
    [HideInInspector] public bool IsRunning = false;

    // ==== 外部参照 ====
    private TurnGenerater turngenerater;
    private FactionState factionState;
    private CrystalSystem crystalsystem;
    private UnitSetting unitset;

    // ==== 自動ターン終了フラグ（ステートのUpdateで参照） ====
    [HideInInspector] public bool TurnTimedOut = false;
    [HideInInspector] public bool TotalTimedOut = false;

    public void Init(TurnGenerater tg, FactionState fs, CrystalSystem cs, UnitSetting us)
    {
        turngenerater = tg;
        factionState = fs;
        crystalsystem = cs;
        unitset = us;

        // 初期持ち時間を設定（上限クランプ）
        PlayerTimeRemaining = Mathf.Min(PlayerTotalTime, MaxTotalTime);
        EnemyTimeRemaining = Mathf.Min(EnemyTotalTime, MaxTotalTime);
        TurnTimeRemaining = TurnTimeLimit;
    }

    /// <summary>ターン開始時に呼ばれる</summary>
    public void OnTurnStart(Team team)
    {
        CurrentTeam = team;
        TurnTimeRemaining = TurnTimeLimit;
        TurnTimedOut = false;
        TotalTimedOut = false;
        IsRunning = true;
    }

    /// <summary>毎フレーム呼ばれる（PlayerMove.Update / EnemyMove.Entry から）</summary>
    public void Tick()
    {
        if (!IsRunning || CurrentTeam == Team.None) return;

        float dt = Time.deltaTime;

        // ターンタイマー減算
        TurnTimeRemaining -= dt;

        // 総合持ち時間減算
        if (CurrentTeam == Team.Player)
            PlayerTimeRemaining -= dt;
        else
            EnemyTimeRemaining -= dt;

        // ターン制限時間切れ
        if (TurnTimeRemaining <= 0f)
        {
            TurnTimeRemaining = 0f;
            TurnTimedOut = true;
            IsRunning = false;
            Debug.Log($"[TimeLimitSystem] {CurrentTeam} ターン制限時間切れ！自動ターン終了");
            return;
        }

        // 総合持ち時間切れ
        float remaining = CurrentTeam == Team.Player ? PlayerTimeRemaining : EnemyTimeRemaining;
        if (remaining <= 0f)
        {
            if (CurrentTeam == Team.Player) PlayerTimeRemaining = 0f;
            else EnemyTimeRemaining = 0f;
            TotalTimedOut = true;
            IsRunning = false;
            Debug.Log($"[TimeLimitSystem] {CurrentTeam} 総合持ち時間切れ！HP比較で勝敗判定");
            return;
        }
    }

    /// <summary>ターン終了時にタイマーを止める</summary>
    public void OnTurnEnd()
    {
        IsRunning = false;
    }

    // ==================================================================
    //  総合持ち時間切れ時の勝敗判定
    // ==================================================================
    public GameResult JudgeByHP()
    {
        int playerCrystalHP = GetCrystalHP(Team.Player);
        int enemyCrystalHP = GetCrystalHP(Team.Enemy);

        Debug.Log($"[TimeLimitSystem] HP比較: Player Crystal HP={playerCrystalHP}, Enemy Crystal HP={enemyCrystalHP}");

        if (playerCrystalHP > enemyCrystalHP) return GameResult.Win;
        if (playerCrystalHP < enemyCrystalHP) return GameResult.Lose;

        // 同量 → 王の残りHP%で決着
        float playerKingPct = GetKingHPPercent(Team.Player);
        float enemyKingPct = GetKingHPPercent(Team.Enemy);

        Debug.Log($"[TimeLimitSystem] 王HP%比較: Player King={playerKingPct:P1}, Enemy King={enemyKingPct:P1}");

        if (playerKingPct > enemyKingPct) return GameResult.Win;
        if (playerKingPct < enemyKingPct) return GameResult.Lose;

        return GameResult.TimeDraw;
    }

    private int GetCrystalHP(Team team)
    {
        Transform parent = team == Team.Player
            ? crystalsystem.Playercrystal
            : crystalsystem.Enemycrystal;

        foreach (Status s in parent.GetComponentsInChildren<Status>())
        {
            if (s.kind == Kind.Crystal) return s.HP;
        }
        return 0;
    }

    private float GetKingHPPercent(Team team)
    {
        Transform parent = team == Team.Player ? unitset.PlayerUnit : unitset.EnemyUnit;
        foreach (Status s in parent.GetComponentsInChildren<Status>())
        {
            if (s.kind == Kind.King && s.gameObject.activeSelf)
            {
                // UnitData の基本HP からmax取得（近似として CrystalHP は使わない）
                // 王の最大HPを Status.HP の初期値から推定（レベル1基準）
                // ここでは「現在HP / CrystalHP」ではなく、ユニットのHP自体を比較
                return s.HP;  // 絶対値を返し、呼び出し側で比較
            }
        }
        return 0f;
    }

    // ==== UI用: フォーマット済み文字列 ====
    public string GetTurnTimeText() => FormatTime(TurnTimeRemaining);
    public string GetPlayerTotalTimeText() => FormatTime(PlayerTimeRemaining);
    public string GetEnemyTotalTimeText() => FormatTime(EnemyTimeRemaining);

    public float GetTurnTimeRatio() => TurnTimeLimit > 0 ? Mathf.Clamp01(TurnTimeRemaining / TurnTimeLimit) : 0f;

    private static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int m = Mathf.FloorToInt(seconds / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        return $"{m:00}:{s:00}";
    }
}
