import { apiFetch } from "@/lib/api-client";
import type { PagedResponse } from "@/lib/api-types";

export type { PagedResponse } from "@/lib/api-types";

export type RuleCategory =
  | "Header"
  | "Row"
  | "Document"
  | "CrossDocument"
  | "Drawing"
  | "ModelInference"
  | "Special"
  | "Custom";
export type RuleLifecycle = "Draft" | "InReview" | "Published" | "Retired";
export type TemplateDocumentDisposition = "Submit" | "Retain" | "Available";
export type RuleEvaluationStatus = "Passed" | "Failed" | "NotEvaluated" | "Error";

export type RuleVersionMetadataDto = {
  id: string;
  versionNumber: number;
  status: RuleLifecycle;
  createdAtUtc: string;
  publishedAtUtc?: string | null;
  retiredAtUtc?: string | null;
  createdByUserId: string;
  astJson?: string | null;
};

export type RuleDefinitionSummaryDto = {
  id: string;
  stableCode: string;
  name: string;
  description?: string | null;
  category: RuleCategory;
  lifecycle: RuleLifecycle;
  currentPublishedVersionId?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
};

export type RuleDefinitionDetailDto = RuleDefinitionSummaryDto & {
  versions: RuleVersionMetadataDto[];
};

export type RuleTraceDto = {
  node: string;
  operator?: string | null;
  left?: string | null;
  right?: string | null;
  status: RuleEvaluationStatus;
  reason?: string | null;
};

export type RuleEvaluationResultDto = {
  status: RuleEvaluationStatus;
  reason?: string | null;
  trace: RuleTraceDto[];
};

export type SearchRulesParams = {
  search?: string;
  category?: RuleCategory;
  status?: RuleLifecycle;
  pageNumber?: number;
  pageSize?: number;
  sort?: "name" | "-name" | "created" | "-created";
};

export type CreateRuleInput = {
  stableCode: string;
  name: string;
  description?: string | null;
  category: RuleCategory;
  astJson: string;
  outcomeJson: string;
};

export type UpdateRuleDraftInput = Omit<CreateRuleInput, "stableCode"> & {
  versionId: string;
  expectedAggregateVersion: number;
};

export type RuleVersionTransitionInput = {
  versionId: string;
  expectedAggregateVersion: number;
};

export type PublishRuleInput = RuleVersionTransitionInput & { retireCurrent?: boolean };
export type TestRuleInput = { astJson: string; documents: Record<string, string>; parametersJson?: string };

export type TemplateDocumentRequirementDto = {
  id: string;
  documentCode: string;
  isRequired: boolean;
  sortOrder: number;
  disposition: TemplateDocumentDisposition;
};

export type TemplateRuleAssignmentDto = {
  id: string;
  ruleVersionId: string;
  documentCode?: string | null;
  parametersJson: string;
};

export type RuleTemplateVersionDto = {
  id: string;
  versionNumber: number;
  status: RuleLifecycle;
  createdByUserId: string;
  createdAtUtc: string;
  publishedAtUtc?: string | null;
  retiredAtUtc?: string | null;
  documentRequirements: TemplateDocumentRequirementDto[];
  ruleAssignments: TemplateRuleAssignmentDto[];
};

export type RuleTemplateSummaryDto = {
  id: string;
  stableCode: string;
  name: string;
  description?: string | null;
  lifecycle: RuleLifecycle;
  currentPublishedVersionId?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
};

export type RuleTemplateDetailDto = RuleTemplateSummaryDto & {
  versions: RuleTemplateVersionDto[];
};

export type TemplateDocumentRequirementInput = {
  documentCode: string;
  isRequired: boolean;
  sortOrder: number;
  disposition: TemplateDocumentDisposition;
};

export type TemplateRuleAssignmentInput = {
  ruleVersionId: string;
  documentCode?: string | null;
  parametersJson: string;
};

export type SearchTemplatesParams = {
  search?: string;
  status?: RuleLifecycle;
  pageNumber?: number;
  pageSize?: number;
  sort?: "name" | "-name" | "created" | "-created";
};

export type CreateTemplateInput = {
  stableCode: string;
  name: string;
  description?: string | null;
};

export type ConfigureTemplateDraftInput = {
  versionId: string;
  expectedAggregateVersion: number;
  name: string;
  description?: string | null;
  documentRequirements: TemplateDocumentRequirementInput[];
  ruleAssignments: TemplateRuleAssignmentInput[];
};

