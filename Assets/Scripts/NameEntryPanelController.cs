using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class NameEntryPanelController : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject namePanel;
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private TMP_Text errorText;

    [Header("Buttons")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button defaultButton;
    [SerializeField] private Button startButton;

    private static NameEntryPanelController activeInstance;

    private bool isSubmitting;
    private int openedFrame = -1;
    private int blockGameStartThroughFrame = -1;

    public static bool BlocksGameStart =>
        activeInstance != null &&
        (activeInstance.IsOpen ||
         Time.frameCount <= activeInstance.blockGameStartThroughFrame);

    public static bool OpenIfNameRequired()
    {
        PlayerProfileManager profile = PlayerProfileManager.Instance;

        if (activeInstance == null ||
            profile == null ||
            profile.HasChosenName)
        {
            return false;
        }

        activeInstance.OpenPanel(profile);
        return true;
    }

    private bool IsOpen =>
        namePanel != null && namePanel.activeInHierarchy;

    private void Awake()
    {
        activeInstance = this;

        if (namePanel == null)
        {
            namePanel = gameObject;
        }

        confirmButton?.onClick.AddListener(ConfirmEnteredName);
        defaultButton?.onClick.AddListener(UseDefaultName);
        nameInput?.onValueChanged.AddListener(HandleNameChanged);

#if UNITY_WEBGL
        ConfigureWebInput();
#endif

        ConfigureButton(confirmButton);
        ConfigureButton(defaultButton);

        if (errorText != null)
        {
            errorText.text = string.Empty;
        }

        namePanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
        {
            activeInstance = null;
        }

        confirmButton?.onClick.RemoveListener(ConfirmEnteredName);
        defaultButton?.onClick.RemoveListener(UseDefaultName);
        nameInput?.onValueChanged.RemoveListener(HandleNameChanged);
    }

    private void Update()
    {
        if (!IsOpen ||
            isSubmitting ||
            Keyboard.current == null ||
            Time.frameCount <= openedFrame)
        {
            return;
        }

        GameObject selected =
            EventSystem.current?.currentSelectedGameObject;

        if (Keyboard.current.upArrowKey.wasPressedThisFrame ||
            Keyboard.current.wKey.wasPressedThisFrame)
        {
            SelectInput();
            return;
        }

        if (Keyboard.current.downArrowKey.wasPressedThisFrame ||
            Keyboard.current.sKey.wasPressedThisFrame ||
            Keyboard.current.tabKey.wasPressedThisFrame)
        {
            SelectButton(confirmButton);
            return;
        }

        if (selected == confirmButton?.gameObject ||
            selected == defaultButton?.gameObject)
        {
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame ||
                Keyboard.current.aKey.wasPressedThisFrame)
            {
                SelectButton(confirmButton);
                return;
            }

            if (Keyboard.current.rightArrowKey.wasPressedThisFrame ||
                Keyboard.current.dKey.wasPressedThisFrame)
            {
                SelectButton(defaultButton);
                return;
            }
        }

        if (!Keyboard.current.enterKey.wasPressedThisFrame &&
            !Keyboard.current.numpadEnterKey.wasPressedThisFrame)
        {
            return;
        }

        if (selected == defaultButton?.gameObject)
        {
            UseDefaultName();
        }
        else
        {
            ConfirmEnteredName();
        }
    }

    private void OpenPanel(PlayerProfileManager profile)
    {
        blockGameStartThroughFrame = int.MaxValue;
        openedFrame = Time.frameCount;
        namePanel.SetActive(true);

        if (nameInput != null)
        {
            nameInput.text = string.Empty;
        }

        if (defaultButton != null)
        {
            TMP_Text label =
                defaultButton.GetComponentInChildren<TMP_Text>();

            if (label != null)
            {
                label.text =
                    $"USE DEFAULT\n{profile.DefaultDisplayName}";
            }
        }

        ShowError(string.Empty);
        SelectInput();
    }

    private async void ClosePanel()
    {
        blockGameStartThroughFrame = Time.frameCount;
        nameInput?.DeactivateInputField();
        namePanel.SetActive(false);

        await Task.Yield();

        if (startButton != null &&
            startButton.gameObject.activeInHierarchy)
        {
            startButton.onClick.Invoke();
        }
    }

    private void ConfirmEnteredName()
    {
        if (!isSubmitting)
        {
            _ = SubmitEnteredNameAsync();
        }
    }

    private void UseDefaultName()
    {
        if (!isSubmitting)
        {
            _ = UseDefaultNameAsync();
        }
    }

    private async Task SubmitEnteredNameAsync()
    {
        PlayerProfileManager profile = PlayerProfileManager.Instance;

        if (profile == null)
        {
            ShowError("找不到 PlayerProfileManager。");
            return;
        }

        string requestedName = nameInput != null
            ? nameInput.text
            : string.Empty;

        if (!profile.TryValidateName(
                requestedName,
                out _,
                out string validationError))
        {
            ShowError(validationError);
            SelectInput(false);
            return;
        }

        await SubmitAsync(profile.SetPlayerNameAsync(requestedName));
    }

    private async Task UseDefaultNameAsync()
    {
        PlayerProfileManager profile = PlayerProfileManager.Instance;

        if (profile == null)
        {
            ShowError("找不到 PlayerProfileManager。");
            return;
        }

        await SubmitAsync(profile.UseDefaultNameAsync());
    }

    private async Task SubmitAsync(Task<bool> submission)
    {
        isSubmitting = true;
        SetButtonsInteractable(false);
        ShowError("儲存中...");
        GameAudioManager.Instance?.PlayButtonSound();

        bool succeeded = await submission;

        if (succeeded)
        {
            ClosePanel();
            return;
        }

        isSubmitting = false;
        SetButtonsInteractable(true);
        ShowError(PlayerProfileManager.Instance?.LastError ??
                  "無法儲存名稱。");
        SelectInput(false);
    }

    private void HandleNameChanged(string value)
    {
        if (!isSubmitting)
        {
            ShowError(string.Empty);
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (confirmButton != null)
        {
            confirmButton.interactable = interactable;
        }

        if (defaultButton != null)
        {
            defaultButton.interactable = interactable;
        }
    }

    private void ShowError(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
        }
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

#if UNITY_WEBGL
    private void ConfigureWebInput()
    {
        if (nameInput != null &&
            nameInput.GetComponent<WebGLSupport.WebGLInput>() == null)
        {
            nameInput.gameObject.AddComponent<WebGLSupport.WebGLInput>();
        }
    }
#endif

    private void SelectInput(bool playSound = true)
    {
        if (nameInput == null || EventSystem.current == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(
            nameInput.gameObject);
        nameInput.ActivateInputField();

        if (playSound)
        {
            GameAudioManager.Instance?.PlayButtonSound();
        }
    }

    private static void SelectButton(
        Button button,
        bool playSound = true)
    {
        if (button == null ||
            !button.gameObject.activeInHierarchy ||
            EventSystem.current == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(
            button.gameObject);

        if (playSound)
        {
            GameAudioManager.Instance?.PlayButtonSound();
        }
    }
}
