using System.Text;
using UnityEngine;

/// <summary>Rule knowledge holds a shared definition, never a second balance table.</summary>
public static class AISpecialSkillKnowledge
{
    public readonly struct Knowledge
    {
        public readonly Kind UnitKind;
        public readonly SpecialSkillDefinition Definition;
        public readonly string Role, Risk, Reward, TargetRule, CounterMethod, SynergyTags;
        public readonly int Duration;
        public readonly bool RequiresVision, SelfDamage, AllyDamage;
        public UnitSpecialSkill SpecialSkill => Definition != null ? Definition.ability : UnitSpecialSkill.None;
        public Knowledge(Status unit, string role, string risk, string reward, string targetRule,
            string counter, string synergy, int duration, bool vision, bool self, bool ally)
        {
            UnitKind = unit.kind; Definition = SpecialSkillRuntime.Definition(unit); Role = role; Risk = risk;
            Reward = reward; TargetRule = targetRule; CounterMethod = counter; SynergyTags = synergy;
            Duration = duration; RequiresVision = vision; SelfDamage = self; AllyDamage = ally;
        }
    }

    public static Knowledge For(Status unit)
    {
        if (unit == null) return default;
        var definition = SpecialSkillRuntime.Definition(unit);
        switch (definition != null ? definition.ability : UnitSpecialSkill.None)
        {
            case UnitSpecialSkill.KnightBreakthrough: return new Knowledge(unit,"突破","突出・死角","押し込み","正面","回り込み","前衛",0,false,false,false);
            case UnitSpecialSkill.ArcherStationary: return new Knowledge(unit,"固定砲台・離脱","攻撃不能・敵ATK強化","未移動火力・敵別隠密","周囲の敵を発動時に記録","強制移動・別の敵で追跡","護衛",2,false,false,false);
            case UnitSpecialSkill.MagicRitual: return new Knowledge(unit,"予約必中","術者撃破・味方HP","供儀分の防御無視","視界内敵駒","術者撃破","護衛・回復",1,true,false,true);
            case UnitSpecialSkill.AssassinVisionSeal: return new Knowledge(unit,"視界妨害","次被ダメージ倍","敵視界封鎖","視界内敵","使用後追撃","回り込み",2,true,true,false);
            case UnitSpecialSkill.ScoutVisionGuard: return new Knowledge(unit,"防護","自身視界低下","一撃無効","発動時の視界内味方","小威力で解除","高価値後衛",2,false,false,false);
            case UnitSpecialSkill.PriestPurification: return new Knowledge(unit,"浄化","自己犠牲・味方強化消失","通常効果解除","周囲双方","範囲外へ","弱体解除",0,false,true,false);
            case UnitSpecialSkill.GuardianLifeLink: return new Knowledge(unit,"接続防衛","転送・死亡交換","重要対象保護","味方駒・建物","転送先評価","王・後衛",0,false,true,true);
            case UnitSpecialSkill.CrossbowStakes: return new Knowledge(unit,"経路制圧","弾倉・可動線・巻き込み","杭弾装填後の射撃で雷線","自身へ装填、命中した敵へ付着","線回避・端点移動","狭路・道・洞窟出口",SpecialSkillDefinition.StakeLifetime,false,false,false);
            case UnitSpecialSkill.MagicSniperLock: return new Knowledge(unit,"長距離必中","反動・標的固定","距離火力","視界内敵をロック","接近","護衛",1,true,true,false);
            case UnitSpecialSkill.LifeBomb: return new Knowledge(unit,"遅延爆弾","所持者HP・巻き込み","最大HP由来の爆発","自分・味方駒","離脱・拘束","忍者輸送",SpecialSkillDefinition.BombDelay,false,true,true);
            case UnitSpecialSkill.WhiteMageHeal: return new Knowledge(unit,"単体回復","行動機会","最大HP回復","視界内味方","回復役分断","高HP",0,true,false,false);
            case UnitSpecialSkill.HolyKnightSanctuary: return new Knowledge(unit,"継続回復","行動不能・被ダメ倍","範囲回復","設定範囲味方","集中攻撃","防衛",3,false,true,false);
            case UnitSpecialSkill.SamuraiCounter: return new Knowledge(unit,"反撃・挑発","元攻撃は被弾","反撃と後衛保護","視界内の敵1体","ゲージ期限待ち","護衛",3,true,false,false);
            case UnitSpecialSkill.NinjaSwap: return new Knowledge(unit,"味方輸送","双方の交換先危険","位置交換","マップ上の味方駒","交換先警戒","生命爆弾・救出",0,false,false,false);
            case UnitSpecialSkill.BerserkerRage: return new Knowledge(unit,"単発強化","DEF低下・AP増","段階火力","自身","先制攻撃","低HP",0,false,true,false);
            case UnitSpecialSkill.NavigatorPull: return new Knowledge(unit,"引き寄せ","接近反撃","正面へ隔離","視界内敵","正面閉鎖","包囲",0,true,false,false);
            default: return default;
        }
    }

    public static bool SniperRestrictsTargets(Status unit)
        => unit?.Special != null && unit.Special.sniperActive && unit.Special.ownerTurn >= unit.Special.sniperFireTurn;

    // Legacy simulation has no delayed/redirected special state. Keep the dedicated static
    // score rather than replacing it with a simulation of a different, ineffective action.
    public static bool NeedsSpecialStateModel(AIAction action)
    {
        if (action == null) return false;
        if (action.SpecialCommand != null) return true;
        if(action.PreserveSpecialEvaluation)return true;
        if (action.ActionType == AIActionType.Wait && HasSpecial(action.Unit)) return true;
        if (action.ActionType == AIActionType.Move || action.ActionType == AIActionType.Retreat
            || action.ActionType == AIActionType.Support || action.ActionType == AIActionType.Surround
            || action.ActionType == AIActionType.DefenseRepos) return HasSpecial(action.Unit);
        return action.ActionType == AIActionType.Attack && (HasSpecial(action.Unit) || HasSpecial(action.TargetUnit));
    }
    internal static bool HasSpecial(Status unit) => unit != null && (SpecialSkillRuntime.Definition(unit)?.enabledForPlay == true
        && SpecialSkillRuntime.Definition(unit).ability != UnitSpecialSkill.None
        || unit.Special != null && (unit.Special.ritualActive || unit.Special.sniperActive || unit.Special.nextDamageDouble
        || unit.Special.damageNullUnused || !string.IsNullOrEmpty(unit.Special.guardianPartnerId)
        || !string.IsNullOrEmpty(unit.Special.bombCasterId)));

    public static string CommandKey(SpecialSkillCommand command)
    {
        if (command == null) return "";
        var text = new StringBuilder();
        var ability = SpecialSkillRuntime.Definition(command.Caster)?.ability ?? UnitSpecialSkill.None;
        text.Append((int)ability);
        if (ability == UnitSpecialSkill.BerserkerRage) text.Append(":stage").Append(Mathf.Clamp(command.BerserkerStage, 0, 5));
        if (command.Sacrifices != null) foreach (var sacrifice in command.Sacrifices)
            if (sacrifice != null) text.Append(':').Append(sacrifice.GetInstanceID());
        return text.ToString();
    }

    public static float FutureWeight(AIBoardState board, int turns)
    {
        int level = board?.ReconThreatLevel ?? 1;
        int horizon = level < 10 ? 1 : level < 31 ? 3 : SpecialSkillDefinition.BombDelay;
        return turns <= horizon ? 1f / (1f + Mathf.Max(0, turns - 1) * .12f) : .2f / Mathf.Max(1, turns - horizon);
    }
}
