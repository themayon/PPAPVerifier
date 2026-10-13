import { expect, test } from "@playwright/test";
import { seedAuthedSession, TEST_USER } from "./helpers/auth-seed";
import { ADMIN_PERMS, installAdminShellMocks, paged } from "./helpers/shell-mocks";

const PERMISSION = "Permissions.DocumentIntelligence.Attempts.View";
const ATTEMPT_ID = "01970000-0000-7000-8000-000000000001";
const OWNER_ID = "01970000-0000-7000-8000-000000000002";
const ATTEMPT = {
  id: ATTEMPT_ID,
  ownerUserId: OWNER_ID,
  submissionDocumentId: "01970000-0000-7000-8000-000000000003",
  sourceFileAssetId: "01970000-0000-7000-8000-000000000004",
  documentCode: "PSW",
  sourceSha256: "7a1ee9d68f4bd06c",
  provider: "AzureDocumentIntelligence",
  model: "ppap-psw-v2",
  extractorVersion: "2.4.1",
  status: "DeadLettered",
  attemptCount: 5,
  errorCode: "EXTRACTION_RETRIES_EXHAUSTED",
  errorMessage: "Provider remained unavailable after the final extraction attempt.",
  canonicalDocumentId: "01970000-0000-7000-8000-000000000005",
  rawResponseFileAssetId: "01970000-0000-7000-8000-000000000006",
  generatedExcelFileAssetId: "01970000-0000-7000-8000-000000000007",
  createdAtUtc: "2026-06-10T09:30:00Z",
  claimedAtUtc: "2026-06-10T09:31:00Z",
  claimedUntilUtc: "2026-06-10T09:36:00Z",
  submittedAtUtc: "2026-06-10T09:31:30Z",
  startedAtUtc: "2026-06-10T09:32:00Z",
  completedAtUtc: null,
  updatedAtUtc: "2026-06-10T09:40:00Z",
  completionEventPublishedAtUtc: null,
  failureEventPublishedAtUtc: "2026-06-10T09:41:00Z",
};

async function installAttemptMocks(page: import("@playwright/test").Page) {
  await page.route(`**/api/v1/document-intelligence/operations/attempts/${ATTEMPT_ID}`, async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(ATTEMPT) });
  });
  await page.route("**/api/v1/document-intelligence/operations/attempts?**", async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(paged([ATTEMPT])) });
  });
}

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, { ...TEST_USER, tenant: "oem-acme", permissions: [...ADMIN_PERMS] });
  await installAdminShellMocks(page);
  await installAttemptMocks(page);
});

test.describe("operator extraction attempts", () => {
  test("sends a tenant-scoped search with PascalCase pagination and sorting", async ({ page }) => {
    const requestPromise = page.waitForRequest((request) =>
      new URL(request.url()).pathname === "/api/v1/document-intelligence/operations/attempts",
    );

    await page.goto("/extraction-attempts");
    const request = await requestPromise;
    const url = new URL(request.url());

    expect(request.headers().tenant).toBe("oem-acme");
    expect(url.searchParams.get("PageNumber")).toBe("1");
    expect(url.searchParams.get("PageSize")).toBe("20");
    expect(url.searchParams.get("Sort")).toBe("-createdAt");
    expect(url.searchParams.has("pageNumber")).toBe(false);
    await expect(page.getByRole("heading", { name: "Extraction attempts" })).toBeVisible();
  });

  test("exposes table sorting semantics and supports keyboard sorting", async ({ page }) => {
    await page.goto("/extraction-attempts");

    const table = page.getByRole("table", { name: "Extraction attempts" });
    const created = table.getByRole("columnheader", { name: "Created" });
    const owner = table.getByRole("columnheader", { name: "Owner" });
    await expect(created).toHaveAttribute("scope", "col");
    await expect(created).toHaveAttribute("aria-sort", "descending");
    await expect(owner).not.toHaveAttribute("aria-sort");

    const ownerSort = owner.getByRole("button", { name: "Owner" });
    await ownerSort.focus();
    await expect(ownerSort).toBeFocused();
    await ownerSort.press("Enter");
    await expect(owner).toHaveAttribute("aria-sort", "ascending");
    await expect(created).not.toHaveAttribute("aria-sort");
    await expect(page.getByRole("status").filter({ hasText: "Showing 1 of 1 attempts" })).toHaveAttribute("aria-live", "polite");
  });

  test("shows dead-letter errors and artifact and timestamp details without replay controls", async ({ page }) => {
    await page.goto("/extraction-attempts");

    await expect(page.getByText("DeadLettered", { exact: true })).toBeVisible();
    await expect(page.getByText("EXTRACTION_RETRIES_EXHAUSTED", { exact: true })).toBeVisible();
    await expect(page.getByText(ATTEMPT.errorMessage, { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /replay/i })).toHaveCount(0);

    await page.getByRole("button", { name: `View details for attempt ${ATTEMPT_ID}` }).click();

    await expect(page.getByRole("heading", { name: "Extraction attempt details" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Failure details" })).toBeVisible();
    await expect(page.getByText(ATTEMPT.rawResponseFileAssetId, { exact: true })).toBeVisible();
    await expect(page.getByText(ATTEMPT.generatedExcelFileAssetId, { exact: true })).toBeVisible();
    await expect(page.getByText("2026-06-10T09:41:00.000Z", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /replay/i })).toHaveCount(0);
  });

  test("applies status, document code, and owner filters with PascalCase query names", async ({ page }) => {
    const requests: URL[] = [];
    await page.route("**/api/v1/document-intelligence/operations/attempts?**", async (route) => {
      requests.push(new URL(route.request().url()));
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(paged([ATTEMPT])) });
    });
    await page.goto("/extraction-attempts");

    await page.getByLabel("Status").click();
    await page.getByRole("menuitem", { name: "DeadLettered" }).click();
    await page.getByLabel("Document code").fill("PSW");
    await page.getByLabel("Owner user ID").fill(OWNER_ID);
    await page.getByRole("button", { name: "Apply filters" }).click();

    await expect.poll(() => requests.length).toBeGreaterThan(1);
    const filtered = requests.at(-1)!;
    expect(filtered.searchParams.get("Status")).toBe("DeadLettered");
    expect(filtered.searchParams.get("DocumentCode")).toBe("PSW");
    expect(filtered.searchParams.get("OwnerUserId")).toBe(OWNER_ID);
    expect(filtered.searchParams.get("PageNumber")).toBe("1");
  });

  test("gates the route and navigation item when permission is missing", async ({ page }) => {
    const permissions = ADMIN_PERMS.filter((permission) => permission !== PERMISSION);
    await seedAuthedSession(page, { ...TEST_USER, tenant: "oem-acme", permissions: [...permissions] });
    await installAdminShellMocks(page, permissions);

    await page.goto("/extraction-attempts");

    await expect(page.getByText("// 403 · access denied")).toBeVisible();
    await expect(page.getByText(PERMISSION, { exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "Extraction attempts" })).toHaveCount(0);
  });
});
