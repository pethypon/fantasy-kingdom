using UnityEngine;

// Observation stages describe knowledge, never a new attack permission or an inferred true location.
public enum AIBaseKnowledgeStage { Unknown, TerritoryLead, Observed, LastKnown, Disproved, Destroyed }

public partial class AIBoardState
{
    void RefreshMainCrystalObservation()
    {
        observedMainCrystal = null;
        PlayerCrystalVisible = false; PlayerCrystalHP = -1;
        if (OpponentCrystalParent != null)
        {
            OpponentCrystalParent.GetComponentsInChildren(true, _crystalCompBuffer);
            foreach (var candidate in _crystalCompBuffer)
            {
                // This is the observation adapter's sole gate. No current HP or position is
                // published unless that actual main crystal's cell is officially visible.
                if (candidate == null || candidate.kind != Kind.Crystal || candidate.team != OpponentTeam
                    || !IsCellInEnemyVision(candidate.transform.position)) continue;
                if (candidate.HP <= 0)
                {
                    ForgetMainCrystal(AIBaseKnowledgeStage.Destroyed);
                    continue;
                }
                if (!candidate.gameObject.activeInHierarchy) continue;
                if (_visionGen != null && !_visionGen.IsActorVisibleTo(ActorTeam, candidate)) continue;
                observedMainCrystal = candidate; PlayerCrystalVisible = true;
                PlayerCrystalPos = candidate.transform.position; PlayerCrystalHP = candidate.HP;
                mainCrystalDisposition = AIBaseKnowledgeStage.Observed;
                return;
            }
        }
        // Inspect the remembered cell, not the hidden live Transform. A visible empty
        // location disproves the old coordinate even if the actual crystal moved in Fog.
        if (_lastKnownPlayerCrystal.Valid && IsCellInEnemyVision(_lastKnownPlayerCrystal.Position))
            ForgetMainCrystal(AIBaseKnowledgeStage.Disproved);
    }
    void ForgetMainCrystal(AIBaseKnowledgeStage reason)
    {
        _lastKnownPlayerCrystal = default;
        _lastKnownPlayerPositions.Remove(int.MinValue);
        PlayerCrystalPos = Vector3.zero; PlayerCrystalHP = -1;
        mainCrystalDisposition = reason;
    }
}
