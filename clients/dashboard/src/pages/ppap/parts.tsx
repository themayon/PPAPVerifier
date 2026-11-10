import { useEffect, useRef, useState, type FormEvent } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import {
  BadgeCheck,
  ClipboardList,
  ClipboardPaste,
  Download,
  Eye,
  FilePlus2,
  FileSearch,
  Link2,
  ListFilter,
  Loader2,
  MoreVertical,
  Pencil,
  Play,
  Plus,
  Search,
  Trash2,
  X,
} from "lucide-react";
import { toast } from "sonner";
import {
  assignPpapPartTemplate,
  cancelPpapSubmission,
  createDraftFromPpapPart,
  getPpapPartsSummary,
  getPublishedRuleTemplates,
  searchMyPpapParts,
  searchPpapSuppliers,
  type PartClassification,
  type PartDirectoryStatus,
  type PartSearchParams,
  type PpapPartDto,
} from "@/api/ppap";
import { useAuth } from "@/auth/use-auth";
import { Combobox } from "@/components/list/combobox";
import { EntityEmpty, ErrorBand } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { ElementsProgress, NumberedPager, StatCard, StatusDot, TonePill } from "@/pages/ppap/directory-ui";
import { downloadCsv, percentChange, type Tone } from "@/pages/ppap/directory-utils";

const PAGE_SIZE = 10;
const GRID = "grid-cols-[minmax(13rem,1.3fr)_minmax(11rem,1fr)_4.5rem_minmax(12rem,1fr)_10rem_10.5rem]";

const STATUS_META: Record<PartDirectoryStatus, { label: string; tone: Tone }> = {
  NotStarted: { label: "Not Started", tone: "muted" },
  Draft: { label: "Draft", tone: "muted" },
  Submitted: { label: "Submitted", tone: "primary" },
  InterimApproved: { label: "Interim Approval", tone: "warning" },
  Approved: { label: "Approved", tone: "success" },
  Rejected: { label: "Rejected", tone: "danger" },
};

function directoryStatus(part: PpapPartDto): PartDirectoryStatus {
  const latest = part.latestSubmission;
  if (!latest) return "NotStarted";
  if (latest.status === "Draft") return "Draft";
  if (latest.decision && latest.decision !== "None") return latest.decision;
  return "Submitted";
}

