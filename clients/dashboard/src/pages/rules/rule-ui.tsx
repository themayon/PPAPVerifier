import { useId, useMemo, useState, type KeyboardEvent, type ReactNode } from "react";
import { AlertTriangle, CheckCircle2, X, type LucideIcon } from "lucide-react";
import type { RuleLifecycle } from "@/api/rules";
import { Combobox, EntityStatusBadge, type EntityStatusTone } from "@/components/list";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/cn";
import { LIFECYCLE_LABELS, PPAP_DOCUMENTS, type FieldRef, type Severity } from "./rule-model";

const SEVERITY_TONE: Record<Severity, EntityStatusTone> = { Error: "danger", Warning: "warning", Info: "info" };

export function SeverityBadge({ severity }: { severity: Severity }) {
  return <EntityStatusBadge tone={SEVERITY_TONE[severity]} withDot>{severity}</EntityStatusBadge>;
}

const LIFECYCLE_TONE: Record<RuleLifecycle, EntityStatusTone> = { Draft: "default", InReview: "warning", Published: "success", Retired: "default" };

export function LifecycleBadge({ status }: { status: RuleLifecycle }) {
  return (
    <EntityStatusBadge tone={LIFECYCLE_TONE[status]} withDot={status === "Published"} className={status === "Retired" ? "opacity-60" : undefined}>
      {LIFECYCLE_LABELS[status]}
    </EntityStatusBadge>
  );
}

export function Segmented<T extends string>({
  label,
  value,
  options,
  onChange,
  disabled,
  size = "md",
}: {
  label: string;
  value: T;
  options: ReadonlyArray<{ value: T; label: ReactNode; title?: string }>;
  onChange: (value: T) => void;
  disabled?: boolean;
  size?: "sm" | "md";
}) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className={cn(
        "inline-flex items-center rounded-lg border border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.5)] p-0.5",
        disabled && "opacity-60",
      )}
    >
      {options.map((option) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            title={option.title}
            disabled={disabled}
            onClick={() => onChange(option.value)}
            className={cn(
              "inline-flex cursor-pointer items-center gap-1.5 rounded-md font-medium transition-all duration-[var(--duration-fast)]",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-ring)] disabled:cursor-not-allowed",
              size === "sm" ? "h-6 px-2 text-[11px]" : "h-7 px-3 text-[12.5px]",
              active
                ? "bg-[var(--color-card)] text-[var(--color-foreground)] shadow-xs ring-1 ring-[var(--color-border)]"
                : "text-[var(--color-muted-foreground)] hover:text-[var(--color-foreground)]",
            )}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}

export function FieldPicker({
  label,
  value,
  onChange,
  disabled,
  suggestions,
  invalid,
  compact,
}: {
  label: string;
  value: FieldRef;
  onChange: (value: FieldRef) => void;
  disabled?: boolean;
  suggestions: readonly FieldRef[];
  invalid?: boolean;
  compact?: boolean;
}) {
  const listId = useId();
  const documentOptions = useMemo(() => {
    const known = PPAP_DOCUMENTS.map((doc) => ({ value: doc.code, label: doc.code, hint: doc.name }));
    return value.documentCode && !known.some((option) => option.value === value.documentCode)
      ? [{ value: value.documentCode, label: value.documentCode, hint: "custom" }, ...known]
      : known;
  }, [value.documentCode]);
  const paths = useMemo(
    () => [...new Set(suggestions.filter((field) => field.documentCode === value.documentCode).map((field) => field.path))].slice(0, 50),
    [suggestions, value.documentCode],
  );
  return (
    <div className={cn("flex min-w-0 items-stretch", compact ? "gap-1" : "gap-1.5")}>
      <Combobox
        label={`${label} document`}
        value={value.documentCode || null}
        onChange={(code) => onChange({ ...value, documentCode: code ?? "" })}
        options={documentOptions}
        placeholder="Document"
        searchable
        disabled={disabled}
        className={cn("w-[7.5rem] shrink-0", invalid && !value.documentCode && "border-[var(--color-destructive)]")}
      />
      <Input
        aria-label={`${label} field`}
        list={paths.length > 0 ? listId : undefined}
        placeholder="field.path"
        value={value.path}
        disabled={disabled}
        aria-invalid={invalid && !value.path.trim() ? true : undefined}
        onChange={(event) => onChange({ ...value, path: event.target.value })}
        className="min-w-0 flex-1 font-mono text-[12.5px]"
      />
      {paths.length > 0 && (
        <datalist id={listId}>
          {paths.map((path) => <option key={path} value={path} />)}
        </datalist>
      )}
    </div>
  );
}

