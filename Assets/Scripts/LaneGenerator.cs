using System.Collections.Generic;
using UnityEngine;

public sealed class LaneGenerator : MonoBehaviour
{
    private enum LaneType
    {
        Grass,
        Road,
        River,
        Rail
    }

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private DifficultyConfig difficultyConfig;
    [SerializeField] private GameObject grassLanePrefab;
    [SerializeField] private GameObject roadLanePrefab;
    [SerializeField] private GameObject riverLanePrefab;
    [SerializeField] private GameObject railLanePrefab;

    [Header("Generation")]
    [SerializeField, Min(1f)] private float spawnAheadDistance = 10f;
    [SerializeField, Min(1f)] private float removeBehindDistance = 8f;
    [SerializeField, Min(0.1f)] private float laneHeight = 1f;

    private readonly Queue<GameObject> activeLanes = new();
    private float highestLaneY;
    private LaneType lastLaneType;
    private int consecutiveLaneCount;
    private bool hasLastLaneType;
    private int lastAdjacentRiverLogTypeIndex = -1;
    private int lastAdjacentRiverDirection;

    private void Start()
    {
        if (player == null || scoreManager == null || difficultyConfig == null ||
            grassLanePrefab == null || roadLanePrefab == null)
        {
            Debug.LogError("LaneGenerator is missing one or more required references.", this);
            enabled = false;
            return;
        }

        RegisterExistingLanes();
        GenerateAhead();
    }

    private void Update()
    {
        GenerateAhead();
        RemoveBehind();
    }

    private void RegisterExistingLanes()
    {
        List<Transform> existingLanes = new();

        foreach (Transform child in transform)
        {
            existingLanes.Add(child);
        }

        existingLanes.Sort((left, right) =>
            left.position.y.CompareTo(right.position.y));

        foreach (Transform lane in existingLanes)
        {
            activeLanes.Enqueue(lane.gameObject);
        }

        highestLaneY = existingLanes.Count > 0
            ? existingLanes[^1].position.y
            : player.position.y - laneHeight;

        RegisterCurrentLaneStreak(existingLanes);
    }

    private void RegisterCurrentLaneStreak(List<Transform> existingLanes)
    {
        if (existingLanes.Count == 0)
        {
            return;
        }

        lastLaneType = GetLaneType(existingLanes[^1]);
        hasLastLaneType = true;

        for (int index = existingLanes.Count - 1; index >= 0; index--)
        {
            if (GetLaneType(existingLanes[index]) != lastLaneType)
            {
                break;
            }

            consecutiveLaneCount++;
        }
    }

    private void GenerateAhead()
    {
        float requiredHighestY = player.position.y + spawnAheadDistance;

        while (highestLaneY < requiredHighestY)
        {
            SpawnNextLane();
        }
    }

    private void SpawnNextLane()
    {
        highestLaneY += laneHeight;
        int laneScore = scoreManager.GetScoreForWorldY(highestLaneY);
        DifficultyTier difficulty = difficultyConfig.GetTier(laneScore);
        LaneType laneType = ChooseLaneType(difficulty);
        GameObject prefab = GetPrefab(laneType);
        int riverLogTypeIndex = ChooseRiverLogType(laneType);
        int riverDirection = ChooseRiverDirection(laneType);

        RegisterSpawnedLaneType(laneType);
        lastAdjacentRiverLogTypeIndex = laneType == LaneType.River
            ? riverLogTypeIndex
            : -1;
        lastAdjacentRiverDirection = laneType == LaneType.River
            ? riverDirection
            : 0;

        Vector3 spawnPosition = new(
            prefab.transform.position.x,
            highestLaneY,
            prefab.transform.position.z);

        GameObject lane = Instantiate(
            prefab,
            spawnPosition,
            Quaternion.identity,
            transform);

        lane.name = $"{prefab.name}_{highestLaneY:0}";

        if (lane.TryGetComponent(out RoadVehicleSpawner roadVehicleSpawner))
        {
            roadVehicleSpawner.Configure(difficulty.roadVehicleSpeedMultiplier);
        }

        if (lane.TryGetComponent(out RiverLogSpawner riverLogSpawner))
        {
            riverLogSpawner.Configure(
                difficulty.logSpeedMultiplier,
                riverLogTypeIndex,
                riverDirection);
        }

        if (lane.TryGetComponent(out RailLaneController railLaneController))
        {
            railLaneController.Configure(
                difficulty.trainSpeedMultiplier,
                difficulty.trainWarningDurationMultiplier);
        }

        activeLanes.Enqueue(lane);
    }

