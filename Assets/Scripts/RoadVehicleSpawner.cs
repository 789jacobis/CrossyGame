using UnityEngine;
using UnityEngine.Serialization;

public sealed class RoadVehicleSpawner : MonoBehaviour
{
    [Header("Road Bounds")]
    [SerializeField, Min(0.1f)] private float horizontalLimit = 11f;

    [Header("Traffic Speed")]
    [FormerlySerializedAs("minSpeed")]
    [SerializeField, Min(0.1f)] private float minBaseSpeed = 3f;
    [FormerlySerializedAs("maxSpeed")]
    [SerializeField, Min(0.1f)] private float maxBaseSpeed = 5f;

    [Header("Spawning")]
    [SerializeField, Min(0.1f)] private float minSpawnInterval = 1.4f;
    [SerializeField, Min(0.1f)] private float maxSpawnInterval = 2.4f;

    private bool movesRight;
    private float roadVehicleSpeed;
    private float spawnTimer;
    private float speedMultiplier = 1f;
    private RoadVehiclePool roadVehiclePool;

    public void Configure(float newSpeedMultiplier)
    {
        speedMultiplier = Mathf.Max(0.1f, newSpeedMultiplier);
    }

    private void Start()
    {
        roadVehiclePool = RoadVehiclePool.Instance;
        if (roadVehiclePool == null)
        {
            Debug.LogError("RoadVehicleSpawner requires an active RoadVehiclePool in the scene.", this);
            enabled = false;
            return;
        }

        movesRight = Random.value >= 0.5f;
        roadVehicleSpeed = Random.Range(
            Mathf.Min(minBaseSpeed, maxBaseSpeed),
            Mathf.Max(minBaseSpeed, maxBaseSpeed)) * speedMultiplier;
        ResetSpawnTimer(0.25f);
    }

    private void Update()
    {
        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f)
        {
            return;
        }

        SpawnVehicle();
        ResetSpawnTimer(1f);
    }

    private void SpawnVehicle()
    {
        float spawnX = movesRight ? -horizontalLimit : horizontalLimit;
        Vector3 spawnPosition = new(spawnX, transform.position.y, 0f);

        roadVehiclePool.Get(spawnPosition, movesRight, roadVehicleSpeed, horizontalLimit);
    }

    private void ResetSpawnTimer(float multiplier)
    {
        float minimum = Mathf.Min(minSpawnInterval, maxSpawnInterval);
        float maximum = Mathf.Max(minSpawnInterval, maxSpawnInterval);
        spawnTimer = Random.Range(minimum, maximum) * multiplier;
    }
}
