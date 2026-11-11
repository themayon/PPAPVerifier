import { apiFetch } from "@/lib/api-client";
import type { PagedResponse } from "@/api/ppap";

export type RuleCategory = "Header" | "Row" | "Document" | "CrossDocument" | "Drawing" | "ModelInference" | "Special" | "Custom";
export type RuleLifecycle = "Draft" | "InReview" | "Published" | "Retired";
export type TemplateDocumentDisposition = "Submit" | "Retain" | "Available";
export type RuleEvaluationStatus = "Passed" | "Failed" | "NotEvaluated" | "Error";

export type RuleVersionMetadataDto = { id: string; versionNumber: number; status: RuleLifecycle; createdAtUtc: string; publishedAtUtc?: string | null; retiredAtUtc?: string | null; createdByUserId: string; astJson?: string | null; outcomeJson?: string | null };
export type RuleDefinitionSummaryDto = { id: string; stableCode: string; name: string; description?: string | null; category: RuleCategory; lifecycle: RuleLifecycle; currentPublishedVersionId?: string | null; createdAtUtc: string; updatedAtUtc: string; version: number; latestOutcomeJson?: string | null; conditionCount?: number; isArchived?: boolean; archivedAtUtc?: string | null };
export type RuleDefinitionDetailDto = RuleDefinitionSummaryDto & { versions: RuleVersionMetadataDto[]; hasEvaluationHistory?: boolean };
export type RuleArchiveFilter = "Exclude" | "Include" | "Only";
export type DeleteRuleOutcome = "Deleted" | "Archived";
export type DeleteRuleResultDto = { outcome: DeleteRuleOutcome };
export type RuleTraceDto = { node: string; operator?: string | null; left?: string | null; right?: string | null; status: RuleEvaluationStatus; reason?: string | null };
export type RuleEvaluationResultDto = { status: RuleEvaluationStatus; reason?: string | null; trace: RuleTraceDto[] };
export type RuleSort = "name" | "-name" | "created" | "-created" | "updated" | "-updated";
export type SearchRulesParams = { search?: string; category?: RuleCategory; categories?: readonly RuleCategory[]; status?: RuleLifecycle; severity?: string; tag?: string; archived?: RuleArchiveFilter; pageNumber?: number; pageSize?: number; sort?: RuleSort };
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

export type RuleColumnKind = "Column" | "Header";
export type RuleColumnDataType = "Text" | "Number" | "Date" | "Boolean";
export type RuleColumnDto = { id: string; name: string; key: string; kind: RuleColumnKind; dataType: RuleColumnDataType; headerNames?: string | null; description?: string | null; isActive: boolean; createdAtUtc: string; updatedAtUtc: string; version: number };
export type SearchRuleColumnsParams = { search?: string; kind?: RuleColumnKind; dataType?: RuleColumnDataType; isActive?: boolean; pageNumber?: number; pageSize?: number; sort?: "name" | "-name" | "key" | "-updated" };
export type CreateRuleColumnInput = { name: string; key: string; kind: RuleColumnKind; dataType: RuleColumnDataType; headerNames?: string | null; description?: string | null };
export type UpdateRuleColumnInput = Omit<CreateRuleColumnInput, "kind"> & { expectedVersion: number; isActive: boolean };

export type DocumentSettingDto = { id: string; documentCode: string; name: string; isEnabled: boolean; sortOrder: number; defaultDisposition: TemplateDocumentDisposition; ruleDefinitionIds: string[]; updatedAtUtc: string; version: number };
export type UpdateDocumentSettingInput = { expectedVersion: number; name: string; isEnabled: boolean; sortOrder: number; defaultDisposition: TemplateDocumentDisposition; ruleDefinitionIds: string[] };
export type RuleLookupDto = { id: string; stableCode: string; name: string; description?: string | null; category: RuleCategory; lifecycle: RuleLifecycle; isLive: boolean; latestOutcomeJson?: string | null };
export type TemplateSetupDocument = { documentCode: string; isEnabled: boolean; disposition: TemplateDocumentDisposition; sortOrder: number };
export type TemplateSetupRule = { documentCode: string; ruleDefinitionId: string; isEnabled: boolean };
export type TemplateSetupDto = { id: string; stableCode: string; name: string; description?: string | null; lifecycle: RuleLifecycle; currentPublishedVersionId?: string | null; updatedAtUtc: string; version: number; documents: TemplateSetupDocument[]; rules: TemplateSetupRule[] };
export type TemplateSetupSummaryDto = { id: string; stableCode: string; name: string; description?: string | null; lifecycle: RuleLifecycle; currentPublishedVersionId?: string | null; enabledDocumentCount: number; documentCount: number; enabledRuleCount: number; ruleCount: number; updatedAtUtc: string; version: number };
export type TemplateSetupSaveResultDto = { templateId: string; publishedVersionId: string; version: number; skippedRuleDefinitionIds: string[] };
export type SaveTemplateSetupInput = { expectedVersion: number; name: string; description?: string | null; documents: TemplateSetupDocument[]; rules: TemplateSetupRule[] };

const BASE = "/api/v1/rules";

