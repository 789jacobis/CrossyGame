using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class GameManager : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text gameOverTitle;
    [SerializeField] private TMP_Text restartHint;
    [SerializeField] private Button gameOverRestartButton;

    [Header("Menus")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private TMP_Text startBestScoreText;
    [SerializeField] private Button startButton;
    [SerializeField] private Button pauseButton;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button pauseResumeButton;
    [SerializeField] private Button pauseRestartButton;

    [Header("Start Countdown")]
    [SerializeField] private TMP_Text countdownText;
    [SerializeField, Min(0.1f)] private float numberDuration = 1f;
    [SerializeField, Min(0.1f)] private float startMessageDuration = 0.75f;

    private bool isGameOver;
    private bool isPaused;
    private bool hasGameplayStarted;
    private bool isStartingGame;
    private bool pauseRestartSelected;

    private void Awake()
    {
        Time.timeScale = 1f;
        player?.SetGameplayInputEnabled(false);

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }

        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }

        if (pauseButton != null)
        {
            pauseButton.gameObject.SetActive(false);
        }

        DisablePauseButtonNavigation();

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        GameAudioManager.Instance?.SetTrainPassSoundEnabled(false);
        GameAudioManager.Instance?.PlayMainMenuMusic();

        if (startPanel != null)
        {
            startPanel.SetActive(true);
            UpdateStartMenu();
            SelectButton(startButton);
            return;
        }

        StartGame();
    }

    private void OnEnable()
    {
        if (player == null)
        {
            Debug.LogError("GameManager requires a Player reference.", this);
            enabled = false;
            return;
        }

        player.Died += HandlePlayerDied;
        player.Moved += HandlePlayerMoved;
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.Died -= HandlePlayerDied;
            player.Moved -= HandlePlayerMoved;
        }
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (isGameOver)
        {
            if (Keyboard.current.rKey.wasPressedThisFrame ||
                WasSubmitPressed())
            {
                InvokeButtonOrRestart(gameOverRestartButton);
            }

            return;
        }

        if (!hasGameplayStarted)
        {
            if (LeaderboardPanelController.ManagesStartMenuInput)
            {
                return;
            }

            if (!isStartingGame &&
                (Keyboard.current.enterKey.wasPressedThisFrame ||
                 Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
                 Keyboard.current.spaceKey.wasPressedThisFrame))
            {
                StartGame();
            }

            return;
        }

        if (isPaused)
        {
            HandlePauseMenuInput();
            return;
        }

        if (Keyboard.current.pKey.wasPressedThisFrame ||
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        if (isGameOver || !hasGameplayStarted)
        {
            return;
        }

        if (isPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    public void PauseGame()
    {
        if (isGameOver || !hasGameplayStarted || isPaused)
        {
            return;
        }

        isPaused = true;
        GameAudioManager.Instance?.PlayButtonSound();
        GameAudioManager.Instance?.PauseMusic();
        player.SetGameplayInputEnabled(false);
        Time.timeScale = 0f;

        if (pausePanel != null)
        {
            pausePanel.SetActive(true);
        }
        else if (countdownText != null)
        {
            countdownText.text = "PAUSED";
            countdownText.gameObject.SetActive(true);
        }

        if (pauseButton != null)
        {
            pauseButton.gameObject.SetActive(false);
        }

        SelectPauseMenuButton(false);
    }

    public void ResumeGame()
    {
        if (isGameOver || !isPaused)
        {
            return;
        }

        isPaused = false;
        GameAudioManager.Instance?.PlayButtonSound();
        GameAudioManager.Instance?.ResumeMusic();
        Time.timeScale = 1f;

        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }
        else if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (pauseButton != null)
        {
            pauseButton.gameObject.SetActive(true);
        }

        SelectButton(pauseButton);
        player.SetGameplayInputEnabled(true);
    }

    public void StartGame()
    {
        if (isGameOver ||
            hasGameplayStarted ||
            isStartingGame ||
            LeaderboardPanelController.BlocksGameStart ||
            SoundSettingsPanelController.BlocksGameStart ||
            NameEntryPanelController.BlocksGameStart)
        {
            return;
        }

        if (NameEntryPanelController.OpenIfNameRequired())
        {
            return;
        }

        isStartingGame = true;
        GameAudioManager.Instance?.PlayButtonSound();
        GameAudioManager.Instance?.PlayGameplayMusic();

        if (startPanel != null)
        {
            startPanel.SetActive(false);
        }

        SelectButton(null);

        if (countdownText == null)
        {
            Debug.LogError(
                "GameManager requires a Countdown Text reference. Gameplay will start without a countdown.",
                this);
            BeginGameplay();
            return;
        }

        StartCoroutine(RunStartCountdown());
    }

    public void RestartGame()
    {
        GameAudioManager.Instance?.PlayButtonSound();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private IEnumerator RunStartCountdown()
    {
        countdownText.gameObject.SetActive(true);

        for (int number = 3; number >= 1; number--)
        {
            countdownText.text = number.ToString();
            yield return new WaitForSecondsRealtime(numberDuration);
        }

        countdownText.text = "START";
        yield return new WaitForSecondsRealtime(startMessageDuration);

        countdownText.gameObject.SetActive(false);
        BeginGameplay();
    }

    private void BeginGameplay()
    {
        if (isGameOver)
        {
            return;
        }

        isStartingGame = false;
        hasGameplayStarted = true;
        player.SetGameplayInputEnabled(true);
        Time.timeScale = 1f;
        GameAudioManager.Instance?.SetTrainPassSoundEnabled(true);

        if (pauseButton != null)
        {
            pauseButton.gameObject.SetActive(true);
        }

        SelectButton(pauseButton);
    }

    private void HandlePlayerDied(PlayerDeathCause cause)
    {
        isGameOver = true;
        isPaused = false;
        player.SetGameplayInputEnabled(false);
        Time.timeScale = 0f;

        if (pausePanel != null)
        {
            pausePanel.SetActive(false);
        }

        if (pauseButton != null)
        {
            pauseButton.gameObject.SetActive(false);
        }

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (scoreManager != null)
        {
            scoreManager.SaveHighScore();

            if (OnlineServicesManager.Instance != null)
            {
                _ = OnlineServicesManager.Instance
                    .SubmitBestScoreAsync(scoreManager.HighScore);
            }
        }

        GameAudioManager.Instance?.StopMusic();
        GameAudioManager.Instance?.SetTrainPassSoundEnabled(false);
        GameAudioManager.Instance?.PlayDeathSounds(cause);

        UpdateGameOverResults();

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        SelectButton(gameOverRestartButton);

        Debug.Log("Game Over - Press R to restart.");
    }

    private void HandlePlayerMoved()
    {
        GameAudioManager.Instance?.PlayJumpSound();
    }

    private void DisablePauseButtonNavigation()
    {
        if (pauseResumeButton != null)
        {
            Navigation resumeNavigation = pauseResumeButton.navigation;
            resumeNavigation.mode = Navigation.Mode.None;
            pauseResumeButton.navigation = resumeNavigation;
        }

        if (pauseRestartButton != null)
        {
            Navigation restartNavigation = pauseRestartButton.navigation;
            restartNavigation.mode = Navigation.Mode.None;
            pauseRestartButton.navigation = restartNavigation;
        }
    }

    private void HandlePauseMenuInput()
    {
        if (Keyboard.current.pKey.wasPressedThisFrame ||
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ResumeGame();
            return;
        }

        if (Keyboard.current.upArrowKey.wasPressedThisFrame ||
            Keyboard.current.downArrowKey.wasPressedThisFrame ||
            Keyboard.current.wKey.wasPressedThisFrame ||
            Keyboard.current.sKey.wasPressedThisFrame)
        {
            SelectPauseMenuButton(!IsPauseRestartSelected());
            GameAudioManager.Instance?.PlayButtonSound();
            return;
        }

        if (!WasSubmitPressed())
        {
            return;
        }

        if (IsPauseRestartSelected())
        {
            if (pauseRestartButton != null)
            {
                pauseRestartButton.onClick.Invoke();
            }
            else
            {
                RestartGame();
            }
        }
        else if (pauseResumeButton != null)
        {
            pauseResumeButton.onClick.Invoke();
        }
        else
        {
            ResumeGame();
        }
    }

    private void SelectPauseMenuButton(bool selectRestart)
    {
        pauseRestartSelected = selectRestart;
        SelectButton(selectRestart
            ? pauseRestartButton
            : pauseResumeButton);
    }

    private bool IsPauseRestartSelected()
    {
        if (EventSystem.current != null &&
            pauseRestartButton != null &&
            EventSystem.current.currentSelectedGameObject ==
            pauseRestartButton.gameObject)
        {
            pauseRestartSelected = true;
        }
        else if (EventSystem.current != null &&
                 pauseResumeButton != null &&
                 EventSystem.current.currentSelectedGameObject ==
                 pauseResumeButton.gameObject)
        {
            pauseRestartSelected = false;
        }

        return pauseRestartSelected;
    }

    private static bool WasSubmitPressed()
    {
        return Keyboard.current.enterKey.wasPressedThisFrame ||
               Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
               Keyboard.current.spaceKey.wasPressedThisFrame;
    }

    private void InvokeButtonOrRestart(Button button)
    {
        if (button != null && button.interactable)
        {
            button.onClick.Invoke();
            return;
        }

        RestartGame();
    }

    private static void SelectButton(Button button)
    {
        if (EventSystem.current == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(null);

        if (button != null && button.gameObject.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(button.gameObject);
        }
    }

    private void UpdateStartMenu()
    {
        if (startBestScoreText != null && scoreManager != null)
        {
            startBestScoreText.text = $"Best: {scoreManager.HighScore}";
        }
    }

    private void UpdateGameOverResults()
    {
        if (scoreManager == null)
        {
            return;
        }

        if (gameOverTitle != null)
        {
            gameOverTitle.text =
                $"GAME OVER\nScore: {scoreManager.CurrentScore}\nBest: {scoreManager.HighScore}";
        }

        if (restartHint != null)
        {
            restartHint.text = scoreManager.IsNewHighScore
                ? "NEW HIGH SCORE!\nPress Enter, R, or click Restart"
                : "Press Enter, R, or click Restart";
        }
    }
}
