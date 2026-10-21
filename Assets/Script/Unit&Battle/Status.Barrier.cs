using UnityEngine;

public partial class Status
{
    [SerializeField, HideInInspector] int barrierPoints;
    [SerializeField, HideInInspector] bool barrierInitialized;
    [HideInInspector] public BarrierRecoveryState BarrierRecovery = new BarrierRecoveryState();
    public int MaxBarrier => BarrierSystem.UsesBarrier(this)
        ? BarrierSystem.Maximum(GrowthData != null && GrowthData.baseBarrier >= 0 ? GrowthData.baseBarrier : BarrierSystem.Initial(kind), Level) : 0;
    public int Barrier
    {
        get { if (!barrierInitialized) ResetBarrier(); return Mathf.Clamp(barrierPoints, 0, MaxBarrier); }
        set { barrierPoints = Mathf.Clamp(value, 0, MaxBarrier); barrierInitialized = true; }
    }
    public void ResetBarrier()
    {
        barrierPoints = HP > 0 ? MaxBarrier : 0; barrierInitialized = true;
        BarrierRecovery = new BarrierRecoveryState();
    }
    public void RestoreBarrier(bool hasSnapshot, int current, BarrierRecoveryState recovery)
    {
        Barrier = HP > 0 ? hasSnapshot ? current : MaxBarrier : 0;
        BarrierRecovery = BarrierSystem.CopyState(recovery);
    }
}
