import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileStack, ListChecks, Search, Settings2 } from "lucide-react";
import { toast } from "sonner";
import {
  getDocumentSettings,
  getRuleLookup,
  updateDocumentSetting,
  type DocumentSettingDto,
  type RuleLookupDto,
  type TemplateDocumentDisposition,
} from "@/api/rules";
import { useAuth } from "@/auth/use-auth";
import { EntityEmpty, EntityPageHeader, ErrorBand } from "@/components/list";
import { Button } from "@/components/ui/button";
import { DialogDescription, DialogTitle, Sheet, SheetContent } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { DISPOSITIONS, RULE_TYPES, ruleTypeForCategory } from "./rule-model";
import { RulePicker } from "./rule-picker";
import { Segmented } from "./rule-ui";

export function DocumentSettingsPage() {
  const { user, permissionsHydrated } = useAuth();
  const perms = user?.permissions ?? [];
  const canView = perms.includes("Permissions.RuleDocumentSettings.View");
  const canEdit = perms.includes("Permissions.RuleDocumentSettings.Update");
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<DocumentSettingDto | null>(null);
  const settings = useQuery({ queryKey: ["document-settings"], queryFn: getDocumentSettings, enabled: permissionsHydrated && canView });
  const rules = useQuery({ queryKey: ["rule-lookup"], queryFn: getRuleLookup, enabled: permissionsHydrated && canView, staleTime: 60_000 });
  const rulesById = useMemo(() => new Map((rules.data ?? []).map((rule) => [rule.id, rule])), [rules.data]);

  const toggle = useMutation({
    mutationFn: (setting: DocumentSettingDto) => updateDocumentSetting(setting.id, { expectedVersion: setting.version, name: setting.name, isEnabled: !setting.isEnabled, sortOrder: setting.sortOrder, defaultDisposition: setting.defaultDisposition, ruleDefinitionIds: setting.ruleDefinitionIds }),
    onSuccess: (updated) => { toast.success(`${updated.documentCode} ${updated.isEnabled ? "enabled" : "disabled"}`); void queryClient.invalidateQueries({ queryKey: ["document-settings"] }); },
    onError: (error) => toast.error("Update failed", { description: describe(error) }),
  });

  if (permissionsHydrated && !canView) return <EntityEmpty icon={FileStack} title="Document settings unavailable" body="You do not have permission to view document settings." />;
  const term = search.trim().toLowerCase();
  const items = (settings.data ?? []).filter((item) => !term || item.documentCode.toLowerCase().includes(term) || item.name.toLowerCase().includes(term));
  const totalMapped = (settings.data ?? []).reduce((sum, item) => sum + item.ruleDefinitionIds.length, 0);

  return (
    <div className="space-y-6 fsh-enter">
      <EntityPageHeader icon={FileStack} title="Document settings" total={settings.data?.length ?? null} unit="document" description="Map rules from the rule library to each PPAP document. New templates copy these documents and rules." />

      <div className="flex flex-wrap items-center gap-3 rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-3 shadow-xs">
        <div className="relative min-w-[16rem] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--color-muted-foreground)]" />
          <input aria-label="Search documents" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search document code or name" className="h-10 w-full rounded-xl border border-[var(--color-border)] bg-transparent pl-9 pr-3 text-sm outline-none focus:border-[oklch(from_var(--color-ring)_l_c_h_/_0.4)]" />
        </div>
        <span className="text-[12.5px] text-[var(--color-muted-foreground)]"><strong className="text-[var(--color-foreground)]">{totalMapped}</strong> rule mappings across {settings.data?.filter((item) => item.isEnabled).length ?? 0} enabled documents</span>
      </div>

      {settings.isError && <ErrorBand message={describe(settings.error)} />}
      {settings.isLoading && <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">{Array.from({ length: 8 }).map((_, index) => <Skeleton key={index} className="h-44 rounded-2xl" />)}</div>}

      <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
        {items.map((setting) => (
          <li key={setting.id}>
            <DocumentCard setting={setting} rulesById={rulesById} canEdit={canEdit} onToggle={() => toggle.mutate(setting)} onConfigure={() => setEditing(setting)} />
          </li>
        ))}
      </ul>

      <ConfigureSheet setting={editing} rules={rules.data ?? []} canEdit={canEdit} onClose={() => setEditing(null)} onSaved={() => void queryClient.invalidateQueries({ queryKey: ["document-settings"] })} />
    </div>
  );
}

function DocumentCard({ setting, rulesById, canEdit, onToggle, onConfigure }: { setting: DocumentSettingDto; rulesById: ReadonlyMap<string, RuleLookupDto>; canEdit: boolean; onToggle: () => void; onConfigure: () => void }) {
  const byType = useMemo(() => {
    const counts = new Map<string, number>();
    setting.ruleDefinitionIds.forEach((id) => {
      const rule = rulesById.get(id);
      if (!rule) return;
      const key = ruleTypeForCategory(rule.category).key;
      counts.set(key, (counts.get(key) ?? 0) + 1);
    });
    return counts;
  }, [setting.ruleDefinitionIds, rulesById]);

  return (
    <article data-testid="document-card" className={cn("flex h-full flex-col rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs transition-shadow hover:shadow-md", !setting.isEnabled && "opacity-60")}>
      <div className="flex items-start justify-between gap-2 px-4 pt-4">
        <div className="min-w-0">
          <code className="rounded-md bg-[var(--color-primary-soft)] px-1.5 py-0.5 font-mono text-[11.5px] font-bold text-[var(--color-primary)]">{setting.documentCode}</code>
          <h3 className="mt-2 line-clamp-2 font-display text-[15px] font-semibold">{setting.name}</h3>
        </div>
        <Switch aria-label={`${setting.documentCode} enabled`} checked={setting.isEnabled} disabled={!canEdit} onCheckedChange={onToggle} />
      </div>
      <div className="flex flex-1 flex-wrap content-start gap-1 px-4 pt-3">
        {RULE_TYPES.filter((type) => byType.get(type.key)).map((type) => (
          <span key={type.key} className="inline-flex items-center gap-1 rounded-md bg-[var(--color-muted)] px-1.5 py-0.5 text-[11px]">
            <type.icon className="size-3" />{type.plural.replace(" rules", "")} <strong>{byType.get(type.key)}</strong>
          </span>
        ))}
        {setting.ruleDefinitionIds.length === 0 && <span className="text-[12px] text-[var(--color-muted-foreground)]">No rules mapped yet</span>}
      </div>
      <footer className="mt-3 flex items-center gap-2 border-t border-[oklch(from_var(--color-border)_l_c_h_/_0.6)] px-4 py-2.5">
        <span className="inline-flex items-center gap-1 text-[12px] text-[var(--color-muted-foreground)]"><ListChecks className="size-3.5" /><strong className="text-[var(--color-foreground)]">{setting.ruleDefinitionIds.length}</strong> rules</span>
        <span className="text-[11px] text-[var(--color-muted-foreground)]">· {setting.defaultDisposition}</span>
        <Button size="xs" variant="soft" className="ml-auto" onClick={onConfigure}><Settings2 />{canEdit ? "Configure rules" : "View rules"}</Button>
      </footer>
    </article>
  );
}

function ConfigureSheet({ setting, rules, canEdit, onClose, onSaved }: { setting: DocumentSettingDto | null; rules: readonly RuleLookupDto[]; canEdit: boolean; onClose: () => void; onSaved: () => void }) {
  return (
    <Sheet open={setting !== null} onOpenChange={(open) => !open && onClose()}>
      <SheetContent side="right" className="w-[min(44rem,96vw)] gap-0 p-0">
        {setting && <ConfigureForm key={setting.id + setting.version} setting={setting} rules={rules} canEdit={canEdit} onClose={onClose} onSaved={onSaved} />}
      </SheetContent>
    </Sheet>
  );
}

function ConfigureForm({ setting, rules, canEdit, onClose, onSaved }: { setting: DocumentSettingDto; rules: readonly RuleLookupDto[]; canEdit: boolean; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(setting.name);
  const [sortOrder, setSortOrder] = useState(String(setting.sortOrder));
  const [disposition, setDisposition] = useState<TemplateDocumentDisposition>(setting.defaultDisposition);
  const [selected, setSelected] = useState<Set<string>>(() => new Set(setting.ruleDefinitionIds));
  const save = useMutation({
    mutationFn: (input: { name: string; sortOrder: number; disposition: TemplateDocumentDisposition; ids: string[] }) =>
      updateDocumentSetting(setting.id, { expectedVersion: setting.version, name: input.name, isEnabled: setting.isEnabled, sortOrder: input.sortOrder, defaultDisposition: input.disposition, ruleDefinitionIds: input.ids }),
    onSuccess: (updated) => { toast.success(`${updated.documentCode}: ${updated.ruleDefinitionIds.length} rules mapped`); onSaved(); onClose(); },
    onError: (error) => toast.error("Save failed", { description: describe(error) }),
  });
  const order = Number(sortOrder);
  const valid = name.trim().length > 0 && Number.isInteger(order) && order >= 1 && order <= 1000;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="border-b border-[var(--color-border)] px-6 py-5 pr-12">
        <DialogTitle className="flex items-center gap-2"><code className="rounded-md bg-[var(--color-primary-soft)] px-1.5 py-0.5 font-mono text-[13px] text-[var(--color-primary)]">{setting.documentCode}</code>Document rules</DialogTitle>
        <DialogDescription>Rules selected here are copied into every new template for this document.</DialogDescription>
        <div className="mt-4 grid gap-3 sm:grid-cols-[minmax(0,1fr)_6rem]">
          <Input aria-label="Document name" value={name} maxLength={200} disabled={!canEdit} onChange={(event) => setName(event.target.value)} />
          <Input aria-label="Display order" type="number" min={1} max={1000} value={sortOrder} disabled={!canEdit} onChange={(event) => setSortOrder(event.target.value)} />
        </div>
        <div className="mt-3 flex flex-wrap items-center gap-2 text-[12px] text-[var(--color-muted-foreground)]">
          Default requirement
          <Segmented label="Default requirement" size="sm" value={disposition} disabled={!canEdit} onChange={setDisposition} options={DISPOSITIONS} />
        </div>
      </div>
      <div className="flex min-h-0 flex-1 flex-col px-6 py-4">
        <RulePicker rules={rules} selected={selected} onChange={setSelected} disabled={!canEdit} />
      </div>
      <div className="flex justify-end gap-2 border-t border-[var(--color-border)] px-6 py-4">
        <Button variant="outline" onClick={onClose}>{canEdit ? "Cancel" : "Close"}</Button>
        {canEdit && (
          <Button disabled={!valid || save.isPending} onClick={() => save.mutate({ name: name.trim(), sortOrder: order, disposition, ids: [...selected] })}>
            {save.isPending ? "Saving…" : `Save ${selected.size} rules`}
          </Button>
        )}
      </div>
    </div>
  );
}
