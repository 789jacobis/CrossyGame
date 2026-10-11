using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CrossyRoad.Tests.EditMode
{
    public sealed class DifficultyConfigTests
    {
        [Test]
        public void DifficultyTiers_AreOrderedAndValid()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:DifficultyConfig",
                new[] { "Assets/Data" });

            Assert.That(guids, Has.Length.EqualTo(1));

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Object config = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.That(config, Is.Not.Null);

            SerializedProperty tiers = new SerializedObject(config)
                .FindProperty("tiers");
            Assert.That(tiers, Is.Not.Null);
            Assert.That(tiers.arraySize, Is.GreaterThan(0));

            int previousMinimumScore = -1;

            for (int index = 0; index < tiers.arraySize; index++)
            {
                SerializedProperty tier = tiers.GetArrayElementAtIndex(index);
                int minimumScore = tier
                    .FindPropertyRelative("minimumScore")
                    .intValue;

                Assert.That(
                    minimumScore,
                    Is.GreaterThan(previousMinimumScore),
                    $"Difficulty tier {index} must have an increasing minimum score.");

                AssertPositive(tier, "roadVehicleSpeedMultiplier", index);
                AssertPositive(tier, "logSpeedMultiplier", index);
                AssertPositive(tier, "trainSpeedMultiplier", index);
                AssertPositive(tier, "trainWarningDurationMultiplier", index);
                AssertProbability(tier, "grassChance", index);
                AssertProbability(tier, "riverChance", index);
                AssertProbability(tier, "railChance", index);
                AssertPositive(tier, "maxConsecutiveRoads", index);
                AssertPositive(tier, "maxConsecutiveGrass", index);
                AssertPositive(tier, "maxConsecutiveRivers", index);
                AssertPositive(tier, "maxConsecutiveRails", index);

                previousMinimumScore = minimumScore;
            }
        }

        private static void AssertPositive(
            SerializedProperty tier,
            string propertyName,
            int tierIndex)
        {
            SerializedProperty property = tier.FindPropertyRelative(propertyName);
            Assert.That(property, Is.Not.Null);

            double value = property.propertyType == SerializedPropertyType.Integer
                ? property.intValue
                : property.floatValue;

            Assert.That(
                value,
                Is.GreaterThan(0d),
                $"Difficulty tier {tierIndex} has an invalid {propertyName}.");
        }

        private static void AssertProbability(
            SerializedProperty tier,
            string propertyName,
            int tierIndex)
        {
            SerializedProperty property = tier.FindPropertyRelative(propertyName);
            Assert.That(property, Is.Not.Null);
            Assert.That(
                property.floatValue,
                Is.InRange(0f, 1f),
                $"Difficulty tier {tierIndex} has an invalid {propertyName}.");
        }
    }
}
