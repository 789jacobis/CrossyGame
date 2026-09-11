using System;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public struct DifficultyTier
{
    [Min(0)] public int minimumScore;
    [FormerlySerializedAs("vehicleSpeedMultiplier")]
    [Min(0.1f)] public float roadVehicleSpeedMultiplier;
    [Min(0.1f)] public float logSpeedMultiplier;
    [Min(0.1f)] public float trainSpeedMultiplier;
    [Min(0.1f)] public float trainWarningDurationMultiplier;
    [Range(0f, 1f)] public float grassChance;
    [Range(0f, 1f)] public float riverChance;
    [Range(0f, 1f)] public float railChance;
    [Min(1)] public int maxConsecutiveRoads;
    [Min(1)] public int maxConsecutiveGrass;
    [Min(1)] public int maxConsecutiveRivers;
    [Min(1)] public int maxConsecutiveRails;
}

[CreateAssetMenu(fileName = "DifficultyConfig", menuName = "Crossy Road/Difficulty Config")]
public sealed class DifficultyConfig : ScriptableObject
{
    [SerializeField] private DifficultyTier[] tiers =
    {
        new DifficultyTier
        {
            minimumScore = 0,
            roadVehicleSpeedMultiplier = 1f,
            logSpeedMultiplier = 1f,
            trainSpeedMultiplier = 1f,
            trainWarningDurationMultiplier = 1f,
            grassChance = 0.4f,
            riverChance = 0.15f,
            railChance = 0.08f,
            maxConsecutiveRoads = 2,
            maxConsecutiveGrass = 3,
            maxConsecutiveRivers = 2,
            maxConsecutiveRails = 1
        },
        new DifficultyTier
        {
            minimumScore = 100,
            roadVehicleSpeedMultiplier = 1.15f,
            logSpeedMultiplier = 1.1f,
            trainSpeedMultiplier = 1.1f,
            trainWarningDurationMultiplier = 0.95f,
            grassChance = 0.35f,
            riverChance = 0.2f,
            railChance = 0.1f,
            maxConsecutiveRoads = 2,
            maxConsecutiveGrass = 3,
            maxConsecutiveRivers = 2,
            maxConsecutiveRails = 1
        },
        new DifficultyTier
        {
            minimumScore = 200,
            roadVehicleSpeedMultiplier = 1.3f,
            logSpeedMultiplier = 1.2f,
            trainSpeedMultiplier = 1.2f,
            trainWarningDurationMultiplier = 0.9f,
            grassChance = 0.3f,
            riverChance = 0.25f,
            railChance = 0.12f,
            maxConsecutiveRoads = 3,
            maxConsecutiveGrass = 2,
            maxConsecutiveRivers = 2,
            maxConsecutiveRails = 1
        },
        new DifficultyTier
        {
            minimumScore = 300,
            roadVehicleSpeedMultiplier = 1.4f,
            logSpeedMultiplier = 1.3f,
            trainSpeedMultiplier = 1.3f,
            trainWarningDurationMultiplier = 0.85f,
            grassChance = 0.25f,
            riverChance = 0.3f,
            railChance = 0.15f,
            maxConsecutiveRoads = 3,
            maxConsecutiveGrass = 2,
            maxConsecutiveRivers = 2,
            maxConsecutiveRails = 2
        }
    };

    public DifficultyTier GetTier(int score)
    {
        DifficultyTier selectedTier = new()
        {
            minimumScore = 0,
            roadVehicleSpeedMultiplier = 1f,
            logSpeedMultiplier = 1f,
            trainSpeedMultiplier = 1f,
            trainWarningDurationMultiplier = 1f,
            grassChance = 0.4f,
            riverChance = 0.15f,
            railChance = 0.08f,
            maxConsecutiveRoads = 2,
            maxConsecutiveGrass = 3,
            maxConsecutiveRivers = 2,
            maxConsecutiveRails = 1
        };

        int selectedMinimumScore = int.MinValue;

        if (tiers == null)
        {
            return selectedTier;
        }

        foreach (DifficultyTier tier in tiers)
        {
            if (tier.minimumScore <= score && tier.minimumScore >= selectedMinimumScore)
            {
                selectedTier = tier;
                selectedMinimumScore = tier.minimumScore;
            }
        }

        selectedTier.roadVehicleSpeedMultiplier = Mathf.Max(
            0.1f,
            selectedTier.roadVehicleSpeedMultiplier);
        selectedTier.logSpeedMultiplier = Mathf.Max(0.1f, selectedTier.logSpeedMultiplier);
        selectedTier.trainSpeedMultiplier = Mathf.Max(
            0.1f,
            selectedTier.trainSpeedMultiplier);
        selectedTier.trainWarningDurationMultiplier = Mathf.Max(
            0.1f,
            selectedTier.trainWarningDurationMultiplier);
        selectedTier.maxConsecutiveRoads = Mathf.Max(1, selectedTier.maxConsecutiveRoads);
        selectedTier.maxConsecutiveGrass = selectedTier.maxConsecutiveGrass > 0
            ? selectedTier.maxConsecutiveGrass
            : 3;
        selectedTier.maxConsecutiveRivers = selectedTier.maxConsecutiveRivers > 0
            ? selectedTier.maxConsecutiveRivers
            : 2;
        selectedTier.maxConsecutiveRails = selectedTier.maxConsecutiveRails > 0
            ? selectedTier.maxConsecutiveRails
            : 1;
        return selectedTier;
    }
}
