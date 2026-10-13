import { expect, test, type Page } from "@playwright/test";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { ADMIN_PERMS, installAdminShellMocks } from "../helpers/shell-mocks";

const REPORT_ID = "0196f000-0000-7000-8000-000000000100";
const AUTOMATED_OPEN_ID = "0196f000-0000-7000-8000-000000000101";
const AUTOMATED_WAIVED_ID = "0196f000-0000-7000-8000-000000000102";
const ACTIVE_WAIVER_ID = "0196f000-0000-7000-8000-000000000103";
const DOCUMENT_ID = "0196f000-0000-7000-8000-000000000104";
const CANONICAL_ID = "0196f000-0000-7000-8000-000000000105";
const ATTEMPT_ID = "0196f000-0000-7000-8000-000000000106";

const REVIEW = {
  id: REPORT_ID,
  submissionId: "0196f000-0000-7000-8000-000000000110",
  jobId: "0196f000-0000-7000-8000-000000000111",
  submissionVersion: 3,
  generatedAtUtc: "2026-05-12T09:00:00Z",
  partName: "Front brake bracket",
  partNumber: "BRK-2048",
  drawingRevision: "C",
  findingCount: 3,
  automatedFindingCount: 2,
  manualFindingCount: 1,
  waivedFindingCount: 1,
  openFindingCount: 2,
  findings: [
    {
      id: AUTOMATED_OPEN_ID,
      ruleEvaluationRunId: "0196f000-0000-7000-8000-000000000120",
      ruleEvaluationResultId: "0196f000-0000-7000-8000-000000000121",
      templateVersionId: "0196f000-0000-7000-8000-000000000122",
      ruleDefinitionId: "0196f000-0000-7000-8000-000000000123",
      ruleVersionId: "0196f000-0000-7000-8000-000000000124",
      submissionDocumentId: DOCUMENT_ID,
      canonicalDocumentId: CANONICAL_ID,
      extractionAttemptId: ATTEMPT_ID,
      stableRuleCode: "DIM-001",
      ruleName: "Dimensional tolerance",
      documentCode: "DIM",
      outcome: "Failed",
      message: "Dimension exceeds tolerance.",
      reason: "Measured value is outside the drawing range.",
      traceJson: "{}",
      evaluatedAtUtc: "2026-05-12T08:30:00Z",
      waivers: [],
    },
    {
      id: AUTOMATED_WAIVED_ID,
      ruleEvaluationRunId: "0196f000-0000-7000-8000-000000000125",
      ruleEvaluationResultId: "0196f000-0000-7000-8000-000000000126",
      templateVersionId: "0196f000-0000-7000-8000-000000000127",
      ruleDefinitionId: "0196f000-0000-7000-8000-000000000128",
      ruleVersionId: "0196f000-0000-7000-8000-000000000129",
      submissionDocumentId: null,
      canonicalDocumentId: null,
      extractionAttemptId: null,
      stableRuleCode: "PACK-002",
      ruleName: "Submission package",
      documentCode: null,
      outcome: "NotEvaluated",
      message: "Package review requires operator confirmation.",
      reason: null,
      traceJson: "{}",
      evaluatedAtUtc: "2026-05-12T08:31:00Z",
      waivers: [
        {
          id: "0196f000-0000-7000-8000-000000000130",
          isActive: false,
          approvedByUserId: "reviewer-old",
          reason: "Superseded customer approval",
          approvedAtUtc: "2026-05-12T10:00:00Z",
          revocation: {
            id: "0196f000-0000-7000-8000-000000000131",
            revokedByUserId: "quality-lead",
            reason: "Customer approval was withdrawn",
            revokedAtUtc: "2026-05-12T11:00:00Z",
          },
        },
        {
          id: ACTIVE_WAIVER_ID,
          isActive: true,
          approvedByUserId: "quality-manager",
          reason: "Approved temporary deviation",
          approvedAtUtc: "2026-05-12T12:00:00Z",
          revocation: null,
        },
      ],
    },
  ],
  manualFindings: [
    {
      id: "0196f000-0000-7000-8000-000000000140",
      submissionDocumentId: null,
      canonicalDocumentId: null,
      extractionAttemptId: null,
      documentCode: null,
      message: "Operator observed an unsigned declaration.",
      evidenceNote: "Signature block is blank on page 2.",
      createdByUserId: TEST_USER.sub,
      createdAtUtc: "2026-05-12T13:00:00Z",
      waivers: [],
    },
  ],
  reproducibilityManifest: {
    documents: [
      {
        id: DOCUMENT_ID,
        documentCode: "DIM",
        disposition: "Available",
        status: "Completed",
        sourceFileId: "0196f000-0000-7000-8000-000000000150",
        notApplicableReason: null,
        extraction: {
          sourceSha256: "abc123",
          attemptId: ATTEMPT_ID,
          provider: "provider",
          model: "model",
          extractorVersion: "1.0",
          canonicalDocumentId: CANONICAL_ID,
          canonicalSchemaVersion: "1.0",
          rawResponseArtifactId: null,
          generatedExcelArtifactId: null,
          createdAtUtc: "2026-05-12T08:00:00Z",
          completedAtUtc: "2026-05-12T08:10:00Z",
        },
      },
      {
        id: "0196f000-0000-7000-8000-000000000151",
        documentCode: "PSW",
        disposition: "Available",
        status: "Pending",
        sourceFileId: null,
        notApplicableReason: null,
        extraction: null,
      },
    ],
  },
};

