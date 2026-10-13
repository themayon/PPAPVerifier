import { useEffect, useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Controller, useFieldArray, useForm } from "react-hook-form";
import { ChevronRight, Plus, RefreshCw, Trash2, PanelsTopLeft } from "lucide-react";
import { toast } from "sonner";
import { z } from "zod";
import {
  cloneTemplate,
  configureTemplateDraft,
  createTemplate,
  getTemplate,
  publishTemplate,
  retireTemplate,
  searchTemplates,
  submitTemplateForReview,
  type RuleLifecycle,
  type RuleTemplateSummaryDto,
  type RuleTemplateVersionDto,
} from "@/api/rules";
import { useAuth } from "@/auth/use-auth";
import { EmptyState } from "@/components/empty-state";
import { EntityPageHeader, ErrorBand, Field, FilterBar, LoadingRow, Pagination, Select } from "@/components/list";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiRequestError } from "@/lib/api-client";
import { cn } from "@/lib/cn";
import { RulesPermissions } from "@/lib/permissions";

const PAGE_SIZE = 20;
const lifecycles: RuleLifecycle[] = ["Draft", "InReview", "Published", "Retired"];
const stableCode = z.string().trim().min(1, "Stable code is required.").max(64).regex(/^[A-Za-z0-9_.-]+$/, "Use letters, numbers, dots, underscores, or hyphens.");
const createSchema = z.object({ stableCode, name: z.string().trim().min(1, "Name is required.").max(200), description: z.string().trim().max(2000).optional() });
type CreateValues = z.infer<typeof createSchema>;
const jsonObject = z.string().min(1).max(16 * 1024, "Keep parameters under 16 KB.").refine((value) => { try { const parsed = JSON.parse(value); return parsed !== null && typeof parsed === "object" && !Array.isArray(parsed); } catch { return false; } }, "Enter a valid JSON object.");
const configureSchema = z.object({
  name: z.string().trim().min(1, "Name is required.").max(200),
  description: z.string().trim().max(2000).optional(),
  documentRequirements: z.array(z.object({ documentCode: z.string().trim().min(1, "Document code is required.").max(32).regex(/^[A-Za-z0-9_.-]+$/), disposition: z.enum(["Submit", "Retain", "Available"]), sortOrder: z.coerce.number().int().min(1).max(1000) })).max(50),
  ruleAssignments: z.array(z.object({ ruleVersionId: z.string().uuid("Enter a published rule version id."), documentCode: z.string().trim().max(32).regex(/^[A-Za-z0-9_.-]*$/), parametersJson: jsonObject })).max(500),
});
type ConfigureValues = z.infer<typeof configureSchema>;
type PendingAction = { kind: "submit" | "publish" | "retire" | "clone"; version: RuleTemplateVersionDto };

export function RuleTemplatesPage() {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const permissions = user?.permissions ?? [];
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<RuleLifecycle | "">("");
  const [createOpen, setCreateOpen] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const query = useQuery({ queryKey: ["rule-templates", { page, search, status }], queryFn: () => searchTemplates({ pageNumber: page, pageSize: PAGE_SIZE, search: search || undefined, status: status || undefined }), placeholderData: keepPreviousData });
  const items = query.data?.items ?? [];
  const canCreate = permissions.includes(RulesPermissions.Templates.Create);
  return <div className="space-y-8">
    <EntityPageHeader icon={PanelsTopLeft} title="Rule templates" total={query.data?.totalCount ?? null} unit="template" description="Compose document requirements and published rule versions into governed reusable templates."><Button variant="outline" size="sm" disabled={query.isFetching} onClick={() => query.refetch()}><RefreshCw className={cn("mr-1.5 h-3.5 w-3.5", query.isFetching && "animate-spin")} />Refresh</Button>{canCreate && <Button onClick={() => setCreateOpen(true)}><Plus className="mr-1 h-4 w-4" />New template</Button>}</EntityPageHeader>
    <FilterBar><div className="min-w-[16rem] flex-1"><Label htmlFor="templates-search" className="sr-only">Search rule templates</Label><Input id="templates-search" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Search code or name" className="h-8" /></div><div className="min-w-[10rem]"><Label htmlFor="templates-status" className="sr-only">Template status</Label><Select id="templates-status" value={status} onValueChange={(value) => { setStatus(value); setPage(1); }} options={lifecycles.map((value) => ({ value, label: value }))} emptyLabel="All statuses" /></div></FilterBar>
    {query.isError && <ErrorBand message={describe(query.error)} />}{query.isLoading && <LoadingRow label="Loading rule templates" />}
    {!query.isLoading && !query.isError && items.length === 0 && <EmptyState icon={PanelsTopLeft} kicker="// no templates" title="No rule templates match." description="Create a template or adjust the catalogue filters." action={canCreate ? <Button onClick={() => setCreateOpen(true)}>Create template</Button> : undefined} />}
    {items.length > 0 && <ol className="divide-y divide-[var(--color-border)] border-y border-[var(--color-border)]">{items.map((template) => <TemplateRow key={template.id} template={template} onOpen={() => setSelectedId(template.id)} />)}</ol>}
    {query.data && query.data.totalPages > 1 && <Pagination page={query.data.pageNumber} totalPages={query.data.totalPages} totalCount={query.data.totalCount} shown={items.length} fetching={query.isFetching} hasPrev={query.data.hasPrevious} hasNext={query.data.hasNext} onPrev={() => setPage((value) => Math.max(1, value - 1))} onNext={() => setPage((value) => value + 1)} noun="templates" />}
    <CreateTemplateDialog open={createOpen} onOpenChange={setCreateOpen} onCreated={(id) => { queryClient.invalidateQueries({ queryKey: ["rule-templates"] }); setSelectedId(id); }} /><TemplateDetailDialog templateId={selectedId} onClose={() => setSelectedId(null)} />
  </div>;
}

