import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks } from "../helpers/shell-mocks";

const reportId = "01960000-0000-7000-8000-000000000088";
const submissionId = "01960000-0000-7000-8000-000000000001";

const findingMetadata = {
  ruleEvaluationRunId: "11111111-1111-7111-8111-111111111111",
  ruleEvaluationResultId: "22222222-2222-7222-8222-222222222222",
  templateVersionId: "33333333-3333-7333-8333-333333333333",
  ruleDefinitionId: "44444444-4444-7444-8444-444444444444",
  ruleVersionId: "55555555-5555-7555-8555-555555555555",
  evaluatedAtUtc: "2026-01-20T10:00:00Z",
};

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
});

test("renders findings and a collapsed reproducibility manifest without raw data", async ({ page }) => {
  await mockJsonResponse(page, `**/api/v1/ppap/reports/${reportId}`, {
    id: reportId,
    submissionId,
    jobId: "01960000-0000-7000-8000-000000000099",
    submissionVersion: 7,
    generatedAtUtc: "2026-01-20T10:01:00Z",
    partName: "Brake pedal assembly",
    partNumber: "BPA-1042",
    drawingRevision: "C",
    availableDocumentCount: 12,
    notApplicableDocumentCount: 3,
    quarantinedDocumentCount: 0,
    findingCount: 4,
    automatedFindingCount: 2,
    manualFindingCount: 2,
    waivedFindingCount: 2,
    openFindingCount: 2,
    failedFindingCount: 1,
    notEvaluatedFindingCount: 1,
    errorFindingCount: 0,
    reproducibilityManifest: {
      schemaVersion: "1.0",
      jobId: "01960000-0000-7000-8000-000000000099",
      submissionId,
      submissionVersion: 7,
      templateVersionId: "33333333-3333-7333-8333-333333333333",
      evaluation: {
        runId: "11111111-1111-7111-8111-111111111111",
        version: 4,
        inputSchemaVersion: "canonical-2.1",
        status: "Completed",
        createdAtUtc: "2026-01-20T09:55:00Z",
        completedAtUtc: "2026-01-20T10:00:00Z",
        results: [
          {
            id: "22222222-2222-7222-8222-222222222222",
            ruleDefinitionId: "44444444-4444-7444-8444-444444444444",
            ruleVersionId: "55555555-5555-7555-8555-555555555555",
            stableRuleCode: "PFMEA.SEVERITY.REQUIRED",
            documentCode: "PFMEA",
            status: "Failed",
            evaluatedAtUtc: "2026-01-20T10:00:00Z",
          },
        ],
      },
      documents: [
        {
          id: "77777777-7777-7777-8777-777777777777",
          documentCode: "PFMEA",
          disposition: "Submit",
          status: "Available",
          sourceFileId: "12121212-1212-7212-8212-121212121212",
          notApplicableReason: null,
          extraction: {
            sourceSha256: "d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2",
            attemptId: "99999999-9999-7999-8999-999999999999",
            provider: "AzureDocumentIntelligence",
            model: "prebuilt-layout",
            extractorVersion: "3.2.1",
            canonicalDocumentId: "88888888-8888-7888-8888-888888888888",
            canonicalSchemaVersion: "pfmea-2.0",
            rawResponseArtifactId: "13131313-1313-7313-8313-131313131313",
            generatedExcelArtifactId: "14141414-1414-7414-8414-141414141414",
            createdAtUtc: "2026-01-20T09:56:00Z",
            completedAtUtc: "2026-01-20T09:58:00Z",
          },
        },
        {
          id: "15151515-1515-7515-8515-151515151515",
          documentCode: "CONTROL_PLAN",
          disposition: "Retain",
          status: "NotApplicable",
          sourceFileId: null,
          notApplicableReason: "SENSITIVE-NOT-APPLICABLE-REASON",
          extraction: null,
        },
      ],
    },
    findings: [
      {
        id: "66666666-6666-7666-8666-666666666666",
        ...findingMetadata,
        submissionDocumentId: "77777777-7777-7777-8777-777777777777",
        canonicalDocumentId: "88888888-8888-7888-8888-888888888888",
        extractionAttemptId: "99999999-9999-7999-8999-999999999999",
        stableRuleCode: "PFMEA.SEVERITY.REQUIRED",
        ruleName: "PFMEA severity is recorded",
        documentCode: "PFMEA",
        outcome: "Failed",
        message: "Record severity for every PFMEA row.",
        reason: "A required value was not available.",
        traceJson: JSON.stringify({ source: "canonical-document", canonicalDocumentId: "88888888-8888-7888-8888-888888888888" }),
        waivers: [
          {
            id: "16161616-1616-7616-8616-161616161616",
            isActive: true,
            approvedByUserId: "17171717-1717-7717-8717-171717171717",
            reason: "Supplier concession approved for this submission.",
            approvedAtUtc: "2026-01-21T11:00:00Z",
            revocation: null,
          },
        ],
      },
      {
        id: "aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa",
        ...findingMetadata,
        submissionDocumentId: null,
        canonicalDocumentId: null,
        extractionAttemptId: null,
        stableRuleCode: "PACKAGE.CROSS_CHECK",
        ruleName: "Package identifiers agree",
        documentCode: null,
        outcome: "NotEvaluated",
        message: null,
        reason: "Canonical documents were not available for comparison.",
        traceJson: JSON.stringify({ comparedDocumentCodes: ["PSW", "PFMEA"] }),
        waivers: [
          {
            id: "18181818-1818-7818-8818-181818181818",
            isActive: false,
            approvedByUserId: "19191919-1919-7919-8919-191919191919",
            reason: "Temporary approval while source files were collected.",
            approvedAtUtc: "2026-01-20T11:00:00Z",
            revocation: {
              id: "20202020-2020-7020-8020-202020202020",
              revokedByUserId: "21212121-2121-7121-8121-212121212121",
              reason: "Comparison evidence is now available.",
              revokedAtUtc: "2026-01-22T12:00:00Z",
            },
          },
        ],
      },
    ],
    manualFindings: [
      {
        id: "22222222-aaaa-7222-8222-222222222222",
        submissionDocumentId: "77777777-7777-7777-8777-777777777777",
        canonicalDocumentId: "88888888-8888-7888-8888-888888888888",
        extractionAttemptId: "99999999-9999-7999-8999-999999999999",
        documentCode: "PFMEA",
        message: "The supplier signature does not match the submitted approval record.",
        evidenceNote: "Reviewed against the signed cover sheet supplied on 23 January.",
        createdByUserId: "23232323-2323-7323-8323-232323232323",
        createdAtUtc: "2026-01-23T09:30:00Z",
        waivers: [
          {
            id: "24242424-2424-7424-8424-242424242424",
            isActive: true,
            approvedByUserId: "25252525-2525-7525-8525-252525252525",
            reason: "Updated signature page accepted after supplier confirmation.",
            approvedAtUtc: "2026-01-24T10:00:00Z",
            revocation: null,
          },
        ],
      },
      {
        id: "26262626-2626-7626-8626-262626262626",
        submissionDocumentId: null,
        canonicalDocumentId: null,
        extractionAttemptId: null,
        documentCode: null,
        message: "Confirm the package-level approval date with the customer.",
        evidenceNote: null,
        createdByUserId: "27272727-2727-7727-8727-272727272727",
        createdAtUtc: "2026-01-23T10:30:00Z",
        waivers: null,
      },
    ],
  });

  await page.goto(`/ppap/reports/${reportId}`);

  await expect(page.getByRole("heading", { name: "Brake pedal assembly" })).toBeVisible();
  await expect(page.getByRole("main")).toHaveCount(1);
  await expect(page.getByText("BPA-1042 · Revision C")).toBeVisible();
  await expect(page.getByText(reportId, { exact: true })).toBeVisible();
  const summary = page.getByRole("region", { name: "Finding summary" });
  await expect(summary.getByText("Total", { exact: true }).locator("..")).toContainText("4");
  await expect(summary.getByText("Automated", { exact: true }).locator("..")).toContainText("2");
  await expect(summary.getByText("Manual", { exact: true }).locator("..")).toContainText("2");
  await expect(summary.getByText("Active waived", { exact: true }).locator("..")).toContainText("2");
  await expect(summary.getByText("Open", { exact: true }).locator("..")).toContainText("2");
  await expect(summary.getByText("Automated failed", { exact: true }).locator("..")).toContainText("1");
  await expect(summary.getByText("Automated not evaluated", { exact: true }).locator("..")).toContainText("1");
  await expect(summary.getByText("Automated error", { exact: true }).locator("..")).toContainText("0");

  await expect(page.getByText("PFMEA.SEVERITY.REQUIRED", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "PFMEA severity is recorded" })).toBeVisible();
  await expect(page.getByText("Record severity for every PFMEA row.")).toBeVisible();
  await expect(page.getByText("PACKAGE.CROSS_CHECK", { exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Automated findings" }).getByText("Cross-document", { exact: true })).toBeVisible();
  await expect(page.getByText("Canonical documents were not available for comparison.")).toBeVisible();

  const manualFindings = page.getByRole("region", { name: "Manual findings" });
  await expect(manualFindings.getByText("PFMEA document", { exact: true })).toBeVisible();
  await expect(manualFindings.getByText("Cross-document", { exact: true })).toBeVisible();
  await expect(manualFindings.getByText("The supplier signature does not match the submitted approval record.")).toBeVisible();
  await expect(manualFindings.getByText("Reviewed against the signed cover sheet supplied on 23 January.")).toBeVisible();
  await expect(manualFindings.getByText("No evidence note provided.")).toBeVisible();
  await expect(manualFindings.getByText("No waiver history.")).toBeVisible();
  await expect(manualFindings.locator('span[title="23232323-2323-7323-8323-232323232323"]')).toBeVisible();

  const activeHistory = page.getByText("Waiver history (1) for PFMEA.SEVERITY.REQUIRED", { exact: true });
  await expect(activeHistory.locator("..")).not.toHaveAttribute("open", "");
  await activeHistory.click();
  await expect(page.getByText("Active waiver", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("Supplier concession approved for this submission.")).toBeVisible();
  await expect(page.getByLabel("Waiver approver: 17171717-1717-7717-8717-171717171717")).toBeVisible();

  const revokedHistory = page.getByText("Waiver history (1) for PACKAGE.CROSS_CHECK", { exact: true });
  await revokedHistory.click();
  await expect(page.getByText("Revoked waiver", { exact: true })).toBeVisible();
  await expect(page.getByText("Temporary approval while source files were collected.")).toBeVisible();
  await expect(page.getByText("Comparison evidence is now available.")).toBeVisible();
  await expect(page.getByLabel("Waiver revoker: 21212121-2121-7121-8121-212121212121")).toBeVisible();

  const manualHistory = manualFindings.getByText("Waiver history (1) for manual finding", { exact: true });
  await manualHistory.click();
  await expect(manualFindings.getByText("Updated signature page accepted after supplier confirmation.")).toBeVisible();
  await expect(manualFindings.getByLabel("Waiver approver: 25252525-2525-7525-8525-252525252525")).toBeVisible();

  const manualFinding = manualFindings.getByRole("article").filter({ hasText: "The supplier signature" });
  await manualFinding.getByText("Finding metadata", { exact: true }).click();
  await expect(manualFinding.getByLabel("Manual finding: 22222222-aaaa-7222-8222-222222222222")).toBeVisible();
  await expect(manualFinding.getByLabel("Manual finding creator: 23232323-2323-7323-8323-232323232323")).toBeVisible();

  const reproducibility = page.getByRole("region", { name: "Reproducibility" });
  const manifestDetails = reproducibility.locator("details");
  await expect(manifestDetails).not.toHaveAttribute("open", "");
  await expect(reproducibility.getByText("AzureDocumentIntelligence")).not.toBeVisible();
  await reproducibility.getByText("Show details", { exact: true }).click();
  await expect(manifestDetails).toHaveAttribute("open", "");
  await expect(reproducibility.getByText("canonical-2.1", { exact: true })).toBeVisible();
  await expect(reproducibility.getByText("1", { exact: true })).toBeVisible();
  await expect(reproducibility.getByText("PFMEA", { exact: true })).toBeVisible();
  await expect(reproducibility.getByText("Submit · Available", { exact: true })).toBeVisible();
  await expect(reproducibility.getByText("AzureDocumentIntelligence", { exact: true })).toBeVisible();
  await expect(reproducibility.getByText("prebuilt-layout", { exact: true })).toBeVisible();
  await expect(reproducibility.getByText("pfmea-2.0", { exact: true })).toBeVisible();
  await expect(reproducibility.getByLabel("Manifest evaluation run: 11111111-1111-7111-8111-111111111111")).toBeVisible();
  await expect(reproducibility.getByLabel("PFMEA source SHA-256: d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2")).toBeVisible();
  await expect(reproducibility.getByLabel("PFMEA raw response artifact: 13131313-1313-7313-8313-131313131313")).toBeVisible();
  await expect(reproducibility.getByLabel("PFMEA generated workbook artifact: 14141414-1414-7414-8414-141414141414")).toBeVisible();
  await expect(page.getByText("SENSITIVE-NOT-APPLICABLE-REASON")).toHaveCount(0);

  const firstFinding = page.getByRole("article").filter({ hasText: "PFMEA.SEVERITY.REQUIRED" });
  await firstFinding.getByText("Evidence metadata", { exact: true }).click();
  await expect(firstFinding.getByText("11111111…1111")).toBeVisible();
  await expect(firstFinding.getByLabel("Finding: 66666666-6666-7666-8666-666666666666")).toBeVisible();
  await expect(firstFinding.getByLabel("Evaluation run: 11111111-1111-7111-8111-111111111111")).toBeVisible();
  await expect(firstFinding.getByLabel("Canonical document: 88888888-8888-7888-8888-888888888888")).toBeVisible();
  const trace = firstFinding.getByRole("textbox", { name: "Evaluation trace for PFMEA.SEVERITY.REQUIRED" });
  await expect(trace).toContainText("canonical-document");
  await trace.focus();
  await expect(trace).toBeFocused();
  await expect(page.getByRole("link", { name: "View submission" })).toHaveAttribute("href", `/ppap/submissions/${submissionId}`);
  await expect(page.getByText(/critical|major|minor/i)).toHaveCount(0);
});

test("renders a concise unavailable note for a historical null manifest", async ({ page }) => {
  await mockJsonResponse(page, `**/api/v1/ppap/reports/${reportId}`, {
    id: reportId,
    submissionId,
    jobId: "01960000-0000-7000-8000-000000000099",
    submissionVersion: 1,
    generatedAtUtc: "2025-01-20T10:01:00Z",
    partName: "Historical brake pedal",
    partNumber: "BPA-1000",
    drawingRevision: "A",
    availableDocumentCount: 0,
    notApplicableDocumentCount: 0,
    quarantinedDocumentCount: 0,
    findingCount: 0,
    automatedFindingCount: null,
    manualFindingCount: null,
    waivedFindingCount: null,
    openFindingCount: null,
    failedFindingCount: 0,
    notEvaluatedFindingCount: 0,
    errorFindingCount: 0,
    findings: [],
    manualFindings: null,
    reproducibilityManifest: null,
  });

  await page.goto(`/ppap/reports/${reportId}`);

  const summary = page.getByRole("region", { name: "Finding summary" });
  await expect(summary.getByText("Total", { exact: true }).locator("..")).toContainText("0");
  await expect(summary.getByText("Automated", { exact: true }).locator("..")).toContainText("0");
  await expect(summary.getByText("Manual", { exact: true }).locator("..")).toContainText("0");
  await expect(summary.getByText("Active waived", { exact: true }).locator("..")).toContainText("0");
  await expect(summary.getByText("Open", { exact: true }).locator("..")).toContainText("0");
  await expect(page.getByText("No automated findings in this report.")).toBeVisible();
  await expect(page.getByText("No manual findings in this report.")).toBeVisible();
  await expect(page.getByText("No waiver history.")).toHaveCount(0);

  const reproducibility = page.getByRole("region", { name: "Reproducibility" });
  await expect(reproducibility.getByText("Reproducibility details are unavailable for this historical report.")).toBeVisible();
  await expect(reproducibility.locator("details")).toHaveCount(0);
});
