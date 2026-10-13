# Next Actions

## Current resume checkpoint

The core PPAP pipeline is implemented through trusted extraction, terminal-state consumption, exact snapshot Rules evaluation, evidence-linked findings, completed reports, notifications, and a bounded reproducibility manifest. PDF and Drawing providers remain disabled by default and require deployment secrets/live contract environments.

Direct EF inspection on 2026-10-07 showed earlier PPAP migrations recorded as applied in local `fsh` outside this agent's executed commands. Only `20261007102729_AddPpapReportManifest` was pending. Do not apply it without explicitly intending local schema mutation.

The next implementation choices require external input or infrastructure: approved legacy parity fixtures, live PDF/Drawing provider environments and expected outputs, Docker, migration application approval, or external visual/docs approval. Manual findings and waiver workflow are complete.

## Completed first task reference

Before editing:

1. Read `AGENTS.md`.
2. Invoke the repository skills `add-integration-event`, `mediator-reference`, and `testing-guide`.
3. Read `.agents/rules/eventing.md`, `jobs.md`, `database.md`, `architecture.md`, `testing.md`, and `integration-testing.md`.
4. Re-run `git status --short` and do not clean/reset the current worktree.
5. Read these files first:
   - `src/Modules/Ppap/Modules.Ppap/Processing/PpapProcessingJob.cs`
   - `src/Modules/Ppap/Modules.Ppap/Processing/PpapProcessingOptions.cs`
   - `src/Modules/Ppap/Modules.Ppap/IntegrationEventHandlers/PpapSubmissionQueuedIntegrationEventHandler.cs`
   - `src/Modules/Ppap/Modules.Ppap/Domain/PpapSubmissionDocument.cs`
   - `src/Modules/DocumentIntelligence/Modules.DocumentIntelligence/Features/v1/Attempts/RequestDocumentExtraction/RequestDocumentExtractionCommandHandler.cs`
   - `src/Modules/DocumentIntelligence/Modules.DocumentIntelligence/Processing/ExtractionProcessingJob.cs`
   - `src/Modules/DocumentIntelligence/Modules.DocumentIntelligence.Contracts/Events/*`

### Recommended implementation sequence

1. Extract the idempotent attempt-creation/hash/enqueue logic from `RequestDocumentExtractionCommandHandler` into an internal DocumentIntelligence service.
2. Keep the existing command handler's authenticated owner equality check; it calls the internal service after authorization.
3. Add a stable `PpapDocumentExtractionRequestedIntegrationEvent` in `Modules.Ppap.Contracts` carrying event metadata, tenant, PPAP job/submission/document IDs, owner ID, source FileAsset ID, and document code. Do not carry file bytes or secrets.
4. Add an idempotent DocumentIntelligence Inbox handler that validates ambient tenant against the event and calls the internal request service using trusted event metadata.
5. Add PPAP processing mode/configuration separate from the development fixture provider. Production must never enable the fixture.
6. In real-extraction mode, after credit commit and job claim, publish one extraction-request event per `Available` PPAP document atomically and leave the PPAP job in `PreprocessingExtraction`; do not prematurely complete the report.
7. Persist enough per-document request state/idempotency marker in PPAP to prevent duplicate request-event emission. This likely needs a PPAP migration; inspect current aggregate before deciding fields.
8. Add tests for duplicate PPAP job execution, duplicate Inbox delivery, tenant mismatch, and no paid provider duplicate.
9. Build and run PPAP, DocumentIntelligence, Architecture tests before moving to result consumption.

Do not combine the first change with Rules/findings implementation. Establish the durable request boundary and tests first.

## Invalid-document credit decision — resolved

The user decided that `InvalidDocument` (wrong document type or coversheet) remains charged. Eligible system/infrastructure failures are refunded.

## Second task: consume extraction terminal events in PPAP — completed

After the request path is stable:

1. Add idempotent PPAP Inbox handlers for `DocumentExtractionCompletedIntegrationEvent` and `DocumentExtractionFailedIntegrationEvent`.
2. Persist extraction attempt ID, terminal extraction status, canonical document ID, raw response artifact ID, generated Excel artifact ID, and sanitized failure code on the PPAP document or a dedicated child entity.
3. Use owner/tenant/document correlation from the event; reject mismatches.
4. When all submitted/available documents are terminal:
   - If any terminal failure exists, apply the approved credit/failure policy and publish PPAP failure exactly once.
   - If all succeed, enqueue the next durable evaluation stage exactly once.
5. Add an additive PPAP migration and integration/domain tests.

## Third task: Rules evaluation orchestration — completed

1. Add a Contracts-only owner/tenant-safe query for canonical documents needed by a PPAP evaluation job; do not expose runtime DbContext access.
2. Build the document-code → canonical JSON dictionary.
3. Trigger the exact snapshotted published/retired template version stored on the PPAP submission.
4. Preserve deterministic run/version IDs and missing-document `NotEvaluated` semantics.
5. Store the Rules evaluation run ID on the PPAP processing/report side.

