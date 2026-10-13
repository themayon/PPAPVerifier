using System.Net;
using System.Text.Json;
using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Processing;
using DES.Modules.Rules.Contracts;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Processing;

public sealed class PpapReportGenerationJobTests
{
    private const string TenantId = "tenant-1";
    private static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime CreatedAtUtc = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime CompletedAtUtc = new(2025, 1, 2, 3, 5, 5, DateTimeKind.Utc);

    [Fact]
    public void BuildReproducibilityManifest_Should_ProduceStableSortedCompleteSnapshot()
    {
        ManifestData data = CreateData();
        RuleEvaluationRunDetails reversedEvaluation = data.Evaluation with
        {
            Results = data.Evaluation.Results.Reverse().ToArray()
        };

        string first = PpapReportGenerationJob.BuildReproducibilityManifest(
            data.Job,
            data.Submission,
            reversedEvaluation,
            data.Provenance.Reverse().ToArray());
        string second = PpapReportGenerationJob.BuildReproducibilityManifest(
            data.Job,
            data.Submission,
            data.Evaluation,
            data.Provenance);
        PpapReproducibilityManifestDto manifest = JsonSerializer.Deserialize<PpapReproducibilityManifestDto>(
            first,
            JsonSerializerOptions.Web)!;

        first.ShouldBe(second);
        manifest.SchemaVersion.ShouldBe("1.0");
        manifest.JobId.ShouldBe(data.Job.Id);
        manifest.SubmissionId.ShouldBe(data.Submission.Id);
        manifest.SubmissionVersion.ShouldBe(data.Submission.Version);
        manifest.TemplateVersionId.ShouldBe(data.Submission.RuleTemplateVersionId!.Value);
        manifest.Evaluation.RunId.ShouldBe(data.Evaluation.Id);
        manifest.Evaluation.Version.ShouldBe(7);
        manifest.Evaluation.InputSchemaVersion.ShouldBe("canonical-1.0");
        manifest.Evaluation.Status.ShouldBe(nameof(EvaluationRunStatus.Completed));
        manifest.Evaluation.CreatedAtUtc.ShouldBe(CreatedAtUtc);
        manifest.Evaluation.CompletedAtUtc.ShouldBe(CompletedAtUtc);
        manifest.Evaluation.Results.Select(x => x.StableRuleCode).ShouldBe(["RULE-A", "RULE-Z"]);
        manifest.Evaluation.Results.Single(x => x.StableRuleCode == "RULE-A").DocumentCode.ShouldBe("PSW");
        manifest.Documents.Select(x => x.DocumentCode).ShouldBe(["DFMEA", "PFD", "PSW"]);

        PpapManifestDocumentDto notApplicable = manifest.Documents.Single(x => x.DocumentCode == "DFMEA");
        notApplicable.Disposition.ShouldBe(nameof(SubmissionDocumentDisposition.Submit));
        notApplicable.Status.ShouldBe(nameof(SubmissionDocumentStatus.NotApplicable));
        notApplicable.SourceFileId.ShouldBeNull();
        notApplicable.NotApplicableReason.ShouldBe("Not required by customer");
        notApplicable.Extraction.ShouldBeNull();

        PpapManifestDocumentDto retained = manifest.Documents.Single(x => x.DocumentCode == "PFD");
        retained.Disposition.ShouldBe(nameof(SubmissionDocumentDisposition.Retain));
        retained.Status.ShouldBe(nameof(SubmissionDocumentStatus.PendingUpload));
        retained.SourceFileId.ShouldBeNull();
        retained.NotApplicableReason.ShouldBeNull();
        retained.Extraction.ShouldBeNull();

        PpapManifestDocumentDto available = manifest.Documents.Single(x => x.DocumentCode == "PSW");
        available.SourceFileId.ShouldBe(data.SourceFileId);
        available.Extraction!.SourceSha256.ShouldBe(new string('a', 64));
        available.Extraction.AttemptId.ShouldBe(data.AttemptId);
        available.Extraction.Provider.ShouldBe("provider");
        available.Extraction.Model.ShouldBe("model");
        available.Extraction.ExtractorVersion.ShouldBe("extractor-2.1");
        available.Extraction.CanonicalDocumentId.ShouldBe(data.CanonicalDocumentId);
        available.Extraction.CanonicalSchemaVersion.ShouldBe("canonical-1.0");
        available.Extraction.RawResponseArtifactId.ShouldBe(data.RawArtifactId);
        available.Extraction.GeneratedExcelArtifactId.ShouldBe(data.ExcelArtifactId);
        available.Extraction.CreatedAtUtc.ShouldBe(CreatedAtUtc);
        available.Extraction.CompletedAtUtc.ShouldBe(CompletedAtUtc);

        first.ShouldNotContain("traceJson");
        first.ShouldNotContain("ownerUserId");
        first.ShouldNotContain("message");
        first.ShouldNotContain("reason\":\"rule");
        first.ShouldNotContain("score");
        first.ShouldNotContain("risk");
    }