async function seed(page: Page, permissions: readonly string[] = ADMIN_PERMS) {
  await seedAuthedSession(page, { ...TEST_USER, tenant: "oem-acme", permissions: [...permissions] });
  await installAdminShellMocks(page, permissions);
  await page.route(`**/api/v1/ppap/operations/reports/${REPORT_ID}/review`, async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(REVIEW) });
  });
}

test.describe("PPAP finding review", () => {
  test("shows finding counts, evidence IDs, reason histories, and omits self-waiver", async ({ page }) => {
    await seed(page);
    await page.goto(`/ppap/reports/${REPORT_ID}/review`);

    await expect(page.getByRole("heading", { name: "PPAP finding review" })).toBeVisible();
    await expect(page.getByText("2", { exact: true })).toHaveCount(2);
    await expect(page.getByText("1", { exact: true })).toHaveCount(2);
    await expect(page.getByText(DOCUMENT_ID, { exact: true })).toBeVisible();
    await expect(page.getByText(CANONICAL_ID, { exact: true })).toBeVisible();
    await expect(page.getByText(ATTEMPT_ID, { exact: true })).toBeVisible();
    await expect(page.getByText("Approved temporary deviation")).toBeVisible();
    await expect(page.getByText("Superseded customer approval")).toBeVisible();
    await expect(page.getByText("Customer approval was withdrawn")).toBeVisible();
    const ownFinding = page.getByText("Operator observed an unsigned declaration.").locator("xpath=ancestor::*[@data-slot='card']");
    await expect(ownFinding.getByRole("button", { name: "Waive finding" })).toHaveCount(0);
  });

  test("gates the route and keeps create and waiver permissions separate", async ({ page }) => {
    const viewOnly = ["Permissions.Ppap.Findings.View"];
    await seed(page, viewOnly);
    await page.goto(`/ppap/reports/${REPORT_ID}/review`);
    await expect(page.getByRole("heading", { name: "PPAP finding review" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Add manual finding" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Waive finding" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Revoke waiver" })).toHaveCount(0);

    await seedAuthedSession(page, { ...TEST_USER, tenant: "oem-acme", permissions: ["Permissions.Ppap.Findings.CreateManual"] });
    await installAdminShellMocks(page, ["Permissions.Ppap.Findings.CreateManual"]);
    await page.goto(`/ppap/reports/${REPORT_ID}/review`);
    await expect(page.getByText("// 403 · access denied")).toBeVisible();
    await expect(page.getByText("Permissions.Ppap.Findings.View", { exact: true })).toBeVisible();
  });

  test("exposes create and waiver actions only for their separate grants", async ({ browser }) => {
    const createContext = await browser.newContext();
    const createPage = await createContext.newPage();
    await seed(createPage, ["Permissions.Ppap.Findings.View", "Permissions.Ppap.Findings.CreateManual"]);
    await createPage.goto(`/ppap/reports/${REPORT_ID}/review`);
    await expect(createPage.getByRole("button", { name: "Add manual finding" })).toBeVisible();
    await expect(createPage.getByRole("button", { name: "Waive finding" })).toHaveCount(0);
    await expect(createPage.getByRole("button", { name: "Revoke waiver" })).toHaveCount(0);
    await createContext.close();

    const manageContext = await browser.newContext();
    const managePage = await manageContext.newPage();
    await seed(managePage, ["Permissions.Ppap.Findings.View", "Permissions.Ppap.Findings.ManageWaivers"]);
    await managePage.goto(`/ppap/reports/${REPORT_ID}/review`);
    await expect(managePage.getByRole("button", { name: "Add manual finding" })).toHaveCount(0);
    await expect(managePage.getByRole("button", { name: "Waive finding" })).toBeVisible();
    await expect(managePage.getByRole("button", { name: "Revoke waiver" })).toBeVisible();
    await manageContext.close();
  });

  test("creates a document-scoped manual finding with exact correlation", async ({ page }) => {
    await seed(page);
    let body: unknown;
    let idempotencyKey: string | undefined;
    await page.route("**/api/v1/ppap/operations/reports/manual-findings", async (route) => {
      body = route.request().postDataJSON();
      idempotencyKey = route.request().headers()["idempotency-key"];
      await route.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify("new-finding-id") });
    });
    await page.goto(`/ppap/reports/${REPORT_ID}/review`);
    await page.getByRole("button", { name: "Add manual finding" }).click();
    await page.getByLabel("Document scope").click();
    await page.getByRole("menuitem", { name: /DIM/ }).click();
    await page.getByLabel("Message").fill("Operator detected a missing approval.");
    await page.getByLabel("Evidence").fill("Approval field is empty.");
    await page.getByRole("button", { name: "Add finding" }).click();

    await expect.poll(() => body).toEqual({
      reportId: REPORT_ID,
      message: "Operator detected a missing approval.",
      evidenceNote: "Approval field is empty.",
      submissionDocumentId: DOCUMENT_ID,
      canonicalDocumentId: CANONICAL_ID,
      extractionAttemptId: ATTEMPT_ID,
    });
    await expect.poll(() => idempotencyKey).toBeTruthy();
  });

  test("sends exact waive and revoke command bodies", async ({ page }) => {
    await seed(page);
    const requests: Array<{ path: string; body: unknown }> = [];
    await page.route("**/api/v1/ppap/operations/reports/finding-waivers", async (route) => {
      requests.push({ path: "waive", body: route.request().postDataJSON() });
      await route.fulfill({ status: 201, contentType: "application/json", body: JSON.stringify("waiver-id") });
    });
    await page.route("**/api/v1/ppap/operations/reports/finding-waivers/revoke", async (route) => {
      requests.push({ path: "revoke", body: route.request().postDataJSON() });
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify("revocation-id") });
    });
    await page.goto(`/ppap/reports/${REPORT_ID}/review`);

    await page.getByRole("button", { name: "Waive finding" }).click();
    await page.getByLabel("Reason").fill("Accepted customer deviation");
    await page.getByRole("button", { name: "Waive finding" }).last().click();
    await expect.poll(() => requests[0]).toEqual({
      path: "waive",
      body: { reportId: REPORT_ID, findingKind: "Automated", findingId: AUTOMATED_OPEN_ID, reason: "Accepted customer deviation" },
    });

    await page.getByRole("button", { name: "Revoke waiver" }).click();
    await page.getByLabel("Reason").fill("Deviation expired");
    await page.getByRole("button", { name: "Revoke waiver" }).last().click();
    await expect.poll(() => requests[1]).toEqual({
      path: "revoke",
      body: { reportId: REPORT_ID, waiverId: ACTIVE_WAIVER_ID, reason: "Deviation expired" },
    });
  });
});
