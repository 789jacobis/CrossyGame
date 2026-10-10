using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IGameBackend
{
    string PlayerId { get; }

    Task InitializeAsync();
    Task<string> StartRunAsync();
    Task<bool> SubmitRunScoreAsync(
        string runId,
        int score,
        string displayName);
    Task<IReadOnlyList<GameLeaderboardEntry>>
        GetLeaderboardAsync(int limit);
}
[Serializable]
public sealed class GameLeaderboardEntry
{
    public int rank;
    public string playerId;
    public string displayName;
    public int score;
}
