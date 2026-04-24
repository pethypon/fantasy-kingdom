using UnityEngine;

/// <summary>
/// 対戦中の統計情報を集約するシングルトン。
/// BattleSystem・SummonSystem・BuildSystem がイベント時に加算し、GameEndState が表示する。
/// ゲーム開始時に Reset() を呼ぶこと。
/// </summary>
public class MatchStats : MonoBehaviour
{
    public static MatchStats Instance { get; private set; }

    // ---- プレイヤー側指標 ----
    public int PlayerDamageDealt;
    public int PlayerDamageTaken;
    public int PlayerKills;
    public int PlayerLosses;
    public int PlayerSummons;
    public int PlayerBuildings;
    public int PlayerSkillsUsed;
    public int PlayerXPGained;

    // ---- 敵側指標 ----
    public int EnemyDamageDealt;
    public int EnemyDamageTaken;
    public int EnemyKills;
    public int EnemyLosses;
    public int EnemySummons;
    public int EnemyBuildings;

    // ---- 共通指標 ----
    public int TurnsPlayed;
    public int DungeonsClaimed;
    public int ArtifactsAcquired;
    public int WildBossDamage;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public static MatchStats GetOrCreate()
    {
        if (Instance == null)
        {
            var go = new GameObject("MatchStats");
            go.AddComponent<MatchStats>();
        }
        return Instance;
    }

    public void Reset()
    {
        PlayerDamageDealt = PlayerDamageTaken = PlayerKills = PlayerLosses = 0;
        PlayerSummons = PlayerBuildings = PlayerSkillsUsed = PlayerXPGained = 0;
        EnemyDamageDealt = EnemyDamageTaken = EnemyKills = EnemyLosses = 0;
        EnemySummons = EnemyBuildings = 0;
        TurnsPlayed = DungeonsClaimed = ArtifactsAcquired = WildBossDamage = 0;
    }

    public void RecordDamage(Team attacker, Team defender, int damage, bool isKill, bool isWildBossTarget)
    {
        if (isWildBossTarget) WildBossDamage += damage;
        if (attacker == Team.Player)
        {
            PlayerDamageDealt += damage;
            if (isKill) PlayerKills++;
        }
        else if (attacker == Team.Enemy)
        {
            EnemyDamageDealt += damage;
            if (isKill) EnemyKills++;
        }
        if (defender == Team.Player)
        {
            PlayerDamageTaken += damage;
            if (isKill) PlayerLosses++;
        }
        else if (defender == Team.Enemy)
        {
            EnemyDamageTaken += damage;
            if (isKill) EnemyLosses++;
        }
    }

    public void RecordSummon(Team team)
    {
        if (team == Team.Player) PlayerSummons++;
        else if (team == Team.Enemy) EnemySummons++;
    }

    public void RecordBuilding(Team team)
    {
        if (team == Team.Player) PlayerBuildings++;
        else if (team == Team.Enemy) EnemyBuildings++;
    }

    public void RecordSkillUsed(Team team)
    {
        if (team == Team.Player) PlayerSkillsUsed++;
    }

    public void RecordXPGain(Team team, int xp)
    {
        if (team == Team.Player) PlayerXPGained += xp;
    }
}
