import { useEffect, useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { Slot } from "@radix-ui/react-slot";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Controller, useForm } from "react-hook-form";
import { BookOpenCheck, ChevronRight, FlaskConical, Plus, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { z } from "zod";
import {
  cloneRule,
  createRule,
  getRule,
  publishRule,
  retireRule,
  searchRules,
  submitRuleForReview,
  testRule,
  type RuleCategory,
  type RuleDefinitionSummaryDto,
  type RuleEvaluationResultDto,
  type RuleLifecycle,
  type RuleVersionMetadataDto,
} from "@/api/rules";
import { useAuth } from "@/auth/use-auth";
import { EmptyState } from "@/components/empty-state";
import { EntityPageHeader, ErrorBand, Field, FilterBar, LoadingRow, Pagination, Select } from "@/components/list";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiRequestError } from "@/lib/api-client";
import { cn } from "@/lib/cn";
import { RulesPermissions } from "@/lib/permissions";

const PAGE_SIZE = 20;
const JSON_LIMIT = 262_144;
const PARAMETERS_JSON_LIMIT = 16 * 1024;
const categories: RuleCategory[] = ["Header", "Row", "Document", "CrossDocument", "Drawing", "ModelInference", "Special", "Custom"];
const lifecycles: RuleLifecycle[] = ["Draft", "InReview", "Published", "Retired"];
const operators = ["Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "Contains", "In", "IsEmpty", "IsNotEmpty"] as const;
const rightOperandSources = ["literal", "parameter"] as const;
const parameterNamePattern = /^[A-Za-z0-9_.-]{1,64}$/;
const jsonObject = (limit: number) => z.string().min(1, "Enter a JSON object.").max(limit, "JSON is too large.").refine((value) => {
  try { return JSON.parse(value) !== null && !Array.isArray(JSON.parse(value)) && typeof JSON.parse(value) === "object"; } catch { return false; }
}, "Enter a valid JSON object.");
const parametersJsonObject = z.string().refine((value) => normalizeParametersJson(value) !== null, "Enter a valid JSON object.").refine((value) => {
  const normalized = normalizeParametersJson(value);
  return normalized !== null && new TextEncoder().encode(normalized).length <= PARAMETERS_JSON_LIMIT;
}, "Parameters JSON must be 16 KiB or smaller.");

const createSchema = z.object({
  stableCode: z.string().trim().min(1, "Stable code is required.").max(64).regex(/^[A-Za-z0-9_.-]+$/, "Use letters, numbers, dots, underscores, or hyphens."),
  name: z.string().trim().min(1, "Name is required.").max(200),
  description: z.string().trim().max(2000).optional(),
  category: z.enum(categories as [RuleCategory, ...RuleCategory[]]),
  documentCode: z.string().trim().min(1, "Document code is required.").max(64),
  fieldPath: z.string().trim().min(1, "Field path is required.").max(512).regex(/^[^.$\[\]]+(\.[^.$\[\]]+)*$/, "Use dot-separated property names."),
  operator: z.enum(operators),
  rightOperandSource: z.enum(rightOperandSources),
  literalJson: z.string().max(65_536, "JSON is too large."),
  parameterName: z.string().trim().max(64),
  outcomeMessage: z.string().trim().min(1, "Outcome message is required.").max(1000),
}).superRefine((values, context) => {
  if (isUnaryOperator(values.operator)) return;
  if (values.rightOperandSource === "parameter") {
    if (!parameterNamePattern.test(values.parameterName)) context.addIssue({ code: z.ZodIssueCode.custom, path: ["parameterName"], message: "Use 1-64 ASCII letters, numbers, dots, underscores, or hyphens." });
    return;
  }
  if (!values.literalJson) {
    context.addIssue({ code: z.ZodIssueCode.custom, path: ["literalJson"], message: "Enter a JSON value." });
    return;
  }
  try { JSON.parse(values.literalJson); } catch { context.addIssue({ code: z.ZodIssueCode.custom, path: ["literalJson"], message: "Enter valid JSON." }); }
});
type CreateValues = z.infer<typeof createSchema>;

const testSchema = z.object({
  documentCode: z.string().trim().min(1, "Document code is required.").max(64),
  documentJson: jsonObject(JSON_LIMIT),
  parametersJson: parametersJsonObject,
});
type TestValues = z.infer<typeof testSchema>;

type PendingAction = { kind: "submit" | "publish" | "retire" | "clone"; version: RuleVersionMetadataDto };

export function RulesCataloguePage() {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const permissions = user?.permissions ?? [];
  const can = (permission: string) => permissions.includes(permission);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState<RuleCategory | "">("");
  const [status, setStatus] = useState<RuleLifecycle | "">("");
  const [createOpen, setCreateOpen] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ["rules", { page, search, category, status }],
    queryFn: () => searchRules({ pageNumber: page, pageSize: PAGE_SIZE, search: search || undefined, category: category || undefined, status: status || undefined }),
    placeholderData: keepPreviousData,
  });
  const items = query.data?.items ?? [];

  return (
    <div className="space-y-8">
      <EntityPageHeader icon={BookOpenCheck} title="Rules" total={query.data?.totalCount ?? null} unit="definition" description="Author and govern deterministic document rules through review, publication, retirement, and safe test execution.">
        <Button variant="outline" size="sm" disabled={query.isFetching} onClick={() => query.refetch()}><RefreshCw className={cn("mr-1.5 h-3.5 w-3.5", query.isFetching && "animate-spin")} />Refresh</Button>
        {can(RulesPermissions.Definitions.Create) && <Button onClick={() => setCreateOpen(true)}><Plus className="mr-1 h-4 w-4" />New rule</Button>}
      </EntityPageHeader>

      <FilterBar>
        <div className="min-w-[16rem] flex-1"><Label htmlFor="rules-search" className="sr-only">Search rules</Label><Input id="rules-search" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Search code or name" className="h-8" /></div>
        <div className="min-w-[11rem]"><Label htmlFor="rules-category" className="sr-only">Rule category</Label><Select id="rules-category" value={category} onValueChange={(value) => { setCategory(value); setPage(1); }} options={categories.map((value) => ({ value, label: value }))} emptyLabel="All categories" /></div>
        <div className="min-w-[10rem]"><Label htmlFor="rules-status" className="sr-only">Rule status</Label><Select id="rules-status" value={status} onValueChange={(value) => { setStatus(value); setPage(1); }} options={lifecycles.map((value) => ({ value, label: value }))} emptyLabel="All statuses" /></div>
      </FilterBar>

      {query.isError && <ErrorBand message={describe(query.error)} />}
      {query.isLoading && <LoadingRow label="Loading rules" />}
      {!query.isLoading && !query.isError && items.length === 0 && <EmptyState icon={BookOpenCheck} kicker="// no rules" title="No rule definitions match." description="Create a comparison rule or adjust the catalogue filters." action={can(RulesPermissions.Definitions.Create) ? <Button onClick={() => setCreateOpen(true)}>Create rule</Button> : undefined} />}
      {items.length > 0 && <ol className="divide-y divide-[var(--color-border)] border-y border-[var(--color-border)]">{items.map((rule) => <RuleRow key={rule.id} rule={rule} onOpen={() => setSelectedId(rule.id)} />)}</ol>}
      {query.data && query.data.totalPages > 1 && <Pagination page={query.data.pageNumber} totalPages={query.data.totalPages} totalCount={query.data.totalCount} shown={items.length} fetching={query.isFetching} hasPrev={query.data.hasPrevious} hasNext={query.data.hasNext} onPrev={() => setPage((value) => Math.max(1, value - 1))} onNext={() => setPage((value) => value + 1)} noun="rules" />}

      <CreateRuleDialog open={createOpen} onOpenChange={setCreateOpen} onCreated={(id) => { queryClient.invalidateQueries({ queryKey: ["rules"] }); setSelectedId(id); }} />
      <RuleDetailDialog ruleId={selectedId} onClose={() => setSelectedId(null)} />
    </div>
  );
}

