using UnityEngine;

public enum GameResult { Win, Lose, Draw }

public class BattleSystem : MonoBehaviour
{
    public Status target;
    public Status AttackSide;
    public TurnGenerater turngenerater;

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

    public void SideDefender()
    {
        switch (target.passiveskill)
        {
            case PassiveSkill.Impregnable: break;
            case PassiveSkill.None: break;
        }
    }

    private void ApplyDamage(int damage)
    {
        if (target != null && target.IsInvincible)
        {
            Debug.Log($"[Battle] {target.kind} は無敵中のためダメージ無効");
            return;
        }

        damage = Mathf.Max(0, damage);
        target.HP -= damage;
        target.HP = Mathf.Max(0, target.HP);

        if (target.kind == Kind.Crystal && turngenerater != null && turngenerater.gameTimerSystem != null)
            turngenerater.gameTimerSystem.EvaluateMainCrystalShieldNow(target);

        Debug.Log($"[Battle] {AttackSide.kind} → {target.kind}  DMG:{damage}  残HP:{target.HP}");
    }

    private void CheckDeath()
    {
        if (target.HP > 0) return;

        if (target.kind == Kind.Crystal || target.kind == Kind.King)
        {
            HandleGameEnd();
            return;
        }

        if (target.type == Type.Unit)
        {
            HandleUnitDeath();
            return;
        }

        if (target.type == Type.Building || target.type == Type.Wall)
            HandleBuildingDeath();
    }

    private void HandleUnitDeath()
    {
        Debug.Log($"[Battle] {target.team} の {target.kind} が撃破された");

        Vector3 cellPos = turngenerater.movegenerater.Cell(target.transform.position);
        turngenerater.movegenerater.UnitPointData.Remove(cellPos);

        if (turngenerater.SelectUnit == target)
            turngenerater.SelectUnit = null;

        target.gameObject.SetActive(false);

        turngenerater.visiongenerater.VisionPoint(
            turngenerater.mapcreate,
            turngenerater.movegenerater,
            turngenerater.crystalsystem
        );
    }

    private void HandleBuildingDeath()
    {
        if (turngenerater.subCrystalSystem != null)
            turngenerater.subCrystalSystem.DestroyBuilding(target);
        else
            target.gameObject.SetActive(false);

        TryGrantSubCrystalBreakReward();
    }

    private void TryGrantSubCrystalBreakReward()
    {
        if (target == null || AttackSide == null) return;
        if (target.facilityKind != FacilityKind.SubCrystal) return;
        if (AttackSide.team == target.team) return;

        FactionState factionState = turngenerater.crystalsystem.Playercrystal.GetComponentInChildren<FactionState>();
        if (factionState == null) return;

        var res = AttackSide.team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        int roll = Random.Range(0, 4);
        switch (roll)
        {
            case 0: res.Bread += 50; Debug.Log("[Battle] サブクリスタル破壊報酬: パン+50"); break;
            case 1: res.Wood += 50; Debug.Log("[Battle] サブクリスタル破壊報酬: 木材+50"); break;
            case 2: res.Stone += 50; Debug.Log("[Battle] サブクリスタル破壊報酬: 石+50"); break;
            default: res.Iron += 50; Debug.Log("[Battle] サブクリスタル破壊報酬: 鉄+50"); break;
        }
    }

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
