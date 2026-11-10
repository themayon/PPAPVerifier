import { useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useBlocker, useNavigate, useParams, useSearchParams } from "react-router-dom";
import {
  AlertTriangle,
  Archive,
  AtSign,
  ChevronRight,
  ClipboardCheck,
  ExternalLink,
  FileText,
  Fingerprint,
  Info,
  Loader2,
  Phone,
  Save,
  Shapes,
  ShieldCheck,
  UserRound,
} from "lucide-react";
import { toast } from "sonner";
import {
  createPpapPart,
  getPpapPart,
  getPublishedRuleTemplates,
  searchPpapSuppliers,
  updatePpapPart,
  type PartClassification,
  type PpapPartDto,
  type PpapSupplierDto,
} from "@/api/ppap";
import { useAuth } from "@/auth/use-auth";
import { Combobox } from "@/components/list/combobox";
import { EntityEmpty, ErrorBand } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { ScoreBar } from "@/pages/ppap/directory-ui";

const PPAP_LEVELS = [
  { value: 1, label: "Level 1 - Warrant only", detail: "Part Submission Warrant only (plus appearance approval for appearance items)." },
  { value: 2, label: "Level 2 - Warrant, samples, limited data", detail: "Warrant with product samples and limited supporting data." },
  { value: 3, label: "Level 3 - Warrant, samples, full documentation", detail: "Warrant with product samples and complete supporting data." },
  { value: 4, label: "Level 4 - Warrant, customer-defined", detail: "Warrant and other requirements as defined by the customer." },
  { value: 5, label: "Level 5 - Full review at supplier site", detail: "Warrant, samples, and complete supporting data reviewed at the supplier's manufacturing location." },
] as const;

const CLASSIFICATIONS: { value: PartClassification; title: string; caption: string; icon: typeof ShieldCheck }[] = [
  { value: "Safety", title: "Safety", caption: "Strict compliance", icon: ShieldCheck },
  { value: "Critical", title: "Critical", caption: "High priority", icon: AlertTriangle },
  { value: "Standard", title: "Standard", caption: "Standard PPAP", icon: Archive },
];

type FormState = { partName: string; partNumber: string; drawingRevision: string; supplierId: string | null; ppapLevel: number; classification: PartClassification; ruleTemplateId: string | null };
type Errors = Partial<Record<"partName" | "partNumber" | "drawingRevision", string>>;

export function PartEditorPage() {
  const { partId } = useParams();
  const { user, permissionsHydrated } = useAuth();
  const perms = user?.permissions ?? [];
  const isNew = !partId;
  const canSave = perms.includes(isNew ? "Permissions.Ppap.Parts.Create" : "Permissions.Ppap.Parts.Update");
  const query = useQuery({ queryKey: ["ppap", "part", partId], queryFn: () => getPpapPart(partId!), enabled: Boolean(partId) });

  if (permissionsHydrated && !perms.includes("Permissions.Ppap.Parts.View")) {
    return <EntityEmpty icon={Fingerprint} title="Parts unavailable" body="You do not have permission to view PPAP parts." />;
  }
  if (!isNew && query.isError) return <div className="space-y-4"><Breadcrumb current="Edit part" /><ErrorBand message={describe(query.error)} /></div>;
  if (!isNew && !query.data) {
    return (
      <div role="status" aria-busy="true" className="space-y-4">
        <span className="sr-only">Loading part…</span>
        <Skeleton className="h-4 w-72" />
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_23rem]"><Skeleton className="h-[36rem] rounded-2xl" /><Skeleton className="h-96 rounded-2xl" /></div>
      </div>
    );
  }
  const editable = canSave && (isNew || query.data?.canEdit !== false);
  return <PartForm key={query.data ? `${query.data.id}:${query.data.version}` : "new"} part={query.data ?? null} editable={editable} />;
}

