using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class AwsGameBackend : IGameBackend
{
    private const string Region = "ap-northeast-1";
    private const string IdentityPoolId =
        "ap-northeast-1:85db1486-a83c-4371-9615-3fd568d17b8b";
    private const string ApiBaseUrl =
        "https://fpvx62ygkk.execute-api.ap-northeast-1.amazonaws.com";

    private AwsCredentials credentials;

    public string PlayerId { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrEmpty(PlayerId) && CredentialsAreValid())
        {
            return;
        }

        var getIdRequest = new GetIdRequest
        {
            IdentityPoolId = IdentityPoolId
        };
        GetIdResponse idResponse = await SendCognitoAsync<GetIdResponse>(
            "AWSCognitoIdentityService.GetId",
            JsonUtility.ToJson(getIdRequest));

        if (string.IsNullOrWhiteSpace(idResponse.IdentityId))
        {
            throw new InvalidOperationException(
                "AWS Cognito did not return a player identity.");
        }

        PlayerId = idResponse.IdentityId;
        var credentialsRequest = new GetCredentialsRequest
        {
            IdentityId = PlayerId
        };
        GetCredentialsResponse credentialsResponse =
            await SendCognitoAsync<GetCredentialsResponse>(
                "AWSCognitoIdentityService.GetCredentialsForIdentity",
                JsonUtility.ToJson(credentialsRequest));

        credentials = credentialsResponse.Credentials;
        if (credentials == null ||
            string.IsNullOrWhiteSpace(credentials.AccessKeyId))
        {
            throw new InvalidOperationException(
                "AWS Cognito did not return temporary credentials.");
        }
    }

    public async Task<string> StartRunAsync()
    {
        RunResponse response = await SendApiAsync<RunResponse>(
            UnityWebRequest.kHttpVerbPOST,
            "/runs",
            "{}");
        return response.runId;
    }

    public async Task<bool> SubmitRunScoreAsync(
        string runId,
        int score,
        string displayName)
    {
        var request = new ScoreRequest
        {
            runId = runId,
            score = score,
            displayName = displayName
        };
        ScoreResponse response = await SendApiAsync<ScoreResponse>(
            UnityWebRequest.kHttpVerbPOST,
            "/scores",
            JsonUtility.ToJson(request));
        return response.accepted;
    }

    public async Task<IReadOnlyList<GameLeaderboardEntry>>
        GetLeaderboardAsync(int limit)
    {
        LeaderboardResponse response =
            await SendApiAsync<LeaderboardResponse>(
                UnityWebRequest.kHttpVerbGET,
                "/leaderboard",
                string.Empty);

        if (response.entries == null)
        {
            return Array.Empty<GameLeaderboardEntry>();
        }

        int count = Math.Min(Math.Max(limit, 0), response.entries.Length);
        var entries = new List<GameLeaderboardEntry>(count);
        for (int index = 0; index < count; index++)
        {
            entries.Add(response.entries[index]);
        }

        return entries;
    }

    private async Task<T> SendApiAsync<T>(
        string method,
        string path,
        string body)
    {
        await EnsureCredentialsAsync();

        string url = ApiBaseUrl + path;
        string payload = body ?? string.Empty;
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        string payloadHash = Hex(Sha256(payloadBytes));
        DateTime utcNow = DateTime.UtcNow;
        string amzDate = utcNow.ToString(
            "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture);
        string dateStamp = utcNow.ToString(
            "yyyyMMdd",
            CultureInfo.InvariantCulture);
        var uri = new Uri(url);

        string canonicalHeaders =
            $"host:{uri.Host}\n" +
            $"x-amz-content-sha256:{payloadHash}\n" +
            $"x-amz-date:{amzDate}\n" +
            $"x-amz-security-token:{credentials.SessionToken}\n";
        const string signedHeaders =
            "host;x-amz-content-sha256;x-amz-date;x-amz-security-token";
        string canonicalRequest =
            $"{method}\n{uri.AbsolutePath}\n{uri.Query.TrimStart('?')}\n" +
            $"{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        string scope =
            $"{dateStamp}/{Region}/execute-api/aws4_request";
        string stringToSign =
            $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n" +
            Hex(Sha256(Encoding.UTF8.GetBytes(canonicalRequest)));
        byte[] signingKey = GetSignatureKey(
            credentials.SecretKey,
            dateStamp,
            Region,
            "execute-api");
        string signature = Hex(Hmac(signingKey, stringToSign));
        string authorization =
            $"AWS4-HMAC-SHA256 Credential={credentials.AccessKeyId}/{scope}, " +
            $"SignedHeaders={signedHeaders}, Signature={signature}";

        using var request = new UnityWebRequest(url, method);
        if (payloadBytes.Length > 0)
        {
            request.uploadHandler = new UploadHandlerRaw(payloadBytes);
        }
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("content-type", "application/json");
        request.SetRequestHeader("x-amz-content-sha256", payloadHash);
        request.SetRequestHeader("x-amz-date", amzDate);
        request.SetRequestHeader(
            "x-amz-security-token",
            credentials.SessionToken);
        request.SetRequestHeader("authorization", authorization);

        await SendWebRequestAsync(request);
        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException(
                $"AWS API request failed ({request.responseCode}): " +
                request.downloadHandler.text);
        }

        return JsonUtility.FromJson<T>(request.downloadHandler.text);
    }

    private async Task EnsureCredentialsAsync()
    {
        if (CredentialsAreValid())
        {
            return;
        }

        credentials = null;
        await InitializeAsync();
    }

    private bool CredentialsAreValid()
    {
        if (credentials == null ||
            string.IsNullOrWhiteSpace(credentials.AccessKeyId))
        {
            return false;
        }

        if (credentials.Expiration <= 0)
        {
            return true;
        }

        DateTimeOffset expiresAt = DateTimeOffset.FromUnixTimeSeconds(
            (long)credentials.Expiration);
        return expiresAt > DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private static async Task<T> SendCognitoAsync<T>(
        string target,
        string json)
    {
        string url = $"https://cognito-identity.{Region}.amazonaws.com/";
        using var request = new UnityWebRequest(
            url,
            UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(
            Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader(
            "content-type",
            "application/x-amz-json-1.1");
        request.SetRequestHeader("x-amz-target", target);

        await SendWebRequestAsync(request);
        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException(
                $"AWS Cognito request failed ({request.responseCode}): " +
                request.downloadHandler.text);
        }

        return JsonUtility.FromJson<T>(request.downloadHandler.text);
    }

    private static Task SendWebRequestAsync(UnityWebRequest request)
    {
        var completion = new TaskCompletionSource<bool>();
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        operation.completed += _ => completion.TrySetResult(true);

        if (operation.isDone)
        {
            completion.TrySetResult(true);
        }

        return completion.Task;
    }

    private static byte[] GetSignatureKey(
        string secret,
        string date,
        string region,
        string service)
    {
        byte[] dateKey = Hmac(
            Encoding.UTF8.GetBytes("AWS4" + secret),
            date);
        byte[] regionKey = Hmac(dateKey, region);
        byte[] serviceKey = Hmac(regionKey, service);
        return Hmac(serviceKey, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string value)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static byte[] Sha256(byte[] value)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(value);
    }

    private static string Hex(byte[] value)
    {
        var builder = new StringBuilder(value.Length * 2);
        foreach (byte item in value)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    [Serializable]
    private sealed class GetIdRequest
    {
        public string IdentityPoolId;
    }

    [Serializable]
    private sealed class GetIdResponse
    {
        public string IdentityId;
    }

    [Serializable]
    private sealed class GetCredentialsRequest
    {
        public string IdentityId;
    }

    [Serializable]
    private sealed class GetCredentialsResponse
    {
        public string IdentityId;
        public AwsCredentials Credentials;
    }

    [Serializable]
    private sealed class AwsCredentials
    {
        public string AccessKeyId;
        public string SecretKey;
        public string SessionToken;
        public double Expiration;
    }

    [Serializable]
    private sealed class RunResponse
    {
        public string runId;
    }

    [Serializable]
    private sealed class ScoreRequest
    {
        public string runId;
        public int score;
        public string displayName;
    }

    [Serializable]
    private sealed class ScoreResponse
    {
        public bool accepted;
    }

    [Serializable]
    private sealed class LeaderboardResponse
    {
        public GameLeaderboardEntry[] entries;
    }
}
