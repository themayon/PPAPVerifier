import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks } from "../helpers/shell-mocks";

const REPORT_ID = "01970000-0000-7000-8000-000000000301";
const SUBMISSION_ID = "01970000-0000-7000-8000-000000000201";

const report = (decision: string, comment: string | null = null) => ({
  id: REPORT_ID,
  submissionId: SUBMISSION_ID,
  jobId: "01970000-0000-7000-8000-000000000401",
  submissionVersion: 3,
  generatedAtUtc: "2026-10-02T00:00:00Z",
  partName: "ECU Controller Bracket",
  partNumber: "12004-R-P0",
  drawingRevision: "A",
  availableDocumentCount: 4,
  notApplicableDocumentCount: 0,
  quarantinedDocumentCount: 0,
  findingCount: 2,
  automatedFindingCount: 0,
  manualFindingCount: 0,
  waivedFindingCount: 0,
  openFindingCount: 2,
  failedFindingCount: 2,
  notEvaluatedFindingCount: 0,
  errorFindingCount: 0,
  findings: [],
  manualFindings: [],
  reproducibilityManifest: null,
  submission: { id: SUBMISSION_ID, status: "Submitted", decision, decisionComment: comment, decidedByUserId: null, decidedAtUtc: null, version: 3, isLatestReport: true },
});

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
  await mockJsonResponse(page, "**/api/v1/identity/permissions", ["Permissions.Ppap.Findings.View", "Permissions.Ppap.Submissions.Approve"]);
});

test("requires a comment to reject and records the decision", async ({ page }) => {
  test.slow();
  let decided = false;
  await page.route(`**/api/v1/ppap/operations/reports/${REPORT_ID}/review`, (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(decided ? report("Rejected", "Missing PFMEA") : report("None")) }));
  await page.route(`**/api/v1/ppap/submissions/${SUBMISSION_ID}/decision`, async (route) => {
    decided = true;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ submissionId: SUBMISSION_ID, status: "Submitted", decision: "Rejected", comment: "Missing PFMEA", decidedByUserId: null, decidedAtUtc: "2026-10-03T00:00:00Z", version: 4 }) });
  });
  await page.goto(`/ppap/reports/${REPORT_ID}/review`);

  const panel = page.getByLabel("Approval decision");
  await expect(panel).toContainText("Awaiting decision", { timeout: 30_000 });
  await expect(panel).toContainText("2 open findings remain");
  await expect(panel.getByRole("button", { name: "Interim approval" })).toBeVisible();

  await panel.getByRole("button", { name: "Reject" }).click();
  await page.getByRole("button", { name: "Confirm" }).click();
  await expect(page.getByText("A comment is required for interim approval or rejection.")).toBeVisible();

  await page.getByLabel("Comment").fill("Missing PFMEA");
  const request = page.waitForRequest((r) => r.url().endsWith(`/submissions/${SUBMISSION_ID}/decision`) && r.method() === "POST");
  await page.getByRole("button", { name: "Confirm" }).click();
  expect((await request).postDataJSON()).toEqual({ decision: "Rejected", comment: "Missing PFMEA", expectedVersion: 3 });
  await expect(panel).toContainText("Rejected");
  await expect(panel).toContainText("Missing PFMEA");
  await expect(panel.getByRole("button", { name: "Approve" })).toHaveCount(0);
});

test("hides decision buttons without the approve permission", async ({ page }) => {
  test.slow();
  await mockJsonResponse(page, "**/api/v1/identity/permissions", ["Permissions.Ppap.Findings.View"]);
  await mockJsonResponse(page, `**/api/v1/ppap/operations/reports/${REPORT_ID}/review`, report("InterimApproved", "Pending capability study"));
  await page.goto(`/ppap/reports/${REPORT_ID}/review`);

  const panel = page.getByLabel("Approval decision");
  await expect(panel).toContainText("Interim approval", { timeout: 30_000 });
  await expect(panel).toContainText("Pending capability study");
  await expect(panel.getByRole("button", { name: "Approve" })).toHaveCount(0);
  await expect(panel).toContainText("You need the Approve PPAP Submissions permission");
});
