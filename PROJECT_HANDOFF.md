# PPAPVerifier Project Handoff

**Handoff date:** 2026-10-30  
**Primary repository:** `D:\Muthu\PPAPVerifier\PPAPVerifier`  
**Implementation plan:** `C:\Users\intel\.devin\plans\plan-73eae422007b537e.md`  
**Important state:** The primary repository has a large, intentionally dirty working tree containing both pre-existing user work and the PPAP workspace work from the latest sessions. Nothing has been committed. Do not reset, clean, stash, delete, or overwrite these changes.

---

## 1. Project Overview

### Purpose and objectives

PPAPVerifier is an enterprise SaaS application for managing Production Part Approval Process (PPAP) submissions. The current objective is to replace a legacy MVC PPAP document-verification experience with a modern, role-aware React workspace while retaining the mature extraction and validation behavior from existing Omnex systems.

The approved target flow is:

1. A supplier creates a PPAP submission for a part.
2. The supplier uploads a controlled primary file for each required PPAP document slot.
3. Upload finalization creates an immutable document revision and queues extraction immediately while the submission is still a draft.
4. Non-DWG documents are processed by ASPECT; DWG documents are processed by Diagram Extractor.
5. Generated PDFs/XLSX files and canonical JSON are stored as private artifacts.
6. Simple rules are evaluated in the existing Rules module.
7. Complex document/cross-document rules are intended to run in a separate .NET validation worker.
8. Findings are stored as mutable working findings before final submission and later snapshotted into an immutable report.
9. Suppliers can request finding waivers; reviewers can approve or reject them.
10. The document workspace displays the source PDF, generated spreadsheet, correlated findings, and previous/next document navigation without returning to the submission overview.

### Main features in the broader working tree

The repository already contains or is actively developing:

- Multitenant authentication/authorization.
- PPAP suppliers and supplier profiles.
- Part directory and PPAP submission setup.
- Rule columns, document settings, templates, and rule editing.
- Presigned file upload and private artifact access.
- ASPECT and Diagram Extractor integration.
- Package-level PPAP processing, rule evaluation, reports, findings, waivers, and decisions.
- A React dashboard for suppliers and tenant reviewers.
- A separate admin application.

Several supplier/parts/rules files and migrations were already modified/untracked before the latest PPAP workspace implementation. Treat them as user-owned concurrent work.

### Target users and use cases

- **Suppliers:** create submissions, upload/revise controlled documents, monitor processing, review findings, request waivers, and submit the package.
- **OEM quality engineers/reviewers:** inspect source evidence and extracted workbooks, review findings, approve/reject waiver requests, create manual findings, and decide submissions.
- **Tenant operators/admins:** configure suppliers, parts, document requirements, rules, templates, permissions, processing providers, and operational health.

### UX references

The design work referenced these files:

- `Screens/3_3_ppap_submission_grid.png`
- `Screens/3_4supplier_project_workspace.png`
- `Screens/MVC_PPAPSubmission.png`
- Legacy application: `D:\TFS\EwIMS\RPA\PPAPVerifier_DynamicForm_Downgrade\OBot`

The temporary interactive concept remains at `clients/dashboard/src/pages/ppap/submission-concept.tsx` and is routed publicly at `/ppap-submission-concept`. It should only be removed after the production workspace reaches accepted parity.

---

## 2. Technology Stack

### Primary application

- **Backend:** C# latest, .NET 10, ASP.NET Core Minimal APIs.
- **Architecture:** modular monolith, Vertical Slice Architecture, CQRS with `Mediator` source generation (not MediatR).
- **Database:** PostgreSQL.
- **ORM:** EF Core 10 with Npgsql.
- **Multitenancy:** Finbuckle; tenant filters are applied by `BaseDbContext`.
- **Authentication:** JWT Bearer + ASP.NET Identity.
- **Background jobs:** Hangfire through `IJobService`.
- **Messaging:** transactional Outbox/Inbox; in-memory or RabbitMQ event bus.
- **Caching:** Redis/Valkey.
- **Storage:** local or S3-compatible storage/MinIO with presigned upload/download URLs.
- **Realtime:** SignalR and dashboard SSE infrastructure.
- **API documentation:** OpenAPI/Scalar.
- **Logging/telemetry:** structured logging, Serilog, OpenTelemetry.
- **Orchestration:** .NET Aspire AppHost.

### Frontend

- React 19.
- Vite 7.
- TypeScript 5.x.
- React Router 7.
- TanStack Query 5.
- Tailwind CSS 4.
- Radix UI/shadcn-style components.
- `react-pdf`/PDF.js for source PDF viewing.
- Local packaged `omnex-spreadjs` for XLSX viewing.
- Playwright for route-mocked E2E tests.

### External processing solutions

#### ASPECT

Path: `D:\Muthu\PPAPVerifier\ASPECT_VALIDATION_LLM`

- Python/FastAPI.
- LLM-backed PDF extraction.
- Document-specific XLSX generators.
- SQLite task metadata for the newly added async API.
- Headless LibreOffice conversion for DOCX/XLSX/PPTX.
- Existing synchronous `/extract_pdf` remains for compatibility.

#### Diagram Extractor

Path: `D:\Muthu\PPAPVerifier\Diagram_Extractor`

- Python/FastAPI.
- Celery + RabbitMQ.
- PostgreSQL/SQL task records depending on configuration.
- Computer vision/YOLO + LLM extraction.
- Asynchronous submit/status/download contract.

