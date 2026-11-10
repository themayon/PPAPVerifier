import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useBlocker, useParams } from "react-router-dom";
import { AlertTriangle, ArrowLeft, FileStack, ListPlus, Save, Search } from "lucide-react";
import { toast } from "sonner";
import {
  getDocumentSettings,
  getRuleLookup,
  getTemplateSetup,
  saveTemplateSetup,
  type RuleLookupDto,
  type TemplateSetupDocument,
  type TemplateSetupDto,
  type TemplateSetupRule,
} from "@/api/rules";
import { useAuth } from "@/auth/use-auth";
import { EntityEmpty, EntityStatusBadge, ErrorBand, Field } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { ApiRequestError } from "@/lib/api-client";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { DISPOSITIONS, PPAP_DOCUMENTS, RULE_TYPES, outcomeSummary, ruleTypeForCategory, type RuleTypeKey } from "./rule-model";
import { RulePicker } from "./rule-picker";
import { Segmented, SeverityBadge } from "./rule-ui";

type Draft = { name: string; description: string; documents: TemplateSetupDocument[]; rules: TemplateSetupRule[] };

const signature = (draft: Draft) => JSON.stringify([draft.name.trim(), draft.description.trim(), draft.documents, draft.rules]);
const toDraft = (setup: TemplateSetupDto): Draft => ({ name: setup.name, description: setup.description ?? "", documents: setup.documents, rules: setup.rules });

export function TemplateEditorPage() {
  const { templateId } = useParams();
  const { user, permissionsHydrated } = useAuth();
  const perms = user?.permissions ?? [];
  const setup = useQuery({ queryKey: ["template-setup", templateId], queryFn: () => getTemplateSetup(templateId!), enabled: Boolean(templateId) && permissionsHydrated });
  const settings = useQuery({ queryKey: ["document-settings"], queryFn: getDocumentSettings, enabled: permissionsHydrated });
  const rules = useQuery({ queryKey: ["rule-lookup"], queryFn: getRuleLookup, enabled: permissionsHydrated, staleTime: 60_000 });
  const [skipped, setSkipped] = useState<string[]>([]);
  const [selectedCode, setSelectedCode] = useState("");

  if (permissionsHydrated && !perms.includes("Permissions.Templates.View")) return <EntityEmpty icon={FileStack} title="Templates unavailable" body="You do not have permission to view templates." />;
  if (setup.isError) return <div className="space-y-4"><BackLink /><ErrorBand message={describe(setup.error)} /></div>;
  if (!setup.data) return <div role="status" aria-busy="true" className="space-y-4"><span className="sr-only">Loading template…</span><Skeleton className="h-14 rounded-xl" /><Skeleton className="h-96 rounded-2xl" /></div>;

  const names = new Map<string, string>([...PPAP_DOCUMENTS.map((doc) => [doc.code, doc.name] as const), ...(settings.data ?? []).map((doc) => [doc.documentCode, doc.name] as const)]);
  return <Editor key={`${setup.data.id}:${setup.data.version}`} setup={setup.data} names={names} rules={rules.data ?? []} canEdit={perms.includes("Permissions.Templates.Edit")} skipped={skipped} setSkipped={setSkipped} selectedCode={selectedCode} setSelectedCode={setSelectedCode} />;
}

