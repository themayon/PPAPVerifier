import { createContext, useContext, useMemo, type ReactNode } from "react";
import { ArrowDown, ArrowUp, Copy, FolderPlus, Plus, Trash2 } from "lucide-react";
import { Combobox } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/cn";
import {
  LIMITS,
  duplicateNode,
  isUnary,
  moveNode,
  newCondition,
  newGroup,
  operatorLabel,
  operatorsFor,
  removeNode,
  updateNode,
  type BuilderNode,
  type ConditionNode,
  type FieldRef,
  type GroupNode,
  type ValueSource,
  type ValueType,
} from "./rule-model";
import { FieldPicker, Segmented } from "./rule-ui";

type BuilderApi = {
  disabled: boolean;
  suggestions: readonly FieldRef[];
  issues: ReadonlyMap<string, string>;
  showIssues: boolean;
  update: (id: string, fn: (node: BuilderNode) => BuilderNode) => void;
  remove: (id: string) => void;
  move: (id: string, delta: -1 | 1) => void;
  duplicate: (id: string) => void;
  defaultDocument: string;
};

const BuilderContext = createContext<BuilderApi | null>(null);
const useBuilder = () => {
  const api = useContext(BuilderContext);
  if (!api) throw new Error("ConditionBuilder context missing");
  return api;
};

const DEPTH_ACCENTS = [
  "border-l-[var(--color-primary)]",
  "border-l-[var(--color-info)]",
  "border-l-[var(--color-saffron)]",
  "border-l-[var(--color-success)]",
];

const VALUE_TYPES: ReadonlyArray<{ value: ValueType; label: string }> = [
  { value: "text", label: "Text" },
  { value: "number", label: "Number" },
  { value: "date", label: "Date" },
  { value: "boolean", label: "Yes / No" },
  { value: "list", label: "List" },
];

const SOURCES: ReadonlyArray<{ value: ValueSource; label: string; title: string }> = [
  { value: "value", label: "Value", title: "Compare with a fixed value" },
  { value: "field", label: "Field", title: "Compare with another document field" },
  { value: "parameter", label: "Param", title: "Compare with a template parameter" },
];

export function ConditionBuilder({
  root,
  onChange,
  disabled = false,
  suggestions,
  issues,
  showIssues,
}: {
  root: GroupNode;
  onChange: (root: GroupNode) => void;
  disabled?: boolean;
  suggestions: readonly FieldRef[];
  issues: ReadonlyMap<string, string>;
  showIssues: boolean;
}) {
  const defaultDocument = useMemo(() => {
    const find = (node: BuilderNode): string => node.kind === "condition" ? node.field.documentCode : node.children.map(find).find(Boolean) ?? "";
    return find(root);
  }, [root]);
  const api: BuilderApi = {
    disabled,
    suggestions,
    issues,
    showIssues,
    defaultDocument,
    update: (id, fn) => onChange(updateNode(root, id, fn)),
    remove: (id) => onChange(removeNode(root, id)),
    move: (id, delta) => onChange(moveNode(root, id, delta)),
    duplicate: (id) => onChange(duplicateNode(root, id)),
  };
  return (
    <BuilderContext.Provider value={api}>
      <GroupView group={root} depth={0} index={0} siblings={1} />
    </BuilderContext.Provider>
  );
}

