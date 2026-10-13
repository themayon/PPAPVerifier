import { BrainCircuit, FileCog, FileSpreadsheet, ListChecks, Sparkles, type LucideIcon } from "lucide-react";
import type { RuleCategory, RuleLifecycle } from "@/api/rules";

export type RuleTypeKey = "validation" | "header" | "special" | "custom" | "ml";

export type RuleTypeConfig = {
  key: RuleTypeKey;
  label: string;
  plural: string;
  description: string;
  icon: LucideIcon;
  categories: readonly RuleCategory[];
  defaultCategory: RuleCategory;
  conditionsHint: string;
  showOutput: boolean;
  showModel: boolean;
};

export const RULE_TYPES: readonly RuleTypeConfig[] = [
  {
    key: "validation",
    label: "Validation rule",
    plural: "Validation rules",
    description: "Row and document checks that flag findings in submitted PPAP documents.",
    icon: ListChecks,
    categories: ["Row", "Document", "CrossDocument", "Drawing"],
    defaultCategory: "Row",
    conditionsHint: "Compare extracted fields with fixed values, other fields, or template parameters.",
    showOutput: true,
    showModel: false,
  },
  {
    key: "header",
    label: "Header rule",
    plural: "Header rules",
    description: "Checks on document header data such as part number, revision, and supplier details.",
    icon: FileSpreadsheet,
    categories: ["Header"],
    defaultCategory: "Header",
    conditionsHint: "Header rules usually compare header fields across documents, e.g. DFMEA part number equals PSW part number.",
    showOutput: true,
    showModel: false,
  },
  {
    key: "special",
    label: "Special rule",
    plural: "Special rules",
    description: "Customer-specific or special-characteristic checks that run alongside the standard set.",
    icon: Sparkles,
    categories: ["Special"],
    defaultCategory: "Special",
    conditionsHint: "Target special characteristics, customer-specific requirements, or exceptional cases.",
    showOutput: true,
    showModel: false,
  },
  {
    key: "custom",
    label: "Custom rule",
    plural: "Custom rules",
    description: "Tenant-authored checks for anything the standard libraries don't cover.",
    icon: FileCog,
    categories: ["Custom"],
    defaultCategory: "Custom",
    conditionsHint: "Build any combination of conditions your quality team needs.",
    showOutput: true,
    showModel: false,
  },
  {
    key: "ml",
    label: "ML rule",
    plural: "ML rules",
    description: "Model-assisted checks that decide on fields produced by an AI model.",
    icon: BrainCircuit,
    categories: ["ModelInference"],
    defaultCategory: "ModelInference",
    conditionsHint: "Decide on model outputs, e.g. prediction.label equals \"Mismatch\" and prediction.confidence is at least 0.8.",
    showOutput: false,
    showModel: true,
  },
];

export function ruleTypeByKey(key: string | undefined): RuleTypeConfig | undefined {
  return RULE_TYPES.find((type) => type.key === key);
}

export function ruleTypeForCategory(category: RuleCategory): RuleTypeConfig {
  return RULE_TYPES.find((type) => type.categories.includes(category)) ?? RULE_TYPES[0]!;
}

export const CATEGORY_LABELS: Record<RuleCategory, string> = {
  Row: "Row",
  Document: "Document",
  CrossDocument: "Cross-document",
  Drawing: "Drawing",
  Header: "Header",
  Special: "Special",
  Custom: "Custom",
  ModelInference: "Model inference",
};

export const LIFECYCLES: readonly RuleLifecycle[] = ["Draft", "InReview", "Published", "Retired"];
export const LIFECYCLE_LABELS: Record<RuleLifecycle, string> = { Draft: "Draft", InReview: "In review", Published: "Live", Retired: "Retired" };

export type Severity = "Error" | "Warning" | "Info";
export const SEVERITIES: readonly Severity[] = ["Error", "Warning", "Info"];
export const SEVERITY_DEFAULT_COLOR: Record<Severity, string> = { Error: "#f4c7c3", Warning: "#fce8b2", Info: "#c6dafc" };
export const SEVERITY_STRIPE: Record<Severity, string> = {
  Error: "bg-[var(--color-destructive)]",
  Warning: "bg-[var(--color-warning)]",
  Info: "bg-[var(--color-info)]",
};

