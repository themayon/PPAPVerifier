import { useState, type FormEvent } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router-dom";
import { FileStack, ListChecks, PanelsTopLeft, Plus, RefreshCw, Search } from "lucide-react";
import { toast } from "sonner";
import { createTemplateFromDocumentSettings, searchTemplateSetups, type TemplateSetupSummaryDto } from "@/api/rules";
import { useAuth } from "@/auth/use-auth";
import { EntityEmpty, EntityPageHeader, EntityPager, EntityStatusBadge, ErrorBand, Field } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";

const CODE_PATTERN = /^[A-Za-z0-9_.-]{1,64}$/;

export function RuleTemplatesPage() {
  const { user, permissionsHydrated } = useAuth();
  const perms = user?.permissions ?? [];
  const canView = perms.includes("Permissions.Templates.View");
  const canCreate = perms.includes("Permissions.Templates.Create");
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [createOpen, setCreateOpen] = useState(false);
  const query = useQuery({
    queryKey: ["template-setups", { page, search }],
    queryFn: () => searchTemplateSetups({ pageNumber: page, pageSize: 24, search: search || undefined }),
    placeholderData: keepPreviousData,
    enabled: permissionsHydrated && canView,
  });

  if (permissionsHydrated && !canView) return <EntityEmpty icon={PanelsTopLeft} title="Templates unavailable" body="You do not have permission to view templates." />;
  const items = query.data?.items ?? [];

  return (
    <div className="space-y-6 fsh-enter">
      <EntityPageHeader icon={PanelsTopLeft} title="Templates" total={query.data?.totalCount ?? null} unit="template" description="Each template is a copy of Document settings where you switch documents and rules on or off for a group of suppliers. Assign a template to a part.">
        <Button variant="outline" aria-label="Refresh templates" disabled={query.isFetching} onClick={() => void query.refetch()}><RefreshCw className={cn("size-4", query.isFetching && "animate-spin")} /></Button>
        {canCreate && <Button onClick={() => setCreateOpen(true)}><Plus className="size-4" />New template</Button>}
      </EntityPageHeader>

      <div className="relative max-w-md">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--color-muted-foreground)]" />
        <input aria-label="Search templates" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Search template code or name" className="h-10 w-full rounded-xl border border-[var(--color-border)] bg-[var(--color-card)] pl-9 pr-3 text-sm outline-none focus:border-[oklch(from_var(--color-ring)_l_c_h_/_0.4)]" />
      </div>

      {query.isError && <ErrorBand message={describe(query.error)} />}
      {query.isLoading && <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">{Array.from({ length: 6 }).map((_, index) => <Skeleton key={index} className="h-40 rounded-2xl" />)}</div>}
      {!query.isLoading && !query.isError && items.length === 0 && (
        <EntityEmpty icon={PanelsTopLeft} title="No templates yet" body="Create a template to copy every document and rule from Document settings, then switch off what a supplier doesn't need." action={canCreate ? <Button onClick={() => setCreateOpen(true)}><Plus className="size-4" />New template</Button> : undefined} />
      )}
      <ul className={cn("grid gap-3 sm:grid-cols-2 xl:grid-cols-3", query.isPlaceholderData && "opacity-70")}>
        {items.map((template) => <li key={template.id}><TemplateCard template={template} /></li>)}
      </ul>
      {query.data && <EntityPager page={query.data.pageNumber} totalPages={query.data.totalPages} hasPrev={query.data.hasPrevious} hasNext={query.data.hasNext} onPrev={() => setPage((value) => Math.max(1, value - 1))} onNext={() => setPage((value) => value + 1)} />}

      <CreateTemplateDialog open={createOpen} onClose={() => setCreateOpen(false)} />
    </div>
  );
}