#### OmnexSpreadJS

Path: `D:\Omnex\OmnexSpreadJS`

- TypeScript spreadsheet engine/viewer.
- ExcelJS for XLSX import.
- Framework-independent DOM API.
- License validation and watermark support.
- Built output is packaged into `clients/dashboard/omnex-spreadjs-0.1.0.tgz`.

### Development/testing tools

- xUnit, Shouldly, NSubstitute, AutoFixture.
- NetArchTest architecture tests.
- Testcontainers for PostgreSQL/Redis/MinIO integration tests.
- ESLint and Playwright.
- Python `unittest` for ASPECT additions.
- Jest/ts-jest/jsdom for OmnexSpreadJS.

---

## 3. Project Architecture

### Overall architecture

```text
Dashboard React app
    |
    v
DES.Starter.Api
    |
    +-- PPAP module --------------------------+
    |   submissions, revisions, findings,     |
    |   waiver requests, reports, readiness   |
    |                                         |
    +-- Files module                          |
    |   presigned URLs/private artifacts      |
    |                                         |
    +-- DocumentIntelligence module ----------+
    |   extraction attempts/provider routing
    |      |                    |
    |      | non-DWG            | DWG
    |      v                    v
    |    ASPECT          Diagram Extractor
    |
    +-- Rules module
    |   rule/template governance and simple evaluation
    |
    +-- RabbitMQ/Outbox
            |
            v
    DES.Ppap.ValidationWorker
    complex executor registry
            |
            v
    RabbitMQ result -> PPAP result listener -> working findings
```

### Important repository paths

```text
clients/dashboard/                         Tenant-facing React application
clients/admin/                             Operator/admin React application
src/Modules/Ppap/                          PPAP domain/runtime/contracts
src/Modules/DocumentIntelligence/          Extraction attempts and providers
src/Modules/Files/                         File metadata and presigned storage
src/Modules/Rules/                         Rule/template governance and evaluation
src/Workers/DES.Ppap.ValidationWorker/     New complex validation worker
src/Host/DES.Starter.Api/                  API composition root/configuration
src/Host/DES.Starter.AppHost/              Aspire orchestration
src/Host/DES.Starter.DbMigrator/           Migration/seed runner
src/Host/DES.Starter.Migrations.PostgreSQL Central EF migrations
src/Tests/                                 Backend tests
Screens/                                   UX reference images
```

### PPAP domain additions

#### `PpapDocumentRevision`

File: `src/Modules/Ppap/Modules.Ppap/Domain/PpapDocumentRevision.cs`

Represents an immutable controlled source revision for a PPAP document slot. Important fields:

- `SubmissionDocumentId`
- `RevisionNumber`
- `OwnerUserId`
- source file metadata and artifact ID
- `IsCurrent`
- upload and processing status
- extraction request/attempt IDs
- canonical/raw/XLSX/preview artifact IDs
- provider/extractor metadata
- failure/timestamps/version

Replacement is blocked while the current revision is queued/converting/extracting/validating. A replacement supersedes a terminal revision.

#### `PpapWorkingFinding`

File: `src/Modules/Ppap/Modules.Ppap/Domain/PpapWorkingFinding.cs`

Stores current pre-report findings and evidence JSON. It is keyed by validation run/rule/input fingerprint and may be document-scoped or cross-document. Findings can be superseded without deleting audit history.

#### `PpapWaiverRequest`

File: `src/Modules/Ppap/Modules.Ppap/Domain/PpapWaiverRequest.cs`

Implements supplier request and reviewer decision states:

- `Pending`
- `Approved`
- `Rejected`
- `Withdrawn`
- `Superseded`

A waiver request is tied to a working-finding fingerprint so replacing input evidence can invalidate it.

### Main relationships

- `PpapSubmission` has many `PpapSubmissionDocument` slots.
- `PpapSubmissionDocument` has many `PpapDocumentRevision` rows; one current revision is enforced by a filtered unique index.
- `PpapWorkingFinding` references a submission, rule version, validation run, and optionally a document/revision.
- `PpapWaiverRequest` references a working finding and submission.
- Final `PpapFinding`, `PpapFindingWaiver`, and `PpapReport` remain the immutable package/report model.
- File artifacts remain in the Files module; PPAP stores only file asset IDs.
- Canonical documents remain in Document Intelligence; PPAP stores canonical IDs and provider artifacts.

The new entities are configured in:

- `src/Modules/Ppap/Modules.Ppap/Data/Configurations/PpapDocumentRevisionConfiguration.cs`
- `src/Modules/Ppap/Modules.Ppap/Data/Configurations/PpapWorkingFindingConfiguration.cs`
- `src/Modules/Ppap/Modules.Ppap/Data/Configurations/PpapWaiverRequestConfiguration.cs`

### Migrations added

- `src/Host/DES.Starter.Migrations.PostgreSQL/Ppap/20261022170038_AddPpapDocumentRevisions.cs`
  - Adds revision storage.
  - Includes a compatibility backfill from existing PPAP document/file rows.
- `src/Host/DES.Starter.Migrations.PostgreSQL/Ppap/20261023110303_AddWorkingFindingsAndWaiverRequests.cs`
  - Adds mutable working findings and waiver requests.

These migrations were generated but **have not been confirmed applied to a live/local database in this session**.

### Upload-first extraction path

