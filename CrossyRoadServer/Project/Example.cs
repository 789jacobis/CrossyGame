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
    private static readonly RunScoreValidator ScoreValidator = new();

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
        RunRecord run = RunRecord.Start(playerId, DateTimeOffset.UtcNow);

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

        RunRecord run = await LoadRunAsync(context, playerId);
        ScoreValidator.Validate(
            run,
            new RunScoreSubmission(playerId, runId, score),
            DateTimeOffset.UtcNow);

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

        RunRecord run = JsonSerializer.Deserialize<RunRecord>(serializedRun)
            ?? throw new InvalidOperationException(
                "The active run data is invalid.");

        // Legacy records are already isolated by the player's protected
        // Cloud Save partition, so they can safely adopt that partition owner.
        if (string.IsNullOrWhiteSpace(run.PlayerId))
        {
            run.PlayerId = playerId;
        }

        return run;
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

}
