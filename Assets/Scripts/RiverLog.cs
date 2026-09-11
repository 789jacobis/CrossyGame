using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public sealed class RiverLog : MonoBehaviour
{
    private Rigidbody2D body;
    private RiverLogPool pool;
    private float speed;
    private bool movesRight;
    private float horizontalLimit;

    public event Action<RiverLog> Released;

    internal int PoolTypeIndex { get; private set; } = -1;

    public Vector2 Velocity =>
        (movesRight ? Vector2.right : Vector2.left) * speed;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    internal void Activate(
        Vector2 position,
        bool shouldMoveRight,
        float moveSpeed,
        float boundary,
        RiverLogPool owner,
        int poolTypeIndex)
    {
        pool = owner;
        PoolTypeIndex = poolTypeIndex;
        movesRight = shouldMoveRight;
        speed = moveSpeed;
        horizontalLimit = boundary;
        body.position = position;
        transform.position = position;
        gameObject.SetActive(true);
    }

    private void FixedUpdate()
    {
        // Runtime fields can temporarily reset during a Unity script reload in Play Mode.
        // Waiting for the next activation is safer than incorrectly returning a visible log.
        if (speed <= 0f || horizontalLimit <= 0f)
        {
            return;
        }

        Vector2 nextPosition = body.position + Velocity * Time.fixedDeltaTime;

        if (Mathf.Abs(nextPosition.x) > horizontalLimit)
        {
            if (pool != null)
            {
                pool.Release(this);
                return;
            }

            nextPosition.x = movesRight ? -horizontalLimit : horizontalLimit;
            body.position = nextPosition;
            transform.position = nextPosition;
            return;
        }

        body.MovePosition(nextPosition);
    }

    internal void Deactivate()
    {
        Released?.Invoke(this);
        body.linearVelocity = Vector2.zero;
        gameObject.SetActive(false);
    }
}
