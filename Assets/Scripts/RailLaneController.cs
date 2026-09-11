using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
[DisallowMultipleComponent]
public sealed class RailLaneController : MonoBehaviour
{
    private enum RailState
    {
        Cooldown,
        Warning,
        TrainPassing
    }

    [Header("Rail Bounds")]
    [SerializeField, Min(0.1f)] private float horizontalLimit = 14f;

    [Header("Train Speed")]
    [SerializeField, Min(0.1f)] private float minBaseSpeed = 12f;
    [SerializeField, Min(0.1f)] private float maxBaseSpeed = 16f;

    [Header("Timing")]
    [SerializeField, Min(0.1f)] private float minCooldown = 3.5f;
    [SerializeField, Min(0.1f)] private float maxCooldown = 6.5f;
    [SerializeField, Min(0.1f)] private float warningDuration = 1.5f;
    [SerializeField, Min(0.05f)] private float warningFlashInterval = 0.2f;

    [Header("Warning Appearance")]
    [SerializeField] private Color warningColor = new(1f, 0.15f, 0.05f, 1f);

    private SpriteRenderer laneRenderer;
    private Color normalColor;
    private TrainPool trainPool;
    private Train activeTrain;
    private RailState state;
    private float stateTimer;
    private float flashTimer;
    private float speedMultiplier = 1f;
    private float warningDurationMultiplier = 1f;
    private bool warningVisible;

    public void Configure(
        float newSpeedMultiplier,
        float newWarningDurationMultiplier)
    {
        speedMultiplier = Mathf.Max(0.1f, newSpeedMultiplier);
        warningDurationMultiplier = Mathf.Max(0.1f, newWarningDurationMultiplier);
    }

    private void Awake()
    {
        laneRenderer = GetComponent<SpriteRenderer>();
        normalColor = laneRenderer.color;
    }

    private void Start()
    {
        trainPool = TrainPool.Instance;
        if (trainPool == null)
        {
            Debug.LogError(
                "RailLaneController requires an active TrainPool in the scene.",
                this);
            enabled = false;
            return;
        }

        BeginCooldown();
    }

    private void Update()
    {
        switch (state)
        {
            case RailState.Cooldown:
                UpdateCooldown();
                break;
            case RailState.Warning:
                UpdateWarning();
                break;
            case RailState.TrainPassing:
                break;
        }
    }

    private void UpdateCooldown()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            BeginWarning();
        }
    }

    private void UpdateWarning()
    {
        stateTimer -= Time.deltaTime;
        flashTimer -= Time.deltaTime;

        if (flashTimer <= 0f)
        {
            warningVisible = !warningVisible;
            laneRenderer.color = warningVisible ? warningColor : normalColor;
            flashTimer = warningFlashInterval;
        }

        if (stateTimer <= 0f)
        {
            SpawnTrain();
        }
    }

    private void BeginCooldown()
    {
        state = RailState.Cooldown;
        laneRenderer.color = normalColor;
        stateTimer = Random.Range(
            Mathf.Min(minCooldown, maxCooldown),
            Mathf.Max(minCooldown, maxCooldown));
    }

    private void BeginWarning()
    {
        state = RailState.Warning;
        stateTimer = warningDuration * warningDurationMultiplier;
        flashTimer = 0f;
        warningVisible = false;
    }

    private void SpawnTrain()
    {
        state = RailState.TrainPassing;
        laneRenderer.color = normalColor;

        bool movesRight = Random.value >= 0.5f;
        float speed = Random.Range(
            Mathf.Min(minBaseSpeed, maxBaseSpeed),
            Mathf.Max(minBaseSpeed, maxBaseSpeed)) * speedMultiplier;
        float spawnX = movesRight ? -horizontalLimit : horizontalLimit;
        Vector2 spawnPosition = new(spawnX, transform.position.y);

        activeTrain = trainPool.Get(
            spawnPosition,
            movesRight,
            speed,
            horizontalLimit);

        GameAudioManager.Instance?.PlayTrainPassSound();
        activeTrain.Released += HandleTrainReleased;
    }

    private void HandleTrainReleased(Train train)
    {
        train.Released -= HandleTrainReleased;

        if (activeTrain == train)
        {
            activeTrain = null;
        }

        BeginCooldown();
    }

    private void OnDestroy()
    {
        if (activeTrain != null)
        {
            activeTrain.Released -= HandleTrainReleased;
        }
    }
}
