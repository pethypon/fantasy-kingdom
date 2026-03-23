using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  SimUtil — シミュレーション用ユーティリティ
// =====================================================================
public static class SimUtil
{
    /// <summary>Vector3Int間の距離（Vector3にキャストして計算）</summary>
    public static float Distance(Vector3Int a, Vector3Int b)
    {
        return Vector3.Distance((Vector3)a, (Vector3)b);
    }
}

// =====================================================================
//  SimUnit — シミュレーション用の軽量ユニットデータ
//  GameObjectに依存せず、コピー可能な値型ベースの表現
// =====================================================================
public class SimUnit
{
    public int Id;
    public Team Team;
    public Kind Kind;
    public Type Type;
    public int HP;
    public int MaxHP;
    public int ATK;
    public int DEF;
    public Vector3Int Position;
    public Direction Direction;
    public bool IsBoss;
    public int AssignedSkillId;
    public int SkillCooldown;
    public int Fatigue;
    public int ShieldTurns;
    public bool ShieldActivated;

    public bool IsAlive => HP > 0;

    public SimUnit Clone()
    {
        return new SimUnit
        {
            Id = Id,
            Team = Team,
            Kind = Kind,
            Type = Type,
            HP = HP,
            MaxHP = MaxHP,
            ATK = ATK,
            DEF = DEF,
            Position = Position,
            Direction = Direction,
            IsBoss = IsBoss,
            AssignedSkillId = AssignedSkillId,
            SkillCooldown = SkillCooldown,
            Fatigue = Fatigue,
            ShieldTurns = ShieldTurns,
            ShieldActivated = ShieldActivated,
        };
    }
}

// =====================================================================
//  SimAction — シミュレーション上の行動
// =====================================================================
public enum SimActionType
{
    Move,
    Attack,
    Build,
    Summon,
    SkillUse,
    Wait
}

public class SimAction
{
    public SimActionType Type;
    public int UnitId;           // 行動するユニットのID
    public Vector3Int TargetPos; // 移動先 or 建築位置
    public int TargetUnitId;     // 攻撃対象のID (-1 = なし)
    public int APCost;
    public FacilityKind Facility;
    public Kind SummonKind;
    public int SkillId;          // 使用スキルID (-1 = なし)

    public SimAction()
    {
        TargetUnitId = -1;
        SkillId = -1;
    }
}

// =====================================================================
//  SimBoardState — 完全シミュレーション可能な盤面状態
//  GameObjectに一切依存せず、Clone()で効率的に複製可能
//  移動・攻撃・建築・召喚をシミュレーション実行できる
// =====================================================================
public class SimBoardState
{
    // ---- ユニットデータ ----
    public List<SimUnit> Units;

    // ---- AP ----
    public int EnemyAP;
    public int PlayerAP;

    // ---- クリスタル ----
    public Vector3Int EnemyCrystalPos;
    public Vector3Int PlayerCrystalPos;

    // ---- 建築 ----
    public Dictionary<FacilityKind, int> EnemyBuildingCounts;
    public Dictionary<FacilityKind, int> PlayerBuildingCounts;

    // ---- マップデータ (共有・変更しない) ----
    public HashSet<Vector3Int> MapTiles; // 有効なタイル座標 (Y=0化済み)

    // ---- 占有セル (毎回Units+Crystalから再構築) ----
    HashSet<Vector3Int> _occupiedCells;

    // ---- ターン数 ----
    public int TurnCount;