function RuleRow({ rule, onOpen }: { rule: RuleDefinitionSummaryDto; onOpen: () => void }) {
  return <li><button type="button" onClick={onOpen} className="group grid w-full grid-cols-1 gap-2 px-2 py-4 text-left hover:bg-[var(--color-muted)]/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-ring)] sm:grid-cols-[minmax(9rem,0.8fr)_minmax(12rem,1.5fr)_auto_auto_auto] sm:items-center sm:gap-4">
    <code className="truncate text-xs text-[var(--color-muted-foreground)]">{rule.stableCode}</code>
    <span className="min-w-0"><span className="block truncate text-sm font-medium">{rule.name}</span><span className="block truncate text-xs text-[var(--color-muted-foreground)]">{rule.description || "No description"}</span></span>
    <Badge variant="outline">{rule.category}</Badge><LifecycleBadge status={rule.lifecycle} /><span className="flex items-center justify-end gap-2 font-mono text-xs text-[var(--color-muted-foreground)]">v{rule.version}<ChevronRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" /></span>
  </button></li>;
}

function CreateRuleDialog({ open, onOpenChange, onCreated }: { open: boolean; onOpenChange: (open: boolean) => void; onCreated: (id: string) => void }) {
  const form = useForm<CreateValues>({ resolver: zodResolver(createSchema), defaultValues: { stableCode: "", name: "", description: "", category: "Document", documentCode: "", fieldPath: "", operator: "Equal", rightOperandSource: "literal", literalJson: "null", parameterName: "", outcomeMessage: "" } });
  const operator = form.watch("operator");
  const rightOperandSource = form.watch("rightOperandSource");
  const mutation = useMutation({
    mutationFn: (values: CreateValues) => createRule({ stableCode: values.stableCode.trim(), name: values.name.trim(), description: values.description?.trim() || null, category: values.category, astJson: buildComparisonAst(values), outcomeJson: JSON.stringify({ message: values.outcomeMessage.trim() }) }),
    onSuccess: (id) => { toast.success("Rule created"); form.reset(); onOpenChange(false); onCreated(id); },
    onError: (error) => toast.error("Create failed", { description: describe(error) }),
  });
  const close = () => { form.reset(); onOpenChange(false); };
  return <Dialog open={open} onOpenChange={(value) => value ? onOpenChange(true) : close()}><DialogContent size="lg"><DialogHeader><DialogTitle>New comparison rule</DialogTitle><DialogDescription>Build one constrained comparison node. The generated AST contains data only and cannot execute arbitrary code.</DialogDescription></DialogHeader><form onSubmit={form.handleSubmit((values) => mutation.mutate(values))}><DialogBody className="grid gap-4 sm:grid-cols-2">
    <Field id="rule-code" label="Stable code" required error={form.formState.errors.stableCode?.message}><Input id="rule-code" {...form.register("stableCode")} /></Field>
    <Field id="rule-name" label="Name" required error={form.formState.errors.name?.message}><Input id="rule-name" {...form.register("name")} /></Field>
    <Field id="rule-description" label="Description" error={form.formState.errors.description?.message} className="sm:col-span-2"><Input id="rule-description" {...form.register("description")} /></Field>
    <Field id="rule-category" label="Category" required error={form.formState.errors.category?.message}><Controller control={form.control} name="category" render={({ field }) => <Select id="rule-category" value={field.value} onValueChange={field.onChange} options={categories.map((value) => ({ value, label: value }))} />} /></Field>
    <Field id="rule-document" label="Document code" required error={form.formState.errors.documentCode?.message}><Input id="rule-document" {...form.register("documentCode")} /></Field>
    <Field id="rule-path" label="Field path" required hint="Dot-separated object properties." error={form.formState.errors.fieldPath?.message}><Input id="rule-path" placeholder="header.partNumber" {...form.register("fieldPath")} /></Field>
    <Field id="rule-operator" label="Operator" required error={form.formState.errors.operator?.message}><Controller control={form.control} name="operator" render={({ field }) => <Select id="rule-operator" value={field.value} onValueChange={field.onChange} options={operators.map((value) => ({ value, label: value }))} />} /></Field>
    {!isUnaryOperator(operator) && <Field id="rule-right-source" label="Right operand source" required error={form.formState.errors.rightOperandSource?.message}><Controller control={form.control} name="rightOperandSource" render={({ field }) => <Select id="rule-right-source" value={field.value} onValueChange={field.onChange} options={[{ value: "literal", label: "Literal" }, { value: "parameter", label: "Template parameter" }]} />} /></Field>}
    {!isUnaryOperator(operator) && rightOperandSource === "literal" && <Field id="rule-literal" label="Literal JSON" required error={form.formState.errors.literalJson?.message} className="sm:col-span-2"><textarea id="rule-literal" rows={3} className={textareaClass} {...form.register("literalJson")} /></Field>}
    {!isUnaryOperator(operator) && rightOperandSource === "parameter" && <Field id="rule-parameter" label="Template parameter name" required hint="1-64 ASCII letters, numbers, dots, underscores, hyphens." error={form.formState.errors.parameterName?.message} className="sm:col-span-2"><Input id="rule-parameter" {...form.register("parameterName")} /></Field>}
    <Field id="rule-outcome" label="Outcome message" required error={form.formState.errors.outcomeMessage?.message} className="sm:col-span-2"><Input id="rule-outcome" {...form.register("outcomeMessage")} /></Field>
  </DialogBody><DialogFooter><Button type="button" variant="outline" onClick={close} disabled={mutation.isPending}>Cancel</Button><Button type="submit" disabled={mutation.isPending}>{mutation.isPending ? "Creating…" : "Create rule"}</Button></DialogFooter></form></DialogContent></Dialog>;
}

