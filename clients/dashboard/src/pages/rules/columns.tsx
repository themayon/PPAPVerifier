import { useEffect, useRef, useState, type FormEvent } from "react";
import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { Columns3, MoreHorizontal, Pencil, Plus, Power, RefreshCw, Search, Trash2 } from "lucide-react";
import { toast } from "sonner";
import {
  createRuleColumn,
  deleteRuleColumn,
  searchRuleColumns,
  updateRuleColumn,
  type RuleColumnDataType,
  type RuleColumnDto,
  type RuleColumnKind,
} from "@/api/rules";
import { useAuth } from "@/auth/use-auth";
import { Combobox, EntityEmpty, EntityListCard, EntityListHeader, EntityListRow, EntityPageHeader, EntityPager, EntityStatusBadge, ErrorBand, Field } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { Segmented } from "./rule-ui";

const PAGE_SIZE = 50;
const DATA_TYPES: readonly RuleColumnDataType[] = ["Text", "Number", "Date", "Boolean"];
const KINDS: ReadonlyArray<{ value: RuleColumnKind; label: string; description: string }> = [
  { value: "Column", label: "Columns", description: "Table columns found in document rows, e.g. Severity, Target Date." },
  { value: "Header", label: "Header columns", description: "Document header fields, e.g. Part Number, Revision Date." },
];
const COLUMNS_GRID = "grid-cols-[minmax(11rem,1.1fr)_minmax(9rem,0.8fr)_6.5rem_minmax(12rem,1.6fr)_6rem_2rem]";
const textareaClass = "w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 text-sm shadow-xs outline-none placeholder:text-[var(--color-muted-foreground)] focus-visible:border-[var(--color-ring)] focus-visible:ring-[3px] focus-visible:ring-[oklch(from_var(--color-ring)_l_c_h_/_0.5)]";
const KEY_PATTERN = /^[A-Za-z0-9_-]{1,64}(\.[A-Za-z0-9_-]{1,64})*$/;

type Editing = { mode: "create"; kind: RuleColumnKind } | { mode: "edit"; column: RuleColumnDto };

