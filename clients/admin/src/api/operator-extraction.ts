import { apiFetch } from "@/lib/api-client";
import type { PagedResponse } from "@/lib/api-types";

const BASE = "/api/v1/document-intelligence/operations/attempts";

export type OperatorExtractionAttemptStatus =
  | "Pending"
  | "Claimed"
  | "Submitted"
  | "Processing"
  | "Succeeded"
  | "InvalidDocument"
  | "Failed"
  | "TimedOut"
  | "DeadLettered"
  | (string & {});

export type OperatorExtractionAttemptDto = {
  id: string;
  ownerUserId: string;
  submissionDocumentId: string;
  sourceFileAssetId: string;
  documentCode: string;
  sourceSha256: string;
  provider: string;
  model: string;
  extractorVersion: string;
  status: OperatorExtractionAttemptStatus;
  attemptCount: number;
  errorCode: string | null;
  errorMessage: string | null;
  canonicalDocumentId: string | null;
  rawResponseFileAssetId: string | null;
  generatedExcelFileAssetId: string | null;
  createdAtUtc: string;
  claimedAtUtc: string | null;
  claimedUntilUtc: string | null;
  submittedAtUtc: string | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  updatedAtUtc: string;
  completionEventPublishedAtUtc: string | null;
  failureEventPublishedAtUtc: string | null;
};

export type SearchOperatorExtractionAttemptsParams = {
  status?: OperatorExtractionAttemptStatus;
  documentCode?: string;
  ownerUserId?: string;
  pageNumber?: number;
  pageSize?: number;
  sort?: string;
};

export function searchOperatorExtractionAttempts(
  params: SearchOperatorExtractionAttemptsParams = {},
): Promise<PagedResponse<OperatorExtractionAttemptDto>> {
  const query = new URLSearchParams();
  if (params.status) query.set("Status", params.status);
  if (params.documentCode) query.set("DocumentCode", params.documentCode);
  if (params.ownerUserId) query.set("OwnerUserId", params.ownerUserId);
  query.set("PageNumber", String(params.pageNumber ?? 1));
  query.set("PageSize", String(params.pageSize ?? 20));
  if (params.sort) query.set("Sort", params.sort);

  return apiFetch<PagedResponse<OperatorExtractionAttemptDto>>(`${BASE}?${query}`);
}

export function getOperatorExtractionAttempt(id: string): Promise<OperatorExtractionAttemptDto> {
  return apiFetch<OperatorExtractionAttemptDto>(`${BASE}/${encodeURIComponent(id)}`);
}
