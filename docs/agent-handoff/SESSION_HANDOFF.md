# Session Handoff

## Handoff metadata

- Repository root: `D:\Muthu\PPAPVerifier\PPAPVerifier`
- Active branch: `main`
- HEAD: `aa4ea3c18dbd4b65d091132e987de3ae6427b256`
- Upstream shown by Git: `origin/main`
- Worktree: heavily modified and uncommitted; do not reset, clean, checkout over, or discard it.
- Canonical execution checklist outside the repository: `C:\Users\intel\.devin\plans\plan-169b65f0a7038701.md`
- Full prior conversation transcript: `C:\Users\intel\AppData\Roaming\devin\cli\summaries\history_9c7dd16e858e4b04.md`
- Prototype reference: `C:\Users\intel\Downloads\omnex_ppap_validator.html`
- Legacy application reference: `D:\TFS\EwIMS\RPA\PPAPVerifier_DynamicForm_Downgrade\OBot`
- PDF/Excel AI service: `D:\TFS\EwIMS\RPA\LLM\Service\ASPECT_VALIDATION_LLM`
- The ASPECT directory is not a Git repository. Its changes exist only in that filesystem directory.
- No commit or push was created during this work.

## Original objective

Build PPAPVerifier on this FullStackHero-derived .NET/React starter while preserving the supplied Omnex prototype's visual language and using the legacy PPAPVerifier behavior as the rule-processing reference. The intended system accepts supplier PPAP documents, validates/extracts them through existing AI services, applies tenant/customer templates and configurable rules, and presents findings, reports, operator workflows, credits, and notifications.

Acceptance criteria carried through the session:

- OEM/customer is the tenant boundary.
- Phase 1 supplier access is owner-only; one supplier maps to one OEM.
- Use the modular monolith, tenant filters, server permissions, Outbox/Inbox, Hangfire, private Files storage, React/TanStack Query, and route-mocked Playwright tests.
- Keep module references Contracts-only.
- Use the Mediator source generator, not MediatR.
- Preserve prototype styling on PPAP pages without changing unrelated starter screens.
- Reserve 10 credits on submission; commit when processing begins; refund eligible system failures; manual operator top-up approval; no credit expiry.
- Use in-app notifications; PDF report download and email remain deferred.
- Reuse existing PDF-to-Excel and Drawing Extractor APIs behind provider-neutral adapters.
- Achieve legacy parity before evolving semantics; do not migrate legacy data.
- Never treat missing extraction as a pass.
- Never invent compliance scores, Level applicability, or AI accuracy.
- The supplied Level 1–5 matrix is authoritative for the 18 listed elements.

## Architecture and stack

The repository uses `DES.*` namespaces and host project names, despite older FSH guide paths.

- Backend: .NET 10, C# latest, ASP.NET Core Minimal APIs, Mediator 3.0.2 source generator, FluentValidation, EF Core 10.0.8, PostgreSQL/Npgsql, Finbuckle multitenancy, Hangfire, Outbox/Inbox, SignalR/SSE, S3/MinIO/local storage.
- Frontend: React 19, Vite 7, TypeScript, TanStack Query v5, React Router 7, Tailwind v4, Radix, Lucide, Playwright.
- Tests: xUnit, Shouldly, NSubstitute, AutoFixture, NetArchTest, Testcontainers.
- Main solution: `src/DES.Starter.slnx`.
- Host: `src/Host/DES.Starter.Api`.
- Migrator: `src/Host/DES.Starter.DbMigrator`.
- Central migrations: `src/Host/DES.Starter.Migrations.PostgreSQL`.

Read `AGENTS.md` and the relevant `.agents/rules/*` before continuing. `src/BuildingBlocks` is protected. The user explicitly approved the one current BuildingBlocks change adding `FileType.Document` rules.

## Implemented modules and product work

### PPAP

`src/Modules/Ppap/`

Implemented tenant/owner-scoped drafts, persistent Parts, exact template snapshots, document slots, private upload/finalize flow, Submit/Retain/Available dispositions, credit account/ledger/top-up decisions, atomic submission confirmation, durable jobs, processing leases, fixture processing, web reports, in-app notification events, supplier pages, and operator pages.

Important current limitation: `PpapProcessingOptions` still configures only the fixture provider. Development enables the fixture and production validation rejects it. PPAP processing is not yet wired to DocumentIntelligence extraction events or Rules evaluation.

### Rules

`src/Modules/Rules/`

Implemented rule definitions and immutable versions, Draft/InReview/Published/Retired lifecycle, safe typed AST, deterministic three-valued evaluation, bounded traces, templates, document requirements, rule assignments, persistent evaluation runs/results, admin catalogue/designer/workbench, and Level 1–5 baseline seed definitions.