function RuleDetailDialog({ ruleId, onClose }: { ruleId: string | null; onClose: () => void }) {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const permissions = user?.permissions ?? [];
  const [selectedVersionId, setSelectedVersionId] = useState("");
  const [pending, setPending] = useState<PendingAction | null>(null);
  const query = useQuery({ queryKey: ["rule", ruleId], queryFn: () => getRule(ruleId!), enabled: Boolean(ruleId) });
  useEffect(() => { if (query.data?.versions[0]) setSelectedVersionId(query.data.versions[0].id); }, [query.data]);
  const selected = query.data?.versions.find((version) => version.id === selectedVersionId) ?? query.data?.versions[0];
  const transition = useMutation({
    mutationFn: async ({ action, id, versionId, expectedAggregateVersion }: { action: PendingAction["kind"]; id: string; versionId: string; expectedAggregateVersion: number }) => {
      const input = { versionId, expectedAggregateVersion };
      if (action === "submit") await submitRuleForReview(id, input);
      else if (action === "publish") await publishRule(id, { ...input, retireCurrent: true });
      else if (action === "retire") await retireRule(id, input);
      else await cloneRule(id, input);
    },
    onSuccess: (_data, variables) => { toast.success(actionLabel(variables.action, true)); setPending(null); queryClient.invalidateQueries({ queryKey: ["rule", ruleId] }); queryClient.invalidateQueries({ queryKey: ["rules"] }); },
    onError: (error) => toast.error("Action failed", { description: describe(error) }),
  });
  const can = (permission: string) => permissions.includes(permission);
  return <Dialog open={Boolean(ruleId)} onOpenChange={(open) => { if (!open) onClose(); }}><DialogContent size="xl"><DialogHeader><DialogTitle>{query.data?.name ?? "Rule detail"}</DialogTitle><DialogDescription>{query.data ? `${query.data.stableCode} · ${query.data.category} · aggregate v${query.data.version}` : "Loading rule definition…"}</DialogDescription></DialogHeader><DialogBody className="space-y-5">
    {query.isLoading && <LoadingRow label="Loading rule detail" />}{query.isError && <ErrorBand message={describe(query.error)} />}
    {query.data && <><div className="flex flex-wrap items-center gap-2"><LifecycleBadge status={query.data.lifecycle} />{query.data.versions.map((version) => <Button key={version.id} type="button" size="sm" variant={version.id === selected?.id ? "default" : "outline"} onClick={() => setSelectedVersionId(version.id)}>v{version.versionNumber} · {version.status}</Button>)}</div>
      {selected && <><div className="flex flex-wrap gap-2">
        {selected.status === "Draft" && can(RulesPermissions.Definitions.Review) && <Button size="sm" onClick={() => setPending({ kind: "submit", version: selected })}>Submit review</Button>}
        {selected.status === "InReview" && can(RulesPermissions.Definitions.Publish) && <Button size="sm" onClick={() => setPending({ kind: "publish", version: selected })}>Publish</Button>}
        {selected.status === "Published" && can(RulesPermissions.Definitions.Retire) && <Button size="sm" variant="destructive" onClick={() => setPending({ kind: "retire", version: selected })}>Retire</Button>}
        {selected.status === "Published" && can(RulesPermissions.Definitions.Edit) && <Button size="sm" variant="outline" onClick={() => setPending({ kind: "clone", version: selected })}>Clone draft</Button>}
      </div><section><h3 id="rule-ast-heading" className="mb-2 text-sm font-semibold">AST · read only</h3><Slot role="region" tabIndex={0} aria-labelledby="rule-ast-heading"><pre className="max-h-64 overflow-auto rounded-lg border border-[var(--color-border)] bg-[var(--color-muted)]/40 p-4 font-mono text-xs whitespace-pre-wrap outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-ring)] focus-visible:ring-offset-2">{formatJson(selected.astJson)}</pre></Slot></section>
        {can(RulesPermissions.Definitions.Test) && selected.astJson && <TestWorkbench astJson={selected.astJson} />}</>}
    </>}
  </DialogBody></DialogContent><ConfirmDialog open={Boolean(pending)} onOpenChange={(open) => { if (!open) setPending(null); }} title={pending ? actionLabel(pending.kind, false) : "Confirm action"} description={pending ? `${actionLabel(pending.kind, false)} version ${pending.version.versionNumber}?` : ""} confirmLabel={pending ? actionLabel(pending.kind, false) : "Confirm"} destructive={pending?.kind === "retire"} pending={transition.isPending} onConfirm={() => { if (pending && ruleId && query.data) transition.mutate({ action: pending.kind, id: ruleId, versionId: pending.version.id, expectedAggregateVersion: query.data.version }); }} /></Dialog>;
}

