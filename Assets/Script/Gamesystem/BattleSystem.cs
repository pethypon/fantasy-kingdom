using UnityEngine;

public enum GameResult { Win, Lose }

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
        else if (target.type == Type.Unit)
        {
            HandleUnitDeath();
        }
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
