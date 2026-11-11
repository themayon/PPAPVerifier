import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, CheckCircle2, FilePlus2, RefreshCw, Send, Trash2, Upload } from "lucide-react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import {
  addSubmissionDocument,
  deleteDocumentRevision,
  getMyCreditBalance,
  getPpapSubmission,
  getSubmissionDocuments,
  getSubmissionWorkspace,
  markSubmissionDocumentNotApplicable,
  reserveDocumentRevision,
  submitPpapSubmission,
  type DocumentRevisionDto,
  type SubmissionDocumentDto,
} from "@/api/ppap";
import { Visibility } from "@/api/files";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { useFileUpload } from "@/hooks/use-file-upload";
import { describe } from "@/lib/list-helpers";
import { DocumentWorkspaceDialog } from "@/pages/ppap/document-workspace";
import "@/styles/ppap.css";

export const PPAP_DOCUMENT_CATALOGUE = [
  ["DWG", "Design record"],
  ["ECD", "Engineering change documents"],
  ["CEA", "Customer engineering approval"],
  ["DFMEA", "Design FMEA"],
  ["PFD", "Process flow diagram"],
  ["PFMEA", "Process FMEA"],
  ["CP", "Control plan"],
  ["MSA", "Measurement system analysis"],
  ["DIMR", "Dimensional results"],
  ["IMDS", "Material data submission"],
  ["MPTR", "Material performance test results"],
  ["IPS", "Initial process studies"],
  ["QLD", "Qualified laboratory documentation"],
  ["AAR", "Appearance approval report"],
  ["SAMP", "Sample production parts"],
  ["MSAM", "Master sample"],
  ["CAID", "Checking aids"],
  ["APDS", "Automotive product data submission"],
  ["CSR", "Customer-specific requirements"],
  ["PSW", "Part submission warrant"],
] as const;

const ALLOWED_EXTENSIONS = [
  ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".csv", ".jpg", ".jpeg", ".png",
  ".webp", ".gif", ".ico", ".zip",
];
const IMAGE_EXTENSIONS = new Set([".jpg", ".jpeg", ".png", ".webp", ".gif", ".ico"]);

function extension(file: File) {
  const dot = file.name.lastIndexOf(".");
  return dot < 0 ? "" : file.name.slice(dot).toLowerCase();
}

function category(file: File) {
  const ext = extension(file);
  if (IMAGE_EXTENSIONS.has(ext)) return "Image";
  if (ext === ".zip") return "Archive";
  return "Document";
}

function maxBytes(file: File) {
  const resolved = category(file);
  if (resolved === "Image") return 10 * 1024 * 1024;
  if (resolved === "Archive") return 50 * 1024 * 1024;
  return 25 * 1024 * 1024;
}

function statusClass(status: string) {
  return `ppap-status ppap-status--${status.toLowerCase()}`;
}

