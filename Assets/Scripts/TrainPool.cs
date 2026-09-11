using System.Collections.Generic;
using UnityEngine;

public sealed class TrainPool : MonoBehaviour
{
    [SerializeField] private Train trainPrefab;
    [SerializeField, Min(0)] private int initialSize = 6;

    private readonly Queue<Train> availableTrains = new();

    public static TrainPool Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Only one TrainPool may exist in a scene.", this);
            enabled = false;
            return;
        }

        Instance = this;

        if (trainPrefab == null)
        {
            Debug.LogError("TrainPool requires a Train Prefab reference.", this);
            Instance = null;
            enabled = false;
            return;
        }

        for (int index = 0; index < initialSize; index++)
        {
            availableTrains.Enqueue(CreateTrain());
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public Train Get(
        Vector2 position,
        bool movesRight,
        float speed,
        float horizontalLimit)
    {
        Train train = availableTrains.Count > 0
            ? availableTrains.Dequeue()
            : CreateTrain();

        train.Activate(position, movesRight, speed, horizontalLimit, this);
        return train;
    }

    public void Release(Train train)
    {
        train.Deactivate();
        availableTrains.Enqueue(train);
    }

    private Train CreateTrain()
    {
        Train train = Instantiate(trainPrefab, transform);
        train.gameObject.SetActive(false);
        return train;
    }
}
