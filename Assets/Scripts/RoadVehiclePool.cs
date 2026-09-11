using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class RoadVehiclePool : MonoBehaviour
{
    [Serializable]
    private sealed class RoadVehicleType
    {
        [SerializeField] private RoadVehicle prefab;
        [SerializeField, Min(0)] private int initialSize = 8;
        [SerializeField, Min(0)] private int spawnWeight = 1;

        [NonSerialized] private Queue<RoadVehicle> availableVehicles;

        public RoadVehicle Prefab => prefab;
        public int InitialSize => initialSize;
        public int SpawnWeight => spawnWeight;
        public Queue<RoadVehicle> AvailableVehicles =>
            availableVehicles ??= new Queue<RoadVehicle>();
    }

    [SerializeField] private RoadVehicleType[] vehicleTypes;

    public static RoadVehiclePool Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Only one RoadVehiclePool may exist in a scene.", this);
            enabled = false;
            return;
        }

        Instance = this;

        if (!HasUsableVehicleType())
        {
            Debug.LogError(
                "RoadVehiclePool requires at least one Vehicle Type with a Prefab and a positive Spawn Weight.",
                this);
            Instance = null;
            enabled = false;
            return;
        }

        for (int typeIndex = 0; typeIndex < vehicleTypes.Length; typeIndex++)
        {
            RoadVehicleType vehicleType = vehicleTypes[typeIndex];

            if (vehicleType == null || vehicleType.Prefab == null)
            {
                continue;
            }

            for (int index = 0; index < vehicleType.InitialSize; index++)
            {
                vehicleType.AvailableVehicles.Enqueue(CreateVehicle(typeIndex));
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public RoadVehicle Get(
        Vector2 position,
        bool movesRight,
        float speed,
        float horizontalLimit)
    {
        int typeIndex = ChooseVehicleTypeIndex();
        RoadVehicleType vehicleType = vehicleTypes[typeIndex];
        RoadVehicle vehicle = vehicleType.AvailableVehicles.Count > 0
            ? vehicleType.AvailableVehicles.Dequeue()
            : CreateVehicle(typeIndex);

        vehicle.Activate(
            position,
            movesRight,
            speed,
            horizontalLimit,
            this,
            typeIndex);

        return vehicle;
    }

    public void Release(RoadVehicle vehicle)
    {
        int typeIndex = vehicle.PoolTypeIndex;

        if (typeIndex < 0 || typeIndex >= vehicleTypes.Length)
        {
            Debug.LogError("Vehicle has an invalid pool type index.", vehicle);
            vehicle.Deactivate();
            return;
        }

        vehicle.Deactivate();
        vehicleTypes[typeIndex].AvailableVehicles.Enqueue(vehicle);
    }

    private bool HasUsableVehicleType()
    {
        if (vehicleTypes == null)
        {
            return false;
        }

        foreach (RoadVehicleType vehicleType in vehicleTypes)
        {
            if (vehicleType != null &&
                vehicleType.Prefab != null &&
                vehicleType.SpawnWeight > 0)
            {
                return true;
            }
        }

        return false;
    }

    private int ChooseVehicleTypeIndex()
    {
        int totalWeight = 0;

        foreach (RoadVehicleType vehicleType in vehicleTypes)
        {
            if (vehicleType != null && vehicleType.Prefab != null)
            {
                totalWeight += vehicleType.SpawnWeight;
            }
        }

        int selection = UnityEngine.Random.Range(0, totalWeight);

        for (int typeIndex = 0; typeIndex < vehicleTypes.Length; typeIndex++)
        {
            RoadVehicleType vehicleType = vehicleTypes[typeIndex];

            if (vehicleType == null || vehicleType.Prefab == null)
            {
                continue;
            }

            selection -= vehicleType.SpawnWeight;

            if (selection < 0)
            {
                return typeIndex;
            }
        }

        throw new InvalidOperationException("No usable vehicle type was found.");
    }

    private RoadVehicle CreateVehicle(int typeIndex)
    {
        RoadVehicle vehicle = Instantiate(vehicleTypes[typeIndex].Prefab, transform);
        vehicle.gameObject.SetActive(false);
        return vehicle;
    }
}
