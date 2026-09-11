using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerDeathCause
{
    Traffic,
    Water
}

[RequireComponent(typeof(Rigidbody2D))]
public sealed class PlayerController : MonoBehaviour
{
    private static readonly int JumpUpTrigger = Animator.StringToHash("JumpUp");
    private static readonly int JumpRightTrigger = Animator.StringToHash("JumpRight");

    [Header("Player Movement")]
    [SerializeField, Min(0.01f)] private float moveDistance = 1f;
    [SerializeField, Min(0.01f)] private float moveDuration = 0.12f;

    [Header("Screen Bounds")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField, Min(0f)] private float edgePadding = 0.4f;

    [Header("Movement Blocking")]
    [SerializeField] private LayerMask obstacleLayers;
    [SerializeField] private Vector2 obstacleCheckSize = new(0.6f, 0.6f);

    private Rigidbody2D body;
    private Collider2D playerCollider;
    private SpriteRenderer playerVisual;
    private Animator playerAnimator;
    private Vector2 startPosition;
    private Vector2 targetPosition;
    private float elapsedTime;
    private bool isMoving;
    private bool gameplayInputEnabled = true;
    private readonly HashSet<RiverLane> overlappingRiverLanes = new();
    private RiverLog supportingLog;
    private Collider2D supportingLogCollider;

    public event Action Moved;
    public event Action<PlayerDeathCause> Died;

    public bool IsDead { get; private set; }

    public void SetGameplayInputEnabled(bool inputEnabled)
    {
        gameplayInputEnabled = inputEnabled;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        playerVisual = GetComponentInChildren<SpriteRenderer>();
        playerAnimator = GetComponentInChildren<Animator>();

        if (gameplayCamera == null)
        {
            Debug.LogError("PlayerController requires a Gameplay Camera reference.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (!gameplayInputEnabled ||
            IsDead ||
            isMoving ||
            Keyboard.current == null)
        {
            return;
        }

        Vector2 direction = ReadMoveDirection();
        if (direction != Vector2.zero)
        {
            BeginMove(direction);
        }
    }

    private void FixedUpdate()
    {
        if (IsDead)
        {
            return;
        }

        Vector2 carrierDelta = supportingLog != null && supportingLog.isActiveAndEnabled
            ? supportingLog.Velocity * Time.fixedDeltaTime
            : Vector2.zero;

        if (carrierDelta != Vector2.zero)
        {
            startPosition += carrierDelta;
            targetPosition += carrierDelta;
        }

        if (isMoving)
        {
            elapsedTime += Time.fixedDeltaTime;
            float progress = Mathf.Clamp01(elapsedTime / moveDuration);
            float smoothedProgress = Mathf.SmoothStep(0f, 1f, progress);

            body.MovePosition(Vector2.Lerp(startPosition, targetPosition, smoothedProgress));

            if (progress >= 1f)
            {
                isMoving = false;
            }
        }
        else if (carrierDelta != Vector2.zero)
        {
            Vector2 carriedPosition = body.position + carrierDelta;
            if (!IsInsideScreenBounds(carriedPosition))
            {
                Die(PlayerDeathCause.Water);
                return;
            }

            body.MovePosition(carriedPosition);
        }

        if (!isMoving &&
            supportingLog != null &&
            supportingLogCollider != null)
        {
            AlignWithLog(supportingLogCollider);
        }

        if (!isMoving && overlappingRiverLanes.Count > 0 && supportingLog == null)
        {
            Die(PlayerDeathCause.Water);
        }
    }

    private void BeginMove(Vector2 direction)
    {
        Vector2 requestedPosition = body.position + direction * moveDistance;
        bool destinationIsRiver = IsRiverPosition(requestedPosition);

        if (!destinationIsRiver)
        {
            requestedPosition.x = SnapToMovementGrid(requestedPosition.x);
        }

        if (!IsInsideScreenBounds(requestedPosition) || IsBlocked(requestedPosition))
        {
            return;
        }

        if (!destinationIsRiver && supportingLog != null)
        {
            ClearSupportingLog(supportingLog);
        }

        startPosition = body.position;
        targetPosition = requestedPosition;
        elapsedTime = 0f;
        isMoving = true;
        PlayMoveAnimation(direction);
        Moved?.Invoke();
    }

    private void PlayMoveAnimation(Vector2 direction)
    {
        if (playerAnimator == null || playerVisual == null)
        {
            return;
        }

        playerAnimator.ResetTrigger(JumpUpTrigger);
        playerAnimator.ResetTrigger(JumpRightTrigger);

        if (Mathf.Abs(direction.x) > 0f)
        {
            playerVisual.flipX = direction.x < 0f;
            playerAnimator.SetTrigger(JumpRightTrigger);
            return;
        }

        playerVisual.flipX = false;
        playerAnimator.SetTrigger(JumpUpTrigger);
    }

    private static bool IsRiverPosition(Vector2 position)
    {
        Collider2D[] colliders = Physics2D.OverlapPointAll(position);

        foreach (Collider2D collider in colliders)
        {
            if (collider.TryGetComponent(out RiverLane _))
            {
                return true;
            }
        }

        return false;
    }

    private float SnapToMovementGrid(float position)
    {
        return Mathf.Round(position / moveDistance) * moveDistance;
    }

    private bool IsInsideScreenBounds(Vector2 position)
    {
        Vector3 bottomLeft = gameplayCamera.ViewportToWorldPoint(Vector3.zero);
        Vector3 topRight = gameplayCamera.ViewportToWorldPoint(Vector3.one);

        bool insideHorizontalBounds =
            position.x >= bottomLeft.x + edgePadding &&
            position.x <= topRight.x - edgePadding;

        bool aboveBottomBound = position.y >= bottomLeft.y + edgePadding;

        return insideHorizontalBounds && aboveBottomBound;
    }

    private bool IsBlocked(Vector2 position)
    {
        return Physics2D.OverlapBox(
            position,
            obstacleCheckSize,
            0f,
            obstacleLayers) != null;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsDead)
        {
            return;
        }

        if (other.TryGetComponent(out RoadVehicle _) ||
            other.TryGetComponent(out Train _))
        {
            Die(PlayerDeathCause.Traffic);
            return;
        }

        if (other.TryGetComponent(out RiverLane riverLane))
        {
            overlappingRiverLanes.Add(riverLane);
        }

        if (other.TryGetComponent(out RiverLog riverLog))
        {
            SetSupportingLog(riverLog, other);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.TryGetComponent(out RiverLane riverLane))
        {
            overlappingRiverLanes.Remove(riverLane);
        }

        if (other.TryGetComponent(out RiverLog riverLog) && riverLog == supportingLog)
        {
            ClearSupportingLog(riverLog);
        }
    }

