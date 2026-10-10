using System;

namespace CrossyRoadServer;

public sealed class RunRecord
{
    public string RunId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public long StartedAtUnixMilliseconds { get; set; }
    public bool Consumed { get; set; }

    public static RunRecord Start(
        string playerId,
        DateTimeOffset startedAt,
        string? runId = null)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException(
                "A player ID is required.",
                nameof(playerId));
        }

        string resolvedRunId = runId ?? Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(resolvedRunId))
        {
            throw new ArgumentException(
                "A run ID is required.",
                nameof(runId));
        }

        return new RunRecord
        {
            RunId = resolvedRunId,
            PlayerId = playerId,
            StartedAtUnixMilliseconds = startedAt.ToUnixTimeMilliseconds(),
            Consumed = false
        };
    }
}

public sealed record RunScoreSubmission(
    string PlayerId,
    string RunId,
    int Score);

public sealed record RunScoreValidationPolicy(
    int MinimumAcceptedScore,
    int MaximumAcceptedScore,
    double MaximumScorePerSecond,
    TimeSpan TimingGrace,
    TimeSpan MaximumRunAge)
{
    public static RunScoreValidationPolicy Default { get; } = new(
        MinimumAcceptedScore: 1,
        MaximumAcceptedScore: 100_000,
        MaximumScorePerSecond: 12d,
        TimingGrace: TimeSpan.FromSeconds(2),
        MaximumRunAge: TimeSpan.FromHours(6));

    public void EnsureValid()
    {
        if (MinimumAcceptedScore < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumAcceptedScore));
        }

        if (MaximumAcceptedScore < MinimumAcceptedScore)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumAcceptedScore));
        }

        if (MaximumScorePerSecond <= 0d ||
            double.IsNaN(MaximumScorePerSecond) ||
            double.IsInfinity(MaximumScorePerSecond))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumScorePerSecond));
        }

        if (TimingGrace < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(TimingGrace));
        }

        if (MaximumRunAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumRunAge));
        }
    }
}

public enum RunScoreRejectionReason
{
    InvalidPlayerId,
    InvalidRunId,
    InvalidScore,
    PlayerMismatch,
    RunAlreadyConsumed,
    InvalidStartTime,
    RunOutsideAcceptedTimeWindow,
    ScoreIncreasedTooQuickly
}

public sealed class RunScoreValidationException : InvalidOperationException
{
    public RunScoreValidationException(
        RunScoreRejectionReason reason,
        string message)
        : base(message)
    {
        Reason = reason;
    }

    public RunScoreValidationException(
        RunScoreRejectionReason reason,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public RunScoreRejectionReason Reason { get; }
}

public sealed class RunScoreValidator
{
    private readonly RunScoreValidationPolicy policy;

    public RunScoreValidator(RunScoreValidationPolicy? policy = null)
    {
        this.policy = policy ?? RunScoreValidationPolicy.Default;
        this.policy.EnsureValid();
    }

    public void Validate(
        RunRecord run,
        RunScoreSubmission submission,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(submission);

        ValidateIdentity(run, submission);
        ValidateScore(submission.Score);

        if (run.Consumed)
        {
            Reject(
                RunScoreRejectionReason.RunAlreadyConsumed,
                "This run has already been submitted.");
        }

        DateTimeOffset startedAt;
        try
        {
            startedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                run.StartedAtUnixMilliseconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new RunScoreValidationException(
                RunScoreRejectionReason.InvalidStartTime,
                "The run start time is invalid.",
                exception);
        }

        TimeSpan elapsed = now - startedAt;
        if (elapsed < TimeSpan.Zero || elapsed > policy.MaximumRunAge)
        {
            Reject(
                RunScoreRejectionReason.RunOutsideAcceptedTimeWindow,
                "This run is outside the accepted time window.");
        }

        double minimumSeconds = Math.Max(
            0d,
            submission.Score / policy.MaximumScorePerSecond -
            policy.TimingGrace.TotalSeconds);

        if (elapsed.TotalSeconds < minimumSeconds)
        {
            Reject(
                RunScoreRejectionReason.ScoreIncreasedTooQuickly,
                "The submitted score increased faster than gameplay allows.");
        }
    }

    private void ValidateIdentity(
        RunRecord run,
        RunScoreSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.PlayerId))
        {
            Reject(
                RunScoreRejectionReason.InvalidPlayerId,
                "An authenticated player is required.");
        }

        if (string.IsNullOrWhiteSpace(submission.RunId) ||
            !string.Equals(
                run.RunId,
                submission.RunId,
                StringComparison.Ordinal))
        {
            Reject(
                RunScoreRejectionReason.InvalidRunId,
                "The RunId is invalid.");
        }

        if (string.IsNullOrWhiteSpace(run.PlayerId) ||
            !string.Equals(
                run.PlayerId,
                submission.PlayerId,
                StringComparison.Ordinal))
        {
            Reject(
                RunScoreRejectionReason.PlayerMismatch,
                "This run does not belong to the authenticated player.");
        }
    }

    private void ValidateScore(int score)
    {
        if (score < policy.MinimumAcceptedScore ||
            score > policy.MaximumAcceptedScore)
        {
            Reject(
                RunScoreRejectionReason.InvalidScore,
                $"Score must be between " +
                $"{policy.MinimumAcceptedScore} and " +
                $"{policy.MaximumAcceptedScore}.");
        }
    }

    private static void Reject(
        RunScoreRejectionReason reason,
        string message)
    {
        throw new RunScoreValidationException(reason, message);
    }
}
