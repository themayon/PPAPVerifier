import { useMemo, useRef, useState, type ChangeEvent, type ComponentType } from "react";
import {
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  CircleDashed,
  Clock3,
  CloudUpload,
  Download,
  Eye,
  FileCheck2,
  FileSpreadsheet,
  FileText,
  Grid2X2,
  Info,
  List,
  Maximize2,
  MessageSquareText,
  MoreHorizontal,
  Paperclip,
  RefreshCw,
  Search,
  Send,
  ShieldCheck,
  SlidersHorizontal,
  Sparkles,
  Upload,
  UserRound,
  X,
  ZoomIn,
  ZoomOut,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/cn";

type ElementStatus = "Approved" | "Under review" | "Uploaded" | "Missing";
type SubmissionElement = {
  id: number;
  code: string;
  name: string;
  description: string;
  status: ElementStatus;
  file?: string;
  updated?: string;
  reviewer?: string;
  required: boolean;
};

const INITIAL_ELEMENTS: SubmissionElement[] = [
  { id: 1, code: "DWG", name: "Design records", description: "CAD drawings and released specifications", status: "Approved", file: "Steering_Column_Rev_B.pdf", updated: "Today, 09:42", reviewer: "Marcus Chen", required: true },
  { id: 2, code: "ECD", name: "Engineering change documents", description: "Approved engineering changes for this revision", status: "Approved", file: "ECN-2026-018.pdf", updated: "Yesterday", reviewer: "Sarah Jenkins", required: true },
  { id: 3, code: "CEA", name: "Customer engineering approval", description: "OEM engineering authorization", status: "Under review", file: "Customer_Approval.pdf", updated: "2 hours ago", reviewer: "Alex Johnson", required: true },
  { id: 4, code: "DFMEA", name: "Design FMEA", description: "Design risk analysis and mitigations", status: "Under review", file: "DFMEA_Steering_v4.xlsx", updated: "4 hours ago", reviewer: "Marcus Chen", required: true },
  { id: 5, code: "PFD", name: "Process flow diagram", description: "End-to-end manufacturing process flow", status: "Missing", required: true },
  { id: 6, code: "PFMEA", name: "Process FMEA", description: "Manufacturing process risk analysis", status: "Missing", required: true },
  { id: 7, code: "CP", name: "Control plan", description: "Production controls linked to PFMEA", status: "Approved", file: "Control_Plan_Rev_C.pdf", updated: "3 days ago", reviewer: "David Lee", required: true },
  { id: 8, code: "MSA", name: "Measurement system analysis", description: "Gauge R&R and measurement studies", status: "Approved", file: "MSA_Report.xlsx", updated: "3 days ago", reviewer: "Elena Moreno", required: true },
  { id: 9, code: "DIMR", name: "Dimensional results", description: "Ballooned drawing inspection results", status: "Uploaded", file: "Dimensional_Results.xlsx", updated: "1 hour ago", required: true },
  { id: 10, code: "IMDS", name: "Material data submission", description: "IMDS material declaration and receipt", status: "Approved", file: "IMDS_Receipt.pdf", updated: "5 days ago", reviewer: "Marcus Chen", required: true },
  { id: 11, code: "MPTR", name: "Material test results", description: "Material and performance validation", status: "Approved", file: "Material_Test_Cert.pdf", updated: "5 days ago", reviewer: "Sarah Jenkins", required: true },
  { id: 12, code: "IPS", name: "Initial process studies", description: "Capability studies for key characteristics", status: "Under review", file: "Capability_Study.xlsx", updated: "Yesterday", reviewer: "David Lee", required: true },
  { id: 13, code: "QLD", name: "Qualified laboratory documentation", description: "Laboratory scope and accreditation", status: "Approved", file: "Lab_Accreditation.pdf", updated: "1 week ago", reviewer: "Elena Moreno", required: true },
  { id: 14, code: "AAR", name: "Appearance approval report", description: "Appearance criteria and approval evidence", status: "Missing", required: false },
  { id: 15, code: "SAMP", name: "Sample production parts", description: "Sample production part evidence", status: "Approved", file: "Sample_Log.pdf", updated: "1 week ago", reviewer: "Alex Johnson", required: true },
  { id: 16, code: "MSAM", name: "Master sample", description: "Master sample retention record", status: "Uploaded", file: "Master_Sample_Record.pdf", updated: "Today, 08:15", required: false },
  { id: 17, code: "CAID", name: "Checking aids", description: "Fixture and checking aid validation", status: "Approved", file: "Checking_Aid_Cert.pdf", updated: "1 week ago", reviewer: "Marcus Chen", required: false },
  { id: 18, code: "PSW", name: "Part submission warrant", description: "Final warrant for customer authorization", status: "Missing", required: true },
];

const STATUS_STYLE: Record<ElementStatus, { icon: ComponentType<{ className?: string }>; className: string; dot: string }> = {
  Approved: { icon: CheckCircle2, className: "bg-[oklch(from_var(--color-success)_l_c_h_/_0.11)] text-[var(--color-success)]", dot: "bg-[var(--color-success)]" },
  "Under review": { icon: Clock3, className: "bg-[var(--color-primary-soft)] text-[var(--color-primary)]", dot: "bg-[var(--color-primary)]" },
  Uploaded: { icon: CloudUpload, className: "bg-[oklch(from_var(--color-warning)_l_c_h_/_0.13)] text-[var(--color-warning)]", dot: "bg-[var(--color-warning)]" },
  Missing: { icon: CircleAlert, className: "bg-[oklch(from_var(--color-destructive)_l_c_h_/_0.10)] text-[var(--color-destructive)]", dot: "bg-[var(--color-destructive)]" },
};

const FILTERS = ["All", "Needs action", "Under review", "Approved"] as const;
type Filter = (typeof FILTERS)[number];

export function PpapSubmissionConceptPage() {
  const [elements, setElements] = useState(INITIAL_ELEMENTS);
  const [filter, setFilter] = useState<Filter>("All");
  const [search, setSearch] = useState("");
  const [view, setView] = useState<"list" | "grid">("list");
  const [selected, setSelected] = useState<SubmissionElement | null>(null);
  const [readinessOpen, setReadinessOpen] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const [uploadTarget, setUploadTarget] = useState<number | null>(null);

  const counts = useMemo(() => elements.reduce<Record<ElementStatus, number>>((result, item) => {
    result[item.status] += 1;
    return result;
  }, { Approved: 0, "Under review": 0, Uploaded: 0, Missing: 0 }), [elements]);
  const ready = counts.Approved;
  const percent = Math.round((ready / elements.length) * 100);
  const visible = elements.filter((item) => {
    const query = search.trim().toLowerCase();
    const matchesSearch = !query || `${item.code} ${item.name} ${item.file ?? ""}`.toLowerCase().includes(query);
    const matchesFilter = filter === "All"
      || (filter === "Needs action" && (item.status === "Missing" || item.status === "Uploaded"))
      || item.status === filter;
    return matchesSearch && matchesFilter;
  });
  const selectedIndex = selected ? elements.findIndex((item) => item.id === selected.id) : -1;
  const navigateDocument = (offset: number) => {
    const nextIndex = selectedIndex + offset;
    if (nextIndex >= 0 && nextIndex < elements.length) setSelected(elements[nextIndex] ?? null);
  };

  const chooseUpload = (id: number) => {
    setUploadTarget(id);
    fileInput.current?.click();
  };
  const applyUpload = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file || uploadTarget === null) return;
    setElements((items) => items.map((item) => item.id === uploadTarget ? { ...item, file: file.name, status: "Uploaded", updated: "Just now" } : item));
    setSelected((item) => item?.id === uploadTarget ? { ...item, file: file.name, status: "Uploaded", updated: "Just now" } : item);
    toast.success("Document added to the package", { description: `${file.name} is ready for validation.` });
    event.target.value = "";
    setUploadTarget(null);
  };

  return (
    <main className="min-h-screen bg-[var(--color-background)] text-[var(--color-foreground)]">
      <input ref={fileInput} className="sr-only" type="file" accept=".pdf,.docx,.xlsx,.png,.jpg" onChange={applyUpload} />
      <header className="sticky top-0 z-30 border-b border-[var(--color-border)] bg-[oklch(from_var(--color-card)_l_c_h_/_0.94)] backdrop-blur-xl">
        <div className="mx-auto flex max-w-[1600px] flex-col gap-4 px-5 py-4 lg:flex-row lg:items-center lg:justify-between lg:px-8">
          <div className="flex min-w-0 items-center gap-3.5">
            <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-[var(--color-primary)] text-[var(--color-primary-foreground)] shadow-sm"><FileCheck2 className="size-5" /></span>
            <div className="min-w-0">
              <div className="flex items-center gap-2 text-xs font-medium text-[var(--color-muted-foreground)]"><span>PPAP submissions</span><ChevronRight className="size-3" /><span className="truncate">PPAP-2026-0048</span></div>
              <div className="mt-0.5 flex flex-wrap items-center gap-x-3 gap-y-1"><h1 className="font-display text-xl font-semibold tracking-tight sm:text-2xl">Steering Column Assembly</h1><span className="rounded-md bg-[var(--color-muted)] px-2 py-0.5 font-mono text-[11px] font-semibold">REV B</span></div>
              <p className="mt-0.5 text-sm text-[var(--color-muted-foreground)]">A102-SC · Apex Automotive Components · Submission level 3</p>
            </div>
          </div>
          <div className="flex items-center gap-2 self-end lg:self-auto">
            <Button variant="outline" onClick={() => toast.success("Draft saved", { description: "All workspace changes are up to date." })}>Save draft</Button>
            <Button onClick={() => setReadinessOpen(true)}><Send />Submit for review</Button>
          </div>
        </div>
      </header>

      <div className="mx-auto max-w-[1600px] px-5 py-6 lg:px-8">
        <section className="mb-5 overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
          <div className="grid lg:grid-cols-[1.4fr_1fr]">
            <div className="p-5 sm:p-6">
              <div className="flex items-start justify-between gap-4">
                <div><p className="text-xs font-semibold uppercase tracking-[0.12em] text-[var(--color-muted-foreground)]">Package readiness</p><div className="mt-1 flex items-end gap-2"><strong className="font-display text-4xl font-semibold tabular-nums">{percent}%</strong><span className="pb-1 text-sm text-[var(--color-muted-foreground)]">{ready} of {elements.length} approved</span></div></div>
                <span className="inline-flex items-center gap-1.5 rounded-full bg-[oklch(from_var(--color-warning)_l_c_h_/_0.12)] px-3 py-1.5 text-xs font-semibold text-[var(--color-warning)]"><CircleDashed className="size-3.5" />In progress</span>
              </div>
              <div className="mt-5 h-2 overflow-hidden rounded-full bg-[var(--color-muted)]"><div className="h-full rounded-full bg-[var(--color-primary)] transition-all" style={{ width: `${percent}%` }} /></div>
              <ol className="mt-6 grid grid-cols-4 gap-2" aria-label="Submission progress">
                {["Part details", "Submission level", "Upload elements", "Final review"].map((step, index) => <li key={step} className="flex min-w-0 items-center gap-2"><span className={cn("grid size-7 shrink-0 place-items-center rounded-full text-xs font-bold", index < 2 ? "bg-[var(--color-success)] text-white" : index === 2 ? "bg-[var(--color-primary)] text-white ring-4 ring-[var(--color-primary-soft)]" : "bg-[var(--color-muted)] text-[var(--color-muted-foreground)]")}>{index < 2 ? <CheckCircle2 className="size-3.5" /> : index + 1}</span><span className={cn("hidden truncate text-xs font-semibold sm:block", index === 2 ? "text-[var(--color-primary)]" : "text-[var(--color-muted-foreground)]")}>{step}</span></li>)}
              </ol>
            </div>
            <div className="grid grid-cols-2 border-t border-[var(--color-border)] lg:border-l lg:border-t-0">
              {(["Approved", "Under review", "Uploaded", "Missing"] as ElementStatus[]).map((status) => {
                const meta = STATUS_STYLE[status];
                return <button key={status} type="button" onClick={() => setFilter(status === "Uploaded" || status === "Missing" ? "Needs action" : status)} className="group border-b border-r border-[var(--color-border)] p-4 text-left transition-colors last:border-b-0 hover:bg-[var(--color-muted)] lg:[&:nth-child(n+3)]:border-b-0"><span className={cn("inline-flex size-8 items-center justify-center rounded-lg", meta.className)}><meta.icon className="size-4" /></span><strong className="mt-2 block font-display text-2xl tabular-nums">{counts[status]}</strong><span className="text-xs font-medium text-[var(--color-muted-foreground)]">{status}</span></button>;
              })}
            </div>
          </div>
        </section>

        <section className="mb-5 rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-4 shadow-xs sm:p-5" aria-labelledby="quick-document-navigation">
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
            <div><h2 id="quick-document-navigation" className="text-sm font-semibold">Quick document navigator</h2><p className="mt-0.5 text-xs text-[var(--color-muted-foreground)]">Jump directly to any element, then use Previous and Next without returning to this screen.</p></div>
            <span className="hidden items-center gap-2 text-[11px] font-medium text-[var(--color-muted-foreground)] sm:flex"><span className="size-1.5 rounded-full bg-[var(--color-success)]" />Approved<span className="ml-2 size-1.5 rounded-full bg-[var(--color-primary)]" />In review<span className="ml-2 size-1.5 rounded-full bg-[var(--color-destructive)]" />Action needed</span>
          </div>
          <div className="mt-4 flex gap-1.5 overflow-x-auto pb-2" aria-label="PPAP document sequence">
            {elements.map((item) => {
              const meta = STATUS_STYLE[item.status];
              return <button key={item.id} type="button" title={`${item.id}. ${item.name} — ${item.status}`} onClick={() => setSelected(item)} className={cn("group relative flex h-12 min-w-16 flex-col items-center justify-center rounded-lg border px-2 transition hover:-translate-y-0.5 hover:shadow-sm", selected?.id === item.id ? "border-[var(--color-primary)] bg-[var(--color-primary)] text-white ring-3 ring-[var(--color-primary-soft)]" : "border-[var(--color-border)] bg-[var(--color-background)] hover:border-[var(--color-primary)]")}><span className="font-mono text-[10px] font-bold">{String(item.id).padStart(2, "0")}</span><span className="mt-0.5 text-[10px] font-semibold">{item.code}</span><span className={cn("absolute -bottom-1 size-2.5 rounded-full border-2 border-[var(--color-card)]", meta.dot)} /></button>;
            })}
          </div>
        </section>

        <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_330px]">
          <section className="min-w-0 overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
            <div className="border-b border-[var(--color-border)] p-4 sm:p-5">
              <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                <div><h2 className="font-display text-lg font-semibold">Submission elements</h2><p className="mt-0.5 text-sm text-[var(--color-muted-foreground)]">Complete the required evidence and track customer approval.</p></div>
                <div className="flex items-center gap-2"><Button variant="outline" size="sm" onClick={() => toast.info("Package export prepared", { description: "The ZIP will include the latest document versions." })}><Download />Export ZIP</Button><span className="inline-flex rounded-lg border border-[var(--color-border)] p-1"><button type="button" aria-label="List view" onClick={() => setView("list")} className={cn("grid size-7 place-items-center rounded-md", view === "list" ? "bg-[var(--color-primary)] text-white" : "text-[var(--color-muted-foreground)]")}><List className="size-4" /></button><button type="button" aria-label="Grid view" onClick={() => setView("grid")} className={cn("grid size-7 place-items-center rounded-md", view === "grid" ? "bg-[var(--color-primary)] text-white" : "text-[var(--color-muted-foreground)]")}><Grid2X2 className="size-4" /></button></span></div>
              </div>
              <div className="mt-4 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
                <div className="relative max-w-lg flex-1"><Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--color-muted-foreground)]" /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search element, code, or file…" className="h-10 w-full rounded-lg border border-[var(--color-input)] bg-[var(--color-background)] pl-9 pr-3 text-sm outline-none transition focus:border-[var(--color-primary)] focus:ring-3 focus:ring-[var(--color-primary-soft)]" /></div>
                <div className="flex gap-1 overflow-x-auto rounded-lg bg-[var(--color-muted)] p-1">{FILTERS.map((item) => <button key={item} type="button" onClick={() => setFilter(item)} className={cn("whitespace-nowrap rounded-md px-3 py-1.5 text-xs font-semibold transition", filter === item ? "bg-[var(--color-card)] text-[var(--color-foreground)] shadow-xs" : "text-[var(--color-muted-foreground)] hover:text-[var(--color-foreground)]")}>{item}{item === "Needs action" && <span className="ml-1.5 rounded-full bg-[var(--color-destructive)] px-1.5 py-0.5 text-[10px] text-white">{counts.Missing + counts.Uploaded}</span>}</button>)}</div>
              </div>
            </div>

            {visible.length === 0 ? <div className="grid min-h-64 place-items-center p-8 text-center"><div><Search className="mx-auto size-8 text-[var(--color-muted-foreground)]" /><h3 className="mt-3 font-semibold">No elements found</h3><p className="mt-1 text-sm text-[var(--color-muted-foreground)]">Try a different search or status filter.</p></div></div> : view === "list" ? (
              <div className="overflow-x-auto"><table className="w-full min-w-[760px] border-collapse text-left"><thead><tr className="bg-[oklch(from_var(--color-muted)_l_c_h_/_0.45)] text-[11px] font-semibold uppercase tracking-[0.1em] text-[var(--color-muted-foreground)]"><th className="px-5 py-3">Element</th><th className="px-4 py-3">Status</th><th className="px-4 py-3">File / activity</th><th className="px-4 py-3">Owner</th><th className="px-5 py-3 text-right">Action</th></tr></thead><tbody>{visible.map((item) => <ElementRow key={item.id} item={item} onOpen={() => setSelected(item)} onUpload={() => chooseUpload(item.id)} />)}</tbody></table></div>
            ) : (
              <div className="grid gap-3 p-4 sm:grid-cols-2 2xl:grid-cols-3">{visible.map((item) => <ElementCard key={item.id} item={item} onOpen={() => setSelected(item)} onUpload={() => chooseUpload(item.id)} />)}</div>
            )}
            <div className="flex items-center justify-between border-t border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] px-5 py-3 text-xs text-[var(--color-muted-foreground)]"><span>Showing {visible.length} of {elements.length} elements</span><span className="inline-flex items-center gap-1.5"><span className="size-1.5 rounded-full bg-[var(--color-success)]" />Autosaved just now</span></div>
          </section>

          <aside className="space-y-4">
            <section className="rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-5 shadow-xs"><div className="flex items-center gap-2"><span className="grid size-8 place-items-center rounded-lg bg-[var(--color-primary-soft)] text-[var(--color-primary)]"><ShieldCheck className="size-4" /></span><div><p className="text-[11px] font-semibold uppercase tracking-[0.1em] text-[var(--color-muted-foreground)]">OEM quality engineer</p><p className="text-sm font-semibold">Marcus Chen</p></div></div><div className="mt-4 flex items-center justify-between rounded-xl bg-[var(--color-muted)] p-3"><span className="flex items-center gap-2 text-xs font-medium"><span className="size-2 rounded-full bg-[var(--color-success)]" />Available now</span><Button variant="ghost" size="xs" onClick={() => toast.info("Conversation opened")}><MessageSquareText />Message</Button></div></section>
            <section className="rounded-2xl border border-[var(--color-primary)] bg-[var(--color-primary-soft)] p-5"><div className="flex items-center gap-2 text-[var(--color-primary)]"><Sparkles className="size-4" /><h3 className="text-sm font-semibold">Readiness insight</h3></div><p className="mt-3 text-sm leading-6">Upload the Process Flow Diagram before PFMEA. This preserves traceability across your process documents.</p><div className="mt-4 rounded-lg bg-[var(--color-card)] p-3 text-xs"><strong>Recommended next step</strong><button type="button" onClick={() => chooseUpload(5)} className="mt-2 flex w-full items-center justify-between font-semibold text-[var(--color-primary)]"><span>Upload Process Flow Diagram</span><ChevronRight className="size-4" /></button></div></section>
            <section className="rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-5 shadow-xs"><div className="flex items-center justify-between"><h3 className="text-sm font-semibold">Recent activity</h3><button type="button" className="text-xs font-semibold text-[var(--color-primary)]">View all</button></div><div className="mt-4 space-y-4"><Activity icon={CheckCircle2} title="Design records approved" detail="Marcus Chen · 2h ago" tone="success" /><Activity icon={CloudUpload} title="Dimensional results uploaded" detail="You · 4h ago" tone="primary" /><Activity icon={MessageSquareText} title="Comment on Design FMEA" detail="Marcus Chen · Yesterday" tone="muted" /></div></section>
            <section className="rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-5 shadow-xs"><div className="flex items-center gap-2"><Info className="size-4 text-[var(--color-primary)]" /><h3 className="text-sm font-semibold">Level 3 requirement</h3></div><p className="mt-2 text-xs leading-5 text-[var(--color-muted-foreground)]">14 required elements must be approved before final customer review. Optional records improve package traceability.</p></section>
          </aside>
        </div>
      </div>

      {selected && <>
        <DocumentWorkspace item={selected} elements={elements} selectedIndex={selectedIndex} onSelect={setSelected} onClose={() => setSelected(null)} onUpload={() => chooseUpload(selected.id)} />
        <nav aria-label="Document detail navigation" className="fixed bottom-0 right-0 z-[60] flex w-full max-w-md items-center gap-2 border-t border-[var(--color-border)] bg-[var(--color-card)] px-4 py-3 shadow-[0_-8px_24px_rgb(13_57_115_/_0.10)]">
          <Button variant="outline" className="flex-1" disabled={selectedIndex <= 0} onClick={() => navigateDocument(-1)}><ChevronLeft />Previous</Button>
          <div className="min-w-16 text-center"><strong className="block text-sm tabular-nums">{selectedIndex + 1} / {elements.length}</strong><span className="text-[10px] text-[var(--color-muted-foreground)]">{selected.code}</span></div>
          <Button className="flex-1" disabled={selectedIndex >= elements.length - 1} onClick={() => navigateDocument(1)}>Next<ChevronRight /></Button>
        </nav>
      </>}
      <Dialog open={readinessOpen} onOpenChange={setReadinessOpen}><DialogContent className="sm:max-w-lg"><DialogHeader><DialogTitle>Submission readiness</DialogTitle><DialogDescription>Your package is saved, but it is not ready for customer review.</DialogDescription></DialogHeader><div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-muted)] p-4"><div className="flex items-center justify-between"><span className="text-sm font-medium">Approved elements</span><strong className="tabular-nums">{ready}/{elements.length}</strong></div><div className="mt-3 h-2 overflow-hidden rounded-full bg-[var(--color-card)]"><div className="h-full bg-[var(--color-primary)]" style={{ width: `${percent}%` }} /></div><ul className="mt-4 space-y-2 text-sm"><li className="flex items-center gap-2"><CircleAlert className="size-4 text-[var(--color-destructive)]" />{counts.Missing} missing elements require a decision</li><li className="flex items-center gap-2"><Clock3 className="size-4 text-[var(--color-primary)]" />{counts["Under review"] + counts.Uploaded} elements are awaiting approval</li></ul></div><DialogFooter><Button variant="outline" onClick={() => setReadinessOpen(false)}>Continue working</Button><Button disabled>Submit package</Button></DialogFooter></DialogContent></Dialog>
    </main>
  );
}