    private void SetSupportingLog(RiverLog riverLog, Collider2D logCollider)
    {
        if (supportingLog == riverLog)
        {
            supportingLogCollider = logCollider;
            AlignWithLog(logCollider);
            return;
        }

        if (supportingLog != null)
        {
            supportingLog.Released -= HandleSupportingLogReleased;
        }

        supportingLog = riverLog;
        supportingLogCollider = logCollider;
        supportingLog.Released += HandleSupportingLogReleased;
        AlignWithLog(logCollider);
    }

    private void AlignWithLog(Collider2D logCollider)
    {
        float playerHalfWidth = playerCollider != null
            ? playerCollider.bounds.extents.x
            : 0f;

        if (playerVisual != null)
        {
            playerHalfWidth = Mathf.Max(
                playerHalfWidth,
                playerVisual.bounds.extents.x);
        }

        float minimumSafeX = logCollider.bounds.min.x + playerHalfWidth;
        float maximumSafeX = logCollider.bounds.max.x - playerHalfWidth;
        float alignedX = minimumSafeX <= maximumSafeX
            ? Mathf.Clamp(body.position.x, minimumSafeX, maximumSafeX)
            : logCollider.bounds.center.x;

        float correction = alignedX - body.position.x;
        if (Mathf.Approximately(correction, 0f))
        {
            return;
        }

        Vector2 alignedPosition = body.position;
        alignedPosition.x = alignedX;
        body.position = alignedPosition;
        transform.position = alignedPosition;

        if (isMoving)
        {
            startPosition.x += correction;
            targetPosition.x += correction;
        }
    }

    private void ClearSupportingLog(RiverLog riverLog)
    {
        riverLog.Released -= HandleSupportingLogReleased;

        if (supportingLog == riverLog)
        {
            supportingLog = null;
            supportingLogCollider = null;
        }
    }

    private void HandleSupportingLogReleased(RiverLog riverLog)
    {
        ClearSupportingLog(riverLog);
    }

    private void Die(PlayerDeathCause cause)
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        isMoving = false;
        Died?.Invoke(cause);
    }

    private static Vector2 ReadMoveDirection()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
        {
            return Vector2.up;
        }

        if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
        {
            return Vector2.down;
        }

        if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
        {
            return Vector2.left;
        }

        if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
        {
            return Vector2.right;
        }

        return Vector2.zero;
    }
}