1. Frontend calls `POST /api/v1/ppap/submissions/{submissionId}/documents/{documentId}/revisions`.
2. PPAP reserves a revision and increments the submission version.
3. Frontend uses the Files presigned upload protocol with owner type `PpapDocumentRevision`.
4. `PpapFileFinalizedHandler` applies the file metadata and writes `PpapDocumentRevisionExtractionRequestedIntegrationEvent` to the Outbox.
5. Document Intelligence consumes the event and creates an extraction attempt.
6. Provider routing sends DWG to Diagram Extractor and other document codes to ASPECT.
7. Terminal extraction events update both compatibility document fields and the current revision.
8. Draft extraction can complete without a package processing job.

Key files:

- `src/Modules/Ppap/Modules.Ppap/Features/v1/Submissions/ReserveDocumentRevision/`
- `src/Modules/Ppap/Modules.Ppap/IntegrationEventHandlers/FileFinalizedIntegrationEventHandler.cs`
- `src/Modules/DocumentIntelligence/Modules.DocumentIntelligence/IntegrationEventHandlers/PpapDocumentRevisionExtractionRequestedIntegrationEventHandler.cs`
- `src/Modules/Ppap/Modules.Ppap/IntegrationEventHandlers/DocumentExtractionTerminalEventHandlers.cs`

### Workspace and waiver endpoints

All paths are under `/api/v1/ppap`:

- `POST /submissions/{submissionId}/documents/{documentId}/revisions`
- `GET /submissions/{submissionId}/workspace`
- `GET /viewer-license`
- `POST /submissions/{submissionId}/findings/{findingId}/waiver-requests`
- `GET /operations/submissions/{submissionId}/waiver-requests`
- `POST /operations/waiver-requests/{requestId}/decision`

The workspace response includes ordered slots, current/history revisions, findings, and waiver requests. It does not return storage keys; viewers request short-lived URLs from Files.

### Frontend workspace components

- `clients/dashboard/src/pages/ppap/document-workspace.tsx`
  - full-screen document workspace
  - document chip navigation and previous/next
  - PDF/XLSX tabs
  - findings panel
  - supplier waiver dialog
- `clients/dashboard/src/pages/ppap/pdf-viewer.tsx`
  - lazy PDF.js viewer
  - page navigation, zoom, open/download, evidence boxes
- `clients/dashboard/src/pages/ppap/omnex-spread-viewer.tsx`
  - fetches protected viewer license and generated workbook
  - mounts/destroys OmnexSpread
  - selects/highlights sheet ranges
- `clients/dashboard/src/pages/ppap/waiver-review-panel.tsx`
  - reviewer pending request list and approve/reject dialogs
- `clients/dashboard/src/pages/ppap/submission.tsx`
  - revision upload/status polling and workspace entry

PDF and Omnex components are dynamically loaded so the normal submission chunk remains small. Current build output still reports large viewer chunks, but they are route/workspace lazy chunks rather than initial submission code.

### Validation worker architecture

Project: `src/Workers/DES.Ppap.ValidationWorker/`

- RabbitMQ consumer of `ComplexValidationRequestedIntegrationEvent`.
- Executor registry.
- One implemented executor: `json-path-compare:v1`.
- Publishes `ComplexValidationCompletedIntegrationEvent`.
- PPAP has a RabbitMQ result listener and result processor intended to persist/supersede working findings.

Critical limitation: no source currently publishes `ComplexValidationRequestedIntegrationEvent`. The worker therefore has no production input path yet.

Also, the worker is in `src/DES.Starter.slnx` but is **not registered in Aspire AppHost**.

---

## 4. Implementation Status

### Completed or substantially implemented

These items exist in source and had successful isolated verification before the latest validation-listener additions:

- PPAP document revision domain/configuration/migrations.
- Revision-based presigned upload flow.
- Upload-first extraction V2 event and Document Intelligence handler.
- Draft extraction terminal persistence.
- ASPECT async task client option in Document Intelligence.
- ASPECT async task API and Office conversion.
- Diagram Extractor security/correlation/webhook hardening.
- Working finding and waiver request domain/migrations.
- Supplier waiver request and reviewer decision endpoints.
- Submission workspace read endpoint.
- Protected OmnexSpread license endpoint/configuration.
- Framework-independent OmnexSpread XLSX viewer.
- Packaged OmnexSpread dashboard dependency.
- PDF.js and OmnexSpread React viewer components.
- Production supplier document workspace integration.
- Reviewer pending waiver panel.
- Validation worker project, RabbitMQ consumer, executor registry, and one generic executor.
- PPAP RabbitMQ result listener and result processor source files.

### Partially implemented

#### Complex validation pipeline

Implemented:

- request/completion contracts
- RabbitMQ worker request consumer
- `json-path-compare:v1` executor
- completion publisher
- PPAP completion queue listener
- working-finding result processor

Missing:

- no `ComplexValidationRequestedIntegrationEvent` publisher exists
- no rule-plan reader/dependency scheduler exists
- no migration of legacy DIMR/DWG, PFMEA/PFD, Control Plan/PFMEA, special-characteristic, MSA, or knowledge-store algorithms
- no cross-document readiness/fingerprint scheduling
- no final-report snapshot of approved working waivers
- no AppHost RabbitMQ/worker orchestration
- no RabbitMQ integration tests

#### Preview PDF correlation

