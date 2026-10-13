import { useState, type FormEvent } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Activity, Check, ClipboardCheck, FileSearch, WalletCards, X } from "lucide-react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { approveCreditTopupRequest, getPpapOperationsSummary, rejectCreditTopupRequest, searchCreditTopupRequests, searchPpapJobs, type CreditTopupRequestDto, type PpapJobStatus } from "@/api/ppap";
import { useAuth } from "@/auth/use-auth";
import { EntityEmpty, EntityPageHeader, EntityPager, EntityStatusBadge, ErrorBand } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { describe, formatDateTimeMono } from "@/lib/list-helpers";

const PAGE_SIZE = 20;
const STATUSES: PpapJobStatus[] = ["Queued", "Processing", "Completed", "Failed", "Cancelled"];
type Decision = { request: CreditTopupRequestDto; mode: "approve" | "reject" };

function tone(status: string): "default" | "success" | "warning" | "danger" | "info" {
  if (status === "Completed" || status === "Approved") return "success";
  if (status === "Failed" || status === "Rejected") return "danger";
  if (status === "Processing") return "info";
  if (status === "Queued" || status === "Pending") return "warning";
  return "default";
}

export function PpapOperationsPage() {
  const { user, permissionsHydrated } = useAuth();
  const queryClient = useQueryClient();
  const canView = user?.permissions.includes("Permissions.Ppap.Operations.View") ?? false;
  const canReview = user?.permissions.includes("Permissions.Ppap.Findings.View") ?? false;
  const canManageCredits = user?.permissions.includes("Permissions.Ppap.Credits.Manage") ?? false;
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<PpapJobStatus | "">("");
  const [search, setSearch] = useState("");
  const [decision, setDecision] = useState<Decision | null>(null);
  const [note, setNote] = useState("");
  const [noteError, setNoteError] = useState("");
  const enabled = permissionsHydrated && canView;
  const summary = useQuery({ queryKey: ["ppap", "operations", "summary"], queryFn: getPpapOperationsSummary, enabled });
  const jobs = useQuery({ queryKey: ["ppap", "operations", "jobs", { page, status, search }], queryFn: () => searchPpapJobs({ pageNumber: page, pageSize: PAGE_SIZE, status: status || undefined, search: search.trim() || undefined, sort: "-createdAtUtc" }), placeholderData: keepPreviousData, enabled });
  const topups = useQuery({ queryKey: ["ppap", "credits", "topup-requests", { status: "Pending" }], queryFn: () => searchCreditTopupRequests({ status: "Pending", pageSize: 100, sort: "-requestedAt" }), enabled: enabled && canManageCredits });
  const finish = async (message: string) => { toast.success(message); setDecision(null); setNote(""); await Promise.all([queryClient.invalidateQueries({ queryKey: ["ppap", "operations"] }), queryClient.invalidateQueries({ queryKey: ["ppap", "credits", "topup-requests"] })]); };
  const approve = useMutation({ mutationFn: approveCreditTopupRequest, onSuccess: () => finish("Top-up approved"), onError: (error) => toast.error("Approval failed", { description: describe(error) }) });
  const reject = useMutation({ mutationFn: rejectCreditTopupRequest, onSuccess: () => finish("Top-up rejected"), onError: (error) => toast.error("Rejection failed", { description: describe(error) }) });
  const pending = approve.isPending || reject.isPending;
  const submitDecision = (event: FormEvent) => { event.preventDefault(); if (!decision) return; if (note.trim().length > 512) { setNoteError("Decision note must be 512 characters or fewer."); return; } const input = { id: decision.request.id, note: note.trim() || undefined }; (decision.mode === "approve" ? approve : reject).mutate(input); };

  if (permissionsHydrated && !canView) return <EntityEmpty icon={ClipboardCheck} title="PPAP operations unavailable" body="You do not have permission to view tenant operations." />;
  return <div className="space-y-6">
    <EntityPageHeader icon={ClipboardCheck} title="PPAP operations" description="Monitor tenant submissions, processing jobs, reports, and credit decisions." />
    {summary.isError && <ErrorBand message={describe(summary.error)} />}
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{[
      ["Submissions", summary.data?.totalSubmissions], ["Active jobs", (summary.data?.queuedJobs ?? 0) + (summary.data?.processingJobs ?? 0)], ["Completed / failed", summary.data ? `${summary.data.completedJobs} / ${summary.data.failedJobs}` : undefined], ["Pending top-ups", summary.data?.pendingTopupRequests],
    ].map(([label, value]) => <Card key={label as string}><CardContent className="pt-5"><p className="text-xs text-[var(--color-muted-foreground)]">{label}</p>{summary.isLoading ? <Skeleton className="mt-2 h-7 w-16" /> : <p className="mt-1 text-2xl font-semibold">{value ?? "—"}</p>}</CardContent></Card>)}</div>
    <Card><CardHeader><CardTitle className="flex items-center gap-2"><Activity className="size-4" />Processing jobs</CardTitle><CardDescription>Live stages, failures, and generated reports.</CardDescription></CardHeader><CardContent className="space-y-4">
      <div className="flex flex-wrap gap-2"><Input aria-label="Search jobs" className="max-w-xs" placeholder="Part name or number" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} /><select aria-label="Job status" className="rounded-lg border border-[var(--color-input)] bg-[var(--color-card)] px-3 text-sm" value={status} onChange={(event) => { setStatus(event.target.value as PpapJobStatus | ""); setPage(1); }}><option value="">All statuses</option>{STATUSES.map((value) => <option key={value}>{value}</option>)}</select></div>
      {jobs.isError && <ErrorBand message={describe(jobs.error)} />}{jobs.isLoading && <Skeleton className="h-40 w-full" />}
      {jobs.data?.items.length === 0 && <EntityEmpty icon={FileSearch} title="No jobs found" body="No processing jobs match the current filters." />}
      {(jobs.data?.items.length ?? 0) > 0 && <div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead className="border-b text-xs text-[var(--color-muted-foreground)]"><tr><th className="p-3">Part</th><th>Status</th><th>Stage</th><th>Progress</th><th>Created</th><th>Result</th></tr></thead><tbody>{jobs.data?.items.map((job) => <tr key={job.id} className="border-b border-[var(--color-border)]"><td className="p-3"><strong>{job.partName}</strong><div className="font-mono text-xs">{job.partNumber}</div></td><td><EntityStatusBadge tone={tone(job.status)}>{job.status}</EntityStatusBadge></td><td>{job.currentStage}</td><td>{job.progressPercent}%</td><td className="whitespace-nowrap text-xs">{formatDateTimeMono(job.createdAtUtc)}</td><td>{job.reportId && job.status === "Completed" && canReview ? <Button asChild size="sm" variant="outline"><Link to={`/ppap/reports/${job.reportId}/review`}>Review</Link></Button> : job.errorMessage ?? "—"}</td></tr>)}</tbody></table></div>}
      {jobs.data && <EntityPager page={jobs.data.pageNumber} totalPages={jobs.data.totalPages} hasPrev={jobs.data.hasPrevious} hasNext={jobs.data.hasNext} onPrev={() => setPage((value) => Math.max(1, value - 1))} onNext={() => setPage((value) => value + 1)} />}
    </CardContent></Card>
    {canManageCredits && <Card><CardHeader><CardTitle className="flex items-center gap-2"><WalletCards className="size-4" />Pending credit top-ups</CardTitle></CardHeader><CardContent>{topups.isError && <ErrorBand message={describe(topups.error)} />}{topups.isLoading && <Skeleton className="h-24 w-full" />}{topups.data?.items.length === 0 && <EntityEmpty icon={WalletCards} title="No pending requests" />}{topups.data?.items.map((request) => <div key={request.id} className="flex flex-wrap items-center justify-between gap-3 border-b py-3"><div><strong>{request.creditsRequested} credits</strong><p className="font-mono text-xs text-[var(--color-muted-foreground)]">{request.ownerUserId} · {formatDateTimeMono(request.requestedAt)}</p></div><div className="flex gap-2"><Button size="sm" onClick={() => { setDecision({ request, mode: "approve" }); setNote(""); }}><Check className="size-4" />Approve</Button><Button size="sm" variant="outline" onClick={() => { setDecision({ request, mode: "reject" }); setNote(""); }}><X className="size-4" />Reject</Button></div></div>)}</CardContent></Card>}
    <Dialog open={decision !== null} onOpenChange={(open) => { if (!open && !pending) setDecision(null); }}><DialogContent><form onSubmit={submitDecision}><DialogHeader><DialogTitle>{decision?.mode === "reject" ? "Reject" : "Approve"} credit top-up</DialogTitle><DialogDescription>Record an optional internal decision note.</DialogDescription></DialogHeader><DialogBody><Label htmlFor="decision-note">Decision note</Label><Input id="decision-note" className="mt-2" value={note} onChange={(event) => { setNote(event.target.value); setNoteError(""); }} disabled={pending} />{noteError && <p role="alert" className="mt-1 text-xs text-[var(--color-destructive)]">{noteError}</p>}</DialogBody><DialogFooter><Button type="button" variant="outline" onClick={() => setDecision(null)} disabled={pending}>Cancel</Button><Button type="submit" variant={decision?.mode === "reject" ? "destructive" : "default"} disabled={pending}>{pending ? "Saving…" : "Confirm"}</Button></DialogFooter></form></DialogContent></Dialog>
  </div>;
}