export function PpapSubmissionPage() {
  const { submissionId = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const pollUntilRef = useRef(0);
  const [workspaceDocumentId, setWorkspaceDocumentId] = useState<string | null>(null);

  const submissionQuery = useQuery({
    queryKey: ["ppap", "submissions", submissionId],
    queryFn: () => getPpapSubmission(submissionId),
    enabled: Boolean(submissionId),
  });
  const documentsQuery = useQuery({
    queryKey: ["ppap", "submissions", submissionId, "documents"],
    queryFn: () => getSubmissionDocuments(submissionId),
    enabled: Boolean(submissionId),
    refetchInterval: (query) => {
      const documents = query.state.data;
      return Date.now() < pollUntilRef.current
        && documents?.some((document) => document.status === "PendingUpload")
        ? 1500
        : false;
    },
  });
  const workspaceQuery = useQuery({
    queryKey: ["ppap", "submissions", submissionId, "workspace"],
    queryFn: () => getSubmissionWorkspace(submissionId),
    enabled: Boolean(submissionId),
    refetchInterval: (query) => query.state.data?.documents.some((document) => {
      const status = document.currentRevision?.processingStatus;
      return status === "Queued" || status === "Converting" || status === "Extracting" || status === "Validating";
    }) ? 2000 : false,
  });
  const balanceQuery = useQuery({
    queryKey: ["ppap", "credits", "balance"],
    queryFn: getMyCreditBalance,
  });

  const addMutation = useMutation({
    mutationFn: ({ id, documentCode }: { id: string; documentCode: string }) =>
      addSubmissionDocument(id, documentCode),
    onSuccess: () => queryClient.invalidateQueries({
      queryKey: ["ppap", "submissions", submissionId, "documents"],
    }),
    onError: (error) => toast.error("Unable to add document", { description: describe(error) }),
  });
  const notApplicableMutation = useMutation({
    mutationFn: ({ id, documentId, reason }: { id: string; documentId: string; reason: string }) =>
      markSubmissionDocumentNotApplicable(id, documentId, reason),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["ppap", "submissions", submissionId] });
      void queryClient.invalidateQueries({
        queryKey: ["ppap", "submissions", submissionId, "documents"],
      });
    },
    onError: (error) => toast.error("Unable to mark document N/A", { description: describe(error) }),
  });
  const deleteRevisionMutation = useMutation({
    mutationFn: (input: { submissionId: string; documentId: string; revisionId: string; expectedVersion: number }) =>
      deleteDocumentRevision(input),
    onSuccess: () => {
      toast.success("File deleted", { description: "Upload a replacement file for this document." });
      void queryClient.invalidateQueries({ queryKey: ["ppap", "submissions", submissionId] });
    },
    onError: (error) => toast.error("Unable to delete file", { description: describe(error) }),
  });
  const submitMutation = useMutation({
    mutationFn: ({ id, expectedVersion }: { id: string; expectedVersion: number }) =>
      submitPpapSubmission(id, expectedVersion),
    onSuccess: (jobId) => navigate(`/ppap/processing/${jobId}`),
    onError: (error) => toast.error("Unable to submit PPAP", { description: describe(error) }),
  });

  if (submissionQuery.isLoading || documentsQuery.isLoading || workspaceQuery.isLoading || balanceQuery.isLoading) {
    return <div className="ppap-page ppap-detail-loading" aria-label="Loading PPAP submission"><Skeleton className="h-24 w-full" /><Skeleton className="h-96 w-full" /></div>;
  }

  const error = submissionQuery.error ?? documentsQuery.error ?? workspaceQuery.error ?? balanceQuery.error;
  if (error) {
    return <div className="ppap-page"><section className="ppap-error" role="alert"><div><strong>Submission could not be loaded.</strong><span>{describe(error)}</span></div><Button variant="outline" onClick={() => void queryClient.invalidateQueries({ queryKey: ["ppap", "submissions", submissionId] })}><RefreshCw />Try again</Button></section><Link to="/ppap">Back to PPAP home</Link></div>;
  }

  const submission = submissionQuery.data;
  if (!submission) {
    return <div className="ppap-page"><section className="ppap-empty"><h1>Submission not found</h1><Link to="/ppap">Back to PPAP home</Link></section></div>;
  }

  const documents = documentsQuery.data ?? [];
  const revisionsByDocument = new Map(
    (workspaceQuery.data?.documents ?? []).map((document) => [document.id, document.currentRevision] as const),
  );
  const existingCodes = new Set(documents.map((document) => document.documentCode));
  const availableCodes = PPAP_DOCUMENT_CATALOGUE.filter(([code]) => !existingCodes.has(code));
  const hasAvailable = documents.some((document) => document.status === "Available");
  const hasBlockedDocument = documents.some((document) =>
    document.status === "Quarantined"
    || (document.status === "PendingUpload" && document.disposition !== "Retain"));
  const enoughCredits = (balanceQuery.data?.availableCredits ?? 0) >= 10;
  const isUpdating = addMutation.isPending
    || notApplicableMutation.isPending
    || deleteRevisionMutation.isPending
    || submissionQuery.isFetching
    || documentsQuery.isFetching;
  const canSubmit = submission.status === "Draft" && enoughCredits && hasAvailable && !hasBlockedDocument && !isUpdating;
  const blockers = [
    !enoughCredits && "At least 10 available credits are required.",
    !hasAvailable && "Upload at least one available document.",
    hasBlockedDocument && "Resolve pending Submit/Available or quarantined documents before submitting.",
    submission.status !== "Draft" && "Only draft submissions can be submitted.",
  ].filter(Boolean) as string[];

  const uploadFinalized = () => {
    pollUntilRef.current = Date.now() + 15000;
    void queryClient.invalidateQueries({ queryKey: ["ppap", "submissions", submissionId] });
    void queryClient.invalidateQueries({
      queryKey: ["ppap", "submissions", submissionId, "documents"],
    });
    void queryClient.invalidateQueries({
      queryKey: ["ppap", "submissions", submissionId, "workspace"],
    });
  };

  return (
    <div className="ppap-page">
      <Link className="ppap-back-link" to="/ppap"><ArrowLeft />PPAP home</Link>
      <header className="ppap-detail-header">
        <div><p className="ppap-eyebrow">Draft workspace</p><h1>{submission.partName}</h1><p>{submission.partNumber} · Revision {submission.drawingRevision} · PPAP level {submission.ppapLevel}</p></div>
        <div className="ppap-detail-badges">
          {submission.ruleTemplateId && submission.ruleTemplateVersionId && (
            <span className="ppap-status" title={`Template ${submission.ruleTemplateId}, version ${submission.ruleTemplateVersionId}`}>Template snapshot</span>
          )}
          <span className={statusClass(submission.status)}>{submission.status}</span>
        </div>
      </header>
      <section className="ppap-detail-meta" aria-label="Submission metadata">
        <div><span>Submission ID</span><strong>{submission.id}</strong></div>
        <div><span>Version</span><strong>{submission.version}</strong></div>
        <div><span>Available credits</span><strong>{balanceQuery.data?.availableCredits ?? 0}</strong></div>
      </section>

      <section className="ppap-panel ppap-document-panel" aria-labelledby="document-checklist-title">
        <div className="ppap-card-heading"><div><p className="ppap-section-kicker"><CheckCircle2 />Package contents</p><h2 id="document-checklist-title">Document checklist</h2></div><span className="ppap-count">{documents.length} slots</span></div>
        {submission.status === "Draft" && availableCodes.length > 0 && (
          <form className="ppap-add-document" onSubmit={(event) => {
            event.preventDefault();
            const documentCode = new FormData(event.currentTarget).get("documentCode")?.toString();
            if (documentCode) addMutation.mutate({ id: submissionId, documentCode });
          }}>
            <label htmlFor="ppap-document-code">Add a document slot</label>
            <select id="ppap-document-code" name="documentCode" defaultValue={availableCodes[0][0]}>{availableCodes.map(([code, label]) => <option key={code} value={code}>{code} — {label}</option>)}</select>
            <Button type="submit" variant="outline" disabled={addMutation.isPending}><FilePlus2 />{addMutation.isPending ? "Adding…" : "Add slot"}</Button>
          </form>
        )}
        {documents.length === 0 ? <div className="ppap-empty"><FilePlus2 /><h3>No document slots yet</h3><p>Add a slot from the catalogue to begin your package.</p></div> : (
          <ul className="ppap-document-list">
            {documents.map((document) => <DocumentSlot key={document.id} submissionId={submissionId} expectedVersion={submission.version} document={document} revision={revisionsByDocument.get(document.id) ?? null} editable={submission.status === "Draft"} onOpen={() => setWorkspaceDocumentId(document.id)} onFinalized={uploadFinalized} onNotApplicable={(reason) => notApplicableMutation.mutate({ id: submissionId, documentId: document.id, reason })} markingNotApplicable={notApplicableMutation.isPending && notApplicableMutation.variables?.documentId === document.id} onDeleteRevision={(revisionId) => deleteRevisionMutation.mutate({ submissionId, documentId: document.id, revisionId, expectedVersion: submission.version })} deletingRevision={deleteRevisionMutation.isPending && deleteRevisionMutation.variables?.documentId === document.id} />)}
          </ul>
        )}
      </section>

      <section className="ppap-submit-card" aria-labelledby="submit-title">
        <div><p className="ppap-section-kicker"><Send />Final step</p><h2 id="submit-title">Submit for processing</h2><p>Submitting reserves 10 credits and locks this draft version.</p><div id="ppap-submit-blockers" role="status" aria-live="polite" aria-atomic="true">{blockers.length > 0 && <ul>{blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul>}</div></div>
        <Button className="ppap-primary-action" aria-describedby={blockers.length > 0 ? "ppap-submit-blockers" : undefined} disabled={!canSubmit || submitMutation.isPending} onClick={() => submitMutation.mutate({ id: submission.id, expectedVersion: submission.version })}>{submitMutation.isPending ? "Submitting…" : isUpdating ? "Updating…" : "Submit PPAP"}</Button>
      </section>
      {workspaceDocumentId && workspaceQuery.data && <DocumentWorkspaceDialog workspace={workspaceQuery.data} selectedDocumentId={workspaceDocumentId} onSelect={setWorkspaceDocumentId} onClose={() => setWorkspaceDocumentId(null)} />}
    </div>
  );
}

