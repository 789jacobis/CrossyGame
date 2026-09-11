using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public sealed class Train : MonoBehaviour
{
    private Rigidbody2D body;
    private SpriteRenderer trainVisual;
    private TrainPool pool;
    private float speed;
    private bool movesRight;
    private float horizontalLimit;

    public event Action<Train> Released;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        trainVisual = GetComponentInChildren<SpriteRenderer>();
    }

    internal void Activate(
        Vector2 position,
        bool shouldMoveRight,
        float moveSpeed,
        float boundary,
        TrainPool owner)
    {
        pool = owner;
        movesRight = shouldMoveRight;
        speed = moveSpeed;
        horizontalLimit = boundary;
        if (trainVisual != null)
        {
            trainVisual.flipX = !movesRight;
        }

        body.position = position;
        transform.position = position;
        gameObject.SetActive(true);
    }

    private void FixedUpdate()
    {
        if (speed <= 0f || horizontalLimit <= 0f)
        {
            return;
        }

        float direction = movesRight ? 1f : -1f;
        Vector2 nextPosition = body.position;
        nextPosition.x += direction * speed * Time.fixedDeltaTime;

        bool hasLeftBounds = movesRight
            ? nextPosition.x > horizontalLimit
            : nextPosition.x < -horizontalLimit;

        if (hasLeftBounds)
        {
            if (pool != null)
            {
                pool.Release(this);
                return;
            }

            gameObject.SetActive(false);
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
