using UnityEngine;

public sealed class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private float verticalOffset = 2f;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;

    private float highestTargetY;
    private float verticalVelocity;

    private void Awake()
    {
        if (player == null)
        {
            Debug.LogError("CameraFollow requires a Player reference.", this);
            enabled = false;
            return;
        }

        highestTargetY = transform.position.y;
    }

    private void LateUpdate()
    {
        highestTargetY = Mathf.Max(highestTargetY, player.position.y + verticalOffset);

        Vector3 cameraPosition = transform.position;
        cameraPosition.y = Mathf.SmoothDamp(
            cameraPosition.y,
            highestTargetY,
            ref verticalVelocity,
            smoothTime);

        transform.position = cameraPosition;
    }
}
