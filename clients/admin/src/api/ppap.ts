import { apiFetch } from "@/lib/api-client";
import type { PagedResponse } from "@/lib/api-types";

const BASE = "/api/v1/ppap";

export type PpapJobStatus =
  | "Queued"
  | "Processing"
  | "Completed"
  | "Failed"
  | "Cancelled"
  | (string & {});

export type PpapOperationsSummaryDto = {
  totalSubmissions: number;
  queuedJobs: number;
  processingJobs: number;
  completedJobs: number;
  failedJobs: number;
  pendingTopupRequests: number;
  totalAvailableCredits: number;
  totalReservedCredits: number;
};

export type PpapAdminJobDto = {
  id: string;
  submissionId: string;
  submissionVersion: number;
  partName: string;
  partNumber: string;
  ownerUserId: string;
  status: PpapJobStatus;
  currentStage: string;
  progressPercent: number;
  createdAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  failedAtUtc: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  reportId: string | null;
};

export type CreditTopupRequestStatus = "Pending" | "Approved" | "Rejected" | (string & {});

export type CreditTopupRequestDto = {
  id: string;
  ownerUserId: string;
  creditsRequested: number;
  paymentReference: string | null;
  note: string | null;
  status: CreditTopupRequestStatus;
  requestedAt: string;
  decidedAt: string | null;
  decidedByUserId: string | null;
  decisionNote: string | null;
};

export type SearchPpapJobsParams = {
  status?: PpapJobStatus;
  search?: string;
  pageNumber?: number;
  pageSize?: number;
  sort?: string;
};

export type SearchTopupsParams = {
  status?: CreditTopupRequestStatus;
  pageNumber?: number;
  pageSize?: number;
  sort?: string;
};

function searchParams(params: Record<string, string | number | undefined>): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") query.set(key, String(value));
  }
  return query.toString();
}

export function getPpapOperationsSummary(): Promise<PpapOperationsSummaryDto> {
  return apiFetch<PpapOperationsSummaryDto>(`${BASE}/operations/summary`);
}

export function searchPpapJobs(params: SearchPpapJobsParams = {}): Promise<PagedResponse<PpapAdminJobDto>> {
  const query = searchParams({
    Status: params.status,
    Search: params.search,
    PageNumber: params.pageNumber ?? 1,
    PageSize: params.pageSize ?? 20,
    Sort: params.sort,
  });
  return apiFetch<PagedResponse<PpapAdminJobDto>>(`${BASE}/operations/jobs?${query}`);
}

export function searchCreditTopupRequests(
  params: SearchTopupsParams = {},
): Promise<PagedResponse<CreditTopupRequestDto>> {
  const query = searchParams({
    Status: params.status,
    PageNumber: params.pageNumber ?? 1,
    PageSize: params.pageSize ?? 20,
    Sort: params.sort,
  });
  return apiFetch<PagedResponse<CreditTopupRequestDto>>(`${BASE}/credits/topup-requests?${query}`);
}

export function approveCreditTopupRequest(input: { id: string; note?: string }): Promise<void> {
  return apiFetch<void>(`${BASE}/credits/topup-requests/${encodeURIComponent(input.id)}/approve`, {
    method: "POST",
    body: JSON.stringify({ decisionNote: input.note || null }),
  });
}

export function rejectCreditTopupRequest(input: { id: string; note?: string }): Promise<void> {
  return apiFetch<void>(`${BASE}/credits/topup-requests/${encodeURIComponent(input.id)}/reject`, {
    method: "POST",
    body: JSON.stringify({ decisionNote: input.note || null }),
  });
}

export type PpapFindingKind = "Automated" | "Manual";

export type PpapWaiverRevocationDto = {
  id: string;
  revokedByUserId: string;
  reason: string;
  revokedAtUtc: string;
};

export type PpapFindingWaiverDto = {
  id: string;
  isActive: boolean;
  approvedByUserId: string;
  reason: string;
  approvedAtUtc: string;
  revocation: PpapWaiverRevocationDto | null;
};

