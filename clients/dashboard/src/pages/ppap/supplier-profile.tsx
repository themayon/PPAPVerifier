import { useMemo, useRef, useState, type DragEvent, type FormEvent, type ReactNode } from "react";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useBlocker, useNavigate, useParams } from "react-router-dom";
import {
  ArrowRight,
  BadgeCheck,
  Building2,
  ChevronRight,
  CircleCheck,
  CircleX,
  Circle,
  ContactRound,
  Download,
  FileText,
  FileUp,
  Globe2,
  Info,
  LifeBuoy,
  Loader2,
  MapPin,
  Trash2,
  UsersRound,
  X,
} from "lucide-react";
import { toast } from "sonner";
import { getFileDownloadUrl } from "@/api/files";
import { getUserById, type UserDto } from "@/api/identity";
import {
  createPpapSupplier,
  getPpapSupplier,
  SUPPLIER_GRADES,
  SUPPLIER_TIERS,
  updatePpapSupplier,
  type PpapSupplierDto,
  type SupplierCertificateItem,
  type SupplierCertificateType,
  type SupplierProfileInput,
  type SupplierQualityGrade,
  type SupplierTierLevel,
} from "@/api/ppap";
import { useAuth } from "@/auth/use-auth";
import { Combobox } from "@/components/list/combobox";
import { UserPicker } from "@/components/identity/user-picker";
import { EntityEmpty, ErrorBand } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { formatBytes, useFileUpload } from "@/hooks/use-file-upload";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";

const CERTIFICATE_TYPES: readonly SupplierCertificateType[] = ["ISO 9001", "IATF 16949", "ISO 14001", "Other"];
const ALLOWED_EXTENSIONS = [".pdf", ".jpg", ".jpeg", ".png"];
const MAX_BYTES = 10 * 1024 * 1024;
const DUNS_PATTERN = /^\d{2}-?\d{3}-?\d{4}$/;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type FormState = SupplierProfileInput & { isActive: boolean; userIds: string[]; certificates: SupplierCertificateItem[] };
type Errors = Partial<Record<"name" | "dunsNumber" | "email", string>>;

const blank: FormState = {
  name: "", taxId: null, dunsNumber: null, address: null, city: null, country: null, siteName: null, siteAddress: null, siteSameAsHeadquarters: false,
  contactName: null, contactJobTitle: null, email: null, phone: null, tierLevel: null, qualityGrade: null, isActive: true, userIds: [], certificates: [],
};

function fromSupplier(supplier: PpapSupplierDto): FormState {
  return {
    name: supplier.name,
    taxId: supplier.taxId ?? null,
    dunsNumber: supplier.dunsNumber ? formatDuns(supplier.dunsNumber) : null,
    address: supplier.address ?? null,
    city: supplier.city ?? null,
    country: supplier.country ?? null,
    siteName: supplier.siteName ?? null,
    siteAddress: supplier.siteAddress ?? null,
    siteSameAsHeadquarters: supplier.siteSameAsHeadquarters ?? false,
    contactName: supplier.contactName ?? null,
    contactJobTitle: supplier.contactJobTitle ?? null,
    email: supplier.email ?? null,
    phone: supplier.phone ?? null,
    tierLevel: supplier.tierLevel ?? null,
    qualityGrade: supplier.qualityGrade ?? null,
    isActive: supplier.isActive,
    userIds: supplier.userIds,
    certificates: (supplier.certificates ?? []).map(({ fileAssetId, fileName, certificateType }) => ({ fileAssetId, fileName, certificateType })),
  };
}

function formatDuns(value: string) {
  const digits = value.replace(/\D/g, "");
  return digits.length === 9 ? `${digits.slice(0, 2)}-${digits.slice(2, 5)}-${digits.slice(5)}` : value;
}

function guessType(fileName: string): SupplierCertificateType {
  const name = fileName.toLowerCase();
  if (name.includes("iatf") || name.includes("16949")) return "IATF 16949";
  if (name.includes("14001")) return "ISO 14001";
  if (name.includes("9001")) return "ISO 9001";
  return "Other";
}

