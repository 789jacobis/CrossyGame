using System.Collections.Generic;
using UnityEngine;

public sealed class GrassObstacleSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject obstaclePrefab;

    [Header("Grid Range")]
    [SerializeField] private int minimumX = -8;
    [SerializeField] private int maximumX = 8;
    [SerializeField, Min(0)] private int minimumObstacles = 0;
    [SerializeField, Min(0)] private int maximumObstacles = 4;
    [SerializeField, Min(1)] private int minimumOpenCells = 3;

    private readonly List<GameObject> spawnedObstacles = new();

    private void Start()
    {
        if (obstaclePrefab == null)
        {
            Debug.LogError("GrassObstacleSpawner requires an Obstacle Prefab reference.", this);
            enabled = false;
            return;
        }

        SpawnObstacles();
    }

    private void OnDestroy()
    {
        foreach (GameObject obstacle in spawnedObstacles)
        {
            if (obstacle != null)
            {
                Destroy(obstacle);
            }
        }
    }

    private void SpawnObstacles()
    {
        int firstX = Mathf.Min(minimumX, maximumX);
        int lastX = Mathf.Max(minimumX, maximumX);
        int cellCount = lastX - firstX + 1;
        int allowedMaximum = Mathf.Max(0, cellCount - minimumOpenCells);
        int clampedMaximum = Mathf.Min(maximumObstacles, allowedMaximum);
        int clampedMinimum = Mathf.Min(minimumObstacles, clampedMaximum);
        int obstacleCount = Random.Range(clampedMinimum, clampedMaximum + 1);

        HashSet<int> occupiedCells = new();
        while (occupiedCells.Count < obstacleCount)
        {
            occupiedCells.Add(Random.Range(firstX, lastX + 1));
        }

        foreach (int x in occupiedCells)
        {
            Vector3 spawnPosition = new(x, transform.position.y, 0f);
            GameObject obstacle = Instantiate(obstaclePrefab, spawnPosition, Quaternion.identity);
            spawnedObstacles.Add(obstacle);
        }
    }
}
