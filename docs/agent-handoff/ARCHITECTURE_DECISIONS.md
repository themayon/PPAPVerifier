# Architecture Decisions and Constraints

## Decisions that must be preserved

### Tenant and ownership

- OEM/customer is the tenant boundary.
- Phase 1 supplier access is owner-only.
- One supplier maps to one OEM in Phase 1.
- All new aggregates are tenant-scoped unless deliberately global.
- Every module DbContext derives from `BaseDbContext` and calls `base.OnModelCreating(modelBuilder)` last.
- Server-side authorization and owner filtering are mandatory; route guards are not security boundaries.

### Module boundaries

- A module may reference another module only through its `.Contracts` project.
- Cross-module reactions use Outbox/Inbox integration events, not runtime-project references or direct foreign DbContexts.
- Module registration must be mirrored in API and DbMigrator Mediator assemblies/module arrays.
- Mediator source generator is mandatory; do not add MediatR.
- Handlers are `public sealed`, return `ValueTask<T>`, propagate cancellation, and commands/paginated queries have validators.

### PPAP credits

- Reserve 10 credits on submission.
- Commit reservation when processing starts.
- Refund eligible system failures.
- Credits do not expire.
- Top-ups are manual operator approvals.
- Ledger is append-only; approvals and processing are idempotent/concurrency-safe.
- Invalid documents (wrong document type or coversheet) remain charged after the 10-credit reservation is committed. Eligible system/infrastructure extraction failures are refunded.

### PPAP document matrix

- `Submit`: upload-blocking.
- `Retain`: not upload-blocking.
- `Available`: upload-blocking for customer/on-site review.
- Quarantined documents always block.
- N/A with reason remains a terminal applicability exception.
- Baseline matrix is exactly the 18 elements supplied by the user.
- `MPTR` represents Material Test Results; `CSR` represents Records of Compliance.
- `IMDS` and `APDS` are absent from the source matrix and must not be silently seeded. They remain available for customer-specific templates.

### Rules safety

- Rules are data, not executable code.
- Never accept arbitrary C#, JavaScript, SQL, reflection, or script expressions.
- Use typed AST nodes and bounded JSON size/depth/node count/path length/traces.
- Missing data is `NotEvaluated`, never pass.
- Evaluation is deterministic and retains all cross-document inputs.
- Published rule/template versions are immutable; clone to edit.
- Evaluation records exact template/rule version identifiers.
- Legacy parity must be fixture-driven before semantics evolve.

### Processing durability

- PPAP processing and extraction use durable DB state, leases, optimistic/conditional claims, bounded retries, terminal events, and Outbox atomicity.
- Do not rely solely on Hangfire execution state.
- Duplicate/retried jobs must not call a paid provider concurrently.
- Terminal completion/failure event publication is idempotent and recoverable.
- Hangfire expression use of `CancellationToken.None` is intentional per repository convention; Hangfire substitutes the runtime cancellation token.

## PDF extraction target contract

### Chosen integration

- Synchronous ASPECT `POST /extract_pdf`.
- Not the legacy Pixl `/api/v1/upload-file/` asynchronous ref-id API.
- Service authentication: `X-API-Key` from secret configuration.
- Authoritative extraction output: generated Excel artifact.
- Transport: authenticated `GET /download_excel/{filename}`.
- No shared-filesystem coupling in the new application.
- Prompt selection: `doc_type`; omit legacy integer `TemplateDocId`.
- MPTR legacy knowledge-store and standard-PDF comparison are deferred.
- Adapter source format: PDF only, maximum 25 MB.
- HTTP timeout: 100 minutes; extraction lease: 120 minutes.
- Coversheet-only result: invalid for PPAP review.

### Request

```text
POST {BaseUrl}/extract_pdf
X-API-Key: {secret}
Accept: application/json
multipart: file, doc_type, generate_excel=true, provider, model
```

Do not send `TemplateDocId`, `standard_pdf`, or `knowledge_store_api_key` in the first adapter.

### Result rules

- Success requires `Is_Valid=Yes`, valid UUID `process_id`, `excel_info.excel_generated=true`, `excel_filename`, and a safe relative `excel_download_url`.
- `Is_Valid=No` maps to `InvalidDocument` and is terminal.
- Coversheet maps to `CoversheetDocument` / invalid.
- HTTP 400/413 are permanent failures.
- HTTP 408/429/500/502/503/504 and network/timeout failures are transient.
- HTTP 200 containing `error` is still failure.
- Download 404 may be retried briefly because artifact generation can lag.
- Never expose or persist raw provider exception details as public errors.
- Returned `excel_file_path` is diagnostic only and must never be opened by the new application.
- The provider does not return canonical `json_data`; canonicalization is from downloaded XLSX.

