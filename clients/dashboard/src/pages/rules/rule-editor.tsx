import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, Navigate, useBlocker, useNavigate, useParams, useSearchParams } from "react-router-dom";
import {
  AlertTriangle,
  ArrowLeft,
  BrainCircuit,
  CheckCircle2,
  ChevronDown,
  FlaskConical,
  GitBranch,
  Highlighter,
  History,
  Info,
  ListTree,
  Lock,
  MessageSquareText,
  Plus,
  Save,
  Settings2,
  Trash2,
} from "lucide-react";
import { toast } from "sonner";
import {
  cloneRule,
  createRule,
  deleteRule,
  getRule,
  isRuleDeletable,
  publishRule,
  retireRule,
  searchRuleColumns,
  submitRuleForReview,
  updateRuleDraft,
  type RuleCategory,
  type RuleDefinitionDetailDto,
  type RuleVersionMetadataDto,
} from "@/api/rules";
import { useAuth } from "@/auth/use-auth";
import { EntityEmpty, ErrorBand, Field } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { ApiRequestError } from "@/lib/api-client";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { ConditionBuilder } from "./condition-builder";
import {
  CATEGORY_LABELS,
  CODE_PATTERN,
  LIMITS,
  SEVERITIES,
  SEVERITY_DEFAULT_COLOR,
  SEVERITY_STRIPE,
  astToTree,
  buildAst,
  defaultOutcome,
  columnLabel,
  emptyField,
  formulaText,
  indexColumns,
  primaryDocument,
  retargetDocument,
  newGroup,
  newId,
  parseOutcome,
  previewLines,
  ruleTypeByKey,
  serializeOutcome,
  treeStats,
  validateTree,
  type ColumnIndex,
  type GroupNode,
  type Issue,
  type RuleOutcome,
  type RuleTypeConfig,
  type SectionId,
} from "./rule-model";
import { RuleTestPanel } from "./rule-test-panel";
import { ColorPicker, ColumnPicker, DocumentPicker, LifecycleBadge, SectionCard, Segmented, SeverityBadge, TagInput } from "./rule-ui";

type Picker = { columns: ColumnIndex; documentCode: string; showDocument: boolean };

const textareaClass = "w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 text-sm shadow-xs outline-none placeholder:text-[var(--color-muted-foreground)] focus-visible:border-[var(--color-ring)] focus-visible:ring-[3px] focus-visible:ring-[oklch(from_var(--color-ring)_l_c_h_/_0.5)] disabled:opacity-60";

type Draft = {
  code: string;
  name: string;
  description: string;
  category: RuleCategory;
  outcome: RuleOutcome;
  tree: GroupNode;
};

type Transition = "submit" | "publish" | "retire" | "clone";

const TRANSITION_COPY: Record<Transition, { title: string; body: string; confirm: string; done: string }> = {
  submit: { title: "Submit for review", body: "The draft becomes read-only until a reviewer publishes it.", confirm: "Submit", done: "Submitted for review" },
  publish: { title: "Publish rule", body: "This version goes live for new evaluations. Any currently live version is retired.", confirm: "Publish", done: "Rule is live" },
  retire: { title: "Retire version", body: "Retired versions stop running in new evaluations. This cannot be undone.", confirm: "Retire", done: "Version retired" },
  clone: { title: "Create new draft", body: "Copies this live version into a new editable draft.", confirm: "Create draft", done: "New draft created" },
};

export function RuleEditorPage() {
  const { type: typeKey, ruleId } = useParams();
  const [params] = useSearchParams();
  const type = ruleTypeByKey(typeKey);
  if (!type) return <Navigate to="/rules/validation" replace />;
  const fromId = ruleId ? null : params.get("from");
  return <RuleEditorLoader key={`${type.key}:${ruleId ?? "new"}:${fromId ?? ""}`} type={type} ruleId={ruleId ?? null} fromId={fromId} />;
}

function RuleEditorLoader({ type, ruleId, fromId }: { type: RuleTypeConfig; ruleId: string | null; fromId: string | null }) {
  const { user, permissionsHydrated } = useAuth();
  const [params] = useSearchParams();
  const sourceId = ruleId ?? fromId;
  const query = useQuery({ queryKey: ["rule", sourceId], queryFn: () => getRule(sourceId!), enabled: Boolean(sourceId) });
  const perms = user?.permissions ?? [];

  if (permissionsHydrated && !perms.includes("Permissions.Definitions.View")) {
    return <EntityEmpty icon={type.icon} title="Rules unavailable" body="You do not have permission to view rule definitions." />;
  }
  if (sourceId && query.isLoading) return <EditorSkeleton />;
  if (sourceId && query.isError) {
    return (
      <div className="space-y-4">
        <BackLink type={type} />
        <ErrorBand message={describe(query.error)} />
      </div>
    );
  }

  const detail = query.data ?? null;
  const versions = detail?.versions ?? [];
  const requested = params.get("version");
  const version = ruleId
    ? versions.find((item) => item.id === requested) ?? versions.find((item) => item.status === "Draft") ?? versions[0] ?? null
    : versions.find((item) => item.status === "Published") ?? versions[0] ?? null;

  return (
    <RuleEditor
      key={`${version?.id ?? "new"}:${detail?.version ?? 0}`}
      type={type}
      detail={ruleId ? detail : null}
      version={ruleId ? version : null}
      initial={initialDraft(type, detail, version, !ruleId)}
    />
  );
}

function initialDraft(type: RuleTypeConfig, detail: RuleDefinitionDetailDto | null, version: RuleVersionMetadataDto | null, duplicate: boolean): Draft {
  if (!detail) return { code: "", name: "", description: "", category: type.defaultCategory, outcome: defaultOutcome(), tree: newGroup() };
  const parsed = parseOutcome(version?.outcomeJson);
  const tree = astToTree(version?.astJson, parsed.mode);
  return {
    code: duplicate ? `${detail.stableCode}_COPY`.slice(0, 64) : detail.stableCode,
    name: duplicate ? `${detail.name} (copy)`.slice(0, 200) : detail.name,
    description: detail.description ?? "",
    category: type.categories.includes(detail.category) ? detail.category : type.defaultCategory,
    outcome: { ...parsed, documentCode: primaryDocument(tree, parsed) },
    tree,
  };
}