    // ================================================================
    //  生成: 実際のゲーム状態からスナップショットを作成
    // ================================================================
    public static SimBoardState CreateFromGame(AIBoardState realBoard, MoveGererater moveGen,
        UnitSetting unitSet, CrystalSystem crystalSystem, APSystem apSystem)
    {
        var state = new SimBoardState();
        state.Units = new List<SimUnit>();
        state.TurnCount = realBoard.TurnCount;

        // マップタイル
        state.MapTiles = new HashSet<Vector3Int>();
        if (moveGen != null && moveGen.mapcreate != null)
        {
            foreach (var sp in moveGen.mapcreate.SetPos)
            {
                state.MapTiles.Add(new Vector3Int(
                    Mathf.RoundToInt(sp.x), 0, Mathf.RoundToInt(sp.z)));
            }
        }

        // 敵ユニット
        int idCounter = 0;
        foreach (var u in realBoard.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            state.Units.Add(CaptureUnit(u, idCounter++));
        }

        // プレイヤーユニット (視界内のみ)
        foreach (var u in realBoard.AlivePlayerUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            state.Units.Add(CaptureUnit(u, idCounter++));
        }

        // クリスタル
        state.EnemyCrystalPos = ToCell(crystalSystem.ECP);
        state.PlayerCrystalPos = ToCell(crystalSystem.PCP);

        // クリスタルをユニットとして追加
        var eCrystal = FindCrystalStatus(unitSet.EnemyUnit);
        if (eCrystal != null)
            state.Units.Add(CaptureUnit(eCrystal, idCounter++));

        var pCrystal = FindCrystalStatus(unitSet.PlayerUnit);
        if (pCrystal != null && realBoard.PlayerCrystalVisible)
            state.Units.Add(CaptureUnit(pCrystal, idCounter++));

        // AP
        state.EnemyAP = realBoard.EnemyAP;
        state.PlayerAP = 20; // プレイヤーAPは概算（不明なため）

        // 建築カウント
        state.EnemyBuildingCounts = new Dictionary<FacilityKind, int>(realBoard.EnemyBuildingCounts);
        state.PlayerBuildingCounts = new Dictionary<FacilityKind, int>();

        state.RebuildOccupied();
        return state;
    }

    static SimUnit CaptureUnit(Status s, int id)
    {
        return new SimUnit
        {
            Id = id,
            Team = s.team,
            Kind = s.kind,
            Type = s.type,
            HP = s.HP,
            MaxHP = s.MaxHP,
            ATK = s.ATK,
            DEF = s.DEF,
            Position = new Vector3Int(
                Mathf.RoundToInt(s.transform.position.x), 0,
                Mathf.RoundToInt(s.transform.position.z)),
            Direction = s.direction,
            IsBoss = s.IsBoss,
            AssignedSkillId = s.AssignedSkillId,
            SkillCooldown = s.SkillCooldown,
            Fatigue = s.Fatigue,
            ShieldTurns = s.ShieldTurns,
            ShieldActivated = s.ShieldActivated,
        };
    }

    static Status FindCrystalStatus(Transform parent)
    {
        if (parent == null) return null;
        foreach (Status s in parent.GetComponentsInChildren<Status>())
        {
            if (s.kind == Kind.Crystal && s.HP > 0) return s;
        }
        return null;
    }

    // ================================================================
    //  Clone — 完全な盤面コピー (探索のノード展開で使用)
    // ================================================================
    public SimBoardState Clone()
    {
        var copy = new SimBoardState();
        copy.Units = new List<SimUnit>(Units.Count);
        for (int i = 0; i < Units.Count; i++)
            copy.Units.Add(Units[i].Clone());
        copy.EnemyAP = EnemyAP;
        copy.PlayerAP = PlayerAP;
        copy.EnemyCrystalPos = EnemyCrystalPos;
        copy.PlayerCrystalPos = PlayerCrystalPos;
        copy.EnemyBuildingCounts = new Dictionary<FacilityKind, int>(EnemyBuildingCounts);
        copy.PlayerBuildingCounts = new Dictionary<FacilityKind, int>(PlayerBuildingCounts);
        copy.MapTiles = MapTiles; // 共有参照 (変更しない)
        copy.TurnCount = TurnCount;
        copy.RebuildOccupied();
        return copy;
    }

