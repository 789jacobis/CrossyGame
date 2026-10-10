using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using CrossyRoadServer;

[assembly: LambdaSerializer(typeof(
    Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CrossyRoad.AwsBackend;

public sealed class Function
{
    private const string LeaderboardId = "global_high_scores";
    private static readonly RunScoreValidator Validator = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAmazonDynamoDB dynamoDb;
    private readonly string runsTable;
    private readonly string scoresTable;

    public Function()
        : this(
            new AmazonDynamoDBClient(),
            RequireEnvironment("RUNS_TABLE_NAME"),
            RequireEnvironment("SCORES_TABLE_NAME"))
    {
    }

    internal Function(
        IAmazonDynamoDB dynamoDb,
        string runsTable,
        string scoresTable)
    {
        this.dynamoDb = dynamoDb;
        this.runsTable = runsTable;
        this.scoresTable = scoresTable;
    }

    public async Task<ApiResponse> FunctionHandler(
        JsonElement request,
        ILambdaContext context)
    {
        try
        {
            string method = ReadString(request, "requestContext", "http", "method") ?? string.Empty;
            string path = ReadString(request, "rawPath") ?? string.Empty;

            if (method == "GET" && path == "/health")
            {
                return Json(HttpStatusCode.OK, new { status = "ok" });
            }

            string playerId = RequirePlayerId(request);

            return (method, path) switch
            {
                ("POST", "/runs") => await StartRunAsync(playerId),
                ("POST", "/scores") => await SubmitScoreAsync(playerId, request),
                ("GET", "/leaderboard") => await GetLeaderboardAsync(),
                _ => Json(HttpStatusCode.NotFound, new { error = "Route not found." })
            };
        }
        catch (RunScoreValidationException exception)
        {
            context.Logger.LogWarning(exception.Message);
            return Json(HttpStatusCode.BadRequest, new
            {
                error = exception.Reason.ToString(),
                message = exception.Message
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Json(HttpStatusCode.Unauthorized, new { error = exception.Message });
        }
        catch (Exception exception)
        {
            context.Logger.LogError(exception.ToString());
            return Json(HttpStatusCode.InternalServerError, new { error = "Internal server error." });
        }
    }

    private async Task<ApiResponse> StartRunAsync(string playerId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RunRecord run = RunRecord.Start(playerId, now);

        await dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = runsTable,
            Item = new Dictionary<string, AttributeValue>
            {
                ["PlayerId"] = new(playerId),
                ["RunId"] = new(run.RunId),
                ["StartedAt"] = new() { N = run.StartedAtUnixMilliseconds.ToString() },
                ["Consumed"] = new() { BOOL = false },
                ["ExpiresAt"] = new() { N = now.AddHours(6).ToUnixTimeSeconds().ToString() }
            },
            ConditionExpression = "attribute_not_exists(PlayerId)"
        });

        return Json(HttpStatusCode.Created, new { runId = run.RunId });
    }

    private async Task<ApiResponse> SubmitScoreAsync(string playerId, JsonElement request)
    {
        ScoreRequest body = DeserializeBody<ScoreRequest>(request);
        Dictionary<string, AttributeValue> key = new()
        {
            ["PlayerId"] = new(playerId),
            ["RunId"] = new(body.RunId ?? string.Empty)
        };

        GetItemResponse runResponse = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = runsTable,
            Key = key,
            ConsistentRead = true
        });

        if (runResponse.Item.Count == 0)
        {
            throw new RunScoreValidationException(
                RunScoreRejectionReason.InvalidRunId,
                "The RunId is invalid.");
        }

        var run = new RunRecord
        {
            PlayerId = runResponse.Item["PlayerId"].S,
            RunId = runResponse.Item["RunId"].S,
            StartedAtUnixMilliseconds = long.Parse(runResponse.Item["StartedAt"].N),
            Consumed = runResponse.Item["Consumed"].BOOL ?? false
        };

        Validator.Validate(
            run,
            new RunScoreSubmission(playerId, body.RunId ?? string.Empty, body.Score),
            DateTimeOffset.UtcNow);

        await dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = runsTable,
            Key = key,
            UpdateExpression = "SET #consumed = :true",
            ConditionExpression = "#consumed = :false",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#consumed"] = "Consumed"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":true"] = new() { BOOL = true },
                [":false"] = new() { BOOL = false }
            }
        });

        await SaveBestScoreAsync(playerId, body);
        return Json(HttpStatusCode.OK, new { accepted = true });
    }

    private async Task SaveBestScoreAsync(string playerId, ScoreRequest body)
    {
        string displayName = string.IsNullOrWhiteSpace(body.DisplayName)
            ? "Player"
            : body.DisplayName.Trim()[..Math.Min(24, body.DisplayName.Trim().Length)];
        string rank = $"{100_000 - body.Score:D6}#{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():D13}#{playerId}";

        try
        {
            await dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = scoresTable,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["LeaderboardId"] = new(LeaderboardId),
                    ["PlayerId"] = new(playerId),
                    ["Score"] = new() { N = body.Score.ToString() },
                    ["ScoreRank"] = new(rank),
                    ["DisplayName"] = new(displayName)
                },
                ConditionExpression = "attribute_not_exists(Score) OR Score < :score",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":score"] = new() { N = body.Score.ToString() }
                }
            });
        }
        catch (ConditionalCheckFailedException)
        {
            // A lower score is valid but does not replace the player's best.
        }
    }

    private async Task<ApiResponse> GetLeaderboardAsync()
    {
        QueryResponse response = await dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = scoresTable,
            IndexName = "LeaderboardRankIndex",
            KeyConditionExpression = "LeaderboardId = :id",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":id"] = new(LeaderboardId)
            },
            ScanIndexForward = true,
            Limit = 10
        });

        var entries = response.Items.Select((item, index) => new
        {
            rank = index + 1,
            playerId = item["PlayerId"].S,
            displayName = item["DisplayName"].S,
            score = int.Parse(item["Score"].N)
        });

        return Json(HttpStatusCode.OK, new { entries });
    }

    private static T DeserializeBody<T>(JsonElement request)
    {
        string body = ReadString(request, "body")
            ?? throw new InvalidOperationException("A request body is required.");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException("The request body is invalid.");
    }

    private static string RequirePlayerId(JsonElement request)
    {
        string? userId = ReadString(
            request,
            "requestContext",
            "authorizer",
            "iam",
            "cognitoIdentity",
            "identityId") ?? ReadString(
                request,
                "requestContext",
                "authorizer",
                "iam",
                "userId");
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("An authenticated AWS player is required.");
        }

        return userId;
    }

    private static string? ReadString(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static ApiResponse Json(HttpStatusCode status, object body) => new()
    {
        StatusCode = (int)status,
        Headers = new Dictionary<string, string>
        {
            ["content-type"] = "application/json"
        },
        Body = JsonSerializer.Serialize(body, JsonOptions)
    };

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing environment variable: {name}");

    private sealed record ScoreRequest(string? RunId, int Score, string? DisplayName);
}

public sealed class ApiResponse
{
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; init; } = new();

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;
}
