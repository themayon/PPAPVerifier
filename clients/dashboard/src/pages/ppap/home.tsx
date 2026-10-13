import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowRight,
  Bot,
  Coins,
  FileCheck2,
  FilePlus2,
  PackageCheck,
  RefreshCw,
  ShieldCheck,
  Sparkles,
} from "lucide-react";
import { toast } from "sonner";
import { Link, useNavigate } from "react-router-dom";
import {
  createCreditTopupRequest,
  createDraftSubmission,
  getMyCreditBalance,
  getMyCreditTopupRequests,
  getPublishedRuleTemplates,
  searchMyPpapSubmissions,
  type CreateCreditTopupRequestInput,
  type CreateDraftSubmissionInput,
  type PpapSubmissionDto,
} from "@/api/ppap";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { describe } from "@/lib/list-helpers";
import "@/styles/ppap.css";

const CREDIT_PACKAGES = [10, 50, 100] as const;
const WORKFLOW_STEPS = [
  "Part details",
  "Requirements",
  "Documents",
  "Validation",
  "Review",
  "Submit",
  "AI processing",
  "Result",
] as const;

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

function statusClass(status: string) {
  return `ppap-status ppap-status--${status.toLowerCase()}`;
}

export function PpapHomePage() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [draftOpen, setDraftOpen] = useState(false);

  const balanceQuery = useQuery({
    queryKey: ["ppap", "credits", "balance"],
    queryFn: getMyCreditBalance,
  });
  const submissionsQuery = useQuery({
    queryKey: ["ppap", "submissions", "me", { pageNumber: 1, pageSize: 5 }],
    queryFn: () => searchMyPpapSubmissions({ pageNumber: 1, pageSize: 5 }),
  });
  const topupsQuery = useQuery({
    queryKey: ["ppap", "credits", "topup-requests", "me", { page: 1, pageSize: 3 }],
    queryFn: () => getMyCreditTopupRequests({ page: 1, pageSize: 3 }),
  });

  const topupMutation = useMutation({
    mutationFn: (input: CreateCreditTopupRequestInput) => createCreditTopupRequest(input),
    onSuccess: () => {
      toast.success("Top-up request sent", {
        description: "Your request is awaiting manual approval.",
      });
      queryClient.invalidateQueries({ queryKey: ["ppap", "credits"] });
    },
    onError: (error) =>
      toast.error("Unable to request credits", { description: describe(error) }),
  });

  const requestPackage = (creditsRequested: 10 | 50 | 100) => {
    topupMutation.mutate({
      creditsRequested,
      paymentReference: null,
      note: null,
    });
  };

  const retryQueries = () => {
    void queryClient.invalidateQueries({ queryKey: ["ppap"] });
  };

  return (
    <div className="ppap-page">
      <header className="ppap-heading">
        <div className="ppap-heading__mark" aria-hidden="true">
          <Bot />
        </div>
        <div>
          <p className="ppap-eyebrow">Omnex AI</p>
          <h1>Production Part Approval Process</h1>
          <p>Build, validate, and submit your PPAP package from one workspace.</p>
        </div>
        <Button asChild variant="outline" className="ml-auto"><Link to="/ppap/parts">Manage parts</Link></Button>
        <Dialog open={draftOpen} onOpenChange={setDraftOpen}>
          <DialogTrigger asChild>
            <Button className="ppap-primary-action">
              <FilePlus2 />
              Create draft
            </Button>
          </DialogTrigger>
          <CreateDraftDialog onCreated={(id) => {
            setDraftOpen(false);
            navigate(`/ppap/submissions/${id}`);
          }} />
        </Dialog>
      </header>

      <section className="ppap-welcome" aria-labelledby="ppap-welcome-title">
        <div>
          <span className="ppap-welcome__icon" aria-hidden="true">
            <Sparkles />
          </span>
          <p>Supplier workspace</p>
          <h2 id="ppap-welcome-title">Welcome to your PPAP command center</h2>
          <span>Start a new draft or continue working with your recent submissions.</span>
        </div>
        <div className="ppap-welcome__seal" aria-hidden="true">
          <ShieldCheck />
        </div>
      </section>

      {(balanceQuery.isError || submissionsQuery.isError || topupsQuery.isError) && (
        <section className="ppap-error" role="alert">
          <div>
            <strong>Some PPAP data could not be loaded.</strong>
            <span>
              {describe(balanceQuery.error ?? submissionsQuery.error ?? topupsQuery.error)}
            </span>
          </div>
          <Button variant="outline" onClick={retryQueries}>
            <RefreshCw />
            Try again
          </Button>
        </section>
      )}

      <section className="ppap-overview-grid">
        <article className="ppap-credit-card" aria-labelledby="credit-balance-title">
          <div className="ppap-section-kicker">
            <Coins />
            <span id="credit-balance-title">Credit balance</span>
          </div>
          {balanceQuery.isLoading ? (
            <div className="ppap-credit-loading" aria-label="Loading credit balance">
              <Skeleton className="h-12 w-28" />
              <Skeleton className="h-5 w-40" />
            </div>
          ) : balanceQuery.data ? (
            <div className="ppap-credit-values">
              <div>
                <strong>{balanceQuery.data.availableCredits}</strong>
                <span>Available credits</span>
              </div>
              <div>
                <strong>{balanceQuery.data.reservedCredits}</strong>
                <span>Reserved credits</span>
              </div>
            </div>
          ) : (
            <p className="ppap-muted">Credit balance is unavailable.</p>
          )}
        </article>

        <article className="ppap-workflow-card" aria-labelledby="workflow-title">
          <div className="ppap-card-heading">
            <div>
              <p className="ppap-section-kicker"><PackageCheck /> PPAP workflow</p>
              <h2 id="workflow-title">Eight steps to a complete package</h2>
            </div>
          </div>
          <ol className="ppap-workflow">
            {WORKFLOW_STEPS.map((step, index) => (
              <li key={step}>
                <span>{index + 1}</span>
                <small>{step}</small>
                {index < WORKFLOW_STEPS.length - 1 && <ArrowRight aria-hidden="true" />}
              </li>
            ))}
          </ol>
        </article>
      </section>

      <section className="ppap-content-grid">
        <article className="ppap-panel" aria-labelledby="recent-submissions-title">
          <div className="ppap-card-heading">
            <div>
              <p className="ppap-section-kicker"><FileCheck2 /> Activity</p>
              <h2 id="recent-submissions-title">Recent submissions</h2>
            </div>
            {submissionsQuery.data && (
              <span className="ppap-count">{submissionsQuery.data.totalCount} total</span>
            )}
          </div>
          <RecentSubmissions
            loading={submissionsQuery.isLoading}
            submissions={submissionsQuery.data?.items ?? []}
          />
        </article>

        <article className="ppap-panel ppap-topups" aria-labelledby="credit-packages-title">
          <div className="ppap-card-heading">
            <div>
              <p className="ppap-section-kicker"><Coins /> Credit packages</p>
              <h2 id="credit-packages-title">Request more credits</h2>
            </div>
          </div>
          <p className="ppap-panel-copy">
            Select a package to send a manual top-up request. Credits are added only after an
            operator reviews and approves the request.
          </p>
          <div className="ppap-packages">
            {CREDIT_PACKAGES.map((credits) => (
              <div className={credits === 50 ? "ppap-package ppap-package--featured" : "ppap-package"} key={credits}>
                {credits === 50 && <span className="ppap-package__flag">Popular</span>}
                <strong>{credits}</strong>
                <span>PPAP credits</span>
                <Button
                  variant={credits === 50 ? "default" : "outline"}
                  disabled={topupMutation.isPending}
                  onClick={() => requestPackage(credits)}
                  aria-label={`Request ${credits} credit package`}
                >
                  {topupMutation.isPending && topupMutation.variables?.creditsRequested === credits
                    ? "Requesting…"
                    : "Request package"}
                </Button>
              </div>
            ))}
          </div>
          <div className="ppap-topup-history">
            <h3>Recent top-up requests</h3>
            {topupsQuery.isLoading ? (
              <Skeleton className="h-12 w-full" />
            ) : (topupsQuery.data?.items.length ?? 0) === 0 ? (
              <p className="ppap-muted">No top-up requests yet.</p>
            ) : (
              <ul>
                {topupsQuery.data?.items.map((request) => (
                  <li key={request.id}>
                    <span>{request.creditsRequested} credits</span>
                    <span className={statusClass(request.status)}>{request.status}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </article>
      </section>
    </div>
  );
}

function RecentSubmissions({
  loading,
  submissions,
}: {
  loading: boolean;
  submissions: PpapSubmissionDto[];
}) {
  if (loading) {
    return (
      <div className="ppap-submission-loading" aria-label="Loading recent submissions">
        {[0, 1, 2].map((item) => <Skeleton className="h-16 w-full" key={item} />)}
      </div>
    );
  }

  if (submissions.length === 0) {
    return (
      <div className="ppap-empty">
        <FilePlus2 aria-hidden="true" />
        <h3>No PPAP submissions yet</h3>
        <p>Create a draft to begin assembling your first package.</p>
      </div>
    );
  }

  return (
    <ul className="ppap-submissions">
      {submissions.map((submission) => (
        <li key={submission.id}>
          <Link to={`/ppap/submissions/${submission.id}`}>
            <div className="ppap-submission-icon" aria-hidden="true"><FileCheck2 /></div>
            <div className="ppap-submission-main">
              <strong>{submission.partName}</strong>
              <span>{submission.partNumber} · Revision {submission.drawingRevision} · Level {submission.ppapLevel}</span>
            </div>
            <div className="ppap-submission-meta">
              <span className={statusClass(submission.status)}>{submission.status}</span>
              <time dateTime={submission.updatedAtUtc ?? submission.createdAtUtc}>
                {formatDate(submission.updatedAtUtc ?? submission.createdAtUtc)}
              </time>
            </div>
          </Link>
        </li>
      ))}
    </ul>
  );
}

function CreateDraftDialog({ onCreated }: { onCreated: (id: string) => void }) {
  const queryClient = useQueryClient();
  const [partName, setPartName] = useState("");
  const [partNumber, setPartNumber] = useState("");
  const [drawingRevision, setDrawingRevision] = useState("");
  const [ppapLevel, setPpapLevel] = useState("3");
  const [ruleTemplateId, setRuleTemplateId] = useState("");
  const templatesQuery = useQuery({
    queryKey: ["rules", "templates", { status: "Published", pageNumber: 1, pageSize: 100 }],
    queryFn: getPublishedRuleTemplates,
  });

  const mutation = useMutation({
    mutationFn: (input: CreateDraftSubmissionInput) => createDraftSubmission(input),
    onSuccess: (id) => {
      toast.success("Draft created");
      queryClient.invalidateQueries({ queryKey: ["ppap", "submissions"] });
      setPartName("");
      setPartNumber("");
      setDrawingRevision("");
      setPpapLevel("3");
      setRuleTemplateId("");
      onCreated(id);
    },
    onError: (error) => toast.error("Unable to create draft", { description: describe(error) }),
  });

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!partName.trim() || !partNumber.trim() || !drawingRevision.trim()) return;
    mutation.mutate({
      partName: partName.trim(),
      partNumber: partNumber.trim(),
      drawingRevision: drawingRevision.trim(),
      ppapLevel: Number(ppapLevel),
      ruleTemplateId: ruleTemplateId || null,
    });
  };

  return (
    <DialogContent className="ppap-dialog">
      <form onSubmit={submit}>
        <DialogHeader>
          <DialogTitle>Create PPAP draft</DialogTitle>
          <DialogDescription>Enter the part details to start a new PPAP package.</DialogDescription>
        </DialogHeader>
        <DialogBody className="ppap-form-grid">
          <div className="ppap-field ppap-field--wide">
            <Label htmlFor="ppap-part-name">Part name</Label>
            <Input id="ppap-part-name" value={partName} onChange={(event) => setPartName(event.target.value)} required />
          </div>
          <div className="ppap-field">
            <Label htmlFor="ppap-part-number">Part number</Label>
            <Input id="ppap-part-number" value={partNumber} onChange={(event) => setPartNumber(event.target.value)} required />
          </div>
          <div className="ppap-field">
            <Label htmlFor="ppap-revision">Revision</Label>
            <Input id="ppap-revision" value={drawingRevision} onChange={(event) => setDrawingRevision(event.target.value)} required />
          </div>
          <div className="ppap-field ppap-field--wide">
            <Label htmlFor="ppap-level">PPAP level</Label>
            <select id="ppap-level" value={ppapLevel} onChange={(event) => setPpapLevel(event.target.value)}>
              {[1, 2, 3, 4, 5].map((level) => <option key={level} value={level}>Level {level}</option>)}
            </select>
          </div>
          <div className="ppap-field ppap-field--wide">
            <Label htmlFor="ppap-template">Checklist template</Label>
            <select
              id="ppap-template"
              value={ruleTemplateId}
              onChange={(event) => setRuleTemplateId(event.target.value)}
            >
              <option value="">Manual checklist</option>
              {templatesQuery.data?.items.map((template) => (
                <option key={template.id} value={template.id}>
                  {template.name} ({template.stableCode})
                </option>
              ))}
            </select>
            {templatesQuery.isLoading && <span role="status">Loading published templates…</span>}
            {templatesQuery.isError && (
              <span role="alert">Published templates could not be loaded. Manual checklist remains available.</span>
            )}
          </div>
        </DialogBody>
        <DialogFooter>
          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? "Creating…" : "Create draft"}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  );
}
