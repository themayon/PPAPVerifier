import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { ADMIN_PERMS, installAdminShellMocks, paged } from "../helpers/shell-mocks";

const SUMMARY = {
  totalSubmissions: 42,
  queuedJobs: 3,
  processingJobs: 2,
  completedJobs: 34,
  failedJobs: 3,
  pendingTopupRequests: 1,
  totalAvailableCredits: 1250,
  totalReservedCredits: 75,
};

const JOBS = paged([
  {
    id: "0196f000-0000-7000-8000-000000000001",
    submissionId: "0196f000-0000-7000-8000-000000000002",
    submissionVersion: 2,
    partName: "Front brake bracket",
    partNumber: "BRK-2048",
    ownerUserId: "0196f000-0000-7000-8000-000000000003",
    status: "Completed",
    currentStage: "CrossValidation",
    progressPercent: 100,
    createdAtUtc: "2026-05-10T09:30:00Z",
    startedAtUtc: "2026-05-10T09:31:00Z",
    completedAtUtc: null,
    failedAtUtc: null,
    errorCode: null,
    errorMessage: null,
    reportId: "0196f000-0000-7000-8000-000000000007",
  },
  {
    id: "0196f000-0000-7000-8000-000000000004",
    submissionId: "0196f000-0000-7000-8000-000000000005",
    submissionVersion: 1,
    partName: "Steering knuckle",
    partNumber: "STK-1001",
    ownerUserId: "0196f000-0000-7000-8000-000000000006",
    status: "Failed",
    currentStage: "DocumentClassification",
    progressPercent: 35,
    createdAtUtc: "2026-05-09T08:00:00Z",
    startedAtUtc: "2026-05-09T08:01:00Z",
    completedAtUtc: null,
    failedAtUtc: "2026-05-09T08:04:00Z",
    errorCode: "DOC-INVALID",
    errorMessage: "Required dimensional result could not be classified.",
    reportId: null,
  },
]);

const TOPUP = {
  id: "0196f000-0000-7000-8000-000000000010",
  ownerUserId: "0196f000-0000-7000-8000-000000000003",
  creditsRequested: 500,
  paymentReference: "PO-7781",
  note: "Quarterly processing allocation",
  status: "Pending",
  requestedAt: "2026-05-11T10:00:00Z",
  decidedAt: null,
  decidedByUserId: null,
  decisionNote: null,
};

async function installPpapMocks(page: import("@playwright/test").Page) {
  await mockJsonResponse(page, "**/api/v1/ppap/operations/summary", SUMMARY);
  await mockJsonResponse(page, "**/api/v1/ppap/operations/jobs?**", JOBS);
  await mockJsonResponse(page, "**/api/v1/ppap/credits/topup-requests?**", paged([TOPUP]));
}

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, { ...TEST_USER, tenant: "oem-acme", permissions: [...ADMIN_PERMS] });
  await installAdminShellMocks(page);
  await installPpapMocks(page);
});

test.describe("PPAP operations", () => {
  test("renders tenant KPIs and live jobs", async ({ page }) => {
    await page.goto("/ppap");

    await expect(page.getByRole("heading", { name: "PPAP operations" })).toBeVisible();
    await expect(page.getByText("42", { exact: true })).toBeVisible();
    await expect(page.getByText("1,250", { exact: true })).toBeVisible();
    await expect(page.getByText("Front brake bracket")).toBeVisible();
    await expect(page.getByText("CrossValidation")).toBeVisible();
    await expect(page.getByText("Available", { exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "Review findings" })).toHaveAttribute("href", `/ppap/reports/${JOBS.items[0].reportId}/review`);
    await expect(page.getByText("DOC-INVALID: Required dimensional result could not be classified.")).toBeVisible();
    await expect(page.getByText("500", { exact: true })).toBeVisible();
    const jobsTable = page.getByRole("table", { name: "Live PPAP jobs" });
    await expect(jobsTable).toBeVisible();
    await expect(page.getByRole("table", { name: "Pending credit top-ups" })).toBeVisible();
    await expect(jobsTable.getByRole("columnheader").first()).toHaveAttribute("scope", "col");
    await expect(jobsTable.getByRole("progressbar", { name: "Front brake bracket progress" })).toHaveAttribute("aria-valuenow", "100");
    await expect(page.getByRole("status").filter({ hasText: "Showing 2 of 2 jobs" })).toHaveAttribute("aria-live", "polite");
  });

  test("approves a top-up with the decision note and refreshes operations data", async ({ page }) => {
    let requestBody: unknown;
    let topupSearches = 0;
    await page.route("**/api/v1/ppap/credits/topup-requests?**", async (route) => {
      topupSearches += 1;
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(paged([TOPUP])) });
    });
    await page.route(`**/api/v1/ppap/credits/topup-requests/${TOPUP.id}/approve`, async (route) => {
      requestBody = route.request().postDataJSON();
      await route.fulfill({ status: 204 });
    });

    await page.goto("/ppap");
    await page.getByRole("button", { name: "Approve" }).click();
    const decisionNote = page.getByLabel("Decision note");
    await decisionNote.fill("x".repeat(513));
    await page.getByRole("button", { name: "Approve request" }).click();
    await expect(decisionNote).toHaveAttribute("aria-invalid", "true");
    await expect(decisionNote).toHaveAttribute("aria-describedby", "decision-note-error");
    await expect(page.locator("#decision-note-error")).toHaveText("Decision note must be 512 characters or fewer.");
    await decisionNote.fill("Payment verified");
    await page.getByRole("button", { name: "Approve request" }).click();

    await expect.poll(() => requestBody).toEqual({ decisionNote: "Payment verified" });
    await expect.poll(() => topupSearches).toBeGreaterThan(1);
    await expect(page.getByText("Top-up approved")).toBeVisible();
  });

  test("forbids the route when one required permission is missing", async ({ page }) => {
    const permissions = ADMIN_PERMS.filter((permission) => permission !== "Permissions.Ppap.Credits.Manage");
    await seedAuthedSession(page, { ...TEST_USER, tenant: "oem-acme", permissions: [...permissions] });
    await installAdminShellMocks(page, permissions);

    await page.goto("/ppap");

    await expect(page.getByText("// 403 · access denied")).toBeVisible();
    await expect(page.getByText("Permissions.Ppap.Credits.Manage", { exact: true })).toBeVisible();
  });
});
