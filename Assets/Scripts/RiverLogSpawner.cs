using UnityEngine;

[DisallowMultipleComponent]
public sealed class RiverLogSpawner : MonoBehaviour
{
    [Header("River Bounds")]
    [SerializeField, Min(0.1f)] private float horizontalLimit = 12.5f;

    [Header("Log Speed")]
    [SerializeField, Min(0.1f)] private float minBaseSpeed = 1.5f;
    [SerializeField, Min(0.1f)] private float maxBaseSpeed = 2.5f;

    [Header("Log Spacing")]
    [SerializeField, Min(0f)] private float minWaterGap = 1.2f;
    [SerializeField, Min(0f)] private float maxWaterGap = 2f;

    private bool movesRight;
    private float logSpeed;
    private float centerSpacing;
    private float spawnTimer;
    private float speedMultiplier = 1f;
    private int logTypeIndex = -1;
    private int configuredMoveDirection;
    private RiverLogPool riverLogPool;

    public void Configure(
        float newSpeedMultiplier,
        int selectedLogTypeIndex = -1,
        int moveDirection = 0)
    {
        speedMultiplier = Mathf.Max(0.1f, newSpeedMultiplier);
        logTypeIndex = selectedLogTypeIndex;
        configuredMoveDirection = moveDirection;
    }

    private void Start()
    {
        riverLogPool = RiverLogPool.Instance;
        if (riverLogPool == null)
        {
            Debug.LogError(
                "RiverLogSpawner requires an active RiverLogPool in the scene.",
                this);
            enabled = false;
            return;
        }

        movesRight = configuredMoveDirection == 0
            ? Random.value >= 0.5f
            : configuredMoveDirection > 0;
        logSpeed = Random.Range(
            Mathf.Min(minBaseSpeed, maxBaseSpeed),
            Mathf.Max(minBaseSpeed, maxBaseSpeed)) * speedMultiplier;

        if (logTypeIndex < 0)
        {
            logTypeIndex = riverLogPool.ChooseLogTypeIndex();
        }

        float logWidth = riverLogPool.GetLogWidth(logTypeIndex);
        float waterGap = Random.Range(
            Mathf.Min(minWaterGap, maxWaterGap),
            Mathf.Max(minWaterGap, maxWaterGap));
        centerSpacing = logWidth + waterGap;

        float initialOffset = Random.Range(0f, centerSpacing);
        SpawnInitialStream(initialOffset);
        spawnTimer = (centerSpacing - initialOffset) / logSpeed;
    }

    private void Update()
    {
        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f)
        {
            return;
        }

        SpawnLogAtEntry();
        spawnTimer += centerSpacing / logSpeed;
    }

    private void SpawnInitialStream(float initialOffset)
    {
        float travelDirection = movesRight ? 1f : -1f;
        float entryX = movesRight ? -horizontalLimit : horizontalLimit;
        float travelDistance = horizontalLimit * 2f;

        for (float distance = initialOffset;
             distance < travelDistance;
             distance += centerSpacing)
        {
            SpawnLogAt(entryX + travelDirection * distance);
        }
    }

    private void SpawnLogAtEntry()
    {
        float spawnX = movesRight ? -horizontalLimit : horizontalLimit;
        SpawnLogAt(spawnX);
    }

    private void SpawnLogAt(float spawnX)
    {
        Vector3 spawnPosition = new(spawnX, transform.position.y, 0f);
        riverLogPool.Get(
            logTypeIndex,
            spawnPosition,
            movesRight,
            logSpeed,
            horizontalLimit);
    }
}
