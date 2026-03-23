using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  SimUtil — シミュレーション用ユーティリティ
// =====================================================================
public static class SimUtil
{
    /// <summary>Vector3Int間の距離（Vector3にキャストして計算）</summary>
    public static float Distance(Vector3Int a, Vector3Int b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>マンハッタン距離</summary>
    public static int Manhattan(Vector3Int a, Vector3Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    }
}

// =====================================================================
//  SimEffect — シミュレーション上のステータス効果
// =====================================================================
public struct SimEffect
{
    public StatusEffectType Debuff;
    public BuffType Buff;
    public int RemainingTurns;

    public bool IsDebuff => Debuff != StatusEffectType.None;
    public bool IsBuff => Buff != BuffType.None;

    public SimEffect(StatusEffectType debuff, int turns)
    {
        Debuff = debuff;
        Buff = BuffType.None;
        RemainingTurns = turns;
    }

    public SimEffect(BuffType buff, int turns)
    {
        Debuff = StatusEffectType.None;
        Buff = buff;
        RemainingTurns = turns;
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
    public PassiveSkill Passive;
    public List<SimEffect> Effects;

    public bool IsAlive => HP > 0;

    public SimUnit()
    {
        Effects = new List<SimEffect>();
    }

    public SimUnit Clone()
    {
        var c = new SimUnit
        {
            Id = Id, Team = Team, Kind = Kind, Type = Type,
            HP = HP, MaxHP = MaxHP, ATK = ATK, DEF = DEF,
            Position = Position, Direction = Direction,
            IsBoss = IsBoss, AssignedSkillId = AssignedSkillId,
            SkillCooldown = SkillCooldown, Fatigue = Fatigue,
            ShieldTurns = ShieldTurns, ShieldActivated = ShieldActivated,
            Passive = Passive,
        };
        c.Effects = new List<SimEffect>(Effects.Count);
        for (int i = 0; i < Effects.Count; i++)
            c.Effects.Add(Effects[i]);
        return c;
    }

    // ---- ステータス効果チェック ----
    public bool HasDebuff(StatusEffectType t)
    {
        for (int i = 0; i < Effects.Count; i++)
            if (Effects[i].Debuff == t) return true;
        return false;
    }

    public bool HasBuff(BuffType t)
    {
        for (int i = 0; i < Effects.Count; i++)
            if (Effects[i].Buff == t) return true;
        return false;
    }

    public bool IsStunned => HasDebuff(StatusEffectType.Stun);
    public bool IsMovementBlocked => HasDebuff(StatusEffectType.Freeze) || HasDebuff(StatusEffectType.Bind);

    // ---- ステータス修飾値 ----
    public float GetATKMod()
    {
        float mod = 1f;
        if (HasDebuff(StatusEffectType.Weaken)) mod -= 0.15f;
        if (HasDebuff(StatusEffectType.Chill)) mod -= 0.10f;
        if (HasBuff(BuffType.Offensive)) mod += 0.15f;
        return Mathf.Clamp(mod, 0f, 2f);
    }

    public float GetDEFMod()
    {
        float mod = 1f;
        if (HasDebuff(StatusEffectType.ArmorBreak)) mod -= 0.15f;
        if (HasBuff(BuffType.Defensive)) mod += 0.20f;
        return Mathf.Clamp(mod, 0f, 2f);
    }

    public float GetIncomingDamageMod()
    {
        float mod = 1f;
        if (HasDebuff(StatusEffectType.Mark)) mod += 0.10f;
        if (HasDebuff(StatusEffectType.Freeze)) mod += 0.10f;
        if (HasBuff(BuffType.Barrier)) mod -= 0.30f;
        return Mathf.Max(0f, mod);
    }

    public int GetMoveAPBonus()
    {
        int bonus = 0;
        if (HasDebuff(StatusEffectType.Slow)) bonus += 2;
        if (HasDebuff(StatusEffectType.Chill)) bonus += 2;
        return bonus;
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
    public Team ActorTeam;       // 行動者のチーム

    public SimAction()
    {
        TargetUnitId = -1;
        SkillId = -1;
        ActorTeam = Team.Enemy;
    }
}

// =====================================================================
//  SimBoardState — 完全シミュレーション可能な盤面状態
//  GameObjectに一切依存せず、Clone()で効率的に複製可能
//  移動・攻撃・建築・召喚・ステータス効果をシミュレーション実行できる
// =====================================================================
public class SimBoardState
{
    // ---- ユニットデータ ----
    public List<SimUnit> Units;

    // ---- AP ----
    public int EnemyAP;
    public int PlayerAP;

    // ---- AP基礎値（ターン開始リセット用） ----
    public int EnemyAPReset;
    public int PlayerAPReset;

    // ---- クリスタル ----
    public Vector3Int EnemyCrystalPos;
    public Vector3Int PlayerCrystalPos;

    // ---- 建築 ----
    public Dictionary<FacilityKind, int> EnemyBuildingCounts;
    public Dictionary<FacilityKind, int> PlayerBuildingCounts;

    // ---- マップデータ (共有・変更しない) ----
    public HashSet<Vector3Int> MapTiles; // 有効なタイル座標 (Y=0化済み)

    // ---- 占有セル ----
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

        // クリスタルをユニットとして追加 (まだ追加されていなければ)
        var eCrystal = FindCrystalStatus(unitSet.EnemyUnit);
        if (eCrystal != null && !HasCrystal(state, Team.Enemy))
            state.Units.Add(CaptureUnit(eCrystal, idCounter++));

        var pCrystal = FindCrystalStatus(unitSet.PlayerUnit);
        if (pCrystal != null && realBoard.PlayerCrystalVisible && !HasCrystal(state, Team.Player))
            state.Units.Add(CaptureUnit(pCrystal, idCounter++));

        // AP
        state.EnemyAP = realBoard.EnemyAP;
        state.PlayerAP = 20; // プレイヤーAPは概算
        state.EnemyAPReset = Mathf.Max(15, realBoard.EnemyAP);
        state.PlayerAPReset = 20;

        // 建築カウント
        state.EnemyBuildingCounts = new Dictionary<FacilityKind, int>(realBoard.EnemyBuildingCounts);
        state.PlayerBuildingCounts = new Dictionary<FacilityKind, int>();

        state.RebuildOccupied();
        return state;
    }

    static bool HasCrystal(SimBoardState state, Team team)
    {
        for (int i = 0; i < state.Units.Count; i++)
            if (state.Units[i].Kind == Kind.Crystal && state.Units[i].Team == team)
                return true;
        return false;
    }

    static SimUnit CaptureUnit(Status s, int id)
    {
        var su = new SimUnit
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
            Passive = s.passiveskill,
        };

        // ステータス効果をコピー
        if (s.ActiveEffects != null)
        {
            for (int i = 0; i < s.ActiveEffects.Count; i++)
            {
                var e = s.ActiveEffects[i];
                if (e.IsDebuff)
                    su.Effects.Add(new SimEffect(e.debuffType, e.remainingTurns));
                else if (e.IsBuff)
                    su.Effects.Add(new SimEffect(e.buffType, e.remainingTurns));
            }
        }

        return su;
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
        copy.EnemyAPReset = EnemyAPReset;
        copy.PlayerAPReset = PlayerAPReset;
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
        if (_occupiedCells == null)
            _occupiedCells = new HashSet<Vector3Int>();
        else
            _occupiedCells.Clear();

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

    public SimUnit GetKing(Team team)
    {
        for (int i = 0; i < Units.Count; i++)
        {
            if (Units[i].Kind == Kind.King && Units[i].Team == team && Units[i].IsAlive)
                return Units[i];
        }
        return null;
    }

    // ================================================================
    //  ターン遷移シミュレーション
    //  ターン間で発生するイベントを再現:
    //  - AP リセット, 疲労リセット
    //  - DoT ダメージ (毒8, 出血6)
    //  - ステータス効果ティック (ターン減算 + 除去)
    //  - クリスタルシールドティック
    //  - スキルクールダウン減算
    // ================================================================
    public void SimulateTurnTransition(Team nextTeam)
    {
        TurnCount++;

        // AP リセット
        if (nextTeam == Team.Enemy)
        {
            EnemyAP = EnemyAPReset;
        }
        else
        {
            PlayerAP = PlayerAPReset;
        }

        // 該当チームの全ユニットを処理
        for (int i = 0; i < Units.Count; i++)
        {
            var u = Units[i];
            if (!u.IsAlive) continue;
            if (u.Team != nextTeam) continue;

            // 疲労リセット
            u.Fatigue = 0;

            // DoT ダメージ
            for (int j = 0; j < u.Effects.Count; j++)
            {
                var eff = u.Effects[j];
                if (eff.Debuff == StatusEffectType.Poison)
                {
                    u.HP = Mathf.Max(0, u.HP - 8);
                }
                else if (eff.Debuff == StatusEffectType.Bleed)
                {
                    u.HP = Mathf.Max(0, u.HP - 6);
                }
            }

            // ステータス効果ティック
            for (int j = u.Effects.Count - 1; j >= 0; j--)
            {
                var eff = u.Effects[j];
                eff.RemainingTurns--;
                if (eff.RemainingTurns <= 0)
                    u.Effects.RemoveAt(j);
                else
                    u.Effects[j] = eff;
            }

            // クリスタルシールドティック
            if (u.ShieldTurns > 0)
                u.ShieldTurns--;

            // スキルクールダウン
            if (u.SkillCooldown > 0)
                u.SkillCooldown--;
        }

        // 死亡チェック (DoTで死んだユニット)
        for (int i = 0; i < Units.Count; i++)
        {
            if (!Units[i].IsAlive && Units[i].Type == Type.Unit)
                _occupiedCells.Remove(Units[i].Position);
        }
    }

    // ================================================================
    //  行動実行 (盤面を変更する)
    // ================================================================

    /// <summary>行動を適用して盤面を変更する。成功ならtrue。</summary>
    public bool ApplyAction(SimAction action)
    {
        switch (action.Type)
        {
            case SimActionType.Move:     return ApplyMove(action);
            case SimActionType.Attack:   return ApplyAttack(action);
            case SimActionType.Build:    return ApplyBuild(action);
            case SimActionType.Summon:   return ApplySummon(action);
            case SimActionType.SkillUse: return ApplySkill(action);
            case SimActionType.Wait:     return true;
            default: return false;
        }
    }

    bool ApplyMove(SimAction action)
    {
        var unit = GetUnit(action.UnitId);
        if (unit == null || !unit.IsAlive) return false;
        if (unit.IsStunned || unit.IsMovementBlocked) return false;
        if (IsOccupied(action.TargetPos)) return false;

        _occupiedCells.Remove(unit.Position);
        unit.Position = action.TargetPos;
        _occupiedCells.Add(unit.Position);
        unit.Fatigue++;

        ConsumeAP(action.ActorTeam, action.APCost);
        return true;
    }

    bool ApplyAttack(SimAction action)
    {
        var attacker = GetUnit(action.UnitId);
        var target = GetUnit(action.TargetUnitId);
        if (attacker == null || target == null || !attacker.IsAlive || !target.IsAlive)
            return false;
        if (attacker.IsStunned) return false;

        // シールドチェック
        if (target.ShieldTurns > 0)
        {
            attacker.Fatigue++;
            ConsumeAP(action.ActorTeam, action.APCost);
            return true;
        }

        int damage = CalcDamage(attacker, target);
        target.HP = Mathf.Max(0, target.HP - damage);
        attacker.Fatigue++;

        // クリスタルシールドチェック (HPが50%以下に初めて到達した場合)
        CheckCrystalShield(target);

        // 死亡処理
        if (!target.IsAlive)
            _occupiedCells.Remove(target.Position);

        ConsumeAP(action.ActorTeam, action.APCost);
        return true;
    }

    bool ApplyBuild(SimAction action)
    {
        var counts = action.ActorTeam == Team.Enemy ? EnemyBuildingCounts : PlayerBuildingCounts;
        if (!counts.ContainsKey(action.Facility))
            counts[action.Facility] = 0;
        counts[action.Facility]++;

        ConsumeAP(action.ActorTeam, action.APCost);
        return true;
    }

    bool ApplySummon(SimAction action)
    {
        if (IsOccupied(action.TargetPos)) return false;

        int newId = 0;
        for (int i = 0; i < Units.Count; i++)
            if (Units[i].Id >= newId) newId = Units[i].Id + 1;

        var newUnit = CreateSimUnitFromKind(action.SummonKind, action.ActorTeam, action.TargetPos, newId);
        Units.Add(newUnit);
        _occupiedCells.Add(action.TargetPos);

        ConsumeAP(action.ActorTeam, action.APCost);
        return true;
    }

    bool ApplySkill(SimAction action)
    {
        var unit = GetUnit(action.UnitId);
        if (unit == null || !unit.IsAlive) return false;
        if (unit.IsStunned) return false;

        if (!SkillData.Table.TryGetValue(action.SkillId, out var skill))
            return false;

        if (action.TargetUnitId >= 0)
        {
            var target = GetUnit(action.TargetUnitId);
            if (target != null && target.IsAlive)
            {
                if (skill.Multiplier > 0 && target.ShieldTurns <= 0)
                {
                    // 攻撃スキル
                    int dmg = CalcSkillDamage(unit, target, skill);
                    target.HP = Mathf.Max(0, target.HP - dmg);

                    // デバフ付与シミュレーション（確率50%以上なら付与とみなす）
                    if (skill.InflictDebuff != StatusEffectType.None && skill.DebuffChance >= 0.5f)
                    {
                        if (!target.HasDebuff(skill.InflictDebuff))
                        {
                            int dur = GetDefaultDebuffDuration(skill.InflictDebuff);
                            target.Effects.Add(new SimEffect(skill.InflictDebuff, dur));
                        }
                    }

                    CheckCrystalShield(target);

                    if (!target.IsAlive)
                        _occupiedCells.Remove(target.Position);
                }
                else if (skill.FixedHeal > 0 && unit.Team == target.Team)
                {
                    // 回復スキル
                    float healMod = 1f;
                    if (target.HasDebuff(StatusEffectType.Poison)) healMod -= 0.25f;
                    if (target.HasDebuff(StatusEffectType.Curse)) healMod -= 0.50f;
                    healMod = Mathf.Max(0f, healMod);
                    int heal = Mathf.RoundToInt(skill.FixedHeal * healMod);
                    target.HP = Mathf.Min(target.HP + heal, target.MaxHP);
                }

                // バフ付与
                if (skill.GrantBuff != BuffType.None)
                {
                    var buffTarget = skill.BuffToSelf ? unit : target;
                    if (!buffTarget.HasBuff(skill.GrantBuff))
                    {
                        int dur = skill.GrantBuff == BuffType.Haste ? 0 : 1;
                        buffTarget.Effects.Add(new SimEffect(skill.GrantBuff, dur));
                    }
                }
            }
        }

        unit.SkillCooldown = GetSkillCooldownFromRarity(action.SkillId);
        ConsumeAP(action.ActorTeam, action.APCost);
        return true;
    }

    void CheckCrystalShield(SimUnit target)
    {
        if (target.Kind != Kind.Crystal) return;
        if (target.ShieldActivated) return;
        if (target.MaxHP <= 0 || !target.IsAlive) return;

        float hpRatio = (float)target.HP / target.MaxHP;
        if (hpRatio < 0.5f)
        {
            target.ShieldTurns = 5;
            target.ShieldActivated = true;
        }
    }

    void ConsumeAP(Team team, int cost)
    {
        if (team == Team.Enemy)
            EnemyAP = Mathf.Max(0, EnemyAP - cost);
        else
            PlayerAP = Mathf.Max(0, PlayerAP - cost);
    }

    // ================================================================
    //  ダメージ計算 (SkillSystem.CalcNormalDamage と同一)
    //  ステータス効果による修飾を完全再現
    // ================================================================
    public static int CalcDamage(SimUnit attacker, SimUnit defender)
    {
        if (attacker == null || defender == null) return 0;

        float atkMod = attacker.GetATKMod();
        float defMod = defender.GetDEFMod();
        float incomingMod = defender.GetIncomingDamageMod();

        float atk = attacker.ATK * atkMod;
        float def = defender.DEF * defMod;

        float baseDmg = DamageCalculator.CalcRawBase(atk, def);
        baseDmg *= incomingMod;

        return Mathf.Max(0, Mathf.RoundToInt(baseDmg));
    }

    public static int CalcSkillDamage(SimUnit caster, SimUnit target, SkillData skill)
    {
        if (skill == null || caster == null || target == null) return 0;

        float atkMod = caster.GetATKMod();
        float defMod = target.GetDEFMod();
        float incomingMod = target.GetIncomingDamageMod();

        float atk = caster.ATK * atkMod;
        float def = target.DEF * defMod;

        float baseDmg = DamageCalculator.CalcRawBase(atk, def);
        float skillMul = skill.Multiplier;

        // 封技修飾
        if (caster.HasDebuff(StatusEffectType.Seal))
            skillMul = Mathf.Max(0, skillMul - 0.20f);

        baseDmg *= skillMul * incomingMod;

        // 固定ダメージ加算
        baseDmg += skill.FixedDamage;

        return Mathf.Max(1, Mathf.RoundToInt(baseDmg));
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

    static int GetDefaultDebuffDuration(StatusEffectType t)
    {
        switch (t)
        {
            case StatusEffectType.Poison:
            case StatusEffectType.Curse:
            case StatusEffectType.Bleed:
                return 2;
            default:
                return 1;
        }
    }

    // ================================================================
    //  AP計算ヘルパー (APSystem.CalcCost と同一)
    // ================================================================
    public int CalcMoveCost(SimUnit unit)
    {
        int cost = 3 + unit.Fatigue + unit.GetMoveAPBonus();
        return cost;
    }

    public int CalcAttackCost(SimUnit unit)
    {
        return 2 + unit.Fatigue;
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
            Passive = PassiveSkill.None,
        };
    }

    // ================================================================
    //  ゲーム終了判定
    // ================================================================
    public bool IsTerminal()
    {
        var ec = GetCrystal(Team.Enemy);
        var pc = GetCrystal(Team.Player);
        if (ec != null && !ec.IsAlive) return true;
        if (pc != null && !pc.IsAlive) return true;
        // King死亡もゲーム終了
        var ek = GetKing(Team.Enemy);
        var pk = GetKing(Team.Player);
        if (ek != null && !ek.IsAlive) return true;
        if (pk != null && !pk.IsAlive) return true;
        return false;
    }

    // ================================================================
    //  ユーティリティ
    // ================================================================
    public static Vector3Int ToCell(Vector3 v)
        => new Vector3Int(Mathf.RoundToInt(v.x), 0, Mathf.RoundToInt(v.z));

    public int GetAP(Team team) => team == Team.Enemy ? EnemyAP : PlayerAP;

    public int NextUnitId()
    {
        int maxId = 0;
        for (int i = 0; i < Units.Count; i++)
            if (Units[i].Id >= maxId) maxId = Units[i].Id + 1;
        return maxId;
    }
}
