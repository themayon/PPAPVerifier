using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapWorkingFindingAndWaiverRequestTests
{
    [Fact]
    public void Finding_Should_RequireBothDocumentCorrelationIdentifiers()
    {
        Should.Throw<ArgumentException>(() => PpapWorkingFinding.Create(
            "tenant-1",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            "DIMR-001",
            "Nominal comparison",
            WorkingFindingStatus.Failed,
            "Nominal value differs.",
            "fingerprint",
            "[]"));
    }

    [Fact]
    public void WaiverRequest_Should_TransitionFromPendingToApproved()
    {
        PpapWaiverRequest request = PpapWaiverRequest.Create(
            "tenant-1",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fingerprint",
            Guid.CreateVersion7(),
            "Customer-approved deviation.",
            DateTime.UtcNow);

        request.Decide(true, Guid.CreateVersion7(), "Approved for this revision.", 0, DateTime.UtcNow);

        request.Status.ShouldBe(WaiverRequestStatus.Approved);
        request.Version.ShouldBe(1);
        request.ReviewedByUserId.ShouldNotBeNull();
    }

    [Fact]
    public void WaiverRequest_Should_RejectStaleVersion()
    {
        PpapWaiverRequest request = PpapWaiverRequest.Create(
            "tenant-1",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "fingerprint",
            Guid.CreateVersion7(),
            "Deviation request.",
            DateTime.UtcNow);

        Should.Throw<CustomException>(() => request.Decide(
            false,
            Guid.CreateVersion7(),
            "Rejected.",
            1,
            DateTime.UtcNow));
    }
}
