using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapFindingTests
{
    [Theory]
    [InlineData(PpapFindingOutcome.Failed)]
    [InlineData(PpapFindingOutcome.NotEvaluated)]
    [InlineData(PpapFindingOutcome.Error)]
    public void Create_Should_PersistDistinctOutcomeAndDocumentCorrelation(PpapFindingOutcome outcome)
    {
        Guid submissionDocumentId = Guid.NewGuid();
        Guid canonicalDocumentId = Guid.NewGuid();
        Guid extractionAttemptId = Guid.NewGuid();
        DateTime evaluatedAtUtc = DateTime.UtcNow;

        PpapFinding finding = PpapFinding.Create(
            " tenant-1 ", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " RULE-1 ", " Rule name ",
            "psw", outcome, " configured message ", " reason ", "[]", evaluatedAtUtc,
            submissionDocumentId, canonicalDocumentId, extractionAttemptId);

        finding.TenantId.ShouldBe("tenant-1");
        finding.Outcome.ShouldBe(outcome);
        finding.DocumentCode.ShouldBe("PSW");
        finding.SubmissionDocumentId.ShouldBe(submissionDocumentId);
        finding.CanonicalDocumentId.ShouldBe(canonicalDocumentId);
        finding.ExtractionAttemptId.ShouldBe(extractionAttemptId);
        finding.Message.ShouldBe("configured message");
        finding.Reason.ShouldBe("reason");
        finding.EvaluatedAtUtc.ShouldBe(evaluatedAtUtc);
    }

    [Fact]
    public void Create_Should_AllowCrossDocumentFindingWithoutDocumentCorrelation()
    {
        PpapFinding finding = PpapFinding.Create(
            "tenant-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "RULE-1", "Rule", null,
            PpapFindingOutcome.Failed, null, null, "[]", DateTime.UtcNow);

        finding.DocumentCode.ShouldBeNull();
        finding.SubmissionDocumentId.ShouldBeNull();
        finding.CanonicalDocumentId.ShouldBeNull();
        finding.ExtractionAttemptId.ShouldBeNull();
    }

    [Fact]
    public void Create_Should_RejectDocumentFindingWithoutCanonicalCorrelation()
    {
        Should.Throw<ArgumentException>(() => PpapFinding.Create(
            "tenant-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "RULE-1", "Rule", "PSW",
            PpapFindingOutcome.Error, null, null, "[]", DateTime.UtcNow));
    }
}
