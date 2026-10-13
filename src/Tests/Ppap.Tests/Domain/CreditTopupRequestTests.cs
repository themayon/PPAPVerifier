using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class CreditTopupRequestTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void Create_Should_InitializePendingRequest_When_PackageIsValid(int credits)
    {
        Guid ownerUserId = Guid.NewGuid();
        DateTime before = DateTime.UtcNow;

        CreditTopupRequest request = CreditTopupRequest.Create(
            " tenant-1 ", ownerUserId, credits, " payment-1 ", " note ");

        request.Id.ShouldNotBe(Guid.Empty);
        request.TenantId.ShouldBe("tenant-1");
        request.OwnerUserId.ShouldBe(ownerUserId);
        request.CreditsRequested.ShouldBe(credits);
        request.PaymentReference.ShouldBe("payment-1");
        request.Note.ShouldBe("note");
        request.Status.ShouldBe(CreditTopupRequestStatus.Pending);
        request.RequestedAt.ShouldBeInRange(before, DateTime.UtcNow);
        request.DecidedAt.ShouldBeNull();
        request.DecidedByUserId.ShouldBeNull();
        request.DecisionNote.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(101)]
    public void Create_Should_RejectCredits_When_PackageIsNotFixed(int credits)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            CreditTopupRequest.Create("tenant-1", Guid.NewGuid(), credits, null, null));
    }

    [Fact]
    public void Create_Should_RejectValues_When_OptionalTextExceedsMaximumLength()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CreditTopupRequest.Create(
            "tenant-1",
            Guid.NewGuid(),
            10,
            new string('p', CreditTopupRequest.MaxPaymentReferenceLength + 1),
            null));
        Should.Throw<ArgumentOutOfRangeException>(() => CreditTopupRequest.Create(
            "tenant-1",
            Guid.NewGuid(),
            10,
            null,
            new string('n', CreditTopupRequest.MaxNoteLength + 1)));
    }

    [Fact]
    public void Create_Should_RejectRequest_When_TenantOrOwnerIsMissing()
    {
        Should.Throw<ArgumentException>(() =>
            CreditTopupRequest.Create(" ", Guid.NewGuid(), 10, null, null));
        Should.Throw<ArgumentException>(() =>
            CreditTopupRequest.Create("tenant-1", Guid.Empty, 10, null, null));
    }

    [Fact]
    public void Approve_Should_RecordDecision_When_RequestIsPending()
    {
        CreditTopupRequest request = CreateRequest();
        Guid operatorId = Guid.NewGuid();
        DateTime before = DateTime.UtcNow;

        request.Approve(operatorId, " paid ");

        request.Status.ShouldBe(CreditTopupRequestStatus.Approved);
        request.DecidedByUserId.ShouldBe(operatorId);
        request.DecisionNote.ShouldBe("paid");
        request.DecidedAt.ShouldNotBeNull();
        request.DecidedAt.Value.ShouldBeInRange(before, DateTime.UtcNow);
    }

    [Fact]
    public void Reject_Should_RecordDecision_When_RequestIsPending()
    {
        CreditTopupRequest request = CreateRequest();
        Guid operatorId = Guid.NewGuid();

        request.Reject(operatorId, " declined ");

        request.Status.ShouldBe(CreditTopupRequestStatus.Rejected);
        request.DecidedByUserId.ShouldBe(operatorId);
        request.DecisionNote.ShouldBe("declined");
        request.DecidedAt.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(CreditTopupRequestStatus.Approved)]
    [InlineData(CreditTopupRequestStatus.Rejected)]
    public void Decision_Should_BeIdempotent_When_ExactDecisionIsReplayed(CreditTopupRequestStatus status)
    {
        CreditTopupRequest request = CreateRequest();
        Guid operatorId = Guid.NewGuid();
        Decide(request, status, operatorId, "decision");
        DateTime? decidedAt = request.DecidedAt;

        Decide(request, status, operatorId, " decision ");

        request.DecidedAt.ShouldBe(decidedAt);
        request.Status.ShouldBe(status);
    }

    [Theory]
    [InlineData(CreditTopupRequestStatus.Approved, CreditTopupRequestStatus.Rejected)]
    [InlineData(CreditTopupRequestStatus.Rejected, CreditTopupRequestStatus.Approved)]
    public void Decision_Should_ThrowConflict_When_OppositeDecisionIsAttempted(
        CreditTopupRequestStatus initial,
        CreditTopupRequestStatus attempted)
    {
        CreditTopupRequest request = CreateRequest();
        Guid operatorId = Guid.NewGuid();
        Decide(request, initial, operatorId, "decision");

        CustomException exception = Should.Throw<CustomException>(() =>
            Decide(request, attempted, operatorId, "decision"));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        request.Status.ShouldBe(initial);
    }

    [Fact]
    public void Approve_Should_ThrowConflict_When_ReplayedWithDifferentDecisionDetails()
    {
        CreditTopupRequest request = CreateRequest();
        request.Approve(Guid.NewGuid(), "decision");

        CustomException exception = Should.Throw<CustomException>(() =>
            request.Approve(Guid.NewGuid(), "different"));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Decision_Should_RejectNote_When_ItExceedsMaximumLength()
    {
        CreditTopupRequest request = CreateRequest();

        Should.Throw<ArgumentOutOfRangeException>(() => request.Approve(
            Guid.NewGuid(),
            new string('d', CreditTopupRequest.MaxDecisionNoteLength + 1)));
        request.Status.ShouldBe(CreditTopupRequestStatus.Pending);
    }

    private static CreditTopupRequest CreateRequest() =>
        CreditTopupRequest.Create("tenant-1", Guid.NewGuid(), 50, null, null);

    private static void Decide(
        CreditTopupRequest request,
        CreditTopupRequestStatus status,
        Guid operatorId,
        string? note)
    {
        if (status == CreditTopupRequestStatus.Approved)
        {
            request.Approve(operatorId, note);
        }
        else
        {
            request.Reject(operatorId, note);
        }
    }
}