function GroupView({ group, depth, index, siblings }: { group: GroupNode; depth: number; index: number; siblings: number }) {
  const api = useBuilder();
  const isRoot = depth === 0;
  const canNest = depth < LIMITS.maxDepth - 4;
  const issue = api.showIssues ? api.issues.get(group.id) : undefined;
  const setGroup = (patch: Partial<GroupNode>) => api.update(group.id, (node) => ({ ...(node as GroupNode), ...patch }));
  const add = (node: BuilderNode) => setGroup({ children: [...group.children, node] });
  const connector = group.combinator === "all" ? "AND" : "OR";

  return (
    <div
      data-testid={isRoot ? "condition-root" : "condition-group"}
      data-issue-id={group.id}
      className={cn(
        "rounded-xl border border-[var(--color-border)] border-l-[3px]",
        DEPTH_ACCENTS[depth % DEPTH_ACCENTS.length],
        isRoot ? "bg-[oklch(from_var(--color-muted)_l_c_h_/_0.25)]" : "bg-[var(--color-card)]",
        group.negate && "ring-1 ring-[oklch(from_var(--color-destructive)_l_c_h_/_0.35)]",
      )}
    >
      <div className="flex flex-wrap items-center gap-2 border-b border-[oklch(from_var(--color-border)_l_c_h_/_0.5)] px-3 py-2">
        <button
          type="button"
          aria-pressed={group.negate}
          disabled={api.disabled}
          onClick={() => setGroup({ negate: !group.negate })}
          title="Negate this group (NOT)"
          className={cn(
            "h-6 cursor-pointer rounded-md px-2 font-mono text-[11px] font-semibold tracking-wider transition-colors disabled:cursor-not-allowed",
            group.negate
              ? "bg-[var(--color-destructive)] text-[var(--color-destructive-foreground)]"
              : "border border-dashed border-[var(--color-border)] text-[var(--color-muted-foreground)] hover:text-[var(--color-foreground)]",
          )}
        >
          NOT
        </button>
        <span className="text-[12.5px] text-[var(--color-muted-foreground)]">Match</span>
        <Segmented
          label={isRoot ? "Root match mode" : "Group match mode"}
          size="sm"
          value={group.combinator}
          disabled={api.disabled}
          onChange={(combinator) => setGroup({ combinator })}
          options={[{ value: "all", label: "ALL" }, { value: "any", label: "ANY" }]}
        />
        <span className="text-[12.5px] text-[var(--color-muted-foreground)]">of the following</span>
        <span className="ml-auto" />
        {!isRoot && !api.disabled && (
          <RowActions id={group.id} index={index} siblings={siblings} label="group" />
        )}
      </div>

      <div className="space-y-0 p-3">
        {group.children.length === 0 && (
          <p className="rounded-lg border border-dashed border-[var(--color-border)] px-3 py-4 text-center text-[12.5px] text-[var(--color-muted-foreground)]">
            Empty group. Add a condition below.
          </p>
        )}
        {group.children.map((child, childIndex) => (
          <div key={child.id}>
            {childIndex > 0 && (
              <div className="relative flex h-7 items-center pl-5" aria-hidden>
                <span className="absolute bottom-0 left-[1.6rem] top-0 w-px bg-[var(--color-border)]" />
                <button
                  type="button"
                  tabIndex={-1}
                  disabled={api.disabled}
                  onClick={() => setGroup({ combinator: group.combinator === "all" ? "any" : "all" })}
                  title="Click to switch between AND / OR"
                  className={cn(
                    "relative z-[1] h-5 cursor-pointer rounded-full px-2 font-mono text-[10px] font-bold tracking-wider disabled:cursor-default",
                    connector === "AND"
                      ? "bg-[var(--color-primary-soft)] text-[var(--color-primary)]"
                      : "bg-[oklch(from_var(--color-info)_l_c_h_/_0.12)] text-[var(--color-info)]",
                  )}
                >
                  {connector}
                </button>
              </div>
            )}
            {child.kind === "group"
              ? <GroupView group={child} depth={depth + 1} index={childIndex} siblings={group.children.length} />
              : <ConditionRow condition={child} index={childIndex} siblings={group.children.length} />}
          </div>
        ))}
        {issue && <p role="alert" className="mt-2 text-[12px] text-[var(--color-warning)]">{issue}</p>}
        {!api.disabled && (
          <div className="mt-3 flex flex-wrap gap-2">
            <Button
              type="button"
              size="xs"
              variant="soft"
              disabled={group.children.length >= LIMITS.maxChildren}
              onClick={() => add(newCondition(api.defaultDocument))}
            >
              <Plus />Condition
            </Button>
            <Button
              type="button"
              size="xs"
              variant="outline"
              disabled={!canNest || group.children.length >= LIMITS.maxChildren}
              title={canNest ? "Add a nested group" : "Maximum nesting reached"}
              onClick={() => add(newGroup(group.combinator === "all" ? "any" : "all", [newCondition(api.defaultDocument)]))}
            >
              <FolderPlus />Group
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}

function RowActions({ id, index, siblings, label }: { id: string; index: number; siblings: number; label: string }) {
  const api = useBuilder();
  return (
    <div className="flex items-center gap-0.5">
      <IconAction label={`Move ${label} up`} disabled={index === 0} onClick={() => api.move(id, -1)}><ArrowUp /></IconAction>
      <IconAction label={`Move ${label} down`} disabled={index >= siblings - 1} onClick={() => api.move(id, 1)}><ArrowDown /></IconAction>
      <IconAction label={`Duplicate ${label}`} onClick={() => api.duplicate(id)}><Copy /></IconAction>
      <IconAction label={`Delete ${label}`} destructive onClick={() => api.remove(id)}><Trash2 /></IconAction>
    </div>
  );
}

function IconAction({ label, onClick, disabled, destructive, children }: { label: string; onClick: () => void; disabled?: boolean; destructive?: boolean; children: ReactNode }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        "grid size-7 cursor-pointer place-items-center rounded-md text-[var(--color-muted-foreground)] transition-colors [&_svg]:size-3.5",
        "hover:bg-[var(--color-accent)] hover:text-[var(--color-foreground)] disabled:cursor-not-allowed disabled:opacity-30",
        destructive && "hover:bg-[oklch(from_var(--color-destructive)_l_c_h_/_0.1)] hover:text-[var(--color-destructive)]",
      )}
    >
      {children}
    </button>
  );
}