export function TagInput({ tags, onChange, disabled }: { tags: string[]; onChange: (tags: string[]) => void; disabled?: boolean }) {
  const [draft, setDraft] = useState("");
  const commit = () => {
    const values = draft.split(",").map((value) => value.trim()).filter((value) => value && value.length <= 64);
    if (values.length) onChange([...new Set([...tags, ...values])].slice(0, 20));
    setDraft("");
  };
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter" || event.key === ",") {
      event.preventDefault();
      commit();
    } else if (event.key === "Backspace" && !draft && tags.length) {
      onChange(tags.slice(0, -1));
    }
  };
  return (
    <div
      className={cn(
        "flex min-h-9 flex-wrap items-center gap-1.5 rounded-lg border border-[var(--color-input)] px-2 py-1 shadow-xs",
        "focus-within:border-[var(--color-ring)] focus-within:ring-[3px] focus-within:ring-[oklch(from_var(--color-ring)_l_c_h_/_0.5)]",
        disabled && "opacity-60",
      )}
    >
      {tags.map((tag) => (
        <span key={tag} className="inline-flex h-6 items-center gap-1 rounded-md bg-[var(--color-primary-soft)] pl-2 pr-1 text-[12px] font-medium text-[var(--color-primary)]">
          #{tag}
          {!disabled && (
            <button type="button" aria-label={`Remove tag ${tag}`} onClick={() => onChange(tags.filter((value) => value !== tag))} className="grid size-4 cursor-pointer place-items-center rounded hover:bg-[oklch(from_var(--color-primary)_l_c_h_/_0.15)]">
              <X className="size-3" />
            </button>
          )}
        </span>
      ))}
      <input
        aria-label="Add tag"
        value={draft}
        disabled={disabled}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={onKeyDown}
        onBlur={commit}
        placeholder={tags.length ? "" : "Add tags, press Enter"}
        className="h-6 min-w-[8rem] flex-1 bg-transparent text-sm outline-none placeholder:text-[var(--color-muted-foreground)]"
      />
    </div>
  );
}

export function SectionCard({
  id,
  step,
  icon: Icon,
  title,
  description,
  issues = 0,
  complete,
  actions,
  children,
}: {
  id: string;
  step: number;
  icon: LucideIcon;
  title: string;
  description?: ReactNode;
  issues?: number;
  complete?: boolean;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section id={id} aria-labelledby={`${id}-title`} className="scroll-mt-28 rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] shadow-xs">
      <header className="flex flex-wrap items-start justify-between gap-3 border-b border-[oklch(from_var(--color-border)_l_c_h_/_0.6)] px-5 py-4">
        <div className="flex min-w-0 items-start gap-3">
          <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-[var(--color-primary-soft)] text-[var(--color-primary)]">
            <Icon className="size-[18px]" />
          </span>
          <div className="min-w-0">
            <h2 id={`${id}-title`} className="flex items-center gap-2 font-display text-[15px] font-semibold text-[var(--color-foreground)]">
              <span className="font-mono text-[11px] text-[var(--color-muted-foreground)]">{String(step).padStart(2, "0")}</span>
              {title}
              {issues > 0 ? (
                <span className="inline-flex items-center gap-1 text-[11px] font-medium text-[var(--color-warning)]"><AlertTriangle className="size-3.5" />{issues}</span>
              ) : complete ? (
                <CheckCircle2 aria-label="Complete" className="size-4 text-[var(--color-success)]" />
              ) : null}
            </h2>
            {description && <p className="mt-0.5 text-[12.5px] text-[var(--color-muted-foreground)]">{description}</p>}
          </div>
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </header>
      <div className="p-5">{children}</div>
    </section>
  );
}