const clean = (value: string | null) => (value?.trim() ? value.trim() : null);

export function SupplierProfilePage() {
  const { supplierId } = useParams();
  const { user, permissionsHydrated } = useAuth();
  const perms = user?.permissions ?? [];
  const isNew = !supplierId;
  const canSave = isNew ? perms.includes("Permissions.Ppap.Suppliers.Create") : perms.includes("Permissions.Ppap.Suppliers.Update");
  const query = useQuery({ queryKey: ["ppap", "supplier", supplierId], queryFn: () => getPpapSupplier(supplierId!), enabled: Boolean(supplierId) });

  if (permissionsHydrated && !perms.includes("Permissions.Ppap.Suppliers.View")) {
    return <EntityEmpty icon={Building2} title="Suppliers unavailable" body="You do not have permission to view suppliers." />;
  }
  if (!isNew && query.isError) return <div className="space-y-4"><Breadcrumb current="Supplier profile" /><ErrorBand message={describe(query.error)} /></div>;
  if (!isNew && !query.data) {
    return (
      <div role="status" aria-busy="true" className="space-y-4">
        <span className="sr-only">Loading supplier…</span>
        <Skeleton className="h-4 w-64" />
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]"><Skeleton className="h-[40rem] rounded-2xl" /><Skeleton className="h-96 rounded-2xl" /></div>
      </div>
    );
  }
  return <SupplierProfileForm key={query.data ? `${query.data.id}:${query.data.version}` : "new"} supplier={query.data ?? null} canSave={canSave} />;
}

