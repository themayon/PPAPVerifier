import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks, paged } from "../helpers/shell-mocks";

const PERMISSIONS = [
  "Permissions.Ppap.Parts.View",
  "Permissions.Ppap.Parts.Create",
  "Permissions.Ppap.Parts.Update",
  "Permissions.Ppap.Parts.AssignTemplate",
  "Permissions.Ppap.Suppliers.View",
  "Permissions.Ppap.Submissions.Create",
  "Permissions.Ppap.Submissions.Cancel",
  "Permissions.Ppap.Submissions.Approve",
  "Permissions.Ppap.Findings.View",
];

const TEMPLATE_ID = "01970000-0000-7000-8000-000000000010";
const supplier = {
  id: "01970000-0000-7000-8000-000000000101",
  code: "VND-000001",
  name: "Apex Automotive Solutions",
  contactName: "Marcus Thorne",
  contactJobTitle: "Account Director",
  email: "m.thorne@apex-solutions.com",
  phone: "+1 (555) 902-3341",
  city: "Detroit",
  country: "USA",
  region: "North America",
  tierLevel: "Tier 1",
  qualityGrade: "A+",
  qualityScore: 98.4,
  activePpapCount: 2,
  isActive: true,
  userIds: [],
  partCount: 4,
  createdAtUtc: "2026-10-01T00:00:00Z",
  updatedAtUtc: "2026-10-01T00:00:00Z",
  version: 1,
};

const base = {
  drawingRevision: "B",
  ruleTemplateId: TEMPLATE_ID,
  ruleTemplateVersionId: "01970000-0000-7000-8000-000000000011",
  isActive: true,
  version: 1,
  createdAtUtc: "2026-09-15T10:00:00Z",
  updatedAtUtc: "2026-09-15T10:00:00Z",
  supplierId: supplier.id,
  ppapLevel: 3,
  classification: "Standard",
  canEdit: true,
  supplier: { id: supplier.id, code: supplier.code, name: supplier.name, city: "Detroit", country: "USA", isActive: true },
};
const submission = (overrides: Record<string, unknown>) => ({
  id: "01970000-0000-7000-8000-000000000201",
  status: "Submitted",
  decision: "None",
  ppapLevel: 3,
  totalElements: 18,
  completedElements: 14,
  latestReportId: "01970000-0000-7000-8000-000000000301",
  openFindingCount: 0,
  isOwner: true,
  version: 3,
  createdAtUtc: "2026-10-01T00:00:00Z",
  submittedAtUtc: "2026-10-02T00:00:00Z",
  decidedAtUtc: null,
  ...overrides,
});

const notStarted = { ...base, id: "01970000-0000-7000-8000-000000000001", partNumber: "55042-T-A1", partName: "Fuel Rail Assembly", latestSubmission: null };
const draft = { ...base, id: "01970000-0000-7000-8000-000000000002", partNumber: "34055-L-X8", partName: "Lower Intake Manifold", latestSubmission: submission({ id: "01970000-0000-7000-8000-000000000202", status: "Draft", completedElements: 12, latestReportId: null }) };
const submitted = { ...base, id: "01970000-0000-7000-8000-000000000003", partNumber: "12004-R-P0", partName: "ECU Controller Bracket", ppapLevel: 2, classification: "Safety", latestSubmission: submission({ ppapLevel: 2, completedElements: 4, openFindingCount: 3, isOwner: false }) };
const approved = { ...base, id: "01970000-0000-7000-8000-000000000004", partNumber: "88291-K-02", partName: "Brake Caliper Housing", ppapLevel: 5, latestSubmission: submission({ id: "01970000-0000-7000-8000-000000000204", ppapLevel: 5, decision: "Approved", completedElements: 18 }) };

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
  await mockJsonResponse(page, "**/api/v1/identity/permissions", PERMISSIONS);
  await mockJsonResponse(page, "**/api/v1/ppap/parts?**", paged([submitted, approved, draft, notStarted], { pageSize: 10 }));
  await mockJsonResponse(page, "**/api/v1/ppap/parts/summary", { totalParts: 4, activeSubmissions: 248, pendingReview: 42, approvalsThisMonth: 164, approvalsLastMonth: 150, newSubmissionsThisMonth: 112, newSubmissionsLastMonth: 100 });
  await mockJsonResponse(page, "**/api/v1/ppap/suppliers?**", paged([supplier], { pageSize: 200 }));
  await mockJsonResponse(page, "**/api/v1/rules/templates?**", paged([{ id: TEMPLATE_ID, stableCode: "STANDARD-PPAP", name: "Standard PPAP" }], { pageSize: 100 }));
});

