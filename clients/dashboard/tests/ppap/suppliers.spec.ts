import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks, paged } from "../helpers/shell-mocks";

const PERMISSIONS = ["Permissions.Ppap.Suppliers.View", "Permissions.Ppap.Suppliers.Create", "Permissions.Ppap.Suppliers.Update"];

const supplier = {
  id: "01970000-0000-7000-8000-000000000101",
  code: "VND-000001",
  name: "Global Tech Automotive GmbH",
  contactName: "John Doe",
  contactJobTitle: "Quality Assurance Manager",
  email: "j.doe@supplier.com",
  phone: "+49 (0) 123 45678",
  address: "Main St 1",
  city: "Munich",
  country: "Germany",
  taxId: "DE123456789",
  dunsNumber: "123456789",
  siteName: "Plant 2",
  siteAddress: null,
  siteSameAsHeadquarters: true,
  isActive: true,
  userIds: [],
  partCount: 3,
  region: "Europe",
  tierLevel: "Tier 1",
  qualityGrade: "A",
  qualityScore: 94,
  activePpapCount: 2,
  certificates: [{ fileAssetId: "01970000-0000-7000-8000-000000000201", fileName: "iatf-16949.pdf", certificateType: "IATF 16949", addedAtUtc: "2026-10-01T00:00:00Z" }],
  createdAtUtc: "2026-10-01T00:00:00Z",
  updatedAtUtc: "2026-10-01T00:00:00Z",
  version: 1,
};

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
  await mockJsonResponse(page, "**/api/v1/identity/permissions", PERMISSIONS);
  await mockJsonResponse(page, "**/api/v1/ppap/suppliers?**", paged([supplier], { pageSize: 10 }));
  await mockJsonResponse(page, "**/api/v1/ppap/suppliers/summary", { totalSuppliers: 1248, activeSuppliers: 1200, newThisMonth: 48, newLastMonth: 30, averageQualityScore: 88.2, activePpaps: 422, regionCount: 5, criticalIssues: 14 });
});

test("lists the supplier directory with score, status, contact, and stats", async ({ page }) => {
  test.slow();
  await page.goto("/ppap/suppliers");

  await expect(page.getByRole("heading", { name: "Supplier Directory" })).toBeVisible({ timeout: 30_000 });
  const row = page.getByRole("row").filter({ hasText: "VND-000001" });
  await expect(row).toContainText("Global Tech Automotive GmbH");
  await expect(row).toContainText("Munich, Germany");
  await expect(row).toContainText("Tier 1 · Grade A");
  await expect(row).toContainText("Active");
  await expect(row).toContainText("John Doe");
  await expect(row.getByLabel("Global Tech Automotive GmbH overall score: 94%")).toBeVisible();
  await expect(page.getByText("Showing 1 to 1 of 1 suppliers")).toBeVisible();

  await expect(page.getByLabel("Total Suppliers")).toContainText("1,248");
  await expect(page.getByLabel("Total Suppliers")).toContainText("+4% from last month");
  await expect(page.getByLabel("Average Quality Score")).toContainText("88.2%");
  await expect(page.getByLabel("Active PPAPs")).toContainText("Across 5 regions");
  await expect(page.getByLabel("Critical Issues")).toContainText("Requires immediate attention");

  await page.getByRole("link", { name: "Add New Supplier" }).click();
  await expect(page).toHaveURL(/\/ppap\/suppliers\/new$/);
  await expect(page.getByRole("heading", { name: "Onboard New Supplier" })).toBeVisible({ timeout: 30_000 });
});

test("filters suppliers by region, tier, and grade", async ({ page }) => {
  test.slow();
  await page.goto("/ppap/suppliers");
  await expect(page.getByRole("heading", { name: "Supplier Directory" })).toBeVisible({ timeout: 30_000 });

  const regionRequest = page.waitForRequest((r) => r.url().includes("/api/v1/ppap/suppliers?") && r.url().includes("Region=Europe"));
  await page.getByRole("button", { name: /All Regions/ }).click();
  await page.getByRole("menuitemradio", { name: "Europe" }).click();
  await regionRequest;

  const tierRequest = page.waitForRequest((r) => r.url().includes("TierLevel=Tier+1"));
  await page.getByRole("button", { name: /Tier Level/ }).click();
  await page.getByRole("menuitemradio", { name: "Tier 1" }).click();
  await tierRequest;

  await expect(page).toHaveURL(/region=Europe/);
  await expect(page).toHaveURL(/tier=Tier\+1/);
});

