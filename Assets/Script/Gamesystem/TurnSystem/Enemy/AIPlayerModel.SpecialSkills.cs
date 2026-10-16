using System;
using UnityEngine;

public sealed partial class AIPlayerModel
{
    const int SpecialAbilityCount=(int)UnitSpecialSkill.NavigatorPull+1;
    const int MaximumSpecialSamples=1000000;
    public sealed partial class Profile
    {
        // Optional version-1 additions. Only bounded means and sample counts persist.
        public float[] SpecialUse=new float[SpecialAbilityCount], SpecialDetail=new float[SpecialAbilityCount];
        public int[] SpecialSamples=new int[SpecialAbilityCount], SpecialDetailSamples=new int[SpecialAbilityCount];
    }
    readonly int[] specialSamples=new int[SpecialAbilityCount], specialDetailSamples=new int[SpecialAbilityCount];
    readonly double[] specialUse=new double[SpecialAbilityCount], specialDetail=new double[SpecialAbilityCount];
    bool HasSpecialObservations {get {for(int i=1;i<specialSamples.Length;i++)if(specialSamples[i]>0)return true;return false;}}
    static bool HasFutureSpecialHistory(Profile profile) => profile!=null
        &&(profile.SpecialUse?.Length>SpecialAbilityCount||profile.SpecialDetail?.Length>SpecialAbilityCount
        ||profile.SpecialSamples?.Length>SpecialAbilityCount||profile.SpecialDetailSamples?.Length>SpecialAbilityCount);
    static bool ValidSpecialHistory(Profile profile)
    {
        if(profile==null)return false;
        if(profile.SpecialUse!=null)foreach(float value in profile.SpecialUse)if(!Valid(value))return false;
        if(profile.SpecialDetail!=null)foreach(float value in profile.SpecialDetail)if(!Valid(value))return false;
        if(profile.SpecialSamples!=null)foreach(int count in profile.SpecialSamples)if(count<0||count>MaximumSpecialSamples)return false;
        if(profile.SpecialDetailSamples!=null)foreach(int count in profile.SpecialDetailSamples)if(count<0||count>MaximumSpecialSamples)return false;
        return true;
    }
    static T[] CopySpecialArray<T>(T[] source)
    {
        var target=new T[SpecialAbilityCount];
        if(source!=null)Array.Copy(source,target,Math.Min(source.Length,target.Length));
        return target;
    }
    void NormalizeSpecialHistory()
    {
        history.SpecialUse=CopySpecialArray(history.SpecialUse);history.SpecialDetail=CopySpecialArray(history.SpecialDetail);
        history.SpecialSamples=CopySpecialArray(history.SpecialSamples);history.SpecialDetailSamples=CopySpecialArray(history.SpecialDetailSamples);
    }
    void CopySpecialHistory(Profile target)
    {
        target.SpecialUse=CopySpecialArray(history.SpecialUse);target.SpecialDetail=CopySpecialArray(history.SpecialDetail);
        target.SpecialSamples=CopySpecialArray(history.SpecialSamples);target.SpecialDetailSamples=CopySpecialArray(history.SpecialDetailSamples);
    }
    public void RecordSpecialSample(UnitSpecialSkill ability,bool used,float detail=float.NaN)
    {
        int index=(int)ability;
        if(completed||index<=0||index>=SpecialAbilityCount||specialSamples[index]>=MaximumSpecialSamples)return;
        specialSamples[index]++;if(used)specialUse[index]++;
        // Missing observations do not enter the detail denominator.
        if(used&&Valid(detail)&&specialDetailSamples[index]<MaximumSpecialSamples)
        {specialDetailSamples[index]++;specialDetail[index]+=detail;}
    }
    void ObserveSpecialState(Status actor)
    {
        var definition=SpecialSkillRuntime.Definition(actor);
        if(definition==null||!definition.enabledForPlay)return;
        if(definition.ability==UnitSpecialSkill.ArcherStationary)
        {
            float charge=definition.ArcherMultiplier(actor.Special?.stationaryTurns??0);
            RecordSpecialSample(definition.ability,charge>1,(charge-1)/Mathf.Max(.001f,SpecialSkillDefinition.ArcherMaximum-1));
        }
    }
    /// <summary>Caller supplies live visibility checks; commands are reduced to statistics immediately.</summary>
    public void ObserveSpecialCommand(SpecialSkillCommand command,Func<Status,bool> observable,Vector3 ownCrystal)
    {
        var caster=command?.Caster;var definition=SpecialSkillRuntime.Definition(caster);
        if(completed||definition==null||observable==null||!observable(caster))return;
        float detail=float.NaN;var target=command.Target;
        bool targetKnown=target!=null&&observable(target);
        switch(definition.ability)
        {
            case UnitSpecialSkill.MagicRitual:
                bool allKnown=command.Sacrifices!=null&&command.Sacrifices.Count>0;
                if(command.Sacrifices!=null)foreach(var ally in command.Sacrifices)if(!observable(ally)){allKnown=false;break;}
                if(allKnown)detail=Mathf.Clamp01(command.Sacrifices.Count/(float)Mathf.Max(1,definition.sacrificeLimit));
                break;
            case UnitSpecialSkill.CrossbowStakes:
                if(targetKnown&&!float.IsNaN(ownCrystal.x))detail=GridHelper.ChebyshevDistance(target.transform.position,ownCrystal)
                    <GridHelper.ChebyshevDistance(caster.transform.position,ownCrystal)?1:0;
                break;
            case UnitSpecialSkill.MagicSniperLock:
                if(targetKnown)detail=Mathf.Clamp01(GridHelper.ChebyshevDistance(caster.transform.position,target.transform.position)/SpecialSkillDefinition.SniperMaximumDistance);
                break;
            case UnitSpecialSkill.LifeBomb:
                if(targetKnown)detail=target.MaxHP>caster.MaxHP?1:0;
                break;
            case UnitSpecialSkill.NinjaSwap:
                if(targetKnown)detail=!string.IsNullOrEmpty(target.Special?.bombCasterId)?1:0;
                break;
        }
        RecordSpecialSample(definition.ability,true,detail);
    }
    void CompleteSpecialHistory(Profile next)
    {
        for(int i=1;i<SpecialAbilityCount;i++)
        {
            if(specialSamples[i]>0)
            {
                next.SpecialUse[i]=Mathf.Lerp(history.SpecialUse[i],(float)(specialUse[i]/specialSamples[i]),.2f);
                next.SpecialSamples[i]=(int)Math.Min(MaximumSpecialSamples,(long)history.SpecialSamples[i]+specialSamples[i]);
            }
            if(specialDetailSamples[i]>0)
            {
                next.SpecialDetail[i]=Mathf.Lerp(history.SpecialDetail[i],(float)(specialDetail[i]/specialDetailSamples[i]),.2f);
                next.SpecialDetailSamples[i]=(int)Math.Min(MaximumSpecialSamples,(long)history.SpecialDetailSamples[i]+specialDetailSamples[i]);
            }
        }
    }
    float SpecialBonus(AIAction action,int level,AIBoardState board)
    {
        if(action?.Unit==null||level<10)return 0;
        if(action.ActionType==AIActionType.Move&&board!=null)
        {
            float adjustment=0;
            foreach(var opponent in board.AlivePlayerUnits)
            {
                if(!board.IsSpecialActorObserved(opponent))continue;
                var d=SpecialSkillRuntime.Definition(opponent);if(d==null)continue;
                int key=(int)d.ability;if(key<=0||key>=SpecialAbilityCount)continue;
                float observedConfidence=Mathf.Clamp01((history.SpecialSamples[key]+specialSamples[key])/20f);
                float distancePreference=history.SpecialDetail[key];
                if(specialDetailSamples[key]>0)distancePreference=Mathf.Lerp(distancePreference,(float)(specialDetail[key]/specialDetailSamples[key]),.6f);
                if(d.ability==UnitSpecialSkill.MagicSniperLock)
                    adjustment+=Mathf.Clamp(GridHelper.ChebyshevDistance(action.Unit.transform.position,opponent.transform.position)
                        -GridHelper.ChebyshevDistance(action.TargetPos,opponent.transform.position),-1,1)*distancePreference*observedConfidence*6;
                if(d.ability==UnitSpecialSkill.CrossbowStakes)
                    adjustment-=Mathf.Min(6,board.EstimateSpecialHazardAt(action.Unit,action.TargetPos))*observedConfidence;
            }
            return Mathf.Clamp(adjustment*(level<31?.5f:1),-12,12);
        }
        if(action.TargetUnit==null)return 0;
        var definition=SpecialSkillRuntime.Definition(action.TargetUnit);
        if(definition==null)return 0;
        int index=(int)definition.ability;
        if(index<=0||index>=SpecialAbilityCount)return 0;
        float tendency=history.SpecialUse[index],detail=history.SpecialDetail[index];
        int samples=history.SpecialSamples[index];
        if(specialSamples[index]>0){tendency=Mathf.Lerp(tendency,(float)(specialUse[index]/specialSamples[index]),.6f);samples+=specialSamples[index];}
        if(specialDetailSamples[index]>0)detail=Mathf.Lerp(detail,(float)(specialDetail[index]/specialDetailSamples[index]),.6f);
        float confidence=Mathf.Clamp01(samples/20f),bonus=0;
        if(action.ActionType==AIActionType.Attack)
        {
            if(definition.ability==UnitSpecialSkill.ArcherStationary||definition.ability==UnitSpecialSkill.MagicRitual
                ||definition.ability==UnitSpecialSkill.CrossbowStakes||definition.ability==UnitSpecialSkill.LifeBomb
                ||definition.ability==UnitSpecialSkill.NinjaSwap)bonus=tendency*8+detail*4;
            if(definition.ability==UnitSpecialSkill.AssassinVisionSeal&&action.TargetUnit.Special?.nextDamageDouble==true)bonus=detail*8;
        }
        return Mathf.Clamp(bonus*confidence*(level<31?.5f:1),-12,12);
    }
}