test("lists parts with supplier, level, elements progress, status, and stats", async ({ page }) => {
  test.slow();
  await page.goto("/ppap/parts");

  await expect(page.getByRole("heading", { name: "Part Submissions Directory" })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByLabel("Total Active Submissions")).toContainText("248");
  await expect(page.getByLabel("Total Active Submissions")).toContainText("+12% vs last month");
  await expect(page.getByLabel("Pending OEM Review")).toContainText("Action Required");
  await expect(page.getByLabel("Approvals This Month")).toContainText("164");

  const rows = page.getByRole("row");
  await expect(rows.filter({ hasText: "12004-R-P0" })).toContainText("Submitted");
  await expect(rows.filter({ hasText: "12004-R-P0" })).toContainText("LVL 2");
  await expect(rows.filter({ hasText: "12004-R-P0" })).toContainText("3 open findings");
  await expect(rows.filter({ hasText: "88291-K-02" })).toContainText("Approved");
  await expect(rows.filter({ hasText: "34055-L-X8" })).toContainText("Draft");
  await expect(rows.filter({ hasText: "55042-T-A1" })).toContainText("Not Started");
  await expect(rows.filter({ hasText: "12004-R-P0" }).getByRole("progressbar", { name: "4 of 18 elements complete" })).toBeVisible();
  await expect(rows.filter({ hasText: "88291-K-02" }).getByRole("progressbar", { name: "18 of 18 elements complete" })).toBeVisible();
  await expect(page.getByText("Showing 1 to 4 of 4 parts")).toBeVisible();

  await expect(rows.filter({ hasText: "12004-R-P0" }).getByRole("button", { name: "Review" })).toBeVisible();
  await expect(rows.filter({ hasText: "88291-K-02" }).getByRole("button", { name: "Review" })).toHaveCount(0);
  await rows.filter({ hasText: "12004-R-P0" }).getByRole("button", { name: "Review" }).click();
  await expect(page).toHaveURL(/\/ppap\/reports\/01970000-0000-7000-8000-000000000301\/review$/, { timeout: 15_000 });
});

test("filters by status and level through the URL", async ({ page }) => {
  test.slow();
  await page.goto("/ppap/parts");
  await expect(page.getByRole("heading", { name: "Part Submissions Directory" })).toBeVisible({ timeout: 30_000 });
  const request = page.waitForRequest((r) => r.url().includes("/api/v1/ppap/parts?") && r.url().includes("Status=Approved"));
  await page.getByRole("button", { name: "Status" }).click();
  await page.getByRole("menuitemradio", { name: "Approved" }).click();
  expect((await request).url()).toContain("Status=Approved");
  await expect(page).toHaveURL(/status=Approved/);
});

test("starts a PPAP submission at the part's level and discards a draft", async ({ page }) => {
  await mockJsonResponse(page, `**/api/v1/ppap/parts/${notStarted.id}/submissions`, '"01970000-0000-7000-8000-000000000099"', { method: "POST", status: 201 });
  await mockJsonResponse(page, `**/api/v1/ppap/submissions/${draft.latestSubmission.id}/cancel`, {}, { method: "POST" });
  test.slow();
  await page.goto("/ppap/parts");
  await expect(page.getByRole("heading", { name: "Part Submissions Directory" })).toBeVisible({ timeout: 30_000 });

  const cancel = page.waitForRequest((r) => r.url().endsWith(`/submissions/${draft.latestSubmission.id}/cancel`) && r.method() === "POST");
  await page.getByRole("button", { name: "Discard draft for 34055-L-X8" }).click();
  await page.getByRole("button", { name: "Discard draft", exact: true }).click();
  expect((await cancel).postDataJSON()).toEqual({ expectedVersion: 3 });

  const start = page.waitForRequest((r) => r.url().endsWith(`/parts/${notStarted.id}/submissions`) && r.method() === "POST");
  await page.getByRole("row").filter({ hasText: "55042-T-A1" }).getByRole("button", { name: "Start" }).click();
  expect((await start).postDataJSON()).toEqual({ ppapLevel: 3 });
  await expect(page).toHaveURL("/ppap/submissions/01970000-0000-7000-8000-000000000099");
});