export function RuleColumnsPage() {
  const { user, permissionsHydrated } = useAuth();
  const perms = user?.permissions ?? [];
  const canView = perms.includes("Permissions.RuleColumns.View");
  const canCreate = perms.includes("Permissions.RuleColumns.Create");
  const canEdit = perms.includes("Permissions.RuleColumns.Update");
  const canDelete = perms.includes("Permissions.RuleColumns.Delete");
  const queryClient = useQueryClient();
  const [params, setParams] = useSearchParams();
  const kind = (params.get("kind") === "Header" ? "Header" : "Column") as RuleColumnKind;
  const dataType = (params.get("type") ?? "") as RuleColumnDataType | "";
  const status = params.get("status") ?? "active";
  const search = params.get("q") ?? "";
  const page = Math.max(1, Number(params.get("page") ?? 1) || 1);
  const [searchDraft, setSearchDraft] = useState(search);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [deleting, setDeleting] = useState<RuleColumnDto | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);

  const patch = (next: Record<string, string | null>, resetPage = true) => setParams((current) => {
    const updated = new URLSearchParams(current);
    Object.entries(next).forEach(([key, value]) => (value ? updated.set(key, value) : updated.delete(key)));
    if (resetPage) updated.delete("page");
    return updated;
  }, { replace: true });

  const isActive = status === "all" ? undefined : status === "active";
  const query = useQuery({
    queryKey: ["rule-columns", { kind, dataType, status, search, page }],
    queryFn: () => searchRuleColumns({ kind, dataType: dataType || undefined, isActive, search: search || undefined, pageNumber: page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
    enabled: permissionsHydrated && canView,
  });
  const counts = useQueries({
    queries: KINDS.map((item) => ({
      queryKey: ["rule-columns", "count", item.value],
      queryFn: () => searchRuleColumns({ kind: item.value, isActive: true, pageSize: 1 }),
      enabled: permissionsHydrated && canView,
    })),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["rule-columns"] });
  const toggle = useMutation({
    mutationFn: (column: RuleColumnDto) => updateRuleColumn(column.id, { expectedVersion: column.version, name: column.name, key: column.key, dataType: column.dataType, headerNames: column.headerNames, description: column.description, isActive: !column.isActive }),
    onSuccess: (_result, column) => { toast.success(column.isActive ? "Column deactivated" : "Column activated"); void invalidate(); },
    onError: (error) => toast.error("Update failed", { description: describe(error) }),
  });
  const remove = useMutation({
    mutationFn: (column: RuleColumnDto) => deleteRuleColumn(column.id),
    onSuccess: () => { toast.success("Column deleted"); setDeleting(null); void invalidate(); },
    onError: (error) => toast.error("Delete failed", { description: describe(error) }),
  });

  if (permissionsHydrated && !canView) return <EntityEmpty icon={Columns3} title="Column master unavailable" body="You do not have permission to view rule columns." />;
  const items = query.data?.items ?? [];
  const activeKind = KINDS.find((item) => item.value === kind)!;

  return (
    <div className="space-y-6 fsh-enter">
      <EntityPageHeader icon={Columns3} title="Column master" total={query.data?.totalCount ?? null} unit="column" description="Every column that can appear in your PPAP documents. Rules pick their columns from this list.">
        <Button variant="outline" aria-label="Refresh columns" disabled={query.isFetching} onClick={() => void query.refetch()}><RefreshCw className={cn("size-4", query.isFetching && "animate-spin")} /></Button>
        {canCreate && <Button onClick={() => setEditing({ mode: "create", kind })}><Plus className="size-4" />New {kind === "Header" ? "header column" : "column"}</Button>}
      </EntityPageHeader>

      <div role="tablist" aria-label="Column kind" className="flex gap-1">
        {KINDS.map((item, index) => (
          <button
            key={item.value}
            type="button"
            role="tab"
            aria-selected={item.value === kind}
            onClick={() => patch({ kind: item.value === "Column" ? null : item.value })}
            className={cn(
              "inline-flex h-10 cursor-pointer items-center gap-2 rounded-xl border px-3.5 text-[13px] font-medium transition-colors",
              item.value === kind ? "border-[oklch(from_var(--color-primary)_l_c_h_/_0.3)] bg-[var(--color-primary-soft)] text-[var(--color-primary)]" : "border-[var(--color-border)] bg-[var(--color-card)] text-[var(--color-muted-foreground)] hover:text-[var(--color-foreground)]",
            )}
          >
            {item.label}
            <span className="rounded-full bg-[var(--color-muted)] px-1.5 font-mono text-[10.5px]">{counts[index]?.data?.totalCount ?? "–"}</span>
          </button>
        ))}
      </div>
      <p className="-mt-3 text-[12.5px] text-[var(--color-muted-foreground)]">{activeKind.description}</p>

      <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-3 shadow-xs">
        <div className="relative min-w-[16rem] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--color-muted-foreground)]" />
          <input
            aria-label="Search columns"
            value={searchDraft}
            placeholder="Search name, key, or header text"
            onChange={(event) => {
              const value = event.target.value;
              setSearchDraft(value);
              clearTimeout(timer.current);
              timer.current = setTimeout(() => patch({ q: value.trim() || null }), 300);
            }}
            className="h-10 w-full rounded-xl border border-[var(--color-border)] bg-transparent pl-9 pr-3 text-sm outline-none focus:border-[oklch(from_var(--color-ring)_l_c_h_/_0.4)] focus:ring-2 focus:ring-[oklch(from_var(--color-ring)_l_c_h_/_0.12)]"
          />
        </div>
        <Combobox variant="filter" label="Data type" value={dataType || null} clearable onChange={(value) => patch({ type: value })} options={DATA_TYPES.map((value) => ({ value, label: value }))} />
        <Segmented label="Status" size="sm" value={status} onChange={(value) => patch({ status: value === "active" ? null : value })} options={[{ value: "active", label: "Active" }, { value: "inactive", label: "Inactive" }, { value: "all", label: "All" }]} />
      </div>

      {query.isError && <ErrorBand message={describe(query.error)} />}
      {query.isLoading && <div className="space-y-2" role="status" aria-busy="true"><span className="sr-only">Loading columns…</span>{Array.from({ length: 6 }).map((_, index) => <Skeleton key={index} className="h-12 rounded-xl" />)}</div>}
      {!query.isLoading && !query.isError && items.length === 0 && (
        <EntityEmpty icon={Columns3} title={search || dataType ? "No columns match" : `No ${activeKind.label.toLowerCase()} yet`} body={activeKind.description} action={canCreate ? <Button onClick={() => setEditing({ mode: "create", kind })}><Plus className="size-4" />Add column</Button> : undefined} />
      )}
      {items.length > 0 && (
        <EntityListCard className={cn(query.isPlaceholderData && "opacity-70")}>
          <div className="overflow-x-auto">
            <div className="min-w-[52rem]">
              <EntityListHeader className={COLUMNS_GRID}>
                <span>Column name</span><span>Key</span><span>Data type</span><span>Actual header names</span><span>Status</span><span className="sr-only">Actions</span>
              </EntityListHeader>
              {items.map((column, index) => (
                <EntityListRow key={column.id} className={COLUMNS_GRID} isLast={index === items.length - 1} dim={!column.isActive} onClick={canEdit ? () => setEditing({ mode: "edit", column }) : undefined}>
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-medium">{column.name}</span>
                    {column.description && <span className="block truncate text-[12px] text-[var(--color-muted-foreground)]">{column.description}</span>}
                  </span>
                  <code className="truncate font-mono text-[12px] text-[var(--color-muted-foreground)]">{column.key}</code>
                  <span><EntityStatusBadge tone="info">{column.dataType}</EntityStatusBadge></span>
                  <span className="flex min-w-0 flex-wrap gap-1">
                    {(column.headerNames ?? "").split(",").map((value) => value.trim()).filter(Boolean).slice(0, 4).map((value) => (
                      <span key={value} className="truncate rounded-md bg-[var(--color-muted)] px-1.5 py-0.5 text-[11px]">{value}</span>
                    ))}
                  </span>
                  <span><EntityStatusBadge tone={column.isActive ? "success" : "default"} withDot={column.isActive}>{column.isActive ? "Active" : "Inactive"}</EntityStatusBadge></span>
                  <span className="flex justify-end" onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()} role="presentation">
                    {(canEdit || canDelete) && (
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <button type="button" aria-label={`Actions for ${column.name}`} className="grid size-7 cursor-pointer place-items-center rounded-md text-[var(--color-muted-foreground)] hover:bg-[var(--color-accent)] hover:text-[var(--color-foreground)]">
                            <MoreHorizontal className="size-4" />
                          </button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          {canEdit && <DropdownMenuItem onSelect={() => setEditing({ mode: "edit", column })}><Pencil className="size-4" />Edit</DropdownMenuItem>}
                          {canEdit && <DropdownMenuItem onSelect={() => toggle.mutate(column)}><Power className="size-4" />{column.isActive ? "Deactivate" : "Activate"}</DropdownMenuItem>}
                          {canDelete && <><DropdownMenuSeparator /><DropdownMenuItem destructive onSelect={() => setDeleting(column)}><Trash2 className="size-4" />Delete</DropdownMenuItem></>}
                        </DropdownMenuContent>
                      </DropdownMenu>
                    )}
                  </span>
                </EntityListRow>
              ))}
            </div>
          </div>
        </EntityListCard>
      )}
      {query.data && (
        <EntityPager page={query.data.pageNumber} totalPages={query.data.totalPages} hasPrev={query.data.hasPrevious} hasNext={query.data.hasNext} onPrev={() => patch({ page: page > 2 ? String(page - 1) : null }, false)} onNext={() => patch({ page: String(page + 1) }, false)} />
      )}

      <ColumnDialog editing={editing} onClose={() => setEditing(null)} onSaved={() => void invalidate()} />

      <Dialog open={deleting !== null} onOpenChange={(open) => !open && setDeleting(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Delete column</DialogTitle>
            <DialogDescription>
              Delete <strong>{deleting?.name}</strong>? Existing rules keep working but will show <code className="font-mono">{deleting?.key}</code> as "not in master". Deactivate instead to hide it from new rules.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleting(null)} disabled={remove.isPending}>Cancel</Button>
            <Button variant="destructive" disabled={remove.isPending} onClick={() => deleting && remove.mutate(deleting)}>Delete</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function toKey(name: string): string {
  const words = name.replace(/[^A-Za-z0-9]+/g, " ").trim().split(/\s+/).filter(Boolean);
  return words.map((word, index) => (index === 0 ? word.toLowerCase() : word[0]!.toUpperCase() + word.slice(1).toLowerCase())).join("").slice(0, 64);
}

function ColumnDialog({ editing, onClose, onSaved }: { editing: Editing | null; onClose: () => void; onSaved: () => void }) {
  const existing = editing?.mode === "edit" ? editing.column : null;
  const [name, setName] = useState("");
  const [key, setKey] = useState("");
  const [keyTouched, setKeyTouched] = useState(false);
  const [dataType, setDataType] = useState<RuleColumnDataType>("Text");
  const [headerNames, setHeaderNames] = useState("");
  const [description, setDescription] = useState("");
  const [active, setActive] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!editing) return;
    setName(existing?.name ?? "");
    setKey(existing?.key ?? "");
    setKeyTouched(Boolean(existing));
    setDataType(existing?.dataType ?? "Text");
    setHeaderNames(existing?.headerNames ?? "");
    setDescription(existing?.description ?? "");
    setActive(existing?.isActive ?? true);
    setError("");
  }, [editing, existing]);

  const save = useMutation({
    mutationFn: async (input: { name: string; key: string; dataType: RuleColumnDataType; headerNames: string | null; description: string | null; active: boolean }) => {
      if (existing) {
        await updateRuleColumn(existing.id, { expectedVersion: existing.version, name: input.name, key: input.key, dataType: input.dataType, headerNames: input.headerNames, description: input.description, isActive: input.active });
        return existing.id;
      }
      return createRuleColumn({ name: input.name, key: input.key, kind: editing?.mode === "create" ? editing.kind : "Column", dataType: input.dataType, headerNames: input.headerNames, description: input.description });
    },
    onSuccess: () => { toast.success(existing ? "Column updated" : "Column created"); onSaved(); onClose(); },
    onError: (failure) => setError(describe(failure)),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!name.trim()) return setError("Column name is required.");
    if (!KEY_PATTERN.test(key.trim())) return setError("Key must use letters, numbers, dashes, or underscores (dots separate nested fields).");
    save.mutate({ name: name.trim(), key: key.trim(), dataType, headerNames: headerNames.trim() || null, description: description.trim() || null, active });
  };

  const kindLabel = (existing?.kind ?? (editing?.mode === "create" ? editing.kind : "Column")) === "Header" ? "header column" : "column";
  return (
    <Dialog open={editing !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-xl">
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>{existing ? `Edit ${kindLabel}` : `New ${kindLabel}`}</DialogTitle>
            <DialogDescription>Columns are shared by every rule. The key must match the field name produced when documents are extracted.</DialogDescription>
          </DialogHeader>
          <DialogBody className="grid gap-4 sm:grid-cols-2">
            <Field id="column-name" label="Column name" required>
              <Input id="column-name" value={name} maxLength={100} placeholder="Completion Date" onChange={(event) => { setName(event.target.value); if (!keyTouched) setKey(toKey(event.target.value)); }} />
            </Field>
            <Field id="column-key" label="Key" required hint="Used inside rules, e.g. completionDate.">
              <Input id="column-key" value={key} maxLength={128} className="font-mono" placeholder="completionDate" onChange={(event) => { setKey(event.target.value); setKeyTouched(true); }} />
            </Field>
            <div className="space-y-1.5 sm:col-span-2">
              <span className="text-[11.5px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">Data type</span>
              <div><Segmented label="Data type" value={dataType} onChange={setDataType} options={DATA_TYPES.map((value) => ({ value, label: value }))} /></div>
            </div>
            <div className="sm:col-span-2">
              <Field id="column-headers" label="Actual header names" hint="Header text this column appears under in documents. Separate with commas or new lines.">
                <textarea id="column-headers" rows={3} maxLength={2000} className={textareaClass} value={headerNames} placeholder="Completion Date, Completed Date, Actual Completion Date" onChange={(event) => setHeaderNames(event.target.value)} />
              </Field>
            </div>
            <div className="sm:col-span-2">
              <Field id="column-description" label="Description">
                <Input id="column-description" value={description} maxLength={500} onChange={(event) => setDescription(event.target.value)} />
              </Field>
            </div>
            {existing && (
              <div className="flex items-center gap-3 sm:col-span-2">
                <Switch id="column-active" aria-label="Active" checked={active} onCheckedChange={setActive} />
                <span className="text-[13px]">Active <span className="text-[var(--color-muted-foreground)]">(inactive columns are hidden from the rule builder)</span></span>
              </div>
            )}
            {error && <p role="alert" className="text-[12.5px] text-[var(--color-destructive)] sm:col-span-2">{error}</p>}
          </DialogBody>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={save.isPending}>Cancel</Button>
            <Button type="submit" disabled={save.isPending}>{save.isPending ? "Saving…" : existing ? "Save column" : "Create column"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