- ASPECT async tasks create `preview.pdf`.
- `PpapDocumentRevision` has `PreviewPdfFileAssetId`.
- The current .NET extraction completion contract does not carry/store the ASPECT preview PDF artifact ID.
- PDF uploads can still be previewed using the original source file; Office uploads will not have an in-app PDF preview until this correlation is completed.

#### Processing progress

- Revision states exist.
- Upload finalization sets `Queued`; terminal extraction sets `Ready`/`Failed`.
- No current call marks a revision `Extracting`, `Converting`, or `Validating`, so UI state granularity is incomplete.

#### Reviewer document access

- `PpapDocumentRevisionFileAccessPolicy` is primarily owner-based.
- Reviewer waiver actions exist, but reviewer access to supplier source/generated artifacts must be reviewed and explicitly authorized before a reviewer workspace can show the same document viewers.

#### Final package reuse/snapshot

- Existing package processing remains.
- It has not been fully refactored to consume/snapshot only current ready revisions and approved working waivers.

#### ASPECT durability

- Async metadata is persisted in SQLite and interrupted tasks are marked failed.
- Execution currently uses FastAPI in-process background tasks, not Celery/RabbitMQ.
- It is documented as single-process only; multiple IIS workers are unsafe for this first slice.

### Not yet started or not completed

- Legacy complex validator characterization/migration.
- Complex validation request publishing and dependency planning.
- AppHost RabbitMQ resource and validation worker process registration.
- SignalR events for document/validation/waiver updates.
- Reviewer source PDF/XLSX workspace parity.
- Final submission authoritative validation barrier.
- Final report snapshot of working findings/waiver approvals.
- Production license secret provisioning.
- Documentation-site and changelog updates required by repository rules.
- Removal of the temporary `/ppap-submission-concept` route.
- Apply/test new migrations against PostgreSQL.

### Important files created or modified for this effort

Backend:

- `src/Modules/Ppap/Modules.Ppap/Domain/PpapDocumentRevision.cs`
- `src/Modules/Ppap/Modules.Ppap/Domain/PpapWorkingFinding.cs`
- `src/Modules/Ppap/Modules.Ppap/Domain/PpapWaiverRequest.cs`
- `src/Modules/Ppap/Modules.Ppap/Data/Configurations/PpapDocumentRevisionConfiguration.cs`
- `src/Modules/Ppap/Modules.Ppap/Data/Configurations/PpapWorkingFindingConfiguration.cs`
- `src/Modules/Ppap/Modules.Ppap/Data/Configurations/PpapWaiverRequestConfiguration.cs`
- `src/Modules/Ppap/Modules.Ppap/Features/v1/Submissions/ReserveDocumentRevision/`
- `src/Modules/Ppap/Modules.Ppap/Features/v1/Workspace/`
- `src/Modules/Ppap/Modules.Ppap/Processing/ComplexValidationOptions.cs`
- `src/Modules/Ppap/Modules.Ppap/Processing/ComplexValidationResultListener.cs`
- `src/Modules/Ppap/Modules.Ppap/Processing/ComplexValidationResultProcessor.cs`
- `src/Modules/Ppap/Modules.Ppap.Contracts/Events/ComplexValidationRequestedIntegrationEvent.cs`
- `src/Modules/Ppap/Modules.Ppap.Contracts/Events/ComplexValidationCompletedIntegrationEvent.cs`
- `src/Workers/DES.Ppap.ValidationWorker/`
- `src/Modules/DocumentIntelligence/Modules.DocumentIntelligence/Providers/AspectPdfExtractionProvider.cs`
- `src/Modules/DocumentIntelligence/Modules.DocumentIntelligence/Providers/AspectPdfExtractionOptions.cs`

Frontend:

- `clients/dashboard/src/pages/ppap/document-workspace.tsx`
- `clients/dashboard/src/pages/ppap/pdf-viewer.tsx`
- `clients/dashboard/src/pages/ppap/omnex-spread-viewer.tsx`
- `clients/dashboard/src/pages/ppap/waiver-review-panel.tsx`
- `clients/dashboard/src/pages/ppap/submission.tsx`
- `clients/dashboard/src/pages/ppap/review.tsx`
- `clients/dashboard/src/api/ppap.ts`
- `clients/dashboard/omnex-spreadjs-0.1.0.tgz`

External projects:

- `D:\Muthu\PPAPVerifier\ASPECT_VALIDATION_LLM\extraction_tasks.py`
- `D:\Muthu\PPAPVerifier\ASPECT_VALIDATION_LLM\document_conversion.py`
- `D:\Muthu\PPAPVerifier\ASPECT_VALIDATION_LLM\main_pdf_extractor.py`
- `D:\Muthu\PPAPVerifier\ASPECT_VALIDATION_LLM\tests\test_extraction_tasks.py`
- `D:\Muthu\PPAPVerifier\ASPECT_VALIDATION_LLM\tests\test_document_conversion.py`
- `D:\Muthu\PPAPVerifier\Diagram_Extractor\app\src\core\callback.py`
- `D:\Muthu\PPAPVerifier\Diagram_Extractor\app\src\core\models.py`
- `D:\Muthu\PPAPVerifier\Diagram_Extractor\app\src\tasks.py`
- `D:\Muthu\PPAPVerifier\Diagram_Extractor\main.py`
- `D:\Omnex\OmnexSpreadJS\src\viewer\SpreadsheetViewer.ts`
- `D:\Omnex\OmnexSpreadJS\src\api\OmnexSpread.ts`
- `D:\Omnex\OmnexSpreadJS\package.json`

