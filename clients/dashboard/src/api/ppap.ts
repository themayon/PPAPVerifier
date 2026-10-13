import { apiFetch } from "@/lib/api-client";

const BASE = "/api/v1/ppap";

export type PagedResponse<T> = {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
};

export type CreditBalanceDto = {
  availableCredits: number;
  reservedCredits: number;
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

export type PpapSubmissionStatus = "Draft" | "Submitted" | "Cancelled" | (string & {});
export type SubmissionDocumentDisposition = "Submit" | "Retain" | "Available";
export type SubmissionDocumentStatus =
  | "PendingUpload"
  | "Available"
  | "Quarantined"
  | "NotApplicable"
  | (string & {});
export type PpapJobStatus = "Queued" | "Processing" | "Completed" | "Failed" | "Cancelled" | (string & {});
export type PpapJobStage =
  | "UploadVerification"
  | "PreprocessingExtraction"
  | "DocumentClassification"
  | "CrossValidation"
  | "ReportGeneration"
  | "NotificationDelivery"
  | (string & {});

export type PpapSubmissionDto = {
  id: string;
  partId: string | null;
  partName: string;
  partNumber: string;
  drawingRevision: string;
  ppapLevel: number;
  ruleTemplateId: string | null;
  ruleTemplateVersionId: string | null;
  status: PpapSubmissionStatus;
  version: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  submittedAtUtc: string | null;
  cancelledAtUtc: string | null;
};

export type SubmissionDocumentDto = {
  id: string;
  submissionId: string;
  documentCode: string;
  isRequired: boolean;
  disposition: SubmissionDocumentDisposition;
  status: SubmissionDocumentStatus;
  fileAssetId: string | null;
  notApplicableReason: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
};

export type PpapJobDto = {
  id: string;
  submissionId: string;
  submissionVersion: number;
  status: PpapJobStatus;
  currentStage: PpapJobStage;
  progressPercent: number;
  createdAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  failedAtUtc: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  reportId: string | null;
};

export type PpapFindingOutcome = "Failed" | "NotEvaluated" | "Error";

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
  outcome: PpapFindingOutcome;
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

export type PpapManifestEvaluationResultDto = {
  id: string;
  ruleDefinitionId: string;
  ruleVersionId: string;
  stableRuleCode: string;
  documentCode: string | null;
  status: string;
  evaluatedAtUtc: string;
};

export type PpapManifestEvaluationDto = {
  runId: string;
  version: number;
  inputSchemaVersion: string;
  status: string;
  createdAtUtc: string;
  completedAtUtc: string | null;
  results: PpapManifestEvaluationResultDto[];
};

export type PpapManifestExtractionProvenanceDto = {
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
  extraction: PpapManifestExtractionProvenanceDto | null;
};

export type PpapReproducibilityManifestDto = {
  schemaVersion: string;
  jobId: string;
  submissionId: string;
  submissionVersion: number;
  templateVersionId: string;
  evaluation: PpapManifestEvaluationDto;
  documents: PpapManifestDocumentDto[];
};

export type PpapReportDto = {
  id: string;
  submissionId: string;
  jobId: string;
  submissionVersion: number;
  generatedAtUtc: string;
  partName: string;
  partNumber: string;
  drawingRevision: string;
  availableDocumentCount: number;
  notApplicableDocumentCount: number;
  quarantinedDocumentCount: number;
  findingCount: number;
  automatedFindingCount: number;
  manualFindingCount: number;
  waivedFindingCount: number;
  openFindingCount: number;
  failedFindingCount: number;
  notEvaluatedFindingCount: number;
  errorFindingCount: number;
  findings: PpapFindingDto[];
  manualFindings: PpapManualFindingDto[];
  reproducibilityManifest: PpapReproducibilityManifestDto | null;
};

export type CreateCreditTopupRequestInput = {
  creditsRequested: 10 | 50 | 100;
  paymentReference?: string | null;
  note?: string | null;
};

export type CreateDraftSubmissionInput = {
  partName: string;
  partNumber: string;
  drawingRevision: string;
  ppapLevel: number;
  ruleTemplateId: string | null;
};

export type RuleTemplateSummaryDto = {
  id: string;
  stableCode: string;
  name: string;
};

export type PpapPartDto = {
  id: string;
  partName: string;
  partNumber: string;
  drawingRevision: string;
  ruleTemplateId: string | null;
  ruleTemplateVersionId: string | null;
  isActive: boolean;
  version: number;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type PartSearchParams = {
  search?: string;
  pageNumber?: number;
  pageSize?: number;
  sort?: string;
};

export type CreatePartInput = Pick<PpapPartDto, "partName" | "partNumber" | "drawingRevision">;
export type UpdatePartInput = CreatePartInput & { expectedVersion: number };
export type AssignPartTemplateInput = { ruleTemplateId: string; expectedVersion: number };

export type MyTopupSearchParams = {
  status?: CreditTopupRequestStatus;
  page?: number;
  pageSize?: number;
};

export type MySubmissionSearchParams = {
  search?: string;
  status?: PpapSubmissionStatus;
  pageNumber?: number;
  pageSize?: number;
  sort?: string;
};

export function getMyCreditBalance() {
  return apiFetch<CreditBalanceDto>(`${BASE}/credits/balance`);
}

export function getMyCreditTopupRequests(params: MyTopupSearchParams = {}) {
  const query = new URLSearchParams();
  if (params.status) query.set("status", params.status);
  if (params.page) query.set("page", String(params.page));
  if (params.pageSize) query.set("pageSize", String(params.pageSize));
  const suffix = query.toString() ? `?${query.toString()}` : "";
  return apiFetch<PagedResponse<CreditTopupRequestDto>>(
    `${BASE}/credits/topup-requests/me${suffix}`,
  );
}

export function createCreditTopupRequest(input: CreateCreditTopupRequestInput) {
  return apiFetch<string>(`${BASE}/credits/topup-requests`, {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function searchMyPpapSubmissions(params: MySubmissionSearchParams = {}) {
  const query = new URLSearchParams();
  if (params.search) query.set("search", params.search);
  if (params.status) query.set("status", params.status);
  if (params.pageNumber) query.set("pageNumber", String(params.pageNumber));
  if (params.pageSize) query.set("pageSize", String(params.pageSize));
  if (params.sort) query.set("sort", params.sort);
  const suffix = query.toString() ? `?${query.toString()}` : "";
  return apiFetch<PagedResponse<PpapSubmissionDto>>(`${BASE}/submissions${suffix}`);
}

export function createDraftSubmission(input: CreateDraftSubmissionInput) {
  return apiFetch<string>(`${BASE}/submissions`, {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function getPublishedRuleTemplates() {
  const query = new URLSearchParams({
    Status: "Published",
    PageNumber: "1",
    PageSize: "100",
  });
  return apiFetch<PagedResponse<RuleTemplateSummaryDto>>(
    `/api/v1/rules/templates?${query.toString()}`,
  );
}

export function searchMyPpapParts(params: PartSearchParams = {}) {
  const query = new URLSearchParams();
  if (params.search) query.set("Search", params.search);
  if (params.pageNumber) query.set("PageNumber", String(params.pageNumber));
  if (params.pageSize) query.set("PageSize", String(params.pageSize));
  if (params.sort) query.set("Sort", params.sort);
  const suffix = query.toString() ? `?${query.toString()}` : "";
  return apiFetch<PagedResponse<PpapPartDto>>(`${BASE}/parts${suffix}`);
}

export function getPpapPart(id: string) {
  return apiFetch<PpapPartDto>(`${BASE}/parts/${encodeURIComponent(id)}`);
}

export function createPpapPart(input: CreatePartInput) {
  return apiFetch<PpapPartDto>(`${BASE}/parts`, { method: "POST", body: JSON.stringify(input) });
}

export function updatePpapPart(id: string, input: UpdatePartInput) {
  return apiFetch<PpapPartDto>(`${BASE}/parts/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(input) });
}

export function assignPpapPartTemplate(id: string, input: AssignPartTemplateInput) {
  return apiFetch<PpapPartDto>(`${BASE}/parts/${encodeURIComponent(id)}/assign-template`, { method: "POST", body: JSON.stringify(input) });
}

export function createDraftFromPpapPart(id: string, ppapLevel: number) {
  return apiFetch<string>(`${BASE}/parts/${encodeURIComponent(id)}/submissions`, { method: "POST", body: JSON.stringify({ ppapLevel }) });
}

export function getPpapSubmission(id: string) {
  return apiFetch<PpapSubmissionDto>(`${BASE}/submissions/${encodeURIComponent(id)}`);
}

export function getSubmissionDocuments(id: string) {
  return apiFetch<SubmissionDocumentDto[]>(
    `${BASE}/submissions/${encodeURIComponent(id)}/documents`,
  );
}

export function addSubmissionDocument(id: string, documentCode: string) {
  return apiFetch<string>(`${BASE}/submissions/${encodeURIComponent(id)}/documents`, {
    method: "POST",
    body: JSON.stringify({ documentCode }),
  });
}

export function markSubmissionDocumentNotApplicable(
  id: string,
  documentId: string,
  reason: string,
) {
  return apiFetch<void>(
    `${BASE}/submissions/${encodeURIComponent(id)}/documents/${encodeURIComponent(documentId)}/not-applicable`,
    { method: "POST", body: JSON.stringify({ reason }) },
  );
}

export function submitPpapSubmission(id: string, expectedVersion: number) {
  return apiFetch<string>(`${BASE}/submissions/${encodeURIComponent(id)}/submit`, {
    method: "POST",
    body: JSON.stringify({ expectedVersion }),
  });
}

export function getPpapJob(id: string) {
  return apiFetch<PpapJobDto>(`${BASE}/jobs/${encodeURIComponent(id)}`);
}

export function getPpapReport(id: string) {
  return apiFetch<PpapReportDto>(`${BASE}/reports/${encodeURIComponent(id)}`);
}

// Tenant-scoped operator capabilities. These reuse the dashboard's existing PPAP
// DTOs so report, finding, waiver, top-up, and pagination shapes have one source.
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

export type PpapAdminJobDto = PpapJobDto & {
  partName: string;
  partNumber: string;
  ownerUserId: string;
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

export type PpapFindingKind = "Automated" | "Manual";
export type CreateManualFindingInput = {
  reportId: string;
  message: string;
  evidenceNote?: string;
  submissionDocumentId?: string;
  canonicalDocumentId?: string;
  extractionAttemptId?: string;
  idempotencyKey: string;
};
export type WaiveFindingInput = { reportId: string; findingKind: PpapFindingKind; findingId: string; reason: string; idempotencyKey: string };
export type RevokeFindingWaiverInput = { reportId: string; waiverId: string; reason: string; idempotencyKey: string };

function operatorSearchParams(params: Record<string, string | number | undefined>) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") query.set(key, String(value));
  }
  return query.toString();
}

export const getPpapOperationsSummary = () => apiFetch<PpapOperationsSummaryDto>(`${BASE}/operations/summary`);
export function searchPpapJobs(params: SearchPpapJobsParams = {}) {
  const query = operatorSearchParams({ Status: params.status, Search: params.search, PageNumber: params.pageNumber ?? 1, PageSize: params.pageSize ?? 20, Sort: params.sort });
  return apiFetch<PagedResponse<PpapAdminJobDto>>(`${BASE}/operations/jobs?${query}`);
}
export function searchCreditTopupRequests(params: SearchTopupsParams = {}) {
  const query = operatorSearchParams({ Status: params.status, PageNumber: params.pageNumber ?? 1, PageSize: params.pageSize ?? 20, Sort: params.sort });
  return apiFetch<PagedResponse<CreditTopupRequestDto>>(`${BASE}/credits/topup-requests?${query}`);
}
export const approveCreditTopupRequest = (input: { id: string; note?: string }) => apiFetch<void>(`${BASE}/credits/topup-requests/${encodeURIComponent(input.id)}/approve`, { method: "POST", body: JSON.stringify({ decisionNote: input.note || null }) });
export const rejectCreditTopupRequest = (input: { id: string; note?: string }) => apiFetch<void>(`${BASE}/credits/topup-requests/${encodeURIComponent(input.id)}/reject`, { method: "POST", body: JSON.stringify({ decisionNote: input.note || null }) });
export const getPpapReportReview = (reportId: string) => apiFetch<PpapReportDto>(`${BASE}/operations/reports/${encodeURIComponent(reportId)}/review`);
export const createManualPpapFinding = (input: CreateManualFindingInput) => apiFetch<string>(`${BASE}/operations/reports/manual-findings`, { method: "POST", headers: { "Idempotency-Key": input.idempotencyKey }, body: JSON.stringify({ reportId: input.reportId, message: input.message, evidenceNote: input.evidenceNote || null, submissionDocumentId: input.submissionDocumentId || null, canonicalDocumentId: input.canonicalDocumentId || null, extractionAttemptId: input.extractionAttemptId || null }) });
export const waivePpapFinding = (input: WaiveFindingInput) => apiFetch<string>(`${BASE}/operations/reports/finding-waivers`, { method: "POST", headers: { "Idempotency-Key": input.idempotencyKey }, body: JSON.stringify({ reportId: input.reportId, findingKind: input.findingKind, findingId: input.findingId, reason: input.reason }) });
export const revokePpapFindingWaiver = (input: RevokeFindingWaiverInput) => apiFetch<string>(`${BASE}/operations/reports/finding-waivers/revoke`, { method: "POST", headers: { "Idempotency-Key": input.idempotencyKey }, body: JSON.stringify({ reportId: input.reportId, waiverId: input.waiverId, reason: input.reason }) });
