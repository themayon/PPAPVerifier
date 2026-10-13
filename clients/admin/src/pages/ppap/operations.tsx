import { useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { Activity, Check, ClipboardCheck, FileSearch, WalletCards, X } from "lucide-react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { z } from "zod";
import {
  approveCreditTopupRequest,
  getPpapOperationsSummary,
  rejectCreditTopupRequest,
  searchCreditTopupRequests,
  searchPpapJobs,
  type CreditTopupRequestDto,
  type PpapJobStatus,
} from "@/api/ppap";
import { useAuth } from "@/auth/use-auth";
import { EmptyState } from "@/components/empty-state";
import { EntityPageHeader, ErrorBand, FilterBar, LoadingRow, Pagination, Select, Stat, StatStrip } from "@/components/list";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
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
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ApiRequestError } from "@/lib/api-client";
import { PpapPermissions } from "@/lib/permissions";

const PAGE_SIZE = 20;
const JOB_STATUSES: PpapJobStatus[] = ["Queued", "Processing", "Completed", "Failed", "Cancelled"];
const decisionSchema = z.object({ note: z.string().trim().max(512, "Decision note must be 512 characters or fewer.") });
type DecisionValues = z.infer<typeof decisionSchema>;
type Decision = { request: CreditTopupRequestDto; mode: "approve" | "reject" };

function describe(error: unknown, fallback: string) {
  if (error instanceof ApiRequestError) return error.problem?.detail ?? error.problem?.title ?? error.message;
  if (error instanceof Error) return error.message;
  return fallback;
}

function formatDate(value: string | null) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}

function statusVariant(status: string): React.ComponentProps<typeof Badge>["variant"] {
  if (status === "Completed" || status === "Approved") return "success";
  if (status === "Failed" || status === "Rejected") return "danger";
  if (status === "Processing") return "info";
  if (status === "Queued" || status === "Pending") return "warning";
  return "default";
}

