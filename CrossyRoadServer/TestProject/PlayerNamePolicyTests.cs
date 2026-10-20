using CrossyRoadServer;

namespace TestProject;

public sealed class PlayerNamePolicyTests
{
    [TestCase(" Alice ", "Alice", "ALICE")]
    [TestCase("alice", "alice", "ALICE")]
    [TestCase("Ａｌｉｃｅ", "Alice", "ALICE")]
    [TestCase("玩家_01", "玩家_01", "玩家_01")]
    public void TryNormalize_UsesOneKeyForEquivalentNames(
        string input,
        string expectedDisplay,
        string expectedKey)
    {
        bool valid = PlayerNamePolicy.TryNormalize(
            input, out string display, out string key);

        Assert.Multiple(() =>
        {
            Assert.That(valid, Is.True);
            Assert.That(display, Is.EqualTo(expectedDisplay));
            Assert.That(key, Is.EqualTo(expectedKey));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("a b")]
    [TestCase("a!")]
    [TestCase("1234567890123")]
    public void TryNormalize_RejectsUnsupportedNames(string? input)
    {
        Assert.That(PlayerNamePolicy.TryNormalize(
            input, out _, out _), Is.False);
    }
}
