using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;
using Unity.Services.Leaderboards.Model;

namespace CrossyRoadServer;

public class MyModule
{
    private const string LeaderboardId = "global_high_scores";
    private const string ActiveRunKey = "crossy_active_run";
    private const int MaximumAcceptedScore = 100_000;
    private const double MaximumScorePerSecond = 12d;
    private const double TimingGraceSeconds = 2d;
    private static readonly TimeSpan MaximumRunAge = TimeSpan.FromHours(6);

    private readonly IGameApiClient gameApiClient;
    private readonly ILogger<MyModule> logger;

    public MyModule(IGameApiClient gameApiClient, ILogger<MyModule> logger)
    {
        this.gameApiClient = gameApiClient;
        this.logger = logger;
    }

    [CloudCodeFunction("SayHello")]
    public string SayHello(string name)
    {
        return $"Hello, {name}!";
    }

    [CloudCodeFunction("StartRun")]
    public async Task<string> StartRun(IExecutionContext context)
    {
        string playerId = RequirePlayerId(context);
        var run = new RunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            StartedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Consumed = false
        };

        await SaveRunAsync(context, playerId, run);
        logger.LogInformation(
            "Started run {RunId} for player {PlayerId}.",
            run.RunId,
            playerId);

        return run.RunId;
    }

    [CloudCodeFunction("SubmitRunScore")]
    public async Task<bool> SubmitRunScore(
        IExecutionContext context,
        string runId,
        int score)
    {
        string playerId = RequirePlayerId(context);
        ValidateScoreRange(score);

        RunRecord run = await LoadRunAsync(context, playerId);
        ValidateRun(run, runId, score);

        await gameApiClient.Leaderboards.AddLeaderboardPlayerScoreAsync(
            context,
            context.ServiceToken,
            Guid.Parse(context.ProjectId),
            LeaderboardId,
            playerId,
            new AddLeaderboardScore(score));

        run.Consumed = true;
        await SaveRunAsync(context, playerId, run);

        logger.LogInformation(
            "Accepted score {Score} for player {PlayerId}, run {RunId}.",
            score,
            playerId,
            run.RunId);

        return true;
    }

    private async Task SaveRunAsync(
        IExecutionContext context,
        string playerId,
        RunRecord run)
    {
        await gameApiClient.CloudSaveData.SetProtectedItemAsync(
            context,
            context.ServiceToken,
            context.ProjectId,
            playerId,
            new SetItemBody(ActiveRunKey, JsonSerializer.Serialize(run)));
    }

    private async Task<RunRecord> LoadRunAsync(
        IExecutionContext context,
        string playerId)
    {
        var response = await gameApiClient.CloudSaveData.GetProtectedItemsAsync(
            context,
            context.AccessToken,
            context.ProjectId,
            playerId,
            new List<string> { ActiveRunKey });

        string? serializedRun = response.Data.Results
            .FirstOrDefault()
            ?.Value
            ?.ToString();

        if (string.IsNullOrWhiteSpace(serializedRun))
        {
            throw new InvalidOperationException(
                "No active run exists for this player.");
        }

        return JsonSerializer.Deserialize<RunRecord>(serializedRun)
            ?? throw new InvalidOperationException(
                "The active run data is invalid.");
    }

    private static string RequirePlayerId(IExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.PlayerId))
        {
            throw new InvalidOperationException(
                "An authenticated player is required.");
        }

        return context.PlayerId;
    }

    private static void ValidateScoreRange(int score)
    {
        if (score <= 0 || score > MaximumAcceptedScore)
        {
            throw new ArgumentOutOfRangeException(
                nameof(score),
                $"Score must be between 1 and {MaximumAcceptedScore}.");
        }
    }

    private static void ValidateRun(
        RunRecord run,
        string submittedRunId,
        int score)
    {
        if (string.IsNullOrWhiteSpace(submittedRunId) ||
            !string.Equals(
                run.RunId,
                submittedRunId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The RunId is invalid.");
        }

        if (run.Consumed)
        {
            throw new InvalidOperationException(
                "This run has already been submitted.");
        }

        DateTimeOffset startedAt =
            DateTimeOffset.FromUnixTimeMilliseconds(
                run.StartedAtUnixMilliseconds);
        TimeSpan elapsed = DateTimeOffset.UtcNow - startedAt;

        if (elapsed < TimeSpan.Zero || elapsed > MaximumRunAge)
        {
            throw new InvalidOperationException(
                "This run is outside the accepted time window.");
        }

        double minimumSeconds = Math.Max(
            0d,
            score / MaximumScorePerSecond - TimingGraceSeconds);

        if (elapsed.TotalSeconds < minimumSeconds)
        {
            throw new InvalidOperationException(
                "The submitted score increased faster than gameplay allows.");
        }
    }

    private sealed class RunRecord
    {
        public string RunId { get; set; } = string.Empty;
        public long StartedAtUnixMilliseconds { get; set; }
        public bool Consumed { get; set; }
    }
}
