import { lazy, Suspense, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, CircleAlert, FileSpreadsheet, FileText, X } from "lucide-react";
import type { SpreadsheetHighlightRange } from "omnex-spreadjs";
import { requestFindingWaiver, type SubmissionWorkspaceDto, type WorkingFindingDto } from "@/api/ppap";
import { Button } from "@/components/ui/button";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { toast } from "sonner";
import type { PdfEvidenceBox } from "@/pages/ppap/pdf-viewer";

const PpapPdfViewer = lazy(() => import("@/pages/ppap/pdf-viewer").then((module) => ({ default: module.PpapPdfViewer })));
const OmnexSpreadsheetViewer = lazy(() => import("@/pages/ppap/omnex-spread-viewer").then((module) => ({ default: module.OmnexSpreadsheetViewer })));

type EvidenceLocation = {
  artifactKind?: string;
  pageNumber?: number;
  boundingBox?: { x: number; y: number; width: number; height: number };
  sheetName?: string;
  startCell?: string;
  endCell?: string;
  observedValue?: string;
  expectedValue?: string;
};

export function DocumentWorkspaceDialog({ workspace, selectedDocumentId, onSelect, onClose }: { workspace: SubmissionWorkspaceDto; selectedDocumentId: string; onSelect: (documentId: string) => void; onClose: () => void }) {
  const queryClient = useQueryClient();
  const document = workspace.documents.find((item) => item.id === selectedDocumentId) ?? workspace.documents[0];
  const revision = document?.currentRevision;
  const findings = workspace.findings.filter((finding) => finding.submissionDocumentId === document?.id || finding.submissionDocumentId === null);
  const [view, setView] = useState<"pdf" | "spreadsheet">("pdf");
  const [activeFindingId, setActiveFindingId] = useState<string | null>(findings[0]?.id ?? null);
  const [waiverFinding, setWaiverFinding] = useState<WorkingFindingDto | null>(null);
  const [waiverReason, setWaiverReason] = useState("");
  const activeFinding = findings.find((finding) => finding.id === activeFindingId) ?? null;
  const activeEvidence = useMemo(() => activeFinding ? parseEvidence(activeFinding.evidenceJson) : [], [activeFinding]);
  const pdfBoxes = useMemo(() => activeEvidence.flatMap<PdfEvidenceBox>((item) => item.pageNumber && item.boundingBox ? [{ pageNumber: item.pageNumber, ...item.boundingBox, label: activeFinding?.message }] : []), [activeEvidence, activeFinding?.message]);
  const spreadsheetHighlights = useMemo(() => findings.flatMap<SpreadsheetHighlightRange>((finding) => parseEvidence(finding.evidenceJson).flatMap((item) => item.sheetName && item.startCell ? [{ sheetName: item.sheetName, range: item.endCell ? `${item.startCell}:${item.endCell}` : item.startCell, tone: finding.id === activeFindingId ? "error" : "warning", label: finding.message }] : [])), [activeFindingId, findings]);
  const selectedCell = activeEvidence.find((item) => item.sheetName && item.startCell);
  const pdfFileId = revision?.previewPdfFileAssetId ?? (revision?.sourceContentType === "application/pdf" ? revision.sourceFileAssetId : null);
  const workbookFileId = revision?.generatedWorkbookFileAssetId ?? null;
  const currentIndex = workspace.documents.findIndex((item) => item.id === document?.id);
  const pendingWaivers = new Map(workspace.waiverRequests.filter((request) => request.status === "Pending").map((request) => [request.workingFindingId, request]));
  const requestWaiver = useMutation({
    mutationFn: requestFindingWaiver,
    onSuccess: async () => {
      toast.success("Waiver request submitted");
      setWaiverFinding(null);
      setWaiverReason("");
      await queryClient.invalidateQueries({ queryKey: ["ppap", "submissions", workspace.submissionId, "workspace"] });
    },
    onError: (error) => toast.error("Unable to request waiver", { description: describe(error) }),
  });
  if (!document) return null;

  const activateFinding = (finding: WorkingFindingDto) => {
    setActiveFindingId(finding.id);
    const evidence = parseEvidence(finding.evidenceJson);
    setView(evidence.some((item) => item.sheetName && item.startCell) ? "spreadsheet" : "pdf");
  };

  return <div className="fixed inset-0 z-50 flex flex-col bg-[var(--color-background)]" role="dialog" aria-modal="true" aria-label={`${document.documentCode} document workspace`}>
    <header className="shrink-0 border-b border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
      <div className="flex items-center justify-between gap-4 px-4 py-3">
        <div className="flex min-w-0 items-center gap-3"><Button variant="ghost" size="icon-sm" aria-label="Back to submission" onClick={onClose}><ChevronLeft /></Button><span className="grid size-9 place-items-center rounded-lg bg-[var(--color-primary-soft)] font-mono text-xs font-bold text-[var(--color-primary)]">{document.documentCode}</span><div className="min-w-0"><h2 className="truncate text-lg font-semibold">{revision?.sourceFileName ?? document.documentCode}</h2><p className="text-xs text-[var(--color-muted-foreground)]">Revision {revision?.revisionNumber ?? "—"} · {revision?.processingStatus ?? document.status} · {findings.length} finding{findings.length === 1 ? "" : "s"}</p></div></div>
        <Button variant="ghost" size="icon-sm" aria-label="Close workspace" onClick={onClose}><X /></Button>
      </div>
      <div className="flex gap-1.5 overflow-x-auto border-t border-[var(--color-border)] px-4 py-2">{workspace.documents.map((item) => <button key={item.id} type="button" onClick={() => { onSelect(item.id); setActiveFindingId(null); }} className={cn("min-w-14 rounded-md border px-2 py-2 font-mono text-[10px] font-bold", item.id === document.id ? "border-[var(--color-primary)] bg-[var(--color-primary)] text-white" : "border-[var(--color-border)] bg-[var(--color-background)]")}>{item.documentCode}</button>)}</div>
    </header>

    <div className="grid min-h-0 flex-1 gap-4 overflow-hidden p-4 pb-20 xl:grid-cols-[minmax(0,1fr)_380px]">
      <section className="min-h-0 overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
        <div className="flex gap-1 border-b border-[var(--color-border)] p-2"><Button variant={view === "pdf" ? "default" : "ghost"} size="sm" disabled={!pdfFileId} onClick={() => setView("pdf")}><FileText />Source document</Button><Button variant={view === "spreadsheet" ? "default" : "ghost"} size="sm" disabled={!workbookFileId} onClick={() => setView("spreadsheet")}><FileSpreadsheet />Extracted spreadsheet</Button></div>
        <div className="h-[calc(100%-3.25rem)] overflow-auto p-3"><Suspense fallback={<div className="grid min-h-72 place-items-center text-sm text-[var(--color-muted-foreground)]">Loading viewer…</div>}>{view === "pdf" ? pdfFileId ? <PpapPdfViewer fileAssetId={pdfFileId} evidence={pdfBoxes} activePage={activeEvidence.find((item) => item.pageNumber)?.pageNumber} /> : <Unavailable title="PDF preview unavailable" /> : workbookFileId ? <OmnexSpreadsheetViewer fileAssetId={workbookFileId} highlights={spreadsheetHighlights} selected={selectedCell?.sheetName && selectedCell.startCell ? { sheetName: selectedCell.sheetName, range: selectedCell.endCell ? `${selectedCell.startCell}:${selectedCell.endCell}` : selectedCell.startCell } : null} /> : <Unavailable title="Extracted workbook unavailable" />}</Suspense></div>
      </section>

      <aside className="min-h-0 overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs"><div className="border-b border-[var(--color-border)] p-4"><div className="flex items-center gap-2"><CircleAlert className="size-4 text-[var(--color-destructive)]" /><h3 className="text-sm font-semibold">Validation findings</h3><span className="rounded-full bg-[var(--color-primary-soft)] px-2 py-0.5 text-[10px] font-bold text-[var(--color-primary)]">{findings.length}</span></div><p className="mt-1 text-xs text-[var(--color-muted-foreground)]">Select a finding to open its page or spreadsheet cell.</p></div><div className="h-[calc(100%-5rem)] space-y-2 overflow-y-auto p-3">{findings.length === 0 && <Unavailable title="No current findings" />}{findings.map((finding) => { const pending = pendingWaivers.get(finding.id); return <article key={finding.id} className={cn("rounded-xl border p-3", finding.id === activeFindingId ? "border-[var(--color-primary)] bg-[var(--color-primary-soft)]" : "border-[var(--color-border)]")}><button type="button" className="w-full text-left" onClick={() => activateFinding(finding)}><span className="font-mono text-[10px] font-bold text-[var(--color-primary)]">{finding.stableRuleCode}</span><h4 className="mt-1 text-xs font-semibold">{finding.ruleName}</h4><p className="mt-1 text-[11px] leading-4 text-[var(--color-muted-foreground)]">{finding.message}</p></button><div className="mt-3 border-t border-[var(--color-border)] pt-2">{pending ? <span className="text-[11px] font-semibold text-[var(--color-warning)]">Waiver pending</span> : <Button variant="outline" size="xs" onClick={() => { setWaiverFinding(finding); setWaiverReason(""); }}>Request waiver</Button>}</div></article>; })}</div></aside>
    </div>

    <nav className="fixed bottom-0 right-0 z-[60] flex w-full max-w-md items-center gap-2 border-t border-[var(--color-border)] bg-[var(--color-card)] px-4 py-3 shadow-lg"><Button variant="outline" className="flex-1" disabled={currentIndex <= 0} onClick={() => onSelect(workspace.documents[currentIndex - 1]!.id)}><ChevronLeft />Previous</Button><span className="min-w-16 text-center text-xs font-semibold tabular-nums">{currentIndex + 1} / {workspace.documents.length}</span><Button className="flex-1" disabled={currentIndex >= workspace.documents.length - 1} onClick={() => onSelect(workspace.documents[currentIndex + 1]!.id)}>Next<ChevronRight /></Button></nav>

    <Dialog open={waiverFinding !== null} onOpenChange={(open) => !open && !requestWaiver.isPending && setWaiverFinding(null)}><DialogContent><form onSubmit={(event) => { event.preventDefault(); if (waiverFinding && waiverReason.trim()) requestWaiver.mutate({ submissionId: workspace.submissionId, findingId: waiverFinding.id, reason: waiverReason.trim() }); }}><DialogHeader><DialogTitle>Request finding waiver</DialogTitle><DialogDescription>{waiverFinding?.stableRuleCode} · This request is tied to the current document revision fingerprint.</DialogDescription></DialogHeader><DialogBody><Label htmlFor="waiver-reason">Reason</Label><textarea id="waiver-reason" required maxLength={1000} rows={4} value={waiverReason} onChange={(event) => setWaiverReason(event.target.value)} className="mt-2 w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-ring)]" /></DialogBody><DialogFooter><Button type="button" variant="outline" onClick={() => setWaiverFinding(null)}>Cancel</Button><Button type="submit" disabled={!waiverReason.trim() || requestWaiver.isPending}>{requestWaiver.isPending ? "Submitting…" : "Submit request"}</Button></DialogFooter></form></DialogContent></Dialog>
  </div>;
}

function parseEvidence(value: string): EvidenceLocation[] {
  try {
    const parsed: unknown = JSON.parse(value);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((item): item is EvidenceLocation => typeof item === "object" && item !== null);
  } catch {
    return [];
  }
}

function Unavailable({ title }: { title: string }) {
  return <div className="grid min-h-48 place-items-center text-center text-sm text-[var(--color-muted-foreground)]">{title}</div>;
}
