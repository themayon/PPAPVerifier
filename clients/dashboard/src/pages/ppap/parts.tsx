import { useState, type FormEvent } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ClipboardList, FilePlus2, Link2, Pencil, Plus, Search } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import {
  assignPpapPartTemplate,
  createDraftFromPpapPart,
  createPpapPart,
  getPublishedRuleTemplates,
  searchMyPpapParts,
  updatePpapPart,
  type CreatePartInput,
  type PpapPartDto,
} from "@/api/ppap";
import { Button } from "@/components/ui/button";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { describe } from "@/lib/list-helpers";
import "@/styles/ppap.css";

export function PpapPartsPage() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [dialog, setDialog] = useState<"create" | "edit" | "assign" | "draft" | null>(null);
  const [selected, setSelected] = useState<PpapPartDto | null>(null);
  const partsQuery = useQuery({
    queryKey: ["ppap", "parts", { search, pageNumber: page, pageSize: 10 }],
    queryFn: () => searchMyPpapParts({ search: search || undefined, pageNumber: page, pageSize: 10, sort: "-updatedAt" }),
    placeholderData: keepPreviousData,
  });

  const open = (kind: typeof dialog, part?: PpapPartDto) => {
    setSelected(part ?? null);
    setDialog(kind);
  };
  const changed = () => void queryClient.invalidateQueries({ queryKey: ["ppap", "parts"] });

  return (
    <div className="ppap-page">
      <header className="ppap-heading">
        <div className="ppap-heading__mark" aria-hidden="true"><ClipboardList /></div>
        <div><p className="ppap-eyebrow">Supplier workspace</p><h1>PPAP Parts</h1><p>Keep reusable part details and published checklist snapshots ready for new submissions.</p></div>
        <Button className="ppap-primary-action" onClick={() => open("create")}><Plus />Add part</Button>
      </header>

      <section className="rounded-xl border border-[var(--ppap-border)] bg-white p-4 shadow-sm" aria-labelledby="parts-list-title">
        <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div><h2 id="parts-list-title" className="text-lg font-bold text-[var(--ppap-navy)]">My parts</h2><p className="text-sm text-[var(--ppap-muted)]">Only parts owned by you are shown.</p></div>
          <div className="relative w-full sm:max-w-xs"><Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--ppap-muted)]" aria-hidden="true" /><Input aria-label="Search parts" className="pl-9" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Search name, number, revision" /></div>
        </div>

        {partsQuery.isLoading ? <div aria-label="Loading parts" className="space-y-3">{[1, 2, 3].map((x) => <Skeleton key={x} className="h-24 w-full" />)}</div>
          : partsQuery.isError ? <div role="alert" className="ppap-error"><strong>Parts could not be loaded.</strong><span>{describe(partsQuery.error)}</span></div>
            : !partsQuery.data?.items.length ? <div className="ppap-empty"><ClipboardList /><h3>No parts found</h3><p>Add a part to reuse its details in future PPAP drafts.</p></div>
              : <ul className="divide-y divide-[var(--ppap-border)]">
                {partsQuery.data.items.map((part) => <li key={part.id} className="grid gap-3 py-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
                  <div className="min-w-0"><div className="flex flex-wrap items-center gap-2"><strong className="truncate text-[var(--ppap-navy)]">{part.partName}</strong><span className={part.ruleTemplateVersionId ? "ppap-status ppap-status--submitted" : "ppap-status ppap-status--draft"}>{part.ruleTemplateVersionId ? "Template snapshot assigned" : "No template"}</span></div><p className="mt-1 text-sm text-[var(--ppap-muted)]">{part.partNumber} · Revision {part.drawingRevision}</p>{part.ruleTemplateVersionId && <p className="mt-1 break-all text-xs text-[var(--ppap-muted)]">Snapshot {part.ruleTemplateVersionId}</p>}</div>
                  <div className="flex flex-wrap gap-2"><Button size="sm" variant="outline" onClick={() => open("edit", part)}><Pencil />Edit</Button><Button size="sm" variant="outline" onClick={() => open("assign", part)}><Link2 />{part.ruleTemplateId ? "Change template" : "Assign template"}</Button><Button size="sm" disabled={!part.ruleTemplateVersionId} onClick={() => open("draft", part)}><FilePlus2 />Create draft</Button></div>
                </li>)}
              </ul>}

        {partsQuery.data && partsQuery.data.totalPages > 1 && <nav aria-label="Parts pagination" className="mt-4 flex items-center justify-between border-t border-[var(--ppap-border)] pt-4"><span className="text-sm text-[var(--ppap-muted)]">Page {partsQuery.data.pageNumber} of {partsQuery.data.totalPages}</span><div className="flex gap-2"><Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((value) => value - 1)}>Previous</Button><Button variant="outline" size="sm" disabled={page >= partsQuery.data.totalPages} onClick={() => setPage((value) => value + 1)}>Next</Button></div></nav>}
      </section>

      <Dialog open={dialog === "create" || dialog === "edit"} onOpenChange={(value) => !value && setDialog(null)}><PartDetailsDialog part={dialog === "edit" ? selected : null} onSaved={() => { setDialog(null); changed(); }} /></Dialog>
      <Dialog open={dialog === "assign"} onOpenChange={(value) => !value && setDialog(null)}>{selected && <AssignTemplateDialog part={selected} onSaved={() => { setDialog(null); changed(); }} />}</Dialog>
      <Dialog open={dialog === "draft"} onOpenChange={(value) => !value && setDialog(null)}>{selected && <CreatePartDraftDialog part={selected} onCreated={(id) => navigate(`/ppap/submissions/${id}`)} />}</Dialog>
    </div>
  );
}