## Fourth task: findings and reports — completed

This needs explicit modeling before coding:

- No persistent `PpapFinding` aggregate was identified in the current domain inventory.
- Evaluation results need stable rule code, message/outcome, evidence, and severity mapping.
- Existing outcome JSON is configurable and may contain only `message`; do not invent Critical/Major/Minor severity.
- Define how `Failed`, `NotEvaluated`, and `Error` become findings and report counts.
- Add evidence links to canonical sheet/cell data and exact rule/template versions.
- Update supplier report and operator UI only after the backend contract is stable.

## Live provider validation — partially verified, still blocked

Do not request secrets in chat or commit them.

Prerequisites:

1. Install dependencies in an isolated ASPECT Python environment from `requirements.txt`; current Python 3.10 lacks `google-genai`.
2. Configure ASPECT environment variables, including a newly rotated `ASPECT_API_KEY` and provider/DB values.
3. Configure matching .NET `AspectPdfExtractionOptions` through environment/secrets.
4. Obtain one sanitized representative PDF, expected document code, and expected Excel/key values from the user.
5. Run ASPECT locally and perform one non-production extraction.
6. Verify invalid-document, coversheet, HTTP-200-error, successful Excel download, private artifact persistence, and canonical JSON.
7. Do not start the full API against local `fsh` merely for health unless startup side effects are accepted. Prefer focused tests or isolated disposable DB.

## Provider follow-up

- Scheduled automatic cleanup/retention is implemented in ASPECT but disabled by default. Before enabling, approve and configure explicit per-category retention minutes (minimum 120) and validate in dry-run mode.
- Preserve authenticated cleanup endpoints.
- Rotate all historical provider/database credentials outside source control.
- OpenAPI/docs policy is complete: disabled by default; explicit enable requires a strict direct-peer CIDR allowlist. IIS/proxy peer networks must be configured deliberately because forwarded headers are ignored.
- Operator-wide tenant-scoped extraction/dead-letter visibility is complete in API and admin UI.
- Replay is intentionally deferred: a dead-letter event has already failed PPAP and may have refunded credits, so replay requires an explicit PPAP reopening/ledger policy. The approved current behavior is visibility only.

## Remaining external dependencies

1. Sanitized drawing PDF/image fixture with expected header, dimensions, tolerances, bubbles/special characteristics, validity, and optional Excel values for live polling validation.
2. Running non-production Diagram Extractor dependencies: configured SQL Server, RabbitMQ, Celery worker, detection model, provider secrets, HTTPS endpoint, and reverse-proxy request-body limits. Application operational/admin endpoints are now key-protected and legacy payloads bounded.
3. Approved sanitized legacy rule parity fixtures with canonical inputs and expected findings for Header, Cross, Document/list, Document-cross, Specific-cross, dynamic formula, Custom/RPN, ML/model, Drawing, and consolidation behavior. Source analysis exists but no executable fixture package was found.
4. Docker availability for Testcontainers.
5. Prototype screenshot/visual approval remains external; targeted PPAP semantic, keyboard, responsive, focus, progress, form-linkage, and reduced-motion review is complete.
6. Separate docs repository and changelog location.

## Verification commands after the next backend slice

Use actual DES paths, not the older FSH examples:

```text
dotnet test src/Tests/DocumentIntelligence.Tests/DocumentIntelligence.Tests.csproj --no-restore
dotnet test src/Tests/Ppap.Tests/Ppap.Tests.csproj --no-restore
dotnet test src/Tests/Rules.Tests/Rules.Tests.csproj --no-restore
dotnet test src/Tests/Architecture.Tests/Architecture.Tests.csproj --no-restore
dotnet build src/DES.Starter.slnx --no-restore
git diff --check
```

If a migration is created, review `Up()` for destructive changes, then use the approved DbMigrator flow. Do not start API as a substitute for migration validation.

Provider checks:

```text
py -3.10 -m unittest discover -s tests -p "test_*.py"
py -3.10 -m py_compile api_security.py connector_modules.py main_pdf_extractor.py pdf_extractor.py tests\test_api_security.py
```

## Work that must not be done without new input/approval

- Preserve the approved invalid-document policy: committed credits remain charged; eligible system failures are refunded.
- Preserve the approved Drawing result transport: authenticated polling with callbacks explicitly disabled; do not add callback fields/authentication without a new decision.
- Do not invent IMDS/APDS Level-matrix requirements.
- Do not copy legacy credentials or API keys.
- Do not migrate legacy customer/rule/history data.
- Do not add executable rule languages.
- Do not broaden BuildingBlocks changes without explicit approval.
- Do not delete/recreate existing migration chains to make tooling easier.
- Do not repair unrelated full Playwright failures while working on the PPAP integration slice.
- Do not commit, push, reset, clean, or discard the current worktree unless the user explicitly requests it.