export function PpapPartsPage() {
  const { user, permissionsHydrated } = useAuth();
  const perms = user?.permissions ?? [];
  const can = (permission: string) => perms.includes(`Permissions.Ppap.${permission}`);
  const canView = can("Parts.View");
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [params, setParams] = useSearchParams();
  const search = params.get("q") ?? "";
  const status = params.get("status") as PartDirectoryStatus | null;
  const level = params.get("level");
  const classification = params.get("class") as PartClassification | null;
  const supplierId = params.get("supplier");
  const page = Math.max(1, Number(params.get("page") ?? 1) || 1);
  const [searchDraft, setSearchDraft] = useState(search);
  const [exporting, setExporting] = useState(false);
  const [templateFor, setTemplateFor] = useState<PpapPartDto | null>(null);
  const [cancelFor, setCancelFor] = useState<PpapPartDto | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);

  const patch = (next: Record<string, string | null>, resetPage = true) => setParams((current) => {
    const updated = new URLSearchParams(current);
    Object.entries(next).forEach(([key, value]) => (value ? updated.set(key, value) : updated.delete(key)));
    if (resetPage) updated.delete("page");
    return updated;
  }, { replace: true });

  const filters: PartSearchParams = {
    search: search || undefined,
    status: status ?? undefined,
    ppapLevel: level ? Number(level) : undefined,
    classification: classification ?? undefined,
    supplierId: supplierId ?? undefined,
  };
  const enabled = permissionsHydrated && canView;
  const query = useQuery({
    queryKey: ["ppap", "parts", { ...filters, page }],
    queryFn: () => searchMyPpapParts({ ...filters, pageNumber: page, pageSize: PAGE_SIZE, sort: "-updatedAt" }),
    placeholderData: keepPreviousData,
    enabled,
  });
  const summary = useQuery({ queryKey: ["ppap", "parts", "summary"], queryFn: getPpapPartsSummary, enabled });
  const suppliers = useQuery({ queryKey: ["ppap", "suppliers", "picker"], queryFn: () => searchPpapSuppliers({ pageSize: 200, sort: "name" }), staleTime: 60_000, enabled: enabled && can("Suppliers.View") });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["ppap", "parts"] });

  const startDraft = useMutation({
    mutationFn: (part: PpapPartDto) => createDraftFromPpapPart(part.id, part.ppapLevel ?? 3),
    onSuccess: async (id) => {
      toast.success("PPAP submission started");
      await refresh();
      navigate(`/ppap/submissions/${id}`);
    },
    onError: (error) => toast.error("Unable to start submission", { description: describe(error) }),
  });
  const cancelDraft = useMutation({
    mutationFn: (part: PpapPartDto) => cancelPpapSubmission(part.latestSubmission!.id, part.latestSubmission!.version),
    onSuccess: async () => {
      toast.success("Draft discarded");
      setCancelFor(null);
      await refresh();
    },
    onError: (error) => toast.error("Unable to discard draft", { description: describe(error) }),
  });

  if (permissionsHydrated && !canView) return <EntityEmpty icon={ClipboardList} title="Parts unavailable" body="You do not have permission to view PPAP parts." />;
  const items = query.data?.items ?? [];
  const filtered = Boolean(search || status || level || classification || supplierId);
  const supplierName = suppliers.data?.items.find((item) => item.id === supplierId)?.name;
  const s = summary.data;
  const change = s ? percentChange(s.newSubmissionsThisMonth, s.newSubmissionsLastMonth) : null;

  const exportCsv = async () => {
    setExporting(true);
    try {
      const rows: PpapPartDto[] = [];
      for (let next = 1, pages = 1; next <= pages && next <= 25; next++) {
        const result = await searchMyPpapParts({ ...filters, pageNumber: next, pageSize: 100, sort: "-updatedAt" });
        rows.push(...result.items);
        pages = result.totalPages;
      }
      downloadCsv(`ppap-parts-${new Date().toISOString().slice(0, 10)}.csv`,
        ["Part number", "Part name", "Revision", "Classification", "PPAP level", "Supplier code", "Supplier", "Location", "Elements complete", "Elements total", "Status", "Open findings"],
        rows.map((part) => [part.partNumber, part.partName, part.drawingRevision, part.classification, part.latestSubmission?.ppapLevel ?? part.ppapLevel, part.supplier?.code, part.supplier?.name,
        [part.supplier?.city, part.supplier?.country].filter(Boolean).join(", "), part.latestSubmission?.completedElements, part.latestSubmission?.totalElements,
        STATUS_META[directoryStatus(part)].label, part.latestSubmission?.openFindingCount]));
      toast.success(`Exported ${rows.length} parts`);
    } catch (error) {
      toast.error("Export failed", { description: describe(error) });
    } finally {
      setExporting(false);
    }
  };

  return (
    <div className="fsh-enter space-y-6">
      <header className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="font-display text-[26px] font-semibold tracking-tight">Part Submissions Directory</h1>
          <p className="mt-1 text-[14px] text-[var(--color-muted-foreground)]">Manage and track production part approval processes across all suppliers.</p>
        </div>
        {can("Parts.Create") && <Button size="lg" asChild><Link to={supplierId ? `/ppap/parts/new?supplier=${supplierId}` : "/ppap/parts/new"}><Plus className="size-4" />Create New Part</Link></Button>}
      </header>

      <div className="grid gap-4 md:grid-cols-3">
        <StatCard label="Total Active Submissions" icon={ClipboardList} tone="primary" value={s ? s.activeSubmissions.toLocaleString() : "—"}
          badge={change !== null && s ? <TonePill tone={change >= 0 ? "success" : "danger"}>{change >= 0 ? "+" : ""}{change}% vs last month</TonePill> : undefined}
          footnote={<span className="text-[var(--color-muted-foreground)]">{s ? `${s.totalParts.toLocaleString()} parts in directory` : "\u00a0"}</span>} />
        <StatCard label="Pending OEM Review" icon={ClipboardPaste} tone="warning" value={s ? s.pendingReview.toLocaleString() : "—"}
          badge={s && s.pendingReview > 0 ? <TonePill tone="warning">Action Required</TonePill> : undefined}
          footnote={<span className="text-[var(--color-muted-foreground)]">Submitted, awaiting a decision</span>} />
        <StatCard label="Approvals This Month" icon={BadgeCheck} tone="success" value={s ? s.approvalsThisMonth.toLocaleString() : "—"}
          badge={s ? <TonePill tone="success">Last month: {s.approvalsLastMonth}</TonePill> : undefined}
          footnote={<span className="text-[var(--color-muted-foreground)]">Full approvals recorded</span>} />
      </div>

      <div className="flex flex-wrap items-center gap-3 rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-4 shadow-xs">
        <div className="relative min-w-[16rem] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--color-muted-foreground)]" />
          <input
            aria-label="Search parts"
            value={searchDraft}
            placeholder="Search by Part #, Supplier or Name…"
            onChange={(event) => {
              const value = event.target.value;
              setSearchDraft(value);
              clearTimeout(timer.current);
              timer.current = setTimeout(() => patch({ q: value.trim() || null }), 300);
            }}
            className="h-10 w-full rounded-lg border border-[var(--color-input)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] pl-9 pr-3 text-sm outline-none focus:border-[oklch(from_var(--color-ring)_l_c_h_/_0.5)]"
          />
        </div>
        <Combobox label="Status" className="h-10 min-w-[10.5rem]" value={status} onChange={(value) => patch({ status: value })} emptyOptionLabel="All Statuses" placeholder="All Statuses"
          options={(Object.keys(STATUS_META) as PartDirectoryStatus[]).map((value) => ({ value, label: STATUS_META[value].label }))} />
        <Combobox label="PPAP level" className="h-10 min-w-[9rem]" value={level} onChange={(value) => patch({ level: value })} emptyOptionLabel="All levels" placeholder="PPAP Level"
          options={[1, 2, 3, 4, 5].map((value) => ({ value: String(value), label: `Level ${value}` }))} />
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" className={cn("h-10", (classification || supplierId) && "text-[var(--color-primary)]")}><ListFilter className="size-4" />More Filters</Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start" className="w-64">
            <DropdownMenuLabel>Classification</DropdownMenuLabel>
            {(["Safety", "Critical", "Standard"] as const).map((value) => (
              <DropdownMenuItem key={value} onSelect={() => patch({ class: classification === value ? null : value })} className={cn(classification === value && "font-semibold text-[var(--color-primary)]")}>{value}</DropdownMenuItem>
            ))}
            {(suppliers.data?.items.length ?? 0) > 0 && (
              <>
                <DropdownMenuSeparator />
                <DropdownMenuLabel>Supplier</DropdownMenuLabel>
                <div className="max-h-48 overflow-y-auto">
                  {suppliers.data!.items.map((item) => (
                    <DropdownMenuItem key={item.id} onSelect={() => patch({ supplier: supplierId === item.id ? null : item.id })} className={cn(supplierId === item.id && "font-semibold text-[var(--color-primary)]")}>
                      <span className="truncate">{item.name}</span><span className="ml-auto font-mono text-[11px] text-[var(--color-muted-foreground)]">{item.code}</span>
                    </DropdownMenuItem>
                  ))}
                </div>
              </>
            )}
            {filtered && <><DropdownMenuSeparator /><DropdownMenuItem onSelect={() => { setSearchDraft(""); setParams({}, { replace: true }); }}>Clear all filters</DropdownMenuItem></>}
          </DropdownMenuContent>
        </DropdownMenu>
        <Button variant="ghost" className="h-10" disabled={exporting || !query.data?.totalCount} onClick={() => void exportCsv()}>
          {exporting ? <Loader2 className="size-4 animate-spin" /> : <Download className="size-4" />}Export
        </Button>
        {(classification || supplierId) && (
          <div className="flex w-full flex-wrap gap-2">
            {classification && <FilterChip label={`Classification: ${classification}`} onClear={() => patch({ class: null })} />}
            {supplierId && <FilterChip label={`Supplier: ${supplierName ?? "Selected supplier"}`} onClear={() => patch({ supplier: null })} />}
          </div>
        )}
      </div>

      {query.isError && <ErrorBand message={describe(query.error)} />}

      <section aria-label="Parts" className={cn("overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs", query.isPlaceholderData && "opacity-70")}>
        <div className="overflow-x-auto">
          <div className="min-w-[66rem]">
            <div role="row" className={cn("grid items-center gap-4 border-b border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] px-5 py-3 text-[11px] font-semibold uppercase tracking-[0.1em] text-[var(--color-muted-foreground)]", GRID)}>
              <span>Part info</span><span>Supplier</span><span>Level</span><span>Elements</span><span>Status</span><span className="text-right">Actions</span>
            </div>
            {query.isLoading && <div role="status" aria-busy="true" className="space-y-2 p-4"><span className="sr-only">Loading parts…</span>{Array.from({ length: 4 }).map((_, index) => <Skeleton key={index} className="h-20 rounded-xl" />)}</div>}
            {!query.isLoading && !query.isError && items.length === 0 && (
              <div className="p-6"><EntityEmpty icon={ClipboardList} title={filtered ? "No parts match these filters" : "No parts yet"} body={filtered ? "Try a different search or clear the filters." : "Create a part to start its PPAP submission."} /></div>
            )}
            {items.map((part) => (
              <PartRow key={part.id} part={part} can={can} busy={startDraft.isPending && startDraft.variables?.id === part.id}
                onStart={() => startDraft.mutate(part)} onCancel={() => setCancelFor(part)} onTemplate={() => setTemplateFor(part)} />
            ))}
          </div>
        </div>
        {query.data && query.data.totalCount > 0 && (
          <NumberedPager page={query.data.pageNumber} totalPages={query.data.totalPages} total={query.data.totalCount} pageSize={PAGE_SIZE} unit="parts" onPage={(next) => patch({ page: next > 1 ? String(next) : null }, false)} />
        )}
      </section>

      <Dialog open={templateFor !== null} onOpenChange={(open) => !open && setTemplateFor(null)}>
        {templateFor && <AssignTemplateDialog part={templateFor} onSaved={() => { setTemplateFor(null); void refresh(); }} />}
      </Dialog>
      <Dialog open={cancelFor !== null} onOpenChange={(open) => !open && !cancelDraft.isPending && setCancelFor(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader><DialogTitle>Discard draft for {cancelFor?.partNumber}?</DialogTitle><DialogDescription>The draft submission is cancelled. Uploaded documents stay in the audit history.</DialogDescription></DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCancelFor(null)} disabled={cancelDraft.isPending}>Keep draft</Button>
            <Button variant="destructive" disabled={cancelDraft.isPending} onClick={() => cancelFor && cancelDraft.mutate(cancelFor)}>{cancelDraft.isPending ? "Discarding…" : "Discard draft"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function PartRow({ part, can, busy, onStart, onCancel, onTemplate }: { part: PpapPartDto; can: (permission: string) => boolean; busy: boolean; onStart: () => void; onCancel: () => void; onTemplate: () => void }) {
  const navigate = useNavigate();
  const status = directoryStatus(part);
  const meta = STATUS_META[status];
  const latest = part.latestSubmission;
  const owner = part.canEdit !== false;
  const level = latest?.ppapLevel ?? part.ppapLevel ?? 3;
  const reviewable = Boolean(latest?.latestReportId) && (status === "Submitted" || status === "InterimApproved") && can("Submissions.Approve") && can("Findings.View");
  const canStart = owner && can("Submissions.Create") && (status === "NotStarted" || status === "Approved" || status === "Rejected" || status === "InterimApproved");
  const view = () => {
    if (!latest) return;
    if (latest.isOwner) navigate(`/ppap/submissions/${latest.id}`);
    else if (latest.latestReportId) navigate(can("Findings.View") ? `/ppap/reports/${latest.latestReportId}/review` : `/ppap/reports/${latest.latestReportId}`);
  };
  const viewable = latest && (latest.isOwner || latest.latestReportId);
  const progressTone: Tone = status === "Approved" ? "success" : status === "Rejected" || (latest && latest.totalElements > 0 && latest.completedElements / latest.totalElements < 0.34) ? "danger" : "primary";

  return (
    <div role="row" className={cn("grid items-center gap-4 border-b border-[var(--color-border)] px-5 py-4 last:border-b-0 hover:bg-[oklch(from_var(--color-muted)_l_c_h_/_0.3)]", GRID)}>
      <div className="min-w-0">
        <Link to={`/ppap/parts/${part.id}/edit`} className="block truncate font-mono text-[15px] font-semibold text-[var(--color-primary)] hover:underline">{part.partNumber}</Link>
        <p className="truncate text-[13.5px]">{part.partName}</p>
        <p className="truncate text-[11.5px] text-[var(--color-muted-foreground)]">
          Rev: {part.drawingRevision}
          {part.classification && part.classification !== "Standard" && <span className={cn("ml-2 font-semibold", part.classification === "Safety" ? "text-[var(--color-destructive)]" : "text-[var(--color-warning)]")}>{part.classification}</span>}
        </p>
      </div>
      <div className="min-w-0">
        {part.supplier ? (
          <>
            <p className="truncate text-[14px] font-medium">{part.supplier.name}</p>
            <p className="truncate text-[12px] text-[var(--color-muted-foreground)]">{[part.supplier.city, part.supplier.country].filter(Boolean).join(", ") || part.supplier.code}</p>
          </>
        ) : <p className="text-[13px] text-[var(--color-muted-foreground)]">No supplier</p>}
      </div>
      <span><span className="inline-flex h-7 items-center rounded-md bg-[var(--color-muted)] px-2 font-mono text-[12px] font-semibold">LVL {level}</span></span>
      <div>
        {latest ? <ElementsProgress completed={latest.completedElements} total={latest.totalElements} tone={progressTone} />
          : <span className="text-[12.5px] text-[var(--color-muted-foreground)]">{part.ruleTemplateId ? "Ready to start" : "Assign a template to start"}</span>}
        {latest && latest.openFindingCount > 0 && status !== "Draft" && <p className="mt-1 text-[11.5px] font-semibold text-[var(--color-destructive)]">{latest.openFindingCount} open finding{latest.openFindingCount === 1 ? "" : "s"}</p>}
      </div>
      <span><StatusDot tone={meta.tone}>{meta.label}</StatusDot></span>
      <div className="flex items-center justify-end gap-1">
        {status === "NotStarted" && canStart && (
          <Button size="sm" variant="secondary" disabled={!part.ruleTemplateId || busy} onClick={onStart} title={part.ruleTemplateId ? undefined : "Assign a template first"}>
            {busy ? <Loader2 className="size-3.5 animate-spin" /> : <Play className="size-3.5" />}Start
          </Button>
        )}
        {status === "Draft" && latest?.isOwner && (
          <>
            <Button variant="ghost" size="icon-sm" aria-label={`Continue draft for ${part.partNumber}`} onClick={() => navigate(`/ppap/submissions/${latest.id}`)}><Pencil className="size-4 text-[var(--color-primary)]" /></Button>
            {can("Submissions.Cancel") && <Button variant="ghost" size="icon-sm" aria-label={`Discard draft for ${part.partNumber}`} onClick={onCancel}><Trash2 className="size-4" /></Button>}
          </>
        )}
        {status !== "NotStarted" && status !== "Draft" && viewable && (
          <Button variant="ghost" size="icon-sm" aria-label={`View submission for ${part.partNumber}`} onClick={view}><Eye className="size-4 text-[var(--color-primary)]" /></Button>
        )}
        {reviewable && (
          <Button size="sm" variant="secondary" className="font-semibold uppercase tracking-wide text-[var(--color-primary)]" onClick={() => navigate(`/ppap/reports/${latest!.latestReportId}/review`)}>Review</Button>
        )}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon-sm" aria-label={`More actions for ${part.partNumber}`}><MoreVertical className="size-4" /></Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onSelect={() => navigate(`/ppap/parts/${part.id}/edit`)}><Pencil className="size-4" />{owner && can("Parts.Update") ? "Edit part" : "View part"}</DropdownMenuItem>
            {owner && can("Parts.AssignTemplate") && <DropdownMenuItem onSelect={onTemplate}><Link2 className="size-4" />{part.ruleTemplateId ? "Change template" : "Assign template"}</DropdownMenuItem>}
            {canStart && status !== "NotStarted" && <DropdownMenuItem disabled={!part.ruleTemplateId || busy} onSelect={onStart}><FilePlus2 className="size-4" />New submission</DropdownMenuItem>}
            {latest?.latestReportId && <DropdownMenuItem onSelect={() => navigate(`/ppap/reports/${latest.latestReportId}`)}><FileSearch className="size-4" />View report</DropdownMenuItem>}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </div>
  );
}

function FilterChip({ label, onClear }: { label: string; onClear: () => void }) {
  return (
    <span className="inline-flex h-7 items-center gap-1 rounded-full bg-[var(--color-primary-soft)] pl-3 pr-1 text-[12px] font-medium text-[var(--color-primary)]">
      {label}
      <button type="button" aria-label={`Clear ${label}`} onClick={onClear} className="grid size-5 cursor-pointer place-items-center rounded-full hover:bg-[oklch(from_var(--color-primary)_l_c_h_/_0.15)]"><X className="size-3" /></button>
    </span>
  );
}

function AssignTemplateDialog({ part, onSaved }: { part: PpapPartDto; onSaved: () => void }) {
  const [templateId, setTemplateId] = useState<string | null>(part.ruleTemplateId);
  const templates = useQuery({ queryKey: ["rules", "templates", { status: "Published", pageNumber: 1, pageSize: 100 }], queryFn: getPublishedRuleTemplates });
  const mutation = useMutation({
    mutationFn: (ruleTemplateId: string) => assignPpapPartTemplate(part.id, { ruleTemplateId, expectedVersion: part.version }),
    onSuccess: () => { toast.success("Template assigned"); onSaved(); },
    onError: (error) => toast.error("Unable to assign template", { description: describe(error) }),
  });
  const submit = (event: FormEvent) => { event.preventDefault(); if (templateId) mutation.mutate(templateId); };
  return (
    <DialogContent className="sm:max-w-md">
      <form onSubmit={submit}>
        <DialogHeader><DialogTitle>{part.ruleTemplateId ? "Change template" : "Assign template"}</DialogTitle><DialogDescription>New submissions for {part.partNumber} use the template's current documents and rules.</DialogDescription></DialogHeader>
        <DialogBody className="space-y-1.5">
          <Label htmlFor="part-template">Published template</Label>
          <Combobox id="part-template" label="Template" value={templateId} onChange={setTemplateId} disabled={templates.isLoading} placeholder={templates.isLoading ? "Loading templates…" : "Select a template"}
            options={(templates.data?.items ?? []).map((item) => ({ value: item.id, label: item.name, hint: item.stableCode }))} />
          {templates.isError && <p role="alert" className="text-[12px] text-[var(--color-destructive)]">Published templates could not be loaded.</p>}
        </DialogBody>
        <DialogFooter><Button type="submit" disabled={!templateId || mutation.isPending}>{mutation.isPending ? "Assigning…" : "Assign template"}</Button></DialogFooter>
      </form>
    </DialogContent>
  );
}