function TemplateRow({ template, onOpen }: { template: RuleTemplateSummaryDto; onOpen: () => void }) {
  return <li><button type="button" onClick={onOpen} className="group grid w-full grid-cols-1 gap-2 px-2 py-4 text-left hover:bg-[var(--color-muted)]/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-ring)] sm:grid-cols-[minmax(9rem,0.8fr)_minmax(12rem,1.5fr)_auto_auto] sm:items-center sm:gap-4"><code className="truncate text-xs text-[var(--color-muted-foreground)]">{template.stableCode}</code><span className="min-w-0"><span className="block truncate text-sm font-medium">{template.name}</span><span className="block truncate text-xs text-[var(--color-muted-foreground)]">{template.description || "No description"}</span></span><LifecycleBadge status={template.lifecycle} /><span className="flex items-center justify-end gap-2 font-mono text-xs text-[var(--color-muted-foreground)]">v{template.version}<ChevronRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" /></span></button></li>;
}

function CreateTemplateDialog({ open, onOpenChange, onCreated }: { open: boolean; onOpenChange: (open: boolean) => void; onCreated: (id: string) => void }) {
  const form = useForm<CreateValues>({ resolver: zodResolver(createSchema), defaultValues: { stableCode: "", name: "", description: "" } });
  const mutation = useMutation({ mutationFn: (values: CreateValues) => createTemplate({ stableCode: values.stableCode.trim(), name: values.name.trim(), description: values.description?.trim() || null }), onSuccess: (id) => { toast.success("Template created"); form.reset(); onOpenChange(false); onCreated(id); }, onError: (error) => toast.error("Create failed", { description: describe(error) }) });
  const close = () => { form.reset(); onOpenChange(false); };
  return <Dialog open={open} onOpenChange={(value) => value ? onOpenChange(true) : close()}><DialogContent size="md"><DialogHeader><DialogTitle>New rule template</DialogTitle><DialogDescription>Create the stable template identity, then configure its draft version.</DialogDescription></DialogHeader><form onSubmit={form.handleSubmit((values) => mutation.mutate(values))}><DialogBody className="space-y-4"><Field id="template-code" label="Stable code" required error={form.formState.errors.stableCode?.message}><Input id="template-code" {...form.register("stableCode")} /></Field><Field id="template-name" label="Name" required error={form.formState.errors.name?.message}><Input id="template-name" {...form.register("name")} /></Field><Field id="template-description" label="Description" error={form.formState.errors.description?.message}><Input id="template-description" {...form.register("description")} /></Field></DialogBody><DialogFooter><Button type="button" variant="outline" onClick={close} disabled={mutation.isPending}>Cancel</Button><Button type="submit" disabled={mutation.isPending}>{mutation.isPending ? "Creating…" : "Create template"}</Button></DialogFooter></form></DialogContent></Dialog>;
}