export const PPAP_DOCUMENTS: ReadonlyArray<{ code: string; name: string }> = [
  { code: "PSW", name: "Part Submission Warrant" },
  { code: "DWG", name: "Design Records" },
  { code: "ECD", name: "Engineering Change Documents" },
  { code: "CEA", name: "Customer Engineering Approval" },
  { code: "DFMEA", name: "Design FMEA" },
  { code: "PFD", name: "Process Flow Diagram" },
  { code: "PFMEA", name: "Process FMEA" },
  { code: "CP", name: "Control Plan" },
  { code: "MSA", name: "Measurement System Analysis" },
  { code: "DIMR", name: "Dimensional Results" },
  { code: "MPTR", name: "Material / Performance Test Results" },
  { code: "IPS", name: "Initial Process Study" },
  { code: "QLD", name: "Qualified Laboratory Documentation" },
  { code: "AAR", name: "Appearance Approval Report" },
  { code: "SAMP", name: "Sample Production Parts" },
  { code: "MSAM", name: "Master Sample" },
  { code: "CAID", name: "Checking Aids" },
  { code: "CSR", name: "Records of Compliance (CSR)" },
  { code: "IMDS", name: "IMDS" },
  { code: "APDS", name: "Additional PPAP Data" },
];

export type FieldRef = { documentCode: string; path: string };

export type Operator =
  | "Equal" | "NotEqual" | "GreaterThan" | "GreaterThanOrEqual" | "LessThan" | "LessThanOrEqual"
  | "Contains" | "In" | "IsEmpty" | "IsNotEmpty";

export type ValueType = "text" | "number" | "date" | "boolean" | "list";
export type ValueSource = "value" | "field" | "parameter";

export type ConditionNode = {
  kind: "condition";
  id: string;
  field: FieldRef;
  operator: Operator;
  source: ValueSource;
  valueType: ValueType;
  value: string;
  valueField: FieldRef;
  parameter: string;
  includeEmpty: boolean;
};

export type GroupNode = {
  kind: "group";
  id: string;
  combinator: "all" | "any";
  negate: boolean;
  children: BuilderNode[];
};

export type BuilderNode = ConditionNode | GroupNode;

export const LIMITS = { maxDepth: 10, maxChildren: 25, maxNodes: 100 } as const;

let idSeed = 0;
export function newId(prefix = "n"): string {
  idSeed += 1;
  return `${prefix}-${Date.now().toString(36)}-${idSeed}`;
}

export function emptyField(): FieldRef {
  return { documentCode: "", path: "" };
}

export function newCondition(documentCode = ""): ConditionNode {
  return {
    kind: "condition",
    id: newId("c"),
    field: { documentCode, path: "" },
    operator: "Equal",
    source: "value",
    valueType: "text",
    value: "",
    valueField: { documentCode, path: "" },
    parameter: "",
    includeEmpty: false,
  };
}

export function newGroup(combinator: "all" | "any" = "all", children?: BuilderNode[]): GroupNode {
  return { kind: "group", id: newId("g"), combinator, negate: false, children: children ?? [newCondition()] };
}

export const isUnary = (operator: Operator) => operator === "IsEmpty" || operator === "IsNotEmpty";

export function operatorLabel(operator: Operator, valueType: ValueType): string {
  const date = valueType === "date";
  switch (operator) {
    case "Equal": return date ? "is on" : "equals";
    case "NotEqual": return date ? "is not on" : "does not equal";
    case "GreaterThan": return date ? "is after" : "is greater than";
    case "GreaterThanOrEqual": return date ? "is on or after" : "is at least";
    case "LessThan": return date ? "is before" : "is less than";
    case "LessThanOrEqual": return date ? "is on or before" : "is at most";
    case "Contains": return "contains";
    case "In": return "is one of";
    case "IsEmpty": return "is empty";
    case "IsNotEmpty": return "is not empty";
  }
}

export function operatorsFor(valueType: ValueType): Operator[] {
  switch (valueType) {
    case "number":
    case "date":
      return ["Equal", "NotEqual", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "In", "IsEmpty", "IsNotEmpty"];
    case "boolean":
      return ["Equal", "NotEqual", "IsEmpty", "IsNotEmpty"];
    case "list":
      return ["In", "IsEmpty", "IsNotEmpty"];
    default:
      return ["Equal", "NotEqual", "Contains", "In", "GreaterThan", "LessThan", "IsEmpty", "IsNotEmpty"];
  }
}

