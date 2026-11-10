import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, CheckCircle2, ClipboardList, Clock, FilePlus2, Gavel, ShieldCheck, XCircle } from "lucide-react";
import { Link, useParams } from "react-router-dom";
import { toast } from "sonner";
import { createManualPpapFinding, decidePpapSubmission, getPpapReportReview, revokePpapFindingWaiver, waivePpapFinding, type PpapFindingKind, type PpapFindingWaiverDto, type PpapReportSubmissionStateDto, type SubmissionDecision } from "@/api/ppap";
import { useAuth } from "@/auth/use-auth";
import { EntityEmpty, EntityPageHeader, EntityStatusBadge, ErrorBand } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { describe, formatDateTimeMono } from "@/lib/list-helpers";
import { WaiverReviewPanel } from "@/pages/ppap/waiver-review-panel";

type FindingAction = { kind: "waive"; findingKind: PpapFindingKind; findingId: string; title: string } | { kind: "revoke"; waiverId: string; title: string };
const textareaClass = "mt-2 w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-ring)]";

function Evidence({ submissionDocumentId, canonicalDocumentId, extractionAttemptId }: { submissionDocumentId: string | null; canonicalDocumentId: string | null; extractionAttemptId: string | null }) {
  return <dl className="grid gap-2 text-xs sm:grid-cols-3">{[["Submission document", submissionDocumentId], ["Canonical document", canonicalDocumentId], ["Extraction attempt", extractionAttemptId]].map(([label, value]) => <div key={label}><dt className="text-[var(--color-muted-foreground)]">{label}</dt><dd className="break-all font-mono">{value ?? "—"}</dd></div>)}</dl>;
}
type DecisionChoice = Exclude<SubmissionDecision, "None">;
const DECISION_LABEL: Record<SubmissionDecision, string> = { None: "Awaiting decision", InterimApproved: "Interim approval", Approved: "Approved", Rejected: "Rejected" };
const DECISION_TONE: Record<SubmissionDecision, "default" | "warning" | "success" | "danger" | "info"> = { None: "info", InterimApproved: "warning", Approved: "success", Rejected: "danger" };

function DecisionPanel({ state, openFindings, canDecide, onDecided }: { state: PpapReportSubmissionStateDto; openFindings: number; canDecide: boolean; onDecided: () => Promise<void> }) {
  const [choice, setChoice] = useState<DecisionChoice | null>(null);
  const [comment, setComment] = useState("");
  const [error, setError] = useState("");
  const decide = useMutation({
    mutationFn: (input: { decision: DecisionChoice; comment: string | null }) => decidePpapSubmission(state.id, { ...input, expectedVersion: state.version }),
    onSuccess: async (result) => { toast.success(`Submission ${DECISION_LABEL[result.decision].toLowerCase()}`); setChoice(null); setComment(""); await onDecided(); },
    onError: (failure) => toast.error("Decision failed", { description: describe(failure) }),
  });
  const open = state.status === "Submitted" && (state.decision === "None" || state.decision === "InterimApproved");
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!choice) return;
    const value = comment.trim();
    if (choice !== "Approved" && !value) { setError("A comment is required for interim approval or rejection."); return; }
    if (value.length > 1000) { setError("Comment must be 1,000 characters or fewer."); return; }
    decide.mutate({ decision: choice, comment: value || null });
  };
  const start = (value: DecisionChoice) => { setChoice(value); setComment(""); setError(""); };
  return (
    <Card aria-label="Approval decision">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2"><Gavel className="size-4" />Approval decision</CardTitle>
            <CardDescription>{open ? "Record the customer decision for this PPAP submission." : `Submission status: ${state.status}.`}</CardDescription>
          </div>
          <EntityStatusBadge tone={DECISION_TONE[state.decision]} withDot>{DECISION_LABEL[state.decision]}</EntityStatusBadge>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {state.decisionComment && <p className="rounded-lg bg-[var(--color-muted)] px-3 py-2 text-sm">“{state.decisionComment}”{state.decidedAtUtc && <span className="ml-2 text-xs text-[var(--color-muted-foreground)]">{formatDateTimeMono(state.decidedAtUtc)}</span>}</p>}
        {!state.isLatestReport && <p role="note" className="text-xs text-[var(--color-warning)]">A newer report exists for this submission. Review the latest report before deciding.</p>}
        {open && canDecide && (
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => start("Approved")} disabled={decide.isPending}><CheckCircle2 className="size-4" />Approve</Button>
            {state.decision === "None" && <Button variant="outline" onClick={() => start("InterimApproved")} disabled={decide.isPending}><Clock className="size-4" />Interim approval</Button>}
            <Button variant="destructive" onClick={() => start("Rejected")} disabled={decide.isPending}><XCircle className="size-4" />Reject</Button>
          </div>
        )}
        {open && canDecide && openFindings > 0 && <p className="text-xs text-[var(--color-muted-foreground)]">{openFindings} open finding{openFindings === 1 ? "" : "s"} remain. Waive or resolve them, or record the reason in your decision comment.</p>}
        {open && !canDecide && <p className="text-xs text-[var(--color-muted-foreground)]">You need the Approve PPAP Submissions permission to record a decision.</p>}
      </CardContent>
      <Dialog open={choice !== null} onOpenChange={(value) => { if (!value && !decide.isPending) setChoice(null); }}>
        <DialogContent>
          <form onSubmit={submit}>
            <DialogHeader>
              <DialogTitle>{choice === "Approved" ? "Approve submission" : choice === "Rejected" ? "Reject submission" : "Grant interim approval"}</DialogTitle>
              <DialogDescription>{choice === "Approved" ? "Approval is final for this submission." : choice === "Rejected" ? "Rejection is final; the supplier must start a new submission." : "Interim approval allows limited shipments until full approval."}</DialogDescription>
            </DialogHeader>
            <DialogBody>
              <Label htmlFor="decision-comment">Comment{choice === "Approved" ? " (optional)" : ""}</Label>
              <textarea id="decision-comment" className={textareaClass} rows={4} value={comment} onChange={(event) => { setComment(event.target.value); setError(""); }} />
              {error && <p role="alert" className="text-xs text-[var(--color-destructive)]">{error}</p>}
            </DialogBody>
            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => setChoice(null)} disabled={decide.isPending}>Cancel</Button>
              <Button type="submit" variant={choice === "Rejected" ? "destructive" : "default"} disabled={decide.isPending}>{decide.isPending ? "Saving…" : "Confirm"}</Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </Card>
  );
}