export type PpapFindingDto = {
  id: string;
  ruleEvaluationRunId: string;
  ruleEvaluationResultId: string;
  templateVersionId: string;
  ruleDefinitionId: string;
  ruleVersionId: string;
  submissionDocumentId: string | null;
  canonicalDocumentId: string | null;
  extractionAttemptId: string | null;
  stableRuleCode: string;
  ruleName: string;
  documentCode: string | null;
  outcome: "Failed" | "NotEvaluated" | "Error" | (string & {});
  message: string | null;
  reason: string | null;
  traceJson: string;
  evaluatedAtUtc: string;
  waivers: PpapFindingWaiverDto[];
};

export type PpapManualFindingDto = {
  id: string;
  submissionDocumentId: string | null;
  canonicalDocumentId: string | null;
  extractionAttemptId: string | null;
  documentCode: string | null;
  message: string;
  evidenceNote: string | null;
  createdByUserId: string;
  createdAtUtc: string;
  waivers: PpapFindingWaiverDto[];
};

export type PpapManifestExtractionDto = {
  sourceSha256: string;
  attemptId: string;
  provider: string;
  model: string;
  extractorVersion: string;
  canonicalDocumentId: string;
  canonicalSchemaVersion: string;
  rawResponseArtifactId: string | null;
  generatedExcelArtifactId: string | null;
  createdAtUtc: string;
  completedAtUtc: string;
};

export type PpapManifestDocumentDto = {
  id: string;
  documentCode: string;
  disposition: string;
  status: string;
  sourceFileId: string | null;
  notApplicableReason: string | null;
  extraction: PpapManifestExtractionDto | null;
};

export type PpapReportReviewDto = {
  id: string;
  submissionId: string;
  jobId: string;
  submissionVersion: number;
  generatedAtUtc: string;
  partName: string;
  partNumber: string;
  drawingRevision: string;
  findingCount: number;
  automatedFindingCount: number;
  manualFindingCount: number;
  waivedFindingCount: number;
  openFindingCount: number;
  findings: PpapFindingDto[];
  manualFindings: PpapManualFindingDto[];
  reproducibilityManifest: { documents: PpapManifestDocumentDto[] } | null;
};

export type CreateManualFindingInput = {
  reportId: string;
  message: string;
  evidenceNote?: string;
  submissionDocumentId?: string;
  canonicalDocumentId?: string;
  extractionAttemptId?: string;
  idempotencyKey: string;
};

export type WaiveFindingInput = {
  reportId: string;
  findingKind: PpapFindingKind;
  findingId: string;
  reason: string;
  idempotencyKey: string;
};

export type RevokeFindingWaiverInput = {
  reportId: string;
  waiverId: string;
  reason: string;
  idempotencyKey: string;
};

export function getPpapReportReview(reportId: string): Promise<PpapReportReviewDto> {
  return apiFetch<PpapReportReviewDto>(`${BASE}/operations/reports/${encodeURIComponent(reportId)}/review`);
}

export function createManualPpapFinding(input: CreateManualFindingInput): Promise<string> {
  return apiFetch<string>(`${BASE}/operations/reports/manual-findings`, {
    method: "POST",
    headers: { "Idempotency-Key": input.idempotencyKey },
    body: JSON.stringify({
      reportId: input.reportId,
      message: input.message,
      evidenceNote: input.evidenceNote || null,
      submissionDocumentId: input.submissionDocumentId || null,
      canonicalDocumentId: input.canonicalDocumentId || null,
      extractionAttemptId: input.extractionAttemptId || null,
    }),
  });
}

export function waivePpapFinding(input: WaiveFindingInput): Promise<string> {
  return apiFetch<string>(`${BASE}/operations/reports/finding-waivers`, {
    method: "POST",
    headers: { "Idempotency-Key": input.idempotencyKey },
    body: JSON.stringify({
      reportId: input.reportId,
      findingKind: input.findingKind,
      findingId: input.findingId,
      reason: input.reason,
    }),
  });
}

export function revokePpapFindingWaiver(input: RevokeFindingWaiverInput): Promise<string> {
  return apiFetch<string>(`${BASE}/operations/reports/finding-waivers/revoke`, {
    method: "POST",
    headers: { "Idempotency-Key": input.idempotencyKey },
    body: JSON.stringify({ reportId: input.reportId, waiverId: input.waiverId, reason: input.reason }),
  });
}
