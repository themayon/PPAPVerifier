import { useState } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Controller, useForm } from "react-hook-form";
import { ArrowLeft, ClipboardList, FilePlus2, ShieldCheck } from "lucide-react";
import { Link, useParams } from "react-router-dom";
import { toast } from "sonner";
import { z } from "zod";
import {
  createManualPpapFinding,
  getPpapReportReview,
  revokePpapFindingWaiver,
  waivePpapFinding,
  type PpapFindingKind,
  type PpapFindingWaiverDto,
  type PpapManifestDocumentDto,
} from "@/api/ppap";
import { useAuth } from "@/auth/use-auth";
import { EmptyState } from "@/components/empty-state";
import { EntityPageHeader, ErrorBand, LoadingRow, Select, Stat, StatStrip } from "@/components/list";
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
import { Label } from "@/components/ui/label";
import { ApiRequestError } from "@/lib/api-client";
import { PpapPermissions } from "@/lib/permissions";

const manualSchema = z.object({
  message: z.string().trim().min(1, "Message is required.").max(1000, "Message must be 1,000 characters or fewer."),
  evidenceNote: z.string().trim().max(2000, "Evidence must be 2,000 characters or fewer."),
  documentId: z.string(),
});
const reasonSchema = z.object({
  reason: z.string().trim().min(1, "Reason is required.").max(1000, "Reason must be 1,000 characters or fewer."),
});
type ManualValues = z.infer<typeof manualSchema>;
type ReasonValues = z.infer<typeof reasonSchema>;
type FindingAction = { kind: "waive"; findingKind: PpapFindingKind; findingId: string; title: string }
  | { kind: "revoke"; waiverId: string; title: string };

function describe(error: unknown, fallback: string) {
  if (error instanceof ApiRequestError) return error.problem?.detail ?? error.problem?.title ?? error.message;
  if (error instanceof Error) return error.message;
  return fallback;
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}

function EvidenceIds({ submissionDocumentId, canonicalDocumentId, extractionAttemptId }: {
  submissionDocumentId: string | null;
  canonicalDocumentId: string | null;
  extractionAttemptId: string | null;
}) {
  if (!submissionDocumentId && !canonicalDocumentId && !extractionAttemptId) {
    return <p className="text-xs text-[var(--color-muted-foreground)]">Cross-document scope · no evidence IDs</p>;
  }
  return (
    <dl className="grid gap-1 text-xs sm:grid-cols-3">
      <div><dt className="text-[var(--color-muted-foreground)]">Submission document</dt><dd className="break-all font-mono">{submissionDocumentId ?? "—"}</dd></div>
      <div><dt className="text-[var(--color-muted-foreground)]">Canonical document</dt><dd className="break-all font-mono">{canonicalDocumentId ?? "—"}</dd></div>
      <div><dt className="text-[var(--color-muted-foreground)]">Extraction attempt</dt><dd className="break-all font-mono">{extractionAttemptId ?? "—"}</dd></div>
    </dl>
  );
}

