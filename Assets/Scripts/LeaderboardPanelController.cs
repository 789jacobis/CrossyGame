using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TMPro;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LeaderboardPanelController : MonoBehaviour
{
    private const string LeaderboardId = "global_high_scores";

    [Header("Panels and Buttons")]
    [SerializeField] private GameObject leaderboardPanel;
    [SerializeField] private Button startButton;
    [SerializeField] private Button leaderboardButton;
    [SerializeField] private Button soundButton;
    [SerializeField] private Button backButton;

    [Header("Leaderboard Content")]
    [SerializeField] private RectTransform entryRowTemplate;
    [SerializeField] private TMP_Text statusText;
    [SerializeField, Min(1)] private int maximumEntries = 10;
    [SerializeField, Min(1)]
    private int topEntriesWhenShowingCurrentPlayer = 8;
    [SerializeField, Min(1f)] private float rowSpacing = 50f;

    [Header("Current Player")]
    [SerializeField] private Color currentPlayerRowColor =
        new Color(1f, 0.72f, 0.15f, 0.24f);

    private static LeaderboardPanelController activeInstance;

    private readonly List<GameObject> spawnedRows = new();
    private bool isOpen;
    private bool isLoading;
    private int loadVersion;
    private int openedFrame = -1;

    public static bool BlocksGameStart =>
        activeInstance != null && activeInstance.isOpen;

    public static bool ManagesStartMenuInput =>
        activeInstance != null &&
        !activeInstance.isOpen &&
        activeInstance.startButton != null &&
        activeInstance.leaderboardButton != null &&
        activeInstance.soundButton != null &&
        activeInstance.startButton.gameObject.activeInHierarchy &&
        activeInstance.leaderboardButton.gameObject.activeInHierarchy &&
        activeInstance.soundButton.gameObject.activeInHierarchy;

    private void Awake()
    {
        activeInstance = this;

        leaderboardButton?.onClick.AddListener(OpenLeaderboard);
        backButton?.onClick.AddListener(CloseLeaderboard);

        ConfigureButton(startButton);
        ConfigureButton(leaderboardButton);
        ConfigureButton(soundButton);
        ConfigureButton(backButton);

        if (entryRowTemplate != null)
        {
            entryRowTemplate.gameObject.SetActive(false);
        }

        if (leaderboardPanel != null)
        {
            leaderboardPanel.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
        {
            activeInstance = null;
        }

        leaderboardButton?.onClick.RemoveListener(OpenLeaderboard);
        backButton?.onClick.RemoveListener(CloseLeaderboard);
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (isOpen)
        {
            HandleLeaderboardInput();
            return;
        }

        if (ManagesStartMenuInput &&
            !NameEntryPanelController.BlocksGameStart &&
            !SoundSettingsPanelController.BlocksGameStart)
        {
            HandleStartMenuInput();
        }
    }

    private void HandleStartMenuInput()
    {
        if (Keyboard.current.upArrowKey.wasPressedThisFrame ||
            Keyboard.current.wKey.wasPressedThisFrame)
        {
            MoveStartMenuSelection(-1);
            return;
        }

        if (Keyboard.current.downArrowKey.wasPressedThisFrame ||
            Keyboard.current.sKey.wasPressedThisFrame)
        {
            MoveStartMenuSelection(1);
            return;
        }

        if (!WasSubmitPressed())
        {
            return;
        }

        GameObject selected =
            EventSystem.current?.currentSelectedGameObject;

        if (selected == leaderboardButton.gameObject)
        {
            OpenLeaderboard();
        }
        else if (selected == soundButton.gameObject)
        {
            soundButton.onClick.Invoke();
        }
        else
        {
            startButton.onClick.Invoke();
        }
    }

    private void MoveStartMenuSelection(int direction)
    {
        Button[] buttons =
        {
            startButton,
            leaderboardButton,
            soundButton
        };

        GameObject selected =
            EventSystem.current?.currentSelectedGameObject;
        int currentIndex = 0;

        for (int index = 0; index < buttons.Length; index++)
        {
            if (buttons[index] != null &&
                buttons[index].gameObject == selected)
            {
                currentIndex = index;
                break;
            }
        }

        int nextIndex =
            (currentIndex + direction + buttons.Length) %
            buttons.Length;

        SelectButton(buttons[nextIndex], true);
    }

    private void HandleLeaderboardInput()
    {
        if (Time.frameCount <= openedFrame)
        {
            return;
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame ||
            Keyboard.current.backspaceKey.wasPressedThisFrame)
        {
            CloseLeaderboard();
            return;
        }

        if (WasSubmitPressed())
        {
            backButton?.onClick.Invoke();
        }
    }

    private void OpenLeaderboard()
    {
        if (isOpen || leaderboardPanel == null)
        {
            return;
        }

        isOpen = true;
        openedFrame = Time.frameCount;
        loadVersion++;

        leaderboardPanel.SetActive(true);
        ClearRows();
        SetStatus("Loading...");
        SelectButton(backButton, false);
        GameAudioManager.Instance?.PlayButtonSound();

        _ = LoadLeaderboardAsync(loadVersion);
    }

    private void CloseLeaderboard()
    {
        if (!isOpen)
        {
            return;
        }

        isOpen = false;
        isLoading = false;
        loadVersion++;
        ClearRows();

        if (leaderboardPanel != null)
        {
            leaderboardPanel.SetActive(false);
        }

        SelectButton(leaderboardButton, false);
        GameAudioManager.Instance?.PlayButtonSound();
    }

    private async Task LoadLeaderboardAsync(int requestedVersion)
    {
        if (isLoading)
        {
            return;
        }

        isLoading = true;

        try
        {
            OnlineServicesManager onlineServices =
                OnlineServicesManager.Instance ??
                FindFirstObjectByType<OnlineServicesManager>();

            if (onlineServices == null)
            {
                SetStatusIfCurrent(
                    requestedVersion,
                    "Online services are unavailable.");
                return;
            }

            await onlineServices.InitializeAsync();

            if (!onlineServices.IsReady)
            {
                SetStatusIfCurrent(
                    requestedVersion,
                    "Unable to connect to the leaderboard.");
                return;
            }

            LeaderboardScoresPage page =
                await LeaderboardsService.Instance.GetScoresAsync(
                    LeaderboardId,
                    new GetScoresOptions
                    {
                        Offset = 0,
                        Limit = maximumEntries
                    });

            string currentPlayerId = onlineServices.PlayerId;
            LeaderboardEntry currentPlayerEntry = null;

            if (!ContainsPlayer(page.Results, currentPlayerId))
            {
                currentPlayerEntry =
                    await TryGetCurrentPlayerEntryAsync();
            }

            if (!IsCurrentRequest(requestedVersion))
            {
                return;
            }

            RenderEntries(page.Results, currentPlayerEntry);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Unable to load leaderboard.\n{exception.Message}",
                this);
            SetStatusIfCurrent(
                requestedVersion,
                "Unable to load the leaderboard.");
        }
        finally
        {
            if (requestedVersion == loadVersion)
            {
                isLoading = false;
            }
        }
    }

    private async Task<LeaderboardEntry>
        TryGetCurrentPlayerEntryAsync()
    {
        try
        {
            return await LeaderboardsService.Instance
                .GetPlayerScoreAsync(LeaderboardId);
        }
        catch (Exception)
        {
            // A new player may not have submitted a score yet.
            return null;
        }
    }

    private void RenderEntries(
        IReadOnlyList<LeaderboardEntry> entries,
        LeaderboardEntry currentPlayerEntry)
    {
        ClearRows();

        if ((entries == null || entries.Count == 0) &&
            currentPlayerEntry == null)
        {
            SetStatus("No scores have been submitted yet.");
            return;
        }

        if (entryRowTemplate == null)
        {
            SetStatus("Entry Row Template is not assigned.");
            Debug.LogError(
                "LeaderboardPanelController requires an Entry Row Template.",
                this);
            return;
        }

        SetStatus(string.Empty);

        string currentPlayerId =
            OnlineServicesManager.Instance?.PlayerId ?? string.Empty;

        bool currentPlayerIsInTop =
            ContainsPlayer(entries, currentPlayerId);

        if (!currentPlayerIsInTop &&
            currentPlayerEntry != null)
        {
            RenderTopEntriesWithCurrentPlayer(
                entries,
                currentPlayerEntry,
                currentPlayerId);
            return;
        }

        int count = entries == null
            ? 0
            : Mathf.Min(maximumEntries, entries.Count);
        long? previousScore = null;
        int displayedRank = 0;

        for (int index = 0; index < count; index++)
        {
            LeaderboardEntry entry = entries[index];
            long roundedScore = (long)Math.Round(entry.Score);

            if (!previousScore.HasValue ||
                roundedScore != previousScore.Value)
            {
                displayedRank = entry.Rank + 1;
            }

            previousScore = roundedScore;

            CreateEntryRow(
                entry,
                index,
                displayedRank,
                currentPlayerId);
        }
    }

    private void RenderTopEntriesWithCurrentPlayer(
        IReadOnlyList<LeaderboardEntry> entries,
        LeaderboardEntry currentPlayerEntry,
        string currentPlayerId)
    {
        int topEntryLimit = Mathf.Min(
            maximumEntries,
            topEntriesWhenShowingCurrentPlayer);
        int topEntryCount = entries == null
            ? 0
            : Mathf.Min(topEntryLimit, entries.Count);
        long? previousScore = null;
        int displayedRank = 0;

        for (int index = 0; index < topEntryCount; index++)
        {
            LeaderboardEntry entry = entries[index];
            long roundedScore = (long)Math.Round(entry.Score);

            if (!previousScore.HasValue ||
                roundedScore != previousScore.Value)
            {
                displayedRank = entry.Rank + 1;
            }

            previousScore = roundedScore;

            CreateEntryRow(
                entry,
                index,
                displayedRank,
                currentPlayerId);
        }

        CreateSeparatorRow(topEntryCount);
        CreateEntryRow(
            currentPlayerEntry,
            topEntryCount + 1,
            currentPlayerEntry.Rank + 1,
            currentPlayerId);
    }

    private void CreateEntryRow(
        LeaderboardEntry entry,
        int rowIndex,
        int displayedRank,
        string currentPlayerId)
    {
        RectTransform row = CreateRow(
            $"EntryRow_{entry.Rank + 1}",
            rowIndex);

        bool isCurrentPlayer =
            !string.IsNullOrEmpty(currentPlayerId) &&
            entry.PlayerId == currentPlayerId;
        long roundedScore = (long)Math.Round(entry.Score);

        SetRowText(
            row,
            "RankText",
            displayedRank.ToString(CultureInfo.InvariantCulture));
        SetRowText(
            row,
            "NameText",
            GetDisplayName(entry, isCurrentPlayer));
        SetRowText(
            row,
            "ScoreText",
            roundedScore.ToString(CultureInfo.InvariantCulture));

        Image background = row.GetComponent<Image>();

        if (background != null)
        {
            background.color = isCurrentPlayer
                ? currentPlayerRowColor
                : Color.clear;
        }
    }

    private void CreateSeparatorRow(int rowIndex)
    {
        RectTransform row = CreateRow(
            "EntryRow_Separator",
            rowIndex);

        SetRowText(row, "RankText", string.Empty);
        SetRowText(row, "NameText", "...");
        SetRowText(row, "ScoreText", string.Empty);

        Image background = row.GetComponent<Image>();

        if (background != null)
        {
            background.color = Color.clear;
        }
    }

    private RectTransform CreateRow(
        string rowName,
        int rowIndex)
    {
        RectTransform row = Instantiate(
            entryRowTemplate,
            entryRowTemplate.parent);

        row.name = rowName;
        row.anchoredPosition =
            entryRowTemplate.anchoredPosition +
            Vector2.down * rowSpacing * rowIndex;
        row.localScale = Vector3.one;
        row.gameObject.SetActive(true);
        spawnedRows.Add(row.gameObject);

        return row;
    }

    private static bool ContainsPlayer(
        IReadOnlyList<LeaderboardEntry> entries,
        string playerId)
    {
        if (entries == null ||
            string.IsNullOrEmpty(playerId))
        {
            return false;
        }

        for (int index = 0; index < entries.Count; index++)
        {
            if (entries[index].PlayerId == playerId)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetDisplayName(
        LeaderboardEntry entry,
        bool isCurrentPlayer)
    {
        string displayName = entry.PlayerName;

        if (isCurrentPlayer &&
            PlayerProfileManager.Instance != null &&
            !string.IsNullOrEmpty(
                PlayerProfileManager.Instance.DisplayName))
        {
            displayName =
                PlayerProfileManager.Instance.DisplayName;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = string.IsNullOrEmpty(entry.PlayerId)
                ? "Unknown"
                : $"Player-{entry.PlayerId[..Mathf.Min(5, entry.PlayerId.Length)]}";
        }

        int separatorIndex = displayName.LastIndexOf('#');

        if (separatorIndex > 0 &&
            separatorIndex + 1 < displayName.Length &&
            int.TryParse(
                displayName[(separatorIndex + 1)..],
                out _))
        {
            displayName = displayName[..separatorIndex];
        }

        return isCurrentPlayer
            ? $"{displayName} (You)"
            : displayName;
    }

    private static void SetRowText(
        Transform row,
        string childName,
        string value)
    {
        Transform child = row.Find(childName);
        TMP_Text text = child != null
            ? child.GetComponent<TMP_Text>()
            : null;

        if (text != null)
        {
            text.text = value;
        }
    }

    private void ClearRows()
    {
        foreach (GameObject row in spawnedRows)
        {
            if (row != null)
            {
                Destroy(row);
            }
        }

        spawnedRows.Clear();
    }

    private void SetStatusIfCurrent(
        int requestedVersion,
        string message)
    {
        if (IsCurrentRequest(requestedVersion))
        {
            SetStatus(message);
        }
    }

    private void SetStatus(string message)
    {
        if (statusText == null)
        {
            return;
        }

        statusText.text = message;
        statusText.gameObject.SetActive(
            !string.IsNullOrEmpty(message));
    }

    private bool IsCurrentRequest(int requestedVersion)
    {
        return isOpen &&
               requestedVersion == loadVersion &&
               leaderboardPanel != null &&
               leaderboardPanel.activeInHierarchy;
    }

    private static void ConfigureButton(Button button)
    {
        if (button == null)
        {
            return;
        }

        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        if (button.GetComponent<MenuButtonSelection>() == null)
        {
            button.gameObject.AddComponent<MenuButtonSelection>();
        }
    }

    private static bool WasSubmitPressed()
    {
        return Keyboard.current.enterKey.wasPressedThisFrame ||
               Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
               Keyboard.current.spaceKey.wasPressedThisFrame;
    }

    private static void SelectButton(
        Button button,
        bool playSound)
    {
        if (button == null ||
            EventSystem.current == null ||
            !button.gameObject.activeInHierarchy)
        {
            return;
        }

        if (EventSystem.current.currentSelectedGameObject ==
            button.gameObject)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(button.gameObject);

        if (playSound)
        {
            GameAudioManager.Instance?.PlayButtonSound();
        }
    }
}