### Excel safety

- Download URL must be relative, same service, under `/download_excel/`, with no query, fragment, traversal, or alternate host.
- Provider requires a safe basename ending `.xlsx` and root containment.
- Download is bounded to 25 MB.
- XLSX canonicalizer does not execute formulas.
- It bounds sheets, nonempty cells, per-cell strings, total uncompressed XML, and suspicious compression.
- Canonical schema currently records `schemaVersion=1.0`, document code, sheets, cell references, rows, columns, values, and value types/evidence.

## Files and artifacts

- Source documents and generated artifacts remain private.
- The Files module owns storage keys and access policy enforcement.
- DocumentIntelligence uses `IInternalFileArtifactService`; it must not access storage implementation or Files DbContext directly.
- `DocumentExtractionFileAccessPolicy` requires the owning user and tenant.
- Raw provider responses are sanitized and saved privately.
- Generated Excel is downloaded immediately; the provider is not durable storage.
- The only approved BuildingBlocks change is adding `FileType.Document` validation for `.xlsx`, `.docx`, `.pptx`, `.txt`, `.csv`, `.json` up to 25 MB. Further BuildingBlocks changes require fresh explicit approval.

## Frontend decisions

- Admin uses React Hook Form + Zod, PascalCase query conventions where established, named exports, lazy routes, hand-written DTOs, permission mirrors, and `RouteGuard`.
- Dashboard uses controlled forms rather than RHF/Zod, named exports, `withSuspense`, TanStack Query, and per-call values passed through `mutate(arg)`.
- Prototype styling is scoped to dashboard PPAP CSS.
- Do not change unrelated starter screens.
- PDF report download and email delivery remain deferred; secured web report and in-app notifications are the accepted Phase 1 surfaces.

## Security requirements

- Never copy legacy credentials into the new repository.
- Do not put ASPECT, LLM, DB, or callback secrets in source, appsettings, logs, docs, or chat.
- Base/development .NET settings leave ASPECT disabled and omit `ApiKey`.
- Production-enabled ASPECT requires HTTPS; HTTP is development-only.
- Provider-side environment variables:

```text
ASPECT_API_KEY
ASPECT_MAX_UPLOAD_BYTES=26214400
GPT4O_KEY and related endpoint/version, or GEMINI_API_KEY/GEMINI_MODEL
LLM_DB_SERVER
LLM_DB_NAME
LLM_DB_USERNAME
LLM_DB_PASSWORD
LLM_DB_DRIVER
```

- .NET-side deployment secrets/configuration:

```text
AspectPdfExtractionOptions__Enabled=true
AspectPdfExtractionOptions__BaseUrl={service-url}
AspectPdfExtractionOptions__ApiKey={same service key}
AspectPdfExtractionOptions__Provider=gemini|openai
AspectPdfExtractionOptions__Model={approved model}
AspectPdfExtractionOptions__TimeoutMinutes=100
AspectPdfExtractionOptions__LeaseMinutes=120
```

- Existing provider/database credentials found in ASPECT source must be rotated by operators; replacing active files with null does not invalidate leaked credentials.
- Existing unrelated development credentials in starter appsettings are pre-existing security debt; do not reproduce them.

## Explicitly rejected or deferred approaches

| Approach | Decision/reason |
|---|---|
| Legacy `/api/v1/upload-file/` ref-id API | Rejected for this adapter; it is a different async contract |
| Shared filesystem `excel_file_path` | Rejected; breaks remote/container deployment and leaks provider internals |
| Return Excel bytes inside `/extract_pdf` | Not selected; use secure immediate download |
| Return cleaned JSON from provider | Not selected for Phase 2; generated Excel is authoritative |
| Legacy integer `TemplateDocId` mapping | Rejected initially; prompt resolution uses document code |
| MPTR legacy knowledge-store key | Deferred |
| Arbitrary-code rule evaluation | Prohibited |
| Legacy data migration | Explicitly not planned |
| PDF report rendering/email | Deferred |
| Inventing IMDS/APDS matrix dispositions | Prohibited without user data |
| Enabling fixture processor in production | Prohibited by validator |
| Direct background call to user-authenticated extraction command | Not viable; command requires current owner user. Use trusted integration event/internal service while keeping the public check |
| Live provider calls during unit tests | Prohibited; use fake handlers/artifact services |

