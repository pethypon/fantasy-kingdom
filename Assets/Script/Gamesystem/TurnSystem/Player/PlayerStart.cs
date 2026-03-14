using UnityEngine;

public class PlayerStart : StateCore
{
    private TurnGenerater turngenerater;
    private UnitClick unitclick;
    public AttackPointt attackpoint;
    public BattleSystem battlesystem;
    public VisionGenerater visiongenerater;
    public MoveGererater movegenerater;
    public MapCreate mapcreate;
    public CrystalSystem crystalsystem;
    public UnitSetting unitset;

    public PlayerStart(TurnGenerater turngenerater, UnitClick unitclick,
        AttackPointt attackpoint, BattleSystem battlesystem,
        VisionGenerater visiongenerater, MoveGererater movegenerater,
        MapCreate mapcreate, CrystalSystem crystalsystem, UnitSetting unitset)
    {
        this.turngenerater = turngenerater;
        this.unitclick = unitclick;
        this.attackpoint = attackpoint;
        this.battlesystem = battlesystem;
        this.visiongenerater = visiongenerater;
        this.movegenerater = movegenerater;
        this.mapcreate = mapcreate;
        this.crystalsystem = crystalsystem;
        this.unitset = unitset;
    }

    public void Entry()
    {
        // ターンカウント更新
        turngenerater.Turn++;

        // AP リセット（FactionState.ResetAPForTurn で Reset+Plus-Minus を計算）
        turngenerater.apsystem.ResetAP(Team.Player);

        // 疲労リセット
        turngenerater.apsystem.ResetFatigue(unitset.PlayerUnit);

        // サブクリスタル返却待ちタイマー処理
        if (turngenerater.subCrystalSystem != null)
            turngenerater.subCrystalSystem.TickPendingReturns(Team.Player);
        else
            Debug.LogWarning("[PlayerStart] subCrystalSystem が null のため TickPendingReturns をスキップ");

        // クリスタル無敵シールドカウントダウン
        TickCrystalShield(Team.Player);

        // タイムリミット: ターン開始時にターンタイマーリセット
        if (turngenerater.timeLimitSystem != null)
            turngenerater.timeLimitSystem.OnTurnStart(Team.Player);

        turngenerater.ChangeState(new PlayerMove(turngenerater, unitclick,
            attackpoint, battlesystem, visiongenerater,
            movegenerater, mapcreate, crystalsystem, unitset));
    }

    /// <summary>クリスタルの無敵シールドカウントダウン。0になったらStateをNormalに戻す</summary>
    private void TickCrystalShield(Team team)
    {
        var fs = turngenerater.apsystem?.factionState;
        if (fs == null) return;
        if (fs.GetShieldTurns(team) <= 0) return;

        fs.TickShield(team);

        if (fs.GetShieldTurns(team) <= 0)
        {
            // シールド期限切れ → クリスタルの State を Normal に戻す
            Transform crystalParent = team == Team.Player
                ? crystalsystem.Playercrystal
                : crystalsystem.Enemycrystal;
            foreach (Status s in crystalParent.GetComponentsInChildren<Status>())
            {
                if (s.kind == Kind.Crystal && s.state == State.Invincible)
                {
                    s.state = State.Normal;
                    Debug.Log($"[PlayerStart] {team} クリスタル無敵シールド解除");
                }
            }
        }
    }

    public void Update() { }
    public void Exit() { }
}