    [Fact]
    public void BuildReproducibilityManifest_Should_RejectPersistedIdentifierMismatches()
    {
        ManifestData data = CreateData();
        ExtractionProvenanceDto original = data.Provenance.Single();
        ExtractionProvenanceDto[] mismatches =
        [
            original with { ExtractionAttemptId = Guid.CreateVersion7() },
            original with { SubmissionDocumentId = Guid.CreateVersion7() },
            original with { SourceFileAssetId = Guid.CreateVersion7() },
            original with { CanonicalDocumentId = Guid.CreateVersion7() },
            original with { RawResponseArtifactId = Guid.CreateVersion7() },
            original with { GeneratedExcelArtifactId = Guid.CreateVersion7() }
        ];

        foreach (ExtractionProvenanceDto mismatch in mismatches)
        {
            CustomException exception = Should.Throw<CustomException>(() =>
                PpapReportGenerationJob.BuildReproducibilityManifest(
                    data.Job,
                    data.Submission,
                    data.Evaluation,
                    [mismatch]));

            exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public void BuildReproducibilityManifest_Should_RejectSnapshotLargerThan256KiB()
    {
        ManifestData data = CreateData();
        ExtractionProvenanceDto oversized = data.Provenance.Single() with
        {
            Model = new string('x', PpapReport.ReproducibilityManifestMaxBytes)
        };

        CustomException exception = Should.Throw<CustomException>(() =>
            PpapReportGenerationJob.BuildReproducibilityManifest(
                data.Job,
                data.Submission,
                data.Evaluation,
                [oversized]));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        exception.Message.ShouldContain("256 KiB");
    }

    private static ManifestData CreateData()
    {
        Guid templateVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        PpapSubmission submission = PpapSubmission.CreateDraft(
            TenantId,
            OwnerUserId,
            "Part",
            "PN-100",
            "A",
            3,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            templateVersionId);
        PpapSubmissionDocument available = submission.AddDocumentSlot("PSW");
        Guid sourceFileId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Guid attemptId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        Guid canonicalDocumentId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        Guid rawArtifactId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        Guid excelArtifactId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        submission.ApplyFinalizedFile(available.Id, sourceFileId, true);
        available.MarkExtractionRequestPublished(Guid.CreateVersion7(), CreatedAtUtc);
        available.RecordExtractionCompleted(
            TenantId,
            "PSW",
            attemptId,
            canonicalDocumentId,
            rawArtifactId,
            excelArtifactId,
            CompletedAtUtc);
        PpapSubmissionDocument notApplicable = submission.AddDocumentSlot("DFMEA");
        submission.MarkDocumentNotApplicable(notApplicable.Id, "Not required by customer");
        _ = submission.AddDocumentSlot("PFD", SubmissionDocumentDisposition.Retain);
        submission.Submit();

        Guid runId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        PpapJob job = PpapJob.CreateQueued(TenantId, submission.Id, submission.Version, OwnerUserId);
        job.Start();
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 20);
        job.AdvanceStage(PpapJobStage.DocumentClassification, 40);
        job.MarkRuleEvaluationQueued();
        job.MarkRuleEvaluationStarted();
        job.CompleteRuleEvaluation(runId);
        job.AdvanceStage(PpapJobStage.CrossValidation, 60);

        RuleEvaluationResultDetails[] results =
        [
            new(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                templateVersionId,
                "RULE-Z",
                "Global rule",
                null,
                RuleEvaluationStatus.Failed,
                "rule reason",
                "rule message",
                "[{\"secret\":true}]",
                CompletedAtUtc),
            new(
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                templateVersionId,
                "RULE-A",
                "Document rule",
                "PSW",
                RuleEvaluationStatus.Passed,
                null,
                null,
                "[]",
                CreatedAtUtc)
        ];
        var evaluation = new RuleEvaluationRunDetails(
            runId,
            templateVersionId,
            submission.Id,
            OwnerUserId,
            "canonical-1.0",
            EvaluationRunStatus.Completed,
            CreatedAtUtc,
            CompletedAtUtc,
            7,
            results);
        ExtractionProvenanceDto[] provenance =
        [
            new(
                attemptId,
                available.Id,
                sourceFileId,
                "PSW",
                new string('a', 64),
                "provider",
                "model",
                "extractor-2.1",
                "Succeeded",
                canonicalDocumentId,
                "canonical-1.0",
                rawArtifactId,
                excelArtifactId,
                CreatedAtUtc,
                CompletedAtUtc)
        ];
        return new ManifestData(
            submission,
            job,
            evaluation,
            provenance,
            sourceFileId,
            attemptId,
            canonicalDocumentId,
            rawArtifactId,
            excelArtifactId);
    }

    private sealed record ManifestData(
        PpapSubmission Submission,
        PpapJob Job,
        RuleEvaluationRunDetails Evaluation,
        ExtractionProvenanceDto[] Provenance,
        Guid SourceFileId,
        Guid AttemptId,
        Guid CanonicalDocumentId,
        Guid RawArtifactId,
        Guid ExcelArtifactId);
}