type AstOperand =
  | { type: "field"; documentCode: string; path: string }
  | { type: "literal"; value: unknown }
  | { type: "parameter"; name: string };

export type AstNode =
  | { type: "all" | "any"; children: AstNode[] }
  | { type: "not"; child: AstNode }
  | { type: "comparison"; operator: Operator; left: AstOperand; right?: AstOperand };

const listItems = (raw: string) => raw.split(",").map((item) => item.trim()).filter(Boolean);

function coerce(raw: string, type: ValueType): unknown {
  if (type === "number") return Number(raw);
  if (type === "boolean") return raw === "true";
  return raw;
}

function literalValue(condition: ConditionNode): unknown {
  const raw = condition.value.trim();
  if (condition.operator === "In" || condition.valueType === "list") {
    return listItems(raw).map((item) => coerce(item, condition.valueType === "list" ? "text" : condition.valueType));
  }
  return coerce(raw, condition.valueType);
}

function conditionToAst(condition: ConditionNode): AstNode {
  const left: AstOperand = { type: "field", documentCode: condition.field.documentCode.trim(), path: condition.field.path.trim() };
  if (isUnary(condition.operator)) return { type: "comparison", operator: condition.operator, left };
  const right: AstOperand =
    condition.source === "field"
      ? { type: "field", documentCode: condition.valueField.documentCode.trim(), path: condition.valueField.path.trim() }
      : condition.source === "parameter"
        ? { type: "parameter", name: condition.parameter.trim() }
        : { type: "literal", value: literalValue(condition) };
  const comparison: AstNode = { type: "comparison", operator: condition.operator, left, right };
  return condition.includeEmpty ? { type: "any", children: [comparison, { type: "comparison", operator: "IsEmpty", left }] } : comparison;
}

export function treeToAst(node: BuilderNode): AstNode {
  if (node.kind === "condition") return conditionToAst(node);
  const group: AstNode = { type: node.combinator, children: node.children.map(treeToAst) };
  return node.negate ? { type: "not", child: group } : group;
}

export function buildAst(root: GroupNode, mode: RuleMode): string {
  const ast = treeToAst(root);
  return JSON.stringify(mode === "flag" ? { type: "not", child: ast } : ast);
}

function isDateString(value: string) {
  return /^\d{4}-\d{2}-\d{2}(T.*)?$/.test(value);
}

function operandToField(operand: AstOperand | undefined): FieldRef {
  return operand?.type === "field" ? { documentCode: operand.documentCode, path: operand.path } : emptyField();
}

function astToCondition(ast: Extract<AstNode, { type: "comparison" }>, includeEmpty = false): ConditionNode {
  const condition = newCondition();
  condition.field = operandToField(ast.left);
  condition.operator = ast.operator;
  condition.includeEmpty = includeEmpty;
  const right = ast.right;
  if (!right) return condition;
  if (right.type === "field") {
    condition.source = "field";
    condition.valueField = operandToField(right);
    return condition;
  }
  if (right.type === "parameter") {
    condition.source = "parameter";
    condition.parameter = right.name;
    return condition;
  }
  const value = right.value;
  if (Array.isArray(value)) {
    condition.valueType = value.length > 0 && value.every((item) => typeof item === "number") ? "number"
      : value.length > 0 && value.every((item) => typeof item === "string" && isDateString(item)) ? "date" : "text";
    condition.value = value.map(String).join(", ");
  } else if (typeof value === "number") {
    condition.valueType = "number";
    condition.value = String(value);
  } else if (typeof value === "boolean") {
    condition.valueType = "boolean";
    condition.value = String(value);
  } else if (typeof value === "string") {
    condition.valueType = isDateString(value) ? "date" : "text";
    condition.value = value;
  } else {
    condition.value = value === null || value === undefined ? "" : JSON.stringify(value);
  }
  return condition;
}

function sameField(a: AstOperand, b: AstOperand) {
  return a.type === "field" && b.type === "field" && a.documentCode === b.documentCode && a.path === b.path;
}

