using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using UnityEngine;

public enum OnlineServicesState
{
    NotInitialized,
    Initializing,
    Ready,
    Offline
}

[DisallowMultipleComponent]
public sealed class OnlineServicesManager : MonoBehaviour
{
    private const string LeaderboardId = "global_high_scores";
    private const string LocalHighScoreKey = "HighScore";
    private const string PendingScoreKey =
        "leaderboard_pending_best_score";

    private Task initializationTask;
    private bool isSubmittingScore;

    public static OnlineServicesManager Instance { get; private set; }

    public OnlineServicesState State { get; private set; } =
        OnlineServicesState.NotInitialized;

    public bool IsReady => State == OnlineServicesState.Ready;

    public string PlayerId =>
        AuthenticationService.Instance.IsSignedIn
            ? AuthenticationService.Instance.PlayerId
            : string.Empty;

    public string LastError { get; private set; } = string.Empty;

    public event Action<OnlineServicesState> StateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        await InitializeAsync();
    }

    public Task InitializeAsync()
    {
        initializationTask ??= InitializeInternalAsync();
        return initializationTask;
    }

    public async Task<bool> SubmitBestScoreAsync(int score)
    {
        if (score <= 0)
        {
            return false;
        }

        SavePendingScore(score);
        await InitializeAsync();

        if (!IsReady)
        {
            return false;
        }

        return await TrySubmitPendingScoreAsync();
    }

#if UNITY_EDITOR
    [ContextMenu("Reset Local Leaderboard Score For Testing")]
    private void ResetLocalLeaderboardScoreForTesting()
    {
        PlayerPrefs.DeleteKey(LocalHighScoreKey);
        PlayerPrefs.DeleteKey(PendingScoreKey);
        PlayerPrefs.Save();

        Debug.Log(
            "Local high score and pending leaderboard score were reset. " +
            "The online leaderboard entry was not deleted.",
            this);
    }
#endif

    private async Task InitializeInternalAsync()
    {
        SetState(OnlineServicesState.Initializing);

        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            LastError = string.Empty;
            SetState(OnlineServicesState.Ready);
            Debug.Log(
                $"Unity Services ready. Player ID: {PlayerId}",
                this);

            SavePendingScore(
                PlayerPrefs.GetInt(LocalHighScoreKey, 0));
            await TrySubmitPendingScoreAsync();
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            SetState(OnlineServicesState.Offline);
            Debug.LogWarning(
                $"Unity Services unavailable. The game will continue offline.\n" +
                exception.Message,
                this);
        }
    }

    private void SetState(OnlineServicesState newState)
    {
        if (State == newState)
        {
            return;
        }

        State = newState;
        StateChanged?.Invoke(State);
    }

    private static void SavePendingScore(int score)
    {
        int pendingScore =
            PlayerPrefs.GetInt(PendingScoreKey, 0);

        if (score <= pendingScore)
        {
            return;
        }

        PlayerPrefs.SetInt(PendingScoreKey, score);
        PlayerPrefs.Save();
    }

    private async Task<bool> TrySubmitPendingScoreAsync()
    {
        int pendingScore =
            PlayerPrefs.GetInt(PendingScoreKey, 0);

        if (pendingScore <= 0)
        {
            return true;
        }

        if (isSubmittingScore || !IsReady)
        {
            return false;
        }

        isSubmittingScore = true;

        try
        {
            await LeaderboardsService.Instance.AddPlayerScoreAsync(
                LeaderboardId,
                pendingScore);

            int latestPendingScore =
                PlayerPrefs.GetInt(PendingScoreKey, 0);

            if (latestPendingScore <= pendingScore)
            {
                PlayerPrefs.DeleteKey(PendingScoreKey);
                PlayerPrefs.Save();
            }

            Debug.Log(
                $"Leaderboard score submitted: {pendingScore}",
                this);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Leaderboard score upload failed. " +
                $"It will be retried later.\n{exception.Message}",
                this);
            return false;
        }
        finally
        {
            isSubmittingScore = false;
        }
    }
}