    private int ChooseRiverLogType(LaneType laneType)
    {
        if (laneType != LaneType.River || RiverLogPool.Instance == null)
        {
            return -1;
        }

        int excludedTypeIndex =
            hasLastLaneType && lastLaneType == LaneType.River
                ? lastAdjacentRiverLogTypeIndex
                : -1;

        return RiverLogPool.Instance.ChooseLogTypeIndex(excludedTypeIndex);
    }

    private int ChooseRiverDirection(LaneType laneType)
    {
        if (laneType != LaneType.River)
        {
            return 0;
        }

        if (hasLastLaneType &&
            lastLaneType == LaneType.River &&
            lastAdjacentRiverDirection != 0)
        {
            return -lastAdjacentRiverDirection;
        }

        return Random.value >= 0.5f ? 1 : -1;
    }

    private LaneType ChooseLaneType(DifficultyTier difficulty)
    {
        float grassWeight = difficulty.grassChance;
        float riverWeight = riverLanePrefab != null ? difficulty.riverChance : 0f;
        float railWeight = railLanePrefab != null ? difficulty.railChance : 0f;
        float roadWeight = Mathf.Max(
            0f,
            1f - grassWeight - riverWeight - railWeight);

        bool grassAllowed =
            !HasReachedLimit(LaneType.Grass, difficulty.maxConsecutiveGrass);
        bool roadAllowed =
            !HasReachedLimit(LaneType.Road, difficulty.maxConsecutiveRoads);
        bool riverAllowed =
            riverLanePrefab != null &&
            !HasReachedLimit(LaneType.River, difficulty.maxConsecutiveRivers);
        bool railAllowed =
            railLanePrefab != null &&
            !HasReachedLimit(LaneType.Rail, difficulty.maxConsecutiveRails);

        if (!grassAllowed)
        {
            grassWeight = 0f;
        }

        if (!roadAllowed)
        {
            roadWeight = 0f;
        }

        if (!riverAllowed)
        {
            riverWeight = 0f;
        }

        if (!railAllowed)
        {
            railWeight = 0f;
        }

        float totalWeight = grassWeight + roadWeight + riverWeight + railWeight;
        if (totalWeight <= 0f)
        {
            return ChooseFallbackLaneType(
                grassAllowed,
                roadAllowed,
                riverAllowed,
                railAllowed);
        }

        float selection = Random.value * totalWeight;
        if (selection < grassWeight)
        {
            return LaneType.Grass;
        }

        selection -= grassWeight;
        if (selection < roadWeight)
        {
            return LaneType.Road;
        }

        selection -= roadWeight;
        if (selection < riverWeight)
        {
            return LaneType.River;
        }

        return LaneType.Rail;
    }

    private bool HasReachedLimit(LaneType laneType, int maximum)
    {
        return hasLastLaneType &&
               lastLaneType == laneType &&
               consecutiveLaneCount >= maximum;
    }

    private static LaneType ChooseFallbackLaneType(
        bool grassAllowed,
        bool roadAllowed,
        bool riverAllowed,
        bool railAllowed)
    {
        if (roadAllowed)
        {
            return LaneType.Road;
        }

        if (grassAllowed)
        {
            return LaneType.Grass;
        }

        if (riverAllowed)
        {
            return LaneType.River;
        }

        return railAllowed ? LaneType.Rail : LaneType.Grass;
    }

    private GameObject GetPrefab(LaneType laneType)
    {
        return laneType switch
        {
            LaneType.Grass => grassLanePrefab,
            LaneType.Road => roadLanePrefab,
            LaneType.River => riverLanePrefab,
            LaneType.Rail => railLanePrefab,
            _ => grassLanePrefab
        };
    }

    private static LaneType GetLaneType(Transform lane)
    {
        if (lane.TryGetComponent(out RailLaneController _))
        {
            return LaneType.Rail;
        }

        if (lane.TryGetComponent(out RiverLogSpawner _))
        {
            return LaneType.River;
        }

        return lane.TryGetComponent(out RoadVehicleSpawner _)
            ? LaneType.Road
            : LaneType.Grass;
    }

    private void RegisterSpawnedLaneType(LaneType laneType)
    {
        if (hasLastLaneType && lastLaneType == laneType)
        {
            consecutiveLaneCount++;
            return;
        }

        lastLaneType = laneType;
        consecutiveLaneCount = 1;
        hasLastLaneType = true;
    }

    private void RemoveBehind()
    {
        float lowestAllowedY = player.position.y - removeBehindDistance;

        while (activeLanes.Count > 0 &&
               activeLanes.Peek().transform.position.y < lowestAllowedY)
        {
            Destroy(activeLanes.Dequeue());
        }
    }
}
