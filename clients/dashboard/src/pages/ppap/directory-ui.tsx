import type { ReactNode } from "react";
import { ChevronLeft, ChevronRight, type LucideIcon } from "lucide-react";
import { cn } from "@/lib/cn";
import { scoreTone, type Tone } from "@/pages/ppap/directory-utils";

const TONE_TEXT: Record<Tone, string> = {
  primary: "text-[var(--color-primary)]",
  success: "text-[var(--color-success)]",
  warning: "text-[var(--color-warning)]",
  danger: "text-[var(--color-destructive)]",
  muted: "text-[var(--color-muted-foreground)]",
};
const TONE_BG: Record<Tone, string> = {
  primary: "bg-[var(--color-primary)]",
  success: "bg-[var(--color-success)]",
  warning: "bg-[var(--color-warning)]",
  danger: "bg-[var(--color-destructive)]",
  muted: "bg-[var(--color-muted-foreground)]",
};
const TONE_SOFT: Record<Tone, string> = {
  primary: "bg-[var(--color-primary-soft)] text-[var(--color-primary)]",
  success: "bg-[oklch(from_var(--color-success)_l_c_h_/_0.12)] text-[var(--color-success)]",
  warning: "bg-[oklch(from_var(--color-warning)_l_c_h_/_0.14)] text-[var(--color-warning)]",
  danger: "bg-[oklch(from_var(--color-destructive)_l_c_h_/_0.12)] text-[var(--color-destructive)]",
  muted: "bg-[var(--color-muted)] text-[var(--color-muted-foreground)]",
};

export function StatCard({ label, value, icon: Icon, tone, footnote, badge, className }: {
  label: string;
  value: ReactNode;
  icon: LucideIcon;
  tone: Tone;
  footnote?: ReactNode;
  badge?: ReactNode;
  className?: string;
}) {
  return (
    <section aria-label={label} className={cn("rounded-2xl border border-[var(--color-border)] bg-[var(--color-card)] p-5 shadow-xs", className)}>
      <div className="flex items-start justify-between gap-3">
        <span className={cn("grid size-10 place-items-center rounded-xl", TONE_SOFT[tone])}><Icon className="size-5" /></span>
        {badge}
      </div>
      <p className="mt-4 text-[11px] font-semibold uppercase tracking-[0.12em] text-[var(--color-muted-foreground)]">{label}</p>
      <p className="mt-1 font-display text-[28px] font-semibold tabular-nums tracking-tight">{value}</p>
      {footnote && <div className="mt-1 text-[12px]">{footnote}</div>}
    </section>
  );
}

export function TonePill({ tone, children }: { tone: Tone; children: ReactNode }) {
  return <span className={cn("inline-flex h-6 items-center rounded-full px-2.5 text-[11px] font-semibold", TONE_SOFT[tone])}>{children}</span>;
}

export function StatusDot({ tone, children }: { tone: Tone; children: ReactNode }) {
  return (
    <span className={cn("inline-flex h-6 items-center gap-1.5 rounded-full px-2.5 text-[12px] font-semibold", TONE_SOFT[tone])}>
      <span aria-hidden className={cn("size-1.5 rounded-full", TONE_BG[tone])} />
      {children}
    </span>
  );
}

export function ScoreBar({ value, label }: { value: number | null | undefined; label: string }) {
  const tone = scoreTone(value);
  if (value === null || value === undefined) return <span className="text-[12px] text-[var(--color-muted-foreground)]">No data</span>;
  return (
    <span className="flex items-center gap-2.5" aria-label={`${label}: ${value}%`}>
      <span className="relative h-1.5 w-24 overflow-hidden rounded-full bg-[var(--color-muted)]">
        <span className={cn("absolute inset-y-0 left-0 rounded-full", TONE_BG[tone])} style={{ width: `${Math.min(100, Math.max(0, value))}%` }} />
      </span>
      <span className={cn("font-mono text-[12px] font-semibold tabular-nums", TONE_TEXT[tone])}>{Math.round(value)}%</span>
    </span>
  );
}