function StatusPill({ status }: { status: ElementStatus }) {
  const meta = STATUS_STYLE[status];
  return <span className={cn("inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-semibold", meta.className)}><meta.icon className="size-3.5" />{status}</span>;
}

function ElementRow({ item, onOpen, onUpload }: { item: SubmissionElement; onOpen: () => void; onUpload: () => void }) {
  return <tr className="group border-t border-[var(--color-border)] transition-colors hover:bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)]"><td className="px-5 py-4"><button type="button" onClick={onOpen} className="flex items-start gap-3 text-left"><span className="grid size-9 shrink-0 place-items-center rounded-lg bg-[var(--color-muted)] font-mono text-[11px] font-bold text-[var(--color-primary)]">{item.code}</span><span><strong className="block text-sm font-semibold group-hover:text-[var(--color-primary)]">{item.name}</strong><span className="mt-0.5 block max-w-xs text-xs text-[var(--color-muted-foreground)]">#{String(item.id).padStart(2, "0")} · {item.description}</span></span></button></td><td className="px-4 py-4"><StatusPill status={item.status} /></td><td className="px-4 py-4"><span className="block max-w-52 truncate text-xs font-medium">{item.file ?? "No file added"}</span><span className="mt-0.5 block text-[11px] text-[var(--color-muted-foreground)]">{item.updated ?? (item.required ? "Required" : "Optional")}</span></td><td className="px-4 py-4">{item.reviewer ? <span className="inline-flex items-center gap-2 text-xs"><span className="grid size-7 place-items-center rounded-full bg-[var(--color-secondary)] text-[10px] font-bold text-[var(--color-secondary-foreground)]">{initials(item.reviewer)}</span>{item.reviewer}</span> : <span className="text-xs text-[var(--color-muted-foreground)]">Unassigned</span>}</td><td className="px-5 py-4 text-right">{item.status === "Missing" ? <Button size="sm" onClick={onUpload}><Upload />Upload</Button> : <Button variant="ghost" size="icon-sm" aria-label={`View ${item.name}`} onClick={onOpen}>{item.status === "Uploaded" ? <MoreHorizontal /> : <Eye />}</Button>}</td></tr>;
}

