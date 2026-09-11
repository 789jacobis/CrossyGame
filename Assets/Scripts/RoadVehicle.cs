using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public sealed class RoadVehicle : MonoBehaviour
{
    private Rigidbody2D body;
    private SpriteRenderer vehicleVisual;
    private RoadVehiclePool pool;
    private float speed;
    private bool movesRight;
    private float horizontalLimit;

    internal int PoolTypeIndex { get; private set; } = -1;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        vehicleVisual = GetComponentInChildren<SpriteRenderer>();
    }

    internal void Activate(
        Vector2 position,
        bool shouldMoveRight,
        float moveSpeed,
        float boundary,
        RoadVehiclePool owner,
        int poolTypeIndex)
    {
        pool = owner;
        PoolTypeIndex = poolTypeIndex;
        movesRight = shouldMoveRight;
        speed = moveSpeed;
        horizontalLimit = boundary;
        if (vehicleVisual != null)
        {
            vehicleVisual.flipX = !movesRight;
        }

        body.position = position;
        transform.position = position;
        gameObject.SetActive(true);
    }

    private void FixedUpdate()
    {
        float direction = movesRight ? 1f : -1f;
        Vector2 nextPosition = body.position;
        nextPosition.x += direction * speed * Time.fixedDeltaTime;

        if (nextPosition.x > horizontalLimit)
        {
            if (pool != null)
            {
                pool.Release(this);
                return;
            }

            nextPosition.x = -horizontalLimit;
            TeleportTo(nextPosition);
            return;
        }

        if (nextPosition.x < -horizontalLimit)
        {
            if (pool != null)
            {
                pool.Release(this);
                return;
            }

            nextPosition.x = horizontalLimit;
            TeleportTo(nextPosition);
            return;
        }

        body.MovePosition(nextPosition);
    }

    internal void Deactivate()
    {
        body.linearVelocity = Vector2.zero;
        gameObject.SetActive(false);
    }

    private void TeleportTo(Vector2 position)
    {
        body.position = position;
        transform.position = position;
    }
}