function SupplierProfileForm({ supplier, canSave }: { supplier: PpapSupplierDto | null; canSave: boolean }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const isNew = !supplier;
  const initial = useMemo(() => (supplier ? fromSupplier(supplier) : blank), [supplier]);
  const [form, setForm] = useState<FormState>(initial);
  const [errors, setErrors] = useState<Errors>({});
  const skipBlock = useRef(false);
  const dirty = canSave && JSON.stringify(form) !== JSON.stringify(initial);
  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm((current) => ({ ...current, [key]: value }));
  const text = (key: keyof SupplierProfileInput) => (value: string) => set(key, (value || null) as never);

  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty && !skipBlock.current && currentLocation.pathname !== nextLocation.pathname);

  const save = useMutation({
    mutationFn: async (input: FormState) => {
      const profile: SupplierProfileInput = {
        name: input.name.trim(),
        taxId: clean(input.taxId),
        dunsNumber: clean(input.dunsNumber),
        address: clean(input.address),
        city: clean(input.city),
        country: clean(input.country),
        siteName: clean(input.siteName),
        siteAddress: input.siteSameAsHeadquarters ? null : clean(input.siteAddress),
        siteSameAsHeadquarters: input.siteSameAsHeadquarters,
        contactName: clean(input.contactName),
        contactJobTitle: clean(input.contactJobTitle),
        email: clean(input.email),
        phone: clean(input.phone),
        tierLevel: input.tierLevel,
        qualityGrade: input.qualityGrade,
      };
      return supplier
        ? updatePpapSupplier(supplier.id, { expectedVersion: supplier.version, code: supplier.code, profile, isActive: input.isActive, userIds: input.userIds, certificates: input.certificates })
        : createPpapSupplier({ profile, userIds: input.userIds, certificates: input.certificates });
    },
    onSuccess: async (saved) => {
      skipBlock.current = true;
      toast.success(isNew ? `Supplier ${saved.code} created` : "Supplier profile saved");
      await queryClient.invalidateQueries({ queryKey: ["ppap", "suppliers"] });
      await queryClient.invalidateQueries({ queryKey: ["ppap", "supplier", saved.id] });
      navigate("/ppap/suppliers");
    },
    onError: (error) => toast.error(isNew ? "Unable to create supplier" : "Unable to save supplier", { description: describe(error) }),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const next: Errors = {};
    if (!form.name.trim()) next.name = "Legal entity name is required.";
    if (form.dunsNumber?.trim() && !DUNS_PATTERN.test(form.dunsNumber.trim())) next.dunsNumber = "Use 9 digits, e.g. 00-000-0000.";
    if (form.email?.trim() && !EMAIL_PATTERN.test(form.email.trim())) next.email = "Enter a valid email address.";
    setErrors(next);
    if (Object.keys(next).length > 0) {
      document.getElementById(`supplier-${Object.keys(next)[0]}`)?.focus();
      return;
    }
    save.mutate(form);
  };

  const dunsState = !form.dunsNumber?.trim() ? "pending" : DUNS_PATTERN.test(form.dunsNumber.trim()) ? "ok" : "error";
  const hasIatf = form.certificates.some((item) => item.certificateType === "IATF 16949");
  const disabled = !canSave;

  return (
    <div className="fsh-enter space-y-5">
      <Breadcrumb current={isNew ? "Create new profile" : supplier.name} />
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <form onSubmit={submit} noValidate className="overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
          <header className="border-b border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.45)] px-6 py-5 sm:px-8">
            <h1 className="font-display text-[22px] font-semibold tracking-tight">{isNew ? "Onboard New Supplier" : "Edit Supplier Profile"}</h1>
            <p className="mt-1 text-[13.5px] text-[var(--color-muted-foreground)]">
              {isNew ? "Initialize the Production Part Approval Process by registering a new manufacturing partner." : `Update ${supplier.name}'s company, location, contact, and certification details.`}
            </p>
          </header>

          <div className="divide-y divide-[var(--color-border)] px-6 sm:px-8">
            <Section icon={Building2} title="Company Profile">
              <TextField id="supplier-name" label="Legal Entity Name" required className="sm:col-span-2" value={form.name} onChange={(value) => set("name", value)} placeholder="e.g. Global Tech Automotive GmbH" maxLength={200} error={errors.name} disabled={disabled} />
              <TextField id="supplier-taxId" label="Tax ID / VAT Number" value={form.taxId} onChange={text("taxId")} placeholder="DE123456789" maxLength={50} disabled={disabled} />
              <TextField id="supplier-dunsNumber" label="DUNS Number" value={form.dunsNumber} onChange={text("dunsNumber")} placeholder="00-000-0000" maxLength={11} inputMode="numeric" error={errors.dunsNumber} disabled={disabled} onBlur={() => form.dunsNumber && set("dunsNumber", formatDuns(form.dunsNumber))} />
              <div className="space-y-1.5">
                <Label htmlFor="supplier-tierLevel" className="text-[13px] font-medium text-[var(--color-foreground)]">Tier Level</Label>
                <Combobox id="supplier-tierLevel" label="Tier level" className="h-10 bg-[var(--color-card)]" value={form.tierLevel} disabled={disabled} emptyOptionLabel="Pending assignment" placeholder="Pending assignment"
                  options={SUPPLIER_TIERS.map((value) => ({ value, label: value }))} onChange={(value) => set("tierLevel", value as SupplierTierLevel | null)} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="supplier-qualityGrade" className="text-[13px] font-medium text-[var(--color-foreground)]">Quality Grade</Label>
                <Combobox id="supplier-qualityGrade" label="Quality grade" className="h-10 bg-[var(--color-card)]" value={form.qualityGrade} disabled={disabled} emptyOptionLabel="Not graded" placeholder="Not graded"
                  options={SUPPLIER_GRADES.map((value) => ({ value, label: `Grade ${value}` }))} onChange={(value) => set("qualityGrade", value as SupplierQualityGrade | null)} />
              </div>
            </Section>

            <Section icon={MapPin} title="Location Details">
              <SubPanel title="Headquarters Address" className="sm:col-span-2">
                <TextField id="supplier-address" label="Street Address" srOnlyLabel className="sm:col-span-2" value={form.address} onChange={text("address")} placeholder="Street Address" maxLength={500} disabled={disabled} />
                <TextField id="supplier-city" label="City" srOnlyLabel value={form.city} onChange={text("city")} placeholder="City" maxLength={100} disabled={disabled} />
                <TextField id="supplier-country" label="Country" srOnlyLabel value={form.country} onChange={text("country")} placeholder="Country" maxLength={100} disabled={disabled} />
              </SubPanel>
              <SubPanel
                title="Manufacturing Site"
                className="sm:col-span-2"
                action={
                  <label className="inline-flex cursor-pointer items-center gap-2 text-[12px] text-[var(--color-muted-foreground)]">
                    <input type="checkbox" className="size-3.5 accent-[var(--color-primary)]" checked={form.siteSameAsHeadquarters} disabled={disabled} onChange={(event) => set("siteSameAsHeadquarters", event.target.checked)} />
                    Same as Headquarters
                  </label>
                }
              >
                <TextField id="supplier-siteName" label="Site Name or Plant Number" srOnlyLabel className="sm:col-span-2" value={form.siteName} onChange={text("siteName")} placeholder="Site Name or Plant Number" maxLength={200} disabled={disabled} />
                <TextField
                  id="supplier-siteAddress"
                  label="Site Street Address"
                  srOnlyLabel
                  className="sm:col-span-2"
                  value={form.siteSameAsHeadquarters ? form.address : form.siteAddress}
                  onChange={text("siteAddress")}
                  placeholder={form.siteSameAsHeadquarters ? "Uses the headquarters address" : "Street Address"}
                  maxLength={500}
                  disabled={disabled || form.siteSameAsHeadquarters}
                />
              </SubPanel>
            </Section>

            <Section icon={ContactRound} title="Primary Quality Contact">
              <TextField id="supplier-contactName" label="Full Name" value={form.contactName} onChange={text("contactName")} placeholder="John Doe" maxLength={200} disabled={disabled} />
              <TextField id="supplier-contactJobTitle" label="Job Title" value={form.contactJobTitle} onChange={text("contactJobTitle")} placeholder="Quality Assurance Manager" maxLength={200} disabled={disabled} />
              <TextField id="supplier-email" label="Email Address" type="email" value={form.email} onChange={text("email")} placeholder="j.doe@supplier.com" maxLength={256} error={errors.email} disabled={disabled} />
              <TextField id="supplier-phone" label="Phone Number" type="tel" value={form.phone} onChange={text("phone")} placeholder="+49 (0) 123 45678" maxLength={50} disabled={disabled} />
            </Section>

            <Section icon={BadgeCheck} title="Initial Certifications">
              <div className="sm:col-span-2">
                <CertificateUploader supplierId={supplier?.id ?? null} certificates={form.certificates} onChange={(certificates) => set("certificates", certificates)} disabled={disabled} />
              </div>
            </Section>

            <Section icon={UsersRound} title="Supplier Portal Users">
              <div className="sm:col-span-2">
                <LinkedUsers userIds={form.userIds} onChange={(userIds) => set("userIds", userIds)} disabled={disabled} />
              </div>
            </Section>

            {!isNew && (
              <Section icon={Info} title="Status">
                <div className="flex items-center gap-3 sm:col-span-2">
                  <Switch id="supplier-active" aria-label="Supplier active" checked={form.isActive} disabled={disabled} onCheckedChange={(value) => set("isActive", value)} />
                  <span className="text-[13px]">{form.isActive ? "Active" : "Inactive"} <span className="text-[var(--color-muted-foreground)]">· inactive suppliers can't be chosen for new parts</span></span>
                </div>
              </Section>
            )}
          </div>

          <footer className="flex flex-col-reverse gap-2 border-t border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] px-6 py-4 sm:flex-row sm:items-center sm:justify-end sm:px-8">
            <Button type="button" variant="ghost" onClick={() => navigate("/ppap/suppliers")} disabled={save.isPending}>{canSave ? "Discard" : "Back"}</Button>
            {canSave && (
              <Button type="submit" size="lg" disabled={save.isPending}>
                {save.isPending ? <Loader2 className="size-4 animate-spin" /> : null}
                {isNew ? "Create Supplier Profile" : "Save Changes"}
                {!save.isPending && <ArrowRight className="size-4" />}
              </Button>
            )}
          </footer>
        </form>

        <aside className="space-y-4 lg:sticky lg:top-2 lg:self-start">
          <section className="rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-5 shadow-xs" aria-labelledby="validation-title">
            <h2 id="validation-title" className="flex items-center gap-2 text-[14px] font-semibold">
              <Info className="size-5 text-[var(--color-warning)]" />Validation Requirements
            </h2>
            <ul className="mt-4 space-y-4">
              <Requirement state="ok" title="Vendor Code Syntax">
                {isNew ? <>Generated automatically on save in the <code className="font-mono text-[11px]">VND-000000</code> format.</> : <>Vendor code <code className="font-mono text-[11px]">{supplier.code}</code> is permanent.</>}
              </Requirement>
              <Requirement state={dunsState} title="DUNS Validation">
                {dunsState === "error" ? "Enter 9 digits in the 00-000-0000 format." : "9 digits in the 00-000-0000 format. The format is checked on save."}
              </Requirement>
              <Requirement state={hasIatf ? "ok" : "pending"} title="IATF Certificate">
                Recommended for Tier 1 suppliers. Upload a certificate and mark it as IATF 16949.
              </Requirement>
            </ul>
            <div className="mt-5 border-t border-[var(--color-border)] pt-4">
              <p className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-muted-foreground)]">System Preview</p>
              <dl className="mt-2 space-y-2 rounded-xl border border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] px-4 py-3 text-[12.5px]">
                <PreviewRow label="Supplier ID"><span className="font-mono font-semibold text-[var(--color-primary)]">{supplier?.code ?? "AUTO-PENDING"}</span></PreviewRow>
                <PreviewRow label="Tier Level"><span className="font-semibold">{form.tierLevel ?? "Pending Assignment"}</span></PreviewRow>
                {!isNew && <PreviewRow label="Overall score"><span className="font-semibold">{supplier.qualityScore != null ? `${supplier.qualityScore}%` : "No data"}</span></PreviewRow>}
                <PreviewRow label="Certificates"><span className="font-semibold">{form.certificates.length}</span></PreviewRow>
                {!isNew && <PreviewRow label="Parts assigned"><span className="font-semibold">{supplier.partCount}</span></PreviewRow>}
              </dl>
            </div>
          </section>

          <section className="rounded-2xl border border-[oklch(from_var(--color-primary)_l_c_h_/_0.2)] bg-[var(--color-primary-soft)] p-5">
            <h2 className="flex items-center gap-2 text-[14px] font-semibold text-[var(--color-primary)]"><LifeBuoy className="size-4" />Need Assistance?</h2>
            <p className="mt-2 text-[12.5px] leading-relaxed text-[var(--color-primary)]/85">Onboarding a complex legal entity with multiple manufacturing sites? Contact the supplier support team.</p>
            <Link to="/tickets" className="mt-3 inline-flex items-center gap-1 text-[12.5px] font-semibold text-[var(--color-primary)] hover:underline">Raise a support ticket<ArrowRight className="size-3.5" /></Link>
          </section>

          <section className="relative isolate overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[linear-gradient(135deg,oklch(from_var(--color-primary)_l_c_h_/_0.85),oklch(from_var(--color-info)_l_c_h_/_0.75))] p-5 text-white shadow-xs" aria-label="Site location">
            <div aria-hidden className="absolute inset-0 -z-10 opacity-25 [background-image:radial-gradient(circle_at_1px_1px,white_1px,transparent_0)] [background-size:14px_14px]" />
            <Globe2 aria-hidden className="absolute -right-6 -top-6 -z-10 size-36 opacity-20" />
            <MapPin className="size-6" />
            <p className="mt-3 text-[14px] font-semibold">Site Location</p>
            <p className="mt-0.5 text-[12.5px] text-white/85">
              {[form.city, form.country].filter((value) => value?.trim()).join(", ") || "Add the headquarters city and country."}
            </p>
            {(form.siteName?.trim() || form.siteSameAsHeadquarters) && (
              <p className="mt-2 text-[11.5px] text-white/75">Manufacturing: {form.siteName?.trim() || "Main plant"}{form.siteSameAsHeadquarters ? " (at headquarters)" : ""}</p>
            )}
          </section>
        </aside>
      </div>

      <Dialog open={blocker.state === "blocked"} onOpenChange={(open) => !open && blocker.reset?.()}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader><DialogTitle>Discard this supplier profile?</DialogTitle><DialogDescription>Your changes haven't been saved.</DialogDescription></DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => blocker.reset?.()}>Keep editing</Button>
            <Button variant="destructive" onClick={() => blocker.proceed?.()}>Discard</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function Breadcrumb({ current }: { current: string }) {
  return (
    <nav aria-label="Breadcrumb" className="flex flex-wrap items-center gap-1.5 text-[11.5px] font-medium uppercase tracking-[0.08em] text-[var(--color-muted-foreground)]">
      <Link to="/ppap" className="hover:text-[var(--color-foreground)]">Directory</Link>
      <ChevronRight className="size-3.5" />
      <Link to="/ppap/suppliers" className="hover:text-[var(--color-foreground)]">Suppliers</Link>
      <ChevronRight className="size-3.5" />
      <span aria-current="page" className="truncate text-[var(--color-foreground)]">{current}</span>
    </nav>
  );
}