function ElementCard({ item, onOpen, onUpload }: { item: SubmissionElement; onOpen: () => void; onUpload: () => void }) {
  return <article className={cn("rounded-xl border p-4 transition hover:-translate-y-0.5 hover:shadow-sm", item.status === "Missing" ? "border-dashed border-[oklch(from_var(--color-destructive)_l_c_h_/_0.45)]" : "border-[var(--color-border)]")}><div className="flex items-start justify-between gap-3"><span className="font-mono text-xs font-bold text-[var(--color-primary)]">#{String(item.id).padStart(2, "0")} · {item.code}</span><StatusPill status={item.status} /></div><h3 className="mt-4 text-sm font-semibold">{item.name}</h3><p className="mt-1 line-clamp-2 min-h-9 text-xs leading-5 text-[var(--color-muted-foreground)]">{item.description}</p><div className="mt-4 border-t border-[var(--color-border)] pt-3"><p className="truncate text-xs font-medium">{item.file ?? "Evidence not uploaded"}</p><div className="mt-3 flex items-center justify-between"><span className="text-[11px] text-[var(--color-muted-foreground)]">{item.updated ?? (item.required ? "Required" : "Optional")}</span>{item.status === "Missing" ? <Button size="xs" onClick={onUpload}><Upload />Upload</Button> : <Button variant="ghost" size="xs" onClick={onOpen}>View details<ChevronRight /></Button>}</div></div></article>;
}

