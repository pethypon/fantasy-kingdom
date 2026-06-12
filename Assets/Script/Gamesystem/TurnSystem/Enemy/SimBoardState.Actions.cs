using UnityEngine;

// =====================================================================
//  SimBoardState.Actions — 行動適用 (盤面変更)
//  Move / Attack / Build / Summon / Skill / Wait を処理する
// =====================================================================
public partial class SimBoardState
{
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
        unit.Fatigue += 1 + GameConstants.GetExtraFatiguePerAction(unit.Kind);

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
            attacker.Fatigue += 1 + GameConstants.GetExtraFatiguePerAction(attacker.Kind);
            ConsumeAP(action.ActorTeam, action.APCost);
            return true;
        }

        int damage = CalcDamage(attacker, target);
        target.HP = Mathf.Max(0, target.HP - damage);
        attacker.Fatigue += 1 + GameConstants.GetExtraFatiguePerAction(attacker.Kind);

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
        counts.TryGetValue(action.Facility, out int cur);
        counts[action.Facility] = cur + 1;

        ConsumeAP(action.ActorTeam, action.APCost);
        return true;
    }

    bool ApplySummon(SimAction action)
    {
        if (IsOccupied(action.TargetPos)) return false;

        int newId = NextUnitId();
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
                    if (target.HasDebuff(StatusEffectType.Poison)) healMod -= (1f - GameConstants.PoisonHealReduction);
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

    void ConsumeAP(Team team, int cost)
    {
        if (team == Team.Enemy)
            EnemyAP = Mathf.Max(0, EnemyAP - cost);
        else
            PlayerAP = Mathf.Max(0, PlayerAP - cost);
    }

    static int GetDefaultDebuffDuration(StatusEffectType t)
    {
        switch (t)
        {
            case StatusEffectType.Poison:
                return 2;
            default:
                return 1;
        }
    }
}