function Section({ icon: Icon, title, children }: { icon: typeof Building2; title: string; children: ReactNode }) {
  return (
    <section className="py-6" aria-label={title}>
      <h2 className="mb-4 flex items-center gap-2 text-[14.5px] font-semibold">
        <span className="grid size-7 place-items-center rounded-lg bg-[var(--color-primary-soft)] text-[var(--color-primary)]"><Icon className="size-4" /></span>
        {title}
      </h2>
      <div className="grid gap-x-6 gap-y-4 sm:grid-cols-2">{children}</div>
    </section>
  );
}

function SubPanel({ title, action, className, children }: { title: string; action?: ReactNode; className?: string; children: ReactNode }) {
  return (
    <div className={cn("rounded-xl border border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] p-4", className)}>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <p className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-muted-foreground)]">{title}</p>
        {action}
      </div>
      <div className="grid gap-3 sm:grid-cols-2">{children}</div>
    </div>
  );
}

function TextField({ id, label, value, onChange, placeholder, maxLength, error, required, disabled, className, srOnlyLabel, type = "text", inputMode, onBlur }: {
  id: string;
  label: string;
  value: string | null;
  onChange: (value: string) => void;
  placeholder?: string;
  maxLength: number;
  error?: string;
  required?: boolean;
  disabled?: boolean;
  className?: string;
  srOnlyLabel?: boolean;
  type?: string;
  inputMode?: "numeric" | "text";
  onBlur?: () => void;
}) {
  return (
    <div className={cn("space-y-1.5", className)}>
      <Label htmlFor={id} className={cn("text-[13px] font-medium text-[var(--color-foreground)]", srOnlyLabel && "sr-only")}>
        {label}{required && <span aria-hidden className="ml-0.5 text-[var(--color-destructive)]">*</span>}
      </Label>
      <Input
        id={id}
        type={type}
        inputMode={inputMode}
        value={value ?? ""}
        placeholder={placeholder}
        maxLength={maxLength}
        disabled={disabled}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : undefined}
        onChange={(event) => onChange(event.target.value)}
        onBlur={onBlur}
        className="h-10 bg-[var(--color-card)]"
      />
      {error && <p id={`${id}-error`} role="alert" className="text-[12px] text-[var(--color-destructive)]">{error}</p>}
    </div>
  );
}

