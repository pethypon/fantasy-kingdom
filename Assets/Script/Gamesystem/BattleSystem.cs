using UnityEngine;

public enum GameResult { Win, Lose, TimeDraw }

public class BattleSystem : MonoBehaviour
{
    public Status target;
    public Status AttackSide;
    public TurnGenerater turngenerater;

    // ─── ダメージ発生（入口） ─────────────────────────────────────────
    public void DamageGenerater(TurnGenerater turngenerater)
    {
        this.turngenerater = turngenerater;
        if (target == null || turngenerater.SelectUnit == null) return;
        AttackSide = turngenerater.SelectUnit;

        // 無敵シールドチェック: クリスタルが無敵状態ならダメージ0
        if (target.kind == Kind.Crystal && target.state == State.Invincible)
        {
            Debug.Log($"[Battle] {target.team} クリスタルは無敵シールド中！ダメージ無効");
            return;
        }

        int damage = 0;
        switch (AttackSide.passiveskill)
        {
            case PassiveSkill.HunterEyes: break;
            case PassiveSkill.Destroyer: break;
            case PassiveSkill.Assassination: break;
            case PassiveSkill.Sniper: break;
            case PassiveSkill.None:
                damage = AttackSide.ATK - target.DEF;
                break;
        }
        ApplyDamage(damage);
        CheckCrystalShield();   // HP50%以下でシールド発動チェック
        CheckDeath();
    }

    // ─── 防御側パッシブ ───────────────────────────────────────────────
    public void SideDefender()
    {
        switch (target.passiveskill)
        {
            case PassiveSkill.Impregnable: break;
            case PassiveSkill.None: break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ダメージ適用
    // ═══════════════════════════════════════════════════════════════════
    private void ApplyDamage(int damage)
    {
        damage = Mathf.Max(0, damage);
        target.HP -= damage;
        target.HP = Mathf.Max(0, target.HP);
        Debug.Log($"[Battle] {AttackSide.kind} → {target.kind}  DMG:{damage}  残HP:{target.HP}");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  クリスタルHP50%以下 → 無敵シールド発動チェック
    // ═══════════════════════════════════════════════════════════════════
    private void CheckCrystalShield()
    {
        if (target.kind != Kind.Crystal) return;
        if (target.HP <= 0) return;

        var factionState = turngenerater.apsystem?.factionState;
        if (factionState == null) return;

        Team team = target.team;
        if (factionState.IsShieldUsed(team)) return;   // 既に使用済み

        float hpRatio = (float)target.HP / CrystalSystem.CrystalHP;
        if (hpRatio <= FactionState.ShieldThreshold)
        {
            factionState.ActivateShield(team);
            target.state = State.Invincible;
            Debug.Log($"[Battle] {team} クリスタル無敵シールド発動！ HP {target.HP}/{CrystalSystem.CrystalHP} ({hpRatio:P0}) → {FactionState.ShieldDuration}ターン無敵");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HP0 判定 → 駒 / ゲーム終了対象 に分岐
    // ═══════════════════════════════════════════════════════════════════
    private void CheckDeath()
    {
        if (target.HP > 0) return;

        // Crystal か King のどちらかが倒れたら即ゲーム終了
        if (target.kind == Kind.Crystal || target.kind == Kind.King)
        {
            HandleGameEnd();
        }
        else if (target.kind == Kind.SubCrystal)
        {
            HandleSubCrystalDeath();
        }
        else if (target.type == Type.Unit)
        {
            HandleUnitDeath();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  サブクリスタル破壊 → 攻撃側にランダム資源+50
    // ═══════════════════════════════════════════════════════════════════
    private void HandleSubCrystalDeath()
    {
        Debug.Log($"[Battle] {target.team} のサブクリスタルが破壊された");

        // サブクリスタルシステムで領地削除+返却予約
        if (turngenerater.subCrystalSystem != null)
            turngenerater.subCrystalSystem.DestroyBuilding(target);

        // 攻撃側が自陣以外のサブクリスタルを壊した場合、ランダム資源+50
        if (AttackSide != null && AttackSide.team != target.team)
        {
            var factionState = turngenerater.apsystem?.factionState;
            if (factionState != null)
            {
                GrantRandomResource(factionState, AttackSide.team);
            }
        }

        // 視界更新
        turngenerater.visiongenerater.VisionPoint(
            turngenerater.mapcreate,
            turngenerater.movegenerater,
            turngenerater.crystalsystem);
    }

    /// <summary>ランダムで1つの資源+50を付与（パン/木材/石/鉄）</summary>
    private void GrantRandomResource(FactionState fs, Team team)
    {
        var res = team == Team.Player ? fs.PlayerResources : fs.EnemyResources;
        int roll = Random.Range(0, 4);
        string name;
        switch (roll)
        {
            case 0: res.Bread += 50; name = "パン"; break;
            case 1: res.Wood += 50; name = "木材"; break;
            case 2: res.Stone += 50; name = "石"; break;
            default: res.Iron += 50; name = "鉄"; break;
        }
        Debug.Log($"[Battle] {team} サブクリスタル破壊報酬: {name}+50");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  A) 一般駒がHP0 → 盤面から除外
    // ═══════════════════════════════════════════════════════════════════
    private void HandleUnitDeath()
    {
        Debug.Log($"[Battle] {target.team} の {target.kind} が撃破された");

        Vector3 cellPos = turngenerater.movegenerater.Cell(target.transform.position);
        turngenerater.movegenerater.UnitPointData.Remove(cellPos);

        if (turngenerater.SelectUnit == target)
        {
            turngenerater.SelectUnit = null;
        }

        target.gameObject.SetActive(false);

        turngenerater.visiongenerater.VisionPoint(
            turngenerater.mapcreate,
            turngenerater.movegenerater,
            turngenerater.crystalsystem
        );
    }

    // ═══════════════════════════════════════════════════════════════════
    //  B) Crystal or King がHP0 → 勝敗確定 → ゲーム終了
    // ═══════════════════════════════════════════════════════════════════
    private void HandleGameEnd()
    {
        GameResult result;

        if (target.team == Team.Enemy)
        {
            result = GameResult.Win;
            Debug.Log($"[Battle] 敵 {target.kind} 破壊 → 勝利！");
        }
        else
        {
            result = GameResult.Lose;
            Debug.Log($"[Battle] 自軍 {target.kind} 破壊 → 敗北…");
        }

        turngenerater.ChangeState(new GameEndState(turngenerater, result));
    }
}
