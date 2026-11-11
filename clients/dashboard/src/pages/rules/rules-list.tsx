import { useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, Navigate, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { Archive, ArchiveRestore, Copy, Filter, Hash, LayoutGrid, MessageSquareText, MoreHorizontal, Plus, RefreshCw, Rows3, Search, SquarePen, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { deleteRule, isRuleDeletable, restoreRule, searchRules, type RuleArchiveFilter, type RuleCategory, type RuleDefinitionSummaryDto, type RuleLifecycle, type RuleSort } from "@/api/rules";
import { useAuth } from "@/auth/use-auth";
import { Combobox, EntityEmpty, EntityListCard, EntityListHeader, EntityListRow, EntityPageHeader, EntityPager, ErrorBand } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { CATEGORY_LABELS, LIFECYCLES, LIFECYCLE_LABELS, RULE_TYPES, SEVERITIES, SEVERITY_STRIPE, outcomeSummary, ruleTypeByKey, type RuleTypeConfig, type Severity } from "./rule-model";
import { LifecycleBadge, SeverityBadge } from "./rule-ui";

const PAGE_SIZE = 24;
const VIEW_KEY = "des.dashboard.rules.view";
const SORTS: ReadonlyArray<{ value: RuleSort; label: string }> = [
  { value: "name", label: "Name A-Z" },
  { value: "-name", label: "Name Z-A" },
  { value: "-updated", label: "Recently updated" },
  { value: "-created", label: "Newest" },
  { value: "created", label: "Oldest" },
];
const LIST_COLUMNS = "grid-cols-[minmax(8rem,0.9fr)_minmax(14rem,2fr)_7rem_6rem_7rem_7rem_2rem]";
const ARCHIVE_OPTIONS: ReadonlyArray<{ value: Exclude<RuleArchiveFilter, "Exclude">; label: string }> = [
  { value: "Only", label: "Archived only" },
  { value: "Include", label: "Active + archived" },
];

type View = "grid" | "list";

export function RulesListPage() {
  const { type: typeKey } = useParams();
  const type = ruleTypeByKey(typeKey);
  if (!type) return <Navigate to="/rules/validation" replace />;
  return <RulesLibrary key={type.key} type={type} />;
}

function RulesLibrary({ type }: { type: RuleTypeConfig }) {
  const { user, permissionsHydrated } = useAuth();
  const navigate = useNavigate();
  const perms = user?.permissions ?? [];
  const canView = perms.includes("Permissions.Definitions.View");
  const canCreate = perms.includes("Permissions.Definitions.Create");
  const canDelete = perms.includes("Permissions.Definitions.Delete");
  const queryClient = useQueryClient();
  const [deleting, setDeleting] = useState<RuleDefinitionSummaryDto | null>(null);
  const remove = useMutation({
    mutationFn: (rule: RuleDefinitionSummaryDto) => deleteRule(rule.id, rule.version),
    onSuccess: (result, rule) => {
      if (result?.outcome === "Archived") {
        toast.success(`Rule ${rule.stableCode} archived`, { description: "It has evaluation history, so it was archived instead of deleted. Restore it from the Archived filter." });
        void queryClient.invalidateQueries({ queryKey: ["rule", rule.id] });
      } else {
        toast.success(`Rule ${rule.stableCode} deleted`);
        queryClient.removeQueries({ queryKey: ["rule", rule.id] });
      }
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: ["rules"] });
      void queryClient.invalidateQueries({ queryKey: ["rule-lookup"] });
    },
    onError: (error) => toast.error("Delete failed", { description: describe(error) }),
  });
  const restore = useMutation({
    mutationFn: (rule: RuleDefinitionSummaryDto) => restoreRule(rule.id, rule.version),
    onSuccess: (_result, rule) => {
      toast.success(`Rule ${rule.stableCode} restored`);
      void queryClient.invalidateQueries({ queryKey: ["rules"] });
      void queryClient.invalidateQueries({ queryKey: ["rule", rule.id] });
      void queryClient.invalidateQueries({ queryKey: ["rule-lookup"] });
    },
    onError: (error) => toast.error("Restore failed", { description: describe(error) }),
  });
  const onDelete = canDelete ? setDeleting : null;
  const onRestore = canDelete ? (rule: RuleDefinitionSummaryDto) => restore.mutate(rule) : null;
  const [params, setParams] = useSearchParams();
  const [view, setView] = useState<View>(() => (localStorage.getItem(VIEW_KEY) === "list" ? "list" : "grid"));
  const searchRef = useRef<HTMLInputElement>(null);

  const search = params.get("q") ?? "";
  const status = (params.get("status") ?? "") as RuleLifecycle | "";
  const severity = (params.get("severity") ?? "") as Severity | "";
  const scope = (params.get("scope") ?? "") as RuleCategory | "";
  const tag = params.get("tag") ?? "";
  const archivedParam = params.get("archived");
  const archived = archivedParam === "Only" || archivedParam === "Include" ? archivedParam : "";
  const sort = (params.get("sort") ?? "name") as RuleSort;
  const page = Math.max(1, Number(params.get("page") ?? 1) || 1);
  const [searchDraft, setSearchDraft] = useState(search);

  const patch = (next: Record<string, string | null>, resetPage = true) => {
    setParams((current) => {
      const updated = new URLSearchParams(current);
      Object.entries(next).forEach(([key, value]) => (value ? updated.set(key, value) : updated.delete(key)));
      if (resetPage) updated.delete("page");
      return updated;
    }, { replace: true });
  };

  const searchTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const changeSearch = (value: string) => {
    setSearchDraft(value);
    clearTimeout(searchTimer.current);
    searchTimer.current = setTimeout(() => patch({ q: value.trim() || null }), 300);
  };
  useEffect(() => () => clearTimeout(searchTimer.current), []);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (target && (target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName))) return;
      if (event.metaKey || event.ctrlKey || event.altKey) return;
      if (event.key === "/") {
        event.preventDefault();
        searchRef.current?.focus();
      } else if (event.key.toLowerCase() === "n" && canCreate) {
        event.preventDefault();
        navigate(`/rules/${type.key}/new`);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [canCreate, navigate, type.key]);

  const changeView = (next: View) => {
    setView(next);
    localStorage.setItem(VIEW_KEY, next);
  };

  const categories = scope ? [scope] : type.categories;
  const query = useQuery({
    queryKey: ["rules", { type: type.key, page, search, status, severity, scope, tag, archived, sort }],
    queryFn: () => searchRules({ categories, pageNumber: page, pageSize: PAGE_SIZE, search: search || undefined, status: status || undefined, severity: severity || undefined, tag: tag || undefined, archived: archived || undefined, sort }),
    placeholderData: keepPreviousData,
    enabled: permissionsHydrated && canView,
  });

  const counts = useQueries({
    queries: RULE_TYPES.map((item) => ({
      queryKey: ["rules", "count", item.key],
      queryFn: () => searchRules({ categories: item.categories, pageNumber: 1, pageSize: 1 }),
      enabled: permissionsHydrated && canView,
      staleTime: 60_000,
    })),
  });

  const activeFilters = [status, severity, scope, tag, archived, search].filter(Boolean).length;
  const items = query.data?.items ?? [];

  if (permissionsHydrated && !canView) {
    return <EntityEmpty icon={type.icon} title="Rules unavailable" body="You do not have permission to view rule definitions." />;
  }

  return (
    <div className="space-y-6 fsh-enter">
      <EntityPageHeader icon={type.icon} title={type.plural} total={query.data?.totalCount ?? null} unit="rule" description={type.description}>
        <Button variant="outline" onClick={() => void query.refetch()} disabled={query.isFetching} aria-label="Refresh rules">
          <RefreshCw className={cn("size-4", query.isFetching && "animate-spin")} />
        </Button>
        {canCreate && (
          <Button asChild>
            <Link to={`/rules/${type.key}/new`}><Plus className="size-4" />New {type.label.toLowerCase()}</Link>
          </Button>
        )}
      </EntityPageHeader>

      <nav aria-label="Rule libraries" className="-mx-1 flex gap-1 overflow-x-auto px-1 pb-1">
        {RULE_TYPES.map((item, index) => {
          const Icon = item.icon;
          const active = item.key === type.key;
          const total = counts[index]?.data?.totalCount;
          return (
            <Link
              key={item.key}
              to={`/rules/${item.key}`}
              aria-current={active ? "page" : undefined}
              className={cn(
                "group inline-flex h-10 shrink-0 items-center gap-2 rounded-xl border px-3.5 text-[13px] font-medium transition-all duration-[var(--duration-fast)]",
                active
                  ? "border-[oklch(from_var(--color-primary)_l_c_h_/_0.3)] bg-[var(--color-primary-soft)] text-[var(--color-primary)] shadow-xs"
                  : "border-[var(--color-border)] bg-[var(--color-card)] text-[var(--color-muted-foreground)] hover:text-[var(--color-foreground)]",
              )}
            >
              <Icon className="size-4" />
              {item.plural}
              <span className={cn("rounded-full px-1.5 font-mono text-[10.5px]", active ? "bg-[oklch(from_var(--color-primary)_l_c_h_/_0.15)]" : "bg-[var(--color-muted)]")}>
                {total ?? "–"}
              </span>
            </Link>
          );
        })}
      </nav>

      <div className="space-y-3 rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-3 shadow-xs">
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative min-w-[16rem] flex-1">
            <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--color-muted-foreground)]" />
            <input
              ref={searchRef}
              aria-label="Search rules"
              value={searchDraft}
              onChange={(event) => changeSearch(event.target.value)}
              onKeyDown={(event: ReactKeyboardEvent<HTMLInputElement>) => event.key === "Escape" && changeSearch("")}
              placeholder="Search by code or name"
              className="h-10 w-full rounded-xl border border-[var(--color-border)] bg-transparent pl-9 pr-12 text-sm outline-none transition-shadow focus:border-[oklch(from_var(--color-ring)_l_c_h_/_0.4)] focus:ring-2 focus:ring-[oklch(from_var(--color-ring)_l_c_h_/_0.12)]"
            />
            <kbd className="pointer-events-none absolute right-3 top-1/2 hidden -translate-y-1/2 rounded border border-[var(--color-border)] px-1.5 font-mono text-[10px] text-[var(--color-muted-foreground)] sm:block">/</kbd>
          </div>
          <div className="w-[11rem]">
            <Combobox label="Sort" value={sort} onChange={(value) => patch({ sort: value && value !== "name" ? value : null })} options={[...SORTS]} />
          </div>
          <div className="flex items-center rounded-lg border border-[var(--color-border)] p-0.5" role="radiogroup" aria-label="View">
            {([["grid", LayoutGrid], ["list", Rows3]] as const).map(([value, Icon]) => (
              <button
                key={value}
                type="button"
                role="radio"
                aria-checked={view === value}
                aria-label={value === "grid" ? "Grid view" : "List view"}
                onClick={() => changeView(value)}
                className={cn("grid size-8 cursor-pointer place-items-center rounded-md transition-colors", view === value ? "bg-[var(--color-primary-soft)] text-[var(--color-primary)]" : "text-[var(--color-muted-foreground)] hover:text-[var(--color-foreground)]")}
              >
                <Icon className="size-4" />
              </button>
            ))}
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Filter aria-hidden className="size-3.5 text-[var(--color-muted-foreground)]" />
          <Combobox variant="filter" label="Status" value={status || null} clearable onChange={(value) => patch({ status: value })} options={LIFECYCLES.map((value) => ({ value, label: LIFECYCLE_LABELS[value] }))} />
          <Combobox variant="filter" label="Severity" value={severity || null} clearable onChange={(value) => patch({ severity: value })} options={SEVERITIES.map((value) => ({ value, label: value }))} />
          {type.categories.length > 1 && (
            <Combobox variant="filter" label="Scope" value={scope || null} clearable onChange={(value) => patch({ scope: value })} options={type.categories.map((value) => ({ value, label: CATEGORY_LABELS[value] }))} />
          )}
          <Combobox variant="filter" label="Archived" value={archived || null} clearable onChange={(value) => patch({ archived: value })} options={[...ARCHIVE_OPTIONS]} />
          {tag && (
            <span className="inline-flex h-7 items-center gap-1 rounded-full border border-[oklch(from_var(--color-primary)_l_c_h_/_0.25)] bg-[oklch(from_var(--color-primary)_l_c_h_/_0.10)] pl-3 pr-1 text-[11px] font-semibold uppercase tracking-wider text-[var(--color-primary)]">
              Tag: #{tag}
              <button type="button" aria-label="Clear tag filter" onClick={() => patch({ tag: null })} className="grid size-5 cursor-pointer place-items-center rounded-full hover:bg-[oklch(from_var(--color-primary)_l_c_h_/_0.15)]"><X className="size-3" /></button>
            </span>
          )}
          {activeFilters > 0 && (
            <Button variant="ghost" size="xs" onClick={() => { clearTimeout(searchTimer.current); setSearchDraft(""); setParams(new URLSearchParams(sort !== "name" ? { sort } : {}), { replace: true }); }}>
              Clear all
            </Button>
          )}
          <span className="ml-auto hidden text-[11px] text-[var(--color-muted-foreground)] md:inline">
            Press <kbd className="rounded border border-[var(--color-border)] px-1 font-mono">/</kbd> to search{canCreate && <> · <kbd className="rounded border border-[var(--color-border)] px-1 font-mono">N</kbd> for a new rule</>}
          </span>
        </div>
      </div>

      {query.isError && <ErrorBand message={describe(query.error)} />}
      {query.isLoading && <LoadingGrid view={view} />}
      {!query.isLoading && !query.isError && items.length === 0 && (
        <EntityEmpty
          icon={type.icon}
          title={activeFilters ? "No rules match these filters" : `No ${type.plural.toLowerCase()} yet`}
          body={activeFilters ? "Try clearing a filter or searching for a different code." : type.description}
          action={canCreate && !activeFilters ? <Button asChild><Link to={`/rules/${type.key}/new`}><Plus className="size-4" />Create the first {type.label.toLowerCase()}</Link></Button> : undefined}
        />
      )}

      {items.length > 0 && view === "grid" && (
        <ul className={cn("grid gap-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4", query.isPlaceholderData && "opacity-70")}>
          {items.map((rule) => (
            <li key={rule.id}>
              <RuleCard rule={rule} type={type} canCreate={canCreate} onDelete={onDelete} onRestore={onRestore} onTag={(value) => patch({ tag: value })} />
            </li>
          ))}
        </ul>
      )}

      {items.length > 0 && view === "list" && (
        <EntityListCard className={cn(query.isPlaceholderData && "opacity-70")}>
          <div className="overflow-x-auto">
            <div className="min-w-[56rem]">
              <EntityListHeader className={LIST_COLUMNS}>
                <span>Code</span><span>Rule</span><span>Severity</span><span>Conditions</span><span>Status</span><span>Updated</span><span className="sr-only">Actions</span>
              </EntityListHeader>
              {items.map((rule, index) => (
                <RuleRow key={rule.id} rule={rule} type={type} isLast={index === items.length - 1} canCreate={canCreate} onDelete={onDelete} onRestore={onRestore} />
              ))}
            </div>
          </div>
        </EntityListCard>
      )}

      {query.data && (
        <EntityPager
          page={query.data.pageNumber}
          totalPages={query.data.totalPages}
          hasPrev={query.data.hasPrevious}
          hasNext={query.data.hasNext}
          onPrev={() => patch({ page: page > 2 ? String(page - 1) : null }, false)}
          onNext={() => patch({ page: String(page + 1) }, false)}
        />
      )}

      <Dialog open={deleting !== null} onOpenChange={(open) => !open && !remove.isPending && setDeleting(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Delete rule</DialogTitle>
            <DialogDescription>
              Delete <strong>{deleting?.name}</strong> (<code className="font-mono">{deleting?.stableCode}</code>)? It is removed from templates and document settings. If the rule has evaluation history it is archived instead, so past results stay intact and it can be restored later. Otherwise it is permanently deleted with all its versions.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleting(null)} disabled={remove.isPending}>Cancel</Button>
            <Button variant="destructive" disabled={remove.isPending} onClick={() => deleting && remove.mutate(deleting)}>{remove.isPending ? "Deleting…" : "Delete"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function useRuleMeta(rule: RuleDefinitionSummaryDto) {
  return useMemo(() => outcomeSummary(rule.latestOutcomeJson), [rule.latestOutcomeJson]);
}

type OnDelete = ((rule: RuleDefinitionSummaryDto) => void) | null;

function ArchivedBadge() {
  return (
    <span className="inline-flex h-5 items-center gap-1 rounded-full bg-[var(--color-muted)] px-2 text-[10.5px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">
      <Archive className="size-3" />Archived
    </span>
  );
}

function RuleMenu({ rule, type, canCreate, onDelete, onRestore }: { rule: RuleDefinitionSummaryDto; type: RuleTypeConfig; canCreate: boolean; onDelete: OnDelete; onRestore: OnDelete }) {
  const navigate = useNavigate();
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          aria-label={`Actions for ${rule.stableCode}`}
          onClick={(event) => event.stopPropagation()}
          className="grid size-7 cursor-pointer place-items-center rounded-md text-[var(--color-muted-foreground)] transition-colors hover:bg-[var(--color-accent)] hover:text-[var(--color-foreground)]"
        >
          <MoreHorizontal className="size-4" />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" onClick={(event) => event.stopPropagation()}>
        <DropdownMenuItem onSelect={() => navigate(`/rules/${type.key}/${rule.id}`)}><SquarePen className="size-4" />Open</DropdownMenuItem>
        {canCreate && <DropdownMenuItem onSelect={() => navigate(`/rules/${type.key}/new?from=${rule.id}`)}><Copy className="size-4" />Duplicate</DropdownMenuItem>}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => { void navigator.clipboard?.writeText(rule.stableCode); toast.success("Rule code copied"); }}><Hash className="size-4" />Copy code</DropdownMenuItem>
        {onRestore && rule.isArchived && (
          <>
            <DropdownMenuSeparator />
            <DropdownMenuItem onSelect={() => onRestore(rule)}><ArchiveRestore className="size-4" />Restore</DropdownMenuItem>
          </>
        )}
        {onDelete && isRuleDeletable(rule) && (
          <>
            <DropdownMenuSeparator />
            <DropdownMenuItem destructive onSelect={() => onDelete(rule)}><Trash2 className="size-4" />Delete</DropdownMenuItem>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function RuleCard({ rule, type, canCreate, onDelete, onRestore, onTag }: { rule: RuleDefinitionSummaryDto; type: RuleTypeConfig; canCreate: boolean; onDelete: OnDelete; onRestore: OnDelete; onTag: (tag: string) => void }) {
  const meta = useRuleMeta(rule);
  const href = `/rules/${type.key}/${rule.id}`;
  return (
    <article
      data-testid="rule-card"
      className={cn(
        "group relative flex h-full flex-col overflow-hidden rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs",
        "transition-all duration-[var(--duration-default)] hover:-translate-y-0.5 hover:shadow-md focus-within:ring-2 focus-within:ring-[var(--color-ring)]",
        (rule.lifecycle === "Retired" || rule.isArchived) && "opacity-70",
      )}
    >
      <span aria-hidden className={cn("absolute inset-y-0 left-0 w-1", SEVERITY_STRIPE[meta.severity])} />
      <div className="flex items-start justify-between gap-2 px-4 pb-2 pl-5 pt-4">
        <div className="flex min-w-0 flex-wrap items-center gap-1.5">
          <code className="truncate rounded-md bg-[var(--color-muted)] px-1.5 py-0.5 font-mono text-[11px] font-semibold text-[var(--color-foreground)]">{rule.stableCode}</code>
          <SeverityBadge severity={meta.severity} />
          {rule.isArchived && <ArchivedBadge />}
        </div>
        <div className="relative z-[1]"><RuleMenu rule={rule} type={type} canCreate={canCreate} onDelete={onDelete} onRestore={onRestore} /></div>
      </div>
      <Link to={href} className="flex-1 px-4 pl-5 outline-none after:absolute after:inset-0 after:content-['']">
        <h3 className="line-clamp-1 font-display text-[15px] font-semibold text-[var(--color-foreground)]">{rule.name}</h3>
        <p className="mt-1 line-clamp-2 min-h-[2.5rem] text-[12.5px] leading-relaxed text-[var(--color-muted-foreground)]">{rule.description || "No description"}</p>
      </Link>
      {meta.tags.length > 0 && (
        <div className="relative z-[1] flex flex-wrap gap-1 px-4 pl-5 pt-2">
          {meta.tags.slice(0, 3).map((value) => (
            <button key={value} type="button" onClick={() => onTag(value)} className="cursor-pointer rounded-md bg-[var(--color-primary-soft)] px-1.5 py-0.5 text-[11px] font-medium text-[var(--color-primary)] hover:brightness-95" title={`Filter by #${value}`}>
              #{value}
            </button>
          ))}
          {meta.tags.length > 3 && <span className="px-1 text-[11px] text-[var(--color-muted-foreground)]">+{meta.tags.length - 3}</span>}
        </div>
      )}
      <footer className="mt-3 flex items-center gap-3 border-t border-[oklch(from_var(--color-border)_l_c_h_/_0.6)] px-4 py-2.5 pl-5 text-[11.5px] text-[var(--color-muted-foreground)]">
        <span className="inline-flex items-center gap-1" title="Conditions"><Filter className="size-3.5" />{rule.conditionCount ?? 0}</span>
        <span className="inline-flex items-center gap-1" title="Messages"><MessageSquareText className="size-3.5" />{meta.messageCount}</span>
        {type.categories.length > 1 && <span className="truncate">{CATEGORY_LABELS[rule.category]}</span>}
        <span className="ml-auto flex items-center gap-2">
          <LifecycleBadge status={rule.lifecycle} />
          <span className="font-mono">v{rule.version}</span>
        </span>
      </footer>
    </article>
  );
}

function RuleRow({ rule, type, isLast, canCreate, onDelete, onRestore }: { rule: RuleDefinitionSummaryDto; type: RuleTypeConfig; isLast: boolean; canCreate: boolean; onDelete: OnDelete; onRestore: OnDelete }) {
  const meta = useRuleMeta(rule);
  const navigate = useNavigate();
  return (
    <EntityListRow className={LIST_COLUMNS} isLast={isLast} dim={rule.lifecycle === "Retired" || Boolean(rule.isArchived)} onClick={() => navigate(`/rules/${type.key}/${rule.id}`)}>
      <code className="flex min-w-0 items-center gap-2 font-mono text-[12px] font-semibold">
        <span aria-hidden className={cn("h-5 w-1 shrink-0 rounded-full", SEVERITY_STRIPE[meta.severity])} />
        <span className="truncate">{rule.stableCode}</span>
      </code>
      <span className="min-w-0">
        <span className="block truncate text-sm font-medium">{rule.name}</span>
        <span className="block truncate text-[12px] text-[var(--color-muted-foreground)]">{rule.description || "No description"}</span>
      </span>
      <span><SeverityBadge severity={meta.severity} /></span>
      <span className="font-mono text-[12px] text-[var(--color-muted-foreground)]">{rule.conditionCount ?? 0}</span>
      <span>{rule.isArchived ? <ArchivedBadge /> : <LifecycleBadge status={rule.lifecycle} />}</span>
      <span className="text-[12px] text-[var(--color-muted-foreground)]">{new Date(rule.updatedAtUtc).toLocaleDateString()}</span>
      <span className="flex justify-end"><RuleMenu rule={rule} type={type} canCreate={canCreate} onDelete={onDelete} onRestore={onRestore} /></span>
    </EntityListRow>
  );
}

function LoadingGrid({ view }: { view: View }) {
  return (
    <div role="status" aria-busy="true" className={view === "grid" ? "grid gap-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4" : "space-y-2"}>
      <span className="sr-only">Loading rules…</span>
      {Array.from({ length: view === "grid" ? 8 : 6 }).map((_, index) => (
        <Skeleton key={index} className={view === "grid" ? "h-[11.5rem] rounded-2xl" : "h-14 rounded-xl"} />
      ))}
    </div>
  );
}
