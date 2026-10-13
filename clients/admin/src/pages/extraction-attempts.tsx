import { useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, ArrowDown, ArrowUp, ArrowUpDown, FileSearch, ScanSearch, X } from "lucide-react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import {
  getOperatorExtractionAttempt,
  searchOperatorExtractionAttempts,
  type OperatorExtractionAttemptDto,
  type OperatorExtractionAttemptStatus,
} from "@/api/operator-extraction";
import { EmptyState } from "@/components/empty-state";
import { EntityPageHeader, ErrorBand, FilterBar, LoadingRow, Pagination, Select } from "@/components/list";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ApiRequestError } from "@/lib/api-client";
import { cn } from "@/lib/cn";

const PAGE_SIZE = 20;
const STATUSES: OperatorExtractionAttemptStatus[] = [
  "Pending",
  "Claimed",
  "Submitted",
  "Processing",
  "Succeeded",
  "InvalidDocument",
  "Failed",
  "TimedOut",
  "DeadLettered",
];
const filterSchema = z.object({
  status: z.string(),
  documentCode: z.string().trim().max(64, "Document code must be 64 characters or fewer."),
  ownerUserId: z.union([z.literal(""), z.string().uuid("Enter a valid owner user ID.")]),
});
type FilterValues = z.infer<typeof filterSchema>;
type AppliedFilters = FilterValues;
type SortKey = "createdAt" | "updatedAt" | "status" | "documentCode" | "ownerUserId";

function describe(error: unknown, fallback: string) {
  if (error instanceof ApiRequestError) return error.problem?.detail ?? error.problem?.title ?? error.message;
  return error instanceof Error ? error.message : fallback;
}

function formatTimestamp(value: string | null) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toISOString();
}

function statusVariant(status: string): React.ComponentProps<typeof Badge>["variant"] {
  if (status === "Succeeded") return "success";
  if (status === "DeadLettered" || status === "Failed" || status === "InvalidDocument") return "danger";
  if (status === "TimedOut") return "warning";
  if (status === "Processing" || status === "Submitted") return "info";
  return "muted";
}

function sortValue(key: SortKey, descending: boolean) {
  return `${descending ? "-" : ""}${key}`;
}

