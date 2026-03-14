using UnityEngine;

public class EnemyStart : StateCore
{
    private TurnGenerater turngenerater;
    private UnitClick unitclick;
    private AttackPointt attackpoint;
    private BattleSystem battlesystem;
    private VisionGenerater visiongenerater;
    private MoveGererater movegenerater;
    private MapCreate mapcreate;
    private CrystalSystem crystalsystem;
    private UnitSetting unitset;

    public EnemyStart(
        TurnGenerater turngenerater, UnitClick unitclick, AttackPointt attackpoint,
        BattleSystem battlesystem, VisionGenerater visiongenerater, MoveGererater movegenerater,
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
        Debug.Log("[EnemyStart] 敵ターン開始");

        turngenerater.apsystem.ResetAP(Team.Enemy);
        turngenerater.apsystem.ResetFatigue(unitset.EnemyUnit);

        // サブクリスタル返却待ちタイマー処理
        if (turngenerater.subCrystalSystem != null)
            turngenerater.subCrystalSystem.TickPendingReturns(Team.Enemy);

        // クリスタル無敵シールドカウントダウン
        TickCrystalShield(Team.Enemy);

        // タイムリミット: ターン開始時にターンタイマーリセット
        if (turngenerater.timeLimitSystem != null)
            turngenerater.timeLimitSystem.OnTurnStart(Team.Enemy);

        turngenerater.ChangeState(new EnemyMove(
            turngenerater, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
    }

    /// <summary>クリスタルの無敵シールドカウントダウン</summary>
    private void TickCrystalShield(Team team)
    {
        var fs = turngenerater.apsystem?.factionState;
        if (fs == null) return;
        if (fs.GetShieldTurns(team) <= 0) return;

        fs.TickShield(team);

        if (fs.GetShieldTurns(team) <= 0)
        {
            Transform crystalParent = team == Team.Player
                ? crystalsystem.Playercrystal
                : crystalsystem.Enemycrystal;
            foreach (Status s in crystalParent.GetComponentsInChildren<Status>())
            {
                if (s.kind == Kind.Crystal && s.state == State.Invincible)
                {
                    s.state = State.Normal;
                    Debug.Log($"[EnemyStart] {team} クリスタル無敵シールド解除");
                }
            }
        }
    }

    public void Update() { }
    public void Exit() { }
}
