using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MenuButtonSelection : MonoBehaviour, IPointerEnterHandler
{
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button == null ||
            !button.IsInteractable() ||
            EventSystem.current == null ||
            EventSystem.current.currentSelectedGameObject == gameObject)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(gameObject);
        GameAudioManager.Instance?.PlayButtonSound();
    }
}