function useColumnIndex(type: RuleTypeConfig): ColumnIndex {
  const query = useQuery({ queryKey: ["rule-columns", "active"], queryFn: () => searchRuleColumns({ isActive: true, pageSize: 500 }), staleTime: 60_000 });
  return useMemo(() => {
    const preferred = type.key === "header" ? "Header" : "Column";
    const items = [...(query.data?.items ?? [])].sort((a, b) => Number(b.kind === preferred) - Number(a.kind === preferred) || a.name.localeCompare(b.name));
    return indexColumns(items);
  }, [query.data, type.key]);
}

function signature(draft: Draft, type: RuleTypeConfig) {
  return JSON.stringify([draft.code, draft.name.trim(), draft.description.trim(), draft.category, buildAst(draft.tree, draft.outcome.mode), serializeOutcome(draft.outcome, type)]);
}

function RuleEditor({ type, detail, version, initial }: { type: RuleTypeConfig; detail: RuleDefinitionDetailDto | null; version: RuleVersionMetadataDto | null; initial: Draft }) {
  const { user } = useAuth();
  const perms = user?.permissions ?? [];
  const can = (permission: string) => perms.includes(`Permissions.Definitions.${permission}`);
  const navigate = useNavigate();
  const [, setParams] = useSearchParams();
  const queryClient = useQueryClient();
  const isNew = !detail;
  const editable = isNew ? can("Create") : version?.status === "Draft" && can("Edit");

  const [draft, setDraft] = useState<Draft>(initial);
  const [baseline, setBaseline] = useState(() => signature(initial, type));
  const [showIssues, setShowIssues] = useState(false);
  const [pending, setPending] = useState<Transition | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const skipBlock = useRef(false);
  const columns = useColumnIndex(type);

  const current = signature(draft, type);
  const dirty = editable && current !== baseline;
  const set = (patch: Partial<Draft>) => setDraft((value) => ({ ...value, ...patch }));
  const setOutcome = (patch: Partial<RuleOutcome>) => setDraft((value) => ({ ...value, outcome: { ...value.outcome, ...patch } }));

  const picker: Picker = { columns, documentCode: draft.outcome.documentCode, showDocument: draft.category === "CrossDocument" || type.key === "header" };
  const changeDocument = (code: string) => setDraft((value) => ({ ...value, ...retargetDocument(value.tree, value.outcome, value.outcome.documentCode, code) }));
  const issues = useMemo(() => collectIssues(draft, type, isNew), [draft, type, isNew]);
  const issueMap = useMemo(() => new Map(issues.map((issue) => [issue.id, issue.message])), [issues]);
  const stats = useMemo(() => treeStats(draft.tree), [draft.tree]);
  const countFor = (section: SectionId) => issues.filter((issue) => issue.section === section).length;

  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty && !skipBlock.current && currentLocation.pathname !== nextLocation.pathname);
  useEffect(() => {
    if (!dirty) return;
    const handler = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [dirty]);

  const save = useMutation({
    mutationFn: async (input: { draft: Draft; versionId: string | null; expected: number }) => {
      const payload = {
        name: input.draft.name.trim(),
        description: input.draft.description.trim() || null,
        category: input.draft.category,
        astJson: buildAst(input.draft.tree, input.draft.outcome.mode),
        outcomeJson: serializeOutcome(input.draft.outcome, type),
      };
      if (!detail || !input.versionId) return createRule({ stableCode: input.draft.code.trim(), ...payload });
      await updateRuleDraft(detail.id, { ...payload, versionId: input.versionId, expectedAggregateVersion: input.expected });
      return detail.id;
    },
    onSuccess: async (id, input) => {
      setBaseline(signature(input.draft, type));
      toast.success(isNew ? "Rule created as draft" : "Draft saved");
      await queryClient.invalidateQueries({ queryKey: ["rules"] });
      if (isNew) {
        skipBlock.current = true;
        navigate(`/rules/${type.key}/${id}`, { replace: true });
      } else {
        await queryClient.invalidateQueries({ queryKey: ["rule", id] });
      }
    },
    onError: (error) => {
      const conflict = error instanceof ApiRequestError && error.status === 409;
      toast.error(conflict ? "Save conflict" : "Save failed", { description: conflict ? `${describe(error)} Reload to get the latest version.` : describe(error) });
    },
  });

  const transition = useMutation({
    mutationFn: async (input: { kind: Transition; id: string; versionId: string; expected: number }) => {
      const body = { versionId: input.versionId, expectedAggregateVersion: input.expected };
      if (input.kind === "submit") await submitRuleForReview(input.id, body);
      else if (input.kind === "publish") await publishRule(input.id, { ...body, retireCurrent: true });
      else if (input.kind === "retire") await retireRule(input.id, body);
      else return cloneRule(input.id, body);
      return null;
    },
    onSuccess: async (newVersionId, input) => {
      toast.success(TRANSITION_COPY[input.kind].done);
      setPending(null);
      await Promise.all([queryClient.invalidateQueries({ queryKey: ["rule", input.id] }), queryClient.invalidateQueries({ queryKey: ["rules"] })]);
      setParams(newVersionId ? { version: newVersionId } : version ? { version: version.id } : {}, { replace: true });
    },
    onError: (error) => toast.error("Action failed", { description: describe(error) }),
  });

  const remove = useMutation({
    mutationFn: (input: { id: string; expected: number }) => deleteRule(input.id, input.expected),
    onSuccess: () => {
      toast.success("Rule deleted");
      setConfirmDelete(false);
      skipBlock.current = true;
      navigate(`/rules/${type.key}`, { replace: true });
      void queryClient.invalidateQueries({ queryKey: ["rules"] });
    },
    onError: (error) => toast.error("Delete failed", { description: describe(error) }),
  });
  const canDelete = Boolean(detail && can("Delete") && isRuleDeletable(detail));

  const onSave = () => {
    setShowIssues(true);
    if (issues.length > 0) {
      toast.error(`Fix ${issues.length} issue${issues.length === 1 ? "" : "s"} before saving`);
      jumpTo(issues[0]!);
      return;
    }
    save.mutate({ draft, versionId: version?.id ?? null, expected: detail?.version ?? 0 });
  };

  const sections: Array<{ id: SectionId; label: string; icon: typeof Settings2 }> = [
    { id: "basics", label: "Basics", icon: Settings2 },
    ...(type.showModel ? [{ id: "model" as const, label: "Model", icon: BrainCircuit }] : []),
    { id: "conditions", label: type.showModel ? "Decision" : "Conditions", icon: ListTree },
    { id: "messages", label: "Messages", icon: MessageSquareText },
    ...(type.showOutput ? [{ id: "output" as const, label: "Output", icon: Highlighter }] : []),
    ...(can("Test") ? [{ id: "test" as const, label: "Test", icon: FlaskConical }] : []),
  ];
  const activeSection = useActiveSection(sections.map((section) => section.id));

  const actions: Transition[] = !detail || !version || dirty ? [] : [
    ...(version.status === "Draft" && can("Review") ? ["submit" as const] : []),
    ...(version.status === "InReview" && can("Publish") ? ["publish" as const] : []),
    ...(version.status === "Published" && can("Edit") && !detail.versions.some((item) => item.status === "Draft") ? ["clone" as const] : []),
    ...(version.status === "Published" && can("Retire") ? ["retire" as const] : []),
  ];

  return (
    <div className="fsh-enter">
      <header className="sticky -top-4 z-20 -mx-4 -mt-4 mb-6 border-b border-[var(--color-border)] bg-[oklch(from_var(--color-background)_l_c_h_/_0.86)] px-4 py-3 backdrop-blur-md md:-top-6 md:-mx-6 md:-mt-6 md:px-6">
        <div className="flex flex-wrap items-center gap-3">
          <BackLink type={type} compact />
          <span aria-hidden className={cn("hidden h-8 w-1 rounded-full sm:block", SEVERITY_STRIPE[draft.outcome.severity])} />
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              {draft.code && <code className="rounded-md bg-[var(--color-muted)] px-1.5 py-0.5 font-mono text-[11px] font-semibold">{draft.code}</code>}
              <h1 className="truncate font-display text-[17px] font-semibold">{draft.name.trim() || `New ${type.label.toLowerCase()}`}</h1>
              {version && <LifecycleBadge status={version.status} />}
              {dirty && <span className="inline-flex items-center gap-1 text-[11px] font-medium text-[var(--color-warning)]"><span className="size-1.5 rounded-full bg-current" />Unsaved changes</span>}
            </div>
            <p className="text-[11.5px] text-[var(--color-muted-foreground)]">
              {type.label}{detail && version ? ` · version ${version.versionNumber} of ${detail.versions.length}` : " · draft"}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {detail && detail.versions.length > 1 && (
              <VersionMenu versions={detail.versions} selected={version} onSelect={(id) => setParams({ version: id }, { replace: true })} />
            )}
            {actions.map((kind) => (
              <Button key={kind} variant={kind === "retire" ? "outline" : kind === "publish" ? "saffron" : "outline"} onClick={() => setPending(kind)}>
                {kind === "clone" && <GitBranch className="size-4" />}
                {TRANSITION_COPY[kind].title}
              </Button>
            ))}
            {canDelete && (
              <Button variant="outline" aria-label="Delete rule" onClick={() => setConfirmDelete(true)} disabled={save.isPending || remove.isPending} className="text-[var(--color-destructive)]">
                <Trash2 className="size-4" />Delete
              </Button>
            )}
            {editable && (
              <Button onClick={onSave} disabled={save.isPending || (!dirty && !isNew)}>
                <Save className="size-4" />{save.isPending ? "Saving…" : isNew ? "Create draft" : "Save draft"}
              </Button>
            )}
          </div>
        </div>
      </header>

      {!editable && version && (
        <div className="mb-6 flex flex-wrap items-center gap-3 rounded-xl border border-[oklch(from_var(--color-info)_l_c_h_/_0.3)] bg-[oklch(from_var(--color-info)_l_c_h_/_0.07)] px-4 py-3 text-[13px]">
          <Lock className="size-4 text-[var(--color-info)]" />
          <span className="flex-1">
            Version {version.versionNumber} is <strong>{version.status === "InReview" ? "in review" : version.status.toLowerCase()}</strong> and read-only.
            {version.status === "Published" && can("Edit") ? " Create a new draft to make changes." : ""}
          </span>
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-[11rem_minmax(0,1fr)] 2xl:grid-cols-[11rem_minmax(0,1fr)_19rem]">
        <nav aria-label="Rule sections" className="hidden lg:block">
          <ol className="sticky top-20 space-y-0.5">
            {sections.map((section, index) => {
              const count = countFor(section.id);
              const Icon = section.icon;
              return (
                <li key={section.id}>
                  <a
                    href={`#${section.id}`}
                    onClick={(event) => { event.preventDefault(); document.getElementById(section.id)?.scrollIntoView({ behavior: "smooth", block: "start" }); }}
                    aria-current={activeSection === section.id ? "true" : undefined}
                    className={cn(
                      "flex items-center gap-2 rounded-lg px-2.5 py-2 text-[13px] font-medium transition-colors",
                      activeSection === section.id ? "bg-[var(--color-primary-soft)] text-[var(--color-primary)]" : "text-[var(--color-muted-foreground)] hover:bg-[var(--color-accent)] hover:text-[var(--color-foreground)]",
                    )}
                  >
                    <span className="font-mono text-[10px] opacity-60">{index + 1}</span>
                    <Icon className="size-4" />
                    <span className="flex-1">{section.label}</span>
                    {showIssues && count > 0 && <span className="rounded-full bg-[oklch(from_var(--color-warning)_l_c_h_/_0.15)] px-1.5 font-mono text-[10px] text-[var(--color-warning)]">{count}</span>}
                  </a>
                </li>
              );
            })}
          </ol>
        </nav>

        <div className="min-w-0 space-y-6">
          {sections.map((section, index) => (
            <div key={section.id}>
              {section.id === "basics" && (
                <SectionCard id="basics" step={index + 1} icon={Settings2} title="Basics" description="Identify the rule and how serious its findings are." issues={showIssues ? countFor("basics") : 0} complete={countFor("basics") === 0}>
                  <BasicsSection draft={draft} type={type} isNew={isNew} disabled={!editable} showIssues={showIssues} issues={issueMap} set={set} setOutcome={setOutcome} onDocumentChange={changeDocument} />
                </SectionCard>
              )}
              {section.id === "model" && (
                <SectionCard id="model" step={index + 1} icon={BrainCircuit} title="Model" description="Which model produces the values this rule decides on." issues={showIssues ? countFor("model") : 0} complete={countFor("model") === 0}>
                  <ModelSection outcome={draft.outcome} disabled={!editable} showIssues={showIssues} issues={issueMap} setOutcome={setOutcome} />
                </SectionCard>
              )}
              {section.id === "conditions" && (
                <SectionCard
                  id="conditions"
                  step={index + 1}
                  icon={ListTree}
                  title={type.showModel ? "Decision conditions" : "Conditions"}
                  description={type.conditionsHint}
                  issues={showIssues ? countFor("conditions") : 0}
                  complete={countFor("conditions") === 0}
                  actions={
                    <Segmented
                      label="Evaluation mode"
                      value={draft.outcome.mode}
                      disabled={!editable}
                      onChange={(mode) => setOutcome({ mode })}
                      options={[
                        { value: "flag", label: "Flag when matched", title: "Conditions describe the problem. A match raises a finding." },
                        { value: "pass", label: "Pass when matched", title: "Conditions describe the expected state. A mismatch raises a finding." },
                      ]}
                    />
                  }
                >
                  <p className="mb-3 flex items-start gap-2 text-[12.5px] text-[var(--color-muted-foreground)]">
                    <Info className="mt-0.5 size-3.5 shrink-0" />
                    {draft.outcome.mode === "flag"
                      ? "Describe the problem. A finding is raised when these conditions match."
                      : "Describe the expected state. A finding is raised when these conditions do not match."}
                  </p>
                  <ConditionBuilder root={draft.tree} onChange={(tree) => set({ tree })} disabled={!editable} columns={picker.columns} documentCode={picker.documentCode} showDocument={picker.showDocument} issues={issueMap} showIssues={showIssues} />
                  <div className="mt-4 rounded-xl border border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] px-4 py-3">
                    <p className="mb-1 text-[11px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">Formula</p>
                    <code data-testid="rule-formula" className="block whitespace-pre-wrap break-words font-mono text-[12.5px] leading-relaxed text-[var(--color-foreground)]">
                      {formulaText(draft.tree, { columns, documentCode: draft.outcome.documentCode })}
                    </code>
                  </div>
                </SectionCard>
              )}
              {section.id === "messages" && (
                <SectionCard id="messages" step={index + 1} icon={MessageSquareText} title="Messages" description="What reviewers see when this rule raises a finding." issues={showIssues ? countFor("messages") : 0} complete={countFor("messages") === 0}>
                  <MessagesSection outcome={draft.outcome} disabled={!editable} picker={picker} showIssues={showIssues} issues={issueMap} setOutcome={setOutcome} />
                </SectionCard>
              )}
              {section.id === "output" && (
                <SectionCard id="output" step={index + 1} icon={Highlighter} title="Output" description="How findings are grouped and highlighted in the report." issues={showIssues ? countFor("output") : 0} complete={countFor("output") === 0}>
                  <OutputSection outcome={draft.outcome} disabled={!editable} picker={picker} showIssues={showIssues} issues={issueMap} setOutcome={setOutcome} />
                </SectionCard>
              )}
              {section.id === "test" && (
                <SectionCard id="test" step={index + 1} icon={FlaskConical} title="Test" description="Run the current (unsaved) conditions against sample data before you save.">
                  <RuleTestPanel tree={draft.tree} mode={draft.outcome.mode} blocked={countFor("conditions") > 0} />
                </SectionCard>
              )}
            </div>
          ))}
        </div>

        <aside className="hidden 2xl:block">
          <div className="sticky top-20 space-y-4">
            <RailCard title="Plain-English preview" icon={ListTree}>
              <PreviewPanel draft={draft} columns={columns} />
            </RailCard>
            <RailCard title="Rule health" icon={issues.length ? AlertTriangle : CheckCircle2}>
              <dl className="grid grid-cols-3 gap-2 text-center">
                <Stat label="Conditions" value={stats.conditions} />
                <Stat label="Nodes" value={`${stats.nodes}/${LIMITS.maxNodes}`} warn={stats.nodes > LIMITS.maxNodes} />
                <Stat label="Issues" value={issues.length} warn={issues.length > 0} />
              </dl>
              {issues.length > 0 && (
                <ul className="mt-3 space-y-1">
                  {issues.slice(0, 6).map((issue) => (
                    <li key={`${issue.id}-${issue.message}`}>
                      <button type="button" onClick={() => { setShowIssues(true); jumpTo(issue); }} className="w-full cursor-pointer rounded-md px-2 py-1 text-left text-[12px] text-[var(--color-muted-foreground)] hover:bg-[var(--color-accent)] hover:text-[var(--color-foreground)]">
                        <AlertTriangle className="mr-1 inline size-3 text-[var(--color-warning)]" />{issue.message}
                      </button>
                    </li>
                  ))}
                  {issues.length > 6 && <li className="px-2 text-[11px] text-[var(--color-muted-foreground)]">+{issues.length - 6} more</li>}
                </ul>
              )}
            </RailCard>
            {detail && (
              <RailCard title="Version history" icon={History}>
                <ol className="space-y-1">
                  {detail.versions.map((item) => (
                    <li key={item.id}>
                      <button
                        type="button"
                        onClick={() => setParams({ version: item.id }, { replace: true })}
                        aria-current={item.id === version?.id ? "true" : undefined}
                        className={cn("flex w-full cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-left text-[12.5px] transition-colors", item.id === version?.id ? "bg-[var(--color-primary-soft)]" : "hover:bg-[var(--color-accent)]")}
                      >
                        <span className="font-mono font-semibold">v{item.versionNumber}</span>
                        <LifecycleBadge status={item.status} />
                        <span className="ml-auto text-[11px] text-[var(--color-muted-foreground)]">{new Date(item.publishedAtUtc ?? item.createdAtUtc).toLocaleDateString()}</span>
                      </button>
                    </li>
                  ))}
                </ol>
              </RailCard>
            )}
          </div>
        </aside>
      </div>

      <Dialog open={pending !== null} onOpenChange={(open) => !open && setPending(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>{pending ? TRANSITION_COPY[pending].title : ""}</DialogTitle>
            <DialogDescription>{pending ? TRANSITION_COPY[pending].body : ""}</DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPending(null)} disabled={transition.isPending}>Cancel</Button>
            <Button
              variant={pending === "retire" ? "destructive" : "default"}
              disabled={transition.isPending}
              onClick={() => pending && detail && version && transition.mutate({ kind: pending, id: detail.id, versionId: version.id, expected: detail.version })}
            >
              {pending ? TRANSITION_COPY[pending].confirm : "Confirm"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={confirmDelete} onOpenChange={(open) => !open && !remove.isPending && setConfirmDelete(false)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Delete rule</DialogTitle>
            <DialogDescription>
              Permanently delete <strong>{detail?.name}</strong> (<code className="font-mono">{detail?.stableCode}</code>) and all its draft versions? It is also removed from document settings. This cannot be undone.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmDelete(false)} disabled={remove.isPending}>Cancel</Button>
            <Button variant="destructive" disabled={remove.isPending} onClick={() => detail && remove.mutate({ id: detail.id, expected: detail.version })}>
              {remove.isPending ? "Deleting…" : "Delete"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={blocker.state === "blocked"} onOpenChange={(open) => !open && blocker.reset?.()}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Discard unsaved changes?</DialogTitle>
            <DialogDescription>You have edits that haven't been saved. Leaving now will lose them.</DialogDescription>
          </DialogHeader>
          <DialogBody />
          <DialogFooter>
            <Button variant="outline" onClick={() => blocker.reset?.()}>Keep editing</Button>
            <Button variant="destructive" onClick={() => blocker.proceed?.()}>Discard changes</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function collectIssues(draft: Draft, type: RuleTypeConfig, isNew: boolean): Issue[] {
  const issues: Issue[] = [];
  if (isNew && !CODE_PATTERN.test(draft.code.trim())) issues.push({ id: "code", section: "basics", message: "Rule code is required: letters, numbers, dots, dashes, or underscores (max 64)." });
  if (!draft.name.trim()) issues.push({ id: "name", section: "basics", message: "Give the rule a display name." });
  if (draft.name.trim().length > 200) issues.push({ id: "name", section: "basics", message: "Display name must be 200 characters or fewer." });
  if (draft.description.trim().length > 2000) issues.push({ id: "description", section: "basics", message: "Description must be 2000 characters or fewer." });
  if (!draft.outcome.documentCode) issues.push({ id: "document", section: "basics", message: "Choose the document this rule checks." });
  issues.push(...validateTree(draft.tree));
  const { outcome } = draft;
  if (!outcome.messages.some((message) => message.text.trim())) issues.push({ id: "messages", section: "messages", message: "Add at least one message so reviewers know what went wrong." });
  outcome.messages.forEach((message) => {
    if (message.text.trim().length > 1000) issues.push({ id: message.id, section: "messages", message: "Messages must be 1000 characters or fewer." });
    if (message.text.includes("#PrimaryKey#") && !outcome.primaryKey?.path.trim()) issues.push({ id: message.id, section: "messages", message: "This message uses #PrimaryKey# but no primary key column is selected." });
    if (message.field.path.trim() && !message.field.documentCode) issues.push({ id: message.id, section: "messages", message: "Choose the document for the message column." });
  });
  if (type.showOutput) {
    outcome.highlights.forEach((item) => {
      if (!item.field.documentCode || !item.field.path.trim()) issues.push({ id: item.id, section: "output", message: "Choose a column to highlight, or remove the row." });
    });
    if (outcome.similarityThreshold !== null && (outcome.similarityThreshold < 0 || outcome.similarityThreshold > 100)) issues.push({ id: "similarity", section: "output", message: "Similarity threshold must be between 0 and 100." });
  }
  if (type.showModel) {
    if (!outcome.model.name.trim()) issues.push({ id: "model-name", section: "model", message: "Name the model this rule relies on." });
    const confidence = outcome.model.confidenceThreshold;
    if (confidence !== null && (confidence < 0 || confidence > 100)) issues.push({ id: "model-confidence", section: "model", message: "Confidence threshold must be between 0 and 100." });
  }
  return issues;
}

function jumpTo(issue: Issue) {
  const target = document.querySelector(`[data-issue-id="${issue.id}"]`) ?? document.getElementById(issue.section);
  target?.scrollIntoView({ behavior: "smooth", block: "center" });
}

function useActiveSection(ids: string[]) {
  const [active, setActive] = useState(ids[0] ?? "");
  const key = ids.join("|");
  useEffect(() => {
    if (typeof IntersectionObserver === "undefined") return;
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter((entry) => entry.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
        if (visible) setActive(visible.target.id);
      },
      { rootMargin: "-120px 0px -55% 0px" },
    );
    key.split("|").forEach((id) => {
      const element = document.getElementById(id);
      if (element) observer.observe(element);
    });
    return () => observer.disconnect();
  }, [key]);
  return active;
}

function IssueText({ id, issues, show }: { id: string; issues: ReadonlyMap<string, string>; show: boolean }) {
  const message = show ? issues.get(id) : undefined;
  return message ? <p role="alert" className="text-[12px] text-[var(--color-warning)]">{message}</p> : null;
}

function BasicsSection({ draft, type, isNew, disabled, showIssues, issues, set, setOutcome, onDocumentChange }: {
  draft: Draft;
  type: RuleTypeConfig;
  isNew: boolean;
  disabled: boolean;
  showIssues: boolean;
  issues: ReadonlyMap<string, string>;
  set: (patch: Partial<Draft>) => void;
  setOutcome: (patch: Partial<RuleOutcome>) => void;
  onDocumentChange: (code: string) => void;
}) {
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <div data-issue-id="code">
        <Field id="rule-code" label="Rule code" required hint={isNew ? "Unique within your workspace, e.g. COM_DATE_BLN_TARG. Cannot change later." : "Rule codes are permanent."}>
          <Input id="rule-code" value={draft.code} disabled={disabled || !isNew} maxLength={64} placeholder="COM_DATE_BLN_TARG" className="font-mono uppercase" onChange={(event) => set({ code: event.target.value.toUpperCase() })} />
        </Field>
        <IssueText id="code" issues={issues} show={showIssues} />
      </div>
      <div data-issue-id="name">
        <Field id="rule-name" label="Display name" required>
          <Input id="rule-name" value={draft.name} disabled={disabled} maxLength={200} placeholder="Recommended action not completed before target date" onChange={(event) => set({ name: event.target.value })} />
        </Field>
        <IssueText id="name" issues={issues} show={showIssues} />
      </div>
      <div className="md:col-span-2" data-issue-id="description">
        <Field id="rule-description" label="Description" hint="Explain why the rule exists. Shown on the rule card and to reviewers.">
          <textarea id="rule-description" rows={3} maxLength={2000} className={textareaClass} value={draft.description} disabled={disabled} onChange={(event) => set({ description: event.target.value })} />
        </Field>
        <IssueText id="description" issues={issues} show={showIssues} />
      </div>
      <div className="space-y-1.5" data-issue-id="document">
        <span className="text-[11.5px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">Document<span aria-hidden className="ml-1.5 text-[var(--color-destructive)]">·</span></span>
        <DocumentPicker label="Rule document" value={draft.outcome.documentCode} disabled={disabled} placeholder="Select document" onChange={onDocumentChange} />
        <p className="text-[11.5px] text-[var(--color-muted-foreground)]">The PPAP document whose columns this rule checks. Changing it moves every column to the new document.</p>
        <IssueText id="document" issues={issues} show={showIssues} />
      </div>
      <div className="space-y-1.5">
        <span id="rule-severity-label" className="text-[11.5px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">Severity</span>
        <div>
          <Segmented
            label="Severity"
            value={draft.outcome.severity}
            disabled={disabled}
            onChange={(severity) => setOutcome({ severity })}
            options={SEVERITIES.map((value) => ({ value, label: <><span aria-hidden className={cn("size-2 rounded-full", SEVERITY_STRIPE[value])} />{value}</> }))}
          />
        </div>
      </div>
      {type.categories.length > 1 && (
        <div className="space-y-1.5">
          <span className="text-[11.5px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">Scope</span>
          <div>
            <Segmented label="Scope" value={draft.category} disabled={disabled} onChange={(category) => set({ category })} options={type.categories.map((value) => ({ value, label: CATEGORY_LABELS[value] }))} />
          </div>
        </div>
      )}
      <div className="md:col-span-2">
        <Field id="rule-tags" label="Tags" hint="Group related rules, e.g. DFMEA, Dates, Customer-X. Press Enter to add.">
          <TagInput tags={draft.outcome.tags} disabled={disabled} onChange={(tags) => setOutcome({ tags })} />
        </Field>
      </div>
    </div>
  );
}

function ModelSection({ outcome, disabled, showIssues, issues, setOutcome }: { outcome: RuleOutcome; disabled: boolean; showIssues: boolean; issues: ReadonlyMap<string, string>; setOutcome: (patch: Partial<RuleOutcome>) => void }) {
  const setModel = (patch: Partial<RuleOutcome["model"]>) => setOutcome({ model: { ...outcome.model, ...patch } });
  return (
    <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_12rem]">
      <div data-issue-id="model-name">
        <Field id="model-name" label="Model" required hint="The extraction or classification model that produces the decision fields.">
          <Input id="model-name" value={outcome.model.name} disabled={disabled} maxLength={128} placeholder="ppap-dfmea-classifier" onChange={(event) => setModel({ name: event.target.value })} />
        </Field>
        <IssueText id="model-name" issues={issues} show={showIssues} />
      </div>
      <div data-issue-id="model-confidence">
        <Field id="model-confidence" label="Min. confidence %">
          <Input id="model-confidence" type="number" min={0} max={100} value={outcome.model.confidenceThreshold ?? ""} disabled={disabled} placeholder="80" onChange={(event) => setModel({ confidenceThreshold: event.target.value === "" ? null : Number(event.target.value) })} />
        </Field>
        <IssueText id="model-confidence" issues={issues} show={showIssues} />
      </div>
      <div className="md:col-span-2">
        <Field id="model-prompt" label="Prompt / instructions" hint="Optional guidance sent with the document when the model runs.">
          <textarea id="model-prompt" rows={4} maxLength={4000} className={textareaClass} value={outcome.model.prompt} disabled={disabled} placeholder="Check whether every failure mode with severity ≥ 9 has a recommended action…" onChange={(event) => setModel({ prompt: event.target.value })} />
        </Field>
      </div>
    </div>
  );
}

function MessagesSection({ outcome, disabled, picker, showIssues, issues, setOutcome }: {
  outcome: RuleOutcome;
  disabled: boolean;
  picker: Picker;
  showIssues: boolean;
  issues: ReadonlyMap<string, string>;
  setOutcome: (patch: Partial<RuleOutcome>) => void;
}) {
  const updateMessage = (id: string, patch: Partial<RuleOutcome["messages"][number]>) =>
    setOutcome({ messages: outcome.messages.map((message) => (message.id === id ? { ...message, ...patch } : message)) });
  return (
    <div className="space-y-5">
      <div className="grid gap-2 rounded-xl bg-[oklch(from_var(--color-muted)_l_c_h_/_0.4)] p-4 md:grid-cols-[12rem_minmax(0,1fr)_auto] md:items-center">
        <div>
          <p className="text-[12.5px] font-semibold">Primary key</p>
          <p className="text-[11.5px] text-[var(--color-muted-foreground)]">Use <code className="rounded bg-[var(--color-card)] px-1 font-mono text-[11px] text-[var(--color-primary)]">#PrimaryKey#</code> in a message to insert this value.</p>
        </div>
        <ColumnPicker label="Primary key" value={outcome.primaryKey ?? emptyField()} disabled={disabled} columns={picker.columns} documentCode={picker.documentCode} showDocument={picker.showDocument} placeholder="Select primary key column" onChange={(field) => setOutcome({ primaryKey: field.path ? field : null, consolidate: field.path ? outcome.consolidate : false })} />
        {!disabled && outcome.primaryKey && <Button type="button" variant="ghost" size="sm" onClick={() => setOutcome({ primaryKey: null, consolidate: false })}>Clear</Button>}
      </div>

      <ul className="space-y-3" data-issue-id="messages">
        {outcome.messages.map((message, index) => (
          <li key={message.id} data-issue-id={message.id} className="rounded-xl border border-[var(--color-border)] p-3">
            <div className="grid gap-2 md:grid-cols-[minmax(0,15rem)_minmax(0,1fr)_auto] md:items-start">
              <div>
                <p className="mb-1 text-[11px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">Column <span className="font-normal normal-case">(optional)</span></p>
                <ColumnPicker label={`Message ${index + 1} column`} value={message.field} disabled={disabled} columns={picker.columns} documentCode={picker.documentCode} showDocument={picker.showDocument} onChange={(field) => updateMessage(message.id, { field })} />
              </div>
              <div>
                <p className="mb-1 text-[11px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">Message</p>
                <Input aria-label={`Message ${index + 1} text`} value={message.text} disabled={disabled} maxLength={1000} placeholder="Recommended action for #PrimaryKey# is not completed before the target date" onChange={(event) => updateMessage(message.id, { text: event.target.value })} />
                {!disabled && outcome.primaryKey && !message.text.includes("#PrimaryKey#") && (
                  <button type="button" onClick={() => updateMessage(message.id, { text: `${message.text}${message.text && !message.text.endsWith(" ") ? " " : ""}#PrimaryKey#` })} className="mt-1 cursor-pointer text-[11.5px] font-medium text-[var(--color-primary)] hover:underline">
                    + Insert #PrimaryKey#
                  </button>
                )}
              </div>
              {!disabled && (
                <Button type="button" variant="ghost" size="icon-xs" aria-label={`Remove message ${index + 1}`} className="md:mt-6" disabled={outcome.messages.length === 1} onClick={() => setOutcome({ messages: outcome.messages.filter((item) => item.id !== message.id) })}>
                  <Trash2 />
                </Button>
              )}
            </div>
            <IssueText id={message.id} issues={issues} show={showIssues} />
          </li>
        ))}
      </ul>
      <IssueText id="messages" issues={issues} show={showIssues} />
      {!disabled && (
        <Button type="button" variant="soft" size="sm" disabled={outcome.messages.length >= 20} onClick={() => setOutcome({ messages: [...outcome.messages, { id: newId("m"), field: emptyField(), text: "" }] })}>
          <Plus />Add message
        </Button>
      )}
    </div>
  );
}

function OutputSection({ outcome, disabled, picker, showIssues, issues, setOutcome }: {
  outcome: RuleOutcome;
  disabled: boolean;
  picker: Picker;
  showIssues: boolean;
  issues: ReadonlyMap<string, string>;
  setOutcome: (patch: Partial<RuleOutcome>) => void;
}) {
  const hasPrimaryKey = Boolean(outcome.primaryKey?.path.trim());
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-4 rounded-xl border border-[var(--color-border)] p-4">
        <Switch id="rule-consolidate" aria-label="Consolidate findings" checked={outcome.consolidate && hasPrimaryKey} disabled={disabled || !hasPrimaryKey} onCheckedChange={(consolidate) => setOutcome({ consolidate })} />
        <div className="min-w-0 flex-1">
          <label htmlFor="rule-consolidate" className="text-[13px] font-semibold">Consolidate findings</label>
          <p className="text-[12px] text-[var(--color-muted-foreground)]">
            {hasPrimaryKey
              ? <>Merge findings that share the same <strong>{columnLabel(outcome.primaryKey!, picker.columns)}</strong> into one entry.</>
              : "Select a primary key in Messages to enable consolidation."}
          </p>
        </div>
      </div>

      <div>
        <div className="mb-2 flex items-center justify-between">
          <p className="text-[13px] font-semibold">Highlights</p>
          <p className="text-[11.5px] text-[var(--color-muted-foreground)]">Cells to colour in the reviewed document</p>
        </div>
        {outcome.highlights.length === 0 && <p className="rounded-xl border border-dashed border-[var(--color-border)] px-4 py-5 text-center text-[12.5px] text-[var(--color-muted-foreground)]">No highlights yet. Findings will appear without cell colouring.</p>}
        <ul className="space-y-2">
          {outcome.highlights.map((item, index) => (
            <li key={item.id} data-issue-id={item.id} className="space-y-1">
              <div className="flex flex-wrap items-center gap-2">
                <div className="min-w-[16rem] flex-1">
                  <ColumnPicker label={`Highlight ${index + 1} column`} value={item.field} disabled={disabled} columns={picker.columns} documentCode={picker.documentCode} showDocument={picker.showDocument} onChange={(field) => setOutcome({ highlights: outcome.highlights.map((value) => (value.id === item.id ? { ...value, field } : value)) })} />
                </div>
                <ColorPicker label={`Highlight ${index + 1} colour`} value={item.color} disabled={disabled} onChange={(color) => setOutcome({ highlights: outcome.highlights.map((value) => (value.id === item.id ? { ...value, color } : value)) })} />
                {!disabled && (
                  <Button type="button" variant="ghost" size="icon-xs" aria-label={`Remove highlight ${index + 1}`} onClick={() => setOutcome({ highlights: outcome.highlights.filter((value) => value.id !== item.id) })}>
                    <Trash2 />
                  </Button>
                )}
              </div>
              <IssueText id={item.id} issues={issues} show={showIssues} />
            </li>
          ))}
        </ul>
        {!disabled && (
          <Button type="button" variant="soft" size="sm" className="mt-3" disabled={outcome.highlights.length >= 20} onClick={() => setOutcome({ highlights: [...outcome.highlights, { id: newId("h"), field: { documentCode: picker.documentCode, path: "" }, color: SEVERITY_DEFAULT_COLOR[outcome.severity] }] })}>
            <Plus />Add highlight
          </Button>
        )}
      </div>

      <div className="grid gap-2 md:grid-cols-[minmax(0,1fr)_7rem] md:items-end" data-issue-id="similarity">
        <Field id="rule-similarity" label="Comparison ratio (similarity %)" hint="For text comparisons: values at or above this similarity count as a match. Leave empty for exact matching.">
          <input
            id="rule-similarity-range"
            aria-label="Similarity threshold slider"
            type="range"
            min={0}
            max={100}
            disabled={disabled}
            value={outcome.similarityThreshold ?? 100}
            onChange={(event) => setOutcome({ similarityThreshold: Number(event.target.value) })}
            className="w-full accent-[var(--color-primary)]"
          />
        </Field>
        <Input id="rule-similarity" type="number" min={0} max={100} placeholder="exact" disabled={disabled} value={outcome.similarityThreshold ?? ""} onChange={(event) => setOutcome({ similarityThreshold: event.target.value === "" ? null : Number(event.target.value) })} />
        <div className="md:col-span-2"><IssueText id="similarity" issues={issues} show={showIssues} /></div>
      </div>
    </div>
  );
}

function PreviewPanel({ draft, columns }: { draft: Draft; columns: ColumnIndex }) {
  const lines = useMemo(() => previewLines(draft.tree, { columns, documentCode: draft.outcome.documentCode }), [draft.tree, columns, draft.outcome.documentCode]);
  const message = draft.outcome.messages.find((item) => item.text.trim())?.text;
  return (
    <div className="space-y-3 text-[12.5px]">
      <p className="font-semibold">
        {draft.outcome.mode === "flag" ? "Raise a finding when" : "Raise a finding unless"}
      </p>
      <ul className="space-y-1 font-mono text-[11.5px]" data-testid="rule-preview">
        {lines.map((line) => (
          <li key={line.id} style={{ paddingLeft: `${line.depth * 12}px` }} className={line.kind === "group" ? "font-semibold text-[var(--color-primary)]" : "text-[var(--color-foreground)]"}>
            {line.kind === "condition" ? "• " : ""}{line.text}
          </li>
        ))}
      </ul>
      <div className="flex items-center gap-2 border-t border-[var(--color-border)] pt-3">
        <span>then report</span>
        <SeverityBadge severity={draft.outcome.severity} />
      </div>
      {message && <p className="rounded-lg bg-[oklch(from_var(--color-muted)_l_c_h_/_0.5)] px-2.5 py-2 italic text-[var(--color-muted-foreground)]">“{message}”</p>}
    </div>
  );
}

function RailCard({ title, icon: Icon, children }: { title: string; icon: typeof Info; children: ReactNode }) {
  return (
    <section className="rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-4 shadow-xs">
      <h2 className="mb-3 flex items-center gap-2 text-[11.5px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">
        <Icon className="size-3.5" />{title}
      </h2>
      {children}
    </section>
  );
}

function Stat({ label, value, warn }: { label: string; value: ReactNode; warn?: boolean }) {
  return (
    <div className="rounded-lg bg-[oklch(from_var(--color-muted)_l_c_h_/_0.5)] px-1 py-2">
      <dd className={cn("font-display text-[15px] font-semibold", warn && "text-[var(--color-warning)]")}>{value}</dd>
      <dt className="text-[10.5px] uppercase tracking-wider text-[var(--color-muted-foreground)]">{label}</dt>
    </div>
  );
}

function VersionMenu({ versions, selected, onSelect }: { versions: RuleVersionMetadataDto[]; selected: RuleVersionMetadataDto | null; onSelect: (id: string) => void }) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" aria-label="Choose version">
          <History className="size-4" />v{selected?.versionNumber ?? "?"}<ChevronDown className="size-3.5" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {versions.map((item) => (
          <DropdownMenuItem key={item.id} onSelect={() => onSelect(item.id)}>
            <span className="font-mono font-semibold">v{item.versionNumber}</span>
            <LifecycleBadge status={item.status} />
            <span className="ml-auto text-[11px] text-[var(--color-muted-foreground)]">{new Date(item.createdAtUtc).toLocaleDateString()}</span>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function BackLink({ type, compact }: { type: RuleTypeConfig; compact?: boolean }) {
  return (
    <Link to={`/rules/${type.key}`} className="inline-flex items-center gap-1.5 rounded-md text-[13px] font-medium text-[var(--color-muted-foreground)] transition-colors hover:text-[var(--color-foreground)]" aria-label={`Back to ${type.plural}`}>
      <ArrowLeft className="size-4" />
      {!compact && type.plural}
    </Link>
  );
}

function EditorSkeleton() {
  return (
    <div role="status" aria-busy="true" className="space-y-6">
      <span className="sr-only">Loading rule…</span>
      <Skeleton className="h-14 w-full rounded-xl" />
      <div className="grid gap-6 lg:grid-cols-[11rem_minmax(0,1fr)] 2xl:grid-cols-[11rem_minmax(0,1fr)_19rem]">
        <Skeleton className="hidden h-48 rounded-xl lg:block" />
        <div className="space-y-6">
          <Skeleton className="h-64 rounded-2xl" />
          <Skeleton className="h-80 rounded-2xl" />
        </div>
        <Skeleton className="hidden h-72 rounded-2xl 2xl:block" />
      </div>
    </div>
  );
}