function TemplateDetailDialog({ templateId, onClose }: { templateId: string | null; onClose: () => void }) {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const permissions = user?.permissions ?? [];
  const [selectedVersionId, setSelectedVersionId] = useState("");
  const [pending, setPending] = useState<PendingAction | null>(null);
  const query = useQuery({ queryKey: ["rule-template", templateId], queryFn: () => getTemplate(templateId!), enabled: Boolean(templateId) });
  useEffect(() => { if (query.data?.versions[0]) setSelectedVersionId(query.data.versions[0].id); }, [query.data]);
  const selected = query.data?.versions.find((version) => version.id === selectedVersionId) ?? query.data?.versions[0];
  const transition = useMutation({ mutationFn: async ({ action, id, versionId, expectedAggregateVersion }: { action: PendingAction["kind"]; id: string; versionId: string; expectedAggregateVersion: number }) => { const input = { versionId, expectedAggregateVersion }; if (action === "submit") await submitTemplateForReview(id, input); else if (action === "publish") await publishTemplate(id, { ...input, retireCurrent: true }); else if (action === "retire") await retireTemplate(id, input); else await cloneTemplate(id, input); }, onSuccess: (_data, variables) => { toast.success(actionLabel(variables.action, true)); setPending(null); queryClient.invalidateQueries({ queryKey: ["rule-template", templateId] }); queryClient.invalidateQueries({ queryKey: ["rule-templates"] }); }, onError: (error) => toast.error("Action failed", { description: describe(error) }) });
  const can = (permission: string) => permissions.includes(permission);
  return <Dialog open={Boolean(templateId)} onOpenChange={(open) => { if (!open) onClose(); }}><DialogContent size="xl"><DialogHeader><DialogTitle>{query.data?.name ?? "Template detail"}</DialogTitle><DialogDescription>{query.data ? `${query.data.stableCode} · aggregate v${query.data.version}` : "Loading rule template…"}</DialogDescription></DialogHeader><DialogBody className="space-y-5">{query.isLoading && <LoadingRow label="Loading template detail" />}{query.isError && <ErrorBand message={describe(query.error)} />}{query.data && <><div className="flex flex-wrap items-center gap-2"><LifecycleBadge status={query.data.lifecycle} />{query.data.versions.map((version) => <Button key={version.id} type="button" size="sm" variant={version.id === selected?.id ? "default" : "outline"} onClick={() => setSelectedVersionId(version.id)}>v{version.versionNumber} · {version.status}</Button>)}</div>{selected && <><div className="flex flex-wrap gap-2">{selected.status === "Draft" && can(RulesPermissions.Templates.Review) && <Button size="sm" onClick={() => setPending({ kind: "submit", version: selected })}>Submit review</Button>}{selected.status === "InReview" && can(RulesPermissions.Templates.Publish) && <Button size="sm" onClick={() => setPending({ kind: "publish", version: selected })}>Publish</Button>}{selected.status === "Published" && can(RulesPermissions.Templates.Retire) && <Button size="sm" variant="destructive" onClick={() => setPending({ kind: "retire", version: selected })}>Retire</Button>}{selected.status === "Published" && can(RulesPermissions.Templates.Edit) && <Button size="sm" variant="outline" onClick={() => setPending({ kind: "clone", version: selected })}>Clone draft</Button>}</div>{selected.status === "Draft" && can(RulesPermissions.Templates.Edit) ? <ConfigureTemplateForm templateId={query.data.id} aggregateVersion={query.data.version} version={selected} name={query.data.name} description={query.data.description} /> : <TemplateVersionSummary version={selected} />}</>}</>}</DialogBody><ConfirmDialog open={Boolean(pending)} onOpenChange={(open) => { if (!open) setPending(null); }} title={pending ? actionLabel(pending.kind, false) : "Confirm action"} description={pending ? `${actionLabel(pending.kind, false)} version ${pending.version.versionNumber}?` : ""} confirmLabel={pending ? actionLabel(pending.kind, false) : "Confirm"} destructive={pending?.kind === "retire"} pending={transition.isPending} onConfirm={() => { if (pending && templateId && query.data) transition.mutate({ action: pending.kind, id: templateId, versionId: pending.version.id, expectedAggregateVersion: query.data.version }); }} /></DialogContent></Dialog>;
}