export const getDocumentSettings = () => apiFetch<DocumentSettingDto[]>(`${BASE}/document-settings`);
export const updateDocumentSetting = (id: string, input: UpdateDocumentSettingInput) => apiFetch<DocumentSettingDto>(`${BASE}/document-settings/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(input) });
export const getRuleLookup = () => apiFetch<RuleLookupDto[]>(`${BASE}/definitions/lookup`);
export function searchTemplateSetups(params: { search?: string; pageNumber?: number; pageSize?: number; sort?: "name" | "-name" | "-updated" } = {}) {
  const value = new URLSearchParams({ PageNumber: String(params.pageNumber ?? 1), PageSize: String(params.pageSize ?? 50) });
  if (params.search?.trim()) value.set("Search", params.search.trim());
  if (params.sort) value.set("Sort", params.sort);
  return apiFetch<PagedResponse<TemplateSetupSummaryDto>>(`${BASE}/template-setups?${value.toString()}`);
}
export const getTemplateSetup = (id: string) => apiFetch<TemplateSetupDto>(`${BASE}/template-setups/${encodeURIComponent(id)}`);
export const createTemplateFromDocumentSettings = (input: { stableCode: string; name: string; description?: string | null }) =>
  apiFetch<TemplateSetupSaveResultDto>(`${BASE}/template-setups`, { method: "POST", body: JSON.stringify(input) });
export const saveTemplateSetup = (id: string, input: SaveTemplateSetupInput) =>
  apiFetch<TemplateSetupSaveResultDto>(`${BASE}/template-setups/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(input) });
function query(params: SearchRulesParams | SearchTemplatesParams) {
  const value = new URLSearchParams({ PageNumber: String(params.pageNumber ?? 1), PageSize: String(params.pageSize ?? 20) });
  if (params.search?.trim()) value.set("Search", params.search.trim());
  if ("category" in params && params.category) value.set("Category", params.category);
  if ("categories" in params && params.categories?.length) value.set("Categories", params.categories.join(","));
  if ("severity" in params && params.severity) value.set("Severity", params.severity);
  if ("tag" in params && params.tag?.trim()) value.set("Tag", params.tag.trim());
  if ("archived" in params && params.archived) value.set("Archived", params.archived);
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
export const deleteRule = (id: string, expectedVersion: number) => apiFetch<DeleteRuleResultDto>(`${BASE}/definitions/${encodeURIComponent(id)}?expectedVersion=${expectedVersion}`, { method: "DELETE" });
export const restoreRule = (id: string, expectedVersion: number) => apiFetch<void>(`${BASE}/definitions/${encodeURIComponent(id)}/restore?expectedVersion=${expectedVersion}`, { method: "POST" });
export const isRuleDeletable = (rule: Pick<RuleDefinitionSummaryDto, "isArchived">) => !rule.isArchived;
export const testRule = (input: TestRuleInput) => apiFetch<RuleEvaluationResultDto>(`${BASE}/definitions/test`, body(input));
export function searchRuleColumns(params: SearchRuleColumnsParams = {}) {
  const value = new URLSearchParams({ PageNumber: String(params.pageNumber ?? 1), PageSize: String(params.pageSize ?? 50) });
  if (params.search?.trim()) value.set("Search", params.search.trim());
  if (params.kind) value.set("Kind", params.kind);
  if (params.dataType) value.set("DataType", params.dataType);
  if (params.isActive !== undefined) value.set("IsActive", String(params.isActive));
  if (params.sort) value.set("Sort", params.sort);
  return apiFetch<PagedResponse<RuleColumnDto>>(`${BASE}/columns?${value.toString()}`);
}
export const createRuleColumn = (input: CreateRuleColumnInput) => apiFetch<string>(`${BASE}/columns`, body(input));
export const updateRuleColumn = (id: string, input: UpdateRuleColumnInput) => apiFetch<void>(`${BASE}/columns/${encodeURIComponent(id)}`, { method: "PUT", body: JSON.stringify(input) });
export const deleteRuleColumn = (id: string) => apiFetch<void>(`${BASE}/columns/${encodeURIComponent(id)}`, { method: "DELETE" });
export const searchTemplates = (params: SearchTemplatesParams = {}) => apiFetch<PagedResponse<RuleTemplateSummaryDto>>(`${BASE}/templates?${query(params)}`);
export const getTemplate = (id: string) => apiFetch<RuleTemplateDetailDto>(`${BASE}/templates/${encodeURIComponent(id)}`);
export const createTemplate = (input: CreateTemplateInput) => apiFetch<string>(`${BASE}/templates`, body(input));
export const configureTemplateDraft = (id: string, input: ConfigureTemplateDraftInput) => apiFetch<void>(`${BASE}/templates/${encodeURIComponent(id)}/draft`, { method: "PUT", body: JSON.stringify(input) });
export const submitTemplateForReview = (id: string, input: RuleVersionTransitionInput) => apiFetch<void>(`${BASE}/templates/${encodeURIComponent(id)}/submit-review`, body(input));
export const publishTemplate = (id: string, input: RuleVersionTransitionInput & { retireCurrent?: boolean }) => apiFetch<void>(`${BASE}/templates/${encodeURIComponent(id)}/publish`, body(input));
export const retireTemplate = (id: string, input: RuleVersionTransitionInput) => apiFetch<void>(`${BASE}/templates/${encodeURIComponent(id)}/retire`, body(input));
export const cloneTemplate = (id: string, input: RuleVersionTransitionInput) => apiFetch<string>(`${BASE}/templates/${encodeURIComponent(id)}/clone`, body(input));
