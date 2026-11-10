import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks } from "../helpers/shell-mocks";

const submissionId = "01960000-0000-7000-8000-000000000001";
const jobId = "01960000-0000-7000-8000-000000000099";

const submission = {
  id: submissionId,
  partName: "Steering Knuckle",
  partNumber: "SK-2048",
  drawingRevision: "C",
  ppapLevel: 3,
  ruleTemplateId: "01960000-0000-7000-8000-000000000030",
  ruleTemplateVersionId: "01960000-0000-7000-8000-000000000031",
  status: "Draft",
  version: 7,
  createdAtUtc: "2026-01-20T10:00:00Z",
  updatedAtUtc: "2026-01-21T12:30:00Z",
  submittedAtUtc: null,
  cancelledAtUtc: null,
};

const documents = [
  {
    id: "01960000-0000-7000-8000-000000000011",
    submissionId,
    documentCode: "DWG",
    isRequired: true,
    disposition: "Submit",
    status: "Available",
    fileAssetId: "01960000-0000-7000-8000-000000000021",
    notApplicableReason: null,
    createdAtUtc: "2026-01-20T10:00:00Z",
    updatedAtUtc: "2026-01-20T10:02:00Z",
  },
  {
    id: "01960000-0000-7000-8000-000000000012",
    submissionId,
    documentCode: "AAR",
    isRequired: false,
    disposition: "Retain",
    status: "NotApplicable",
    fileAssetId: null,
    notApplicableReason: "No appearance requirement",
    createdAtUtc: "2026-01-20T10:00:00Z",
    updatedAtUtc: "2026-01-20T10:03:00Z",
  },
];

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
  await mockJsonResponse(page, `**/api/v1/ppap/submissions/${submissionId}`, submission);
  await mockJsonResponse(page, `**/api/v1/ppap/submissions/${submissionId}/documents`, documents);
  await mockJsonResponse(page, `**/api/v1/ppap/submissions/${submissionId}/workspace`, {
    submissionId,
    partName: submission.partName,
    partNumber: submission.partNumber,
    drawingRevision: submission.drawingRevision,
    ppapLevel: submission.ppapLevel,
    status: submission.status,
    version: submission.version,
    documents: documents.map((document) => ({ ...document, currentRevision: null, revisions: [] })),
  });
  await mockJsonResponse(page, `**/api/v1/ppap/submissions/${submissionId}/documents/*/revisions`, '"01960000-0000-7000-8000-000000000077"', { method: "POST" });
  await mockJsonResponse(page, "**/api/v1/ppap/credits/balance", {
    availableCredits: 20,
    reservedCredits: 0,
  });
  await mockJsonResponse(page, `**/api/v1/ppap/submissions/${submissionId}/submit`, `"${jobId}"`, {
    method: "POST",
  });
});

test("renders document states and submits the current version", async ({ page }) => {
  await page.goto(`/ppap/submissions/${submissionId}`);

  await expect(page.getByRole("heading", { name: "Steering Knuckle" })).toBeVisible();
  await expect(page.getByRole("main")).toHaveCount(1);
  await expect(page.getByText("Template snapshot", { exact: true })).toBeVisible();
  await expect(page.getByText("S — Submit", { exact: true })).toBeVisible();
  await expect(page.getByText("R — Retain", { exact: true })).toBeVisible();
  await expect(page.getByText("Upload required", { exact: true })).toBeVisible();
  await expect(page.getByText("Retained", { exact: true })).toBeVisible();
  await expect(page.getByText("Available", { exact: true })).toBeVisible();
  await expect(page.getByText("NotApplicable", { exact: true })).toBeVisible();
  await expect(page.getByText("Reason: No appearance requirement")).toBeVisible();

  const submitRequest = page.waitForRequest((request) =>
    request.url().endsWith(`/api/v1/ppap/submissions/${submissionId}/submit`)
    && request.method() === "POST",
  );
  await page.getByRole("button", { name: "Submit PPAP" }).click();
  expect((await submitRequest).postDataJSON()).toEqual({ expectedVersion: 7 });
  await expect(page).toHaveURL(`/ppap/processing/${jobId}`);
});