function astToNode(ast: AstNode): BuilderNode {
  if (ast.type === "comparison") return astToCondition(ast);
  if (ast.type === "not") {
    const inner = astToNode(ast.child);
    return inner.kind === "group" && !inner.negate ? { ...inner, negate: true } : { ...newGroup("all", [inner]), negate: true };
  }
  const [first, second] = ast.children;
  if (ast.type === "any" && ast.children.length === 2 && first?.type === "comparison" && second?.type === "comparison"
    && second.operator === "IsEmpty" && !isUnary(first.operator) && sameField(first.left, second.left)) {
    return astToCondition(first, true);
  }
  return { kind: "group", id: newId("g"), combinator: ast.type, negate: false, children: ast.children.map(astToNode) };
}

export function astToTree(astJson: string | null | undefined, mode: RuleMode): GroupNode {
  if (!astJson) return newGroup();
  try {
    let ast = JSON.parse(astJson) as AstNode;
    if (mode === "flag" && ast.type === "not") ast = ast.child;
    const node = astToNode(ast);
    return node.kind === "group" ? node : newGroup("all", [node]);
  } catch {
    return newGroup();
  }
}

export type RuleMode = "flag" | "pass";
export type RuleMessage = { id: string; field: FieldRef; text: string };
export type RuleHighlight = { id: string; field: FieldRef; color: string };

export type RuleOutcome = {
  severity: Severity;
  tags: string[];
  mode: RuleMode;
  messages: RuleMessage[];
  primaryKey: FieldRef | null;
  consolidate: boolean;
  highlights: RuleHighlight[];
  similarityThreshold: number | null;
  model: { name: string; prompt: string; confidenceThreshold: number | null };
};

export function defaultOutcome(): RuleOutcome {
  return {
    severity: "Error",
    tags: [],
    mode: "flag",
    messages: [{ id: newId("m"), field: emptyField(), text: "" }],
    primaryKey: null,
    consolidate: false,
    highlights: [],
    similarityThreshold: null,
    model: { name: "", prompt: "", confidenceThreshold: null },
  };
}

type StoredOutcome = Partial<{
  severity: string;
  tags: unknown;
  mode: string;
  messages: Array<{ field?: FieldRef | null; text?: string }>;
  primaryKey: FieldRef | null;
  consolidate: { enabled?: boolean } | boolean;
  highlights: Array<{ field?: FieldRef | null; color?: string }>;
  similarityThreshold: number | null;
  model: Partial<{ name: string; prompt: string; confidenceThreshold: number | null }>;
  message: string;
}>;

const asField = (value: FieldRef | null | undefined): FieldRef => ({ documentCode: value?.documentCode ?? "", path: value?.path ?? "" });

export function parseOutcome(json: string | null | undefined): RuleOutcome {
  const fallback = defaultOutcome();
  if (!json) return fallback;
  let stored: StoredOutcome;
  try { stored = JSON.parse(json) as StoredOutcome; } catch { return fallback; }
  if (!stored || typeof stored !== "object") return fallback;
  const messages = Array.isArray(stored.messages) && stored.messages.length > 0
    ? stored.messages.map((message) => ({ id: newId("m"), field: asField(message.field), text: message.text ?? "" }))
    : [{ id: newId("m"), field: emptyField(), text: typeof stored.message === "string" ? stored.message : "" }];
  return {
    severity: SEVERITIES.includes(stored.severity as Severity) ? (stored.severity as Severity) : "Error",
    tags: Array.isArray(stored.tags) ? stored.tags.filter((tag): tag is string => typeof tag === "string") : [],
    mode: stored.mode === "flag" ? "flag" : "pass",
    messages,
    primaryKey: stored.primaryKey?.path ? asField(stored.primaryKey) : null,
    consolidate: typeof stored.consolidate === "boolean" ? stored.consolidate : Boolean(stored.consolidate?.enabled),
    highlights: Array.isArray(stored.highlights) ? stored.highlights.map((item) => ({ id: newId("h"), field: asField(item.field), color: item.color ?? "#fce8b2" })) : [],
    similarityThreshold: typeof stored.similarityThreshold === "number" ? stored.similarityThreshold : null,
    model: { name: stored.model?.name ?? "", prompt: stored.model?.prompt ?? "", confidenceThreshold: stored.model?.confidenceThreshold ?? null },
  };
}