function Activity({ icon: Icon, title, detail, tone }: { icon: ComponentType<{ className?: string }>; title: string; detail: string; tone: "success" | "primary" | "muted" }) {
  return <div className="flex gap-3"><span className={cn("mt-0.5 grid size-7 shrink-0 place-items-center rounded-full", tone === "success" ? "bg-[oklch(from_var(--color-success)_l_c_h_/_0.11)] text-[var(--color-success)]" : tone === "primary" ? "bg-[var(--color-primary-soft)] text-[var(--color-primary)]" : "bg-[var(--color-muted)] text-[var(--color-muted-foreground)]")}><Icon className="size-3.5" /></span><div><p className="text-xs font-semibold">{title}</p><p className="mt-0.5 text-[11px] text-[var(--color-muted-foreground)]">{detail}</p></div></div>;
}

const SAMPLE_FINDINGS = [
  { id: 1, severity: "Error", rule: "SR-29-DWG", cell: "E14", title: "Nominal dimension does not match the drawing", detail: "Measured nominal 4.6; drawing requires 178.0 for bubble 1." },
  { id: 2, severity: "Error", rule: "SR-29-DWG", cell: "E15", title: "Nominal dimension does not match the drawing", detail: "Measured nominal 4.6; drawing requires 178.0 for bubble 2." },
  { id: 3, severity: "Warning", rule: "DIMR-12", cell: "H20", title: "Tolerance range needs confirmation", detail: "Lower tolerance is outside the customer-specific requirement." },
  { id: 4, severity: "Error", rule: "HDR-04", cell: "B6", title: "Supplier/vendor code is missing", detail: "A supplier code is required in the dimensional report header." },
] as const;

