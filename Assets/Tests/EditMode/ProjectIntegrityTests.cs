using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrossyRoad.Tests.EditMode
{
    public sealed class ProjectIntegrityTests
    {
        private const string ProductionScenePath =
            "Assets/Scenes/SampleScene.unity";

        private static readonly string[] RequiredRootObjects =
        {
            "Main Camera",
            "GameManager",
            "Player",
            "Environment",
            "Canvas",
            "EventSystem",
            "RoadVehiclePool",
            "RiverLogPool",
            "TrainPool",
            "GameAudioManager",
            "OnlineServicesManager"
        };

        [Test]
        public void ProductionScene_IsEnabledInBuildSettings()
        {
            bool isEnabled = EditorBuildSettings.scenes.Any(
                scene => scene.enabled && scene.path == ProductionScenePath);

            Assert.That(
                isEnabled,
                Is.True,
                $"{ProductionScenePath} must be enabled in Build Settings.");
        }

        [Test]
        public void ProductionScene_ContainsRequiredRootObjects()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ProductionScenePath,
                OpenSceneMode.Single);
            HashSet<string> rootNames = scene
                .GetRootGameObjects()
                .Select(root => root.name)
                .ToHashSet();

            foreach (string requiredName in RequiredRootObjects)
            {
                Assert.That(
                    rootNames,
                    Does.Contain(requiredName),
                    $"Production scene is missing the root object '{requiredName}'.");
            }
        }

        [Test]
        public void ProductionScene_HasNoMissingMonoBehaviours()
        {
            Scene scene = EditorSceneManager.OpenScene(
                ProductionScenePath,
                OpenSceneMode.Single);

            AssertNoMissingScripts(
                scene.GetRootGameObjects(),
                ProductionScenePath);
        }

        [Test]
        public void GameplayPrefabs_HaveNoMissingMonoBehaviours()
        {
            string[] prefabGuids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { "Assets/Prefabs" });

            Assert.That(prefabGuids, Is.Not.Empty);

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                Assert.That(prefab, Is.Not.Null, $"Unable to load prefab {path}.");
                AssertNoMissingScripts(new[] { prefab }, path);
            }
        }

        private static void AssertNoMissingScripts(
            IEnumerable<GameObject> roots,
            string assetPath)
        {
            foreach (GameObject root in roots)
            {
                foreach (Transform transform in
                         root.GetComponentsInChildren<Transform>(true))
                {
                    int missingCount =
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            transform.gameObject);

                    Assert.That(
                        missingCount,
                        Is.Zero,
                        $"{assetPath} contains {missingCount} missing script(s) " +
                        $"on '{GetHierarchyPath(transform)}'.");
                }
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            Transform parent = transform.parent;

            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }

            return path;
        }
    }
}