function TemplateCard({ template }: { template: TemplateSetupSummaryDto }) {
  const live = Boolean(template.currentPublishedVersionId);
  return (
    <Link
      to={`/rule-templates/${template.id}`}
      data-testid="template-card"
      className="group flex h-full flex-col rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-4 shadow-xs transition-all hover:-translate-y-0.5 hover:shadow-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-ring)]"
    >
      <div className="flex items-start justify-between gap-2">
        <code className="truncate rounded-md bg-[var(--color-muted)] px-1.5 py-0.5 font-mono text-[11px] font-semibold">{template.stableCode}</code>
        <EntityStatusBadge tone={live ? "success" : "default"} withDot={live}>{live ? "Live" : "Not live"}</EntityStatusBadge>
      </div>
      <h3 className="mt-2 line-clamp-1 font-display text-[15px] font-semibold">{template.name}</h3>
      <p className="mt-1 line-clamp-2 min-h-[2.5rem] text-[12.5px] text-[var(--color-muted-foreground)]">{template.description || "No description"}</p>
      <div className="mt-3 grid grid-cols-2 gap-2">
        <Stat icon={FileStack} label="Documents" value={`${template.enabledDocumentCount} / ${template.documentCount}`} />
        <Stat icon={ListChecks} label="Rules" value={`${template.enabledRuleCount} / ${template.ruleCount}`} />
      </div>
      <p className="mt-3 text-[11px] text-[var(--color-muted-foreground)]">Updated {new Date(template.updatedAtUtc).toLocaleDateString()}</p>
    </Link>
  );
}

function Stat({ icon: Icon, label, value }: { icon: typeof FileStack; label: string; value: string }) {
  return (
    <div className="rounded-xl bg-[oklch(from_var(--color-muted)_l_c_h_/_0.5)] px-3 py-2">
      <p className="flex items-center gap-1 text-[10.5px] uppercase tracking-wider text-[var(--color-muted-foreground)]"><Icon className="size-3" />{label}</p>
      <p className="font-display text-[15px] font-semibold">{value}</p>
    </div>
  );
}

function CreateTemplateDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [error, setError] = useState("");
  const create = useMutation({
    mutationFn: createTemplateFromDocumentSettings,
    onSuccess: (result) => {
      toast.success("Template created from Document settings");
      void queryClient.invalidateQueries({ queryKey: ["template-setups"] });
      onClose();
      navigate(`/rule-templates/${result.templateId}`);
    },
    onError: (failure) => setError(describe(failure)),
  });
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!CODE_PATTERN.test(code.trim())) return setError("Code: letters, numbers, dots, dashes, or underscores (max 64).");
    if (!name.trim()) return setError("Name is required.");
    create.mutate({ stableCode: code.trim().toUpperCase(), name: name.trim(), description: description.trim() || null });
  };
  return (
    <Dialog open={open} onOpenChange={(value) => { if (!value) { setError(""); onClose(); } }}>
      <DialogContent className="sm:max-w-lg">
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle>New template</DialogTitle>
            <DialogDescription>All documents and their mapped rules are copied from Document settings. You can switch them on or off afterwards.</DialogDescription>
          </DialogHeader>
          <DialogBody className="grid gap-4">
            <Field id="template-code" label="Template code" required hint="e.g. SUPPLIER-TIER-1. Cannot change later.">
              <Input id="template-code" value={code} maxLength={64} className="font-mono uppercase" onChange={(event) => setCode(event.target.value.toUpperCase())} />
            </Field>
            <Field id="template-name" label="Template name" required>
              <Input id="template-name" value={name} maxLength={200} placeholder="Tier 1 suppliers - full PPAP" onChange={(event) => setName(event.target.value)} />
            </Field>
            <Field id="template-description" label="Description">
              <Input id="template-description" value={description} maxLength={2000} onChange={(event) => setDescription(event.target.value)} />
            </Field>
            {error && <p role="alert" className="text-[12.5px] text-[var(--color-destructive)]">{error}</p>}
          </DialogBody>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={create.isPending}>Cancel</Button>
            <Button type="submit" disabled={create.isPending}>{create.isPending ? "Creating…" : "Create template"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
