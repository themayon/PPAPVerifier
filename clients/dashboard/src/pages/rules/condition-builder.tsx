import { createContext, useContext, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { ArrowDown, ArrowUp, Copy, FolderPlus, Plus, Trash2 } from "lucide-react";
import type { RuleColumnDto } from "@/api/rules";
import { Combobox } from "@/components/list";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/cn";
import {
  LIMITS,
  OPERATOR_SYMBOL,
  duplicateNode,
  isUnary,
  moveNode,
  newCondition,
  newGroup,
  operatorLabel,
  operatorsFor,
  removeNode,
  updateNode,
  valueTypeFor,
  type BuilderNode,
  type ColumnIndex,
  type ConditionNode,
  type FieldRef,
  type GroupNode,
  type ValueSource,
} from "./rule-model";
import { ColumnPicker, Segmented } from "./rule-ui";

type BuilderApi = {
  disabled: boolean;
  columns: ColumnIndex;
  documentCode: string;
  showDocument: boolean;
  issues: ReadonlyMap<string, string>;
  showIssues: boolean;
  update: (id: string, fn: (node: BuilderNode) => BuilderNode) => void;
  remove: (id: string) => void;
  move: (id: string, delta: -1 | 1) => void;
  duplicate: (id: string) => void;
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

const TYPES: ReadonlyArray<{ value: ValueSource; label: string; hint: string }> = [
  { value: "value", label: "Value", hint: "fixed value" },
  { value: "field", label: "Column", hint: "another column" },
  { value: "parameter", label: "Parameter", hint: "template parameter" },
];

export function ConditionBuilder({
  root,
  onChange,
  disabled = false,
  columns,
  documentCode,
  showDocument,
  issues,
  showIssues,
}: {
  root: GroupNode;
  onChange: (root: GroupNode) => void;
  disabled?: boolean;
  columns: ColumnIndex;
  documentCode: string;
  showDocument: boolean;
  issues: ReadonlyMap<string, string>;
  showIssues: boolean;
}) {
  const api: BuilderApi = {
    disabled,
    columns,
    documentCode,
    showDocument,
    issues,
    showIssues,
    update: (id, fn) => onChange(updateNode(root, id, fn)),
    remove: (id) => onChange(removeNode(root, id)),
    move: (id, delta) => onChange(moveNode(root, id, delta)),
    duplicate: (id) => onChange(duplicateNode(root, id)),
  };
  return (
    <BuilderContext.Provider value={api}>
      {columns.list.length === 0 && (
        <p className="mb-3 rounded-lg border border-dashed border-[var(--color-border)] px-3 py-2.5 text-[12.5px] text-[var(--color-muted-foreground)]">
          The column master is empty. <Link to="/rules/columns" className="font-medium text-[var(--color-primary)] hover:underline">Add columns</Link> to build conditions.
        </p>
      )}
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
        {!isRoot && <span className="font-mono text-[13px] font-bold text-[var(--color-muted-foreground)]" aria-hidden>(</span>}
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
        <span className="text-[12.5px] text-[var(--color-muted-foreground)]">Join with</span>
        <Segmented
          label={isRoot ? "Root match mode" : "Group match mode"}
          size="sm"
          value={group.combinator}
          disabled={api.disabled}
          onChange={(combinator) => setGroup({ combinator })}
          options={[{ value: "all", label: "AND" }, { value: "any", label: "OR" }]}
        />
        <span className="text-[12.5px] text-[var(--color-muted-foreground)]">{group.combinator === "all" ? "all must be true" : "any can be true"}</span>
        <span className="ml-auto" />
        {!isRoot && !api.disabled && <RowActions id={group.id} index={index} siblings={siblings} label="group" />}
      </div>

      <div className="p-3">
        {group.children.length === 0 && (
          <p className="rounded-lg border border-dashed border-[var(--color-border)] px-3 py-4 text-center text-[12.5px] text-[var(--color-muted-foreground)]">
            Empty group. Add a formula below.
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
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <Button type="button" size="xs" variant="soft" disabled={group.children.length >= LIMITS.maxChildren} onClick={() => add(newCondition(api.documentCode))}>
              <Plus />Add formula
            </Button>
            <Button
              type="button"
              size="xs"
              variant="outline"
              disabled={!canNest || group.children.length >= LIMITS.maxChildren}
              title={canNest ? "Add a bracketed group, like ( … ) in the legacy formula" : "Maximum nesting reached"}
              onClick={() => add(newGroup(group.combinator === "all" ? "any" : "all", [newCondition(api.documentCode)]))}
            >
              <FolderPlus />Add ( group )
            </Button>
            {!isRoot && <span className="font-mono text-[13px] font-bold text-[var(--color-muted-foreground)]" aria-hidden>)</span>}
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

function FieldLabel({ children }: { children: ReactNode }) {
  return <span className="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">{children}</span>;
}

function ConditionRow({ condition, index, siblings }: { condition: ConditionNode; index: number; siblings: number }) {
  const api = useBuilder();
  const issue = api.showIssues ? api.issues.get(condition.id) : undefined;
  const set = (patch: Partial<ConditionNode>) => api.update(condition.id, (node) => ({ ...(node as ConditionNode), ...patch }));
  const allowed = operatorsFor(condition.valueType);
  const unary = isUnary(condition.operator);
  const name = `Condition ${index + 1}`;

  const pickColumn = (field: FieldRef, column: RuleColumnDto | undefined) => {
    if (!column) return set({ field });
    const valueType = valueTypeFor(column.dataType);
    const operators = operatorsFor(valueType);
    set({
      field,
      valueType,
      operator: operators.includes(condition.operator) ? condition.operator : operators[0]!,
      value: valueType === condition.valueType ? condition.value : valueType === "boolean" ? "true" : "",
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
      <div className="grid grid-cols-[repeat(auto-fit,minmax(10.5rem,1fr))] items-end gap-2">
        <div className="min-w-0">
          <FieldLabel>Column</FieldLabel>
          <ColumnPicker label={`${name} column`} value={condition.field} onChange={pickColumn} columns={api.columns} documentCode={api.documentCode} showDocument={api.showDocument} disabled={api.disabled} invalid={Boolean(issue)} />
        </div>
        <div className="min-w-0">
          <FieldLabel>Operator</FieldLabel>
          <div role="group" aria-label={`${name} operator`}>
            <Combobox
              label="Operator"
              value={condition.operator}
              onChange={(value) => value && set({ operator: value as ConditionNode["operator"] })}
              options={allowed.map((operator) => ({ value: operator, label: OPERATOR_SYMBOL[operator], hint: operatorLabel(operator, condition.valueType) }))}
              disabled={api.disabled}
            />
          </div>
        </div>
        {!unary ? (
          <div className="min-w-0">
            <FieldLabel>Type</FieldLabel>
            <div role="group" aria-label={`${name} type`}>
              <Combobox label="Type" value={condition.source} onChange={(value) => value && set({ source: value as ValueSource })} options={TYPES.map((type) => ({ ...type }))} disabled={api.disabled} />
            </div>
          </div>
        ) : null}
        {!unary ? (
          <div className="min-w-0">
            <FieldLabel>{condition.source === "field" ? "Value column" : condition.source === "parameter" ? "Parameter" : "Value"}</FieldLabel>
            {condition.source === "field" && (
              <ColumnPicker
                label={`${name} value column`}
                value={condition.valueField}
                onChange={(valueField) => set({ valueField })}
                columns={api.columns}
                documentCode={api.documentCode}
                showDocument={api.showDocument}
                disabled={api.disabled}
                invalid={Boolean(issue)}
              />
            )}
            {condition.source === "parameter" && (
              <Input aria-label={`${name} parameter`} placeholder="parameterName" value={condition.parameter} disabled={api.disabled} onChange={(event) => set({ parameter: event.target.value })} className="font-mono text-[12.5px]" />
            )}
            {condition.source === "value" && <ValueInput condition={condition} label={`${name} value`} onChange={(value) => set({ value })} />}
          </div>
        ) : null}
      </div>
      <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
        {!unary ? (
          <label className="inline-flex cursor-pointer select-none items-center gap-1.5 whitespace-nowrap text-[12px] text-[var(--color-muted-foreground)]">
            <input type="checkbox" className="size-3.5 accent-[var(--color-primary)]" checked={condition.includeEmpty} disabled={api.disabled} onChange={(event) => set({ includeEmpty: event.target.checked })} />
            Include empty value
          </label>
        ) : <span />}
        {!api.disabled && <RowActions id={condition.id} index={index} siblings={siblings} label="formula" />}
      </div>
      {issue && <p role="alert" className="mt-2 text-[12px] text-[var(--color-warning)]">{issue}</p>}
    </div>
  );
}

function ValueInput({ condition, label, onChange }: { condition: ConditionNode; label: string; onChange: (value: string) => void }) {
  const api = useBuilder();
  if (condition.valueType === "boolean" && condition.operator !== "In") {
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
