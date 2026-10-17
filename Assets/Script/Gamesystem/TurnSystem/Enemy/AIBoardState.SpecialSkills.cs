using System.Collections.Generic;
using UnityEngine;

public partial class AIBoardState
{
    public GameSystems SpecialSystems { get; set; }
    readonly List<Status> specialObservedActors = new List<Status>(64);
    readonly Dictionary<(Status,Vector3),int> specialHazardCache = new Dictionary<(Status,Vector3),int>();
    readonly Dictionary<Status,int> specialAttackFlatCache = new Dictionary<Status,int>();
    bool hasObservedSpecialState;
    public bool HasObservedSpecialState { get { _=SpecialObservedActors;return hasObservedSpecialState; } }
    int specialObservedGeneration = -1;
    public IReadOnlyList<Status> SpecialObservedActors
    {
        get
        {
            if (specialObservedGeneration == Generation) return specialObservedActors;
            specialObservedGeneration = Generation;
            specialObservedActors.Clear();
            specialHazardCache.Clear(); specialAttackFlatCache.Clear(); hasObservedSpecialState=false;
            foreach (var actor in AliveEnemyUnits) if (IsSpecialActorObserved(actor)) specialObservedActors.Add(actor);
            foreach (var actor in AlivePlayerUnits) if (IsSpecialActorObserved(actor)) specialObservedActors.Add(actor);
            AppendOwnedSpecialTargets(_buildSystem != null ? _buildSystem.GetBuildingParent(ActorTeam) : null);
            AppendOwnedSpecialTargets(OwnCrystalParent);
            if (PlayerCrystalVisible)
            {
                var crystal = FindCrystal(OpponentCrystalParent);
                if (crystal != null && crystal.IsAlive && !specialObservedActors.Contains(crystal)) specialObservedActors.Add(crystal);
            }
            foreach(var actor in specialObservedActors)if(AISpecialSkillKnowledge.HasSpecial(actor)){hasObservedSpecialState=true;break;}
            return specialObservedActors;
        }
    }
    public int ObservedAttackFlatAdjustment(Status actor)
    {
        _ = SpecialObservedActors;
        if (!IsSpecialActorObserved(actor)) return 0;
        if (!specialAttackFlatCache.TryGetValue(actor, out int value))
            specialAttackFlatCache[actor] = value = SpecialSkillRuntime.GetATKFlatAdjustment(actor);
        return value;
    }
    void AppendOwnedSpecialTargets(Transform parent)
    {
        if (parent == null) return;
        foreach (Transform child in parent)
        {
            var actor = child.GetComponentInChildren<Status>();
            if (actor != null && actor.IsAlive && actor.team == ActorTeam && !specialObservedActors.Contains(actor)) specialObservedActors.Add(actor);
        }
    }
    public int EstimateSpecialHazardAt(Status unit, Vector3 destination)
    {
        var observed=SpecialObservedActors;
        if(!hasObservedSpecialState)return 0;
        if(specialHazardCache.TryGetValue((unit,destination),out int value))return value;
        return specialHazardCache[(unit,destination)]=SpecialSkillRuntime.HazardDamageAt(unit,destination,observed);
    }

    /// <summary>Only owned or currently observed actors may expose their current state to evaluation.</summary>
    public bool IsSpecialActorObserved(Status actor)
    {
        if (actor == null || !actor.IsAlive || !actor.gameObject.activeInHierarchy) return false;
        if (actor.team == ActorTeam) return true;
        return (AlivePlayerUnits.Contains(actor) || PlayerCrystalVisible && actor.kind == Kind.Crystal)
            && IsVisibleToEnemy(actor.transform.position) && _visionGen != null && _visionGen.IsActorVisibleTo(ActorTeam, actor);
    }

    public Status FindObservedSpecialActor(string lifeId)
    {
        if (string.IsNullOrEmpty(lifeId)) return null;
        foreach (var actor in SpecialObservedActors)
            if (actor != null && actor.ReflectionLifeId == lifeId && IsSpecialActorObserved(actor)) return actor;
        return null;
    }

    public void CollectSpecialTargets(Status caster, List<Status> result)
    {
        result.Clear();
        if (caster == null) return;
        if (SpecialSkillRuntime.IsLegalTarget(caster, caster, out _)) result.Add(caster);
        foreach (var actor in SpecialObservedActors)
            if (actor != caster && IsSpecialActorObserved(actor) && SpecialSkillRuntime.IsLegalTarget(caster, actor, out _)) result.Add(actor);
    }

    public void CollectRitualSacrifices(Status caster, List<Status> result)
    {
        result.Clear();
        foreach (var ally in AliveEnemyUnits)
            if (ally != null && ally.team == ActorTeam && SpecialSkillRuntime.IsLegalSacrifice(caster, ally))
                result.Add(ally);
    }
}
