using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode.GeneratedBindings;
using Unity.Services.Core;
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
    private const string LocalHighScoreKey = "HighScore";

    private Task initializationTask;
    private Task<string> activeRunTask;
    private MyModuleBindings cloudCodeModule;
    private string activeRunId = string.Empty;
    private bool isSubmittingRun;

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

    public async Task<bool> BeginRunAsync()
    {
        await InitializeAsync();

        if (!IsReady)
        {
            return false;
        }

        activeRunId = string.Empty;
        activeRunTask = StartRunInternalAsync();
        activeRunId = await activeRunTask;
        return !string.IsNullOrWhiteSpace(activeRunId);
    }

    public async Task<bool> SubmitRunScoreAsync(int score)
    {
        if (score <= 0)
        {
            return false;
        }

        await InitializeAsync();

        if (!IsReady || isSubmittingRun)
        {
            return false;
        }

        isSubmittingRun = true;

        try
        {
            if (activeRunTask != null &&
                string.IsNullOrWhiteSpace(activeRunId))
            {
                activeRunId = await activeRunTask;
            }

            if (string.IsNullOrWhiteSpace(activeRunId))
            {
                Debug.LogWarning(
                    "Score was not submitted because no server-issued RunId is active.",
                    this);
                return false;
            }

            bool accepted = await CloudCodeModule.SubmitRunScore(
                activeRunId,
                score);

            if (accepted)
            {
                Debug.Log(
                    $"Server validated and submitted score: {score}",
                    this);
                activeRunId = string.Empty;
                activeRunTask = null;
            }

            return accepted;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Server rejected or failed to submit the run score.\n" +
                exception.Message,
                this);
            return false;
        }
        finally
        {
            isSubmittingRun = false;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Reset Local Leaderboard Score For Testing")]
    private void ResetLocalLeaderboardScoreForTesting()
    {
        PlayerPrefs.DeleteKey(LocalHighScoreKey);
        PlayerPrefs.Save();

        Debug.Log(
            "Local high score was reset. " +
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

    private MyModuleBindings CloudCodeModule =>
        cloudCodeModule ??= new MyModuleBindings();

    private async Task<string> StartRunInternalAsync()
    {
        try
        {
            string runId = await CloudCodeModule.StartRun();

            Debug.Log(
                $"Server run started. RunId: {runId}",
                this);
            return runId;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Unable to start a server-validated run.\n" +
                exception.Message,
                this);
            return string.Empty;
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

}