The authoritative 18-code matrix is in `Persistence/PpapLevelMatrix.cs`. `IMDS` and `APDS` were deliberately not seeded because they were absent from the supplied matrix; they remain customer-template options.

### DocumentIntelligence

`src/Modules/DocumentIntelligence/`

Implemented tenant-scoped extraction attempts, canonical documents, provider-neutral contracts, owner-scoped retrieval/search, ASPECT provider options, API adapter, private raw/Excel artifact storage, XLSX canonicalization, durable extraction requests/jobs, atomic claims, leases, bounded retries/dead-lettering, and completion/failure Outbox events.

Important current limitation: `RequestDocumentExtractionCommandHandler` requires the current authenticated user to equal `OwnerUserId`. A PPAP background job therefore cannot call this command directly. The next session should preserve this user-facing security check and introduce a trusted Inbox/integration-event path or extracted internal request service for background orchestration.

### Files and Storage

Added `IInternalFileArtifactService`, private generated-artifact creation/read, policy enforcement, antivirus result handling, cleanup-on-failed-save, and `DocumentExtractionFileAccessPolicy`.

The user explicitly approved the minimal protected change in `src/BuildingBlocks/Storage/FileType.cs`: `FileType.Document` supports `.xlsx`, `.docx`, `.pptx`, `.txt`, `.csv`, `.json` up to 25 MB.

### Frontends

- Admin: PPAP operations, credit top-up decisions, Rules catalogue/templates/workbench, permission mirrors and route guards.
- Dashboard: PPAP home, Parts, submission checklist/upload, processing/realtime recovery, report page, prototype-scoped PPAP CSS.
- S/R/A dispositions are shown in the template editor and supplier checklist.

## Authoritative Level 1–5 interpretation

Disposition meanings:

- `S` / Submit: upload required before submission.
- `R` / Retain: visible but does not block digital submission.
- `A` / Available: must be available/uploaded for customer/on-site review.

Baseline templates:

- Level 1: PSW Submit; other 17 Retain.
- Level 2: DIMR, MPTR, IPS, QLD, AAR, SAMP, CSR, PSW Submit; others Retain.
- Level 3: all 18 Submit.
- Level 4: PSW Submit; other 17 Retain.
- Level 5: all 18 Available.

The five templates were seeded for root, acme, and globex and an idempotent second seed pass succeeded.

## Verified PDF extraction contract

Selected approach:

- Use synchronous `POST /extract_pdf`, not the older `/api/v1/upload-file/` ref-id API.
- Authenticate with `X-API-Key` from secret configuration.
- Use generated Excel as the authoritative extraction artifact.
- Retrieve Excel through authenticated HTTP, never a shared filesystem path.
- Resolve prompts by `doc_type`; omit legacy integer `TemplateDocId`.
- Defer MPTR knowledge-store comparison; omit `knowledge_store_api_key` and `standard_pdf` initially.
- Source PDF limit: 25 MB.
- Request timeout: 100 minutes; extraction lease: 120 minutes.
- Coversheet-only documents are terminal invalid-for-review.

Request multipart fields: `file`, `doc_type`, `generate_excel=true`, `provider`, `model`.

Success response requires case-insensitive `Is_Valid=Yes`, a valid `process_id`, and `excel_info` containing `excel_generated=true`, `excel_filename`, and relative `excel_download_url`. The active provider does not return cleaned `json_data`; the adapter downloads and canonicalizes Excel.

Body-aware result mapping is required because the legacy service returns some errors with HTTP 200. `Is_Valid=No` is `InvalidDocument`; coversheet is `CoversheetDocument`; 400/413 are permanent; 408/429/500/502/503/504 and network timeout are transient; Excel 404 is retried briefly within the same attempt.

## ASPECT service changes

Changed in the non-Git directory `D:\TFS\EwIMS\RPA\LLM\Service\ASPECT_VALIDATION_LLM`:

- Added `api_security.py`.
- Added `.env.example` containing names/placeholders only.
- Added `tests/test_api_security.py`.
- Modified `main_pdf_extractor.py`, `connector_modules.py`, and `pdf_extractor.py`.
- Replaced active provider and DB credential values in `CONFIG_FILES/api_config.yaml` and `CONFIG_FILES/llm_extractor_configs.yaml` with null placeholders.
- Added constant-time `X-API-Key` validation, 25 MB bounded reads, safe `.xlsx` path resolution, secured destructive cleanup endpoints, coversheet invalid response, typed response metadata, and environment overrides.

Historical credentials were present in source files. Their values must never be copied into this repository or handoff. Removing current values does not rotate credentials or remove them from source-control/history; operators must rotate them.

## Database state

Approved local target: PostgreSQL database `fsh` at localhost:5432. Tenants observed: root, acme, globex.

All PPAP, Rules, and DocumentIntelligence migrations were applied through DbMigrator. The latest verified `list-pending` run reported zero pending migrations. The latest applied migration was `20261006102339_AddExtractionProcessing`.