const EXTRACTED_ROWS = [
  ["1", "1", "", "4.6", "4.6", "4.9", "4.5", "0.3", "-0.1"],
  ["1", "1", "", "4.6", "4.6", "4.9", "4.5", "0.3", "-0.1"],
  ["1", "5", "", "6", "6", "6.2", "5.8", "0.2", "-0.2"],
  ["1", "6", "", "16.46", "16.46", "16.66", "16.26", "0.2", "-0.2"],
  ["1", "7", "", "14.56", "14.56", "14.56", "14.31", "0", "-0.25"],
  ["1", "8", "", "12", "12", "12.2", "11.8", "0.2", "-0.2"],
];

function DocumentWorkspace({ item, elements, selectedIndex, onSelect, onClose, onUpload }: {
  item: SubmissionElement;
  elements: SubmissionElement[];
  selectedIndex: number;
  onSelect: (item: SubmissionElement) => void;
  onClose: () => void;
  onUpload: () => void;
}) {
  const [view, setView] = useState<"split" | "document" | "data">("split");
  const [findingFilter, setFindingFilter] = useState<"All" | "Error" | "Warning">("All");
  const [activeFinding, setActiveFinding] = useState<number | null>(1);
  const [waived, setWaived] = useState<Set<number>>(new Set());
  const [zoom, setZoom] = useState(85);
  const findings = item.file ? SAMPLE_FINDINGS : [];
  const visibleFindings = findings.filter((finding) => findingFilter === "All" || finding.severity === findingFilter);
  const toggleWaive = (id: number) => setWaived((current) => {
    const next = new Set(current);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  return <>
    <div className="fixed inset-0 z-50 hidden flex-col bg-[var(--color-background)] md:flex" role="dialog" aria-modal="true" aria-label={`${item.name} document workspace`}>
      <header className="shrink-0 border-b border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
        <div className="flex items-center justify-between gap-4 px-5 py-3">
          <div className="flex min-w-0 items-center gap-3">
            <Button variant="ghost" size="icon-sm" aria-label="Back to submission overview" onClick={onClose}><ChevronLeft /></Button>
            <span className="grid size-9 shrink-0 place-items-center rounded-lg bg-[var(--color-primary-soft)] font-mono text-[11px] font-bold text-[var(--color-primary)]">{item.code}</span>
            <div className="min-w-0"><div className="flex items-center gap-2"><h2 className="truncate font-display text-lg font-semibold">{item.name}</h2><StatusPill status={item.status} /></div><p className="truncate text-xs text-[var(--color-muted-foreground)]">Element {String(item.id).padStart(2, "0")} of {elements.length} · {item.file ?? "No document uploaded"}</p></div>
          </div>
          <div className="flex items-center gap-2">
            {item.file && <><Button variant="outline" size="sm" onClick={() => toast.info("Document download started")}><Download />Download</Button><Button variant="outline" size="sm" onClick={() => toast.success("Validation restarted", { description: "Results will refresh when processing completes." })}><RefreshCw />Re-validate</Button></>}
            <Button size="sm" onClick={onUpload}><Upload />{item.file ? "New version" : "Upload document"}</Button>
            <Button variant="ghost" size="icon-sm" aria-label="Close workspace" onClick={onClose}><X /></Button>
          </div>
        </div>
        <div className="flex items-center gap-1.5 overflow-x-auto border-t border-[var(--color-border)] px-5 py-2" aria-label="Document workspace navigator">
          {elements.map((element) => {
            const meta = STATUS_STYLE[element.status];
            return <button key={element.id} type="button" title={element.name} onClick={() => onSelect(element)} className={cn("relative flex h-9 min-w-14 items-center justify-center rounded-md border px-2 font-mono text-[10px] font-bold transition", element.id === item.id ? "border-[var(--color-primary)] bg-[var(--color-primary)] text-white" : "border-[var(--color-border)] bg-[var(--color-background)] hover:border-[var(--color-primary)]")}><span className={cn("mr-1.5 size-1.5 rounded-full", meta.dot)} />{element.code}</button>;
          })}
        </div>
      </header>

      {!item.file ? <div className="grid flex-1 place-items-center p-8 pb-24"><section className="w-full max-w-2xl rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-8 text-center shadow-sm"><span className="mx-auto grid size-14 place-items-center rounded-2xl bg-[var(--color-primary-soft)] text-[var(--color-primary)]"><CloudUpload className="size-7" /></span><h3 className="mt-4 font-display text-xl font-semibold">Upload {item.name}</h3><p className="mx-auto mt-2 max-w-md text-sm leading-6 text-[var(--color-muted-foreground)]">Drop the controlled document here. It will be converted, extracted, and validated automatically.</p><button type="button" onClick={onUpload} className="mt-6 w-full rounded-xl border-2 border-dashed border-[var(--color-primary)] bg-[var(--color-primary-soft)] px-6 py-12 text-sm font-semibold text-[var(--color-primary)] transition hover:brightness-95"><Upload className="mx-auto mb-3 size-7" />Choose file or drag and drop</button><p className="mt-3 text-xs text-[var(--color-muted-foreground)]">PDF, DOCX, or XLSX · Maximum 25 MB</p><div className="mt-6 border-t border-[var(--color-border)] pt-5"><Button variant="outline" onClick={() => toast.info("Additional document picker opened")}><Paperclip />Add supporting document</Button></div></section></div> : (
        <div className="grid min-h-0 flex-1 gap-4 overflow-hidden p-4 pb-20 xl:grid-cols-[minmax(0,1fr)_380px]">
          <div className="min-h-0 overflow-y-auto rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
            <div className="sticky top-0 z-10 flex items-center justify-between gap-3 border-b border-[var(--color-border)] bg-[var(--color-card)] px-4 py-3">
              <div className="flex rounded-lg bg-[var(--color-muted)] p-1">
                {(["split", "document", "data"] as const).map((mode) => <button key={mode} type="button" onClick={() => setView(mode)} className={cn("rounded-md px-3 py-1.5 text-xs font-semibold capitalize", view === mode ? "bg-[var(--color-card)] shadow-xs" : "text-[var(--color-muted-foreground)]")}>{mode === "split" ? "Document + data" : mode === "document" ? "Document only" : "Extracted data"}</button>)}
              </div>
              <div className="flex items-center gap-1"><Button variant="ghost" size="icon-xs" aria-label="Zoom out" onClick={() => setZoom((value) => Math.max(50, value - 10))}><ZoomOut /></Button><span className="min-w-10 text-center text-[11px] font-semibold tabular-nums">{zoom}%</span><Button variant="ghost" size="icon-xs" aria-label="Zoom in" onClick={() => setZoom((value) => Math.min(150, value + 10))}><ZoomIn /></Button><Button variant="ghost" size="icon-xs" aria-label="Full screen preview" onClick={() => toast.info("Full-screen preview opened")}><Maximize2 /></Button></div>
            </div>

            {(view === "split" || view === "document") && <section className="border-b border-[var(--color-border)]" aria-labelledby="source-document-title"><div className="flex items-center justify-between px-4 py-3"><div className="flex items-center gap-2"><FileText className="size-4 text-[var(--color-primary)]" /><h3 id="source-document-title" className="text-sm font-semibold">Source document</h3><span className="rounded-full bg-[var(--color-muted)] px-2 py-0.5 text-[10px] text-[var(--color-muted-foreground)]">Page 1 of 5</span></div><button type="button" className="text-xs font-semibold text-[var(--color-primary)]">Open original</button></div><div className={cn("overflow-auto bg-[#252a32] p-5", view === "document" ? "min-h-[calc(100vh-14rem)]" : "h-72")}><div className="mx-auto aspect-[1.414/1] min-w-[520px] max-w-3xl bg-white p-6 shadow-xl" style={{ width: `${zoom}%` }}><div className="flex h-full flex-col border-2 border-slate-700 p-3 text-slate-800"><div className="flex items-center justify-between border-b border-slate-400 pb-2"><strong className="text-sm">DIMENSIONAL INSPECTION REPORT</strong><span className="text-[9px]">Form QF-018 · Rev B</span></div><div className="mt-2 grid grid-cols-3 gap-2 text-[8px]"><span>Part: Steering Column Assembly</span><span>Part no: A102-SC</span><span>Supplier: Apex Automotive</span></div><svg className="mt-3 min-h-0 flex-1" viewBox="0 0 600 230" role="img" aria-label="Engineering drawing preview"><rect x="140" y="35" width="320" height="160" fill="none" stroke="#334155" strokeWidth="2" /><circle cx="210" cy="115" r="42" fill="none" stroke="#334155" strokeWidth="2" /><circle cx="390" cy="115" r="42" fill="none" stroke="#334155" strokeWidth="2" /><path d="M252 90 H348 M252 140 H348 M110 20 V210 M490 20 V210" fill="none" stroke="#64748b" /><path d="M140 15 H460 M140 10 V20 M460 10 V20" fill="none" stroke="#ef4444" /><text x="280" y="12" fontSize="10" fill="#ef4444">178.0 ±0.3</text><g fill="#2375e5"><circle cx="140" cy="35" r="9" /><circle cx="460" cy="195" r="9" /></g><g fill="white" fontSize="8" textAnchor="middle"><text x="140" y="38">1</text><text x="460" y="198">2</text></g></svg><div className="grid grid-cols-4 border-t border-slate-500 pt-2 text-[7px]"><span>DRAWN: AM</span><span>CHECKED: MC</span><span>DATE: 2026-10-18</span><span>REVISION: B</span></div></div></div></div></section>}

            {(view === "split" || view === "data") && <section aria-labelledby="extracted-data-title"><div className="flex items-center justify-between px-4 py-3"><div className="flex items-center gap-2"><FileSpreadsheet className="size-4 text-[var(--color-success)]" /><h3 id="extracted-data-title" className="text-sm font-semibold">Extracted spreadsheet data</h3><span className="rounded-full bg-[oklch(from_var(--color-success)_l_c_h_/_0.11)] px-2 py-0.5 text-[10px] font-semibold text-[var(--color-success)]">98.4% confidence</span></div><div className="flex items-center gap-2"><Button variant="ghost" size="xs"><SlidersHorizontal />Columns</Button><Button variant="outline" size="xs"><Download />Export</Button></div></div><div className={cn("overflow-auto border-t border-[var(--color-border)]", view === "data" ? "max-h-[calc(100vh-14rem)]" : "max-h-72")}><table className="w-full min-w-[850px] border-collapse text-left text-[11px]"><thead className="sticky top-0 bg-[var(--color-muted)]"><tr>{["Cavity", "Bubble", "Description", "Nominal description", "Nominal", "Upper limit", "Lower limit", "Tolerance +", "Tolerance −"].map((header, index) => <th key={header} className="border-b border-r border-[var(--color-border)] px-3 py-2"><span className="block font-mono text-[9px] font-normal text-[var(--color-muted-foreground)]">{String.fromCharCode(65 + index)}</span>{header}</th>)}</tr></thead><tbody>{EXTRACTED_ROWS.map((row, rowIndex) => <tr key={rowIndex} className="hover:bg-[var(--color-muted)]">{row.map((cell, columnIndex) => { const highlighted = activeFinding !== null && ((activeFinding <= 2 && rowIndex <= 1 && columnIndex === 4) || (activeFinding === 3 && rowIndex === 2 && columnIndex === 6) || (activeFinding === 4 && rowIndex === 3 && columnIndex === 1)); return <td key={columnIndex} className={cn("border-b border-r border-[var(--color-border)] px-3 py-2 font-mono", highlighted && "bg-[oklch(from_var(--color-destructive)_l_c_h_/_0.16)] text-[var(--color-destructive)] ring-1 ring-inset ring-[var(--color-destructive)]")}>{cell || "—"}</td>; })}</tr>)}</tbody></table></div></section>}
            <section className="border-t border-[var(--color-border)] px-4 py-3"><div className="flex items-center justify-between"><div className="flex items-center gap-2"><Paperclip className="size-4 text-[var(--color-muted-foreground)]" /><div><h3 className="text-xs font-semibold">Supporting documents</h3><p className="text-[11px] text-[var(--color-muted-foreground)]">2 files attached to this element</p></div></div><Button variant="outline" size="xs" onClick={() => toast.info("Supporting document picker opened")}><Upload />Add file</Button></div></section>
          </div>

          <aside className="min-h-0 overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
            <div className="border-b border-[var(--color-border)] p-4"><div className="flex items-center justify-between"><div><div className="flex items-center gap-2"><CircleAlert className="size-4 text-[var(--color-destructive)]" /><h3 className="text-sm font-semibold">Validation findings</h3><span className="rounded-full bg-[oklch(from_var(--color-destructive)_l_c_h_/_0.11)] px-2 py-0.5 text-[10px] font-bold text-[var(--color-destructive)]">14</span></div><p className="mt-1 text-xs text-[var(--color-muted-foreground)]">Select a finding to highlight its source cell.</p></div><Button variant="ghost" size="icon-xs" aria-label="Finding options"><MoreHorizontal /></Button></div><div className="mt-4 grid grid-cols-3 gap-2">{[["Errors", "11", "danger"], ["Warnings", "3", "warning"], ["Waived", String(waived.size), "primary"]].map(([label, value, tone]) => <div key={label} className="rounded-lg bg-[var(--color-muted)] p-2 text-center"><strong className={cn("block text-lg tabular-nums", tone === "danger" ? "text-[var(--color-destructive)]" : tone === "warning" ? "text-[var(--color-warning)]" : "text-[var(--color-primary)]")}>{value}</strong><span className="text-[10px] text-[var(--color-muted-foreground)]">{label}</span></div>)}</div><div className="mt-3 flex gap-1 rounded-lg bg-[var(--color-muted)] p-1">{(["All", "Error", "Warning"] as const).map((option) => <button key={option} type="button" onClick={() => setFindingFilter(option)} className={cn("flex-1 rounded-md px-2 py-1.5 text-[11px] font-semibold", findingFilter === option ? "bg-[var(--color-card)] shadow-xs" : "text-[var(--color-muted-foreground)]")}>{option}</button>)}</div></div>
            <div className="h-[calc(100%-13.5rem)] space-y-2 overflow-y-auto p-3 pb-20">{visibleFindings.map((finding) => <article key={finding.id} className={cn("rounded-xl border p-3 transition", activeFinding === finding.id ? "border-[var(--color-primary)] bg-[var(--color-primary-soft)]" : "border-[var(--color-border)] hover:border-[var(--color-primary)]")}><div className="flex items-start justify-between gap-2"><div className="flex items-center gap-2"><span className={cn("grid size-6 place-items-center rounded-md text-[10px] font-bold", finding.severity === "Error" ? "bg-[oklch(from_var(--color-destructive)_l_c_h_/_0.12)] text-[var(--color-destructive)]" : "bg-[oklch(from_var(--color-warning)_l_c_h_/_0.14)] text-[var(--color-warning)]")}>{finding.id}</span><span className="font-mono text-[10px] font-bold text-[var(--color-muted-foreground)]">{finding.rule}</span></div><button type="button" onClick={(event) => { event.stopPropagation(); setActiveFinding(finding.id); setView("data"); }} className="rounded-md bg-[var(--color-card)] px-2 py-1 font-mono text-[10px] font-bold text-[var(--color-primary)] shadow-xs">{finding.cell}</button></div><h4 className="mt-2 text-xs font-semibold leading-5">{finding.title}</h4><p className="mt-1 text-[11px] leading-4 text-[var(--color-muted-foreground)]">{finding.detail}</p><label className="mt-3 flex cursor-pointer items-center gap-2 border-t border-[var(--color-border)] pt-2 text-[11px] font-semibold"><input type="checkbox" checked={waived.has(finding.id)} onChange={() => toggleWaive(finding.id)} onClick={(event) => event.stopPropagation()} className="size-3.5 accent-[var(--color-primary)]" />Request waiver{waived.has(finding.id) && <span className="ml-auto rounded-full bg-[oklch(from_var(--color-warning)_l_c_h_/_0.14)] px-2 py-0.5 text-[9px] text-[var(--color-warning)]">Pending</span>}</label></article>)}</div>
          </aside>
        </div>
      )}
      <div className="pointer-events-none fixed bottom-3 left-5 z-[61] hidden items-center gap-2 text-[11px] text-[var(--color-muted-foreground)] xl:flex"><span className="rounded-md border border-[var(--color-border)] bg-[var(--color-card)] px-2 py-1 shadow-xs">Element {selectedIndex + 1} of {elements.length}</span><span>Document, extracted data, and findings stay in one workspace</span></div>
    </div>
    <div className="md:hidden"><ElementDetail item={item} onClose={onClose} onUpload={onUpload} /></div>
  </>;
}

function ElementDetail({ item, onClose, onUpload }: { item: SubmissionElement; onClose: () => void; onUpload: () => void }) {
  return <div className="fixed inset-0 z-50 flex justify-end bg-[var(--color-overlay)]" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onClose()}><aside role="dialog" aria-modal="true" aria-label={`${item.name} details`} className="h-full w-full max-w-md overflow-y-auto bg-[var(--color-card)] pb-20 shadow-2xl"><div className="flex items-center justify-between border-b border-[var(--color-border)] px-5 py-4"><div><p className="font-mono text-xs font-bold text-[var(--color-primary)]">ELEMENT {String(item.id).padStart(2, "0")} · {item.code}</p><h2 className="mt-1 font-display text-xl font-semibold">{item.name}</h2></div><Button variant="ghost" size="icon" aria-label="Close details" onClick={onClose}><X /></Button></div><div className="space-y-5 p-5"><StatusPill status={item.status} /><p className="text-sm leading-6 text-[var(--color-muted-foreground)]">{item.description}</p><section className="rounded-xl border border-[var(--color-border)] p-4"><p className="text-[11px] font-semibold uppercase tracking-[0.1em] text-[var(--color-muted-foreground)]">Current evidence</p>{item.file ? <div className="mt-3 flex items-center gap-3"><span className="grid size-10 place-items-center rounded-lg bg-[var(--color-primary-soft)] text-[var(--color-primary)]"><FileText className="size-5" /></span><div className="min-w-0"><p className="truncate text-sm font-semibold">{item.file}</p><p className="text-xs text-[var(--color-muted-foreground)]">{item.updated} · Version 1</p></div></div> : <div className="mt-4 rounded-lg border border-dashed border-[var(--color-border-strong)] p-5 text-center"><CloudUpload className="mx-auto size-7 text-[var(--color-muted-foreground)]" /><p className="mt-2 text-sm font-semibold">No evidence uploaded</p></div>}<Button className="mt-4 w-full" variant={item.file ? "outline" : "default"} onClick={onUpload}><Upload />{item.file ? "Upload new version" : "Choose file"}</Button></section><section><h3 className="text-sm font-semibold">Requirement guidance</h3><ul className="mt-3 space-y-2 text-xs leading-5 text-[var(--color-muted-foreground)]"><li className="flex gap-2"><CheckCircle2 className="mt-0.5 size-3.5 shrink-0 text-[var(--color-success)]" />Use the latest controlled document revision.</li><li className="flex gap-2"><CheckCircle2 className="mt-0.5 size-3.5 shrink-0 text-[var(--color-success)]" />Include approvals and effective dates.</li><li className="flex gap-2"><CheckCircle2 className="mt-0.5 size-3.5 shrink-0 text-[var(--color-success)]" />Accepted formats: PDF, DOCX, XLSX.</li></ul></section>{item.reviewer && <section className="rounded-xl bg-[var(--color-muted)] p-4"><div className="flex items-center gap-2"><UserRound className="size-4 text-[var(--color-primary)]" /><p className="text-sm font-semibold">Assigned to {item.reviewer}</p></div><p className="mt-2 text-xs leading-5 text-[var(--color-muted-foreground)]">The reviewer will be notified when a new version is uploaded.</p></section>}</div></aside></div>;
}

function initials(name: string) {
  return name.split(" ").map((part) => part[0]).join("").slice(0, 2).toUpperCase();
}