function Requirement({ state, title, children }: { state: "ok" | "pending" | "error"; title: string; children: ReactNode }) {
  const Icon = state === "ok" ? CircleCheck : state === "error" ? CircleX : Circle;
  return (
    <li className="flex gap-3" data-state={state}>
      <Icon aria-hidden className={cn("mt-0.5 size-[18px] shrink-0", state === "ok" ? "text-[var(--color-success)]" : state === "error" ? "text-[var(--color-destructive)]" : "text-[var(--color-muted-foreground)]/60")} />
      <div>
        <p className="text-[12.5px] font-semibold">{title}<span className="sr-only"> — {state === "ok" ? "met" : state === "error" ? "invalid" : "pending"}</span></p>
        <p className="mt-0.5 text-[12px] leading-relaxed text-[var(--color-muted-foreground)]">{children}</p>
      </div>
    </li>
  );
}

function PreviewRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-2">
      <dt className="text-[var(--color-muted-foreground)]">{label}:</dt>
      <dd>{children}</dd>
    </div>
  );
}

function CertificateUploader({ supplierId, certificates, onChange, disabled }: { supplierId: string | null; certificates: SupplierCertificateItem[]; onChange: (next: SupplierCertificateItem[]) => void; disabled: boolean }) {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const latest = useRef(certificates);
  latest.current = certificates;
  const uploader = useFileUpload({
    ownerType: "PpapSupplierCertificate",
    ownerId: supplierId,
    category: (file) => (file.name.toLowerCase().endsWith(".pdf") ? "Document" : "Image"),
    allowedExtensions: ALLOWED_EXTENSIONS,
    maxBytes: MAX_BYTES,
  });
  const busy = uploader.isUploading;

  const handleFiles = async (files: FileList | null) => {
    if (!files?.length || disabled) return;
    for (const file of Array.from(files)) {
      try {
        const asset = await uploader.upload(file);
        latest.current = [...latest.current, { fileAssetId: asset.id, fileName: asset.originalFileName, certificateType: guessType(asset.originalFileName) }];
        onChange(latest.current);
        uploader.reset();
      } catch (error) {
        toast.error(`Couldn't upload ${file.name}`, { description: error instanceof Error ? error.message : undefined });
      }
    }
    if (inputRef.current) inputRef.current.value = "";
  };

  const download = async (fileAssetId: string) => {
    try {
      const { url } = await getFileDownloadUrl(fileAssetId);
      window.open(url, "_blank", "noopener,noreferrer");
    } catch (error) {
      toast.error("Download failed", { description: describe(error) });
    }
  };

  return (
    <div className="space-y-3">
      <div
        onDragOver={(event: DragEvent) => { event.preventDefault(); if (!disabled && !busy) setDragOver(true); }}
        onDragLeave={() => setDragOver(false)}
        onDrop={(event: DragEvent) => { event.preventDefault(); setDragOver(false); if (!busy) void handleFiles(event.dataTransfer.files); }}
        className={cn(
          "flex flex-col items-center justify-center gap-3 rounded-2xl border-2 border-dashed px-6 py-10 text-center transition-colors",
          dragOver ? "border-[var(--color-primary)] bg-[var(--color-primary-soft)]" : "border-[var(--color-border)]",
          disabled && "opacity-60",
        )}
      >
        <span className="grid size-14 place-items-center rounded-full bg-[var(--color-primary-soft)] text-[var(--color-primary)]">
          {busy ? <Loader2 className="size-6 animate-spin" /> : <FileUp className="size-6" />}
        </span>
        <div>
          <p className="text-[14px] font-semibold">{busy ? `Uploading ${uploader.progress?.fileName ?? "…"} (${uploader.progress?.percent ?? 0}%)` : "Click to upload or drag and drop"}</p>
          <p className="mt-1 text-[12px] text-[var(--color-muted-foreground)]">ISO 9001, IATF 16949, or ISO 14001 (PDF, JPG, PNG up to 10MB)</p>
        </div>
        <input ref={inputRef} type="file" multiple accept={ALLOWED_EXTENSIONS.join(",")} className="sr-only" aria-label="Certificate files" disabled={disabled || busy} onChange={(event) => void handleFiles(event.target.files)} />
        <Button type="button" variant="outline" size="sm" disabled={disabled || busy} onClick={() => inputRef.current?.click()}>Select Documents</Button>
      </div>

      {certificates.length > 0 && (
        <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]" aria-label="Uploaded certificates">
          {certificates.map((item) => (
            <li key={item.fileAssetId} className="flex flex-wrap items-center gap-3 px-3 py-2.5">
              <FileText className="size-4 shrink-0 text-[var(--color-muted-foreground)]" />
              <span className="min-w-0 flex-1 truncate text-[13px] font-medium">{item.fileName}</span>
              <select
                aria-label={`${item.fileName} certificate type`}
                value={item.certificateType}
                disabled={disabled}
                onChange={(event) => onChange(certificates.map((value) => (value.fileAssetId === item.fileAssetId ? { ...value, certificateType: event.target.value as SupplierCertificateType } : value)))}
                className="h-8 rounded-lg border border-[var(--color-input)] bg-[var(--color-card)] px-2 text-[12.5px]"
              >
                {CERTIFICATE_TYPES.map((type) => <option key={type} value={type}>{type}</option>)}
              </select>
              <Button type="button" variant="ghost" size="icon-xs" aria-label={`Download ${item.fileName}`} onClick={() => void download(item.fileAssetId)}><Download /></Button>
              {!disabled && <Button type="button" variant="ghost" size="icon-xs" aria-label={`Remove ${item.fileName}`} onClick={() => onChange(certificates.filter((value) => value.fileAssetId !== item.fileAssetId))}><Trash2 /></Button>}
            </li>
          ))}
        </ul>
      )}
      <p className="text-[11.5px] text-[var(--color-muted-foreground)]">Files up to {formatBytes(MAX_BYTES)} each. Removing a file here detaches it from the supplier when you save.</p>
    </div>
  );
}

