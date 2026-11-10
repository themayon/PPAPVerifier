import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import type { RuleLookupDto } from "@/api/rules";
import { EntityStatusBadge } from "@/components/list";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/cn";
import { RULE_TYPES, outcomeSummary, ruleTypeForCategory, type RuleTypeKey } from "./rule-model";
import { SeverityBadge } from "./rule-ui";

export function RulePicker({
  rules,
  selected,
  onChange,
  disabled,
}: {
  rules: readonly RuleLookupDto[];
  selected: ReadonlySet<string>;
  onChange: (next: Set<string>) => void;
  disabled?: boolean;
}) {
  const [search, setSearch] = useState("");
  const [type, setType] = useState<RuleTypeKey | "all">("all");
  const [onlySelected, setOnlySelected] = useState(false);

  const counts = useMemo(() => {
    const map = new Map<RuleTypeKey, number>();
    rules.forEach((rule) => map.set(ruleTypeForCategory(rule.category).key, (map.get(ruleTypeForCategory(rule.category).key) ?? 0) + 1));
    return map;
  }, [rules]);

  const visible = useMemo(() => {
    const term = search.trim().toLowerCase();
    return rules.filter((rule) =>
      (type === "all" || ruleTypeForCategory(rule.category).key === type)
      && (!onlySelected || selected.has(rule.id))
      && (!term || rule.stableCode.toLowerCase().includes(term) || rule.name.toLowerCase().includes(term)));
  }, [rules, search, type, onlySelected, selected]);

  const toggle = (id: string) => {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    onChange(next);
  };
  const setVisible = (on: boolean) => {
    const next = new Set(selected);
    visible.forEach((rule) => (on ? next.add(rule.id) : next.delete(rule.id)));
    onChange(next);
  };

  return (
    <div className="flex min-h-0 flex-col gap-3">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--color-muted-foreground)]" />
        <input
          aria-label="Search rules to map"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Search rule code or name"
          className="h-9 w-full rounded-lg border border-[var(--color-input)] bg-transparent pl-9 pr-3 text-sm outline-none focus:border-[var(--color-ring)]"
        />
      </div>
      <div role="tablist" aria-label="Rule type" className="flex flex-wrap gap-1">
        {[{ key: "all" as const, plural: "All", count: rules.length }, ...RULE_TYPES.map((item) => ({ key: item.key, plural: item.plural, count: counts.get(item.key) ?? 0 }))].map((item) => (
          <button
            key={item.key}
            type="button"
            role="tab"
            aria-selected={type === item.key}
            onClick={() => setType(item.key)}
            className={cn(
              "inline-flex h-7 cursor-pointer items-center gap-1.5 rounded-full border px-2.5 text-[11.5px] font-medium transition-colors",
              type === item.key ? "border-[oklch(from_var(--color-primary)_l_c_h_/_0.3)] bg-[var(--color-primary-soft)] text-[var(--color-primary)]" : "border-[var(--color-border)] text-[var(--color-muted-foreground)] hover:text-[var(--color-foreground)]",
            )}
          >
            {item.plural}<span className="font-mono text-[10px] opacity-70">{item.count}</span>
          </button>
        ))}
      </div>
      <div className="flex flex-wrap items-center gap-2 text-[12px]">
        <span className="font-medium">{selected.size} selected</span>
        <span className="text-[var(--color-muted-foreground)]">· {visible.length} shown</span>
        <label className="ml-2 inline-flex cursor-pointer items-center gap-1.5 text-[var(--color-muted-foreground)]">
          <input type="checkbox" className="size-3.5 accent-[var(--color-primary)]" checked={onlySelected} onChange={(event) => setOnlySelected(event.target.checked)} />
          Selected only
        </label>
        {!disabled && (
          <span className="ml-auto flex gap-1">
            <Button type="button" size="xs" variant="ghost" onClick={() => setVisible(true)} disabled={visible.length === 0}>Select shown</Button>
            <Button type="button" size="xs" variant="ghost" onClick={() => setVisible(false)} disabled={visible.length === 0}>Clear shown</Button>
          </span>
        )}
      </div>
      <ul className="min-h-0 flex-1 divide-y divide-[var(--color-border)] overflow-y-auto rounded-xl border border-[var(--color-border)]" aria-label="Rules">
        {visible.length === 0 && <li className="px-4 py-8 text-center text-[12.5px] text-[var(--color-muted-foreground)]">No rules match.</li>}
        {visible.map((rule) => {
          const meta = outcomeSummary(rule.latestOutcomeJson);
          const ruleType = ruleTypeForCategory(rule.category);
          return (
            <li key={rule.id}>
              <label className={cn("flex cursor-pointer items-start gap-3 px-3 py-2.5 hover:bg-[oklch(from_var(--color-accent)_l_c_h_/_0.4)]", disabled && "cursor-default")}>
                <input type="checkbox" aria-label={`${rule.stableCode} ${rule.name}`} className="mt-1 size-4 accent-[var(--color-primary)]" checked={selected.has(rule.id)} disabled={disabled} onChange={() => toggle(rule.id)} />
                <span className="min-w-0 flex-1">
                  <span className="flex flex-wrap items-center gap-1.5">
                    <code className="rounded bg-[var(--color-muted)] px-1 font-mono text-[11px] font-semibold">{rule.stableCode}</code>
                    <span className="truncate text-[13px] font-medium">{rule.name}</span>
                  </span>
                  <span className="mt-1 flex flex-wrap items-center gap-1.5 text-[11px] text-[var(--color-muted-foreground)]">
                    <SeverityBadge severity={meta.severity} />
                    <span>{ruleType.label}</span>
                    {!rule.isLive && <EntityStatusBadge tone="warning">Not live</EntityStatusBadge>}
                  </span>
                </span>
              </label>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
