using System;
using System.Collections;
using CML.Unity.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CML.Tests.PlayMode
{
    public sealed class StarterIslandReviewPlayModeTests
    {
        private const string ReviewScenePath =
            "Assets/_Project/Scenes/91_StarterIsland_Review.unity";

        [UnityTest]
        public IEnumerator PlayerSettlesAndWalksUsingOnlyUnityPhysics()
        {
            if (!Application.CanStreamedLevelBeLoaded(ReviewScenePath))
            {
                Assert.Pass(
                    "Starter Island review scene has not been generated yet.");
                yield break;
            }

            var operation = SceneManager.LoadSceneAsync(
                ReviewScenePath,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            while (!operation.isDone)
            {
                yield return null;
            }

            yield return null;
            Physics.SyncTransforms();

            var island = GameObject.Find("PF_StarterIsland");
            var player =
                GameObject.Find("ENV_StarterIsland_ReviewPlayer");
            Assert.That(island, Is.Not.Null);
            Assert.That(player, Is.Not.Null);

            var islandColliders =
                island.GetComponentsInChildren<Collider>(true);
            Assert.That(islandColliders, Has.Length.EqualTo(1));
            Assert.That(islandColliders[0], Is.TypeOf<MeshCollider>());
            var meshCollider = (MeshCollider)islandColliders[0];
            Assert.That(meshCollider.convex, Is.False);
            Assert.That(meshCollider.isTrigger, Is.False);
            Assert.That(
                island.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);

            foreach (var behaviour in
                     island.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Assert.That(
                    behaviour,
                    Is.Null,
                    "The island prefab must contain no custom behaviour.");
            }

            foreach (var root in
                     SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var behaviour in
                         root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    Assert.That(behaviour, Is.Not.Null);
                    Assert.That(
                        behaviour.GetType().Name,
                        Is.Not.EqualTo("AirshipObstacleIdentity"));
                    Assert.That(
                        behaviour.GetType().Name,
                        Is.Not.EqualTo("AirshipTechnicalScenario"));
                }
            }

            var controller = player.GetComponent<CharacterController>();
            var input = player.GetComponent<StarterIslandReviewPlayer>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(input, Is.Not.Null);
            Assert.That(player.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(input.CharacterController, Is.SameAs(controller));

            input.enabled = false;
            var initialPosition = player.transform.position;
            for (var step = 0; step < 120; step++)
            {
                input.StepMovement(
                    Vector2.zero,
                    false,
                    1f / 60f);
                yield return null;
            }

            Assert.That(
                player.transform.position.y,
                Is.GreaterThan(initialPosition.y - 2f),
                "Player fell through the visible island mesh.");
            Assert.That(controller.isGrounded, Is.True);

            var settledPosition = player.transform.position;
            for (var step = 0; step < 90; step++)
            {
                input.StepMovement(
                    Vector2.up,
                    false,
                    1f / 60f);
                yield return null;
            }

            var planarDisplacement = Vector3.ProjectOnPlane(
                player.transform.position - settledPosition,
                Vector3.up).magnitude;
            Assert.That(
                planarDisplacement,
                Is.GreaterThan(3.5f),
                "Player is trapped by collision that has no visible source.");
            Assert.That(
                player.transform.position.y,
                Is.GreaterThan(-5f),
                "Player left the authored walkable island unexpectedly.");
        }
    }
}
