import { useMemo, useState, type KeyboardEvent, type ReactNode } from "react";
import { AlertTriangle, Check, CheckCircle2, X, type LucideIcon } from "lucide-react";
import type { RuleColumnDto, RuleLifecycle } from "@/api/rules";
import { Combobox, EntityStatusBadge, type EntityStatusTone } from "@/components/list";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { cn } from "@/lib/cn";
import { LIFECYCLE_LABELS, PPAP_DOCUMENTS, type ColumnIndex, type FieldRef, type Severity } from "./rule-model";

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

export function DocumentPicker({ label, value, onChange, disabled, placeholder = "Document", className }: { label: string; value: string; onChange: (code: string) => void; disabled?: boolean; placeholder?: string; className?: string }) {
  const options = useMemo(() => {
    const known = PPAP_DOCUMENTS.map((doc) => ({ value: doc.code, label: doc.code, hint: doc.name }));
    return value && !known.some((option) => option.value === value) ? [{ value, label: value, hint: "custom" }, ...known] : known;
  }, [value]);
  return (
    <div role="group" aria-label={label} className={className}>
      <Combobox label={label} value={value || null} onChange={(code) => onChange(code ?? "")} options={options} placeholder={placeholder} searchable disabled={disabled} />
    </div>
  );
}

export function ColumnPicker({
  label,
  value,
  onChange,
  columns,
  documentCode,
  showDocument,
  disabled,
  invalid,
  placeholder = "Select column",
  className,
}: {
  label: string;
  value: FieldRef;
  onChange: (value: FieldRef, column: RuleColumnDto | undefined) => void;
  columns: ColumnIndex;
  documentCode: string;
  showDocument?: boolean;
  disabled?: boolean;
  invalid?: boolean;
  placeholder?: string;
  className?: string;
}) {
  const options = useMemo(() => {
    const list = columns.list.map((column) => ({
      value: column.key,
      label: column.name,
      hint: column.kind === "Header" ? `Header · ${column.dataType}` : column.dataType,
    }));
    return value.path && !columns.byKey.has(value.path) ? [{ value: value.path, label: value.path, hint: "not in master" }, ...list] : list;
  }, [columns, value.path]);
  const differentDocument = Boolean(value.documentCode && documentCode && value.documentCode !== documentCode);
  return (
    <div role="group" aria-label={label} className={cn("flex min-w-0 items-stretch gap-1", className)}>
      {(showDocument || differentDocument) && (
        <DocumentPicker
          label={`${label} document`}
          value={value.documentCode || documentCode}
          disabled={disabled}
          onChange={(code) => onChange({ ...value, documentCode: code }, columns.byKey.get(value.path))}
          className="w-[6.5rem] shrink-0"
        />
      )}
      <div className={cn("min-w-0 flex-1 rounded-md", invalid && !value.path && "ring-1 ring-[var(--color-destructive)]")}>
        <Combobox
          label={label}
          value={value.path || null}
          onChange={(key) => onChange({ documentCode: value.documentCode || documentCode, path: key ?? "" }, key ? columns.byKey.get(key) : undefined)}
          options={options}
          placeholder={placeholder}
          searchable
          disabled={disabled}
        />
      </div>
    </div>
  );
}

const COLOR_SWATCHES = [
  "#ef4444", "#f97316", "#f59e0b", "#eab308", "#84cc16", "#22c55e",
  "#10b981", "#14b8a6", "#06b6d4", "#3b82f6", "#6366f1", "#8b5cf6",
  "#a855f7", "#ec4899", "#f43f5e", "#fecaca", "#fde68a", "#bbf7d0",
  "#bfdbfe", "#e9d5ff", "#ffffff", "#d1d5db", "#6b7280", "#111827",
] as const;
const HEX_PATTERN = /^#[0-9a-fA-F]{6}$/;

export function ColorPicker({ label, value, onChange, disabled }: { label: string; value: string; onChange: (color: string) => void; disabled?: boolean }) {
  const [hex, setHex] = useState(value);
  const commitHex = (next: string) => {
    const normalized = next.startsWith("#") ? next : `#${next}`;
    if (HEX_PATTERN.test(normalized)) onChange(normalized.toLowerCase());
  };
  return (
    <DropdownMenu onOpenChange={(open) => open && setHex(value)}>
      <DropdownMenuTrigger asChild disabled={disabled}>
        <button
          type="button"
          aria-label={label}
          title={value}
          disabled={disabled}
          className="grid h-9 w-12 cursor-pointer place-items-center rounded-lg border border-[var(--color-input)] bg-transparent p-1 disabled:cursor-not-allowed disabled:opacity-60"
        >
          <span aria-hidden className="size-full rounded-md ring-1 ring-inset ring-black/10" style={{ backgroundColor: value }} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-0 w-[13.5rem] p-2">
        <div role="group" aria-label={`${label} swatches`} className="grid grid-cols-6 gap-1.5">
          {COLOR_SWATCHES.map((swatch) => {
            const selected = swatch.toLowerCase() === value.toLowerCase();
            return (
              <DropdownMenuItem
                key={swatch}
                aria-label={swatch}
                onSelect={() => onChange(swatch)}
                className="m-0 grid size-7 place-items-center rounded-md p-0 ring-1 ring-inset ring-black/10 data-[highlighted]:ring-2 data-[highlighted]:ring-[var(--color-ring)]"
                style={{ backgroundColor: swatch }}
              >
                {selected && <Check className="size-3.5 text-white mix-blend-difference" />}
              </DropdownMenuItem>
            );
          })}
        </div>
        <div className="mt-2 flex items-center gap-2">
          <span aria-hidden className="size-7 shrink-0 rounded-md ring-1 ring-inset ring-black/10" style={{ backgroundColor: HEX_PATTERN.test(hex) ? hex : value }} />
          <input
            aria-label={`${label} hex`}
            value={hex}
            maxLength={7}
            spellCheck={false}
            onChange={(event) => { setHex(event.target.value); commitHex(event.target.value); }}
            onKeyDown={(event) => { if (event.key !== "Escape") event.stopPropagation(); }}
            className="h-7 min-w-0 flex-1 rounded-md border border-[var(--color-input)] bg-transparent px-2 font-mono text-[12px] uppercase outline-none focus:border-[var(--color-ring)]"
          />
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
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