function PartForm({ part, editable }: { part: PpapPartDto | null; editable: boolean }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [params] = useSearchParams();
  const isNew = !part;
  const suppliersQuery = useQuery({ queryKey: ["ppap", "suppliers", "picker"], queryFn: () => searchPpapSuppliers({ pageSize: 200, sort: "name" }), staleTime: 60_000 });
  const templatesQuery = useQuery({ queryKey: ["rules", "templates", { status: "Published", pageNumber: 1, pageSize: 100 }], queryFn: getPublishedRuleTemplates, enabled: isNew });
  const initial = useMemo<FormState>(() => ({
    partName: part?.partName ?? "",
    partNumber: part?.partNumber ?? "",
    drawingRevision: part?.drawingRevision ?? "",
    supplierId: part?.supplierId ?? params.get("supplier"),
    ppapLevel: part?.ppapLevel ?? 3,
    classification: part?.classification ?? "Standard",
    ruleTemplateId: null,
  }), [part, params]);
  const [form, setForm] = useState<FormState>(initial);
  const [errors, setErrors] = useState<Errors>({});
  const skipBlock = useRef(false);
  const dirty = editable && JSON.stringify(form) !== JSON.stringify(initial);
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty && !skipBlock.current && currentLocation.pathname !== nextLocation.pathname);
  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm((current) => ({ ...current, [key]: value }));

  const suppliers = suppliersQuery.data?.items ?? [];
  const supplier = suppliers.find((item) => item.id === form.supplierId) ?? null;
  const supplierOptions = suppliers
    .filter((item) => item.isActive || item.id === part?.supplierId)
    .map((item) => ({ value: item.id, label: item.name, hint: item.isActive ? item.code : `${item.code} · inactive` }));
  const level = PPAP_LEVELS.find((item) => item.value === form.ppapLevel) ?? PPAP_LEVELS[2];
  const safetyBelowLevel5 = form.classification === "Safety" && form.ppapLevel < 5;

  const save = useMutation({
    mutationFn: (input: FormState) => {
      const base = { partName: input.partName.trim(), partNumber: input.partNumber.trim(), drawingRevision: input.drawingRevision.trim(), supplierId: input.supplierId, ppapLevel: input.ppapLevel, classification: input.classification };
      return part
        ? updatePpapPart(part.id, { ...base, expectedVersion: part.version })
        : createPpapPart({ ...base, ...(input.ruleTemplateId ? { ruleTemplateId: input.ruleTemplateId } : {}) });
    },
    onSuccess: async (saved) => {
      skipBlock.current = true;
      toast.success(isNew ? `Part ${saved.partNumber} created` : "Part saved");
      await queryClient.invalidateQueries({ queryKey: ["ppap", "parts"] });
      await queryClient.invalidateQueries({ queryKey: ["ppap", "part", saved.id] });
      navigate("/ppap/parts");
    },
    onError: (error) => toast.error(isNew ? "Unable to create part" : "Unable to save part", { description: describe(error) }),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const next: Errors = {};
    if (!form.partName.trim()) next.partName = "Part name is required.";
    if (!form.partNumber.trim()) next.partNumber = "Part number is required.";
    if (!form.drawingRevision.trim()) next.drawingRevision = "Engineering revision is required.";
    setErrors(next);
    if (Object.keys(next).length > 0) {
      document.getElementById(`part-${Object.keys(next)[0]}`)?.focus();
      return;
    }
    save.mutate(form);
  };

  return (
    <div className="fsh-enter space-y-5">
      <Breadcrumb current={isNew ? "New Part Creation" : `Edit ${part.partNumber}`} />
      {!editable && part && (
        <div role="note" className="flex items-center gap-2 rounded-xl border border-[var(--color-border)] bg-[var(--color-muted)] px-4 py-3 text-[13px]">
          <Info className="size-4 text-[var(--color-muted-foreground)]" />This part is read-only for you. Only its owner can change it.
        </div>
      )}
      <form onSubmit={submit} noValidate>
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_23rem]">
          <div className="space-y-6">
            <Card icon={Fingerprint} title="Part Identity">
              <div className="grid gap-4 sm:grid-cols-2">
                <TextField id="part-partName" label="Part Name" className="sm:col-span-2" value={form.partName} onChange={(value) => set("partName", value)} placeholder="e.g. Front Axle Assembly" maxLength={200} error={errors.partName} disabled={!editable} />
                <TextField id="part-partNumber" label="Part Number" value={form.partNumber} onChange={(value) => set("partNumber", value)} placeholder="PN-000-0000" maxLength={100} error={errors.partNumber} disabled={!editable} />
                <TextField id="part-drawingRevision" label="Engineering Revision" hint="Rev A, B, C…" value={form.drawingRevision} onChange={(value) => set("drawingRevision", value)} placeholder="e.g. Rev 04" maxLength={50} error={errors.drawingRevision} disabled={!editable} />
              </div>
            </Card>

            <Card icon={ClipboardCheck} title="Supplier & PPAP Setup">
              <div className="space-y-5">
                <div className="space-y-1.5">
                  <Label htmlFor="part-supplier" className="text-[13px] font-medium">Search Supplier Directory</Label>
                  <Combobox id="part-supplier" label="Supplier" searchable clearable className="h-11" value={form.supplierId} disabled={!editable || suppliersQuery.isLoading}
                    placeholder={suppliersQuery.isLoading ? "Loading suppliers…" : "Search by supplier name or code"} options={supplierOptions} onChange={(value) => set("supplierId", value)} />
                  {suppliersQuery.isError && <p role="alert" className="text-[12px] text-[var(--color-destructive)]">Suppliers could not be loaded.</p>}
                </div>
                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="space-y-1.5">
                    <Label htmlFor="part-level" className="text-[13px] font-medium">PPAP Submission Level</Label>
                    <Combobox id="part-level" label="PPAP level" className="h-11" value={String(form.ppapLevel)} disabled={!editable}
                      options={PPAP_LEVELS.map((item) => ({ value: String(item.value), label: item.label }))} onChange={(value) => value && set("ppapLevel", Number(value))} />
                    <p className="text-[11.5px] italic text-[var(--color-muted-foreground)]">Level 3 is default for standard series production parts.</p>
                  </div>
                  <div className="flex gap-3 rounded-xl border border-[oklch(from_var(--color-primary)_l_c_h_/_0.2)] bg-[var(--color-primary-soft)] p-3.5 text-[12.5px] leading-relaxed">
                    <FileText className="mt-0.5 size-4 shrink-0 text-[var(--color-primary)]" />
                    <p><span className="font-semibold">{level.detail}</span> The selected level dictates the mandatory document checklist (ISIR, FMEA, CP, etc.) required for final submission.</p>
                  </div>
                </div>
                {isNew && (
                  <div className="space-y-1.5">
                    <Label htmlFor="part-template" className="text-[13px] font-medium">Rule Template</Label>
                    <Combobox id="part-template" label="Rule template" className="h-11" value={form.ruleTemplateId} disabled={!editable || templatesQuery.isLoading} emptyOptionLabel="Assign later"
                      placeholder="Assign later" options={(templatesQuery.data?.items ?? []).map((item) => ({ value: item.id, label: item.name, hint: item.stableCode }))} onChange={(value) => set("ruleTemplateId", value)} />
                    <p className="text-[11.5px] text-[var(--color-muted-foreground)]">Decides which documents and verification rules apply to this part's submissions.</p>
                  </div>
                )}
              </div>
            </Card>

            <Card icon={Shapes} title="Part Classification">
              <div role="radiogroup" aria-label="Part classification" className="grid gap-3 sm:grid-cols-3">
                {CLASSIFICATIONS.map((item) => {
                  const selected = form.classification === item.value;
                  return (
                    <button
                      key={item.value}
                      type="button"
                      role="radio"
                      aria-checked={selected}
                      disabled={!editable}
                      onClick={() => {
                        set("classification", item.value);
                        if (item.value === "Safety" && form.ppapLevel < 5) set("ppapLevel", 5);
                      }}
                      className={cn(
                        "flex cursor-pointer flex-col items-start gap-2 rounded-xl border-2 p-4 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-60",
                        selected ? "border-[var(--color-primary)] bg-[var(--color-primary-soft)]" : "border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] hover:border-[var(--color-border-strong)]",
                      )}
                    >
                      <item.icon className={cn("size-5", selected ? "text-[var(--color-primary)]" : "text-[var(--color-muted-foreground)]")} />
                      <span className="text-[14px] font-semibold">{item.title}</span>
                      <span className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-muted-foreground)]">{item.caption}</span>
                    </button>
                  );
                })}
              </div>
              {safetyBelowLevel5 && (
                <p role="alert" className="mt-3 flex items-center gap-2 text-[12.5px] text-[var(--color-warning)]"><AlertTriangle className="size-4" />Safety parts normally require Level 5 PPAP documentation.</p>
              )}
            </Card>
          </div>

          <aside className="space-y-4 lg:sticky lg:top-2 lg:self-start">
            <SupplierCard supplier={supplier} loading={suppliersQuery.isLoading && Boolean(form.supplierId)} />
            <section className="flex gap-3 rounded-2xl border border-[oklch(from_var(--color-primary)_l_c_h_/_0.2)] bg-[var(--color-primary-soft)] p-5">
              <Info className="mt-0.5 size-5 shrink-0 text-[var(--color-primary)]" />
              <div>
                <h2 className="text-[12px] font-semibold uppercase tracking-[0.1em] text-[var(--color-primary)]">System Note</h2>
                <p className="mt-1 text-[13px] leading-relaxed">Safety-critical parts require Level 5 PPAP documentation as per IATF 16949 standards.</p>
              </div>
            </section>
          </aside>
        </div>

        <div className="mt-8 flex flex-col-reverse gap-3 border-t border-[var(--color-border)] pt-6 sm:flex-row sm:justify-end">
          <Button type="button" variant="outline" size="lg" onClick={() => navigate("/ppap/parts")} disabled={save.isPending}>{editable ? "Cancel & Discard" : "Back"}</Button>
          {editable && (
            <Button type="submit" size="lg" disabled={save.isPending}>
              {save.isPending ? <Loader2 className="size-4 animate-spin" /> : <Save className="size-4" />}
              {isNew ? "Save Part" : "Save Changes"}
            </Button>
          )}
        </div>
      </form>

      <Dialog open={blocker.state === "blocked"} onOpenChange={(open) => !open && blocker.reset?.()}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader><DialogTitle>Discard this part?</DialogTitle><DialogDescription>Your changes haven't been saved.</DialogDescription></DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => blocker.reset?.()}>Keep editing</Button>
            <Button variant="destructive" onClick={() => blocker.proceed?.()}>Discard</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function SupplierCard({ supplier, loading }: { supplier: PpapSupplierDto | null; loading: boolean }) {
  if (loading) return <Skeleton className="h-96 rounded-2xl" />;
  if (!supplier) {
    return (
      <section aria-label="Selected supplier" className="rounded-2xl border border-dashed border-[var(--color-border)] bg-[var(--color-card)] p-6 text-center">
        <UserRound className="mx-auto size-8 text-[var(--color-muted-foreground)]" />
        <p className="mt-3 text-[14px] font-semibold">No supplier selected</p>
        <p className="mt-1 text-[12.5px] text-[var(--color-muted-foreground)]">Search the supplier directory to see the vendor's quality metadata and contact.</p>
      </section>
    );
  }
  return (
    <section aria-label="Selected supplier" className="overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-sm">
      <div className="relative isolate overflow-hidden bg-[var(--color-primary)] px-6 py-5 text-[var(--color-primary-foreground)]">
        <ShieldCheck aria-hidden className="absolute -right-6 -top-6 -z-10 size-32 opacity-15" />
        <span className="inline-flex h-6 items-center rounded-md bg-white/15 px-2 text-[10.5px] font-semibold uppercase tracking-[0.12em]">{supplier.isActive ? "Active vendor" : "Inactive vendor"}</span>
        <h2 className="mt-3 font-display text-[20px] font-semibold leading-tight">{supplier.name}</h2>
        <p className="mt-0.5 font-mono text-[12.5px] opacity-85">ID: {supplier.code}</p>
      </div>
      <div className="space-y-5 p-6">
        <div>
          <p className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-muted-foreground)]">Supplier Metadata</p>
          <div className="mt-2 grid grid-cols-2 gap-3">
            <Metric label="Quality Grade">
              {supplier.qualityGrade ? <span className="flex items-center gap-1.5"><span aria-hidden className="size-2 rounded-full bg-[var(--color-success)]" />Grade {supplier.qualityGrade}</span> : <span className="text-[var(--color-muted-foreground)]">Not graded</span>}
            </Metric>
            <Metric label="Overall Score"><ScoreBar value={supplier.qualityScore} label="Overall score" /></Metric>
            <Metric label="Tier Level">{supplier.tierLevel ?? <span className="text-[var(--color-muted-foreground)]">Pending</span>}</Metric>
            <Metric label="Active PPAPs">{supplier.activePpapCount ?? 0}</Metric>
          </div>
        </div>
        <div>
          <p className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-muted-foreground)]">Primary Contact</p>
          <div className="mt-2 flex items-center gap-3">
            <span className="grid size-10 place-items-center rounded-full bg-[var(--color-muted)] text-[var(--color-muted-foreground)]"><UserRound className="size-5" /></span>
            <div className="min-w-0">
              <p className="truncate text-[14px] font-semibold">{supplier.contactName || "No contact on file"}</p>
              {supplier.contactJobTitle && <p className="truncate text-[12.5px] text-[var(--color-muted-foreground)]">{supplier.contactJobTitle}</p>}
            </div>
          </div>
          <ul className="mt-3 space-y-1.5 text-[13px]">
            {supplier.email && <li className="flex items-center gap-2"><AtSign className="size-3.5 text-[var(--color-muted-foreground)]" /><a href={`mailto:${supplier.email}`} className="truncate hover:underline">{supplier.email}</a></li>}
            {supplier.phone && <li className="flex items-center gap-2"><Phone className="size-3.5 text-[var(--color-muted-foreground)]" />{supplier.phone}</li>}
          </ul>
        </div>
        <Button asChild variant="secondary" className="w-full">
          <Link to={`/ppap/suppliers/${supplier.id}`}><ExternalLink className="size-4" />View Full Directory Profile</Link>
        </Button>
      </div>
    </section>
  );
}