export function outcomeSummary(json: string | null | undefined) {
  const outcome = parseOutcome(json);
  return { severity: outcome.severity, tags: outcome.tags, messageCount: outcome.messages.filter((message) => message.text.trim()).length, mode: outcome.mode };
}

const cleanField = (field: FieldRef) => ({ documentCode: field.documentCode.trim(), path: field.path.trim() });

export function serializeOutcome(outcome: RuleOutcome, type: RuleTypeConfig): string {
  const messages = outcome.messages.filter((message) => message.text.trim()).map((message) => ({ field: message.field.path.trim() ? cleanField(message.field) : null, text: message.text.trim() }));
  const primary = messages[0]?.text ?? null;
  const primaryKey = outcome.primaryKey?.path.trim() ? cleanField(outcome.primaryKey) : null;
  return JSON.stringify({
    schema: "ppap-rule/v1",
    ruleType: type.key,
    severity: outcome.severity,
    tags: outcome.tags,
    mode: outcome.mode,
    message: primary,
    Failed: primary ? { message: primary } : undefined,
    messages,
    primaryKey,
    consolidate: { enabled: outcome.consolidate && primaryKey !== null, field: outcome.consolidate ? primaryKey : null },
    highlights: type.showOutput ? outcome.highlights.filter((item) => item.field.path.trim()).map((item) => ({ field: cleanField(item.field), color: item.color })) : [],
    similarityThreshold: type.showOutput ? outcome.similarityThreshold : null,
    model: type.showModel ? { name: outcome.model.name.trim(), prompt: outcome.model.prompt.trim(), confidenceThreshold: outcome.model.confidenceThreshold } : undefined,
  });
}

export type SectionId = "basics" | "conditions" | "model" | "messages" | "output" | "test";
export type Issue = { id: string; section: SectionId; message: string };

const PATH_PATTERN = /^[^.$[\]]+(\.[^.$[\]]+)*$/;
const PARAM_PATTERN = /^[A-Za-z0-9_.-]{1,64}$/;
export const CODE_PATTERN = /^[A-Za-z0-9_.-]{1,64}$/;

function fieldIssue(field: FieldRef, label: string): string | null {
  if (!field.documentCode.trim()) return `${label}: choose a document.`;
  if (!field.path.trim()) return `${label}: enter a field.`;
  if (!PATH_PATTERN.test(field.path.trim()) || field.path.split(".").length > 10) return `${label}: use dot-separated field names (no brackets or $).`;
  return null;
}

export function conditionIssue(condition: ConditionNode): string | null {
  const left = fieldIssue(condition.field, "Field");
  if (left) return left;
  if (isUnary(condition.operator)) return null;
  if (condition.source === "field") return fieldIssue(condition.valueField, "Compare-to field");
  if (condition.source === "parameter") return PARAM_PATTERN.test(condition.parameter.trim()) ? null : "Parameter: use 1-64 letters, numbers, dots, dashes, or underscores.";
  const raw = condition.value.trim();
  if (condition.operator === "In" || condition.valueType === "list") {
    const items = listItems(raw);
    if (items.length === 0) return "Enter at least one value (comma-separated).";
    if (items.length > 100) return "A list can hold at most 100 values.";
    if (condition.valueType === "number" && items.some((item) => Number.isNaN(Number(item)))) return "Every list value must be a number.";
    if (condition.valueType === "date" && items.some((item) => !isDateString(item))) return "Every list value must be a date (yyyy-mm-dd).";
    return null;
  }
  if (!raw && condition.valueType !== "text") return "Enter a value.";
  if (condition.valueType === "number" && Number.isNaN(Number(raw))) return "Enter a valid number.";
  if (condition.valueType === "date" && !isDateString(raw)) return "Pick a date.";
  if (condition.operator === "Contains" && condition.valueType !== "text") return "Contains only works with text.";
  return null;
}

function astMetrics(ast: AstNode, level: number): { nodes: number; depth: number } {
  if (ast.type === "comparison") return { nodes: 1, depth: level };
  const children = ast.type === "not" ? [ast.child] : ast.children;
  return children.reduce((total, child) => {
    const metrics = astMetrics(child, level + 1);
    return { nodes: total.nodes + metrics.nodes, depth: Math.max(total.depth, metrics.depth) };
  }, { nodes: 1, depth: level });
}