test("creates a part with supplier, PPAP level, and classification", async ({ page }) => {
  test.slow();
  await mockJsonResponse(page, "**/api/v1/ppap/parts", { ...notStarted, id: "01970000-0000-7000-8000-000000000009" }, { method: "POST", status: 201 });
  await page.goto("/ppap/parts/new");

  await expect(page.getByLabel("Part Identity")).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("No supplier selected")).toBeVisible();
  await page.getByRole("button", { name: "Save Part" }).click();
  await expect(page.getByText("Part name is required.")).toBeVisible();

  await page.getByLabel("Part Name").fill("Front Axle Assembly");
  await page.getByLabel("Part Number").fill("PN-100-2000");
  await page.getByLabel("Engineering Revision").fill("Rev 04");
  await page.locator("#part-supplier").click();
  await page.getByRole("menuitemradio", { name: /Apex Automotive Solutions/ }).click();
  const card = page.getByLabel("Selected supplier");
  await expect(card).toContainText("ID: VND-000001");
  await expect(card).toContainText("Grade A+");
  await expect(card).toContainText("Marcus Thorne");
  await expect(card).toContainText("m.thorne@apex-solutions.com");

  await page.getByRole("radio", { name: /Safety/ }).click();
  await expect(page.locator("#part-level")).toContainText("Level 5");
  await page.locator("#part-level").click();
  await page.getByRole("menuitemradio", { name: /Level 3/ }).click();
  await expect(page.getByText("Safety parts normally require Level 5 PPAP documentation.")).toBeVisible();
  await page.locator("#part-template").click();
  await page.getByRole("menuitemradio", { name: /Standard PPAP/ }).click();

  const request = page.waitForRequest((r) => r.url().endsWith("/api/v1/ppap/parts") && r.method() === "POST");
  await page.getByRole("button", { name: "Save Part" }).click();
  expect((await request).postDataJSON()).toEqual({
    partName: "Front Axle Assembly",
    partNumber: "PN-100-2000",
    drawingRevision: "Rev 04",
    supplierId: supplier.id,
    ppapLevel: 3,
    classification: "Safety",
    ruleTemplateId: TEMPLATE_ID,
  });
  await expect(page).toHaveURL(/\/ppap\/parts$/);
});

test("edits an existing part without a template field", async ({ page }) => {
  test.slow();
  await mockJsonResponse(page, `**/api/v1/ppap/parts/${approved.id}`, approved);
  await page.goto(`/ppap/parts/${approved.id}/edit`);
  await expect(page.getByLabel("Part Name")).toHaveValue("Brake Caliper Housing", { timeout: 30_000 });
  await expect(page.locator("#part-template")).toHaveCount(0);

  await page.route(`**/api/v1/ppap/parts/${approved.id}`, async (route) => {
    if (route.request().method() !== "PUT") return route.fallback();
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ ...approved, version: 2 }) });
  });
  await page.getByLabel("Engineering Revision").fill("C");
  const request = page.waitForRequest((r) => r.url().endsWith(`/api/v1/ppap/parts/${approved.id}`) && r.method() === "PUT");
  await page.getByRole("button", { name: "Save Changes" }).click();
  expect((await request).postDataJSON()).toMatchObject({ drawingRevision: "C", expectedVersion: 1, ppapLevel: 5, classification: "Standard", supplierId: supplier.id });
});
