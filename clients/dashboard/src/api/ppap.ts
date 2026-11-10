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
  decision?: SubmissionDecision;
  decisionComment?: string | null;
  decidedAtUtc?: string | null;
};

export type SubmissionDecision = "None" | "InterimApproved" | "Approved" | "Rejected";
export type PpapSubmissionDecisionDto = {
  submissionId: string;
  status: PpapSubmissionStatus;
  decision: SubmissionDecision;
  comment: string | null;
  decidedByUserId: string | null;
  decidedAtUtc: string | null;
  version: number;
};
export type PpapReportSubmissionStateDto = {
  id: string;
  status: PpapSubmissionStatus;
  decision: SubmissionDecision;
  decisionComment: string | null;
  decidedByUserId: string | null;
  decidedAtUtc: string | null;
  version: number;
  isLatestReport: boolean;
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

export type DocumentUploadStatus = "Uploading" | "Available" | "Quarantined";
export type DocumentProcessingStatus = "NotQueued" | "Queued" | "Converting" | "Extracting" | "Validating" | "Ready" | "Failed" | "Superseded";
export type DocumentRevisionDto = {
  id: string;
  revisionNumber: number;
  version: number;
  sourceFileAssetId: string | null;
  sourceFileName: string;
  sourceContentType: string;
  sourceSizeBytes: number;
  uploadStatus: DocumentUploadStatus;
  processingStatus: DocumentProcessingStatus;
  previewPdfFileAssetId: string | null;
  generatedWorkbookFileAssetId: string | null;
  canonicalDocumentId: string | null;
  provider: string | null;
  extractorVersion: string | null;
  failureCode: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  completedAtUtc: string | null;
};
export type WorkspaceDocumentDto = {
  id: string;
  documentCode: string;
  isRequired: boolean;
  disposition: string;
  status: string;
  fileAssetId: string | null;
  notApplicableReason: string | null;
  currentRevision: DocumentRevisionDto | null;
  revisions: DocumentRevisionDto[];
};
export type WorkingFindingDto = {
  id: string;
  validationRunId: string;
  ruleVersionId: string;
  submissionDocumentId: string | null;
  documentRevisionId: string | null;
  stableRuleCode: string;
  ruleName: string;
  status: string;
  message: string;
  inputFingerprint: string;
  evidenceJson: string;
  createdAtUtc: string;
};
export type WaiverRequestDto = {
  id: string;
  submissionId: string;
  workingFindingId: string;
  findingFingerprint: string;
  requestedByUserId: string;
  requestReason: string;
  status: string;
  reviewedByUserId: string | null;
  reviewReason: string | null;
  requestedAtUtc: string;
  reviewedAtUtc: string | null;
  version: number;
};
export type WaiverReviewDto = {
  request: WaiverRequestDto;
  stableRuleCode: string;
  ruleName: string;
  findingMessage: string;
  evidenceJson: string;
};
export type SubmissionWorkspaceDto = {
  submissionId: string;
  partName: string;
  partNumber: string;
  drawingRevision: string;
  ppapLevel: number;
  status: string;
  version: number;
  documents: WorkspaceDocumentDto[];
  findings: WorkingFindingDto[];
  waiverRequests: WaiverRequestDto[];
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
  submission?: PpapReportSubmissionStateDto | null;
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
  supplierId?: string | null;
  ppapLevel?: number;
  classification?: PartClassification;
  canEdit?: boolean;
  supplier?: PpapPartSupplierSummaryDto | null;
  latestSubmission?: PpapPartSubmissionSummaryDto | null;
};

export type PartClassification = "Standard" | "Critical" | "Safety";
export type PartDirectoryStatus = "NotStarted" | "Draft" | "Submitted" | "InterimApproved" | "Approved" | "Rejected";
export type PpapPartSupplierSummaryDto = { id: string; code: string; name: string; city: string | null; country: string | null; isActive: boolean };
export type PpapPartSubmissionSummaryDto = {
  id: string;
  status: PpapSubmissionStatus;
  decision: SubmissionDecision;
  ppapLevel: number;
  totalElements: number;
  completedElements: number;
  latestReportId: string | null;
  openFindingCount: number;
  isOwner: boolean;
  version: number;
  createdAtUtc: string;
  submittedAtUtc: string | null;
  decidedAtUtc: string | null;
};
export type PpapPartsSummaryDto = {
  totalParts: number;
  activeSubmissions: number;
  pendingReview: number;
  approvalsThisMonth: number;
  approvalsLastMonth: number;
  newSubmissionsThisMonth: number;
  newSubmissionsLastMonth: number;
};

export type PartSearchParams = {
  search?: string;
  supplierId?: string;
  status?: PartDirectoryStatus;
  ppapLevel?: number;
  classification?: PartClassification;
  pageNumber?: number;
  pageSize?: number;
  sort?: string;
};

export type CreatePartInput = Pick<PpapPartDto, "partName" | "partNumber" | "drawingRevision"> & { supplierId?: string | null; ruleTemplateId?: string | null; ppapLevel?: number; classification?: PartClassification };
export type UpdatePartInput = Pick<PpapPartDto, "partName" | "partNumber" | "drawingRevision"> & { expectedVersion: number; supplierId?: string | null; ppapLevel?: number; classification?: PartClassification };

export type PpapSupplierDto = {
  id: string;
  code: string;
  name: string;
  contactName?: string | null;
  email?: string | null;
  phone?: string | null;
  address?: string | null;
  isActive: boolean;
  userIds: string[];
  partCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
  taxId?: string | null;
  dunsNumber?: string | null;
  city?: string | null;
  country?: string | null;
  siteName?: string | null;
  siteAddress?: string | null;
  siteSameAsHeadquarters?: boolean;
  contactJobTitle?: string | null;
  certificates?: SupplierCertificateDto[];
  region?: string | null;
  tierLevel?: SupplierTierLevel | null;
  qualityGrade?: SupplierQualityGrade | null;
  qualityScore?: number | null;
  activePpapCount?: number;
};
export type SupplierTierLevel = "Tier 1" | "Tier 2" | "Tier 3";
export type SupplierQualityGrade = "A+" | "A" | "B" | "C" | "D";
export const SUPPLIER_TIERS: readonly SupplierTierLevel[] = ["Tier 1", "Tier 2", "Tier 3"];
export const SUPPLIER_GRADES: readonly SupplierQualityGrade[] = ["A+", "A", "B", "C", "D"];
export const SUPPLIER_REGIONS = ["Europe", "North America", "Latin America", "Asia Pacific", "Middle East & Africa", "Other"] as const;
export type PpapSuppliersSummaryDto = {
  totalSuppliers: number;
  activeSuppliers: number;
  newThisMonth: number;
  newLastMonth: number;
  averageQualityScore: number | null;
  activePpaps: number;
  regionCount: number;
  criticalIssues: number;
};
export type SupplierCertificateType = "ISO 9001" | "IATF 16949" | "ISO 14001" | "Other";
export type SupplierCertificateDto = { fileAssetId: string; fileName: string; certificateType: SupplierCertificateType; addedAtUtc: string };
export type SupplierCertificateItem = { fileAssetId: string; fileName: string; certificateType: SupplierCertificateType };
export type SupplierProfileInput = {
  name: string;
  taxId: string | null;
  dunsNumber: string | null;
  address: string | null;
  city: string | null;
  country: string | null;
  siteName: string | null;
  siteAddress: string | null;
  siteSameAsHeadquarters: boolean;
  contactName: string | null;
  contactJobTitle: string | null;
  email: string | null;
  phone: string | null;
  tierLevel: SupplierTierLevel | null;
  qualityGrade: SupplierQualityGrade | null;
};
export type SupplierSearchParams = {
  search?: string;
  isActive?: boolean;
  region?: string;
  tierLevel?: string;
  qualityGrade?: string;
  pageNumber?: number;
  pageSize?: number;
  sort?: "name" | "-name" | "code" | "-updated";
};
export type CreateSupplierInput = { code?: string | null; profile: SupplierProfileInput; userIds: string[]; certificates: SupplierCertificateItem[] };
export type UpdateSupplierInput = { expectedVersion: number; code: string; profile: SupplierProfileInput; isActive: boolean; userIds: string[]; certificates: SupplierCertificateItem[] };
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
  if (params.supplierId) query.set("SupplierId", params.supplierId);
  if (params.status) query.set("Status", params.status);
  if (params.ppapLevel) query.set("PpapLevel", String(params.ppapLevel));
  if (params.classification) query.set("Classification", params.classification);
  if (params.pageNumber) query.set("PageNumber", String(params.pageNumber));
  if (params.pageSize) query.set("PageSize", String(params.pageSize));
  if (params.sort) query.set("Sort", params.sort);
  const suffix = query.toString() ? `?${query.toString()}` : "";
  return apiFetch<PagedResponse<PpapPartDto>>(`${BASE}/parts${suffix}`);
}

