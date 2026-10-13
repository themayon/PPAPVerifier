import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, ClipboardCheck, FileCheck2, Fingerprint, RefreshCw, ShieldAlert } from "lucide-react";
import { Link, useParams } from "react-router-dom";
import {
  getPpapReport,
  type PpapFindingDto,
  type PpapFindingOutcome,
  type PpapFindingWaiverDto,
} from "@/api/ppap";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { describe } from "@/lib/list-helpers";
import "@/styles/ppap.css";

function outcomeLabel(outcome: PpapFindingOutcome) {
  return outcome === "NotEvaluated" ? "Not evaluated" : outcome;
}

function findingMessage(finding: PpapFindingDto) {
  return finding.message?.trim() || finding.reason?.trim() || "No message or reason was provided.";
}

function formatTrace(traceJson: string) {
  try {
    return JSON.stringify(JSON.parse(traceJson), null, 2);
  } catch {
    return "Trace unavailable.";
  }
}

function compactId(id: string | null) {
  if (!id) return "Not linked";
  return id.length > 13 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id;
}

function AccessibleId({ label, id }: { label: string; id: string | null }) {
  const value = id ?? "Not linked";
  return <dd title={id ?? undefined} aria-label={`${label}: ${value}`}>{compactId(id)}</dd>;
}

function ReportTime({ value }: { value: string | null }) {
  if (!value) return <dd>Not completed</dd>;
  return <dd><time dateTime={value}>{new Date(value).toLocaleString()}</time></dd>;
}

function WaiverHistory({ waivers, findingLabel }: { waivers: PpapFindingWaiverDto[] | null | undefined; findingLabel: string }) {
  const history = waivers ?? [];
  if (history.length === 0) return <p className="ppap-waiver-empty">No waiver history.</p>;

  return (
    <details className="ppap-waiver-history">
      <summary>{`Waiver history (${history.length}) for ${findingLabel}`}</summary>
      <div className="ppap-waiver-list">
        {history.map((waiver) => (
          <article className="ppap-waiver" key={waiver.id}>
            <header>
              <strong>{waiver.isActive ? "Active waiver" : "Revoked waiver"}</strong>
              <span>{waiver.isActive ? "Active" : "Revoked"}</span>
            </header>
            <p><strong>Approval reason:</strong> {waiver.reason}</p>
            <dl className="ppap-waiver-metadata">
              <div><dt>Waiver</dt><AccessibleId label="Waiver" id={waiver.id} /></div>
              <div><dt>Approved by</dt><AccessibleId label="Waiver approver" id={waiver.approvedByUserId} /></div>
              <div><dt>Approved</dt><ReportTime value={waiver.approvedAtUtc} /></div>
            </dl>
            {waiver.revocation && (
              <div className="ppap-waiver-revocation">
                <p><strong>Revocation reason:</strong> {waiver.revocation.reason}</p>
                <dl className="ppap-waiver-metadata">
                  <div><dt>Revocation</dt><AccessibleId label="Waiver revocation" id={waiver.revocation.id} /></div>
                  <div><dt>Revoked by</dt><AccessibleId label="Waiver revoker" id={waiver.revocation.revokedByUserId} /></div>
                  <div><dt>Revoked</dt><ReportTime value={waiver.revocation.revokedAtUtc} /></div>
                </dl>
              </div>
            )}
          </article>
        ))}
      </div>
    </details>
  );
}

