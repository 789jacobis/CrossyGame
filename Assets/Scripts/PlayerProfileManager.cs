using System;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerProfileManager : MonoBehaviour
{
    private const string DisplayNameKey = "player_profile_display_name";
    private const string HasChosenNameKey = "player_profile_has_chosen_name";
    private const string PendingNameKey = "player_profile_pending_name";
    private const string DefaultNumberKey = "player_profile_default_number";

    private const int MinimumNameLength = 1;
    private const int MaximumNameLength = 12;

    private Task initializationTask;

    public static PlayerProfileManager Instance { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;
    public string DefaultDisplayName { get; private set; } = string.Empty;
    public bool HasChosenName { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool IsSyncing { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    public event Action ProfileChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        DefaultDisplayName = LoadOrCreateDefaultName();
        DisplayName = PlayerPrefs.GetString(DisplayNameKey, DefaultDisplayName);
        HasChosenName = PlayerPrefs.GetInt(HasChosenNameKey, 0) == 1;
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

    public bool TryValidateName(
        string rawName,
        out string cleanedName,
        out string error)
    {
        cleanedName = (rawName ?? string.Empty).Trim();
        error = string.Empty;

        int characterCount =
            new StringInfo(cleanedName).LengthInTextElements;

        if (characterCount < MinimumNameLength ||
            characterCount > MaximumNameLength)
        {
            error = $"名稱必須是 {MinimumNameLength} 到 " +
                    $"{MaximumNameLength} 個字。";
            return false;
        }

        foreach (char character in cleanedName)
        {
            if (!char.IsLetterOrDigit(character) &&
                character != '_' &&
                character != '-')
            {
                error = "名稱只能使用中英文字母、數字、底線或連字號，" +
                        "不能包含空格。";
                return false;
            }
        }

        return true;
    }

    public async Task<bool> SetPlayerNameAsync(string rawName)
    {
        if (!TryValidateName(
                rawName,
                out string cleanedName,
                out string validationError))
        {
            LastError = validationError;
            ProfileChanged?.Invoke();
            return false;
        }

        SaveLocalName(cleanedName);
        await TrySyncPendingNameAsync();
        return true;
    }

    public Task<bool> UseDefaultNameAsync()
    {
        return SetPlayerNameAsync(DefaultDisplayName);
    }

    public async Task RetryPendingSyncAsync()
    {
        await InitializeAsync();
        await TrySyncPendingNameAsync();
    }

#if UNITY_EDITOR
    [ContextMenu("Reset Player Name For Testing")]
    private void ResetPlayerNameForTesting()
    {
        PlayerPrefs.DeleteKey(DisplayNameKey);
        PlayerPrefs.DeleteKey(HasChosenNameKey);
        PlayerPrefs.DeleteKey(PendingNameKey);
        PlayerPrefs.Save();

        DefaultDisplayName = LoadOrCreateDefaultName();
        DisplayName = DefaultDisplayName;
        HasChosenName = false;
        LastError = string.Empty;

        ProfileChanged?.Invoke();
        Debug.Log(
            "Player name reset for testing. Scores and the default ID were preserved.",
            this);
    }
#endif

    private async Task InitializeInternalAsync()
    {
        OnlineServicesManager onlineServices =
            OnlineServicesManager.Instance ??
            FindFirstObjectByType<OnlineServicesManager>();

        if (onlineServices != null)
        {
            await onlineServices.InitializeAsync();
        }

        IsInitialized = true;
        await TrySyncPendingNameAsync();

        Debug.Log(
            $"Player profile ready. Display name: {DisplayName}",
            this);
        ProfileChanged?.Invoke();
    }

    private async Task TrySyncPendingNameAsync()
    {
        string pendingName =
            PlayerPrefs.GetString(PendingNameKey, string.Empty);

        if (string.IsNullOrEmpty(pendingName))
        {
            return;
        }

        OnlineServicesManager onlineServices =
            OnlineServicesManager.Instance;

        if (onlineServices == null || !onlineServices.IsReady)
        {
            LastError =
                "目前離線，玩家名稱會在下次連線時自動同步。";
            ProfileChanged?.Invoke();
            return;
        }

        IsSyncing = true;
        LastError = string.Empty;
        ProfileChanged?.Invoke();

        try
        {
            await AuthenticationService.Instance
                .UpdatePlayerNameAsync(pendingName);

            PlayerPrefs.DeleteKey(PendingNameKey);
            PlayerPrefs.Save();
            LastError = string.Empty;
        }
        catch (Exception exception)
        {
            LastError =
                "玩家名稱尚未同步，稍後會再嘗試。" +
                $"\n{exception.Message}";
            Debug.LogWarning(LastError, this);
        }
        finally
        {
            IsSyncing = false;
            ProfileChanged?.Invoke();
        }
    }

    private void SaveLocalName(string displayName)
    {
        DisplayName = displayName;
        HasChosenName = true;
        LastError = string.Empty;

        PlayerPrefs.SetString(DisplayNameKey, DisplayName);
        PlayerPrefs.SetInt(HasChosenNameKey, 1);
        PlayerPrefs.SetString(PendingNameKey, DisplayName);
        PlayerPrefs.Save();

        ProfileChanged?.Invoke();
    }

    private static string LoadOrCreateDefaultName()
    {
        int number = PlayerPrefs.GetInt(DefaultNumberKey, 0);

        if (number == 0)
        {
            number = UnityEngine.Random.Range(10000, 100000);
            PlayerPrefs.SetInt(DefaultNumberKey, number);
            PlayerPrefs.Save();
        }

        return $"Player-{number}";
    }
}
