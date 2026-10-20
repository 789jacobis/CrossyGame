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
            CognitoGuestIdentityStore.Save(PoolA, Identity);

            Assert.That(CognitoGuestIdentityStore.Load(PoolA), Is.EqualTo(Identity));
            Assert.That(CognitoGuestIdentityStore.Load(PoolB), Is.Empty);
        }

        [Test]
        public void MalformedOrWrongRegionIdentity_IsIgnored()
        {
            PlayerPrefs.SetString("CrossyRoad.CognitoGuestIdentity." + PoolA,
                "ap-northeast-1:not-a-guid");
            Assert.That(CognitoGuestIdentityStore.Load(PoolA), Is.Empty);

            PlayerPrefs.SetString("CrossyRoad.CognitoGuestIdentity." + PoolA,
                "us-east-1:33333333-3333-4333-8333-333333333333");
            Assert.That(CognitoGuestIdentityStore.Load(PoolA), Is.Empty);
        }

        [Test]
        public void Save_RejectsInvalidIdentity()
        {
            Assert.Throws<System.ArgumentException>(() =>
                CognitoGuestIdentityStore.Save(PoolA, "invalid"));
            Assert.That(CognitoGuestIdentityStore.Load(PoolA), Is.Empty);
        }
    }
}