function TestWorkbench({ astJson }: { astJson: string }) {
  const form = useForm<TestValues>({ resolver: zodResolver(testSchema), defaultValues: { documentCode: "", documentJson: "{}", parametersJson: "{}" } });
  const [result, setResult] = useState<RuleEvaluationResultDto | null>(null);
  const mutation = useMutation({ mutationFn: (values: TestValues) => testRule({ astJson, documents: { [values.documentCode.trim()]: JSON.stringify(JSON.parse(values.documentJson)) }, parametersJson: normalizeParametersJson(values.parametersJson) ?? "{}" }), onSuccess: setResult, onError: (error) => toast.error("Test failed", { description: describe(error) }) });
  return <section className="space-y-3 border-t border-[var(--color-border)] pt-5"><div><h3 className="flex items-center gap-2 text-sm font-semibold"><FlaskConical className="h-4 w-4" />Test workbench</h3><p className="text-xs text-[var(--color-muted-foreground)]">Supply one canonical JSON document object, up to 256 KB, and optional parameters up to 16 KiB.</p></div><form className="space-y-3" onSubmit={form.handleSubmit((values) => mutation.mutate(values))}><Field id="test-document-code" label="Document code" required error={form.formState.errors.documentCode?.message}><Input id="test-document-code" {...form.register("documentCode")} /></Field><Field id="test-document-json" label="Document JSON object" required error={form.formState.errors.documentJson?.message}><textarea id="test-document-json" rows={6} className={textareaClass} {...form.register("documentJson")} /></Field><Field id="test-parameters-json" label="Parameters JSON object" hint="Optional" error={form.formState.errors.parametersJson?.message}><textarea id="test-parameters-json" rows={4} className={textareaClass} {...form.register("parametersJson")} /></Field><Button type="submit" size="sm" disabled={mutation.isPending}>{mutation.isPending ? "Running…" : "Run test"}</Button></form>{result && <div aria-live="polite" className="space-y-2 rounded-lg border border-[var(--color-border)] p-4"><div className="flex items-center gap-2"><strong>Result</strong><EvaluationBadge status={result.status} /></div>{result.reason && <p className="text-sm">{result.reason}</p>}<Slot role="region" tabIndex={0} aria-label="Rule evaluation trace"><ol className="max-h-48 space-y-2 overflow-auto rounded-sm outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-ring)] focus-visible:ring-offset-2">{result.trace.slice(0, 100).map((trace, index) => <li key={`${trace.node}-${index}`} className="rounded bg-[var(--color-muted)]/40 p-2 font-mono text-xs"><span className="font-semibold">{trace.node}</span>{trace.operator ? ` · ${trace.operator}` : ""} · {trace.status}{trace.reason ? ` · ${trace.reason}` : ""}</li>)}</ol></Slot></div>}</section>;
}

