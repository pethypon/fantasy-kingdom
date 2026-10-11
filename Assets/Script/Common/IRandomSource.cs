/// <summary>Turn-seeded gameplay randomness. Implementations must version any sequence changes.</summary>
public interface IRandomSource
{
    int Seed { get; }
    void SetTurnSeed(int turn);
    int Next();
    float NextFloat();
    int Range(int min, int max);
    float Range(float min, float max);
}