function ConfigureTemplateForm({ templateId, aggregateVersion, version, name, description }: { templateId: string; aggregateVersion: number; version: RuleTemplateVersionDto; name: string; description?: string | null }) {
  const queryClient = useQueryClient();
  const form = useForm<ConfigureValues>({ resolver: zodResolver(configureSchema), defaultValues: toFormValues(version, name, description) });
  const documents = useFieldArray({ control: form.control, name: "documentRequirements" });
  const assignments = useFieldArray({ control: form.control, name: "ruleAssignments" });
  useEffect(() => { form.reset(toFormValues(version, name, description)); }, [version, name, description, form]);
  const mutation = useMutation({ mutationFn: (values: ConfigureValues) => configureTemplateDraft(templateId, { versionId: version.id, expectedAggregateVersion: aggregateVersion, name: values.name.trim(), description: values.description?.trim() || null, documentRequirements: values.documentRequirements.map((item) => ({ documentCode: item.documentCode.trim(), disposition: item.disposition, isRequired: item.disposition !== "Retain", sortOrder: item.sortOrder })), ruleAssignments: values.ruleAssignments.map((item) => ({ ruleVersionId: item.ruleVersionId, documentCode: item.documentCode.trim() || null, parametersJson: JSON.stringify(JSON.parse(item.parametersJson)) })) }), onSuccess: () => { toast.success("Draft configured"); queryClient.invalidateQueries({ queryKey: ["rule-template", templateId] }); queryClient.invalidateQueries({ queryKey: ["rule-templates"] }); }, onError: (error) => toast.error("Configure failed", { description: describe(error) }) });
  return <form className="space-y-6 border-t border-[var(--color-border)] pt-5" onSubmit={form.handleSubmit((values) => mutation.mutate(values))}><div className="grid gap-4 sm:grid-cols-2"><Field id="configure-name" label="Name" required error={form.formState.errors.name?.message}><Input id="configure-name" {...form.register("name")} /></Field><Field id="configure-description" label="Description" error={form.formState.errors.description?.message}><Input id="configure-description" {...form.register("description")} /></Field></div>
    <section className="space-y-3"><div className="flex items-center justify-between"><div><h3 className="text-sm font-semibold">Document requirements</h3><p className="text-xs text-[var(--color-muted-foreground)]">S = submit, R = retain at supplier, A = available for customer review.</p></div><Button type="button" size="sm" variant="outline" onClick={() => documents.append({ documentCode: "", disposition: "Submit", sortOrder: documents.fields.length + 1 })}><Plus className="mr-1 h-3.5 w-3.5" />Add document</Button></div>{documents.fields.length === 0 && <p className="rounded-lg border border-dashed border-[var(--color-border)] p-4 text-sm text-[var(--color-muted-foreground)]">No document requirements.</p>}{documents.fields.map((field, index) => <div key={field.id} className="grid gap-3 rounded-lg border border-[var(--color-border)] p-3 sm:grid-cols-[1fr_8rem_8rem_auto]"><Field id={`document-code-${index}`} label="Document code" required error={form.formState.errors.documentRequirements?.[index]?.documentCode?.message}><Input id={`document-code-${index}`} {...form.register(`documentRequirements.${index}.documentCode`)} /></Field><Field id={`document-order-${index}`} label="Sort order" required error={form.formState.errors.documentRequirements?.[index]?.sortOrder?.message}><Input id={`document-order-${index}`} type="number" {...form.register(`documentRequirements.${index}.sortOrder`)} /></Field><Field id={`document-disposition-${index}`} label="Disposition"><Controller control={form.control} name={`documentRequirements.${index}.disposition`} render={({ field: control }) => <Select id={`document-disposition-${index}`} value={control.value} onValueChange={control.onChange} options={[{ value: "Submit", label: "S — Submit" }, { value: "Retain", label: "R — Retain" }, { value: "Available", label: "A — Available" }]} />} /></Field><Button type="button" size="sm" variant="ghost" className="self-end" aria-label={`Remove document requirement ${index + 1}`} onClick={() => documents.remove(index)}><Trash2 className="h-4 w-4" /></Button></div>)}</section>
    <section className="space-y-3"><div className="flex items-center justify-between"><div><h3 className="text-sm font-semibold">Rule assignments</h3><p className="text-xs text-[var(--color-muted-foreground)]">Enter the id of a published RuleVersion.</p></div><Button type="button" size="sm" variant="outline" onClick={() => assignments.append({ ruleVersionId: "", documentCode: "", parametersJson: "{}" })}><Plus className="mr-1 h-3.5 w-3.5" />Assign rule</Button></div>{assignments.fields.length === 0 && <p className="rounded-lg border border-dashed border-[var(--color-border)] p-4 text-sm text-[var(--color-muted-foreground)]">No rule assignments.</p>}{assignments.fields.map((field, index) => <div key={field.id} className="grid gap-3 rounded-lg border border-[var(--color-border)] p-3 sm:grid-cols-2"><Field id={`assignment-version-${index}`} label="Published RuleVersion id" required error={form.formState.errors.ruleAssignments?.[index]?.ruleVersionId?.message}><Input id={`assignment-version-${index}`} {...form.register(`ruleAssignments.${index}.ruleVersionId`)} /></Field><Field id={`assignment-document-${index}`} label="Document code" hint="Optional" error={form.formState.errors.ruleAssignments?.[index]?.documentCode?.message}><Input id={`assignment-document-${index}`} {...form.register(`ruleAssignments.${index}.documentCode`)} /></Field><Field id={`assignment-parameters-${index}`} label="Parameters JSON object" required error={form.formState.errors.ruleAssignments?.[index]?.parametersJson?.message} className="sm:col-span-2"><textarea id={`assignment-parameters-${index}`} rows={3} className={textareaClass} {...form.register(`ruleAssignments.${index}.parametersJson`)} /></Field><Button type="button" size="sm" variant="ghost" className="sm:col-span-2 sm:justify-self-end" onClick={() => assignments.remove(index)}><Trash2 className="mr-1 h-4 w-4" />Remove assignment</Button></div>)}</section><div className="flex justify-end"><Button type="submit" disabled={mutation.isPending}>{mutation.isPending ? "Saving…" : "Save draft configuration"}</Button></div>
  </form>;
}

