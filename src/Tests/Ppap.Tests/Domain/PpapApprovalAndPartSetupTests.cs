using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Features.v1.Submissions.DecidePpapSubmission;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapApprovalAndPartSetupTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    private static PpapSubmission Submitted()
    {
        PpapSubmission submission = PpapSubmission.CreateDraft("tenant-1", Guid.NewGuid(), "Part", "PN-100", "A", 3);
        PpapSubmissionDocument document = submission.AddDocumentSlot(PpapDocumentCodes.Psw);
        submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true);
        submission.Submit();
        return submission;
    }

    [Fact]
    public void Decide_Should_ApproveSubmittedSubmission()
    {
        PpapSubmission submission = Submitted();
        Guid approver = Guid.NewGuid();
        int version = submission.Version;

        submission.Decide(SubmissionDecision.Approved, "  Looks good  ", approver, version, Now);

        submission.Decision.ShouldBe(SubmissionDecision.Approved);
        submission.DecisionComment.ShouldBe("Looks good");
        submission.DecidedByUserId.ShouldBe(approver);
        submission.DecidedAtUtc.ShouldBe(Now);
        submission.Version.ShouldBe(version + 1);
    }

    [Fact]
    public void Decide_Should_AllowInterimThenFinalApproval()
    {
        PpapSubmission submission = Submitted();
        submission.Decide(SubmissionDecision.InterimApproved, "Pending capability study", Guid.NewGuid(), submission.Version, Now);
        submission.Decide(SubmissionDecision.Approved, null, Guid.NewGuid(), submission.Version, Now);
        submission.Decision.ShouldBe(SubmissionDecision.Approved);
    }

    [Theory]
    [InlineData(SubmissionDecision.Approved)]
    [InlineData(SubmissionDecision.Rejected)]
    public void Decide_Should_RejectChangesAfterFinalDecision(SubmissionDecision final)
    {
        PpapSubmission submission = Submitted();
        submission.Decide(final, "Final", Guid.NewGuid(), submission.Version, Now);
        Should.Throw<CustomException>(() => submission.Decide(SubmissionDecision.InterimApproved, "Again", Guid.NewGuid(), submission.Version, Now));
    }

    [Fact]
    public void Decide_Should_RequireSubmittedStatus()
    {
        PpapSubmission draft = PpapSubmission.CreateDraft("tenant-1", Guid.NewGuid(), "Part", "PN-100", "A", 3);
        Should.Throw<CustomException>(() => draft.Decide(SubmissionDecision.Approved, null, Guid.NewGuid(), draft.Version, Now));
    }

    [Theory]
    [InlineData(SubmissionDecision.Rejected)]
    [InlineData(SubmissionDecision.InterimApproved)]
    public void Decide_Should_RequireComment_ForRejectAndInterim(SubmissionDecision decision)
    {
        PpapSubmission submission = Submitted();
        Should.Throw<ArgumentException>(() => submission.Decide(decision, "  ", Guid.NewGuid(), submission.Version, Now));
    }

    [Fact]
    public void Decide_Should_RejectStaleVersion()
    {
        PpapSubmission submission = Submitted();
        Should.Throw<CustomException>(() => submission.Decide(SubmissionDecision.Approved, null, Guid.NewGuid(), submission.Version - 1, Now));
    }

    [Theory]
    [InlineData("Approved", null, true)]
    [InlineData("rejected", "Missing PFMEA", true)]
    [InlineData("Rejected", null, false)]
    [InlineData("InterimApproved", "", false)]
    [InlineData("None", null, false)]
    [InlineData("2", null, false)]
    public void DecisionValidator_Should_ValidateDecisionAndComment(string decision, string? comment, bool valid)
    {
        DecidePpapSubmissionCommandValidator validator = new();
        validator.Validate(new DecidePpapSubmissionCommand(Guid.NewGuid(), decision, comment, 1)).IsValid.ShouldBe(valid);
    }

    [Fact]
    public void Part_Should_StoreLevelAndClassification()
    {
        PpapPart part = PpapPart.Create("tenant-1", Guid.NewGuid(), "Part", "PN-1", "A", null, 5, PartClassification.Safety);
        part.PpapLevel.ShouldBe(5);
        part.Classification.ShouldBe(PartClassification.Safety);

        part.UpdateDetails("Part", "PN-1", "B", null, 2, PartClassification.Critical, part.Version);
        part.PpapLevel.ShouldBe(2);
        part.Classification.ShouldBe(PartClassification.Critical);

        part.UpdateDetails("Part", "PN-1", "C", null, part.Version);
        part.PpapLevel.ShouldBe(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Part_Should_RejectInvalidLevel(int level)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PpapPart.Create("tenant-1", Guid.NewGuid(), "Part", "PN-1", "A", null, level));
    }

    [Theory]
    [InlineData("Germany", PpapRegions.Europe)]
    [InlineData("usa", PpapRegions.NorthAmerica)]
    [InlineData("Mexico", PpapRegions.LatinAmerica)]
    [InlineData("China", PpapRegions.AsiaPacific)]
    [InlineData("Atlantis", PpapRegions.Other)]
    [InlineData(null, null)]
    public void Regions_Should_MapCountries(string? country, string? region)
    {
        PpapRegions.FromCountry(country).ShouldBe(region);
    }

    [Fact]
    public void Supplier_Should_NormalizeTierGrade_AndDeriveRegion()
    {
        PpapSupplier supplier = PpapSupplier.Create("acme", "VND-000001", new PpapSupplierProfile("S", Country: "Japan", TierLevel: "tier 1", QualityGrade: "a+"), [], [], Now);
        supplier.TierLevel.ShouldBe("Tier 1");
        supplier.QualityGrade.ShouldBe("A+");
        supplier.Region.ShouldBe(PpapRegions.AsiaPacific);
        Should.Throw<ArgumentException>(() => PpapSupplier.Create("acme", "VND-000002", new PpapSupplierProfile("S", TierLevel: "Tier 9"), [], [], Now));
    }
}
