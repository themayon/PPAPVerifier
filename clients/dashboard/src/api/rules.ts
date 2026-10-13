import { apiFetch } from "@/lib/api-client";
import type { PagedResponse } from "@/api/ppap";

export type RuleCategory = "Header" | "Row" | "Document" | "CrossDocument" | "Drawing" | "ModelInference" | "Special" | "Custom";
export type RuleLifecycle = "Draft" | "InReview" | "Published" | "Retired";
export type TemplateDocumentDisposition = "Submit" | "Retain" | "Available";
export type RuleEvaluationStatus = "Passed" | "Failed" | "NotEvaluated" | "Error";

export type RuleVersionMetadataDto = { id: string; versionNumber: number; status: RuleLifecycle; createdAtUtc: string; publishedAtUtc?: string | null; retiredAtUtc?: string | null; createdByUserId: string; astJson?: string | null; outcomeJson?: string | null };
export type RuleDefinitionSummaryDto = { id: string; stableCode: string; name: string; description?: string | null; category: RuleCategory; lifecycle: RuleLifecycle; currentPublishedVersionId?: string | null; createdAtUtc: string; updatedAtUtc: string; version: number; latestOutcomeJson?: string | null; conditionCount?: number };
export type RuleDefinitionDetailDto = RuleDefinitionSummaryDto & { versions: RuleVersionMetadataDto[] };
export type RuleTraceDto = { node: string; operator?: string | null; left?: string | null; right?: string | null; status: RuleEvaluationStatus; reason?: string | null };
export type RuleEvaluationResultDto = { status: RuleEvaluationStatus; reason?: string | null; trace: RuleTraceDto[] };
export type RuleSort = "name" | "-name" | "created" | "-created" | "updated" | "-updated";
export type SearchRulesParams = { search?: string; category?: RuleCategory; categories?: readonly RuleCategory[]; status?: RuleLifecycle; severity?: string; tag?: string; pageNumber?: number; pageSize?: number; sort?: RuleSort };
export type CreateRuleInput = { stableCode: string; name: string; description?: string | null; category: RuleCategory; astJson: string; outcomeJson: string };
export type UpdateRuleDraftInput = Omit<CreateRuleInput, "stableCode"> & { versionId: string; expectedAggregateVersion: number };
export type RuleVersionTransitionInput = { versionId: string; expectedAggregateVersion: number };
export type TestRuleInput = { astJson: string; documents: Record<string, string>; parametersJson?: string };

export type TemplateDocumentRequirementDto = { id: string; documentCode: string; isRequired: boolean; sortOrder: number; disposition: TemplateDocumentDisposition };
export type TemplateRuleAssignmentDto = { id: string; ruleVersionId: string; documentCode?: string | null; parametersJson: string };
export type RuleTemplateVersionDto = { id: string; versionNumber: number; status: RuleLifecycle; createdByUserId: string; createdAtUtc: string; publishedAtUtc?: string | null; retiredAtUtc?: string | null; documentRequirements: TemplateDocumentRequirementDto[]; ruleAssignments: TemplateRuleAssignmentDto[] };
export type RuleTemplateSummaryDto = { id: string; stableCode: string; name: string; description?: string | null; lifecycle: RuleLifecycle; currentPublishedVersionId?: string | null; createdAtUtc: string; updatedAtUtc: string; version: number };
export type RuleTemplateDetailDto = RuleTemplateSummaryDto & { versions: RuleTemplateVersionDto[] };
export type SearchTemplatesParams = { search?: string; status?: RuleLifecycle; pageNumber?: number; pageSize?: number; sort?: "name" | "-name" | "created" | "-created" };
export type CreateTemplateInput = { stableCode: string; name: string; description?: string | null };
export type ConfigureTemplateDraftInput = { versionId: string; expectedAggregateVersion: number; name: string; description?: string | null; documentRequirements: Array<{ documentCode: string; isRequired: boolean; sortOrder: number; disposition: TemplateDocumentDisposition }>; ruleAssignments: Array<{ ruleVersionId: string; documentCode?: string | null; parametersJson: string }> };