### Pre-existing concurrent work in the dirty tree

Supplier/parts/rules changes and their migrations were present before or alongside this work. Do not assume they can be reverted. Examples:

- supplier profile/domain/features
- part directory and approval fields
- rule columns/document settings/template setup
- associated React pages and Playwright tests
- multiple October 13/15 PPAP and Rules migrations

Use `git diff` per file before editing to distinguish existing work.

---

## 5. Current Working State

### Most recent task

The latest work was the separate complex validation worker and PPAP RabbitMQ completion listener.

The worker project itself previously built successfully. It was then added to the full solution, and PPAP gained `ComplexValidationResultListener`/`ComplexValidationResultProcessor`.

### Current compile error

The full solution currently fails with one analyzer error:

```text
src/Modules/Ppap/Modules.Ppap/Processing/ComplexValidationResultProcessor.cs(44,54)
CA1826: Do not use Enumerable methods on indexable collections.
```

Current code:

```csharp
ComplexValidationEvidence? primaryEvidence = result.Evidence.FirstOrDefault();
```

The completion contract exposes `IReadOnlyList<ComplexValidationEvidence>`, so the likely fix is an index/count expression such as:

```csharp
ComplexValidationEvidence? primaryEvidence = result.Evidence.Count > 0
    ? result.Evidence[0]
    : null;
```

No source code was changed after discovering this error because the user requested handoff preparation only.

### Validation worker status

- `src/Workers/DES.Ppap.ValidationWorker` exists and is included in `src/DES.Starter.slnx`.
- It consumes `ComplexValidationRequestedIntegrationEvent` from `ppap.complex-validation.requests`.
- It currently registers only `JsonPathComparisonExecutor` (`json-path-compare:v1`).
- It publishes `ComplexValidationCompletedIntegrationEvent` to the same topic exchange.
- PPAP listens on `ppap.complex-validation.results` when `ComplexValidationOptions:Enabled=true`.
- **No request publisher exists.** Searching the solution finds the request type only in the contracts and worker; therefore the worker will sit idle.
- Worker is not started by Aspire AppHost.
- RabbitMQ is not added to AppHost in the inspected source.

### Current frontend state

- Dashboard production build succeeds.
- Targeted ESLint for the new PPAP viewer/workspace files succeeds.
- Submission workspace test suite currently has 4 tests.
- Latest run: 3 passed, one first-navigation timeout; rerunning the timed-out test alone passed. Treat it as a startup/flakiness issue, not a confirmed assertion failure.
- PDF and Omnex viewers are lazy chunks.
- Viewer license endpoint defaults disabled; spreadsheet viewer will show an initialization error until configured.
- Workspace opens only when a document has a revision.

### Current dependency/security state

`npm audit --omit=dev` reports two remaining vulnerabilities, both in the existing React Router dependency range:

- `react-router`
- `react-router-dom`

The fix requires a newer React Router version. Do not modify repository security controls or use `npm audit fix --force`. Review and upgrade deliberately with routing regression tests.

Overrides were added for newly encountered `uuid` and `brace-expansion` issues. OmnexSpread is packaged as a local tarball dependency.

### Verification results

Latest verified:

- Dashboard `npm run build`: **passed**.
- Targeted PPAP viewer/workspace ESLint: **passed**.
- OmnexSpread build: **passed**.
- OmnexSpread Jest: **61 passed**.
- ASPECT Python tests: **20 passed**.
- Diagram Extractor `compileall`: **passed**.
- `git diff --check`: **passed**.
- Submission Playwright: **3/4 passed in one run; timed-out test passed alone**.

Earlier, before the latest result listener code:

- PPAP tests: **265 passed**.
- Document Intelligence tests: **138 passed**.
- Architecture tests: **55 passed**.
- Full backend solution build had passed before the latest listener addition.

Current full backend build: **fails only on CA1826 described above**. Re-run all targeted tests after fixing it.

### Unresolved behavior risks

- No complex validation request is published.
- Mandatory RabbitMQ publish from worker can requeue indefinitely if no result queue is declared/listening.
- Result listener uses direct RabbitMQ rather than framework Inbox dedup; processor has business-key duplicate checking but no durable request-event inbox record.
- `ComplexValidationResultProcessor` supersedes current findings by rule version, then creates a finding for Failed/Error; Passed/NotEvaluated only supersede.
- Review whether an Error with no evidence should be cross-document/null scoped; current processor supports null correlation.
- ASPECT preview PDF is not propagated into revision state.
- Revision status is not advanced through Converting/Extracting/Validating.
- Reviewer artifact read policy is incomplete.
- Worker options/API options use disabled defaults and development Rabbit credentials; production secrets are not configured.

---

## 6. Important Technical Decisions

### Architecture decisions

1. **Keep PPAP state in PPAPVerifier.** External extraction/validation services must not poll or directly mutate PPAP tables.
2. **Outbox/Hangfire orchestration.** Upload and extraction requests originate transactionally from the modular monolith.
3. **Hybrid rule execution.** Rules module owns governance/versioning; a separate .NET worker executes complex algorithms.
4. **Upload-first processing.** Extraction starts when a draft revision finalizes, not only after package submission.
5. **Immutable revisions.** New primary uploads create revisions; old terminal revisions are superseded, not overwritten.
6. **Working vs final findings.** Draft findings are mutable/current/superseded; final report findings remain immutable.
7. **Fingerprint-bound waivers.** Waiver approvals are tied to exact evidence inputs and are superseded on replacement.
8. **Framework-independent OmnexSpread.** The core JS API accepts a DOM container and can be used by React, Angular, Vue, MVC, or plain JS.
9. **Read-only generated evidence.** Spreadsheet corrections happen by uploading a new source revision, not editing generated XLSX evidence in the browser.
10. **Server-safe Office conversion.** LibreOffice headless conversion was selected instead of Microsoft Office COM automation.