## Edge cases to preserve

- HTTP 200 may contain an error body.
- `excel_filename` is the actual provider field; an earlier adapter test incorrectly used `filename` and was fixed.
- Coversheet response may include both `Is_Valid=No` and `error`; classify validity before generic error.
- Token-count metric names are not secrets; `access_token`/`refresh_token` and key/secret/password/authorization fields remain filtered.
- Same original PDF filenames can collide in provider output; adapter prefixes the uploaded filename with extraction attempt UUID.
- Provider artifacts have no guaranteed automatic retention yet; download immediately.
- Late Files finalize events after PPAP submission are ignored rather than dead-lettered.
- Active unexpired processing leases cause duplicate job execution to return without provider invocation.

## Trusted extraction-request continuation decisions

- PPAP processing mode is explicit: `Disabled`, `Fixture`, or `Real`. Base/default remains disabled, development uses fixture success, and production rejects fixture mode.
- `PpapDocumentExtractionRequestedIntegrationEvent` is the stable PPAP-to-DocumentIntelligence request contract. It carries identifiers and document code only; no file bytes or secrets.
- PPAP persists `ExtractionRequestEventId` and `ExtractionRequestPublishedAtUtc` on each submission document. Marker writes, stage advancement, credit commit, and Outbox writes share one transaction.
- Real processing stops at `PreprocessingExtraction`; it must not create a report or publish PPAP completion before extraction/rules stages finish.
- The user-authenticated extraction command retains owner equality authorization. Trusted Inbox handling delegates to the same internal request service only after exact ambient/event tenant validation.
- Duplicate pending extraction requests reuse the existing attempt and may re-enqueue the extraction job. Job leases, not enqueue uniqueness, are the paid-provider concurrency boundary; re-enqueue is required to recover a committed attempt whose first Hangfire enqueue failed.
- A real-mode PPAP infrastructure exception releases its processing lease before rethrow so the Hangfire retry can reclaim the durable job. It does not apply extraction failure or credit-refund policy.

## Rules orchestration continuation decisions

- Canonical documents are read through the owner- and tenant-safe `ICanonicalDocumentReader` Contracts service; PPAP never accesses the DocumentIntelligence runtime DbContext.
- Background evaluation uses `IRuleEvaluationService`; the user-facing command retains authenticated-owner authorization and delegates to the same deterministic service.
- Exact snapshotted template versions may be evaluated after becoming `Retired`; published/retired rule versions are immutable and valid historical inputs.
- One semantic evaluation run exists per tenant, PPAP submission subject, and template version. Rules persistence and PPAP run markers make retries converge on the same run.
- PPAP stores evaluation queue/start/completion timestamps and the Rules run ID. Successful extraction queues a durable Hangfire evaluation job; successful evaluation advances only to `CrossValidation`.
- Mixed canonical schema versions are rejected deterministically rather than silently selecting or transforming a schema.

## Findings and report continuation decisions

- Phase 1 findings have no severity taxonomy. Critical/Major/Minor counters are removed rather than inferred.
- Every non-pass Rules result is persisted as a distinct finding: `Failed`, `NotEvaluated`, or `Error`. Passed results create no finding.
- NotEvaluated and Error findings do not fail processing; a terminal evaluation produces a completed report with explicit outcome counts.
- Findings snapshot the exact evaluation run/result, template/rule definition/version, stable rule code/name, configured message, reason, trace JSON, and evaluation timestamp.
- Document-scoped findings also retain PPAP submission-document, canonical-document, and extraction-attempt IDs. Cross-document findings carry no false single-document correlation.
- Reports expose total, Failed, NotEvaluated, and Error counts. They do not expose compliance scores, risk levels, recommendations, or unapproved severities.
- Report generation is a durable idempotent job after Rules evaluation. It materializes findings, creates one report, completes PPAP processing, and publishes completion through the Outbox exactly once.

## Provider validation and operations continuation decisions

