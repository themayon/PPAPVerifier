import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, Circle, LoaderCircle, RefreshCw, XCircle } from "lucide-react";
import { Link, useParams } from "react-router-dom";
import { getPpapJob } from "@/api/ppap";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { describe } from "@/lib/list-helpers";
import { useRealtimeEvent } from "@/realtime/realtime-context";
import "@/styles/ppap.css";

type PpapJobProgressedPayload = {
  jobId: string;
  submissionId: string;
  status: string;
  currentStage: string;
  progressPercent: number;
  updatedAtUtc: string;
};

const ACTIVE_POLLING_WINDOW_MS = 15 * 60 * 1000;

const PROCESSING_STAGES = [
  ["UploadVerification", "Upload verification"],
  ["PreprocessingExtraction", "Preprocessing and extraction"],
  ["DocumentClassification", "Document classification"],
  ["CrossValidation", "Cross-validation"],
  ["ReportGeneration", "Report generation"],
  ["NotificationDelivery", "Notification delivery"],
] as const;

function statusClass(status: string) {
  return `ppap-status ppap-status--${status.toLowerCase()}`;
}

export function PpapProcessingPage() {
  const { jobId = "" } = useParams();
  const queryClient = useQueryClient();
  const queryKey = ["ppap", "jobs", jobId] as const;

  useRealtimeEvent<PpapJobProgressedPayload>(
    "PpapJobProgressed",
    (payload) => {
      if (payload.jobId !== jobId) return;
      void queryClient.invalidateQueries({ queryKey, exact: true });
    },
    [jobId, queryClient],
  );

  const jobQuery = useQuery({
    queryKey,
    queryFn: () => getPpapJob(jobId),
    enabled: Boolean(jobId),
    refetchInterval: (query) => {
      const job = query.state.data;
      const active = job?.status === "Queued" || job?.status === "Processing";
      const createdAt = job ? new Date(job.createdAtUtc).getTime() : Number.NaN;
      return active && Number.isFinite(createdAt) && Date.now() - createdAt < ACTIVE_POLLING_WINDOW_MS
        ? 2000
        : false;
    },
  });

  if (jobQuery.isLoading) {
    return <div className="ppap-page ppap-processing-loading" aria-label="Loading processing status"><Skeleton className="h-96 w-full" /></div>;
  }

  if (jobQuery.isError) {
    return <div className="ppap-page"><section className="ppap-error" role="alert"><div><strong>Processing status could not be loaded.</strong><span>{describe(jobQuery.error)}</span></div><Button variant="outline" onClick={() => void jobQuery.refetch()}><RefreshCw />Try again</Button></section><Link to="/ppap">Back to PPAP home</Link></div>;
  }

  const job = jobQuery.data;
  if (!job) {
    return <div className="ppap-page"><section className="ppap-empty"><h1>Processing job not found</h1><Link to="/ppap">Back to PPAP home</Link></section></div>;
  }

  const currentStageIndex = PROCESSING_STAGES.findIndex(([stage]) => stage === job.currentStage);
  const completed = job.status === "Completed";
  const failed = job.status === "Failed" || job.status === "Cancelled";
  const active = job.status === "Queued" || job.status === "Processing";
  const createdAt = new Date(job.createdAtUtc).getTime();
  const stale = active && Number.isFinite(createdAt) && Date.now() - createdAt >= ACTIVE_POLLING_WINDOW_MS;

  return (
    <div className="ppap-page ppap-processing-page">
      <section className="ppap-processing-card" aria-labelledby="processing-title">
        <div className="ppap-processing-heading">
          <div className={failed ? "ppap-processing-icon ppap-processing-icon--failed" : "ppap-processing-icon"} aria-hidden="true">{failed ? <XCircle /> : completed ? <Check /> : <LoaderCircle />}</div>
          <div><p className="ppap-eyebrow">PPAP processing</p><h1 id="processing-title">{completed ? "Processing complete" : failed ? "Processing stopped" : "Your package is being processed"}</h1><p>{completed ? "All processing stages completed successfully." : failed ? "The job could not be completed." : "This page updates automatically while work is in progress."}</p></div>
          <span className={statusClass(job.status)}>{job.status}</span>
        </div>

        <p className="sr-only" role="status" aria-live="polite" aria-atomic="true">
          {`Processing status: ${job.status}. Current stage: ${job.currentStage}. ${job.progressPercent}% complete.`}
        </p>
        <div
          className="ppap-job-progress"
          role="progressbar"
          aria-label="PPAP processing progress"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={job.progressPercent}
          aria-valuetext={`${job.progressPercent}% complete`}
        ><div><span style={{ width: `${job.progressPercent}%` }} /></div><strong aria-hidden="true">{job.progressPercent}%</strong></div>

        <ol className="ppap-processing-stages">
          {PROCESSING_STAGES.map(([stage, label], index) => {
            const isDone = completed || index < currentStageIndex;
            const isCurrent = !completed && index === currentStageIndex;
            return <li className={isDone ? "is-done" : isCurrent ? "is-current" : ""} key={stage}><span aria-hidden="true">{isDone ? <Check /> : isCurrent && !failed ? <LoaderCircle /> : <Circle />}</span><div><strong>{label}</strong>{isCurrent && <small>{failed ? "Stopped here" : job.status}</small>}</div></li>;
          })}
        </ol>

        {failed && <div className="ppap-processing-failure" role="alert"><strong>{job.errorCode ?? "Processing failed"}</strong><p>{job.errorMessage ?? "No additional error details were provided."}</p></div>}
        {stale && <div className="ppap-processing-stale" role="alert"><div><strong>Processing is taking longer than expected.</strong><p>Automatic polling has paused, but realtime updates remain active.</p></div><Button variant="outline" onClick={() => void jobQuery.refetch()} disabled={jobQuery.isFetching}><RefreshCw />{jobQuery.isFetching ? "Refreshing…" : "Refresh"}</Button></div>}

        <div className="ppap-processing-links"><Button asChild variant="outline"><Link to="/ppap">PPAP home</Link></Button><Button asChild variant={completed && job.reportId ? "outline" : "default"}><Link to={`/ppap/submissions/${job.submissionId}`}>View submission</Link></Button>{completed && job.reportId && <Button asChild><Link to={`/ppap/reports/${job.reportId}`}>View report</Link></Button>}</div>
      </section>
    </div>
  );
}