function PartDetailsDialog({ part, onSaved }: { part: PpapPartDto | null; onSaved: () => void }) {
  const [partName, setPartName] = useState(part?.partName ?? "");
  const [partNumber, setPartNumber] = useState(part?.partNumber ?? "");
  const [drawingRevision, setDrawingRevision] = useState(part?.drawingRevision ?? "");
  const mutation = useMutation({
    mutationFn: (input: CreatePartInput) => part ? updatePpapPart(part.id, { ...input, expectedVersion: part.version }) : createPpapPart(input),
    onSuccess: () => { toast.success(part ? "Part updated" : "Part created"); onSaved(); },
    onError: (error) => toast.error(part ? "Unable to update part" : "Unable to create part", { description: describe(error) }),
  });
  const submit = (event: FormEvent) => { event.preventDefault(); if (!partName.trim() || !partNumber.trim() || !drawingRevision.trim()) return; mutation.mutate({ partName: partName.trim(), partNumber: partNumber.trim(), drawingRevision: drawingRevision.trim() }); };
  return <DialogContent className="ppap-dialog"><form onSubmit={submit}><DialogHeader><DialogTitle>{part ? "Edit part" : "Add part"}</DialogTitle><DialogDescription>Part details remain independent from any submission draft.</DialogDescription></DialogHeader><DialogBody className="ppap-form-grid"><Field id="part-name" label="Part name" value={partName} setValue={setPartName} maxLength={200} /><Field id="part-number" label="Part number" value={partNumber} setValue={setPartNumber} maxLength={100} /><Field id="drawing-revision" label="Drawing revision" value={drawingRevision} setValue={setDrawingRevision} maxLength={50} /></DialogBody><DialogFooter><Button type="submit" disabled={mutation.isPending}>{mutation.isPending ? "Saving…" : "Save part"}</Button></DialogFooter></form></DialogContent>;
}

function Field({ id, label, value, setValue, maxLength }: { id: string; label: string; value: string; setValue: (value: string) => void; maxLength: number }) {
  return <div className="ppap-field ppap-field--wide"><Label htmlFor={id}>{label}</Label><Input id={id} value={value} maxLength={maxLength} onChange={(event) => setValue(event.target.value)} required /></div>;
}

function AssignTemplateDialog({ part, onSaved }: { part: PpapPartDto; onSaved: () => void }) {
  const [templateId, setTemplateId] = useState(part.ruleTemplateId ?? "");
  const templates = useQuery({ queryKey: ["rules", "templates", { status: "Published", pageNumber: 1, pageSize: 100 }], queryFn: getPublishedRuleTemplates });
  const mutation = useMutation({ mutationFn: (ruleTemplateId: string) => assignPpapPartTemplate(part.id, { ruleTemplateId, expectedVersion: part.version }), onSuccess: () => { toast.success("Template snapshot assigned"); onSaved(); }, onError: (error) => toast.error("Unable to assign template", { description: describe(error) }) });
  const submit = (event: FormEvent) => { event.preventDefault(); if (templateId) mutation.mutate(templateId); };
  return <DialogContent className="ppap-dialog"><form onSubmit={submit}><DialogHeader><DialogTitle>Assign published template</DialogTitle><DialogDescription>The current published version is saved as a reproducible snapshot.</DialogDescription></DialogHeader><DialogBody><div className="ppap-field"><Label htmlFor="part-template">Published template</Label><select id="part-template" value={templateId} onChange={(event) => setTemplateId(event.target.value)} required><option value="">Select a template</option>{templates.data?.items.map((template) => <option key={template.id} value={template.id}>{template.name} ({template.stableCode})</option>)}</select>{templates.isLoading && <span role="status">Loading published templates…</span>}{templates.isError && <span role="alert">Published templates could not be loaded.</span>}</div></DialogBody><DialogFooter><Button type="submit" disabled={!templateId || mutation.isPending}>{mutation.isPending ? "Assigning…" : "Assign template"}</Button></DialogFooter></form></DialogContent>;
}

function CreatePartDraftDialog({ part, onCreated }: { part: PpapPartDto; onCreated: (id: string) => void }) {
  const queryClient = useQueryClient();
  const [level, setLevel] = useState("3");
  const mutation = useMutation({ mutationFn: (ppapLevel: number) => createDraftFromPpapPart(part.id, ppapLevel), onSuccess: (id) => { toast.success("Draft created from part"); void queryClient.invalidateQueries({ queryKey: ["ppap", "submissions"] }); onCreated(id); }, onError: (error) => toast.error("Unable to create draft", { description: describe(error) }) });
  const submit = (event: FormEvent) => { event.preventDefault(); mutation.mutate(Number(level)); };
  return <DialogContent className="ppap-dialog"><form onSubmit={submit}><DialogHeader><DialogTitle>Create draft from {part.partName}</DialogTitle><DialogDescription>Part details and the exact assigned template version will be copied into the draft.</DialogDescription></DialogHeader><DialogBody><div className="ppap-field"><Label htmlFor="part-ppap-level">PPAP level</Label><select id="part-ppap-level" value={level} onChange={(event) => setLevel(event.target.value)}>{[1, 2, 3, 4, 5].map((value) => <option key={value} value={value}>Level {value}</option>)}</select></div></DialogBody><DialogFooter><Button type="submit" disabled={mutation.isPending}>{mutation.isPending ? "Creating…" : "Create draft"}</Button></DialogFooter></form></DialogContent>;
}