function WaiverHistory({ waivers, canManage, onRevoke }: {
  waivers: PpapFindingWaiverDto[];
  canManage: boolean;
  onRevoke: (waiver: PpapFindingWaiverDto) => void;
}) {
  if (waivers.length === 0) return <p className="text-xs text-[var(--color-muted-foreground)]">No waiver history.</p>;
  return (
    <div className="space-y-2" aria-label="Waiver history">
      {waivers.map((waiver) => (
        <div key={waiver.id} className="rounded-md border border-[var(--color-border)] p-3 text-sm">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <Badge variant={waiver.isActive ? "success" : "default"}>{waiver.isActive ? "Active waiver" : "Revoked waiver"}</Badge>
            {canManage && waiver.isActive && <Button size="sm" variant="outline" onClick={() => onRevoke(waiver)}>Revoke waiver</Button>}
          </div>
          <p className="mt-2">{waiver.reason}</p>
          <p className="mt-1 text-xs text-[var(--color-muted-foreground)]">Approved by <span className="font-mono">{waiver.approvedByUserId}</span> · {formatDate(waiver.approvedAtUtc)}</p>
          {waiver.revocation && (
            <div className="mt-3 border-l-2 border-[var(--color-border-strong)] pl-3">
              <p>{waiver.revocation.reason}</p>
              <p className="mt-1 text-xs text-[var(--color-muted-foreground)]">Revoked by <span className="font-mono">{waiver.revocation.revokedByUserId}</span> · {formatDate(waiver.revocation.revokedAtUtc)}</p>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

export function PpapReportReviewPage() {
  const { reportId = "" } = useParams();
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const permissions = user?.permissions ?? [];
  const canCreateManual = permissions.includes(PpapPermissions.Findings.CreateManual);
  const canManageWaivers = permissions.includes(PpapPermissions.Findings.ManageWaivers);
  const [manualOpen, setManualOpen] = useState(false);
  const [action, setAction] = useState<FindingAction | null>(null);
  const manualForm = useForm<ManualValues>({
    resolver: zodResolver(manualSchema),
    defaultValues: { message: "", evidenceNote: "", documentId: "" },
  });
  const reasonForm = useForm<ReasonValues>({
    resolver: zodResolver(reasonSchema),
    defaultValues: { reason: "" },
  });
  const reviewQuery = useQuery({
    queryKey: ["ppap", "operations", "reports", reportId, "review"],
    queryFn: () => getPpapReportReview(reportId),
    enabled: Boolean(reportId),
  });
  const invalidateReview = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["ppap", "operations", "reports", reportId, "review"] }),
      queryClient.invalidateQueries({ queryKey: ["ppap", "operations"] }),
    ]);
  };
  const createMutation = useMutation({
    mutationFn: createManualPpapFinding,
    onSuccess: async () => {
      toast.success("Manual finding added");
      setManualOpen(false);
      manualForm.reset();
      await invalidateReview();
    },
    onError: (error) => toast.error("Finding creation failed", { description: describe(error, "Could not create the manual finding.") }),
  });
  const waiveMutation = useMutation({
    mutationFn: waivePpapFinding,
    onSuccess: async () => {
      toast.success("Finding waived");
      setAction(null);
      reasonForm.reset();
      await invalidateReview();
    },
    onError: (error) => toast.error("Waiver failed", { description: describe(error, "Could not waive the finding.") }),
  });
  const revokeMutation = useMutation({
    mutationFn: revokePpapFindingWaiver,
    onSuccess: async () => {
      toast.success("Waiver revoked");
      setAction(null);
      reasonForm.reset();
      await invalidateReview();
    },
    onError: (error) => toast.error("Revocation failed", { description: describe(error, "Could not revoke the waiver.") }),
  });
  const report = reviewQuery.data;
  const documents = report?.reproducibilityManifest?.documents.filter((document) => document.extraction !== null) ?? [];
  const pending = waiveMutation.isPending || revokeMutation.isPending;
  const openAction = (next: FindingAction) => {
    reasonForm.reset({ reason: "" });
    setAction(next);
  };
  const submitManual = manualForm.handleSubmit((values) => {
    const document = documents.find((candidate) => candidate.id === values.documentId);
    createMutation.mutate({
      reportId,
      message: values.message,
      evidenceNote: values.evidenceNote || undefined,
      submissionDocumentId: document?.id,
      canonicalDocumentId: document?.extraction?.canonicalDocumentId,
      extractionAttemptId: document?.extraction?.attemptId,
      idempotencyKey: crypto.randomUUID(),
    });
  });
  const submitAction = reasonForm.handleSubmit(({ reason }) => {
    if (!action) return;
    if (action.kind === "waive") {
      waiveMutation.mutate({ reportId, findingKind: action.findingKind, findingId: action.findingId, reason, idempotencyKey: crypto.randomUUID() });
    } else {
      revokeMutation.mutate({ reportId, waiverId: action.waiverId, reason, idempotencyKey: crypto.randomUUID() });
    }
  });

  return (
    <div className="space-y-6">
      <EntityPageHeader
        icon={ClipboardList}
        title="PPAP finding review"
        tone="primary"
        description={report ? `${report.partName} · ${report.partNumber} · revision ${report.drawingRevision}` : "Review automated findings, manual findings, and waiver history."}
      >
        <Button asChild variant="outline" size="sm"><Link to="/ppap"><ArrowLeft className="mr-1 size-4" />PPAP operations</Link></Button>
        {canCreateManual && <Button size="sm" onClick={() => { manualForm.reset(); setManualOpen(true); }}><FilePlus2 className="mr-1 size-4" />Add manual finding</Button>}
      </EntityPageHeader>

      {reviewQuery.isError && <ErrorBand message={describe(reviewQuery.error, "Failed to load the report review.")} />}
      {reviewQuery.isLoading && <LoadingRow label="Loading finding review" />}
      {report && (
        <>
          <StatStrip cols={4}>
            <Stat label="Automated" value={report.automatedFindingCount.toLocaleString()} hint="rule findings" />
            <Stat label="Manual" value={report.manualFindingCount.toLocaleString()} hint="operator findings" />
            <Stat label="Open" value={report.openFindingCount.toLocaleString()} hint="requires review" tone={report.openFindingCount > 0 ? "warning" : "default"} />
            <Stat label="Waived" value={report.waivedFindingCount.toLocaleString()} hint="active waivers" />
          </StatStrip>

          <section aria-labelledby="automated-findings-title" className="space-y-3">
            <div><h2 id="automated-findings-title" className="font-display text-lg font-semibold">Automated findings</h2><p className="text-sm text-[var(--color-muted-foreground)]">Findings produced by the completed evaluation.</p></div>
            {report.findings.length === 0 ? <EmptyState icon={ShieldCheck} kicker="Automated" title="No automated findings" description="The evaluation produced no automated findings." /> : report.findings.map((finding) => {
              const active = finding.waivers.some((waiver) => waiver.isActive);
              return (
                <Card key={finding.id}>
                  <CardHeader><div className="flex flex-wrap items-start justify-between gap-3"><div><CardTitle>{finding.stableRuleCode} · {finding.ruleName}</CardTitle><CardDescription>{finding.documentCode ? `Document ${finding.documentCode}` : "Cross-document scope"} · {finding.outcome} · {formatDate(finding.evaluatedAtUtc)}</CardDescription></div>{canManageWaivers && !active && <Button size="sm" variant="outline" onClick={() => openAction({ kind: "waive", findingKind: "Automated", findingId: finding.id, title: finding.stableRuleCode })}>Waive finding</Button>}</div></CardHeader>
                  <CardContent className="space-y-4"><div><p>{finding.message ?? "No finding message."}</p>{finding.reason && <p className="mt-1 text-sm text-[var(--color-muted-foreground)]">{finding.reason}</p>}</div><EvidenceIds submissionDocumentId={finding.submissionDocumentId} canonicalDocumentId={finding.canonicalDocumentId} extractionAttemptId={finding.extractionAttemptId} /><WaiverHistory waivers={finding.waivers} canManage={canManageWaivers} onRevoke={(waiver) => openAction({ kind: "revoke", waiverId: waiver.id, title: finding.stableRuleCode })} /></CardContent>
                </Card>
              );
            })}
          </section>

          <section aria-labelledby="manual-findings-title" className="space-y-3">
            <div><h2 id="manual-findings-title" className="font-display text-lg font-semibold">Manual findings</h2><p className="text-sm text-[var(--color-muted-foreground)]">Operator-recorded findings and their evidence.</p></div>
            {report.manualFindings.length === 0 ? <EmptyState icon={FilePlus2} kicker="Manual" title="No manual findings" description="No operator has added a manual finding." /> : report.manualFindings.map((finding) => {
              const active = finding.waivers.some((waiver) => waiver.isActive);
              const isOwnFinding = finding.createdByUserId === user?.id;
              return (
                <Card key={finding.id}>
                  <CardHeader><div className="flex flex-wrap items-start justify-between gap-3"><div><CardTitle>{finding.message}</CardTitle><CardDescription>{finding.documentCode ? `Document ${finding.documentCode}` : "Cross-document scope"} · created by {finding.createdByUserId} · {formatDate(finding.createdAtUtc)}</CardDescription></div>{canManageWaivers && !active && !isOwnFinding && <Button size="sm" variant="outline" onClick={() => openAction({ kind: "waive", findingKind: "Manual", findingId: finding.id, title: finding.message })}>Waive finding</Button>}</div></CardHeader>
                  <CardContent className="space-y-4">{finding.evidenceNote && <div><p className="text-xs font-medium uppercase tracking-wide text-[var(--color-muted-foreground)]">Evidence</p><p className="mt-1 text-sm">{finding.evidenceNote}</p></div>}<EvidenceIds submissionDocumentId={finding.submissionDocumentId} canonicalDocumentId={finding.canonicalDocumentId} extractionAttemptId={finding.extractionAttemptId} /><WaiverHistory waivers={finding.waivers} canManage={canManageWaivers} onRevoke={(waiver) => openAction({ kind: "revoke", waiverId: waiver.id, title: finding.message })} /></CardContent>
                </Card>
              );
            })}
          </section>
        </>
      )}

      <Dialog open={manualOpen} onOpenChange={(open) => { if (!open && !createMutation.isPending) setManualOpen(false); }}>
        <DialogContent>
          <form onSubmit={submitManual}>
            <DialogHeader><DialogTitle>Add manual finding</DialogTitle><DialogDescription>Record a cross-document finding or correlate it to a manifest document with completed extraction.</DialogDescription></DialogHeader>
            <DialogBody className="space-y-4">
              <div><Label htmlFor="manual-document">Document scope</Label><Controller control={manualForm.control} name="documentId" render={({ field }) => <Select id="manual-document" className="mt-2" value={field.value} onValueChange={field.onChange} emptyLabel="Cross-document" options={documents.map((document: PpapManifestDocumentDto) => ({ value: document.id, label: document.documentCode, hint: document.status }))} disabled={createMutation.isPending} />} /></div>
              <div><Label htmlFor="manual-message">Message</Label><textarea id="manual-message" rows={4} className="mt-2 w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 text-sm outline-none focus-visible:border-[var(--color-ring)] focus-visible:ring-[3px] focus-visible:ring-[oklch(from_var(--color-ring)_l_c_h_/_0.5)]" disabled={createMutation.isPending} aria-invalid={Boolean(manualForm.formState.errors.message)} aria-describedby={manualForm.formState.errors.message ? "manual-message-error" : undefined} {...manualForm.register("message")} />{manualForm.formState.errors.message && <p id="manual-message-error" role="alert" className="mt-1 text-xs text-[var(--color-destructive)]">{manualForm.formState.errors.message.message}</p>}</div>
              <div><Label htmlFor="manual-evidence">Evidence</Label><textarea id="manual-evidence" rows={3} className="mt-2 w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 text-sm outline-none focus-visible:border-[var(--color-ring)] focus-visible:ring-[3px] focus-visible:ring-[oklch(from_var(--color-ring)_l_c_h_/_0.5)]" placeholder="Optional evidence note" disabled={createMutation.isPending} aria-invalid={Boolean(manualForm.formState.errors.evidenceNote)} aria-describedby={manualForm.formState.errors.evidenceNote ? "manual-evidence-error" : undefined} {...manualForm.register("evidenceNote")} />{manualForm.formState.errors.evidenceNote && <p id="manual-evidence-error" role="alert" className="mt-1 text-xs text-[var(--color-destructive)]">{manualForm.formState.errors.evidenceNote.message}</p>}</div>
            </DialogBody>
            <DialogFooter><Button type="button" variant="outline" disabled={createMutation.isPending} onClick={() => setManualOpen(false)}>Cancel</Button><Button type="submit" disabled={createMutation.isPending}>{createMutation.isPending ? "Adding…" : "Add finding"}</Button></DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      <Dialog open={action !== null} onOpenChange={(open) => { if (!open && !pending) setAction(null); }}>
        <DialogContent>
          <form onSubmit={submitAction}>
            <DialogHeader><DialogTitle>{action?.kind === "revoke" ? "Revoke waiver" : "Waive finding"}</DialogTitle><DialogDescription>{action ? `${action.title}. Record the reason for this ${action.kind === "revoke" ? "revocation" : "waiver"}.` : "A reason is required."}</DialogDescription></DialogHeader>
            <DialogBody><Label htmlFor="finding-action-reason">Reason</Label><textarea id="finding-action-reason" rows={4} className="mt-2 w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 text-sm outline-none focus-visible:border-[var(--color-ring)] focus-visible:ring-[3px] focus-visible:ring-[oklch(from_var(--color-ring)_l_c_h_/_0.5)]" disabled={pending} aria-invalid={Boolean(reasonForm.formState.errors.reason)} aria-describedby={reasonForm.formState.errors.reason ? "finding-action-reason-error" : undefined} {...reasonForm.register("reason")} />{reasonForm.formState.errors.reason && <p id="finding-action-reason-error" role="alert" className="mt-1 text-xs text-[var(--color-destructive)]">{reasonForm.formState.errors.reason.message}</p>}</DialogBody>
            <DialogFooter><Button type="button" variant="outline" disabled={pending} onClick={() => setAction(null)}>Cancel</Button><Button type="submit" variant={action?.kind === "revoke" ? "destructive" : "default"} disabled={pending}>{pending ? "Saving…" : action?.kind === "revoke" ? "Revoke waiver" : "Waive finding"}</Button></DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