export function treeStats(root: GroupNode) {
  let conditions = 0;
  const walk = (node: BuilderNode): void => {
    if (node.kind === "condition") conditions += 1;
    else node.children.forEach(walk);
  };
  walk(root);
  return { ...astMetrics({ type: "not", child: treeToAst(root) }, 1), conditions };
}

export function validateTree(root: GroupNode): Issue[] {
  const issues: Issue[] = [];
  const walk = (node: BuilderNode): void => {
    if (node.kind === "condition") {
      const message = conditionIssue(node);
      if (message) issues.push({ id: node.id, section: "conditions", message });
      return;
    }
    if (node.children.length === 0) issues.push({ id: node.id, section: "conditions", message: "A group needs at least one condition." });
    if (node.children.length > LIMITS.maxChildren) issues.push({ id: node.id, section: "conditions", message: `A group can hold at most ${LIMITS.maxChildren} items.` });
    node.children.forEach(walk);
  };
  walk(root);
  const stats = treeStats(root);
  if (stats.nodes > LIMITS.maxNodes) issues.push({ id: root.id, section: "conditions", message: `This rule is too large (${stats.nodes}/${LIMITS.maxNodes} nodes). Split it into smaller rules.` });
  if (stats.depth > LIMITS.maxDepth) issues.push({ id: root.id, section: "conditions", message: "Groups are nested too deeply. Flatten some groups or split the rule." });
  return issues;
}

export type PreviewLine = { id: string; depth: number; text: string; kind: "group" | "condition" };

export const fieldLabel = (field: FieldRef) => (field.path ? `${field.documentCode || "?"} › ${field.path}` : field.documentCode ? `${field.documentCode} › …` : "…");

export function describeCondition(condition: ConditionNode): string {
  const left = fieldLabel(condition.field);
  const op = operatorLabel(condition.operator, condition.valueType);
  if (isUnary(condition.operator)) return `${left} ${op}`;
  const isList = condition.operator === "In" || condition.valueType === "list";
  const right =
    condition.source === "field" ? fieldLabel(condition.valueField)
      : condition.source === "parameter" ? `{${condition.parameter || "parameter"}}`
        : isList ? `[${condition.value || "…"}]`
          : condition.valueType === "text" ? `"${condition.value}"`
            : condition.valueType === "boolean" ? (condition.value === "true" ? "Yes" : "No")
              : condition.value || "…";
  return `${left} ${op} ${right}${condition.includeEmpty ? " (or is empty)" : ""}`;
}

export function previewLines(root: GroupNode): PreviewLine[] {
  const lines: PreviewLine[] = [];
  const walk = (node: BuilderNode, depth: number): void => {
    if (node.kind === "condition") {
      lines.push({ id: node.id, depth, text: describeCondition(node), kind: "condition" });
      return;
    }
    lines.push({ id: node.id, depth, text: `${node.negate ? "NOT " : ""}${node.combinator === "all" ? "ALL" : "ANY"} of:`, kind: "group" });
    node.children.forEach((child) => walk(child, depth + 1));
  };
  walk(root, 0);
  return lines;
}

export function sampleDocument(root: GroupNode, documentCode: string): string {
  const sample: Record<string, unknown> = {};
  const put = (field: FieldRef, value: unknown) => {
    if (field.documentCode !== documentCode || !field.path.trim()) return;
    const segments = field.path.trim().split(".");
    let cursor = sample;
    segments.forEach((segment, index) => {
      if (index === segments.length - 1) {
        if (!(segment in cursor)) cursor[segment] = value;
        return;
      }
      if (typeof cursor[segment] !== "object" || cursor[segment] === null) cursor[segment] = {};
      cursor = cursor[segment] as Record<string, unknown>;
    });
  };
  const walk = (node: BuilderNode): void => {
    if (node.kind === "group") return node.children.forEach(walk);
    const value = node.valueType === "number" ? 0 : node.valueType === "boolean" ? false : node.valueType === "date" ? "2026-01-01" : "";
    put(node.field, value);
    if (node.source === "field") put(node.valueField, value);
  };
  walk(root);
  return JSON.stringify(sample, null, 2);
}