const BASE = "/api/v1/rules";
function query(params: SearchRulesParams | SearchTemplatesParams) {
  const value = new URLSearchParams({ PageNumber: String(params.pageNumber ?? 1), PageSize: String(params.pageSize ?? 20) });
  if (params.search?.trim()) value.set("Search", params.search.trim());
  if ("category" in params && params.category) value.set("Category", params.category);
  if ("categories" in params && params.categories?.length) value.set("Categories", params.categories.join(","));
  if ("severity" in params && params.severity) value.set("Severity", params.severity);
  if ("tag" in params && params.tag?.trim()) value.set("Tag", params.tag.trim());
  if (params.status) value.set("Status", params.status);
  if (params.sort) value.set("Sort", params.sort);
  return value.toString();
}
const body = (input: unknown): RequestInit => ({ method: "POST", body: JSON.stringify(input) });

export const searchRules = (params: SearchRulesParams = {}) => apiFetch<PagedResponse<RuleDefinitionSummaryDto>>(`${BASE}/definitions?${query(params)}`);
export const getRule = (id: string) => apiFetch<RuleDefinitionDetailDto>(`${BASE}/definitions/${encodeURIComponent(id)}`);
export const createRule = (input: CreateRuleInput) => apiFetch<string>(`${BASE}/definitions`, body(input));
export const updateRuleDraft = (id: string, input: UpdateRuleDraftInput) => apiFetch<void>(`${BASE}/definitions/${encodeURIComponent(id)}/draft`, { method: "PUT", body: JSON.stringify(input) });
export const submitRuleForReview = (id: string, input: RuleVersionTransitionInput) => apiFetch<void>(`${BASE}/definitions/${encodeURIComponent(id)}/submit-review`, body(input));
export const publishRule = (id: string, input: RuleVersionTransitionInput & { retireCurrent?: boolean }) => apiFetch<void>(`${BASE}/definitions/${encodeURIComponent(id)}/publish`, body(input));
export const retireRule = (id: string, input: RuleVersionTransitionInput) => apiFetch<void>(`${BASE}/definitions/${encodeURIComponent(id)}/retire`, body(input));
export const cloneRule = (id: string, input: RuleVersionTransitionInput) => apiFetch<string>(`${BASE}/definitions/${encodeURIComponent(id)}/clone`, body(input));
export const testRule = (input: TestRuleInput) => apiFetch<RuleEvaluationResultDto>(`${BASE}/definitions/test`, body(input));
export const searchTemplates = (params: SearchTemplatesParams = {}) => apiFetch<PagedResponse<RuleTemplateSummaryDto>>(`${BASE}/templates?${query(params)}`);
export const getTemplate = (id: string) => apiFetch<RuleTemplateDetailDto>(`${BASE}/templates/${encodeURIComponent(id)}`);
export const createTemplate = (input: CreateTemplateInput) => apiFetch<string>(`${BASE}/templates`, body(input));
export const configureTemplateDraft = (id: string, input: ConfigureTemplateDraftInput) => apiFetch<void>(`${BASE}/templates/${encodeURIComponent(id)}/draft`, { method: "PUT", body: JSON.stringify(input) });
export const submitTemplateForReview = (id: string, input: RuleVersionTransitionInput) => apiFetch<void>(`${BASE}/templates/${encodeURIComponent(id)}/submit-review`, body(input));
export const publishTemplate = (id: string, input: RuleVersionTransitionInput & { retireCurrent?: boolean }) => apiFetch<void>(`${BASE}/templates/${encodeURIComponent(id)}/publish`, body(input));
export const retireTemplate = (id: string, input: RuleVersionTransitionInput) => apiFetch<void>(`${BASE}/templates/${encodeURIComponent(id)}/retire`, body(input));
export const cloneTemplate = (id: string, input: RuleVersionTransitionInput) => apiFetch<string>(`${BASE}/templates/${encodeURIComponent(id)}/clone`, body(input));