- The configured Azure/OpenAI values may be injected from the existing private `aspect_gpt4o_config.yaml` into a temporary process; values must never be displayed, copied into repository configuration, or documented.
- A live provider response is not successful for PPAP unless it includes `Is_Valid=Yes`, a valid process ID, and generated downloadable Excel metadata. HTTP 200 plus validity alone remains a contract failure.
- Prompt DB credentials remain required for document-specific extraction. Generic fallback does not prove PFMEA prompt accuracy and must not be reported as a successful PFMEA contract validation.
- Operator extraction operations are tenant-scoped and permission-gated. They expose sanitized attempt metadata and artifact IDs, never secrets, raw response content, storage paths, provider request IDs, or worker identities.
- Dead-letter replay is deferred. Once failure has propagated to PPAP, replay requires an explicit PPAP reopening and credit-ledger policy; visibility-only is the approved current behavior.

## ASPECT retention continuation decisions

- Provider retention is disabled by default and dry-run defaults to true. Enabling requires an explicit scheduler interval and retention minutes for every artifact category; no production duration is inferred.
- Every retention age must be at least 120 minutes so cleanup cannot race the documented long-running extraction window.
- Cleanup roots are fixed in code under the ASPECT service directory. Environment configuration cannot redirect deletion paths.
- Roots are never deleted. Cleanup rejects symlinks/junctions/reparse points, resolved path escapes, unsupported file types, and directories containing recent descendants.
- Scheduled cleanup runs blocking filesystem work off the event loop and is cancelled during FastAPI shutdown. An authenticated manual endpoint returns aggregate path-free counts and supports dry-run override.
- Existing authenticated cleanup endpoints remain available; automatic retention covers Excel outputs, process results, detection pages, operation directories, response/LLM logs, and temporary artifacts.

## ASPECT documentation access continuation decisions

- Built-in FastAPI Swagger, ReDoc, and OpenAPI routes are disabled by default.
- Documentation can be enabled only with `ASPECT_DOCS_ENABLED=true` and a non-empty strict comma-separated IPv4/IPv6 CIDR allowlist in `ASPECT_DOCS_ALLOWED_NETWORKS`.
- Invalid booleans, host-only entries, non-network CIDRs, empty entries, and malformed networks fail configuration rather than silently broadening access.
- Authorization uses only the directly connected peer address. `X-Forwarded-For` and all forwarded-client headers are ignored to prevent spoofing.
- Behind IIS or another reverse proxy, the allowlist sees the proxy peer rather than the originating browser. The proxy network may be allow-listed only as part of an explicitly trusted deployment topology.
- Custom `/docs`, `/redoc`, and `/openapi.json` routes return 404 when disabled and 403 to peers outside the allowlist; the custom routes are excluded from the generated schema.

## PPAP accessibility continuation decisions

- AppShell owns the single main landmark; routed PPAP pages use non-main containers to avoid nested landmarks.
- Dynamic processing and upload percentages use progressbar semantics, and meaningful status transitions use focused polite live regions rather than announcing entire polling surfaces.
- Disabled submission actions reference a stable live blocker description so the reason is programmatically available.
- PPAP focus indicators must remain visible and mobile layouts must reflow metadata rather than hiding meaningful timestamps.
- Admin data tables expose accessible names, scoped headers, active-column-only sort state, keyboard-visible sort controls, and polite pagination summaries.
- Existing global reduced-motion behavior is retained; no PPAP-specific animation exception is introduced.
- Regression coverage uses existing route-mocked Playwright semantic and keyboard assertions; no new accessibility dependency was added.

## Drawing extraction continuation decisions

- Drawing result delivery uses authenticated polling, not callbacks. PPAPVerifier submits with `disable_webhook=true`, polls `omnex-task-status/{task_id}`, and downloads optional Excel through the authenticated task-scoped route.
- `DWG` routes to `DiagramDrawingExtractionProvider`; all other document codes continue through ASPECT. Existing extraction attempts, leases, retries, terminal events, PPAP failure policy, and findings orchestration remain shared.
- Diagram Extractor submission uses `X-API-KEY`; optional external tenant/model keys use headers, and supported detection settings use only the provider's actual multipart field names.
- Submission correlation comes from `ref_id`; every status response must carry the exact `task_id`. Provider-local paths, image/base64 payloads, credentials, tokens, secrets, and download URLs are stripped before private raw/canonical persistence.
- Canonical drawing schema is `drawing-1.0`; raw status JSON and optional Excel are private Files artifacts. Response, source, Excel, JSON depth/array count, and canonical size are bounded.
- Drawing integration is disabled in base/development configuration. Production requires HTTPS and non-placeholder deployment secrets.
- The external provider preserves legacy webhook behavior by default but supports explicit suppression. API keys are compared in constant time and are no longer printed; upload and notation reads are bounded.
- Callback signing/replay behavior is intentionally not part of the approved polling contract.

