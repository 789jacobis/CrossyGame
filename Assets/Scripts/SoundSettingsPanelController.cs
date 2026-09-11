using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SoundSettingsPanelController : MonoBehaviour
{
    [Header("Panels and Buttons")]
    [SerializeField] private GameObject soundPanel;
    [SerializeField] private Button soundButton;
    [SerializeField] private Button soundToggleButton;
    [SerializeField] private Button soundBackButton;

    [Header("Text")]
    [SerializeField] private TMP_Text soundToggleText;

    private static SoundSettingsPanelController activeInstance;

    private bool isOpen;
    private int openedFrame = -1;
    private int closedFrame = -1;

    public static bool BlocksGameStart =>
        activeInstance != null &&
        (activeInstance.isOpen ||
         Time.frameCount <= activeInstance.closedFrame);

    private void Awake()
    {
        activeInstance = this;

        soundButton?.onClick.AddListener(OpenSoundPanel);
        soundToggleButton?.onClick.AddListener(ToggleSound);
        soundBackButton?.onClick.AddListener(CloseSoundPanel);

        ConfigureButton(soundButton);
        ConfigureButton(soundToggleButton);
        ConfigureButton(soundBackButton);

        if (soundPanel != null)
        {
            soundPanel.SetActive(false);
        }

        UpdateToggleText();
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
        {
            activeInstance = null;
        }

        soundButton?.onClick.RemoveListener(OpenSoundPanel);
        soundToggleButton?.onClick.RemoveListener(ToggleSound);
        soundBackButton?.onClick.RemoveListener(CloseSoundPanel);
    }

    private void Update()
    {
        if (!isOpen || Keyboard.current == null)
        {
            return;
        }

        if (Time.frameCount <= openedFrame)
        {
            return;
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame ||
            Keyboard.current.backspaceKey.wasPressedThisFrame)
        {
            CloseSoundPanel();
            return;
        }

        if (Keyboard.current.upArrowKey.wasPressedThisFrame ||
            Keyboard.current.wKey.wasPressedThisFrame)
        {
            MoveSelection(-1);
            return;
        }

        if (Keyboard.current.downArrowKey.wasPressedThisFrame ||
            Keyboard.current.sKey.wasPressedThisFrame)
        {
            MoveSelection(1);
            return;
        }

        if (!WasSubmitPressed())
        {
            return;
        }

        GameObject selected =
            EventSystem.current?.currentSelectedGameObject;

        if (selected == soundBackButton?.gameObject)
        {
            soundBackButton.onClick.Invoke();
        }
        else
        {
            soundToggleButton?.onClick.Invoke();
        }

    }

    private void MoveSelection(int direction)
    {
        Button[] buttons =
        {
            soundToggleButton,
            soundBackButton
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

    private void OpenSoundPanel()
    {
        if (isOpen || soundPanel == null)
        {
            return;
        }

        isOpen = true;
        openedFrame = Time.frameCount;
        soundPanel.SetActive(true);
        UpdateToggleText();
        SelectButton(soundToggleButton, false);
        GameAudioManager.Instance?.PlayButtonSound();
    }

    private void CloseSoundPanel()
    {
        if (!isOpen)
        {
            return;
        }

        isOpen = false;
        closedFrame = Time.frameCount;

        if (soundPanel != null)
        {
            soundPanel.SetActive(false);
        }

        SelectButton(soundButton, false);
        GameAudioManager.Instance?.PlayButtonSound();
    }

    private void ToggleSound()
    {
        GameAudioManager audioManager = GameAudioManager.Instance;

        if (audioManager == null)
        {
            return;
        }

        bool enableAudio = !audioManager.IsAudioEnabled;

        if (!enableAudio)
        {
            audioManager.PlayButtonSound();
        }

        audioManager.SetAudioEnabled(enableAudio);

        if (enableAudio)
        {
            audioManager.PlayButtonSound();
        }

        UpdateToggleText();
        SelectButton(soundToggleButton, false);
    }

    private void UpdateToggleText()
    {
        if (soundToggleText == null)
        {
            return;
        }

        bool isEnabled =
            GameAudioManager.Instance == null ||
            GameAudioManager.Instance.IsAudioEnabled;

        soundToggleText.text = isEnabled
            ? "SOUND: ON"
            : "SOUND: OFF";
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