function Metric({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="rounded-xl bg-[oklch(from_var(--color-muted)_l_c_h_/_0.5)] px-3 py-2.5">
      <p className="text-[11px] text-[var(--color-muted-foreground)]">{label}</p>
      <div className="mt-1 text-[13.5px] font-semibold">{children}</div>
    </div>
  );
}

function Breadcrumb({ current }: { current: string }) {
  return (
    <nav aria-label="Breadcrumb" className="flex flex-wrap items-center gap-1.5 text-[13px] text-[var(--color-muted-foreground)]">
      <Link to="/ppap" className="hover:text-[var(--color-foreground)]">Dashboard</Link>
      <ChevronRight className="size-3.5" />
      <Link to="/ppap/parts" className="hover:text-[var(--color-foreground)]">Parts</Link>
      <ChevronRight className="size-3.5" />
      <span aria-current="page" className="truncate font-medium text-[var(--color-foreground)]">{current}</span>
    </nav>
  );
}

function Card({ icon: Icon, title, children }: { icon: typeof Fingerprint; title: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
      <h2 className="flex items-center gap-2.5 border-b border-[var(--color-border)] px-6 py-4 text-[15px] font-semibold">
        <Icon className="size-5 text-[var(--color-primary)]" />{title}
      </h2>
      <div className="p-6">{children}</div>
    </section>
  );
}

function TextField({ id, label, hint, value, onChange, placeholder, maxLength, error, disabled, className }: {
  id: string;
  label: string;
  hint?: string;
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
  maxLength: number;
  error?: string;
  disabled?: boolean;
  className?: string;
}) {
  return (
    <div className={cn("space-y-1.5", className)}>
      <div className="flex items-baseline justify-between gap-2">
        <Label htmlFor={id} className="text-[13px] font-medium">{label}<span aria-hidden className="ml-0.5 text-[var(--color-destructive)]">*</span></Label>
        {hint && <span className="text-[12px] italic text-[var(--color-muted-foreground)]">{hint}</span>}
      </div>
      <Input id={id} value={value} placeholder={placeholder} maxLength={maxLength} disabled={disabled} required aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-error` : undefined}
        onChange={(event) => onChange(event.target.value)} className="h-11 bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)]" />
      {error && <p id={`${id}-error`} role="alert" className="text-[12px] text-[var(--color-destructive)]">{error}</p>}
    </div>
  );
}