export function getPpapPartsSummary() {
  return apiFetch<PpapPartsSummaryDto>(`${BASE}/parts/summary`);
}

export function searchPpapSuppliers(params: SupplierSearchParams = {}) {
  const query = new URLSearchParams({ PageNumber: String(params.pageNumber ?? 1), PageSize: String(params.pageSize ?? 20) });
  if (params.search?.trim()) query.set("Search", params.search.trim());
  if (params.isActive !== undefined) query.set("IsActive", String(params.isActive));
  if (params.region) query.set("Region", params.region);
  if (params.tierLevel) query.set("TierLevel", params.tierLevel);
  if (params.qualityGrade) query.set("QualityGrade", params.qualityGrade);
  if (params.sort) query.set("Sort", params.sort);
  return apiFetch<PagedResponse<PpapSupplierDto>>(`${BASE}/suppliers?${query.toString()}`);
}

export function getPpapSuppliersSummary() {
  return apiFetch<PpapSuppliersSummaryDto>(`${BASE}/suppliers/summary`);
}

export function decidePpapSubmission(id: string, input: { decision: Exclude<SubmissionDecision, "None">; comment: string | null; expectedVersion: number }) {
  return apiFetch<PpapSubmissionDecisionDto>(`${BASE}/submissions/${encodeURIComponent(id)}/decision`, { method: "POST", body: JSON.stringify(input) });
}