export type TemplateVersionTransitionInput = RuleVersionTransitionInput;
export type PublishTemplateInput = TemplateVersionTransitionInput & { retireCurrent?: boolean };

const ROOT = "/api/v1/rules";

function searchQuery(params: SearchRulesParams | SearchTemplatesParams): string {
  const query = new URLSearchParams();
  query.set("PageNumber", String(params.pageNumber ?? 1));
  query.set("PageSize", String(params.pageSize ?? 20));
  if (params.search?.trim()) query.set("Search", params.search.trim());
  if ("category" in params && params.category) query.set("Category", params.category);
  if (params.status) query.set("Status", params.status);
  if (params.sort) query.set("Sort", params.sort);
  return query.toString();
}

export function searchRules(params: SearchRulesParams = {}): Promise<PagedResponse<RuleDefinitionSummaryDto>> {
  return apiFetch(`${ROOT}/definitions?${searchQuery(params)}`);
}

export function getRule(id: string): Promise<RuleDefinitionDetailDto> {
  return apiFetch(`${ROOT}/definitions/${encodeURIComponent(id)}`);
}

export function createRule(input: CreateRuleInput): Promise<string> {
  return apiFetch(`${ROOT}/definitions`, { method: "POST", body: JSON.stringify(input) });
}

export function updateRuleDraft(id: string, input: UpdateRuleDraftInput): Promise<void> {
  return apiFetch(`${ROOT}/definitions/${encodeURIComponent(id)}/draft`, { method: "PUT", body: JSON.stringify(input) });
}

export function submitRuleForReview(id: string, input: RuleVersionTransitionInput): Promise<void> {
  return apiFetch(`${ROOT}/definitions/${encodeURIComponent(id)}/submit-review`, { method: "POST", body: JSON.stringify(input) });
}

export function publishRule(id: string, input: PublishRuleInput): Promise<void> {
  return apiFetch(`${ROOT}/definitions/${encodeURIComponent(id)}/publish`, { method: "POST", body: JSON.stringify(input) });
}

export function retireRule(id: string, input: RuleVersionTransitionInput): Promise<void> {
  return apiFetch(`${ROOT}/definitions/${encodeURIComponent(id)}/retire`, { method: "POST", body: JSON.stringify(input) });
}

export function cloneRule(id: string, input: RuleVersionTransitionInput): Promise<string> {
  return apiFetch(`${ROOT}/definitions/${encodeURIComponent(id)}/clone`, { method: "POST", body: JSON.stringify(input) });
}

export function testRule(input: TestRuleInput): Promise<RuleEvaluationResultDto> {
  return apiFetch(`${ROOT}/definitions/test`, { method: "POST", body: JSON.stringify(input) });
}

export function searchTemplates(params: SearchTemplatesParams = {}): Promise<PagedResponse<RuleTemplateSummaryDto>> {
  return apiFetch(`${ROOT}/templates?${searchQuery(params)}`);
}

export function getTemplate(id: string): Promise<RuleTemplateDetailDto> {
  return apiFetch(`${ROOT}/templates/${encodeURIComponent(id)}`);
}

export function createTemplate(input: CreateTemplateInput): Promise<string> {
  return apiFetch(`${ROOT}/templates`, { method: "POST", body: JSON.stringify(input) });
}

export function configureTemplateDraft(id: string, input: ConfigureTemplateDraftInput): Promise<void> {
  return apiFetch(`${ROOT}/templates/${encodeURIComponent(id)}/draft`, { method: "PUT", body: JSON.stringify(input) });
}

export function submitTemplateForReview(id: string, input: TemplateVersionTransitionInput): Promise<void> {
  return apiFetch(`${ROOT}/templates/${encodeURIComponent(id)}/submit-review`, { method: "POST", body: JSON.stringify(input) });
}

export function publishTemplate(id: string, input: PublishTemplateInput): Promise<void> {
  return apiFetch(`${ROOT}/templates/${encodeURIComponent(id)}/publish`, { method: "POST", body: JSON.stringify(input) });
}

export function retireTemplate(id: string, input: TemplateVersionTransitionInput): Promise<void> {
  return apiFetch(`${ROOT}/templates/${encodeURIComponent(id)}/retire`, { method: "POST", body: JSON.stringify(input) });
}

export function cloneTemplate(id: string, input: TemplateVersionTransitionInput): Promise<string> {
  return apiFetch(`${ROOT}/templates/${encodeURIComponent(id)}/clone`, { method: "POST", body: JSON.stringify(input) });
}