function Waivers({ items, canManage, onRevoke }: { items: PpapFindingWaiverDto[]; canManage: boolean; onRevoke: (waiver: PpapFindingWaiverDto) => void }) {
  if (!items.length) return <p className="text-xs text-[var(--color-muted-foreground)]">No waiver history.</p>;
  return <div className="space-y-2">{items.map((waiver) => <div className="rounded-lg border p-3 text-sm" key={waiver.id}><div className="flex justify-between"><EntityStatusBadge tone={waiver.isActive ? "success" : "default"}>{waiver.isActive ? "Active waiver" : "Revoked"}</EntityStatusBadge>{canManage && waiver.isActive && <Button size="sm" variant="outline" onClick={() => onRevoke(waiver)}>Revoke</Button>}</div><p className="mt-2">{waiver.reason}</p><p className="text-xs text-[var(--color-muted-foreground)]">{formatDateTimeMono(waiver.approvedAtUtc)}</p></div>)}</div>;
}

export function PpapReportReviewPage() {
  const { reportId = "" } = useParams();
  const { user, permissionsHydrated } = useAuth();
  const queryClient = useQueryClient();
  const canView = user?.permissions.includes("Permissions.Ppap.Findings.View") ?? false;
  const canCreate = user?.permissions.includes("Permissions.Ppap.Findings.CreateManual") ?? false;
  const canManage = user?.permissions.includes("Permissions.Ppap.Findings.ManageWaivers") ?? false;
  const canDecide = user?.permissions.includes("Permissions.Ppap.Submissions.Approve") ?? false;
  const [manualOpen, setManualOpen] = useState(false);
  const [message, setMessage] = useState(""); const [evidence, setEvidence] = useState(""); const [documentId, setDocumentId] = useState(""); const [manualError, setManualError] = useState("");
  const [action, setAction] = useState<FindingAction | null>(null); const [reason, setReason] = useState(""); const [reasonError, setReasonError] = useState("");
  const query = useQuery({ queryKey: ["ppap", "operations", "reports", reportId, "review"], queryFn: () => getPpapReportReview(reportId), enabled: permissionsHydrated && canView && Boolean(reportId) });
  const refresh = async () => { await queryClient.invalidateQueries({ queryKey: ["ppap", "operations", "reports", reportId, "review"] }); };
  const create = useMutation({ mutationFn: createManualPpapFinding, onSuccess: async () => { toast.success("Manual finding added"); setManualOpen(false); setMessage(""); setEvidence(""); setDocumentId(""); await refresh(); }, onError: (error) => toast.error("Finding creation failed", { description: describe(error) }) });
  const waive = useMutation({ mutationFn: waivePpapFinding, onSuccess: async () => { toast.success("Finding waived"); setAction(null); setReason(""); await refresh(); }, onError: (error) => toast.error("Waiver failed", { description: describe(error) }) });
  const revoke = useMutation({ mutationFn: revokePpapFindingWaiver, onSuccess: async () => { toast.success("Waiver revoked"); setAction(null); setReason(""); await refresh(); }, onError: (error) => toast.error("Revocation failed", { description: describe(error) }) });
  const documents = query.data?.reproducibilityManifest?.documents.filter((item) => item.extraction) ?? [];
  const submitManual = (event: FormEvent) => { event.preventDefault(); const value = message.trim(); if (!value || value.length > 1000 || evidence.trim().length > 2000) { setManualError(!value ? "Message is required." : "Message or evidence is too long."); return; } const document = documents.find((item) => item.id === documentId); create.mutate({ reportId, message: value, evidenceNote: evidence.trim() || undefined, submissionDocumentId: document?.id, canonicalDocumentId: document?.extraction?.canonicalDocumentId, extractionAttemptId: document?.extraction?.attemptId, idempotencyKey: crypto.randomUUID() }); };
  const submitAction = (event: FormEvent) => { event.preventDefault(); const value = reason.trim(); if (!value || value.length > 1000) { setReasonError(!value ? "Reason is required." : "Reason must be 1,000 characters or fewer."); return; } if (!action) return; if (action.kind === "waive") waive.mutate({ reportId, findingKind: action.findingKind, findingId: action.findingId, reason: value, idempotencyKey: crypto.randomUUID() }); else revoke.mutate({ reportId, waiverId: action.waiverId, reason: value, idempotencyKey: crypto.randomUUID() }); };
  const openAction = (value: FindingAction) => { setReason(""); setReasonError(""); setAction(value); };
  if (permissionsHydrated && !canView) return <EntityEmpty icon={ClipboardList} title="Finding review unavailable" body="You do not have permission to review PPAP findings." />;
  const report = query.data; const pending = waive.isPending || revoke.isPending;
  return <div className="space-y-6"><EntityPageHeader icon={ClipboardList} title="PPAP finding review" description={report ? `${report.partName} · ${report.partNumber} · revision ${report.drawingRevision}` : "Review findings and waiver history."}><Button asChild variant="outline"><Link to="/ppap/parts"><ArrowLeft className="size-4" />Parts</Link></Button>{canCreate && <Button onClick={() => setManualOpen(true)}><FilePlus2 className="size-4" />Add finding</Button>}</EntityPageHeader>
    {query.isError && <ErrorBand message={describe(query.error)} />}{query.isLoading && <Skeleton className="h-48 w-full" />}
    {report && <>{report.submission && <DecisionPanel state={report.submission} openFindings={report.openFindingCount} canDecide={canDecide} onDecided={async () => { await refresh(); await queryClient.invalidateQueries({ queryKey: ["ppap", "parts"] }); }} />}<WaiverReviewPanel submissionId={report.submissionId} enabled={canManage} /><div className="grid gap-3 sm:grid-cols-4">{[["Automated", report.automatedFindingCount], ["Manual", report.manualFindingCount], ["Open", report.openFindingCount], ["Waived", report.waivedFindingCount]].map(([label, value]) => <Card key={label as string}><CardContent className="pt-5"><p className="text-xs text-[var(--color-muted-foreground)]">{label}</p><p className="text-2xl font-semibold">{value}</p></CardContent></Card>)}</div>
      <section className="space-y-3"><h2 className="text-lg font-semibold">Automated findings</h2>{!report.findings.length && <EntityEmpty icon={ShieldCheck} title="No automated findings" />}{report.findings.map((finding) => { const active = finding.waivers.some((item) => item.isActive); return <Card key={finding.id}><CardHeader><div className="flex justify-between gap-3"><div><CardTitle>{finding.stableRuleCode} · {finding.ruleName}</CardTitle><CardDescription>{finding.outcome} · {finding.documentCode ?? "Cross-document"}</CardDescription></div>{canManage && !active && <Button size="sm" variant="outline" onClick={() => openAction({ kind: "waive", findingKind: "Automated", findingId: finding.id, title: finding.stableRuleCode })}>Waive</Button>}</div></CardHeader><CardContent className="space-y-4"><p>{finding.message ?? finding.reason ?? "No message."}</p><Evidence {...finding} /><Waivers items={finding.waivers} canManage={canManage} onRevoke={(item) => openAction({ kind: "revoke", waiverId: item.id, title: finding.stableRuleCode })} /></CardContent></Card>; })}</section>
      <section className="space-y-3"><h2 className="text-lg font-semibold">Manual findings</h2>{!report.manualFindings.length && <EntityEmpty icon={FilePlus2} title="No manual findings" />}{report.manualFindings.map((finding) => { const active = finding.waivers.some((item) => item.isActive); return <Card key={finding.id}><CardHeader><div className="flex justify-between gap-3"><div><CardTitle>{finding.message}</CardTitle><CardDescription>Created by {finding.createdByUserId}</CardDescription></div>{canManage && !active && finding.createdByUserId !== user?.id && <Button size="sm" variant="outline" onClick={() => openAction({ kind: "waive", findingKind: "Manual", findingId: finding.id, title: finding.message })}>Waive</Button>}</div></CardHeader><CardContent className="space-y-4">{finding.evidenceNote && <p>{finding.evidenceNote}</p>}<Evidence {...finding} /><Waivers items={finding.waivers} canManage={canManage} onRevoke={(item) => openAction({ kind: "revoke", waiverId: item.id, title: finding.message })} /></CardContent></Card>; })}</section></>}
    <Dialog open={manualOpen} onOpenChange={setManualOpen}><DialogContent><form onSubmit={submitManual}><DialogHeader><DialogTitle>Add manual finding</DialogTitle><DialogDescription>Record a cross-document finding or attach extracted document evidence.</DialogDescription></DialogHeader><DialogBody className="space-y-4"><div><Label htmlFor="manual-document">Document scope</Label><select id="manual-document" className="mt-2 h-10 w-full rounded-lg border bg-[var(--color-card)] px-3" value={documentId} onChange={(event) => setDocumentId(event.target.value)}><option value="">Cross-document</option>{documents.map((item) => <option key={item.id} value={item.id}>{item.documentCode}</option>)}</select></div><div><Label htmlFor="manual-message">Message</Label><textarea id="manual-message" className={textareaClass} rows={4} value={message} onChange={(event) => { setMessage(event.target.value); setManualError(""); }} /></div><div><Label htmlFor="manual-evidence">Evidence</Label><textarea id="manual-evidence" className={textareaClass} rows={3} value={evidence} onChange={(event) => setEvidence(event.target.value)} /></div>{manualError && <p role="alert" className="text-xs text-[var(--color-destructive)]">{manualError}</p>}</DialogBody><DialogFooter><Button type="button" variant="outline" onClick={() => setManualOpen(false)}>Cancel</Button><Button type="submit" disabled={create.isPending}>{create.isPending ? "Adding…" : "Add finding"}</Button></DialogFooter></form></DialogContent></Dialog>
    <Dialog open={action !== null} onOpenChange={(open) => { if (!open && !pending) setAction(null); }}><DialogContent><form onSubmit={submitAction}><DialogHeader><DialogTitle>{action?.kind === "revoke" ? "Revoke waiver" : "Waive finding"}</DialogTitle><DialogDescription>{action?.title}</DialogDescription></DialogHeader><DialogBody><Label htmlFor="finding-reason">Reason</Label><textarea id="finding-reason" className={textareaClass} rows={4} value={reason} onChange={(event) => { setReason(event.target.value); setReasonError(""); }} />{reasonError && <p role="alert" className="text-xs text-[var(--color-destructive)]">{reasonError}</p>}</DialogBody><DialogFooter><Button type="button" variant="outline" onClick={() => setAction(null)}>Cancel</Button><Button type="submit" disabled={pending}>{pending ? "Saving…" : "Confirm"}</Button></DialogFooter></form></DialogContent></Dialog>
  </div>;
}