function DocumentSlot({ submissionId, expectedVersion, document, revision, editable, onOpen, onFinalized, onNotApplicable, markingNotApplicable, onDeleteRevision, deletingRevision }: { submissionId: string; expectedVersion: number; document: SubmissionDocumentDto; revision: DocumentRevisionDto | null; editable: boolean; onOpen: () => void; onFinalized: () => void; onNotApplicable: (reason: string) => void; markingNotApplicable: boolean; onDeleteRevision: (revisionId: string) => void; deletingRevision: boolean }) {
  const [reason, setReason] = useState("");
  const [pendingUpload, setPendingUpload] = useState<{ revisionId: string; file: File } | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const label = PPAP_DOCUMENT_CATALOGUE.find(([code]) => code === document.documentCode)?.[1] ?? document.documentCode;

  const chooseFile = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    try {
      const revisionId = await reserveDocumentRevision({
        submissionId,
        documentId: document.id,
        expectedVersion,
        fileName: file.name,
        contentType: file.type || "application/octet-stream",
        sizeBytes: file.size,
      });
      setPendingUpload({ revisionId, file });
    } catch (error) {
      toast.error("Unable to reserve document revision", { description: describe(error) });
    }
  };

  return (
    <li className="ppap-document-row">
      <div className="ppap-document-code">{document.documentCode}</div>
      <div className="ppap-document-info"><strong>{label}</strong><span className="ppap-requirement-badge" title={document.disposition === "Submit" ? "Submit to customer" : document.disposition === "Retain" ? "Retain at supplier; upload does not block submission" : "Available for customer review"}>{document.disposition.slice(0, 1)} — {document.disposition}</span><span className="ppap-requirement-badge">{document.isRequired ? "Upload required" : "Retained"}</span><span className={statusClass(document.status)}>{document.status}</span>{revision && <span className={statusClass(revision.processingStatus)}>{`Revision ${revision.revisionNumber} · ${revision.processingStatus}`}</span>}{document.notApplicableReason && <p>Reason: {document.notApplicableReason}</p>}{pendingUpload && <RevisionUploader revisionId={pendingUpload.revisionId} file={pendingUpload.file} onFinalized={() => { setPendingUpload(null); onFinalized(); }} />}</div>
      {editable && document.status === "PendingUpload" && <div className="ppap-document-actions"><input ref={fileInputRef} className="sr-only" type="file" accept={ALLOWED_EXTENSIONS.join(",")} onChange={chooseFile} /><Button type="button" variant="outline" disabled={pendingUpload !== null} onClick={() => fileInputRef.current?.click()}><Upload />{pendingUpload ? "Uploading…" : "Upload file"}</Button><div className="ppap-na-control"><Input aria-label={`N/A reason for ${document.documentCode}`} placeholder="N/A reason" value={reason} onChange={(event) => setReason(event.target.value)} /><Button type="button" variant="ghost" disabled={!reason.trim() || markingNotApplicable || pendingUpload !== null} onClick={() => onNotApplicable(reason.trim())}>{markingNotApplicable ? "Saving…" : "Mark N/A"}</Button></div></div>}
      {revision && <div className="ppap-document-actions"><Button type="button" variant="outline" onClick={onOpen}>Open workspace</Button>{editable && <Button type="button" variant="ghost" aria-label={`Delete ${document.documentCode} file`} disabled={deletingRevision} onClick={() => { if (window.confirm(`Delete revision ${revision.revisionNumber} of ${document.documentCode}? Its findings will be cleared and you can upload a replacement.`)) onDeleteRevision(revision.id); }}><Trash2 />{deletingRevision ? "Deleting…" : "Delete file"}</Button>}</div>}
      {editable && document.status === "Quarantined" && <p className="ppap-document-warning">This file is quarantined. Upload a replacement revision after resolving the file issue.</p>}
    </li>
  );
}

function RevisionUploader({ revisionId, file, onFinalized }: { revisionId: string; file: File; onFinalized: () => void }) {
  const started = useRef(false);
  const upload = useFileUpload({ ownerType: "PpapDocumentRevision", ownerId: revisionId, visibility: Visibility.Private, category, allowedExtensions: ALLOWED_EXTENSIONS, maxBytes });

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    void upload.upload(file)
      .then(() => {
        toast.success("Upload finalized", { description: "Extraction has been queued for this document revision." });
        onFinalized();
      })
      .catch((error) => toast.error("Upload failed", { description: describe(error) }));
  }, [file, onFinalized, upload]);

  if (!upload.progress) return null;
  return <div className="ppap-upload-progress" role="progressbar" aria-label={`${upload.progress.fileName} upload progress`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={upload.progress.percent} aria-valuetext={upload.progress.status === "error" ? `Upload failed: ${upload.progress.error}` : `${upload.progress.status}, ${upload.progress.percent}%`}><div><span style={{ width: `${upload.progress.percent}%` }} /></div><small aria-hidden="true">{upload.progress.status === "error" ? upload.progress.error : `${upload.progress.fileName}: ${upload.progress.status} (${upload.progress.percent}%)`}</small></div>;
}