const userLabel = (user: UserDto | undefined, id: string) =>
  user ? [user.firstName, user.lastName].filter(Boolean).join(" ") || user.email || user.userName || id : id;

function LinkedUsers({ userIds, onChange, disabled }: { userIds: string[]; onChange: (ids: string[]) => void; disabled: boolean }) {
  const [known, setKnown] = useState<Record<string, UserDto>>({});
  const [pickerKey, setPickerKey] = useState(0);
  const users = useQueries({ queries: userIds.filter((id) => !known[id]).map((id) => ({ queryKey: ["user", id], queryFn: () => getUserById(id), staleTime: 300_000 })) });
  const lookup = (id: string) => known[id] ?? users.find((result) => result.data?.id === id)?.data;
  return (
    <div className="space-y-2">
      {userIds.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {userIds.map((id) => (
            <span key={id} className="inline-flex h-7 items-center gap-1 rounded-full bg-[var(--color-primary-soft)] pl-2.5 pr-1 text-[12px] font-medium text-[var(--color-primary)]">
              {userLabel(lookup(id), id)}
              {!disabled && (
                <button type="button" aria-label={`Unlink ${userLabel(lookup(id), id)}`} onClick={() => onChange(userIds.filter((item) => item !== id))} className="grid size-5 cursor-pointer place-items-center rounded-full hover:bg-[oklch(from_var(--color-primary)_l_c_h_/_0.15)]">
                  <X className="size-3" />
                </button>
              )}
            </span>
          ))}
        </div>
      )}
      {!disabled && (
        <UserPicker
          key={pickerKey}
          value={null}
          placeholder="Search a user to link…"
          onChange={(id, picked) => {
            if (!id) return;
            if (picked) setKnown((value) => ({ ...value, [id]: picked }));
            onChange(userIds.includes(id) ? userIds : [...userIds, id]);
            setPickerKey((value) => value + 1);
          }}
        />
      )}
      <p className="text-[11.5px] text-[var(--color-muted-foreground)]">Users who sign in on behalf of this supplier. Optional.</p>
    </div>
  );
}
