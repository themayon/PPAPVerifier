import { expect, test } from "@playwright/test";
import { mockJsonResponse, mockProblemDetails } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks, paged } from "../helpers/shell-mocks";

const submission = {
  id: "01960000-0000-7000-8000-000000000001",
  partName: "Steering Knuckle",
  partNumber: "SK-2048",
  drawingRevision: "C",
  ppapLevel: 3,
  ruleTemplateId: null,
  ruleTemplateVersionId: null,
  status: "Draft",
  version: 1,
  createdAtUtc: "2026-01-20T10:00:00Z",
  updatedAtUtc: "2026-01-21T12:30:00Z",
  submittedAtUtc: null,
  cancelledAtUtc: null,
};

async function installPpapMocks(page: Parameters<typeof installShellMocks>[0]) {
  await mockJsonResponse(page, "**/api/v1/ppap/credits/balance", {
    availableCredits: 84,
    reservedCredits: 6,
  });
  await mockJsonResponse(page, "**/api/v1/ppap/submissions?**", paged([submission], { pageSize: 5 }));
  await mockJsonResponse(page, "**/api/v1/rules/templates?**", paged([
    {
      id: "01960000-0000-7000-8000-000000000010",
      stableCode: "STANDARD-PPAP",
      name: "Standard PPAP",
    },
  ], { pageSize: 100 }));
  await mockJsonResponse(
    page,
    "**/api/v1/ppap/credits/topup-requests/me?**",
    paged([
      {
        id: "01960000-0000-7000-8000-000000000002",
        ownerUserId: TEST_USER.sub,
        creditsRequested: 10,
        paymentReference: null,
        note: null,
        status: "Pending",
        requestedAt: "2026-01-22T09:00:00Z",
        decidedAt: null,
        decidedByUserId: null,
        decisionNote: null,
      },
    ], { pageSize: 3 }),
  );
  await mockJsonResponse(page, "**/api/v1/ppap/submissions", '"01960000-0000-7000-8000-000000000003"', { method: "POST", status: 201 });
  await mockJsonResponse(page, "**/api/v1/ppap/credits/topup-requests", '"01960000-0000-7000-8000-000000000004"', { method: "POST", status: 201 });
}

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
});

test("uses the PPAP workspace as the authenticated landing page", async ({ page }) => {
  await installPpapMocks(page);
  await page.goto("/");

  await expect(page).toHaveURL(/\/ppap$/);
  await expect(page.getByRole("heading", { name: "Production Part Approval Process" })).toBeVisible();
});

test("renders supplier data and sends draft and package requests", async ({ page }) => {
  await installPpapMocks(page);
  await page.goto("/ppap");

  await expect(page.getByRole("heading", { name: "Production Part Approval Process" })).toBeVisible();
  await expect(page.getByRole("main")).toHaveCount(1);
  const creditBalance = page.getByRole("article", { name: "Credit balance" });
  await expect(creditBalance.getByText("84", { exact: true })).toBeVisible();
  await expect(creditBalance.getByText("6", { exact: true })).toBeVisible();
  const recentSubmission = page.getByRole("link", { name: /Steering Knuckle/ });
  await expect(recentSubmission).toBeVisible();
  await expect(recentSubmission).toHaveAttribute("href", `/ppap/submissions/${submission.id}`);
  await expect(page.getByText("SK-2048 · Revision C · Level 3")).toBeVisible();
  await recentSubmission.focus();
  await page.keyboard.press("Shift+Tab");
  await page.keyboard.press("Tab");
  await expect(recentSubmission).toBeFocused();
  expect(await recentSubmission.evaluate((element) => {
    const style = getComputedStyle(element);
    return style.outlineStyle !== "none" && parseFloat(style.outlineWidth) > 0;
  })).toBe(true);
  await page.setViewportSize({ width: 390, height: 844 });
  const timestamp = recentSubmission.locator("time");
  await expect(timestamp).toBeVisible();
  const timestampBox = await timestamp.boundingBox();
  expect(timestampBox).not.toBeNull();
  expect(timestampBox!.x + timestampBox!.width).toBeLessThanOrEqual(390);

  await page.getByRole("button", { name: "Create draft" }).click();
  await page.getByLabel("Part name").fill("Brake Caliper");
  await page.getByLabel("Part number").fill("BC-900");
  await page.getByLabel("Revision").fill("B2");
  await page.getByLabel("PPAP level").selectOption("4");
  const draftRequest = page.waitForRequest((request) =>
    request.url().endsWith("/api/v1/ppap/submissions") && request.method() === "POST",
  );
  await page.getByRole("button", { name: "Create draft", exact: true }).last().click();
  expect((await draftRequest).postDataJSON()).toEqual({
    partName: "Brake Caliper",
    partNumber: "BC-900",
    drawingRevision: "B2",
    ppapLevel: 4,
    ruleTemplateId: null,
  });
  await expect(page).toHaveURL("/ppap/submissions/01960000-0000-7000-8000-000000000003");
  await page.goto("/ppap");

  const topupRequest = page.waitForRequest((request) =>
    request.url().endsWith("/api/v1/ppap/credits/topup-requests") && request.method() === "POST",
  );
  await page.getByRole("button", { name: "Request 50 credit package" }).click();
  expect((await topupRequest).postDataJSON()).toEqual({
    creditsRequested: 50,
    paymentReference: null,
    note: null,
  });
  await expect(page.getByText("Credits are added only after an operator reviews and approves the request.")).toBeVisible();
});

test("creates a draft from a published checklist template", async ({ page }) => {
  await installPpapMocks(page);
  await page.goto("/ppap");

  await page.getByRole("button", { name: "Create draft" }).click();
  await expect(page.getByLabel("Checklist template")).toContainText("Standard PPAP (STANDARD-PPAP)");
  await page.getByLabel("Part name").fill("Brake Caliper");
  await page.getByLabel("Part number").fill("BC-900");
  await page.getByLabel("Revision").fill("B2");
  await page.getByLabel("Checklist template").selectOption("01960000-0000-7000-8000-000000000010");
  const draftRequest = page.waitForRequest((request) =>
    request.url().endsWith("/api/v1/ppap/submissions") && request.method() === "POST",
  );

  await page.getByRole("button", { name: "Create draft", exact: true }).last().click();

  expect((await draftRequest).postDataJSON()).toMatchObject({
    ruleTemplateId: "01960000-0000-7000-8000-000000000010",
  });
});

test("shows empty submissions and a load error", async ({ page }) => {
  await mockJsonResponse(page, "**/api/v1/ppap/credits/balance", {
    availableCredits: 0,
    reservedCredits: 0,
  });
  await mockJsonResponse(page, "**/api/v1/ppap/submissions?**", paged([], { pageSize: 5 }));
  await mockProblemDetails(page, "**/api/v1/ppap/credits/topup-requests/me?**", 500, {
    detail: "Top-up history is temporarily unavailable.",
  });

  await page.goto("/ppap");

  await expect(page.getByRole("heading", { name: "No PPAP submissions yet" })).toBeVisible();
  await expect(page.getByRole("alert")).toContainText("Top-up history is temporarily unavailable.");
});