function buildComparisonAst(values: CreateValues): string {
  const ast: Record<string, unknown> = { type: "comparison", operator: values.operator, left: { type: "field", documentCode: values.documentCode.trim(), path: values.fieldPath.trim() } };
  if (!isUnaryOperator(values.operator)) ast.right = values.rightOperandSource === "parameter" ? { type: "parameter", name: values.parameterName.trim() } : { type: "literal", value: JSON.parse(values.literalJson) };
  return JSON.stringify(ast);
}

function isUnaryOperator(operator: CreateValues["operator"]): boolean { return operator === "IsEmpty" || operator === "IsNotEmpty"; }
function normalizeParametersJson(value: string): string | null { try { const parsed = JSON.parse(value.trim() || "{}"); return parsed !== null && !Array.isArray(parsed) && typeof parsed === "object" ? JSON.stringify(parsed) : null; } catch { return null; } }

function LifecycleBadge({ status }: { status: RuleLifecycle }) { return <Badge variant={status === "Published" ? "success" : status === "InReview" ? "info" : status === "Retired" ? "muted" : "outline"}>{status}</Badge>; }
function EvaluationBadge({ status }: { status: RuleEvaluationResultDto["status"] }) { return <Badge variant={status === "Passed" ? "success" : status === "Failed" || status === "Error" ? "danger" : "muted"}>{status}</Badge>; }
function formatJson(value?: string | null) { if (!value) return "AST unavailable"; try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; } }
function actionLabel(action: PendingAction["kind"], past: boolean) { const labels = { submit: past ? "Submitted for review" : "Submit for review", publish: past ? "Rule published" : "Publish rule", retire: past ? "Rule retired" : "Retire rule", clone: past ? "Draft cloned" : "Clone draft" }; return labels[action]; }
function describe(error: unknown) { if (error instanceof ApiRequestError) return error.problem?.detail ?? error.problem?.title ?? error.message; return error instanceof Error ? error.message : String(error); }
const textareaClass = "flex min-h-20 w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 font-mono text-xs shadow-xs outline-none focus-visible:border-[var(--color-ring)] focus-visible:ring-2 focus-visible:ring-[var(--color-ring)]/30";