export function documentsUsed(root: GroupNode): string[] {
  const codes = new Set<string>();
  const walk = (node: BuilderNode): void => {
    if (node.kind === "group") return node.children.forEach(walk);
    if (node.field.documentCode) codes.add(node.field.documentCode);
    if (node.source === "field" && node.valueField.documentCode) codes.add(node.valueField.documentCode);
  };
  walk(root);
  return [...codes];
}

export function parametersUsed(root: GroupNode): string[] {
  const names = new Set<string>();
  const walk = (node: BuilderNode): void => {
    if (node.kind === "group") return node.children.forEach(walk);
    if (node.source === "parameter" && node.parameter.trim() && !isUnary(node.operator)) names.add(node.parameter.trim());
  };
  walk(root);
  return [...names];
}

export function updateNode(root: GroupNode, id: string, update: (node: BuilderNode) => BuilderNode): GroupNode {
  const walk = (node: BuilderNode): BuilderNode => {
    if (node.id === id) return update(node);
    if (node.kind === "group") return { ...node, children: node.children.map(walk) };
    return node;
  };
  return walk(root) as GroupNode;
}

export function removeNode(root: GroupNode, id: string): GroupNode {
  const walk = (node: GroupNode): GroupNode => ({
    ...node,
    children: node.children.filter((child) => child.id !== id).map((child) => (child.kind === "group" ? walk(child) : child)),
  });
  return walk(root);
}

export function moveNode(root: GroupNode, id: string, delta: -1 | 1): GroupNode {
  const walk = (node: GroupNode): GroupNode => {
    const index = node.children.findIndex((child) => child.id === id);
    if (index >= 0) {
      const target = index + delta;
      if (target < 0 || target >= node.children.length) return node;
      const children = [...node.children];
      [children[index], children[target]] = [children[target]!, children[index]!];
      return { ...node, children };
    }
    return { ...node, children: node.children.map((child) => (child.kind === "group" ? walk(child) : child)) };
  };
  return walk(root);
}

export function cloneNode(node: BuilderNode): BuilderNode {
  if (node.kind === "condition") return { ...node, id: newId("c"), field: { ...node.field }, valueField: { ...node.valueField } };
  return { ...node, id: newId("g"), children: node.children.map(cloneNode) };
}

export function duplicateNode(root: GroupNode, id: string): GroupNode {
  const walk = (node: GroupNode): GroupNode => {
    const index = node.children.findIndex((child) => child.id === id);
    if (index >= 0) {
      const children = [...node.children];
      children.splice(index + 1, 0, cloneNode(node.children[index]!));
      return { ...node, children };
    }
    return { ...node, children: node.children.map((child) => (child.kind === "group" ? walk(child) : child)) };
  };
  return walk(root);
}

const RECENT_KEY = "des.dashboard.rules.recentFields";

export function readRecentFields(): FieldRef[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(RECENT_KEY) ?? "[]") as unknown;
    return Array.isArray(parsed)
      ? parsed.filter((item): item is FieldRef => typeof item?.documentCode === "string" && typeof item?.path === "string").slice(0, 200)
      : [];
  } catch {
    return [];
  }
}

export function rememberFields(fields: FieldRef[]) {
  const seen = new Set<string>();
  const merged = [...fields, ...readRecentFields()].filter((field) => {
    const key = `${field.documentCode}|${field.path}`;
    if (!field.documentCode || !field.path || seen.has(key)) return false;
    seen.add(key);
    return true;
  });
  try { localStorage.setItem(RECENT_KEY, JSON.stringify(merged.slice(0, 200))); } catch { return; }
}

export function fieldsInRule(root: GroupNode, outcome: RuleOutcome): FieldRef[] {
  const fields: FieldRef[] = [];
  const walk = (node: BuilderNode): void => {
    if (node.kind === "group") return node.children.forEach(walk);
    fields.push(cleanField(node.field));
    if (node.source === "field") fields.push(cleanField(node.valueField));
  };
  walk(root);
  outcome.messages.forEach((message) => fields.push(cleanField(message.field)));
  outcome.highlights.forEach((item) => fields.push(cleanField(item.field)));
  if (outcome.primaryKey) fields.push(cleanField(outcome.primaryKey));
  return fields;
}