    // ================================================================
    //  占有セル再構築
    // ================================================================
    public void RebuildOccupied()
    {
        _occupiedCells = new HashSet<Vector3Int>();
        _occupiedCells.Add(EnemyCrystalPos);
        _occupiedCells.Add(PlayerCrystalPos);
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].IsAlive && Units[i].Type == Type.Unit)
                _occupiedCells.Add(Units[i].Position);
        }
    }

    public bool IsOccupied(Vector3Int pos) => _occupiedCells.Contains(pos);

    // ================================================================
    //  ユニット検索
    // ================================================================
    public SimUnit GetUnit(int id)
    {
        for (int i = 0; i < Units.Count; i++)
            if (Units[i].Id == id) return Units[i];
        return null;
    }

    public List<SimUnit> GetAliveUnits(Team team)
    {
        var list = new List<SimUnit>();
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].IsAlive && Units[i].Team == team && Units[i].Type == Type.Unit)
                list.Add(Units[i]);
        }
        return list;
    }

    public SimUnit GetCrystal(Team team)
    {
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].Kind == Kind.Crystal && Units[i].Team == team && Units[i].IsAlive)
                return Units[i];
        }
        return null;
    }

    // ================================================================
    //  行動実行 (盤面を変更する)
    // ================================================================

    /// <summary>行動を適用して盤面を変更する。成功ならtrue。</summary>
    public bool ApplyAction(SimAction action)
    {
        switch (action.Type)
        {
            case SimActionType.Move:   return ApplyMove(action);
            case SimActionType.Attack: return ApplyAttack(action);
            case SimActionType.Build:  return ApplyBuild(action);
            case SimActionType.Summon: return ApplySummon(action);
            case SimActionType.SkillUse: return ApplySkill(action);
            case SimActionType.Wait:   return true;
            default: return false;
        }
    }

    bool ApplyMove(SimAction action)
    {
        var unit = GetUnit(action.UnitId);
        if (unit == null || !unit.IsAlive) return false;
        if (IsOccupied(action.TargetPos)) return false;

        _occupiedCells.Remove(unit.Position);
        unit.Position = action.TargetPos;
        _occupiedCells.Add(unit.Position);
        unit.Fatigue++;

        ConsumeAP(unit.Team, action.APCost);
        return true;
    }

    bool ApplyAttack(SimAction action)
    {
        var attacker = GetUnit(action.UnitId);
        var target = GetUnit(action.TargetUnitId);
        if (attacker == null || target == null || !attacker.IsAlive || !target.IsAlive)
            return false;

        // シールドチェック
        if (target.ShieldTurns > 0)
        {
            // ダメージ無効だがAPは消費
            attacker.Fatigue++;
            ConsumeAP(attacker.Team, action.APCost);
            return true;
        }

        int damage = CalcDamage(attacker, target);
        target.HP -= damage;
        attacker.Fatigue++;

        if (target.HP <= 0)
        {
            target.HP = 0;
            _occupiedCells.Remove(target.Position);

            // クリスタルシールドチェック
            if (target.Kind == Kind.Crystal && !target.ShieldActivated
                && target.MaxHP > 0 && target.HP > 0)
            {
                float hpRatio = (float)target.HP / target.MaxHP;
                if (hpRatio < 0.5f)
                {
                    target.ShieldTurns = 5;
                    target.ShieldActivated = true;
                }
            }
        }
        else
        {
            // クリスタルシールドチェック (HP > 0)
            if (target.Kind == Kind.Crystal && !target.ShieldActivated && target.MaxHP > 0)
            {
                float hpRatio = (float)target.HP / target.MaxHP;
                if (hpRatio < 0.5f)
                {
                    target.ShieldTurns = 5;
                    target.ShieldActivated = true;
                }
            }
        }

        ConsumeAP(attacker.Team, action.APCost);
        return true;
    }

    bool ApplyBuild(SimAction action)
    {
        var counts = action.APCost > 0 ? EnemyBuildingCounts : PlayerBuildingCounts;
        // 行動のAPコストからチーム推定 (EnemyAPから引くので)
        if (!counts.ContainsKey(action.Facility))
            counts[action.Facility] = 0;
        counts[action.Facility]++;

        // ここではTeam.Enemyとして処理
        ConsumeAP(Team.Enemy, action.APCost);
        return true;
    }

    bool ApplySummon(SimAction action)
    {
        if (IsOccupied(action.TargetPos)) return false;

        // 新ユニットを追加
        int newId = 0;
        for (int i = 0; i < Units.Count; i++)
            if (Units[i].Id >= newId) newId = Units[i].Id + 1;

        var newUnit = CreateSimUnitFromKind(action.SummonKind, Team.Enemy, action.TargetPos, newId);
        Units.Add(newUnit);
        _occupiedCells.Add(action.TargetPos);

        ConsumeAP(Team.Enemy, action.APCost);
        return true;
    }

    bool ApplySkill(SimAction action)
    {
        var unit = GetUnit(action.UnitId);
        if (unit == null || !unit.IsAlive) return false;

        if (action.TargetUnitId >= 0)
        {
            var target = GetUnit(action.TargetUnitId);
            if (target != null && target.IsAlive)
            {
                if (!SkillData.Table.TryGetValue(action.SkillId, out var skill))
                    return false;

                if (skill.Multiplier > 0 && target.ShieldTurns <= 0)
                {
                    // 攻撃スキル
                    int dmg = CalcSkillDamage(unit, target, skill);
                    target.HP -= dmg;
                    if (target.HP <= 0)
                    {
                        target.HP = 0;
                        _occupiedCells.Remove(target.Position);
                    }
                }
                else if (skill.FixedHeal > 0 && unit.Team == target.Team)
                {
                    // 回復スキル
                    target.HP = Mathf.Min(target.HP + skill.FixedHeal, target.MaxHP);
                }
            }
        }

        unit.SkillCooldown = GetSkillCooldownFromRarity(action.SkillId);
        ConsumeAP(unit.Team, action.APCost);
        return true;
    }

    void ConsumeAP(Team team, int cost)
    {
        if (team == Team.Enemy)
            EnemyAP = Mathf.Max(0, EnemyAP - cost);
        else
            PlayerAP = Mathf.Max(0, PlayerAP - cost);
    }

    // ================================================================
    //  ダメージ計算 (BattleSystem.DamageGenerater と同一式)
    // ================================================================
    public static int CalcDamage(SimUnit attacker, SimUnit defender)
    {
        if (attacker == null || defender == null) return 0;
        int atk = attacker.ATK;
        int def = defender.DEF;
        return Mathf.Max(0, 1 + (atk / 6) + ((atk / 2) - (def / 4)));
    }

    public static int CalcSkillDamage(SimUnit caster, SimUnit target, SkillData skill)
    {
        if (skill == null || caster == null || target == null) return 0;
        float baseDmg = caster.ATK * skill.Multiplier;
        float defense = target.DEF * 0.25f;
        return Mathf.Max(1, Mathf.RoundToInt(baseDmg - defense + skill.FixedDamage));
    }

    int GetSkillCooldownFromRarity(int skillId)
    {
        if (!SkillData.Table.TryGetValue(skillId, out var skill)) return 2;
        switch (skill.Rarity)
        {
            case SkillRarity.Normal: return 1;
            case SkillRarity.Rare: return 2;
            case SkillRarity.SuperRare: return 3;
            case SkillRarity.Legendary: return 4;
            default: return 2;
        }
    }

    // ================================================================
    //  AP計算ヘルパー
    // ================================================================
    public int CalcMoveCost(SimUnit unit)
    {
        return 3 + unit.Fatigue; // 基本3 + 疲労
    }

    public int CalcAttackCost(SimUnit unit)
    {
        return 2 + unit.Fatigue; // 基本2 + 疲労
    }

    // ================================================================
    //  SimUnit作成ヘルパー (召喚用)
    // ================================================================
    static SimUnit CreateSimUnitFromKind(Kind kind, Team team, Vector3Int pos, int id)
    {
        int hp = 10, atk = 5, def = 3;
        if (UnitStaticData.Table.TryGetValue(kind, out var data))
        {
            hp = data.BaseHP;
            atk = data.BaseATK;
            def = data.BaseDEF;
        }

        return new SimUnit
        {
            Id = id,
            Team = team,
            Kind = kind,
            Type = Type.Unit,
            HP = hp,
            MaxHP = hp,
            ATK = atk,
            DEF = def,
            Position = pos,
            Direction = team == Team.Enemy ? Direction.S : Direction.N,
            IsBoss = false,
            AssignedSkillId = -1,
            SkillCooldown = 0,
            Fatigue = 0,
            ShieldTurns = 0,
            ShieldActivated = false,
        };
    }

    // ================================================================
    //  ユーティリティ
    // ================================================================
    public static Vector3Int ToCell(Vector3 v)
        => new Vector3Int(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.z));

    public int GetAP(Team team) => team == Team.Enemy ? EnemyAP : PlayerAP;
}