export function PpapReportPage() {
  const { reportId = "" } = useParams();
  const reportQuery = useQuery({
    queryKey: ["ppap", "reports", reportId],
    queryFn: () => getPpapReport(reportId),
    enabled: Boolean(reportId),
  });

  if (reportQuery.isLoading) {
    return <div className="ppap-page ppap-report-loading" aria-label="Loading PPAP report"><Skeleton className="h-40 w-full" /><Skeleton className="h-64 w-full" /></div>;
  }

  if (reportQuery.isError) {
    return <div className="ppap-page"><section className="ppap-error" role="alert"><div><strong>Report could not be loaded.</strong><span>{describe(reportQuery.error)}</span></div><Button variant="outline" onClick={() => void reportQuery.refetch()}><RefreshCw />Try again</Button></section><Link to="/ppap">Back to PPAP home</Link></div>;
  }

  const report = reportQuery.data;
  if (!report) return <div className="ppap-page"><section className="ppap-empty"><h1>Report not found</h1><Link to="/ppap">Back to PPAP home</Link></section></div>;

  const automatedFindings = report.findings ?? [];
  const manualFindings = report.manualFindings ?? [];
  const automatedFindingCount = report.automatedFindingCount ?? automatedFindings.length;
  const manualFindingCount = report.manualFindingCount ?? manualFindings.length;
  const findingCount = report.findingCount ?? automatedFindingCount + manualFindingCount;
  const waivedFindingCount = report.waivedFindingCount ?? 0;
  const openFindingCount = report.openFindingCount ?? Math.max(findingCount - waivedFindingCount, 0);
  const documentCounts = [
    ["Available", report.availableDocumentCount],
    ["Not applicable", report.notApplicableDocumentCount],
    ["Quarantined", report.quarantinedDocumentCount],
  ] as const;
  const findingCounts = [
    ["Total", findingCount],
    ["Automated", automatedFindingCount],
    ["Manual", manualFindingCount],
    ["Active waived", waivedFindingCount],
    ["Open", openFindingCount],
    ["Automated failed", report.failedFindingCount],
    ["Automated not evaluated", report.notEvaluatedFindingCount],
    ["Automated error", report.errorFindingCount],
  ] as const;

  return (
    <div className="ppap-page ppap-report-page">
      <Link className="ppap-back-link" to="/ppap"><ArrowLeft />PPAP home</Link>
      <header className="ppap-report-header">
        <div className="ppap-report-mark" aria-hidden="true"><ClipboardCheck /></div>
        <div><p className="ppap-eyebrow">Phase 1 report</p><h1>{report.partName}</h1><p>{report.partNumber} · Revision {report.drawingRevision}</p></div>
        <time dateTime={report.generatedAtUtc}>Generated {new Date(report.generatedAtUtc).toLocaleString()}</time>
      </header>

      <dl className="ppap-report-metadata">
        <div><dt>Submission version</dt><dd>{report.submissionVersion}</dd></div>
        <div><dt>Report ID</dt><dd>{report.id}</dd></div>
        <div><dt>Job ID</dt><dd>{report.jobId}</dd></div>
      </dl>

      <div className="ppap-report-grid">
        <section className="ppap-report-section" aria-labelledby="document-completeness-title">
          <div className="ppap-report-section-heading"><FileCheck2 aria-hidden="true" /><div><p className="ppap-section-kicker">Package inventory</p><h2 id="document-completeness-title">Document completeness</h2></div></div>
          <dl className="ppap-report-counts">{documentCounts.map(([label, count]) => <div key={label}><dt>{label}</dt><dd>{count}</dd></div>)}</dl>
        </section>

        <section className="ppap-report-section ppap-report-section--findings" aria-labelledby="finding-summary-title">
          <div className="ppap-report-section-heading"><ShieldAlert aria-hidden="true" /><div><p className="ppap-section-kicker">Review findings</p><h2 id="finding-summary-title">Finding summary</h2></div></div>
          <dl className="ppap-report-counts ppap-report-counts--findings">{findingCounts.map(([label, count]) => <div key={label}><dt>{label}</dt><dd>{count ?? 0}</dd></div>)}</dl>
        </section>
      </div>

      {report.reproducibilityManifest ? (
        <section className="ppap-reproducibility" aria-labelledby="reproducibility-title">
          <details>
            <summary>
              <Fingerprint aria-hidden="true" />
              <span><span className="ppap-section-kicker">Audit snapshot</span><span className="ppap-reproducibility-title" id="reproducibility-title" role="heading" aria-level={2}>Reproducibility</span></span>
              <span className="ppap-reproducibility-action"><span className="ppap-reproducibility-show">Show details</span><span className="ppap-reproducibility-hide">Hide details</span></span>
            </summary>
            <div className="ppap-reproducibility-content">
              <dl className="ppap-reproducibility-overview">
                <div><dt>Manifest schema</dt><dd>{report.reproducibilityManifest.schemaVersion}</dd></div>
                <div><dt>Job</dt><AccessibleId label="Manifest job" id={report.reproducibilityManifest.jobId} /></div>
                <div><dt>Submission</dt><AccessibleId label="Manifest submission" id={report.reproducibilityManifest.submissionId} /></div>
                <div><dt>Submission version</dt><dd>{report.reproducibilityManifest.submissionVersion}</dd></div>
                <div><dt>Template version</dt><AccessibleId label="Manifest template version" id={report.reproducibilityManifest.templateVersionId} /></div>
                <div><dt>Evaluation run</dt><AccessibleId label="Manifest evaluation run" id={report.reproducibilityManifest.evaluation.runId} /></div>
                <div><dt>Evaluation version</dt><dd>{report.reproducibilityManifest.evaluation.version}</dd></div>
                <div><dt>Input schema</dt><dd>{report.reproducibilityManifest.evaluation.inputSchemaVersion}</dd></div>
                <div><dt>Evaluation status</dt><dd>{report.reproducibilityManifest.evaluation.status}</dd></div>
                <div><dt>Evaluation created</dt><ReportTime value={report.reproducibilityManifest.evaluation.createdAtUtc} /></div>
                <div><dt>Evaluation completed</dt><ReportTime value={report.reproducibilityManifest.evaluation.completedAtUtc} /></div>
                <div><dt>Rule results</dt><dd>{report.reproducibilityManifest.evaluation.results.length}</dd></div>
              </dl>

              <div className="ppap-manifest-documents">
                <div className="ppap-manifest-documents-heading"><h3>Document inventory</h3><strong>{report.reproducibilityManifest.documents.length} documents</strong></div>
                <div className="ppap-manifest-document-list">
                  {report.reproducibilityManifest.documents.map((document) => (
                    <article className="ppap-manifest-document" key={document.id}>
                      <header><div><span>{document.documentCode}</span><h4>{document.disposition} · {document.status}</h4></div><span className="ppap-manifest-extraction-status">{document.extraction ? "Extraction recorded" : "No extraction"}</span></header>
                      <dl className="ppap-manifest-document-metadata">
                        <div><dt>Document</dt><AccessibleId label={`${document.documentCode} document`} id={document.id} /></div>
                        <div><dt>Source file</dt><AccessibleId label={`${document.documentCode} source file`} id={document.sourceFileId} /></div>
                        <div><dt>Disposition</dt><dd>{document.disposition}</dd></div>
                        <div><dt>Status</dt><dd>{document.status}</dd></div>
                      </dl>
                      {document.extraction && (
                        <dl className="ppap-manifest-extraction">
                          <div><dt>Provider</dt><dd>{document.extraction.provider}</dd></div>
                          <div><dt>Model</dt><dd>{document.extraction.model}</dd></div>
                          <div><dt>Extractor version</dt><dd>{document.extraction.extractorVersion}</dd></div>
                          <div><dt>Canonical schema</dt><dd>{document.extraction.canonicalSchemaVersion}</dd></div>
                          <div><dt>Source SHA-256</dt><AccessibleId label={`${document.documentCode} source SHA-256`} id={document.extraction.sourceSha256} /></div>
                          <div><dt>Extraction attempt</dt><AccessibleId label={`${document.documentCode} extraction attempt`} id={document.extraction.attemptId} /></div>
                          <div><dt>Canonical document</dt><AccessibleId label={`${document.documentCode} canonical document`} id={document.extraction.canonicalDocumentId} /></div>
                          <div><dt>Raw response artifact</dt><AccessibleId label={`${document.documentCode} raw response artifact`} id={document.extraction.rawResponseArtifactId} /></div>
                          <div><dt>Generated workbook artifact</dt><AccessibleId label={`${document.documentCode} generated workbook artifact`} id={document.extraction.generatedExcelArtifactId} /></div>
                          <div><dt>Extraction created</dt><ReportTime value={document.extraction.createdAtUtc} /></div>
                          <div><dt>Extraction completed</dt><ReportTime value={document.extraction.completedAtUtc} /></div>
                        </dl>
                      )}
                    </article>
                  ))}
                </div>
              </div>
            </div>
          </details>
        </section>
      ) : (
        <section className="ppap-reproducibility ppap-reproducibility--unavailable" aria-labelledby="reproducibility-title">
          <Fingerprint aria-hidden="true" />
          <div><h2 id="reproducibility-title">Reproducibility</h2><p>Reproducibility details are unavailable for this historical report.</p></div>
        </section>
      )}

      <section className="ppap-findings" aria-labelledby="automated-findings-title">
        <div className="ppap-findings-heading"><div><p className="ppap-section-kicker">Rule evaluation</p><h2 id="automated-findings-title">Automated findings</h2></div><strong>{automatedFindingCount} total</strong></div>
        {automatedFindings.length === 0 && <p className="ppap-report-fixture-note">No automated findings in this report.</p>}
        <div className="ppap-finding-list">
          {automatedFindings.map((finding) => (
            <article className="ppap-finding" key={finding.id}>
              <header className="ppap-finding-heading">
                <div><span className="ppap-finding-code">{finding.stableRuleCode}</span><h3>{finding.ruleName}</h3></div>
                <span className="ppap-finding-outcome" data-outcome={finding.outcome}>{outcomeLabel(finding.outcome)}</span>
              </header>
              <p className="ppap-finding-document">{finding.documentCode || "Cross-document"}</p>
              <p className="ppap-finding-message">{findingMessage(finding)}</p>
              <WaiverHistory waivers={finding.waivers} findingLabel={finding.stableRuleCode} />
              <details className="ppap-finding-trace">
                <summary>Evidence metadata</summary>
                <dl className="ppap-finding-metadata">
                  <div><dt>Finding</dt><AccessibleId label="Finding" id={finding.id} /></div>
                  <div><dt>Evaluation run</dt><AccessibleId label="Evaluation run" id={finding.ruleEvaluationRunId} /></div>
                  <div><dt>Evaluation result</dt><AccessibleId label="Evaluation result" id={finding.ruleEvaluationResultId} /></div>
                  <div><dt>Template version</dt><AccessibleId label="Template version" id={finding.templateVersionId} /></div>
                  <div><dt>Rule definition</dt><AccessibleId label="Rule definition" id={finding.ruleDefinitionId} /></div>
                  <div><dt>Rule version</dt><AccessibleId label="Rule version" id={finding.ruleVersionId} /></div>
                  <div><dt>Submission document</dt><AccessibleId label="Submission document" id={finding.submissionDocumentId} /></div>
                  <div><dt>Canonical document</dt><AccessibleId label="Canonical document" id={finding.canonicalDocumentId} /></div>
                  <div><dt>Extraction attempt</dt><AccessibleId label="Extraction attempt" id={finding.extractionAttemptId} /></div>
                  <div><dt>Evaluated</dt><dd><time dateTime={finding.evaluatedAtUtc}>{new Date(finding.evaluatedAtUtc).toLocaleString()}</time></dd></div>
                </dl>
                <textarea className="ppap-finding-trace-json" aria-label={`Evaluation trace for ${finding.stableRuleCode}`} value={formatTrace(finding.traceJson)} readOnly spellCheck={false} />
              </details>
            </article>
          ))}
        </div>
      </section>

      <section className="ppap-findings ppap-manual-findings" aria-labelledby="manual-findings-title">
        <div className="ppap-findings-heading"><div><p className="ppap-section-kicker">Supplier review</p><h2 id="manual-findings-title">Manual findings</h2></div><strong>{manualFindingCount} total</strong></div>
        {manualFindings.length === 0 && <p className="ppap-report-fixture-note">No manual findings in this report.</p>}
        <div className="ppap-finding-list">
          {manualFindings.map((finding) => (
            <article className="ppap-finding ppap-manual-finding" key={finding.id}>
              <header className="ppap-finding-heading">
                <div><span className="ppap-finding-code">Manual finding</span><h3>{finding.documentCode ? `${finding.documentCode} document` : "Cross-document"}</h3></div>
                <span className="ppap-finding-outcome">Manual</span>
              </header>
              <p className="ppap-finding-message">{finding.message}</p>
              <div className="ppap-manual-evidence"><strong>Evidence note</strong><p>{finding.evidenceNote?.trim() || "No evidence note provided."}</p></div>
              <p className="ppap-finding-byline">Created by <span title={finding.createdByUserId}>{compactId(finding.createdByUserId)}</span> on <time dateTime={finding.createdAtUtc}>{new Date(finding.createdAtUtc).toLocaleString()}</time></p>
              <WaiverHistory waivers={finding.waivers} findingLabel="manual finding" />
              <details className="ppap-finding-trace">
                <summary>Finding metadata</summary>
                <dl className="ppap-finding-metadata">
                  <div><dt>Finding</dt><AccessibleId label="Manual finding" id={finding.id} /></div>
                  <div><dt>Created by</dt><AccessibleId label="Manual finding creator" id={finding.createdByUserId} /></div>
                  <div><dt>Submission document</dt><AccessibleId label="Manual finding submission document" id={finding.submissionDocumentId} /></div>
                  <div><dt>Canonical document</dt><AccessibleId label="Manual finding canonical document" id={finding.canonicalDocumentId} /></div>
                  <div><dt>Extraction attempt</dt><AccessibleId label="Manual finding extraction attempt" id={finding.extractionAttemptId} /></div>
                  <div><dt>Created</dt><dd><time dateTime={finding.createdAtUtc}>{new Date(finding.createdAtUtc).toLocaleString()}</time></dd></div>
                </dl>
              </details>
            </article>
          ))}
        </div>
      </section>

      <nav className="ppap-processing-links" aria-label="Report links"><Button asChild variant="outline"><Link to="/ppap">PPAP home</Link></Button><Button asChild><Link to={`/ppap/submissions/${report.submissionId}`}>View submission</Link></Button></nav>
    </div>
  );
}
