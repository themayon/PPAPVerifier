using System.Net;
using System.Text;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapJob : AggregateRoot<Guid>, IHasTenant
{
    public const int ErrorCodeMaxLength = 100;
    public const int ErrorMessageMaxLength = 1000;

    public string TenantId { get; private set; } = default!;
    public Guid SubmissionId { get; private set; }
    public int SubmissionVersion { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public PpapJobStatus Status { get; private set; }
    public PpapJobStage CurrentStage { get; private set; }
    public int ProgressPercent { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public Guid? ProcessingLeaseId { get; private set; }
    public DateTime? ProcessingLeaseExpiresAtUtc { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public Guid? CompletionEventId { get; private set; }
    public DateTime? CompletionEventPublishedAtUtc { get; private set; }
    public Guid? FailureEventId { get; private set; }
    public DateTime? FailureEventPublishedAtUtc { get; private set; }
    public DateTime? RuleEvaluationQueuedAtUtc { get; private set; }
    public DateTime? RuleEvaluationStartedAtUtc { get; private set; }
    public DateTime? RuleEvaluationCompletedAtUtc { get; private set; }
    public Guid? RuleEvaluationRunId { get; private set; }
    public DateTime? ReportGenerationQueuedAtUtc { get; private set; }
    public DateTime? ReportGenerationStartedAtUtc { get; private set; }
    public DateTime? ReportGenerationCompletedAtUtc { get; private set; }
    public Guid? ReportGenerationReportId { get; private set; }

    private PpapJob() { }

    public static PpapJob CreateQueued(
        string tenantId,
        Guid submissionId,
        int submissionVersion,
        Guid ownerUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        string normalizedTenantId = tenantId.Trim();
        if (normalizedTenantId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        }
        if (submissionId == Guid.Empty)
        {
            throw new ArgumentException("Submission ID is required.", nameof(submissionId));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(submissionVersion);
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Owner user ID is required.", nameof(ownerUserId));
        }

        return new PpapJob
        {
            Id = Guid.CreateVersion7(),
            TenantId = normalizedTenantId,
            SubmissionId = submissionId,
            SubmissionVersion = submissionVersion,
            OwnerUserId = ownerUserId,
            Status = PpapJobStatus.Queued,
            CurrentStage = PpapJobStage.UploadVerification,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Start()
    {
        EnsureStatus(PpapJobStatus.Queued, "start");
        Status = PpapJobStatus.Processing;
        StartedAtUtc = DateTime.UtcNow;
    }

    public void RenewProcessingLease(Guid leaseId, DateTime leaseExpiresAtUtc)
    {
        EnsureStatus(PpapJobStatus.Processing, "renew the processing lease for");
        if (leaseId == Guid.Empty)
        {
            throw new ArgumentException("Processing lease ID is required.", nameof(leaseId));
        }
        if (ProcessingLeaseId is not null && ProcessingLeaseId != leaseId)
        {
            throw Conflict("Cannot renew a processing lease owned by another worker.");
        }
        if (leaseExpiresAtUtc.Kind != DateTimeKind.Utc || leaseExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAtUtc));
        }

        ProcessingLeaseId = leaseId;
        ProcessingLeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    public void AdvanceStage(PpapJobStage stage, int progressPercent)
    {
        EnsureStatus(PpapJobStatus.Processing, "advance");
        PpapJobStage nextStage = (PpapJobStage)((int)CurrentStage + 1);
        if (!Enum.IsDefined(stage) || stage != nextStage)
        {
            throw Conflict($"Cannot advance PPAP job from stage {CurrentStage} to {stage}.");
        }
        if (progressPercent <= ProgressPercent || progressPercent >= 100)
        {
            throw Conflict("PPAP job progress must increase and remain below 100 until completion.");
        }

        CurrentStage = stage;
        ProgressPercent = progressPercent;
    }

    public void Complete()
    {
        EnsureStatus(PpapJobStatus.Processing, "complete");
        if (CurrentStage != PpapJobStage.NotificationDelivery)
        {
            throw Conflict("PPAP job cannot complete before notification delivery.");
        }

        Status = PpapJobStatus.Completed;
        ProgressPercent = 100;
        CompletedAtUtc = DateTime.UtcNow;
        ClearProcessingLease();
    }

    public void Fail(string errorCode, string errorMessage)
    {
        EnsureStatus(PpapJobStatus.Processing, "fail");
        string normalizedErrorCode = SanitizeErrorCode(errorCode);
        string normalizedErrorMessage = SanitizeErrorMessage(errorMessage);
        Status = PpapJobStatus.Failed;
        ErrorCode = normalizedErrorCode;
        ErrorMessage = normalizedErrorMessage;
        FailedAtUtc = DateTime.UtcNow;
        ClearProcessingLease();
    }

    public bool MarkCompletionEventPublished(Guid eventId)
    {
        EnsureStatus(PpapJobStatus.Completed, "publish the completion event for");
        if (CompletionEventPublishedAtUtc is not null) return false;
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID is required.", nameof(eventId));
        CompletionEventId = eventId;
        CompletionEventPublishedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkFailureEventPublished(Guid eventId)
    {
        EnsureStatus(PpapJobStatus.Failed, "publish the failure event for");
        if (FailureEventPublishedAtUtc is not null) return false;
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID is required.", nameof(eventId));
        FailureEventId = eventId;
        FailureEventPublishedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkRuleEvaluationQueued()
    {
        EnsureRuleEvaluationStage("queue rule evaluation for");
        if (RuleEvaluationQueuedAtUtc is not null) return false;
        RuleEvaluationQueuedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkRuleEvaluationStarted()
    {
        EnsureRuleEvaluationStage("start rule evaluation for");
        if (RuleEvaluationQueuedAtUtc is null)
        {
            throw Conflict("Cannot start PPAP rule evaluation before it is queued.");
        }
        if (RuleEvaluationStartedAtUtc is not null) return false;
        RuleEvaluationStartedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool CompleteRuleEvaluation(Guid ruleEvaluationRunId)
    {
        if (ruleEvaluationRunId == Guid.Empty)
        {
            throw new ArgumentException("Rule evaluation run ID is required.", nameof(ruleEvaluationRunId));
        }
        if (RuleEvaluationRunId is not null)
        {
            if (RuleEvaluationRunId == ruleEvaluationRunId && RuleEvaluationCompletedAtUtc is not null)
            {
                return false;
            }

            throw Conflict("A different rule evaluation run is already recorded for this PPAP job.");
        }

        EnsureRuleEvaluationStage("complete rule evaluation for");
        if (RuleEvaluationStartedAtUtc is null)
        {
            throw Conflict("Cannot complete PPAP rule evaluation before it is started.");
        }

        RuleEvaluationRunId = ruleEvaluationRunId;
        RuleEvaluationCompletedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkReportGenerationQueued()
    {
        EnsureStatus(PpapJobStatus.Processing, "queue report generation for");
        if (CurrentStage != PpapJobStage.CrossValidation || RuleEvaluationCompletedAtUtc is null || RuleEvaluationRunId is null)
        {
            throw Conflict("Cannot queue PPAP report generation before rule evaluation completes.");
        }
        if (ReportGenerationQueuedAtUtc is not null) return false;
        ReportGenerationQueuedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkReportGenerationStarted()
    {
        EnsureStatus(PpapJobStatus.Processing, "start report generation for");
        if (CurrentStage is not (PpapJobStage.CrossValidation or PpapJobStage.ReportGeneration)
            || ReportGenerationQueuedAtUtc is null)
        {
            throw Conflict("Cannot start PPAP report generation before it is queued.");
        }
        if (ReportGenerationStartedAtUtc is not null) return false;
        ReportGenerationStartedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool MarkReportGenerationCompleted(Guid reportId)
    {
        if (reportId == Guid.Empty) throw new ArgumentException("Report ID is required.", nameof(reportId));
        if (ReportGenerationReportId is not null)
        {
            if (ReportGenerationReportId == reportId && ReportGenerationCompletedAtUtc is not null) return false;
            throw Conflict("A different report is already recorded for this PPAP job.");
        }
        EnsureStatus(PpapJobStatus.Processing, "complete report generation for");
        if (CurrentStage != PpapJobStage.ReportGeneration || ReportGenerationStartedAtUtc is null)
        {
            throw Conflict("Cannot complete PPAP report generation before it is started.");
        }
        ReportGenerationReportId = reportId;
        ReportGenerationCompletedAtUtc = DateTime.UtcNow;
        return true;
    }

    private void EnsureRuleEvaluationStage(string action)
    {
        EnsureStatus(PpapJobStatus.Processing, action);
        if (CurrentStage != PpapJobStage.DocumentClassification)
        {
            throw Conflict($"Cannot {action} a PPAP job in stage {CurrentStage}.");
        }
    }

    private void ClearProcessingLease()
    {
        ProcessingLeaseId = null;
        ProcessingLeaseExpiresAtUtc = null;
    }

    private void EnsureStatus(PpapJobStatus requiredStatus, string action)
    {
        if (Status != requiredStatus)
        {
            throw Conflict($"Cannot {action} a PPAP job in status {Status}.");
        }
    }

    private static string SanitizeErrorCode(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        string normalized = new(errorCode.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .Take(ErrorCodeMaxLength)
            .ToArray());
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Error code must contain a letter or digit.", nameof(errorCode));
        }

        return normalized;
    }

    private static string SanitizeErrorMessage(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        StringBuilder sanitized = new(errorMessage.Length);
        bool pendingWhitespace = false;
        foreach (char character in errorMessage.Trim())
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingWhitespace = sanitized.Length > 0;
                continue;
            }
            if (pendingWhitespace && sanitized.Length < ErrorMessageMaxLength)
            {
                sanitized.Append(' ');
            }
            if (sanitized.Length < ErrorMessageMaxLength)
            {
                sanitized.Append(character);
            }
            pendingWhitespace = false;
            if (sanitized.Length == ErrorMessageMaxLength)
            {
                break;
            }
        }

        string normalized = sanitized.ToString().Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Error message is required.", nameof(errorMessage));
        }

        return normalized;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
