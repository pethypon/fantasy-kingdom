using UnityEngine;

// One choice happens after owner-start effects and before either human or AI can spend AP.
public sealed class CampaignRewardTurn : TurnState
{
    private readonly Team owner;
    private GameObject panel;
    private CampaignRewards Rewards => Systems.FactionState.Rewards;

    private CampaignRewardTurn(TurnGenerator turn, Team owner) : base(turn) { this.owner = owner; }

    public static void BeginActions(TurnGenerator turn, Team team)
    {
        if (turn.IsGameOver) return;
        var systems = turn.Systems;
        var rewards = systems.FactionState?.Rewards;
        if (rewards != null)
        {
            var map = systems.MapCreate;
            int seed = unchecked(map.SeedX.GetHashCode() * 397 ^ map.SeedZ.GetHashCode());
            if (rewards.Prepare(team, turn.Context.Turn, seed))
            {
                turn.ChangeState(new CampaignRewardTurn(turn, team));
                return;
            }
        }
        EnterActionPhase(turn, team);
    }

    static void EnterActionPhase(TurnGenerator turn, Team team)
    {
        if (team == Team.Player) turn.ChangeState(new PlayerMove(turn));
        else turn.ChangeState(new EnemyMove(turn));
    }

    public override void Entry()
    {
        if (owner == Team.Enemy || Turn.DeveloperPlayerAIEnabled) { ChooseForAI(); return; }
        Systems.InputHintUI?.SetHints(string.Empty);
        panel = CampaignRewardPanel.Show(Rewards, Context.Turn, Claim);
    }
    public override void Update()
    {
        if (Turn.DeveloperPlayerAIEnabled) ChooseForAI();
    }
    void ChooseForAI() => Claim(Rewards.Choose(Systems, owner));
    private void Claim(int index)
    {
        if (Turn.CurrentState != this || !Rewards.Claim(Systems.FactionState, owner, Context.Turn, index)) return;
        Context.ConsumeUICommand();
        EnterActionPhase(Turn, owner);
    }

    public override void Exit()
    {
        if (panel != null) { panel.SetActive(false); Object.Destroy(panel); }
    }
}
