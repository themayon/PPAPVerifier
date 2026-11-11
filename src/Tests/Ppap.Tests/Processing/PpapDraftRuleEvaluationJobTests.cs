using DES.Modules.Ppap.Processing;
using DES.Modules.Rules.Contracts;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Processing;

public sealed class PpapDraftRuleEvaluationJobTests
{
    [Fact]
    public void ComputeFingerprint_Should_BeStable_ForSameInputs()
    {
        Guid submissionId = Guid.NewGuid();
        Guid ruleVersionId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();

        string first = PpapDraftRuleEvaluationJob.ComputeFingerprint(submissionId, ruleVersionId, "PFMEA", revisionId, RuleEvaluationStatus.Failed);
        string second = PpapDraftRuleEvaluationJob.ComputeFingerprint(submissionId, ruleVersionId, "PFMEA", revisionId, RuleEvaluationStatus.Failed);

        first.ShouldBe(second);
        first.Length.ShouldBe(64);
    }

    [Fact]
    public void ComputeFingerprint_Should_Differ_When_RevisionOrStatusChanges()
    {
        Guid submissionId = Guid.NewGuid();
        Guid ruleVersionId = Guid.NewGuid();
        Guid revisionId = Guid.NewGuid();
        string baseline = PpapDraftRuleEvaluationJob.ComputeFingerprint(submissionId, ruleVersionId, "PFMEA", revisionId, RuleEvaluationStatus.Failed);

        PpapDraftRuleEvaluationJob.ComputeFingerprint(submissionId, ruleVersionId, "PFMEA", Guid.NewGuid(), RuleEvaluationStatus.Failed).ShouldNotBe(baseline);
        PpapDraftRuleEvaluationJob.ComputeFingerprint(submissionId, ruleVersionId, "PFMEA", revisionId, RuleEvaluationStatus.Error).ShouldNotBe(baseline);
        PpapDraftRuleEvaluationJob.ComputeFingerprint(Guid.NewGuid(), ruleVersionId, "PFMEA", revisionId, RuleEvaluationStatus.Failed).ShouldNotBe(baseline);
    }

    [Theory]
    [InlineData("Configured message", "reason", RuleEvaluationStatus.Failed, "Configured message")]
    [InlineData(null, "Value missing", RuleEvaluationStatus.Failed, "Overdue actions: Value missing")]
    [InlineData(null, null, RuleEvaluationStatus.Failed, "Overdue actions failed.")]
    [InlineData(null, null, RuleEvaluationStatus.Error, "Overdue actions could not be evaluated.")]
    public void BuildMessage_Should_PreferConfiguredMessage_ThenReason_ThenDefault(string? message, string? reason, RuleEvaluationStatus status, string expected)
    {
        var result = new RuleEvaluationPreviewResult(Guid.NewGuid(), Guid.NewGuid(), "RULE-1", "Overdue actions", "PFMEA", status, reason, message, "[]");

        PpapDraftRuleEvaluationJob.BuildMessage(result).ShouldBe(expected);
    }
}
