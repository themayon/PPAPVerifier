import { useMemo, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { CheckCircle2, CircleAlert, CircleSlash, Play, Wand2, XCircle } from "lucide-react";
import { testRule, type RuleEvaluationResultDto, type RuleEvaluationStatus } from "@/api/rules";
import { EntityStatusBadge, type EntityStatusTone } from "@/components/list";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/cn";
import { describe } from "@/lib/list-helpers";
import { buildAst, documentsUsed, parametersUsed, sampleDocument, type GroupNode, type RuleMode } from "./rule-model";
import { Segmented } from "./rule-ui";

const textareaClass = "w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 font-mono text-[12px] leading-relaxed shadow-xs outline-none focus-visible:border-[var(--color-ring)] focus-visible:ring-[3px] focus-visible:ring-[oklch(from_var(--color-ring)_l_c_h_/_0.5)]";
const STATUS_TONE: Record<RuleEvaluationStatus, EntityStatusTone> = { Passed: "success", Failed: "danger", NotEvaluated: "warning", Error: "warning" };

function isJsonObject(value: string) {
  try {
    const parsed: unknown = JSON.parse(value);
    return parsed !== null && typeof parsed === "object" && !Array.isArray(parsed);
  } catch {
    return false;
  }
}

export function RuleTestPanel({ tree, mode, blocked }: { tree: GroupNode; mode: RuleMode; blocked: boolean }) {
  const codes = useMemo(() => documentsUsed(tree), [tree]);
  const parameters = useMemo(() => parametersUsed(tree), [tree]);
  const [active, setActive] = useState<string>("");
  const [documents, setDocuments] = useState<Record<string, string>>({});
  const [parametersJson, setParametersJson] = useState("{}");
  const [result, setResult] = useState<RuleEvaluationResultDto | null>(null);
  const current = codes.includes(active) ? active : codes[0] ?? "";
  const docValue = (code: string) => documents[code] ?? sampleDocument(tree, code);
  const invalidDocs = codes.filter((code) => !isJsonObject(docValue(code)));
  const invalidParams = parameters.length > 0 && !isJsonObject(parametersJson);

  const mutation = useMutation({
    mutationFn: testRule,
    onSuccess: setResult,
    onError: () => setResult(null),
  });

  const run = () => {
    mutation.mutate({
      astJson: buildAst(tree, mode),
      documents: Object.fromEntries(codes.map((code) => [code, JSON.stringify(JSON.parse(docValue(code)))])),
      parametersJson: parameters.length ? JSON.stringify(JSON.parse(parametersJson)) : "{}",
    });
  };

  if (codes.length === 0) {
    return <p className="text-[13px] text-[var(--color-muted-foreground)]">Add at least one condition with a document and field to test this rule.</p>;
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)]">
      <div className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          {codes.length > 1
            ? <Segmented label="Test document" size="sm" value={current} onChange={setActive} options={codes.map((code) => ({ value: code, label: code }))} />
            : <span className="font-mono text-[12px] font-semibold">{current}</span>}
          <Button type="button" size="xs" variant="ghost" onClick={() => setDocuments((value) => ({ ...value, [current]: sampleDocument(tree, current) }))}>
            <Wand2 />Reset sample
          </Button>
        </div>
        <textarea
          aria-label={`${current} test document JSON`}
          rows={10}
          spellCheck={false}
          className={cn(textareaClass, invalidDocs.includes(current) && "border-[var(--color-destructive)]")}
          value={docValue(current)}
          onChange={(event) => setDocuments((value) => ({ ...value, [current]: event.target.value }))}
        />
        {parameters.length > 0 && (
          <div className="space-y-1.5">
            <p className="text-[11.5px] font-semibold uppercase tracking-wider text-[var(--color-muted-foreground)]">Template parameters · {parameters.join(", ")}</p>
            <textarea aria-label="Test parameters JSON" rows={3} spellCheck={false} className={cn(textareaClass, invalidParams && "border-[var(--color-destructive)]")} value={parametersJson} onChange={(event) => setParametersJson(event.target.value)} />
          </div>
        )}
        {(invalidDocs.length > 0 || invalidParams) && <p className="text-[12px] text-[var(--color-destructive)]">Each test input must be a valid JSON object.</p>}
        <Button type="button" onClick={run} disabled={blocked || mutation.isPending || invalidDocs.length > 0 || invalidParams}>
          <Play className="size-4" />{mutation.isPending ? "Running…" : "Run test"}
        </Button>
        {blocked && <p className="text-[12px] text-[var(--color-muted-foreground)]">Fix the highlighted conditions before testing.</p>}
      </div>

      <div aria-live="polite" className="min-h-[10rem] rounded-xl border border-dashed border-[var(--color-border)] p-4">
        {mutation.isError && <p className="text-[13px] text-[var(--color-destructive)]">{describe(mutation.error)}</p>}
        {!result && !mutation.isError && (
          <div className="grid h-full place-items-center text-center text-[12.5px] text-[var(--color-muted-foreground)]">
            Edit the sample values and run the test to see how this rule behaves, step by step.
          </div>
        )}
        {result && <TestResult result={result} mode={mode} />}
      </div>
    </div>
  );
}

function TestResult({ result, mode }: { result: RuleEvaluationResultDto; mode: RuleMode }) {
  const verdict =
    result.status === "Passed" ? { icon: CheckCircle2, tone: "text-[var(--color-success)]", title: mode === "flag" ? "No finding" : "Rule passed", body: mode === "flag" ? "The conditions did not match, so no finding would be raised." : "All conditions were satisfied." }
      : result.status === "Failed" ? { icon: XCircle, tone: "text-[var(--color-destructive)]", title: mode === "flag" ? "Finding raised" : "Rule failed", body: mode === "flag" ? "The conditions matched, so this document would be flagged." : "The conditions were not satisfied." }
        : result.status === "NotEvaluated" ? { icon: CircleSlash, tone: "text-[var(--color-warning)]", title: "Not evaluated", body: result.reason ?? "A document or field was missing." }
          : { icon: CircleAlert, tone: "text-[var(--color-warning)]", title: "Evaluation error", body: result.reason ?? "Operand types are incompatible." };
  const Icon = verdict.icon;
  return (
    <div className="space-y-3">
      <div className="flex items-start gap-3">
        <Icon className={cn("mt-0.5 size-6 shrink-0", verdict.tone)} />
        <div>
          <p className="font-display text-[15px] font-semibold" data-testid="rule-test-verdict">{verdict.title}</p>
          <p className="text-[12.5px] text-[var(--color-muted-foreground)]">{verdict.body}</p>
        </div>
      </div>
      {result.trace.length > 0 && (
        <ol className="max-h-64 space-y-1 overflow-auto pr-1">
          {result.trace.filter((step) => step.node === "Comparison").map((step, index) => (
            <li key={index} className="flex items-center gap-2 rounded-lg bg-[oklch(from_var(--color-muted)_l_c_h_/_0.5)] px-2.5 py-1.5 text-[12px]">
              <EntityStatusBadge tone={STATUS_TONE[step.status]}>{step.status === "NotEvaluated" ? "Skipped" : step.status}</EntityStatusBadge>
              <span className="min-w-0 truncate font-mono">
                {step.left ?? "∅"} <span className="text-[var(--color-muted-foreground)]">{step.operator}</span> {step.right ?? ""}
              </span>
              {step.reason && <span className="ml-auto shrink-0 text-[11px] text-[var(--color-muted-foreground)]">{step.reason}</span>}
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
