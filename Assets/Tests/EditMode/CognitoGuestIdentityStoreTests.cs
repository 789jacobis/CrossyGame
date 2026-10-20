using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace CrossyRoad.Tests.EditMode
{
    public sealed class CognitoGuestIdentityStoreTests
    {
        private const string PoolA =
            "ap-northeast-1:11111111-1111-4111-8111-111111111111";
        private const string PoolB =
            "ap-northeast-1:22222222-2222-4222-8222-222222222222";
        private const string Identity =
            "ap-northeast-1:33333333-3333-4333-8333-333333333333";

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey("CrossyRoad.CognitoGuestIdentity." + PoolA);
            PlayerPrefs.DeleteKey("CrossyRoad.CognitoGuestIdentity." + PoolB);
        }

        [Test]
        public void SavedIdentity_IsReusedForTheSamePool()
        {
            Save(PoolA, Identity);

            Assert.That(Load(PoolA), Is.EqualTo(Identity));
            Assert.That(Load(PoolB), Is.Empty);
        }

        [Test]
        public void MalformedOrWrongRegionIdentity_IsIgnored()
        {
            PlayerPrefs.SetString("CrossyRoad.CognitoGuestIdentity." + PoolA,
                "ap-northeast-1:not-a-guid");
            Assert.That(Load(PoolA), Is.Empty);

            PlayerPrefs.SetString("CrossyRoad.CognitoGuestIdentity." + PoolA,
                "us-east-1:33333333-3333-4333-8333-333333333333");
            Assert.That(Load(PoolA), Is.Empty);
        }

        [Test]
        public void Save_RejectsInvalidIdentity()
        {
            TargetInvocationException exception =
                Assert.Throws<TargetInvocationException>(() =>
                    Save(PoolA, "invalid"));
            Assert.That(exception.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(Load(PoolA), Is.Empty);
        }

        // Unity's asmdef test assembly cannot directly reference Assembly-CSharp.
        private static Type StoreType => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("CognitoGuestIdentityStore"))
            .First(type => type != null);

        private static string Load(string poolId)
        {
            return (string)StoreType.GetMethod("Load")
                .Invoke(null, new object[] { poolId });
        }

        private static void Save(string poolId, string identityId)
        {
            StoreType.GetMethod("Save")
                .Invoke(null, new object[] { poolId, identityId });
        }
    }
}