### Rejected or deferred approaches

- Recreating legacy shared-database polling workers: rejected.
- Copying legacy stored procedures/direct SQL orchestration: rejected.
- Using only the current simple Rules evaluator for all legacy logic: rejected as insufficient.
- Making OmnexSpread React-only: rejected.
- Treating a native PDF iframe as the final viewer: rejected because it cannot reliably navigate/highlight evidence.
- Referencing `D:\Omnex\OmnexSpreadJS` directly from Vite/CI: rejected; a versioned tarball is used for now.
- Packing the entire Omnex repo: initially produced a 14.6 MB tarball containing demos/build output; `package.json.files` was restricted to `dist/**/*`, resulting in an approximately 400 KB tarball.

### Coding conventions that must remain

- Do not modify `src/BuildingBlocks` without explicit approval.
- Module-to-module references are Contracts-only.
- Mediator handlers are `public sealed`, return `ValueTask<T>`, and use `.ConfigureAwait(false)`.
- Every command handler and paginated query handler requires a validator.
- Forward `CancellationToken` to EF/IO.
- Use structured logging; never log API keys, signed URLs, licenses, canonical document content, or document bodies.
- Use `Guid.CreateVersion7()` for new IDs.
- `BaseDbContext` applies tenant isolation; `base.OnModelCreating` must be called last.
- Do not rename existing integration event types; Outbox rows store assembly-qualified names.
- Frontend mutations pass call-specific data through `mutate(arg)`, not closed-over state.
- Dashboard forms are controlled/hand-rolled; do not add react-hook-form/zod.
- Preserve existing comments unless explicitly asked.
- Do not weaken security controls or dependency age policies to make CI pass.

### Business rules already encoded

- A document revision cannot be replaced while processing is nonterminal.
- Finalized file metadata must match reserved file content type/size.
- One current revision per document is enforced by a filtered unique index.
- Duplicate extraction/completion results are checked for idempotency/conflict.
- Working findings require both document and revision IDs when document-scoped.
- Only Pending waiver requests can be decided or withdrawn.
- Only the requester can withdraw a Pending waiver.
- Reviewer decisions require a reason and optimistic version.

---

## 7. Pending Tasks and Roadmap

### Priority 0: restore a green build

1. Fix CA1826 in `ComplexValidationResultProcessor.cs` line 44.
2. Run full solution build.
3. Run PPAP, Document Intelligence, Architecture, worker, dashboard build/lint, and submission E2E tests.

### Priority 1: make complex validation actually run

1. Add a Rules contract/service that returns complex validation plans for a template version.
2. Determine executor key from versioned assignment parameters (for example `executorKey`).
3. Extract document dependencies from rule operands/parameters.
4. After a document revision reaches extraction Ready, load all required canonical documents.
5. Publish `ComplexValidationRequestedIntegrationEvent` through `IOutboxWriter` only when all dependencies are ready.
6. Compute a deterministic input fingerprint from rule version, executor version, revision IDs, and source hashes.
7. Skip missing dependencies and reconsider on every newly ready revision.
8. Avoid publishing duplicate fingerprints.
9. Add working-finding persistence tests for Passed/Failed/Error/NotEvaluated and duplicate result delivery.
10. Add RabbitMQ integration tests and dead-letter/retry behavior.

### Priority 2: migrate legacy validators

Characterize and migrate in this order:

1. DIMR-to-DWG nominal/tolerance comparison.
2. PFD-to-PFMEA process-step comparison.
3. PFMEA-to-Control Plan control comparison.
4. Special-characteristic consistency.
5. MSA/gauge validation.
6. Knowledge-store comparisons.

Use fixtures from the legacy OBot flow. Migrate business behavior only; do not bring direct SQL/stored-procedure/file-system orchestration.

### Priority 3: production orchestration

1. Add RabbitMQ resource/configuration to Aspire AppHost.
2. Add validation worker project reference and `AddProject` entry to AppHost.
3. Ensure API and worker use identical exchange/queue names and credentials.
4. Add startup validation for enabled options and placeholder secrets.
5. Add worker health/readiness and queue metrics.
6. Decide durable completion dedup strategy; preferably integrate with Inbox semantics or add a dedicated processed-request table.

### Priority 4: complete viewer/evidence pipeline

1. Carry ASPECT `preview.pdf` into Document Intelligence completion and `PpapDocumentRevision.PreviewPdfFileAssetId`.
2. Update reviewer file access policy with explicit permissions and tenant checks.
3. Add viewer tests with mocked file URLs, license endpoint, PDF evidence, and spreadsheet ranges.
4. Add graceful signed-URL expiry refresh.
5. Add preview states for Office conversion failures.
6. Review Omnex workbook chunk size/performance for representative large XLSX files.

### Priority 5: final submission/report integration

1. Require all mandatory current revisions to be Ready or validly N/A.
2. Add authoritative final validation barrier.
3. Reuse current extraction artifacts instead of extracting unchanged files again.
4. Snapshot current revision IDs/hashes into the report manifest.
5. Copy current working findings and approved fingerprint-matching waiver requests into immutable final report records.
6. Supersede stale findings/waivers on revision replacement.