test("onboards a supplier with an auto-generated vendor code", async ({ page }) => {
  test.slow();
  await mockJsonResponse(page, "**/api/v1/ppap/suppliers", { ...supplier, id: "01970000-0000-7000-8000-000000000102", code: "VND-000002", certificates: [] }, { method: "POST", status: 201 });
  await page.goto("/ppap/suppliers/new");

  await expect(page.getByRole("heading", { name: "Onboard New Supplier" })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("AUTO-PENDING")).toBeVisible();

  await page.getByRole("button", { name: "Create Supplier Profile" }).click();
  await expect(page.getByText("Legal entity name is required.")).toBeVisible();

  await page.getByLabel("Legal Entity Name").fill("Global Tech Automotive GmbH");
  await page.getByLabel("Tax ID / VAT Number").fill("DE123456789");
  await page.getByLabel("DUNS Number").fill("1234");
  await page.getByRole("button", { name: "Create Supplier Profile" }).click();
  await expect(page.getByText("Use 9 digits, e.g. 00-000-0000.")).toBeVisible();
  await expect(page.locator("li[data-state='error']")).toContainText("DUNS Validation");

  await page.getByLabel("DUNS Number").fill("123456789");
  await page.getByLabel("DUNS Number").blur();
  await expect(page.getByLabel("DUNS Number")).toHaveValue("12-345-6789");
  await page.getByLabel("Street Address", { exact: true }).fill("Main St 1");
  await page.getByLabel("City").fill("Munich");
  await page.getByLabel("Country").fill("Germany");
  await page.getByLabel("Site Name or Plant Number").fill("Plant 2");
  await page.getByLabel("Same as Headquarters").check();
  await expect(page.getByLabel("Site Street Address")).toBeDisabled();
  await expect(page.getByLabel("Site location")).toContainText("Munich, Germany");
  await page.getByLabel("Full Name").fill("John Doe");
  await page.getByLabel("Job Title").fill("Quality Assurance Manager");
  await page.getByLabel("Email Address").fill("j.doe@supplier.com");
  await page.getByLabel("Phone Number").fill("+49 (0) 123 45678");

  const request = page.waitForRequest((r) => r.url().endsWith("/api/v1/ppap/suppliers") && r.method() === "POST");
  await page.getByRole("button", { name: "Create Supplier Profile" }).click();

  expect((await request).postDataJSON()).toEqual({
    profile: {
      name: "Global Tech Automotive GmbH",
      taxId: "DE123456789",
      dunsNumber: "12-345-6789",
      address: "Main St 1",
      city: "Munich",
      country: "Germany",
      siteName: "Plant 2",
      siteAddress: null,
      siteSameAsHeadquarters: true,
      contactName: "John Doe",
      contactJobTitle: "Quality Assurance Manager",
      email: "j.doe@supplier.com",
      phone: "+49 (0) 123 45678",
      tierLevel: null,
      qualityGrade: null,
    },
    userIds: [],
    certificates: [],
  });
  await expect(page).toHaveURL(/\/ppap\/suppliers$/);
});

test("edits an existing supplier profile and keeps its certificates", async ({ page }) => {
  test.slow();
  await mockJsonResponse(page, `**/api/v1/ppap/suppliers/${supplier.id}`, supplier);
  await page.goto(`/ppap/suppliers/${supplier.id}`);

  await expect(page.getByRole("heading", { name: "Edit Supplier Profile" })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByLabel("DUNS Number")).toHaveValue("12-345-6789");
  await expect(page.getByLabel("Uploaded certificates")).toContainText("iatf-16949.pdf");
  await expect(page.locator("li[data-state='ok']").filter({ hasText: "IATF Certificate" })).toBeVisible();

  await page.getByLabel("Job Title").fill("Supplier Quality Lead");
  await page.getByLabel("iatf-16949.pdf certificate type").selectOption("ISO 9001");

  await page.route(`**/api/v1/ppap/suppliers/${supplier.id}`, async (route) => {
    if (route.request().method() !== "PUT") return route.fallback();
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ ...supplier, version: 2 }) });
  });
  const request = page.waitForRequest((r) => r.url().endsWith(`/api/v1/ppap/suppliers/${supplier.id}`) && r.method() === "PUT");
  await page.getByRole("button", { name: "Save Changes" }).click();

  const body = (await request).postDataJSON();
  expect(body.expectedVersion).toBe(1);
  expect(body.code).toBe("VND-000001");
  expect(body.profile.contactJobTitle).toBe("Supplier Quality Lead");
  expect(body.certificates).toEqual([{ fileAssetId: supplier.certificates[0].fileAssetId, fileName: "iatf-16949.pdf", certificateType: "ISO 9001" }]);
});