## Rule compatibility continuation decisions

- Legacy source code is behavioral evidence only. SQL seeds, stored procedures, hardcoded branches, and sample API payloads are not accepted as parity fixtures without approved sanitized inputs and expected findings.
- Observed legacy defects are not reproduced automatically: substring-style `IN`, inverted additional-condition `NOT IN`, inactive Header/Doc Cross dispatch, unused transformed arithmetic expressions, and ambiguous cross-document direction require explicit fixture decisions.
- Typed template parameters use AST operands shaped as `{ "type": "parameter", "name": "..." }`; names are case-sensitive and bounded to safe ASCII letters, digits, dot, underscore, and hyphen.
- Each immutable template rule assignment binds only its own bounded `ParametersJson` object. Missing parameters and invalid stored parameters become sanitized evaluation `Error` outcomes, never passes and never abort unrelated assignments.
- Parameter binding adds no tolerance, fuzzy, unit, regex, row, plugin, drawing-comparison, or ML semantics. Those remain fixture/policy gated.
- Current exact field/header and cross-document comparisons remain deterministic. Inline value sets and boolean implication encoding remain available but are not claims of legacy category parity.

## Report reproducibility continuation decisions

- Every newly generated real PPAP report stores an immutable bounded schema `1.0` reproducibility manifest; historical fixture/pre-manifest reports may expose null rather than fabricated provenance.
- Manifest data is assembled through owner/tenant-safe Contracts readers only. PPAP never reads DocumentIntelligence or Rules runtime DbContexts.
- The manifest records job/submission/version, exact template/evaluation IDs and timestamps, all evaluation result/rule version identifiers and statuses, document inventory, source hashes, provider/model/extractor versions, canonical schema/IDs, artifact IDs, and extraction timestamps.
- The manifest excludes tenant/user identifiers, canonical/raw content, traces, messages/reasons, provider request IDs, secrets, paths, scores, and risk claims. Finding traces remain separately available to the authorized report owner.
- Document and result arrays are deterministically sorted; JSON must be an object with schema `1.0` and no more than 256 KiB UTF-8.
- Provenance correlation rejects attempt/document/source/canonical/artifact mismatches before report completion.

## Manual findings and waivers continuation decisions

- Manual findings are immutable tenant-scoped review records allowed only for completed PPAP reports. Document-scoped records derive document code and require exact submission-document/canonical-document/extraction-attempt correlation; cross-document records carry no false document link.
- Authorization is separated into `Findings.View`, `Findings.CreateManual`, and `Findings.ManageWaivers`. A manual-finding creator cannot approve a waiver for their own finding.
- Automated findings remain immutable. Manual findings add to returned total count; active waivers reduce open count but never delete or alter the finding or stored automated outcome counters.
- Waivers retain immutable approver, reason, and approval timestamp. Revocation atomically deactivates the waiver and appends a separate immutable revocation actor/reason/timestamp; a later new waiver is permitted.
- At most one active waiver exists per target through serializable command handling plus a filtered unique database index. Revocation has a unique one-to-one persistence record.
- Supplier report owners see manual finding details and complete active/revoked waiver reasons, actors, and timestamps. Operator review uses independent permission-gated APIs and UI.
- Reproducibility manifests remain immutable automated processing snapshots; post-completion manual review records are intentionally outside the manifest and are returned as a separate audited layer.

## Diagram Extractor endpoint hardening decisions

- `GET /health` remains public and exposes only service health. Celery status/purge and log download require the existing master key; PDF conversion and extraction/status/artifact routes require the service API key.
- Legacy `/omnex` raw/data-URI base64 inputs are strictly decoded before database/Celery work with encoded-length precheck, validated padding/alphabet, configured decoded-size limits, and independent JPEG/PNG notation validation.
- `POST /pdf-to-jpeg` uses the same configured bounded upload read and returns sanitized 413/400/500 responses.
- Operational exceptions expose/log exception type or generic status only; payloads, keys, paths, and raw exception messages are not returned.
- Reverse-proxy request-body limits remain mandatory because application endpoint bounds occur after multipart parsing.

- API startup has real local-DB side effects through existing recurring/startup jobs.