export function PpapOperationsPage() {
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const canReviewFindings = (user?.permissions ?? []).includes(PpapPermissions.Findings.View);
  const [jobPage, setJobPage] = useState(1);
  const [jobStatus, setJobStatus] = useState<PpapJobStatus | "">("");
  const [search, setSearch] = useState("");
  const [decision, setDecision] = useState<Decision | null>(null);
  const form = useForm<DecisionValues>({
    resolver: zodResolver(decisionSchema),
    defaultValues: { note: "" },
  });

  const summaryQuery = useQuery({
    queryKey: ["ppap", "operations", "summary"],
    queryFn: getPpapOperationsSummary,
  });
  const jobsQuery = useQuery({
    queryKey: ["ppap", "operations", "jobs", { pageNumber: jobPage, status: jobStatus, search }],
    queryFn: () => searchPpapJobs({
      pageNumber: jobPage,
      pageSize: PAGE_SIZE,
      status: jobStatus || undefined,
      search: search.trim() || undefined,
      sort: "-createdAtUtc",
    }),
    placeholderData: keepPreviousData,
  });
  const topupsQuery = useQuery({
    queryKey: ["ppap", "credits", "topup-requests", { status: "Pending" }],
    queryFn: () => searchCreditTopupRequests({
      status: "Pending",
      pageNumber: 1,
      pageSize: 100,
      sort: "-requestedAt",
    }),
  });

  const closeDecision = () => {
    setDecision(null);
    form.reset({ note: "" });
  };
  const invalidateOperations = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["ppap", "operations", "summary"] }),
      queryClient.invalidateQueries({ queryKey: ["ppap", "credits", "topup-requests"] }),
    ]);
  };
  const approveMutation = useMutation({
    mutationFn: approveCreditTopupRequest,
    onSuccess: async () => {
      toast.success("Top-up approved");
      closeDecision();
      await invalidateOperations();
    },
    onError: (error) => toast.error("Approval failed", { description: describe(error, "Could not approve the request.") }),
  });
  const rejectMutation = useMutation({
    mutationFn: rejectCreditTopupRequest,
    onSuccess: async () => {
      toast.success("Top-up rejected");
      closeDecision();
      await invalidateOperations();
    },
    onError: (error) => toast.error("Rejection failed", { description: describe(error, "Could not reject the request.") }),
  });
  const decisionPending = approveMutation.isPending || rejectMutation.isPending;
  const submitDecision = form.handleSubmit(({ note }) => {
    if (!decision) return;
    const input = { id: decision.request.id, note: note || undefined };
    if (decision.mode === "approve") approveMutation.mutate(input);
    else rejectMutation.mutate(input);
  });
  const summary = summaryQuery.data;
  const jobs = jobsQuery.data;
  const topups = topupsQuery.data;

  return (
    <div className="space-y-6">
      <EntityPageHeader
        icon={ClipboardCheck}
        title="PPAP operations"
        tone="primary"
        description="Monitor submissions, processing jobs, and credit decisions for the current tenant."
      />

      {summaryQuery.isError && <ErrorBand message={describe(summaryQuery.error, "Failed to load PPAP summary.")} />}
      <StatStrip cols={4}>
        <Stat label="Submissions" value={summaryQuery.isLoading ? <Skeleton className="h-7 w-14" /> : summary?.totalSubmissions.toLocaleString() ?? "—"} hint="tenant total" />
        <Stat label="Active jobs" value={summaryQuery.isLoading ? <Skeleton className="h-7 w-14" /> : ((summary?.queuedJobs ?? 0) + (summary?.processingJobs ?? 0)).toLocaleString()} hint={`${summary?.queuedJobs ?? 0} queued · ${summary?.processingJobs ?? 0} processing`} tone={(summary?.failedJobs ?? 0) > 0 ? "warning" : "default"} />
        <Stat label="Completed / failed" value={summaryQuery.isLoading ? <Skeleton className="h-7 w-20" /> : `${summary?.completedJobs ?? 0} / ${summary?.failedJobs ?? 0}`} hint="processing outcomes" />
        <Stat label="Pending top-ups" value={summaryQuery.isLoading ? <Skeleton className="h-7 w-14" /> : summary?.pendingTopupRequests.toLocaleString() ?? "—"} hint="awaiting decision" tone={(summary?.pendingTopupRequests ?? 0) > 0 ? "warning" : "default"} />
      </StatStrip>
      <StatStrip cols={2}>
        <Stat label="Available credits" value={summaryQuery.isLoading ? <Skeleton className="h-7 w-20" /> : summary?.totalAvailableCredits.toLocaleString() ?? "—"} hint="across tenant accounts" />
        <Stat label="Reserved credits" value={summaryQuery.isLoading ? <Skeleton className="h-7 w-20" /> : summary?.totalReservedCredits.toLocaleString() ?? "—"} hint="committed to processing" />
      </StatStrip>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2"><Activity className="size-4" />Live jobs</CardTitle>
          <CardDescription>Real processing stages, progress, failures, and generated reports.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <FilterBar>
            <Input
              aria-label="Search jobs"
              className="w-full sm:w-64"
              placeholder="Part name or number"
              value={search}
              onChange={(event) => { setSearch(event.target.value); setJobPage(1); }}
            />
            <Select
              aria-label="Job status"
              value={jobStatus}
              onValueChange={(value) => { setJobStatus(value as PpapJobStatus | ""); setJobPage(1); }}
              options={JOB_STATUSES.map((status) => ({ value: status, label: status }))}
              emptyLabel="All statuses"
            />
          </FilterBar>
          {jobsQuery.isError && <ErrorBand message={describe(jobsQuery.error, "Failed to load PPAP jobs.")} />}
          {jobsQuery.isLoading && !jobs ? <LoadingRow label="Loading jobs" /> : jobs?.items.length === 0 ? (
            <EmptyState icon={FileSearch} kicker="Jobs" title="No jobs found" description="No processing jobs match the current filters." />
          ) : (
            <Table aria-label="Live PPAP jobs">
              <TableHeader><TableRow><TableHead scope="col">Part</TableHead><TableHead scope="col">Status</TableHead><TableHead scope="col">Stage</TableHead><TableHead scope="col">Progress</TableHead><TableHead scope="col">Owner</TableHead><TableHead scope="col">Created</TableHead><TableHead scope="col">Result</TableHead></TableRow></TableHeader>
              <TableBody>
                {jobs?.items.map((job) => (
                  <TableRow key={job.id}>
                    <TableCell><div className="font-medium">{job.partName}</div><div className="font-mono text-xs text-[var(--color-muted-foreground)]">{job.partNumber} · v{job.submissionVersion}</div></TableCell>
                    <TableCell><Badge variant={statusVariant(job.status)}>{job.status}</Badge></TableCell>
                    <TableCell>{job.currentStage}</TableCell>
                    <TableCell><div className="min-w-28"><div className="mb-1 flex justify-between text-xs"><span>{job.progressPercent}%</span></div><div role="progressbar" aria-label={`${job.partName} progress`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.min(100, Math.max(0, job.progressPercent))} className="h-1.5 overflow-hidden rounded-full bg-[var(--color-surface-2)]"><div className="h-full bg-[var(--color-primary)]" style={{ width: `${Math.min(100, Math.max(0, job.progressPercent))}%` }} /></div></div></TableCell>
                    <TableCell className="font-mono text-xs">{job.ownerUserId}</TableCell>
                    <TableCell className="whitespace-nowrap text-xs">{formatDate(job.createdAtUtc)}</TableCell>
                    <TableCell>{job.status === "Completed" && job.reportId ? <div className="flex items-center gap-2"><Badge variant="success">Available</Badge>{canReviewFindings && <Button asChild size="sm" variant="outline"><Link to={`/ppap/reports/${job.reportId}/review`}>Review findings</Link></Button>}</div> : job.errorMessage ? <span className="block max-w-64 text-xs text-[var(--color-destructive)]" title={job.errorMessage}>{job.errorCode ? `${job.errorCode}: ` : ""}{job.errorMessage}</span> : <span className="text-[var(--color-muted-foreground)]">—</span>}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          {jobs && <Pagination page={jobs.pageNumber} totalPages={jobs.totalPages} totalCount={jobs.totalCount} shown={jobs.items.length} fetching={jobsQuery.isFetching} hasPrev={jobs.hasPrevious} hasNext={jobs.hasNext} onPrev={() => setJobPage((page) => Math.max(1, page - 1))} onNext={() => setJobPage((page) => page + 1)} noun="jobs" />}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2"><WalletCards className="size-4" />Pending credit top-ups</CardTitle>
          <CardDescription>Review tenant-wide requests and record an approval or rejection note.</CardDescription>
        </CardHeader>
        <CardContent>
          {topupsQuery.isError && <ErrorBand message={describe(topupsQuery.error, "Failed to load top-up requests.")} />}
          {topupsQuery.isLoading && !topups ? <LoadingRow label="Loading top-ups" /> : topups?.items.length === 0 ? (
            <EmptyState icon={WalletCards} kicker="Credits" title="No pending requests" description="There are no credit top-ups awaiting a decision." />
          ) : (
            <Table aria-label="Pending credit top-ups">
              <TableHeader><TableRow><TableHead scope="col">Owner</TableHead><TableHead scope="col">Credits</TableHead><TableHead scope="col">Reference</TableHead><TableHead scope="col">Requested</TableHead><TableHead scope="col" className="text-right">Actions</TableHead></TableRow></TableHeader>
              <TableBody>
                {topups?.items.map((request) => (
                  <TableRow key={request.id}>
                    <TableCell className="font-mono text-xs">{request.ownerUserId}</TableCell>
                    <TableCell className="font-semibold">{request.creditsRequested.toLocaleString()}</TableCell>
                    <TableCell><div>{request.paymentReference ?? "—"}</div>{request.note && <div className="text-xs text-[var(--color-muted-foreground)]">{request.note}</div>}</TableCell>
                    <TableCell className="whitespace-nowrap text-xs">{formatDate(request.requestedAt)}</TableCell>
                    <TableCell><div className="flex justify-end gap-2"><Button size="sm" onClick={() => { form.reset({ note: "" }); setDecision({ request, mode: "approve" }); }}><Check className="mr-1 size-3.5" />Approve</Button><Button size="sm" variant="outline" onClick={() => { form.reset({ note: "" }); setDecision({ request, mode: "reject" }); }}><X className="mr-1 size-3.5" />Reject</Button></div></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <Dialog open={decision !== null} onOpenChange={(open) => { if (!open && !decisionPending) closeDecision(); }}>
        <DialogContent>
          <form onSubmit={submitDecision}>
            <DialogHeader>
              <DialogTitle>{decision?.mode === "reject" ? "Reject credit top-up" : "Approve credit top-up"}</DialogTitle>
              <DialogDescription>{decision ? `${decision.request.creditsRequested.toLocaleString()} credits requested by ${decision.request.ownerUserId}.` : "Record this credit decision."}</DialogDescription>
            </DialogHeader>
            <DialogBody>
              <Label htmlFor="decision-note">Decision note</Label>
              <Input id="decision-note" className="mt-2" placeholder="Optional internal note" disabled={decisionPending} aria-invalid={Boolean(form.formState.errors.note)} aria-describedby={form.formState.errors.note ? "decision-note-error" : undefined} {...form.register("note")} />
              {form.formState.errors.note && <p id="decision-note-error" role="alert" className="mt-1 text-xs text-[var(--color-destructive)]">{form.formState.errors.note.message}</p>}
            </DialogBody>
            <DialogFooter>
              <Button type="button" variant="outline" disabled={decisionPending} onClick={closeDecision}>Cancel</Button>
              <Button type="submit" variant={decision?.mode === "reject" ? "destructive" : "default"} disabled={decisionPending}>{decisionPending ? "Saving…" : decision?.mode === "reject" ? "Reject request" : "Approve request"}</Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
