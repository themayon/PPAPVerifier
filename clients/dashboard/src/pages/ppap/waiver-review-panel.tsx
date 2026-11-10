import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Clock3, XCircle } from "lucide-react";
import { decideFindingWaiverRequest, getPendingWaiverRequests, type WaiverReviewDto } from "@/api/ppap";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { describe, formatDateTimeMono } from "@/lib/list-helpers";
import { toast } from "sonner";

export function WaiverReviewPanel({ submissionId, enabled }: { submissionId: string; enabled: boolean }) {
  const queryClient = useQueryClient();
  const [selection, setSelection] = useState<{ item: WaiverReviewDto; approve: boolean } | null>(null);
  const [reason, setReason] = useState("");
  const query = useQuery({ queryKey: ["ppap", "operations", "submissions", submissionId, "waiver-requests"], queryFn: () => getPendingWaiverRequests(submissionId), enabled: enabled && Boolean(submissionId) });
  const decide = useMutation({
    mutationFn: decideFindingWaiverRequest,
    onSuccess: async (_, variables) => {
      toast.success(variables.approve ? "Waiver request approved" : "Waiver request rejected");
      setSelection(null);
      setReason("");
      await queryClient.invalidateQueries({ queryKey: ["ppap", "operations", "submissions", submissionId, "waiver-requests"] });
    },
    onError: (error) => toast.error("Unable to review waiver request", { description: describe(error) }),
  });
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!selection || !reason.trim()) return;
    decide.mutate({ requestId: selection.item.request.id, approve: selection.approve, reason: reason.trim(), expectedVersion: selection.item.request.version });
  };

  if (!enabled) return null;
  return <section className="space-y-3" aria-labelledby="pending-waivers-title">
    <div className="flex items-center justify-between"><div><h2 id="pending-waivers-title" className="text-lg font-semibold">Pending waiver requests</h2><p className="text-sm text-[var(--color-muted-foreground)]">Review supplier requests tied to current document evidence.</p></div>{query.data && <span className="rounded-full bg-[var(--color-primary-soft)] px-3 py-1 text-xs font-semibold text-[var(--color-primary)]">{query.data.length} pending</span>}</div>
    {query.isLoading && <Skeleton className="h-28 w-full" />}
    {query.isError && <p role="alert" className="text-sm text-[var(--color-destructive)]">{describe(query.error)}</p>}
    {query.data?.length === 0 && <Card><CardContent className="flex items-center gap-2 py-5 text-sm text-[var(--color-muted-foreground)]"><CheckCircle2 className="size-4 text-[var(--color-success)]" />No pending waiver requests.</CardContent></Card>}
    {query.data?.map((item) => <Card key={item.request.id}><CardHeader><div className="flex flex-wrap items-start justify-between gap-3"><div><CardTitle>{item.stableRuleCode} · {item.ruleName}</CardTitle><CardDescription>Requested {formatDateTimeMono(item.request.requestedAtUtc)}</CardDescription></div><span className="inline-flex items-center gap-1 rounded-full bg-[oklch(from_var(--color-warning)_l_c_h_/_0.14)] px-2.5 py-1 text-xs font-semibold text-[var(--color-warning)]"><Clock3 className="size-3" />Pending</span></div></CardHeader><CardContent className="space-y-3"><p className="text-sm">{item.findingMessage}</p><div className="rounded-lg bg-[var(--color-muted)] p-3 text-sm"><strong>Supplier reason</strong><p className="mt-1 text-[var(--color-muted-foreground)]">{item.request.requestReason}</p></div><div className="flex gap-2"><Button size="sm" onClick={() => { setSelection({ item, approve: true }); setReason(""); }}><CheckCircle2 />Approve</Button><Button size="sm" variant="destructive" onClick={() => { setSelection({ item, approve: false }); setReason(""); }}><XCircle />Reject</Button></div></CardContent></Card>)}
    <Dialog open={selection !== null} onOpenChange={(open) => !open && !decide.isPending && setSelection(null)}><DialogContent><form onSubmit={submit}><DialogHeader><DialogTitle>{selection?.approve ? "Approve waiver request" : "Reject waiver request"}</DialogTitle><DialogDescription>{selection?.item.stableRuleCode} · The decision is recorded against this finding fingerprint.</DialogDescription></DialogHeader><DialogBody><Label htmlFor="waiver-review-reason">Review reason</Label><textarea id="waiver-review-reason" required maxLength={1000} rows={4} value={reason} onChange={(event) => setReason(event.target.value)} className="mt-2 w-full rounded-lg border border-[var(--color-input)] bg-transparent px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-ring)]" /></DialogBody><DialogFooter><Button type="button" variant="outline" onClick={() => setSelection(null)}>Cancel</Button><Button type="submit" variant={selection?.approve ? "default" : "destructive"} disabled={!reason.trim() || decide.isPending}>{decide.isPending ? "Saving…" : "Confirm"}</Button></DialogFooter></form></DialogContent></Dialog>
  </section>;
}