export function cancelPpapSubmission(id: string, expectedVersion: number) {
  return apiFetch<PpapSubmissionDto>(`${BASE}/submissions/${encodeURIComponent(id)}/cancel`, { method: "POST", body: JSON.stringify({ expectedVersion }) });
}

export function getPpapSupplier(id: string) {
  return apiFetch<PpapSupplierDto>(`${BASE}/suppliers/${encodeURIComponent(id)}`);
}

export function createPpapSupplier(input: CreateSupplierInput) {
  return apiFetch<PpapSupplierDto>(`${BASE}/suppliers`, { method: "POST", body: JSON.stringify(input) });
}

export function updatePpapSupplier(id: string, input: UpdateSupplierInput) {
  return apiFetch<PpapSupplierDto>(`${BASE}/suppliers/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(input) });
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

export function getSubmissionWorkspace(id: string) {
  return apiFetch<SubmissionWorkspaceDto>(`${BASE}/submissions/${encodeURIComponent(id)}/workspace`);
}

export function getSpreadsheetViewerLicense() {
  return apiFetch<{ licenseKey: string; expiresAt: string | null }>(`${BASE}/viewer-license`);
}

export function reserveDocumentRevision(input: {
  submissionId: string;
  documentId: string;
  expectedVersion: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}) {
  return apiFetch<string>(
    `${BASE}/submissions/${encodeURIComponent(input.submissionId)}/documents/${encodeURIComponent(input.documentId)}/revisions`,
    {
      method: "POST",
      body: JSON.stringify({
        expectedVersion: input.expectedVersion,
        fileName: input.fileName,
        contentType: input.contentType,
        sizeBytes: input.sizeBytes,
      }),
    },
  );
}

export function requestFindingWaiver(input: { submissionId: string; findingId: string; reason: string }) {
  return apiFetch<string>(`${BASE}/submissions/${encodeURIComponent(input.submissionId)}/findings/${encodeURIComponent(input.findingId)}/waiver-requests`, {
    method: "POST",
    body: JSON.stringify({ reason: input.reason }),
  });
}

export function getPendingWaiverRequests(submissionId: string) {
  return apiFetch<WaiverReviewDto[]>(`${BASE}/operations/submissions/${encodeURIComponent(submissionId)}/waiver-requests`);
}

export function decideFindingWaiverRequest(input: { requestId: string; approve: boolean; reason: string; expectedVersion: number }) {
  return apiFetch<void>(`${BASE}/operations/waiver-requests/${encodeURIComponent(input.requestId)}/decision`, {
    method: "POST",
    body: JSON.stringify({ approve: input.approve, reason: input.reason, expectedVersion: input.expectedVersion }),
  });
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