test("describes submit blockers in a polite live region", async ({ page }) => {
  await mockJsonResponse(page, "**/api/v1/ppap/credits/balance", {
    availableCredits: 0,
    reservedCredits: 0,
  });

  await page.goto(`/ppap/submissions/${submissionId}`);

  const blockers = page.locator("#ppap-submit-blockers");
  const submitButton = page.getByRole("button", { name: "Submit PPAP" });
  await expect(blockers).toHaveAttribute("role", "status");
  await expect(blockers).toHaveAttribute("aria-live", "polite");
  await expect(blockers).toContainText("At least 10 available credits are required.");
  await expect(submitButton).toHaveAttribute("aria-describedby", "ppap-submit-blockers");
  await expect(submitButton).toBeDisabled();
});

test("opens the integrated document workspace and requests a finding waiver", async ({ page }) => {
  const revisionId = "01960000-0000-7000-8000-000000000077";
  const findingId = "01960000-0000-7000-8000-000000000088";
  await mockJsonResponse(page, `**/api/v1/ppap/submissions/${submissionId}/workspace`, {
    submissionId,
    partName: submission.partName,
    partNumber: submission.partNumber,
    drawingRevision: submission.drawingRevision,
    ppapLevel: submission.ppapLevel,
    status: submission.status,
    version: submission.version,
    documents: documents.map((document, index) => ({
      ...document,
      currentRevision: index === 0 ? { id: revisionId, revisionNumber: 1, version: 0, sourceFileAssetId: null, sourceFileName: "design-record.pdf", sourceContentType: "application/pdf", sourceSizeBytes: 1024, uploadStatus: "Available", processingStatus: "Ready", previewPdfFileAssetId: null, generatedWorkbookFileAssetId: null, canonicalDocumentId: null, provider: "aspect-pdf", extractorVersion: "aspect-v1", failureCode: null, createdAtUtc: document.createdAtUtc, updatedAtUtc: document.updatedAtUtc, completedAtUtc: document.updatedAtUtc } : null,
      revisions: [],
    })),
    findings: [{ id: findingId, validationRunId: revisionId, ruleVersionId: revisionId, submissionDocumentId: documents[0].id, documentRevisionId: revisionId, stableRuleCode: "DWG-001", ruleName: "Drawing header", status: "Failed", message: "Part number is missing.", inputFingerprint: "fingerprint", evidenceJson: "[]", createdAtUtc: "2026-01-20T10:04:00Z" }],
    waiverRequests: [],
  });
  await mockJsonResponse(page, `**/api/v1/ppap/submissions/${submissionId}/findings/${findingId}/waiver-requests`, '"01960000-0000-7000-8000-000000000099"', { method: "POST" });
  await page.goto(`/ppap/submissions/${submissionId}`);

  await page.getByRole("button", { name: "Open workspace" }).click();
  await expect(page.getByRole("dialog", { name: /DWG document workspace/ })).toBeVisible();
  await expect(page.getByText("Part number is missing.")).toBeVisible();
  await page.getByRole("button", { name: "Request waiver" }).click();
  await page.getByLabel("Reason").fill("Customer approved this temporary deviation.");
  const waiverRequest = page.waitForRequest((request) => request.url().endsWith(`/findings/${findingId}/waiver-requests`) && request.method() === "POST");
  await page.getByRole("button", { name: "Submit request" }).click();
  expect((await waiverRequest).postDataJSON()).toEqual({ reason: "Customer approved this temporary deviation." });
});

test("exposes upload progress semantics", async ({ page }) => {
  await mockJsonResponse(page, `**/api/v1/ppap/submissions/${submissionId}/documents`, [{
    ...documents[0],
    status: "PendingUpload",
    fileAssetId: null,
  }]);

  await page.goto(`/ppap/submissions/${submissionId}`);
  await page.locator('input[type="file"]').setInputFiles({
    name: "control-plan.exe",
    mimeType: "application/octet-stream",
    buffer: Buffer.from("not executable content"),
  });

  const progress = page.getByRole("progressbar", { name: "control-plan.exe upload progress" });
  await expect(progress).toHaveAttribute("aria-valuemin", "0");
  await expect(progress).toHaveAttribute("aria-valuemax", "100");
  await expect(progress).toHaveAttribute("aria-valuenow", "0");
  await expect(progress).toHaveAttribute("aria-valuetext", /Upload failed/);
});