### Priority 6: cleanup and release

1. Upgrade React Router to a non-vulnerable supported release and run all routing tests.
2. Apply migrations through DbMigrator and validate data backfill.
3. Configure real provider URLs/API keys/license/RabbitMQ via secrets.
4. Update separate docs repository and changelog.
5. Remove public concept route and sample component after acceptance.
6. Commit changes in coherent slices; current working tree mixes multiple feature efforts.

---

## 8. Environment and Execution

### Primary repository installation

```powershell
cd D:\Muthu\PPAPVerifier\PPAPVerifier

cd clients\admin
npm install

cd ..\dashboard
npm install

cd ..\..
dotnet restore src\DES.Starter.slnx
```

### Run the complete stack

```powershell
dotnet run --project src\Host\DES.Starter.AppHost
```

The AppHost currently does not start RabbitMQ or the validation worker; that remains pending.

### Run API only

```powershell
dotnet run --project src\Host\DES.Starter.Api
```

Default API endpoints from project guidance are HTTP 5030 / HTTPS 7030.

### Run dashboard

```powershell
cd clients\dashboard
npm run dev
```

Dashboard: `http://localhost:5174`

Temporary concept: `http://localhost:5174/ppap-submission-concept`

### Run validation worker manually

```powershell
dotnet run --project src\Workers\DES.Ppap.ValidationWorker
```

It exits/does nothing when `ValidationWorker:Enabled` is false. It also requires RabbitMQ and a request publisher before it can process real work.

### Database migrations

Preview pending migrations:

```powershell
dotnet run --project src\Host\DES.Starter.DbMigrator -- list-pending
```

Apply:

```powershell
dotnet run --project src\Host\DES.Starter.DbMigrator -- apply
```

Seed when appropriate:

```powershell
dotnet run --project src\Host\DES.Starter.DbMigrator -- apply --seed
```

Do not apply migrations to shared/production databases without explicit approval and review.

### Build and test commands

```powershell
dotnet build src\DES.Starter.slnx

dotnet test src\Tests\Ppap.Tests\Ppap.Tests.csproj
dotnet test src\Tests\DocumentIntelligence.Tests\DocumentIntelligence.Tests.csproj
dotnet test src\Tests\Rules.Tests\Rules.Tests.csproj
dotnet test src\Tests\Architecture.Tests\Architecture.Tests.csproj

cd clients\dashboard
npm run build
npm run lint
npx playwright test tests\ppap\submission.spec.ts --workers=1
```

OmnexSpread:

```powershell
cd D:\Omnex\OmnexSpreadJS
npm install
npm run build
npm test -- --runInBand
npm run bundle
```

Repackage after changing OmnexSpread:

```powershell
npm pack --pack-destination D:\Muthu\PPAPVerifier\PPAPVerifier\clients\dashboard
cd D:\Muthu\PPAPVerifier\PPAPVerifier\clients\dashboard
npm install
```

ASPECT:

```powershell
cd D:\Muthu\PPAPVerifier\ASPECT_VALIDATION_LLM
python -m pip install -r requirements.txt
python -m unittest discover -s tests -p "test_*.py"
python main_pdf_extractor.py
```

Diagram Extractor local API/worker:

```powershell
cd D:\Muthu\PPAPVerifier\Diagram_Extractor
uvicorn main:app --reload --host 127.0.0.1 --port 8002
celery -A app.src.tasks worker --loglevel=info --concurrency=4 --pool=threads
```

### Environment variable names

Never commit or paste values. Relevant variable names include:

#### Primary API

- `DatabaseOptions__Provider`
- `DatabaseOptions__ConnectionString`
- `DatabaseOptions__MigrationsAssembly`
- `CachingOptions__Redis`
- `JwtOptions__Issuer`
- `JwtOptions__Audience`
- `JwtOptions__SigningKey`
- `Storage__Provider`
- `Storage__ServiceUrl`
- `Storage__AccessKey`
- `Storage__SecretKey`
- `Storage__BucketName`
- `Storage__ForcePathStyle`
- `AspectPdfExtractionOptions__Enabled`
- `AspectPdfExtractionOptions__BaseUrl`
- `AspectPdfExtractionOptions__ApiKey`
- `AspectPdfExtractionOptions__Provider`
- `AspectPdfExtractionOptions__Model`
- `AspectPdfExtractionOptions__UseAsyncTaskApi`
- `AspectPdfExtractionOptions__PollIntervalSeconds`
- `DiagramDrawingExtractionOptions__Enabled`
- `DiagramDrawingExtractionOptions__BaseUrl`
- `DiagramDrawingExtractionOptions__ApiKey`
- `DiagramDrawingExtractionOptions__Provider`
- `DiagramDrawingExtractionOptions__Model`
- `DiagramDrawingExtractionOptions__ModelApiKey`
- `DiagramDrawingExtractionOptions__DetectionModelApiKey`
- `DiagramDrawingExtractionOptions__DrawingDetectionApiKey`
- `OmnexSpreadViewerOptions__Enabled`
- `OmnexSpreadViewerOptions__LicenseKey`
- `OmnexSpreadViewerOptions__ExpiresAt`
- `ComplexValidationOptions__Enabled`
- `ComplexValidationOptions__Host`
- `ComplexValidationOptions__Port`
- `ComplexValidationOptions__UserName`
- `ComplexValidationOptions__Password`
- `ComplexValidationOptions__VirtualHost`
- `ComplexValidationOptions__ExchangeName`
- `ComplexValidationOptions__ResultQueueName`
- `ComplexValidationOptions__UseSsl`
- `EventingOptions__Provider`
- `RabbitMqOptions__Host`
- `RabbitMqOptions__Port`
- `RabbitMqOptions__UserName`
- `RabbitMqOptions__Password`
- `RabbitMqOptions__VirtualHost`
- `RabbitMqOptions__ExchangeName`