function Editor({ setup, names, rules, canEdit, skipped, setSkipped, selectedCode: chosenCode, setSelectedCode }: {
  setup: TemplateSetupDto;
  names: ReadonlyMap<string, string>;
  rules: readonly RuleLookupDto[];
  canEdit: boolean;
  skipped: string[];
  setSkipped: (ids: string[]) => void;
  selectedCode: string;
  setSelectedCode: (code: string) => void;
}) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<Draft>(() => toDraft(setup));
  const [baseline, setBaseline] = useState(() => signature(toDraft(setup)));
  const selectedCode = setup.documents.some((doc) => doc.documentCode === chosenCode)
    ? chosenCode
    : setup.documents.find((doc) => doc.isEnabled)?.documentCode ?? setup.documents[0]?.documentCode ?? "";
  const [docSearch, setDocSearch] = useState("");
  const rulesById = useMemo(() => new Map(rules.map((rule) => [rule.id, rule])), [rules]);
  const dirty = canEdit && signature(draft) !== baseline;

  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty && currentLocation.pathname !== nextLocation.pathname);
  useEffect(() => {
    if (!dirty) return;
    const handler = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [dirty]);

  const save = useMutation({
    mutationFn: (input: Draft) => saveTemplateSetup(setup.id, { expectedVersion: setup.version, name: input.name.trim(), description: input.description.trim() || null, documents: input.documents, rules: input.rules }),
    onSuccess: async (result, input) => {
      setBaseline(signature(input));
      setSkipped(result.skippedRuleDefinitionIds);
      toast.success("Template saved", { description: result.skippedRuleDefinitionIds.length ? `${result.skippedRuleDefinitionIds.length} enabled rules are not live yet and won't run.` : "Changes apply to new submissions immediately." });
      await Promise.all([queryClient.invalidateQueries({ queryKey: ["template-setup", setup.id] }), queryClient.invalidateQueries({ queryKey: ["template-setups"] })]);
    },
    onError: (error) => toast.error(error instanceof ApiRequestError && error.status === 409 ? "Save conflict" : "Save failed", { description: describe(error) }),
  });

  const setDocument = (code: string, patch: Partial<TemplateSetupDocument>) => setDraft((value) => ({ ...value, documents: value.documents.map((doc) => (doc.documentCode === code ? { ...doc, ...patch } : doc)) }));
  const setAllDocuments = (isEnabled: boolean) => setDraft((value) => ({ ...value, documents: value.documents.map((doc) => ({ ...doc, isEnabled })) }));
  const setRules = (code: string, ids: ReadonlySet<string>, isEnabled: boolean) => setDraft((value) => ({ ...value, rules: value.rules.map((rule) => (rule.documentCode === code && ids.has(rule.ruleDefinitionId) ? { ...rule, isEnabled } : rule)) }));
  const replaceRules = (code: string, ids: ReadonlySet<string>) => setDraft((value) => {
    const kept = value.rules.filter((rule) => rule.documentCode !== code || ids.has(rule.ruleDefinitionId));
    const existing = new Set(kept.filter((rule) => rule.documentCode === code).map((rule) => rule.ruleDefinitionId));
    return { ...value, rules: [...kept, ...[...ids].filter((id) => !existing.has(id)).map((id) => ({ documentCode: code, ruleDefinitionId: id, isEnabled: true }))] };
  });

  const enabledDocs = draft.documents.filter((doc) => doc.isEnabled);
  const enabledCodes = new Set(enabledDocs.map((doc) => doc.documentCode));
  const enabledRules = draft.rules.filter((rule) => rule.isEnabled && enabledCodes.has(rule.documentCode));
  const selected = draft.documents.find((doc) => doc.documentCode === selectedCode);
  const term = docSearch.trim().toLowerCase();
  const visibleDocs = draft.documents.filter((doc) => !term || doc.documentCode.toLowerCase().includes(term) || (names.get(doc.documentCode) ?? "").toLowerCase().includes(term));

  return (
    <div className="fsh-enter">
      <header className="sticky -top-4 z-20 -mx-4 -mt-4 mb-6 border-b border-[var(--color-border)] bg-[oklch(from_var(--color-background)_l_c_h_/_0.86)] px-4 py-3 backdrop-blur-md md:-top-6 md:-mx-6 md:-mt-6 md:px-6">
        <div className="flex flex-wrap items-center gap-3">
          <BackLink compact />
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <code className="rounded-md bg-[var(--color-muted)] px-1.5 py-0.5 font-mono text-[11px] font-semibold">{setup.stableCode}</code>
              <h1 className="truncate font-display text-[17px] font-semibold">{draft.name || "Template"}</h1>
              <EntityStatusBadge tone={setup.currentPublishedVersionId ? "success" : "default"} withDot={Boolean(setup.currentPublishedVersionId)}>{setup.currentPublishedVersionId ? "Live" : "Not live"}</EntityStatusBadge>
              {dirty && <span className="inline-flex items-center gap-1 text-[11px] font-medium text-[var(--color-warning)]"><span className="size-1.5 rounded-full bg-current" />Unsaved changes</span>}
            </div>
            <p className="text-[11.5px] text-[var(--color-muted-foreground)]">{enabledDocs.length} of {draft.documents.length} documents · {enabledRules.length} active rules</p>
          </div>
          {canEdit && <Button onClick={() => save.mutate(draft)} disabled={!dirty || save.isPending || !draft.name.trim()}><Save className="size-4" />{save.isPending ? "Saving…" : "Save template"}</Button>}
        </div>
      </header>

      {skipped.length > 0 && (
        <div role="alert" className="mb-6 flex gap-3 rounded-xl border border-[oklch(from_var(--color-warning)_l_c_h_/_0.4)] bg-[oklch(from_var(--color-warning)_l_c_h_/_0.08)] px-4 py-3 text-[13px]">
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-[var(--color-warning)]" />
          <div>
            <p className="font-medium">{skipped.length} enabled rule{skipped.length === 1 ? " is" : "s are"} not live yet, so {skipped.length === 1 ? "it" : "they"} won't run.</p>
            <p className="text-[var(--color-muted-foreground)]">Publish {skipped.map((id) => rulesById.get(id)?.stableCode ?? id).join(", ")} in the rule library, then save this template again.</p>
          </div>
        </div>
      )}

      <section className="mb-6 grid gap-4 rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-5 shadow-xs md:grid-cols-2">
        <Field id="template-name" label="Template name" required>
          <Input id="template-name" value={draft.name} maxLength={200} disabled={!canEdit} onChange={(event) => setDraft((value) => ({ ...value, name: event.target.value }))} />
        </Field>
        <Field id="template-description" label="Description">
          <Input id="template-description" value={draft.description} maxLength={2000} disabled={!canEdit} onChange={(event) => setDraft((value) => ({ ...value, description: event.target.value }))} />
        </Field>
      </section>

      <div className="grid gap-6 lg:grid-cols-[minmax(17rem,22rem)_minmax(0,1fr)]">
        <section aria-label="Template documents" className="flex max-h-[calc(100vh-14rem)] min-h-[24rem] flex-col rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
          <div className="space-y-2 border-b border-[var(--color-border)] p-3">
            <div className="flex items-center justify-between">
              <h2 className="text-[13px] font-semibold">Documents</h2>
              {canEdit && (
                <span className="flex gap-1">
                  <Button size="xs" variant="ghost" onClick={() => setAllDocuments(true)}>Enable all</Button>
                  <Button size="xs" variant="ghost" onClick={() => setAllDocuments(false)}>Disable all</Button>
                </span>
              )}
            </div>
            <div className="relative">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-[var(--color-muted-foreground)]" />
              <input aria-label="Search template documents" value={docSearch} onChange={(event) => setDocSearch(event.target.value)} placeholder="Filter documents" className="h-8 w-full rounded-lg border border-[var(--color-input)] bg-transparent pl-8 pr-2 text-[12.5px] outline-none focus:border-[var(--color-ring)]" />
            </div>
          </div>
          <ul className="min-h-0 flex-1 overflow-y-auto p-2">
            {visibleDocs.map((doc) => {
              const docRules = draft.rules.filter((rule) => rule.documentCode === doc.documentCode);
              const active = docRules.filter((rule) => rule.isEnabled).length;
              return (
                <li key={doc.documentCode}>
                  <div
                    data-testid="template-document"
                    className={cn(
                      "flex items-center gap-2.5 rounded-xl px-2.5 py-2 transition-colors",
                      doc.documentCode === selectedCode ? "bg-[var(--color-primary-soft)]" : "hover:bg-[oklch(from_var(--color-accent)_l_c_h_/_0.5)]",
                    )}
                  >
                    <Switch aria-label={`${doc.documentCode} enabled`} checked={doc.isEnabled} disabled={!canEdit} onCheckedChange={(isEnabled) => setDocument(doc.documentCode, { isEnabled })} />
                    <button type="button" onClick={() => setSelectedCode(doc.documentCode)} className={cn("flex min-w-0 flex-1 cursor-pointer items-center gap-2 text-left", !doc.isEnabled && "opacity-55")}>
                      <span className="min-w-0 flex-1">
                        <span className="block font-mono text-[12px] font-bold">{doc.documentCode}</span>
                        <span className="block truncate text-[11.5px] text-[var(--color-muted-foreground)]">{names.get(doc.documentCode) ?? doc.documentCode}</span>
                      </span>
                      <span className="shrink-0 rounded-full bg-[var(--color-muted)] px-1.5 font-mono text-[10.5px]" title="Active rules / mapped rules">{active}/{docRules.length}</span>
                    </button>
                  </div>
                </li>
              );
            })}
          </ul>
        </section>

        {selected
          ? <DocumentRules key={selected.documentCode} document={selected} name={names.get(selected.documentCode) ?? selected.documentCode} rules={draft.rules.filter((rule) => rule.documentCode === selected.documentCode)} rulesById={rulesById} allRules={rules} canEdit={canEdit}
            onDisposition={(disposition) => setDocument(selected.documentCode, { disposition })}
            onEnabled={(isEnabled) => setDocument(selected.documentCode, { isEnabled })}
            onRules={(ids, isEnabled) => setRules(selected.documentCode, ids, isEnabled)}
            onReplace={(ids) => replaceRules(selected.documentCode, ids)} />
          : <EntityEmpty icon={FileStack} title="Select a document" body="Pick a document on the left to manage its rules." />}
      </div>

      <Dialog open={blocker.state === "blocked"} onOpenChange={(open) => !open && blocker.reset?.()}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader><DialogTitle>Discard unsaved changes?</DialogTitle><DialogDescription>Your template changes haven't been saved.</DialogDescription></DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => blocker.reset?.()}>Keep editing</Button>
            <Button variant="destructive" onClick={() => blocker.proceed?.()}>Discard changes</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function DocumentRules({ document, name, rules, rulesById, allRules, canEdit, onDisposition, onEnabled, onRules, onReplace }: {
  document: TemplateSetupDocument;
  name: string;
  rules: TemplateSetupRule[];
  rulesById: ReadonlyMap<string, RuleLookupDto>;
  allRules: readonly RuleLookupDto[];
  canEdit: boolean;
  onDisposition: (value: TemplateSetupDocument["disposition"]) => void;
  onEnabled: (value: boolean) => void;
  onRules: (ids: ReadonlySet<string>, isEnabled: boolean) => void;
  onReplace: (ids: ReadonlySet<string>) => void;
}) {
  const [search, setSearch] = useState("");
  const [type, setType] = useState<RuleTypeKey | "all">("all");
  const [picking, setPicking] = useState(false);
  const term = search.trim().toLowerCase();
  const rows = rules
    .map((rule) => ({ rule, info: rulesById.get(rule.ruleDefinitionId) }))
    .filter(({ info }) => (type === "all" || (info && ruleTypeForCategory(info.category).key === type))
      && (!term || (info?.stableCode ?? "").toLowerCase().includes(term) || (info?.name ?? "").toLowerCase().includes(term)))
    .sort((a, b) => (a.info?.stableCode ?? "").localeCompare(b.info?.stableCode ?? ""));
  const visibleIds = new Set(rows.map(({ rule }) => rule.ruleDefinitionId));
  const active = rules.filter((rule) => rule.isEnabled).length;

  return (
    <section aria-label={`${document.documentCode} rules`} className="flex min-h-[24rem] flex-col rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-[var(--color-border)] p-4">
        <div className="min-w-0">
          <h2 className="flex items-center gap-2 font-display text-[16px] font-semibold">
            <code className="rounded-md bg-[var(--color-primary-soft)] px-1.5 py-0.5 font-mono text-[12.5px] text-[var(--color-primary)]">{document.documentCode}</code>{name}
          </h2>
          <p className="mt-0.5 text-[12px] text-[var(--color-muted-foreground)]">{active} of {rules.length} rules active{!document.isEnabled && " · document is disabled, none of its rules will run"}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <label className="inline-flex items-center gap-2 text-[12.5px]">
            <Switch aria-label="Document enabled" checked={document.isEnabled} disabled={!canEdit} onCheckedChange={onEnabled} />
            {document.isEnabled ? "Enabled" : "Disabled"}
          </label>
          <Segmented label="Requirement" size="sm" value={document.disposition} disabled={!canEdit || !document.isEnabled} onChange={onDisposition} options={DISPOSITIONS} />
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2 border-b border-[var(--color-border)] px-4 py-2.5">
        <div className="relative min-w-[12rem] flex-1">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-[var(--color-muted-foreground)]" />
          <input aria-label="Search document rules" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search rules" className="h-8 w-full rounded-lg border border-[var(--color-input)] bg-transparent pl-8 pr-2 text-[12.5px] outline-none focus:border-[var(--color-ring)]" />
        </div>
        <div className="flex flex-wrap gap-1">
          {[{ key: "all" as const, label: "All" }, ...RULE_TYPES.map((item) => ({ key: item.key, label: item.plural.replace(" rules", "") }))].map((item) => (
            <button key={item.key} type="button" aria-pressed={type === item.key} onClick={() => setType(item.key)} className={cn("h-7 cursor-pointer rounded-full border px-2.5 text-[11.5px]", type === item.key ? "border-[oklch(from_var(--color-primary)_l_c_h_/_0.3)] bg-[var(--color-primary-soft)] text-[var(--color-primary)]" : "border-[var(--color-border)] text-[var(--color-muted-foreground)]")}>
              {item.label}
            </button>
          ))}
        </div>
        {canEdit && (
          <span className="flex gap-1">
            <Button size="xs" variant="ghost" disabled={rows.length === 0} onClick={() => onRules(visibleIds, true)}>Enable shown</Button>
            <Button size="xs" variant="ghost" disabled={rows.length === 0} onClick={() => onRules(visibleIds, false)}>Disable shown</Button>
            <Button size="xs" variant="soft" onClick={() => setPicking(true)}><ListPlus />Add / remove rules</Button>
          </span>
        )}
      </div>

      <ul className="min-h-0 flex-1 divide-y divide-[var(--color-border)] overflow-y-auto" aria-label="Document rules">
        {rows.length === 0 && <li className="px-4 py-10 text-center text-[12.5px] text-[var(--color-muted-foreground)]">{rules.length === 0 ? "No rules mapped to this document. Use “Add / remove rules” or map them in Document settings." : "No rules match the filter."}</li>}
        {rows.map(({ rule, info }) => {
          const meta = outcomeSummary(info?.latestOutcomeJson);
          return (
            <li key={rule.ruleDefinitionId} data-testid="template-rule" className={cn("flex items-center gap-3 px-4 py-2.5", (!rule.isEnabled || !document.isEnabled) && "opacity-60")}>
              <Switch aria-label={`${info?.stableCode ?? rule.ruleDefinitionId} enabled`} checked={rule.isEnabled} disabled={!canEdit} onCheckedChange={(isEnabled) => onRules(new Set([rule.ruleDefinitionId]), isEnabled)} />
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-1.5">
                  <code className="rounded bg-[var(--color-muted)] px-1 font-mono text-[11px] font-semibold">{info?.stableCode ?? "Unknown rule"}</code>
                  <span className="truncate text-[13px] font-medium">{info?.name ?? rule.ruleDefinitionId}</span>
                </div>
                <div className="mt-0.5 flex flex-wrap items-center gap-1.5 text-[11px] text-[var(--color-muted-foreground)]">
                  <SeverityBadge severity={meta.severity} />
                  {info && <span>{ruleTypeForCategory(info.category).label}</span>}
                  {info && !info.isLive && <EntityStatusBadge tone="warning">Not live</EntityStatusBadge>}
                </div>
              </div>
            </li>
          );
        })}
      </ul>

      <PickRulesDialog open={picking} documentCode={document.documentCode} rules={allRules} initial={new Set(rules.map((rule) => rule.ruleDefinitionId))} onClose={() => setPicking(false)} onConfirm={(ids) => { onReplace(ids); setPicking(false); }} />
    </section>
  );
}

function PickRulesDialog({ open, documentCode, rules, initial, onClose, onConfirm }: { open: boolean; documentCode: string; rules: readonly RuleLookupDto[]; initial: ReadonlySet<string>; onClose: () => void; onConfirm: (ids: Set<string>) => void }) {
  return (
    <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        {open && <PickRulesBody documentCode={documentCode} rules={rules} initial={initial} onClose={onClose} onConfirm={onConfirm} />}
      </DialogContent>
    </Dialog>
  );
}

function PickRulesBody({ documentCode, rules, initial, onClose, onConfirm }: { documentCode: string; rules: readonly RuleLookupDto[]; initial: ReadonlySet<string>; onClose: () => void; onConfirm: (ids: Set<string>) => void }) {
  const [selected, setSelected] = useState<Set<string>>(() => new Set(initial));
  return (
    <>
      <DialogHeader>
        <DialogTitle>{documentCode} rules in this template</DialogTitle>
        <DialogDescription>Add rules from the rule library or untick rules to remove them from this template. Document settings are not changed.</DialogDescription>
      </DialogHeader>
      <DialogBody className="flex h-[min(32rem,60vh)] flex-col">
        <RulePicker rules={rules} selected={selected} onChange={setSelected} />
      </DialogBody>
      <DialogFooter>
        <Button variant="outline" onClick={onClose}>Cancel</Button>
        <Button onClick={() => onConfirm(selected)}>Apply ({selected.size})</Button>
      </DialogFooter>
    </>
  );
}

function BackLink({ compact }: { compact?: boolean }) {
  return (
    <Link to="/rule-templates" aria-label="Back to templates" className="inline-flex items-center gap-1.5 text-[13px] font-medium text-[var(--color-muted-foreground)] hover:text-[var(--color-foreground)]">
      <ArrowLeft className="size-4" />{!compact && "Templates"}
    </Link>
  );
}