function ConditionRow({ condition, index, siblings }: { condition: ConditionNode; index: number; siblings: number }) {
  const api = useBuilder();
  const issue = api.showIssues ? api.issues.get(condition.id) : undefined;
  const set = (patch: Partial<ConditionNode>) => api.update(condition.id, (node) => ({ ...(node as ConditionNode), ...patch }));
  const allowed = operatorsFor(condition.valueType);
  const unary = isUnary(condition.operator);

  const changeType = (valueType: ValueType) => {
    const operators = operatorsFor(valueType);
    set({
      valueType,
      operator: operators.includes(condition.operator) ? condition.operator : operators[0]!,
      value: valueType === "boolean" ? "true" : condition.valueType === "boolean" ? "" : condition.value,
    });
  };

  return (
    <div
      data-testid="condition-row"
      data-issue-id={condition.id}
      className={cn(
        "rounded-lg border bg-[var(--color-card)] p-3 transition-colors",
        issue ? "border-[oklch(from_var(--color-warning)_l_c_h_/_0.6)]" : "border-[var(--color-border)] hover:border-[var(--color-border-strong)]",
      )}
    >
      <div className="flex flex-wrap items-start gap-2">
        <div className="min-w-[16rem] flex-[2]">
          <FieldPicker label={`Condition ${index + 1}`} value={condition.field} onChange={(field) => set({ field })} disabled={api.disabled} suggestions={api.suggestions} invalid={Boolean(issue)} />
        </div>
        <div className="w-[9.5rem] shrink-0">
          <Combobox
            label={`Condition ${index + 1} operator`}
            value={condition.operator}
            onChange={(value) => value && set({ operator: value as ConditionNode["operator"] })}
            options={allowed.map((operator) => ({ value: operator, label: operatorLabel(operator, condition.valueType) }))}
            disabled={api.disabled}
          />
        </div>
        {!api.disabled && <div className="ml-auto"><RowActions id={condition.id} index={index} siblings={siblings} label="condition" /></div>}
      </div>

      {!unary && (
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <span className="text-[11px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">Compare to</span>
          <Segmented label={`Condition ${index + 1} compare source`} size="sm" value={condition.source} disabled={api.disabled} onChange={(source) => set({ source })} options={SOURCES} />
          {condition.source === "value" && (
            <div className="w-[7.5rem]">
              <Combobox label={`Condition ${index + 1} value type`} value={condition.valueType} onChange={(value) => value && changeType(value as ValueType)} options={[...VALUE_TYPES]} disabled={api.disabled} />
            </div>
          )}
          <div className="min-w-[12rem] flex-1">
            {condition.source === "field" && (
              <FieldPicker label={`Condition ${index + 1} compare`} value={condition.valueField} onChange={(valueField) => set({ valueField })} disabled={api.disabled} suggestions={api.suggestions} invalid={Boolean(issue)} compact />
            )}
            {condition.source === "parameter" && (
              <Input aria-label={`Condition ${index + 1} parameter`} placeholder="parameterName" value={condition.parameter} disabled={api.disabled} onChange={(event) => set({ parameter: event.target.value })} className="font-mono text-[12.5px]" />
            )}
            {condition.source === "value" && <ValueInput condition={condition} index={index} onChange={(value) => set({ value })} />}
          </div>
          <label className="inline-flex cursor-pointer select-none items-center gap-1.5 text-[12px] text-[var(--color-muted-foreground)]">
            <input
              type="checkbox"
              className="size-3.5 accent-[var(--color-primary)]"
              checked={condition.includeEmpty}
              disabled={api.disabled}
              onChange={(event) => set({ includeEmpty: event.target.checked })}
            />
            Also match when empty
          </label>
        </div>
      )}
      {issue && <p role="alert" className="mt-2 text-[12px] text-[var(--color-warning)]">{issue}</p>}
    </div>
  );
}

function ValueInput({ condition, index, onChange }: { condition: ConditionNode; index: number; onChange: (value: string) => void }) {
  const api = useBuilder();
  const label = `Condition ${index + 1} value`;
  if (condition.valueType === "boolean") {
    return <Segmented label={label} size="sm" value={condition.value === "false" ? "false" : "true"} disabled={api.disabled} onChange={onChange} options={[{ value: "true", label: "Yes" }, { value: "false", label: "No" }]} />;
  }
  const isList = condition.operator === "In" || condition.valueType === "list";
  const type = isList ? "text" : condition.valueType === "number" ? "number" : condition.valueType === "date" ? "date" : "text";
  return (
    <Input
      aria-label={label}
      type={type}
      value={condition.value}
      disabled={api.disabled}
      placeholder={isList ? "Value A, Value B, Value C" : condition.valueType === "number" ? "0" : "Value"}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}