export function ExtractionAttemptsPage() {
  const [page, setPage] = useState(1);
  const [sort, setSort] = useState("-createdAt");
  const [filters, setFilters] = useState<AppliedFilters>({ status: "", documentCode: "", ownerUserId: "" });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const form = useForm<FilterValues>({
    resolver: zodResolver(filterSchema),
    defaultValues: filters,
  });

  const query = useQuery({
    queryKey: ["document-intelligence", "operator-attempts", { page, sort, ...filters }],
    queryFn: () => searchOperatorExtractionAttempts({
      status: (filters.status || undefined) as OperatorExtractionAttemptStatus | undefined,
      documentCode: filters.documentCode || undefined,
      ownerUserId: filters.ownerUserId || undefined,
      pageNumber: page,
      pageSize: PAGE_SIZE,
      sort,
    }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;
  const activeFilterCount = Object.values(filters).filter(Boolean).length;

  const applyFilters = form.handleSubmit((values) => {
    setFilters({ ...values, documentCode: values.documentCode.trim() });
    setPage(1);
  });
  const clearFilters = () => {
    const cleared = { status: "", documentCode: "", ownerUserId: "" };
    form.reset(cleared);
    setFilters(cleared);
    setPage(1);
  };
  const changeSort = (key: SortKey) => {
    setSort((current) => current === key ? sortValue(key, true) : sortValue(key, false));
    setPage(1);
  };

  return (
    <div className="space-y-6">
      <EntityPageHeader
        icon={ScanSearch}
        title="Extraction attempts"
        total={data?.totalCount ?? null}
        unit="attempt"
        tone="primary"
        description="Inspect tenant-scoped document extraction attempts, failures, artifacts, and processing timestamps."
      />

      <form onSubmit={applyFilters} noValidate>
        <FilterBar
          trailing={activeFilterCount > 0 ? (
            <Button type="button" variant="ghost" size="sm" onClick={clearFilters}>
              <X className="mr-1 size-3.5" /> Clear filters ({activeFilterCount})
            </Button>
          ) : undefined}
        >
          <div className="min-w-44">
            <Label htmlFor="attempt-status" className="sr-only">Status</Label>
            <Select
              id="attempt-status"
              value={form.watch("status")}
              onValueChange={(value) => form.setValue("status", value)}
              options={STATUSES.map((status) => ({ value: status, label: status }))}
              emptyLabel="All statuses"
            />
          </div>
          <div className="min-w-40 flex-1">
            <Label htmlFor="document-code" className="sr-only">Document code</Label>
            <Input id="document-code" placeholder="Document code" {...form.register("documentCode")} />
            {form.formState.errors.documentCode && <p role="alert" className="mt-1 text-xs text-[var(--color-destructive)]">{form.formState.errors.documentCode.message}</p>}
          </div>
          <div className="min-w-64 flex-[2]">
            <Label htmlFor="owner-user-id" className="sr-only">Owner user ID</Label>
            <Input id="owner-user-id" className="font-mono text-xs" placeholder="Owner user ID" {...form.register("ownerUserId")} />
            {form.formState.errors.ownerUserId && <p role="alert" className="mt-1 text-xs text-[var(--color-destructive)]">{form.formState.errors.ownerUserId.message}</p>}
          </div>
          <Button type="submit" size="sm">Apply filters</Button>
        </FilterBar>
      </form>

      {query.isError && <ErrorBand message={describe(query.error, "Failed to load extraction attempts.")} />}
      {query.isLoading && !data && <LoadingRow label="Loading extraction attempts" />}
      {!query.isLoading && !query.isError && data?.items.length === 0 && (
        <EmptyState
          icon={FileSearch}
          kicker="// no attempts"
          title="No extraction attempts match your filters."
          description={activeFilterCount ? "Clear or adjust the filters to widen the results." : "No extraction attempts have been recorded for this tenant."}
          action={activeFilterCount ? <Button variant="outline" onClick={clearFilters}>Clear filters</Button> : undefined}
        />
      )}

      {data && data.items.length > 0 && (
        <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-card)]">
          <Table aria-label="Extraction attempts">
            <TableHeader>
              <TableRow>
                <SortableHead label="Document" sortKey="documentCode" current={sort} onSort={changeSort} />
                <SortableHead label="Status" sortKey="status" current={sort} onSort={changeSort} />
                <TableHead scope="col">Attempts</TableHead>
                <SortableHead label="Owner" sortKey="ownerUserId" current={sort} onSort={changeSort} />
                <TableHead scope="col">Provider / model</TableHead>
                <TableHead scope="col">Error</TableHead>
                <SortableHead label="Created" sortKey="createdAt" current={sort} onSort={changeSort} />
                <SortableHead label="Updated" sortKey="updatedAt" current={sort} onSort={changeSort} />
                <TableHead scope="col"><span className="sr-only">Details</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.items.map((attempt) => (
                <TableRow key={attempt.id}>
                  <TableCell>
                    <div className="font-semibold">{attempt.documentCode}</div>
                    <div className="max-w-40 truncate font-mono text-[10px] text-[var(--color-muted-foreground)]" title={attempt.id}>{attempt.id}</div>
                  </TableCell>
                  <TableCell><Badge variant={statusVariant(attempt.status)}>{attempt.status}</Badge></TableCell>
                  <TableCell>{attempt.attemptCount}</TableCell>
                  <TableCell className="max-w-44 truncate font-mono text-xs" title={attempt.ownerUserId}>{attempt.ownerUserId}</TableCell>
                  <TableCell><div>{attempt.provider}</div><div className="text-xs text-[var(--color-muted-foreground)]">{attempt.model}</div></TableCell>
                  <TableCell>
                    {attempt.errorCode || attempt.errorMessage ? (
                      <div className="max-w-64 text-xs text-[var(--color-destructive)]">
                        <div className="font-mono font-semibold">{attempt.errorCode ?? "Extraction error"}</div>
                        <div className="line-clamp-2" title={attempt.errorMessage ?? undefined}>{attempt.errorMessage}</div>
                      </div>
                    ) : <span className="text-[var(--color-muted-foreground)]">—</span>}
                  </TableCell>
                  <TableCell className="whitespace-nowrap font-mono text-xs">{formatTimestamp(attempt.createdAtUtc)}</TableCell>
                  <TableCell className="whitespace-nowrap font-mono text-xs">{formatTimestamp(attempt.updatedAtUtc)}</TableCell>
                  <TableCell className="text-right">
                    <Button variant="ghost" size="sm" onClick={() => setSelectedId(attempt.id)} aria-label={`View details for attempt ${attempt.id}`}>Details</Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {data && (
        <Pagination
          page={data.pageNumber}
          totalPages={data.totalPages}
          totalCount={data.totalCount}
          shown={data.items.length}
          fetching={query.isFetching}
          hasPrev={data.hasPrevious}
          hasNext={data.hasNext}
          onPrev={() => setPage((current) => Math.max(1, current - 1))}
          onNext={() => setPage((current) => current + 1)}
          noun="attempts"
        />
      )}

      <AttemptDetailSheet attemptId={selectedId} fallback={data?.items.find((item) => item.id === selectedId)} onClose={() => setSelectedId(null)} />
    </div>
  );
}

function SortableHead({ label, sortKey, current, onSort }: { label: string; sortKey: SortKey; current: string; onSort: (key: SortKey) => void }) {
  const active = current === sortKey || current === `-${sortKey}`;
  const descending = current === `-${sortKey}`;
  const Icon = active ? (descending ? ArrowDown : ArrowUp) : ArrowUpDown;
  return (
    <TableHead scope="col" aria-sort={active ? (descending ? "descending" : "ascending") : undefined}>
      <button type="button" className="inline-flex items-center gap-1 rounded-sm outline-none hover:text-[var(--color-foreground)] focus-visible:ring-2 focus-visible:ring-[var(--color-ring)] focus-visible:ring-offset-2" onClick={() => onSort(sortKey)}>
        {label}<Icon className="size-3" aria-hidden />
      </button>
    </TableHead>
  );
}

function AttemptDetailSheet({ attemptId, fallback, onClose }: { attemptId: string | null; fallback?: OperatorExtractionAttemptDto; onClose: () => void }) {
  const query = useQuery({
    queryKey: ["document-intelligence", "operator-attempt", attemptId],
    queryFn: () => getOperatorExtractionAttempt(attemptId!),
    enabled: Boolean(attemptId),
  });
  const attempt = query.data ?? fallback;

  return (
    <Sheet open={Boolean(attemptId)} onOpenChange={(open) => { if (!open) onClose(); }}>
      <SheetContent side="right" showClose={false} className="w-[min(680px,95vw)] max-w-none overflow-hidden p-0">
        <div className="flex h-full flex-col">
          <div className="flex items-start justify-between gap-4 border-b border-[var(--color-border)] px-6 py-5">
            <div>
              <DialogTitle>Extraction attempt details</DialogTitle>
              <DialogDescription className="mt-1 font-mono text-xs">{attemptId}</DialogDescription>
            </div>
            <Button variant="ghost" size="icon" aria-label="Close extraction attempt details" onClick={onClose}><X className="size-4" /></Button>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-6">
            {query.isLoading && !attempt && <LoadingRow label="Loading extraction attempt" />}
            {query.isError && <ErrorBand message={describe(query.error, "Failed to load extraction attempt details.")} />}
            {attempt && <AttemptDetails attempt={attempt} />}
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
}

function AttemptDetails({ attempt }: { attempt: OperatorExtractionAttemptDto }) {
  const identifiers = [
    ["Owner user ID", attempt.ownerUserId],
    ["Submission document ID", attempt.submissionDocumentId],
    ["Source file asset ID", attempt.sourceFileAssetId],
    ["Canonical document ID", attempt.canonicalDocumentId],
    ["Raw response file asset ID", attempt.rawResponseFileAssetId],
    ["Generated Excel file asset ID", attempt.generatedExcelFileAssetId],
  ] as const;
  const timestamps = [
    ["Created", attempt.createdAtUtc], ["Updated", attempt.updatedAtUtc], ["Claimed", attempt.claimedAtUtc],
    ["Claim expires", attempt.claimedUntilUtc], ["Submitted", attempt.submittedAtUtc], ["Started", attempt.startedAtUtc],
    ["Completed", attempt.completedAtUtc], ["Completion event published", attempt.completionEventPublishedAtUtc],
    ["Failure event published", attempt.failureEventPublishedAtUtc],
  ] as const;

  return (
    <div className="space-y-6">
      <section aria-labelledby="attempt-summary-heading">
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <h2 id="attempt-summary-heading" className="font-display text-base font-semibold">{attempt.documentCode}</h2>
          <Badge variant={statusVariant(attempt.status)}>{attempt.status}</Badge>
          <span className="text-xs text-[var(--color-muted-foreground)]">Attempt {attempt.attemptCount}</span>
        </div>
        <dl className="grid gap-2 sm:grid-cols-2">
          <Fact label="Provider" value={attempt.provider} />
          <Fact label="Model" value={attempt.model} />
          <Fact label="Extractor version" value={attempt.extractorVersion} />
          <Fact label="Source SHA-256" value={attempt.sourceSha256} mono />
        </dl>
      </section>

      {(attempt.status === "DeadLettered" || attempt.errorCode || attempt.errorMessage) && (
        <section aria-labelledby="attempt-error-heading" className="rounded-lg border border-[var(--color-destructive)]/30 bg-[var(--color-destructive)]/5 p-4">
          <h2 id="attempt-error-heading" className="flex items-center gap-2 font-semibold text-[var(--color-destructive)]"><AlertTriangle className="size-4" />Failure details</h2>
          <dl className="mt-3 grid gap-2">
            <Fact label="Error code" value={attempt.errorCode} mono />
            <Fact label="Error message" value={attempt.errorMessage} />
          </dl>
        </section>
      )}

      <DetailSection title="Artifact and record IDs" values={identifiers} />
      <DetailSection title="Processing timestamps" values={timestamps.map(([label, value]) => [label, formatTimestamp(value)] as const)} />
    </div>
  );
}

function DetailSection({ title, values }: { title: string; values: readonly (readonly [string, string | null])[] }) {
  const headingId = title.toLowerCase().replaceAll(" ", "-");
  return (
    <section aria-labelledby={headingId}>
      <h2 id={headingId} className="mb-3 font-display text-sm font-semibold">{title}</h2>
      <dl className="grid gap-2 sm:grid-cols-2">
        {values.map(([label, value]) => <Fact key={label} label={label} value={value} mono />)}
      </dl>
    </section>
  );
}

function Fact({ label, value, mono = false }: { label: string; value: string | number | null; mono?: boolean }) {
  return (
    <div className="min-w-0 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface-1)] px-3 py-2.5">
      <dt className="font-mono text-[10px] uppercase tracking-[0.12em] text-[var(--color-muted-foreground)]">{label}</dt>
      <dd className={cn("mt-1 break-words text-sm", mono && "font-mono text-xs")}>{value ?? "—"}</dd>
    </div>
  );
}
