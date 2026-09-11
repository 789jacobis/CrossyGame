using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class RiverLogPool : MonoBehaviour
{
    [Serializable]
    private sealed class RiverLogType
    {
        [SerializeField] private RiverLog prefab;
        [SerializeField, Min(0)] private int initialSize = 8;
        [SerializeField, Min(0)] private int laneWeight = 1;

        [NonSerialized] private Queue<RiverLog> availableLogs;

        public RiverLog Prefab => prefab;
        public int InitialSize => initialSize;
        public int LaneWeight => laneWeight;
        public Queue<RiverLog> AvailableLogs =>
            availableLogs ??= new Queue<RiverLog>();
    }

    [SerializeField] private RiverLogType[] logTypes;

    public static RiverLogPool Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Only one RiverLogPool may exist in a scene.", this);
            enabled = false;
            return;
        }

        Instance = this;

        if (!HasUsableLogType())
        {
            Debug.LogError(
                "RiverLogPool requires at least one Log Type with a Prefab and a positive Lane Weight.",
                this);
            Instance = null;
            enabled = false;
            return;
        }

        for (int typeIndex = 0; typeIndex < logTypes.Length; typeIndex++)
        {
            RiverLogType logType = logTypes[typeIndex];
            if (logType == null || logType.Prefab == null)
            {
                continue;
            }

            for (int index = 0; index < logType.InitialSize; index++)
            {
                logType.AvailableLogs.Enqueue(CreateLog(typeIndex));
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

    public int ChooseLogTypeIndex(int excludedTypeIndex = -1)
    {
        int totalWeight = CalculateTotalWeight(excludedTypeIndex);

        // If only one usable type exists, keep the river functional even though
        // the adjacent-river rule cannot be satisfied.
        if (totalWeight <= 0)
        {
            excludedTypeIndex = -1;
            totalWeight = CalculateTotalWeight(excludedTypeIndex);
        }

        int selection = UnityEngine.Random.Range(0, totalWeight);

        for (int typeIndex = 0; typeIndex < logTypes.Length; typeIndex++)
        {
            RiverLogType logType = logTypes[typeIndex];
            if (typeIndex == excludedTypeIndex ||
                logType == null ||
                logType.Prefab == null ||
                logType.LaneWeight <= 0)
            {
                continue;
            }

            selection -= logType.LaneWeight;
            if (selection < 0)
            {
                return typeIndex;
            }
        }

        throw new InvalidOperationException("No usable river log type was found.");
    }

    private int CalculateTotalWeight(int excludedTypeIndex)
    {
        int totalWeight = 0;

        for (int typeIndex = 0; typeIndex < logTypes.Length; typeIndex++)
        {
            RiverLogType logType = logTypes[typeIndex];
            if (typeIndex != excludedTypeIndex &&
                logType != null &&
                logType.Prefab != null)
            {
                totalWeight += logType.LaneWeight;
            }
        }

        return totalWeight;
    }

    public float GetLogWidth(int typeIndex)
    {
        ValidateTypeIndex(typeIndex);

        RiverLog prefab = logTypes[typeIndex].Prefab;
        BoxCollider2D boxCollider = prefab.GetComponent<BoxCollider2D>();

        if (boxCollider != null)
        {
            return Mathf.Abs(
                prefab.transform.localScale.x * boxCollider.size.x);
        }

        return Mathf.Max(0.1f, Mathf.Abs(prefab.transform.localScale.x));
    }

    public RiverLog Get(
        int typeIndex,
        Vector2 position,
        bool movesRight,
        float speed,
        float horizontalLimit)
    {
        ValidateTypeIndex(typeIndex);

        RiverLogType logType = logTypes[typeIndex];
        RiverLog riverLog = logType.AvailableLogs.Count > 0
            ? logType.AvailableLogs.Dequeue()
            : CreateLog(typeIndex);

        riverLog.Activate(
            position,
            movesRight,
            speed,
            horizontalLimit,
            this,
            typeIndex);

        return riverLog;
    }

    public void Release(RiverLog riverLog)
    {
        int typeIndex = riverLog.PoolTypeIndex;
        ValidateTypeIndex(typeIndex);

        riverLog.Deactivate();
        logTypes[typeIndex].AvailableLogs.Enqueue(riverLog);
    }

    private bool HasUsableLogType()
    {
        if (logTypes == null)
        {
            return false;
        }

        foreach (RiverLogType logType in logTypes)
        {
            if (logType != null &&
                logType.Prefab != null &&
                logType.LaneWeight > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void ValidateTypeIndex(int typeIndex)
    {
        if (typeIndex < 0 ||
            typeIndex >= logTypes.Length ||
            logTypes[typeIndex] == null ||
            logTypes[typeIndex].Prefab == null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(typeIndex),
                typeIndex,
                "River log type index is invalid.");
        }
    }

    private RiverLog CreateLog(int typeIndex)
    {
        RiverLog riverLog = Instantiate(logTypes[typeIndex].Prefab, transform);
        riverLog.gameObject.SetActive(false);
        return riverLog;
    }
}