#### Validation worker

- `ValidationWorker__Enabled`
- `ValidationWorker__Host`
- `ValidationWorker__Port`
- `ValidationWorker__UserName`
- `ValidationWorker__Password`
- `ValidationWorker__VirtualHost`
- `ValidationWorker__ExchangeName`
- `ValidationWorker__RequestQueueName`
- `ValidationWorker__UseSsl`
- `ValidationWorker__PrefetchCount`

#### ASPECT

- `ASPECT_API_KEY`
- `ASPECT_MAX_UPLOAD_BYTES`
- `ASPECT_TASK_STORAGE_DIR`
- `ASPECT_SOFFICE_EXECUTABLE`
- `ASPECT_OFFICE_CONVERSION_TIMEOUT_SECONDS`
- `GPT4O_KEY`
- `GPT4O_ENDPOINT`
- `GPT4O_API_VERSION`
- `GEMINI_API_KEY`
- `GEMINI_MODEL`
- `LLM_DB_SERVER`
- `LLM_DB_NAME`
- `LLM_DB_USERNAME`
- `LLM_DB_PASSWORD`
- `LLM_DB_DRIVER`

#### Diagram Extractor

- `OPENAI_ENDPOINT`
- `OPENAI_API_KEY`
- `X-API-KEY`
- `MASTER-KEY`
- `DB_CONN_URL`
- `CELERY_BROKER_URL`
- `CELERY_RESULT_BACKEND`
- `WEBHOOK_SIGNING_ENABLED`
- `WEBHOOK_SIGNING_SECRET`
- `WEBHOOK_SIGNATURE_TOLERANCE_SECONDS`

---

## 9. Important Instructions for the Next Chat

1. Read `AGENTS.md` and the relevant `.agents/rules` before editing.
2. Start with `git status --short`; the tree is heavily dirty.
3. Do not reset, clean, checkout, stash, delete, or overwrite current changes.
4. Do not assume every dirty file belongs to this PPAP workspace effort; supplier/parts/rules work was already in progress.
5. Fix the current CA1826 build error before adding more functionality.
6. Run a full build immediately after the fix to discover any subsequent analyzer errors.
7. Do not claim the validation worker is operational until a request publisher exists and RabbitMQ/AppHost integration is tested.
8. Do not enable `ComplexValidationOptions` in production yet.
9. Do not enable OmnexSpread viewer without a valid domain-bound signed license supplied through secrets.
10. Do not expose provider/API/license values in source, logs, tests, or handoff messages.
11. Do not modify `src/BuildingBlocks` without explicit approval.
12. Keep integration event type names/namespaces stable.
13. Add tests for every command handler and complex validation transition.
14. Use `--workers=1` for the submission Playwright spec on this machine; parallel startup showed intermittent navigation timeouts.
15. Reviewer artifact read access must fail closed until explicitly implemented.
16. The worker project is not an `IModule`; it does not require the module four-place registration, but it does require AppHost orchestration and shared RabbitMQ configuration.
17. The local Omnex tarball is generated from `D:\Omnex\OmnexSpreadJS`; rebuild and repack after library changes.
18. ASPECT async execution is currently single-process/in-process; do not deploy multiple IIS workers for it without moving execution to a durable external queue.
19. Update docs/changelog before declaring the user-facing feature complete.
20. Keep `submission-concept.tsx` only until the production workspace has been manually accepted.

### Files that should not be unnecessarily modified

- `src/BuildingBlocks/**`
- Existing historical migration files
- Existing integration event type names
- Unrelated supplier/rules files in the dirty tree
- `CLAUDE.md` (canonical rules belong in `AGENTS.md`)

### Uncertain/unverified items

- New migrations have not been applied to a real PostgreSQL database in this session.
- RabbitMQ request/result round-trip has not been integration tested.
- No real Omnex license was tested.
- No real private MinIO/S3 PDF/XLSX artifact was manually opened in the new workspace.
- Reviewer source-file authorization is incomplete.
- Legacy business validator parity is not established.
- Full integration tests requiring Docker were not run during the latest sequence.

---

## 10. Exact Next Action

**First action in the new chat:**

Open `src/Modules/Ppap/Modules.Ppap/Processing/ComplexValidationResultProcessor.cs` and replace line 44's `result.Evidence.FirstOrDefault()` with an index/count-safe expression for `IReadOnlyList`, for example:

```csharp
ComplexValidationEvidence? primaryEvidence = result.Evidence.Count > 0
    ? result.Evidence[0]
    : null;
```

Then run:

```powershell
dotnet build src\DES.Starter.slnx
```

Do not begin validation request publishing until the solution is green. After the build passes, the next implementation task is to add the Rules-side dependency/plan reader and publish `ComplexValidationRequestedIntegrationEvent` through the Outbox when a current revision becomes extraction-ready.