export function ElementsProgress({ completed, total, tone }: { completed: number; total: number; tone: Tone }) {
  const percent = total > 0 ? Math.round((completed / total) * 100) : 0;
  const segments = 8;
  const filled = Math.round((percent / 100) * segments);
  return (
    <div className="w-full max-w-[15rem]" role="progressbar" aria-valuemin={0} aria-valuemax={total} aria-valuenow={completed} aria-label={`${completed} of ${total} elements complete`}>
      <div className="flex items-center justify-between text-[11px] font-semibold tabular-nums">
        <span className={tone === "danger" ? TONE_TEXT.danger : undefined}>{completed}/{total}</span>
        <span className={tone === "danger" ? TONE_TEXT.danger : "text-[var(--color-muted-foreground)]"}>{percent}%</span>
      </div>
      <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-[var(--color-muted)]">
        <div className={cn("h-full rounded-full", TONE_BG[tone])} style={{ width: `${percent}%` }} />
      </div>
      <div aria-hidden className="mt-1.5 flex gap-1">
        {Array.from({ length: segments }).map((_, index) => (
          <span key={index} className={cn("h-1 w-2.5 rounded-full", index < filled ? TONE_BG[tone] : "bg-[var(--color-muted)]")} />
        ))}
      </div>
    </div>
  );
}

export function NumberedPager({ page, totalPages, total, pageSize, unit, onPage }: {
  page: number;
  totalPages: number;
  total: number;
  pageSize: number;
  unit: string;
  onPage: (page: number) => void;
}) {
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);
  const pages = pageWindow(page, totalPages);
  return (
    <nav aria-label={`${unit} pagination`} className="flex flex-col gap-3 border-t border-[var(--color-border)] bg-[oklch(from_var(--color-muted)_l_c_h_/_0.35)] px-5 py-3 sm:flex-row sm:items-center sm:justify-between">
      <p className="text-[13px] text-[var(--color-muted-foreground)]">
        Showing <strong className="text-[var(--color-foreground)]">{from}</strong> to <strong className="text-[var(--color-foreground)]">{to}</strong> of <strong className="text-[var(--color-foreground)]">{total}</strong> {unit}
      </p>
      {totalPages > 1 && (
        <div className="flex items-center gap-1.5">
          <PagerButton label="Previous page" disabled={page <= 1} onClick={() => onPage(page - 1)}><ChevronLeft className="size-4" /></PagerButton>
          {pages.map((item, index) => item === "…"
            ? <span key={`gap-${index}`} className="px-1 text-[var(--color-muted-foreground)]">…</span>
            : <PagerButton key={item} label={`Page ${item}`} current={item === page} onClick={() => onPage(item)}>{item}</PagerButton>)}
          <PagerButton label="Next page" disabled={page >= totalPages} onClick={() => onPage(page + 1)}><ChevronRight className="size-4" /></PagerButton>
        </div>
      )}
    </nav>
  );
}

function PagerButton({ label, current, disabled, onClick, children }: { label: string; current?: boolean; disabled?: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      aria-label={label}
      aria-current={current ? "page" : undefined}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        "grid h-8 min-w-8 cursor-pointer place-items-center rounded-lg border px-2 text-[13px] font-medium tabular-nums transition-colors disabled:cursor-not-allowed disabled:opacity-40",
        current ? "border-[var(--color-primary)] bg-[var(--color-primary)] text-[var(--color-primary-foreground)]" : "border-[var(--color-border)] bg-[var(--color-card)] hover:bg-[var(--color-muted)]",
      )}
    >
      {children}
    </button>
  );
}

function pageWindow(page: number, totalPages: number): (number | "…")[] {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, index) => index + 1);
  const pages = new Set([1, totalPages, page - 1, page, page + 1].filter((value) => value >= 1 && value <= totalPages));
  if (page <= 3) [2, 3].forEach((value) => pages.add(value));
  if (page >= totalPages - 2) [totalPages - 1, totalPages - 2].forEach((value) => pages.add(value));
  const sorted = [...pages].sort((a, b) => a - b);
  return sorted.flatMap((value, index) => (index > 0 && value - sorted[index - 1]! > 1 ? ["…" as const, value] : [value]));
}
