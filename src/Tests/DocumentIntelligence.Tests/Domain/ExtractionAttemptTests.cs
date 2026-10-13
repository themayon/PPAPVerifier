using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Domain;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Domain;

public sealed class ExtractionAttemptTests
{
    [Fact]
    public void Create_Should_NormalizeAndInitialize_When_InputIsValid()
    {
        ExtractionAttempt attempt = CreateAttempt("ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD");

        attempt.Id.ShouldNotBe(Guid.Empty);
        attempt.TenantId.ShouldBe("tenant-1");
        attempt.DocumentCode.ShouldBe("PSW");
        attempt.SourceSha256.ShouldBe("abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd");
        attempt.Status.ShouldBe(ExtractionAttemptStatus.Pending);
        attempt.AttemptCount.ShouldBe(0);
        attempt.Version.ShouldBe(0);
    }

    [Fact]
    public void Create_Should_Throw_When_SourceHashIsInvalid()
    {
        Should.Throw<ArgumentException>(() => CreateAttempt("not-a-hash"));
    }

    [Fact]
    public void Create_Should_Throw_When_RequiredIdIsEmpty()
    {
        Should.Throw<ArgumentException>(() => ExtractionAttempt.Create(
            "tenant-1", Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "PSW",
            new string('a', 64), "key", "provider", "model", "v1"));
    }

    [Fact]
    public void Lifecycle_Should_SucceedAndIncrementVersion_When_TransitionsAreValid()
    {
        ExtractionAttempt attempt = CreateAttempt();

        attempt.Claim();
        attempt.MarkSubmitted(" request-1 ");
        attempt.MarkProcessing();
        attempt.Succeed();

        attempt.Status.ShouldBe(ExtractionAttemptStatus.Succeeded);
        attempt.AttemptCount.ShouldBe(1);
        attempt.ProviderRequestId.ShouldBe("request-1");
        attempt.ClaimedAtUtc.ShouldNotBeNull();
        attempt.SubmittedAtUtc.ShouldNotBeNull();
        attempt.StartedAtUtc.ShouldNotBeNull();
        attempt.CompletedAtUtc.ShouldNotBeNull();
        attempt.Version.ShouldBe(4);
    }

    [Fact]
    public void TerminalReplay_Should_BeIdempotent_When_PayloadMatches()
    {
        ExtractionAttempt attempt = CreateAttempt();
        attempt.Claim();
        attempt.MarkSubmitted("request-1");
        attempt.MarkProcessing();
        attempt.Fail("PROVIDER/ERROR", "Provider\r\n failed");
        int version = attempt.Version;
        DateTime? completedAt = attempt.CompletedAtUtc;

        attempt.Fail("PROVIDER/ERROR", "Provider\r\n failed");

        attempt.ErrorCode.ShouldBe("PROVIDERERROR");
        attempt.ErrorMessage.ShouldBe("Provider failed");
        attempt.Version.ShouldBe(version);
        attempt.CompletedAtUtc.ShouldBe(completedAt);
    }

    [Fact]
    public void TerminalReplay_Should_ThrowConflict_When_PayloadDiffers()
    {
        ExtractionAttempt attempt = CreateAttempt();
        attempt.Claim();
        attempt.Fail("FAILED", "First failure");

        CustomException exception = Should.Throw<CustomException>(() => attempt.Fail("FAILED", "Different failure"));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Requeue_Should_ClearTransientStateAndKeepAttemptCountMonotonic()
    {
        ExtractionAttempt attempt = CreateAttempt();
        attempt.Claim();
        attempt.MarkSubmitted("request-1");
        attempt.MarkTimedOut("TIMEOUT", "Provider timed out");

        attempt.Requeue();
        attempt.Claim();

        attempt.Status.ShouldBe(ExtractionAttemptStatus.Claimed);
        attempt.AttemptCount.ShouldBe(2);
        attempt.ProviderRequestId.ShouldBeNull();
        attempt.ErrorCode.ShouldBeNull();
        attempt.ErrorMessage.ShouldBeNull();
        attempt.SubmittedAtUtc.ShouldBeNull();
        attempt.CompletedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Claim_Should_ThrowConflict_When_AttemptIsNotPending()
    {
        ExtractionAttempt attempt = CreateAttempt();
        attempt.Claim();

        CustomException exception = Should.Throw<CustomException>(() => attempt.Claim());

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void RenewLease_Should_UpdateOwnedLeaseAndRejectAnotherWorker()
    {
        ExtractionAttempt attempt = CreateAttempt();
        DateTime initialExpiry = DateTime.UtcNow.AddMinutes(10);
        attempt.Claim("worker-1", initialExpiry);

        DateTime renewedExpiry = DateTime.UtcNow.AddMinutes(20);
        attempt.RenewLease("worker-1", renewedExpiry);

        attempt.ClaimedBy.ShouldBe("worker-1");
        attempt.ClaimedUntilUtc.ShouldBe(renewedExpiry);
        Should.Throw<CustomException>(() => attempt.RenewLease("worker-2", DateTime.UtcNow.AddMinutes(30)));
    }

    [Fact]
    public void TerminalTransition_Should_ClearLease()
    {
        ExtractionAttempt attempt = CreateAttempt();
        attempt.Claim("worker-1", DateTime.UtcNow.AddMinutes(10));

        attempt.Succeed();

        attempt.ClaimedBy.ShouldBeNull();
        attempt.ClaimedUntilUtc.ShouldBeNull();
    }

    [Fact]
    public void EventMarkers_Should_BeIdempotentForMatchingTerminalOutcome()
    {
        ExtractionAttempt succeeded = CreateAttempt();
        succeeded.Claim();
        succeeded.Succeed();
        Guid completionEventId = Guid.NewGuid();

        succeeded.MarkCompletionEventPublished(completionEventId).ShouldBeTrue();
        succeeded.MarkCompletionEventPublished(Guid.NewGuid()).ShouldBeFalse();
        succeeded.CompletionEventId.ShouldBe(completionEventId);
        succeeded.CompletionEventPublishedAtUtc.ShouldNotBeNull();

        ExtractionAttempt invalid = CreateAttempt();
        invalid.Claim();
        invalid.MarkInvalidDocument("InvalidDocument", "Invalid document");
        Guid failureEventId = Guid.NewGuid();

        invalid.MarkFailureEventPublished(failureEventId).ShouldBeTrue();
        invalid.MarkFailureEventPublished(Guid.NewGuid()).ShouldBeFalse();
        invalid.FailureEventId.ShouldBe(failureEventId);
        invalid.FailureEventPublishedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void DeadLetter_Should_RequireRetryableTerminalState()
    {
        ExtractionAttempt attempt = CreateAttempt();

        CustomException exception = Should.Throw<CustomException>(() => attempt.DeadLetter("FAILED", "Failure"));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static ExtractionAttempt CreateAttempt(string? hash = null) => ExtractionAttempt.Create(
        " tenant-1 ",
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        " PSW ",
        hash ?? new string('a', 64),
        "idempotency-key",
        "provider",
        "model",
        "v1");
}
