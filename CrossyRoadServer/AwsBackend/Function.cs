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
    private readonly string namesTable;

    public Function()
        : this(
            new AmazonDynamoDBClient(),
            RequireEnvironment("RUNS_TABLE_NAME"),
            RequireEnvironment("SCORES_TABLE_NAME"),
            RequireEnvironment("NAMES_TABLE_NAME"))
    {
    }

    internal Function(
        IAmazonDynamoDB dynamoDb,
        string runsTable,
        string scoresTable,
        string namesTable)
    {
        this.dynamoDb = dynamoDb;
        this.runsTable = runsTable;
        this.scoresTable = scoresTable;
        this.namesTable = namesTable;
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
                ("PUT", "/profile/name") => await ClaimNameAsync(playerId, request),
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
        catch (PlayerNameRequiredException)
        {
            return Json(HttpStatusCode.Conflict, new
            {
                error = "NameRequired",
                message = "Choose an available player name before submitting a score."
            });
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
        string displayName = await GetRegisteredNameAsync(playerId);
        if (string.IsNullOrEmpty(displayName))
        {
            // Older WebGL builds do not call /profile/name. Allow them to
            // register their name through the same atomic reservation path.
            ApiResponse claim = await ClaimNameAsync(playerId, request);
            if (claim.StatusCode != (int)HttpStatusCode.OK ||
                !PlayerNamePolicy.TryNormalize(
                    body.DisplayName, out displayName, out _))
            {
                throw new PlayerNameRequiredException();
            }
        }
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

        await SaveBestScoreAsync(playerId, body.Score, displayName);
        return Json(HttpStatusCode.OK, new { accepted = true });
    }

    private async Task SaveBestScoreAsync(
        string playerId,
        int score,
        string displayName)
    {
        string rank = $"{100_000 - score:D6}#{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():D13}#{playerId}";

        try
        {
            await dynamoDb.PutItemAsync(new PutItemRequest
            {
                TableName = scoresTable,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["LeaderboardId"] = new(LeaderboardId),
                    ["PlayerId"] = new(playerId),
                    ["Score"] = new() { N = score.ToString() },
                    ["ScoreRank"] = new(rank),
                    ["DisplayName"] = new(displayName)
                },
                ConditionExpression = "attribute_not_exists(Score) OR Score < :score",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":score"] = new() { N = score.ToString() }
                }
            });
        }
        catch (ConditionalCheckFailedException)
        {
            // A lower score is valid but does not replace the player's best.
        }
    }

    private async Task<ApiResponse> ClaimNameAsync(
        string playerId,
        JsonElement request)
    {
        NameRequest body = DeserializeBody<NameRequest>(request);
        if (!PlayerNamePolicy.TryNormalize(
                body.DisplayName, out string displayName, out string nameKey))
        {
            return Json(HttpStatusCode.BadRequest, new
            {
                error = "InvalidName",
                message = "Name must contain 1–12 letters, digits, _ or -."
            });
        }

        string profileRecordKey = $"PLAYER#{playerId}";
        string reservationKey = $"NAME#{nameKey}";
        GetItemResponse profile = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = namesTable,
            Key = NameRecordKey(profileRecordKey),
            ConsistentRead = true
        });
        string? previousNameKey = profile.Item != null && profile.Item.TryGetValue(
            "NameKey", out AttributeValue? previousName)
            ? previousName.S : null;

        if (profile.Item != null && previousNameKey == nameKey)
        {
            return Json(HttpStatusCode.OK, new
            {
                displayName = profile.Item["DisplayName"].S
            });
        }

        var changes = new List<TransactWriteItem>
        {
            new()
            {
                Put = new Put
                {
                    TableName = namesTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["RecordKey"] = new(reservationKey),
                        ["OwnerPlayerId"] = new(playerId)
                    },
                    ConditionExpression = "attribute_not_exists(RecordKey)"
                }
            }
        };

        if (previousNameKey is null)
        {
            changes.Add(new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = namesTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["RecordKey"] = new(profileRecordKey),
                        ["NameKey"] = new(nameKey),
                        ["DisplayName"] = new(displayName)
                    },
                    ConditionExpression = "attribute_not_exists(RecordKey)"
                }
            });
        }
        else
        {
            changes.Add(new TransactWriteItem
            {
                Update = new Update
                {
                    TableName = namesTable,
                    Key = NameRecordKey(profileRecordKey),
                    UpdateExpression = "SET NameKey = :new, DisplayName = :display",
                    ConditionExpression = "NameKey = :old",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":new"] = new(nameKey),
                        [":old"] = new(previousNameKey),
                        [":display"] = new(displayName)
                    }
                }
            });
            changes.Add(new TransactWriteItem
            {
                Delete = new Delete
                {
                    TableName = namesTable,
                    Key = NameRecordKey($"NAME#{previousNameKey}"),
                    ConditionExpression = "OwnerPlayerId = :owner",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":owner"] = new(playerId)
                    }
                }
            });
        }

        GetItemResponse score = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = scoresTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["LeaderboardId"] = new(LeaderboardId),
                ["PlayerId"] = new(playerId)
            },
            ConsistentRead = true
        });
        if (score.Item?.Count > 0)
        {
            changes.Add(new TransactWriteItem
            {
                Update = new Update
                {
                    TableName = scoresTable,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["LeaderboardId"] = new(LeaderboardId),
                        ["PlayerId"] = new(playerId)
                    },
                    UpdateExpression = "SET DisplayName = :display",
                    ConditionExpression = "attribute_exists(PlayerId)",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":display"] = new(displayName)
                    }
                }
            });
        }

        try
        {
            await dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
            {
                TransactItems = changes
            });
        }
        catch (TransactionCanceledException exception) when (
            exception.CancellationReasons?.Any(reason =>
                reason.Code == "ConditionalCheckFailed") == true)
        {
            return Json(HttpStatusCode.Conflict, new
            {
                error = "NameUnavailable",
                message = "This name is already in use or the profile changed."
            });
        }

        return Json(HttpStatusCode.OK, new { displayName });
    }

    private async Task<string> GetRegisteredNameAsync(string playerId)
    {
        GetItemResponse response = await dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = namesTable,
            Key = NameRecordKey($"PLAYER#{playerId}"),
            ConsistentRead = true
        });
        if (response.Item == null || !response.Item.TryGetValue(
                "DisplayName", out AttributeValue? displayName) ||
            string.IsNullOrWhiteSpace(displayName.S))
        {
            return string.Empty;
        }

        return displayName.S;
    }

    private static Dictionary<string, AttributeValue> NameRecordKey(string key) =>
        new() { ["RecordKey"] = new(key) };

    private async Task<ApiResponse> GetLeaderboardAsync()
    {
        var entries = new List<LeaderboardEntryResponse>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, AttributeValue>? nextKey = null;

        do
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
                Limit = 25,
                ExclusiveStartKey = nextKey
            });

            foreach (Dictionary<string, AttributeValue> item in response.Items)
            {
                string displayName = item["DisplayName"].S;
                string normalizedName = displayName.Trim()
                    .Normalize(System.Text.NormalizationForm.FormKC)
                    .ToUpperInvariant();
                if (!seenNames.Add(normalizedName))
                {
                    continue;
                }

                entries.Add(new LeaderboardEntryResponse(
                    entries.Count + 1,
                    item["PlayerId"].S,
                    displayName,
                    int.Parse(item["Score"].N)));
                if (entries.Count == 10)
                {
                    break;
                }
            }

            nextKey = response.LastEvaluatedKey;
        }
        while (entries.Count < 10 && nextKey?.Count > 0);

        return Json(HttpStatusCode.OK, new { entries });
    }

    private sealed record LeaderboardEntryResponse(
        int Rank,
        string PlayerId,
        string DisplayName,
        int Score);

    private sealed class PlayerNameRequiredException : Exception
    {
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

    private sealed record NameRequest(string? DisplayName);
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
