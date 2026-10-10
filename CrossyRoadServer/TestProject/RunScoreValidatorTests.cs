using CrossyRoadServer;

namespace TestProject;

public sealed class RunScoreValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly RunScoreValidator validator = new();

    [Test]
    public void Start_CreatesUnconsumedRunForPlayer()
    {
        RunRecord run = RunRecord.Start("player-1", Now, "run-1");

        Assert.Multiple(() =>
        {
            Assert.That(run.RunId, Is.EqualTo("run-1"));
            Assert.That(run.PlayerId, Is.EqualTo("player-1"));
            Assert.That(
                run.StartedAtUnixMilliseconds,
                Is.EqualTo(Now.ToUnixTimeMilliseconds()));
            Assert.That(run.Consumed, Is.False);
        });
    }

    [TestCase(1)]
    [TestCase(100_000)]
    public void Validate_AcceptsScoresAtInclusiveRangeBoundaries(int score)
    {
        RunRecord run = CreateRun(Now - TimeSpan.FromHours(3));

        Assert.DoesNotThrow(() => validator.Validate(
            run,
            new RunScoreSubmission("player-1", "run-1", score),
            Now));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(100_001)]
    public void Validate_RejectsScoreOutsideAcceptedRange(int score)
    {
        RunRecord run = CreateRun(Now - TimeSpan.FromMinutes(1));

        AssertRejected(
            RunScoreRejectionReason.InvalidScore,
            () => validator.Validate(
                run,
                new RunScoreSubmission("player-1", "run-1", score),
                Now));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("another-run")]
    public void Validate_RejectsInvalidRunId(string? runId)
    {
        RunRecord run = CreateRun(Now - TimeSpan.FromMinutes(1));

        AssertRejected(
            RunScoreRejectionReason.InvalidRunId,
            () => validator.Validate(
                run,
                new RunScoreSubmission("player-1", runId!, 10),
                Now));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void Validate_RejectsMissingAuthenticatedPlayer(string? playerId)
    {
        RunRecord run = CreateRun(Now - TimeSpan.FromMinutes(1));

        AssertRejected(
            RunScoreRejectionReason.InvalidPlayerId,
            () => validator.Validate(
                run,
                new RunScoreSubmission(playerId!, "run-1", 10),
                Now));
    }

    [Test]
    public void Validate_RejectsRunOwnedByDifferentPlayer()
    {
        RunRecord run = CreateRun(Now - TimeSpan.FromMinutes(1));

        AssertRejected(
            RunScoreRejectionReason.PlayerMismatch,
            () => validator.Validate(
                run,
                new RunScoreSubmission("player-2", "run-1", 10),
                Now));
    }

    [Test]
    public void Validate_RejectsConsumedRun()
    {
        RunRecord run = CreateRun(Now - TimeSpan.FromMinutes(1));
        run.Consumed = true;

        AssertRejected(
            RunScoreRejectionReason.RunAlreadyConsumed,
            () => validator.Validate(
                run,
                new RunScoreSubmission("player-1", "run-1", 10),
                Now));
    }

    [Test]
    public void Validate_RejectsRunThatStartsInFuture()
    {
        RunRecord run = CreateRun(Now + TimeSpan.FromMilliseconds(1));

        AssertRejected(
            RunScoreRejectionReason.RunOutsideAcceptedTimeWindow,
            () => validator.Validate(
                run,
                new RunScoreSubmission("player-1", "run-1", 10),
                Now));
    }

    [Test]
    public void Validate_AcceptsRunAtMaximumAgeBoundary()
    {
        RunRecord run = CreateRun(Now - TimeSpan.FromHours(6));

        Assert.DoesNotThrow(() => validator.Validate(
            run,
            new RunScoreSubmission("player-1", "run-1", 10),
            Now));
    }

    [Test]
    public void Validate_RejectsExpiredRun()
    {
        RunRecord run = CreateRun(
            Now - TimeSpan.FromHours(6) - TimeSpan.FromMilliseconds(1));

        AssertRejected(
            RunScoreRejectionReason.RunOutsideAcceptedTimeWindow,
            () => validator.Validate(
                run,
                new RunScoreSubmission("player-1", "run-1", 10),
                Now));
    }

    [Test]
    public void Validate_AcceptsScoreAtSpeedBoundaryIncludingGrace()
    {
        RunRecord run = CreateRun(Now - TimeSpan.FromSeconds(8));

        Assert.DoesNotThrow(() => validator.Validate(
            run,
            new RunScoreSubmission("player-1", "run-1", 120),
            Now));
    }

    [Test]
    public void Validate_RejectsScoreFasterThanGameplayAllows()
    {
        RunRecord run = CreateRun(
            Now - TimeSpan.FromSeconds(8) + TimeSpan.FromMilliseconds(1));

        AssertRejected(
            RunScoreRejectionReason.ScoreIncreasedTooQuickly,
            () => validator.Validate(
                run,
                new RunScoreSubmission("player-1", "run-1", 120),
                Now));
    }

    [Test]
    public void Validate_RejectsInvalidStoredStartTime()
    {
        RunRecord run = CreateRun(Now);
        run.StartedAtUnixMilliseconds = long.MaxValue;

        AssertRejected(
            RunScoreRejectionReason.InvalidStartTime,
            () => validator.Validate(
                run,
                new RunScoreSubmission("player-1", "run-1", 10),
                Now));
    }

    [Test]
    public void Validate_DoesNotMutateRunRecord()
    {
        RunRecord run = CreateRun(Now - TimeSpan.FromMinutes(1));

        validator.Validate(
            run,
            new RunScoreSubmission("player-1", "run-1", 10),
            Now);

        Assert.That(run.Consumed, Is.False);
    }

    [Test]
    public void Constructor_RejectsInvalidPolicy()
    {
        var invalidPolicy = RunScoreValidationPolicy.Default with
        {
            MaximumScorePerSecond = 0d
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunScoreValidator(invalidPolicy));
    }

    private static RunRecord CreateRun(DateTimeOffset startedAt)
    {
        return RunRecord.Start("player-1", startedAt, "run-1");
    }

    private static void AssertRejected(
        RunScoreRejectionReason reason,
        TestDelegate action)
    {
        RunScoreValidationException? exception =
            Assert.Throws<RunScoreValidationException>(action);

        Assert.That(exception!.Reason, Is.EqualTo(reason));
    }
}