function TemplateVersionSummary({ version }: { version: RuleTemplateVersionDto }) { return <div className="grid gap-4 sm:grid-cols-2"><section><h3 className="mb-2 text-sm font-semibold">Document requirements</h3>{version.documentRequirements.length === 0 ? <p className="text-sm text-[var(--color-muted-foreground)]">None</p> : <ul className="space-y-2">{version.documentRequirements.map((item) => <li key={item.id} className="rounded-lg border border-[var(--color-border)] p-3 text-sm"><Badge variant="outline"><code>{item.documentCode}</code></Badge> · {item.disposition.slice(0, 1)} — {item.disposition} · {item.isRequired ? "upload required" : "retained"} · order {item.sortOrder}</li>)}</ul>}</section><section><h3 className="mb-2 text-sm font-semibold">Rule assignments</h3>{version.ruleAssignments.length === 0 ? <p className="text-sm text-[var(--color-muted-foreground)]">None</p> : <ul className="space-y-2">{version.ruleAssignments.map((item) => <li key={item.id} className="rounded-lg border border-[var(--color-border)] p-3 text-sm"><code className="break-all">{item.ruleVersionId}</code>{item.documentCode ? ` · ${item.documentCode}` : ""}</li>)}</ul>}</section></div>; }
function toFormValues(version: RuleTemplateVersionDto, name: string, description?: string | null): ConfigureValues { return { name, description: description ?? "", documentRequirements: version.documentRequirements.map((item) => ({ documentCode: item.documentCode, disposition: item.disposition, sortOrder: item.sortOrder })), ruleAssignments: version.ruleAssignments.map((item) => ({ ruleVersionId: item.ruleVersionId, documentCode: item.documentCode ?? "", parametersJson: formatJson(item.parametersJson) })) }; }
function LifecycleBadge({ status }: { status: RuleLifecycle }) { return <Badge variant={status === "Published" ? "success" : status === "InReview" ? "info" : status === "Retired" ? "muted" : "outline"}>{status}</Badge>; }
function formatJson(value: string) { try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return value; } }
function actionLabel(action: PendingAction["kind"], past: boolean) { const labels = { submit: past ? "Submitted for review" : "Submit for review", publish: past ? "Template published" : "Publish template", retire: past ? "Template retired" : "Retire template", clone: past ? "Draft cloned" : "Clone draft" }; return labels[action]; }
function describe(error: unknown) { if (error instanceof ApiRequestError) return error.problem?.detail ?? error.problem?.title ?? error.message; return error instanceof Error ? error.message : String(error); }
const textareaClass = "flex min-h-20 w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 font-mono text-xs shadow-xs outline-none focus-visible:border-[var(--color-ring)] focus-visible:ring-2 focus-visible:ring-[var(--color-ring)]/30";