API startup was performed once for a health smoke check and returned HTTP 200. Existing startup jobs also synchronized permission claims and generated two Billing draft usage invoices for period `2026-09` (acme and globex). The API was stopped immediately. Do not repeat API startup against this DB without considering those side effects.

## Verification summary

Latest commands actually run in this session:

- DocumentIntelligence tests: 56 passed.
- Files tests: 28 passed.
- Framework tests: 134 passed.
- Architecture tests: 55 passed.
- Full `src/DES.Starter.slnx` build: succeeded, 0 warnings/errors.
- Python 3.10 `unittest` security suite: 13 passed.
- Python compilation of changed ASPECT modules: passed.
- `git diff --check`: passed before handoff document creation.

Earlier verified/reported and not rerun during handoff:

- PPAP tests: 184 passed.
- Rules tests: 46 passed.
- Admin PPAP Playwright: 3 passed; Rules/Templates: 5 passed.
- Dashboard PPAP targeted suites: 10 passed before matrix updates; later submission/template-focused runs also passed.
- Admin/dashboard lint and production builds passed with pre-existing Fast Refresh warnings.

Blocked/failed verification:

- Docker is unavailable, so Testcontainers runtime tests remain blocked.
- FastAPI in-process TestClient wiring smoke failed to import the full application because the available Python 3.10 environment does not have declared `google-genai` installed. The lightweight security tests still pass; no live LLM call was made.
- Full unrelated frontend Playwright suites have pre-existing failures/timeouts; targeted PPAP/Rules suites pass.
- `dotnet tool restore` is blocked by unavailable `fullstack.cli 10.0.0-rc.1`; migration generation used an installed global `dotnet-ef`.

## Security and configuration

- Do not put ASPECT/API/provider keys in appsettings, source code, docs, logs, or chat.
- The .NET adapter is disabled in base and development appsettings. Enable through deployment secrets/environment only.
- Required live-test configuration names are documented in `ARCHITECTURE_DECISIONS.md`.
- Existing development appsettings contain unrelated legacy/dev credentials. Do not reproduce them in documentation; treat them as pre-existing security debt.
- `/download_excel` previously had path traversal; it was changed to basename/extension/root-containment checks.
- Raw provider errors are sanitized before persistence and exposure.

## Subagent state

No subagent is running. All subagents worked directly in the shared worktree; there are no subagent branches or merge references.

| Agent ID | Responsibility | Status | Output location |
|---|---|---|---|
| `5d771c86` | Read-only review of PPAP/Rules/DocumentIntelligence/frontend work | Completed | Findings were integrated; no writes |
| `cb2b9caf` | PPAP processing leases/failure hardening and frontend race fixes | Completed | Shared PPAP/frontend worktree |
| `56325280` | Level 1–5 matrix, dispositions, seeds, UI and migrations | Completed | Shared Rules/PPAP/frontend worktree |
| `75887370` | Read-only ASPECT API contract confirmation | Completed | No writes |
| `7a31932f` | Read-only legacy caller/worker contract confirmation | Completed | No writes |
| `aaa6ea00` | Harden ASPECT API/security/config/tests | Completed | External non-Git ASPECT directory |
| `8f0b9842` | Files internal artifact service and DI access policy | Completed | Shared repository worktree |
| `6740daaa` | ASPECT .NET adapter and XLSX canonicalizer | Completed | Shared DocumentIntelligence worktree |
| `44f6847b` | Durable extraction request/job/leases/events/migration | Completed | Shared DocumentIntelligence worktree |

Earlier subagents from the prior summarized conversation are not individually identifiable by agent ID in the available transcript. Their changes are represented in the current worktree and checklist; no running status was reported.

## Critical unfinished work

1. PPAP does not yet request DocumentIntelligence extraction through a trusted cross-module event.
2. PPAP does not consume extraction completed/failed events or persist per-document extraction state.
3. Rules evaluation is not triggered from extracted canonical documents.
4. Evaluation results are not translated into persistent evidence-linked PPAP findings/reports.
5. Credit behavior for invalid documents versus infrastructure failures needs an explicit decision before PPAP failure handling is wired. Only eligible system failures are approved for refund.
6. Live non-production ASPECT contract test needs a configured secret environment and representative sanitized PDF/expected Excel.
7. Automatic ASPECT artifact/process-result retention cleanup is still pending.
8. Drawing Extractor callback/authentication/replay contract is still missing.
9. Legacy rule-category parity fixtures are still missing.
10. Docker-backed integration, visual baseline, accessibility, security/performance/rollout reviews, external docs repo, and changelog remain pending.

See `IMPLEMENTATION_STATUS.md`, `ARCHITECTURE_DECISIONS.md`, and `NEXT_ACTIONS.md` for exact status and resume steps.
