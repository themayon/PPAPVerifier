using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Processing;
using DES.Modules.Rules.Contracts;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Processing;

public sealed class PpapRuleEvaluationJobTests
{
    private const string TenantId = "tenant-1";
    private static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void BuildRequest_Should_MapMetadataAndDocumentsByDocumentCode()
    {
        EvaluationData data = CreateData(withTemplate: true, "PSW", "DFMEA");
        CanonicalDocumentDto[] canonicalDocuments = data.Documents
            .Reverse()
            .Select((document, index) => Canonical(document, "1.0", $"{{\"index\":{index}}}"))
            .ToArray();

        RuleEvaluationRequest request = PpapRuleEvaluationJob.BuildRequest(
            data.Job,
            data.Submission,
            data.Documents,
            canonicalDocuments);

        request.TemplateVersionId.ShouldBe(data.Submission.RuleTemplateVersionId!.Value);
        request.SubjectId.ShouldBe(data.Submission.Id);
        request.OwnerUserId.ShouldBe(OwnerUserId);
        request.InputSchemaVersion.ShouldBe("1.0");
        request.Documents.Keys.ShouldBe(new[] { "DFMEA", "PSW" });
        request.Documents["DFMEA"].ShouldBe("{\"index\":0}");
        request.Documents["PSW"].ShouldBe("{\"index\":1}");
    }

    [Fact]
    public void BuildRequest_Should_RejectMissingSnapshottedTemplateVersion()
    {
        EvaluationData data = CreateData(withTemplate: false, "PSW");
        CanonicalDocumentDto[] canonicalDocuments = data.Documents
            .Select(document => Canonical(document, "1.0", "{}"))
            .ToArray();

        CustomException exception = Should.Throw<CustomException>(() => PpapRuleEvaluationJob.BuildRequest(
            data.Job,
            data.Submission,
            data.Documents,
            canonicalDocuments));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        exception.Message.ShouldContain("snapshotted rule template version");
    }

    [Fact]
    public void BuildRequest_Should_RejectMismatchedCanonicalSchemaVersions()
    {
        EvaluationData data = CreateData(withTemplate: true, "PSW", "DFMEA");
        CanonicalDocumentDto[] canonicalDocuments =
        [
            Canonical(data.Documents[0], "1.0", "{}"),
            Canonical(data.Documents[1], "2.0", "{}")
        ];

        CustomException exception = Should.Throw<CustomException>(() => PpapRuleEvaluationJob.BuildRequest(
            data.Job,
            data.Submission,
            data.Documents,
            canonicalDocuments));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        exception.Message.ShouldContain("same schema version");
    }

    [Fact]
    public void BuildRequest_Should_RejectCanonicalDocumentFromDifferentExtractionAttempt()
    {
        EvaluationData data = CreateData(withTemplate: true, "PSW");
        PpapSubmissionDocument document = data.Documents.Single();
        CanonicalDocumentDto canonicalDocument = Canonical(document, "1.0", "{}") with
        {
            ExtractionAttemptId = Guid.CreateVersion7()
        };

        CustomException exception = Should.Throw<CustomException>(() => PpapRuleEvaluationJob.BuildRequest(
            data.Job,
            data.Submission,
            data.Documents,
            [canonicalDocument]));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        exception.Message.ShouldContain("does not correlate");
    }

    private static EvaluationData CreateData(bool withTemplate, params string[] documentCodes)
    {
        Guid? templateId = withTemplate ? Guid.CreateVersion7() : null;
        Guid? templateVersionId = withTemplate ? Guid.CreateVersion7() : null;
        PpapSubmission submission = PpapSubmission.CreateDraft(
            TenantId,
            OwnerUserId,
            "Part",
            "PN-100",
            "A",
            3,
            templateId,
            templateVersionId);

        foreach (string documentCode in documentCodes)
        {
            PpapSubmissionDocument document = submission.AddDocumentSlot(documentCode);
            submission.ApplyFinalizedFile(document.Id, Guid.CreateVersion7(), true);
            document.MarkExtractionRequestPublished(Guid.CreateVersion7(), DateTime.UtcNow);
            document.RecordExtractionCompleted(
                TenantId,
                documentCode,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                null,
                null,
                DateTime.UtcNow);
        }
        submission.Submit();

        PpapJob job = PpapJob.CreateQueued(TenantId, submission.Id, submission.Version, OwnerUserId);
        job.Start();
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 20);
        job.AdvanceStage(PpapJobStage.DocumentClassification, 40);
        job.MarkRuleEvaluationQueued();
        return new EvaluationData(submission, job, submission.Documents.ToArray());
    }

    private static CanonicalDocumentDto Canonical(
        PpapSubmissionDocument document,
        string schemaVersion,
        string canonicalJson) => new(
            document.CanonicalDocumentId!.Value,
            document.ExtractionAttemptId!.Value,
            schemaVersion,
            canonicalJson,
            null,
            null,
            DateTime.UtcNow);

    private sealed record EvaluationData(
        PpapSubmission Submission,
        PpapJob Job,
        PpapSubmissionDocument[] Documents);
}
