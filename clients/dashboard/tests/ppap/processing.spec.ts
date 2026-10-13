import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks } from "../helpers/shell-mocks";

const jobId = "01960000-0000-7000-8000-000000000099";
const submissionId = "01960000-0000-7000-8000-000000000001";
const reportId = "01960000-0000-7000-8000-000000000088";

function job(status: string, overrides: Record<string, unknown> = {}) {
  return {
    id: jobId,
    submissionId,
    submissionVersion: 7,
    status,
    currentStage: "CrossValidation",
    progressPercent: 55,
    createdAtUtc: new Date().toISOString(),
    startedAtUtc: new Date().toISOString(),
    completedAtUtc: null,
    failedAtUtc: null,
    errorCode: null,
    errorMessage: null,
    reportId: null,
    ...overrides,
  };
}

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
});

test("polls an active job until it completes", async ({ page }) => {
  let requests = 0;
  await page.route(`**/api/v1/ppap/jobs/${jobId}`, async (route) => {
    requests += 1;
    const body = requests === 1
      ? job("Processing")
      : job("Completed", {
        currentStage: "NotificationDelivery",
        progressPercent: 100,
        completedAtUtc: "2026-01-20T10:01:00Z",
        reportId,
      });
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(body),
    });
  });

  await page.goto(`/ppap/processing/${jobId}`);
  await expect(page.getByRole("heading", { name: "Your package is being processed" })).toBeVisible();
  await expect(page.getByRole("main")).toHaveCount(1);
  const progress = page.getByRole("progressbar", { name: "PPAP processing progress" });
  await expect(progress).toHaveAttribute("aria-valuemin", "0");
  await expect(progress).toHaveAttribute("aria-valuemax", "100");
  await expect(progress).toHaveAttribute("aria-valuenow", "55");
  const announcement = page.locator('.ppap-processing-page [role="status"]');
  await expect(announcement).toHaveAttribute("aria-live", "polite");
  await expect(announcement).toContainText("Processing status: Processing");
  await expect(page.getByRole("heading", { name: "Processing complete" })).toBeVisible({ timeout: 7000 });
  await expect(progress).toHaveAttribute("aria-valuenow", "100");
  await expect(announcement).toContainText("Processing status: Completed");
  await expect(page.getByText("100%", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "View report" })).toHaveAttribute("href", `/ppap/reports/${reportId}`);
  expect(requests).toBeGreaterThanOrEqual(2);
});

test("stops polling a stale active job and offers manual refresh", async ({ page }) => {
  let requests = 0;
  await page.route(`**/api/v1/ppap/jobs/${jobId}`, async (route) => {
    requests += 1;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(job("Processing", {
        createdAtUtc: new Date(Date.now() - 16 * 60 * 1000).toISOString(),
      })),
    });
  });

  await page.goto(`/ppap/processing/${jobId}`);
  await expect(page.getByRole("alert")).toContainText("Processing is taking longer than expected.");
  await page.waitForTimeout(2500);
  expect(requests).toBe(1);
  await page.getByRole("button", { name: "Refresh" }).click();
  await expect.poll(() => requests).toBe(2);
});

test("renders failure details and recovery links", async ({ page }) => {
  await mockJsonResponse(page, `**/api/v1/ppap/jobs/${jobId}`, job("Failed", {
    progressPercent: 55,
    failedAtUtc: "2026-01-20T10:00:30Z",
    errorCode: "DOCUMENT_INVALID",
    errorMessage: "The control plan could not be validated.",
  }));

  await page.goto(`/ppap/processing/${jobId}`);

  await expect(page.getByRole("heading", { name: "Processing stopped" })).toBeVisible();
  await expect(page.getByRole("alert")).toContainText("DOCUMENT_INVALID");
  await expect(page.getByRole("alert")).toContainText("The control plan could not be validated.");
  await expect(page.getByRole("link", { name: "PPAP home" })).toHaveAttribute("href", "/ppap");
  await expect(page.getByRole("link", { name: "View submission" })).toHaveAttribute(
    "href",
    `/ppap/submissions/${submissionId}`,
  );
});
