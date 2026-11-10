import { useEffect, useRef, useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { AlertTriangle, BadgeCheck, Building2, ClipboardList, Download, Eye, Factory, ListFilter, Loader2, MoreVertical, Plus, Search, TrendingUp, UsersRound } from "lucide-react";
import { toast } from "sonner";
import {
  getPpapSuppliersSummary,
  searchPpapSuppliers,
  SUPPLIER_GRADES,
  SUPPLIER_REGIONS,
  SUPPLIER_TIERS,
  type PpapSupplierDto,
  type SupplierSearchParams,
} from "@/api/ppap";
import { useAuth } from "@/auth/use-auth";
import { Combobox } from "@/components/list/combobox";
import { EntityEmpty, ErrorBand } from "@/components/list";
import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { NumberedPager, ScoreBar, StatCard, StatusDot } from "@/pages/ppap/directory-ui";
import { downloadCsv } from "@/pages/ppap/directory-utils";

const PAGE_SIZE = 10;
const GRID = "grid-cols-[minmax(14rem,1.6fr)_8rem_minmax(9rem,1fr)_5.5rem_9.5rem_6.5rem_minmax(9rem,1fr)_4rem]";
const UNASSIGNED = "unassigned";

export function PpapSuppliersPage() {
  const { user, permissionsHydrated } = useAuth();
  const perms = user?.permissions ?? [];
  const canView = perms.includes("Permissions.Ppap.Suppliers.View");
  const canCreate = perms.includes("Permissions.Ppap.Suppliers.Create");
  const canCreatePart = perms.includes("Permissions.Ppap.Parts.Create");
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const status = params.get("status") ?? "all";
  const search = params.get("q") ?? "";
  const region = params.get("region");
  const tier = params.get("tier");
  const grade = params.get("grade");
  const page = Math.max(1, Number(params.get("page") ?? 1) || 1);
  const [searchDraft, setSearchDraft] = useState(search);
  const [exporting, setExporting] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);

  const patch = (next: Record<string, string | null>, resetPage = true) => setParams((current) => {
    const updated = new URLSearchParams(current);
    Object.entries(next).forEach(([key, value]) => (value ? updated.set(key, value) : updated.delete(key)));
    if (resetPage) updated.delete("page");
    return updated;
  }, { replace: true });

  const filters: SupplierSearchParams = {
    search: search || undefined,
    isActive: status === "all" ? undefined : status === "active",
    region: region ?? undefined,
    tierLevel: tier ?? undefined,
    qualityGrade: grade ?? undefined,
  };
  const enabled = permissionsHydrated && canView;
  const query = useQuery({
    queryKey: ["ppap", "suppliers", { ...filters, page }],
    queryFn: () => searchPpapSuppliers({ ...filters, pageNumber: page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
    enabled,
  });
  const summary = useQuery({ queryKey: ["ppap", "suppliers", "summary"], queryFn: getPpapSuppliersSummary, enabled });

  if (permissionsHydrated && !canView) return <EntityEmpty icon={Building2} title="Suppliers unavailable" body="You do not have permission to view suppliers." />;
  const items = query.data?.items ?? [];
  const filtered = Boolean(search || region || tier || grade || status !== "all");

  const exportCsv = async () => {
    setExporting(true);
    try {
      const rows: PpapSupplierDto[] = [];
      for (let next = 1, pages = 1; next <= pages && next <= 25; next++) {
        const result = await searchPpapSuppliers({ ...filters, pageNumber: next, pageSize: 200 });
        rows.push(...result.items);
        pages = result.totalPages;
      }
      downloadCsv(`suppliers-${new Date().toISOString().slice(0, 10)}.csv`,
        ["Vendor code", "Company name", "City", "Country", "Region", "Tier", "Quality grade", "Overall score %", "Projects", "Active PPAPs", "Status", "Primary contact", "Job title", "Email", "Phone", "DUNS"],
        rows.map((item) => [item.code, item.name, item.city, item.country, item.region, item.tierLevel, item.qualityGrade, item.qualityScore, item.partCount, item.activePpapCount, item.isActive ? "Active" : "Inactive", item.contactName, item.contactJobTitle, item.email, item.phone, item.dunsNumber]));
      toast.success(`Exported ${rows.length} suppliers`);
    } catch (error) {
      toast.error("Export failed", { description: describe(error) });
    } finally {
      setExporting(false);
    }
  };

  const s = summary.data;
  const growth = s && s.totalSuppliers - s.newThisMonth > 0 ? Math.round((s.newThisMonth / (s.totalSuppliers - s.newThisMonth)) * 100) : null;

  return (
    <div className="fsh-enter space-y-6">
      <header className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="font-display text-[26px] font-semibold tracking-tight">Supplier Directory</h1>
          <p className="mt-1 text-[14px] text-[var(--color-muted-foreground)]">Manage global automotive supply chain partners and quality compliance.</p>
        </div>
        {canCreate && <Button size="lg" asChild><Link to="/ppap/suppliers/new"><Plus className="size-4" />Add New Supplier</Link></Button>}
      </header>

      <div className="flex flex-wrap items-center gap-3 rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-4 shadow-xs">
        <div className="relative min-w-[16rem] flex-[2]">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--color-muted-foreground)]" />
          <input
            aria-label="Search suppliers"
            value={searchDraft}
            placeholder="Search by name or vendor code…"
            onChange={(event) => {
              const value = event.target.value;
              setSearchDraft(value);
              clearTimeout(timer.current);
              timer.current = setTimeout(() => patch({ q: value.trim() || null }), 300);
            }}
            className="h-10 w-full rounded-lg border border-[var(--color-input)] bg-transparent pl-9 pr-3 text-sm outline-none focus:border-[oklch(from_var(--color-ring)_l_c_h_/_0.5)]"
          />
        </div>
        <Combobox label="Region" className="h-10 min-w-[11rem]" value={region} onChange={(value) => patch({ region: value })} emptyOptionLabel="All Regions" placeholder="All Regions" options={SUPPLIER_REGIONS.map((value) => ({ value, label: value }))} />
        <Combobox label="Tier level" className="h-10 min-w-[10rem]" value={tier} onChange={(value) => patch({ tier: value })} emptyOptionLabel="All tiers" placeholder="Tier Level" options={[...SUPPLIER_TIERS.map((value) => ({ value, label: value })), { value: UNASSIGNED, label: "Pending assignment" }]} />
        <Combobox label="Quality grade" className="h-10 min-w-[10rem]" value={grade} onChange={(value) => patch({ grade: value })} emptyOptionLabel="All grades" placeholder="Quality Grade" options={[...SUPPLIER_GRADES.map((value) => ({ value, label: `Grade ${value}` })), { value: UNASSIGNED, label: "Not graded" }]} />
        <div className="ml-auto flex items-center gap-2">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="icon" aria-label="Status filter" className={cn("size-10", status !== "all" && "border-[var(--color-primary)] text-[var(--color-primary)]")}><ListFilter className="size-4" /></Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuLabel>Status</DropdownMenuLabel>
              {(["all", "active", "inactive"] as const).map((value) => (
                <DropdownMenuItem key={value} onSelect={() => patch({ status: value === "all" ? null : value })} className={cn(status === value && "font-semibold text-[var(--color-primary)]")}>
                  {value === "all" ? "All suppliers" : value === "active" ? "Active only" : "Inactive only"}
                </DropdownMenuItem>
              ))}
              {filtered && <><DropdownMenuSeparator /><DropdownMenuItem onSelect={() => { setSearchDraft(""); setParams({}, { replace: true }); }}>Clear all filters</DropdownMenuItem></>}
            </DropdownMenuContent>
          </DropdownMenu>
          <Button variant="outline" size="icon" aria-label="Export suppliers to CSV" className="size-10" disabled={exporting || !query.data?.totalCount} onClick={() => void exportCsv()}>
            {exporting ? <Loader2 className="size-4 animate-spin" /> : <Download className="size-4" />}
          </Button>
        </div>
      </div>

      {query.isError && <ErrorBand message={describe(query.error)} />}

      <section aria-label="Suppliers" className={cn("overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs", query.isPlaceholderData && "opacity-70")}>
        <div className="overflow-x-auto">
          <div className="min-w-[68rem]">
            <div role="row" className={cn("grid items-center gap-4 border-b border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] px-5 py-3 text-[11px] font-semibold uppercase tracking-[0.1em] text-[var(--color-muted-foreground)]", GRID)}>
              <span>Company name</span><span>Vendor code</span><span>Location</span><span>Projects</span><span>Overall score</span><span>Status</span><span>Primary contact</span><span className="text-right">Actions</span>
            </div>
            {query.isLoading && <div role="status" aria-busy="true" className="space-y-2 p-4"><span className="sr-only">Loading suppliers…</span>{Array.from({ length: 5 }).map((_, index) => <Skeleton key={index} className="h-14 rounded-xl" />)}</div>}
            {!query.isLoading && !query.isError && items.length === 0 && (
              <div className="p-6"><EntityEmpty icon={Building2} title={filtered ? "No suppliers match these filters" : "No suppliers yet"} body={filtered ? "Try a different search or clear the filters." : "Onboard suppliers so parts can be assigned to them with the right template."} /></div>
            )}
            {items.map((supplier) => (
              <div key={supplier.id} role="row" className={cn("grid items-center gap-4 border-b border-[var(--color-border)] px-5 py-4 last:border-b-0 hover:bg-[oklch(from_var(--color-muted)_l_c_h_/_0.3)]", GRID, !supplier.isActive && "opacity-75")}>
                <span className="flex min-w-0 items-center gap-3">
                  <span className="grid size-9 shrink-0 place-items-center rounded-lg bg-[var(--color-muted)] text-[var(--color-muted-foreground)]"><Factory className="size-4" /></span>
                  <span className="min-w-0">
                    <Link to={`/ppap/suppliers/${supplier.id}`} className="block truncate text-[14.5px] font-semibold text-[var(--color-primary)] hover:underline">{supplier.name}</Link>
                    {(supplier.tierLevel || supplier.qualityGrade) && (
                      <span className="block truncate text-[11.5px] text-[var(--color-muted-foreground)]">{[supplier.tierLevel, supplier.qualityGrade && `Grade ${supplier.qualityGrade}`].filter(Boolean).join(" · ")}</span>
                    )}
                  </span>
                </span>
                <code className="truncate font-mono text-[12.5px]">{supplier.code}</code>
                <span className="text-[13px] leading-snug">{[supplier.city, supplier.country].filter(Boolean).join(", ") || <span className="text-[var(--color-muted-foreground)]">—</span>}</span>
                <span><span className="inline-grid h-7 min-w-9 place-items-center rounded-full bg-[var(--color-muted)] px-2 font-mono text-[12px] font-semibold" title={`${supplier.activePpapCount ?? 0} active PPAPs`}>{supplier.partCount}</span></span>
                <ScoreBar value={supplier.qualityScore} label={`${supplier.name} overall score`} />
                <span>{supplier.isActive ? <StatusDot tone="success">Active</StatusDot> : <StatusDot tone="muted">Inactive</StatusDot>}</span>
                <span className="min-w-0 text-[13px] leading-snug">
                  <span className="block truncate">{supplier.contactName || <span className="text-[var(--color-muted-foreground)]">—</span>}</span>
                  {supplier.contactJobTitle && <span className="block truncate text-[11.5px] text-[var(--color-muted-foreground)]">{supplier.contactJobTitle}</span>}
                </span>
                <span className="flex justify-end">
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button variant="ghost" size="icon-sm" aria-label={`Actions for ${supplier.name}`}><MoreVertical className="size-4" /></Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem onSelect={() => navigate(`/ppap/suppliers/${supplier.id}`)}><Eye className="size-4" />View profile</DropdownMenuItem>
                      <DropdownMenuItem onSelect={() => navigate(`/ppap/parts?supplier=${supplier.id}`)}><ClipboardList className="size-4" />View parts</DropdownMenuItem>
                      {canCreatePart && supplier.isActive && <DropdownMenuItem onSelect={() => navigate(`/ppap/parts/new?supplier=${supplier.id}`)}><Plus className="size-4" />Create part</DropdownMenuItem>}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </span>
              </div>
            ))}
          </div>
        </div>
        {query.data && query.data.totalCount > 0 && (
          <NumberedPager page={query.data.pageNumber} totalPages={query.data.totalPages} total={query.data.totalCount} pageSize={PAGE_SIZE} unit="suppliers" onPage={(next) => patch({ page: next > 1 ? String(next) : null }, false)} />
        )}
      </section>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label="Total Suppliers" icon={UsersRound} tone="primary" value={s ? s.totalSuppliers.toLocaleString() : "—"}
          footnote={growth !== null ? <span className="inline-flex items-center gap-1 text-[var(--color-success)]"><TrendingUp className="size-3.5" />+{growth}% from last month</span> : <span className="text-[var(--color-muted-foreground)]">{s ? `${s.activeSuppliers} active` : "\u00a0"}</span>} />
        <StatCard label="Average Quality Score" icon={BadgeCheck} tone="success" value={s?.averageQualityScore != null ? `${s.averageQualityScore.toFixed(1)}%` : "—"}
          footnote={<span className="text-[var(--color-muted-foreground)]">{s?.averageQualityScore != null ? "Weighted verification pass rate" : "No verified submissions yet"}</span>} />
        <StatCard label="Active PPAPs" icon={ClipboardList} tone="warning" value={s ? s.activePpaps.toLocaleString() : "—"}
          footnote={<span className="text-[var(--color-primary)]">Across {s?.regionCount ?? 0} region{s?.regionCount === 1 ? "" : "s"}</span>} />
        <StatCard label="Critical Issues" icon={AlertTriangle} tone="danger" value={s ? s.criticalIssues.toLocaleString() : "—"}
          footnote={<span className={s?.criticalIssues ? "text-[var(--color-destructive)]" : "text-[var(--color-muted-foreground)]"}>{s?.criticalIssues ? "Requires immediate attention" : "No open verification failures"}</span>} />
      </div>
    </div>
  );
}
