import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks, paged } from "../helpers/shell-mocks";

const PERMISSIONS = [
  "Permissions.Definitions.View",
  "Permissions.RuleDocumentSettings.View",
  "Permissions.RuleDocumentSettings.Update",
  "Permissions.Templates.View",
  "Permissions.Templates.Create",
  "Permissions.Templates.Edit",
  "Permissions.Ppap.Suppliers.View",
  "Permissions.Ppap.Suppliers.Create",
  "Permissions.Ppap.Suppliers.Update",
  "Permissions.Ppap.Parts.View",
  "Permissions.Ppap.Parts.Create",
];

const RULES = [
  { id: "r1", stableCode: "COM_DATE_BLN_TARG", name: "Completion after target", category: "Row", lifecycle: "Published", isLive: true, latestOutcomeJson: JSON.stringify({ severity: "Error" }) },
  { id: "r2", stableCode: "MISSING_OCC", name: "Missing occurrence", category: "Row", lifecycle: "Published", isLive: true, latestOutcomeJson: JSON.stringify({ severity: "Warning" }) },
  { id: "r3", stableCode: "HDR_PART_NO", name: "Part number matches PSW", category: "Header", lifecycle: "Draft", isLive: false, latestOutcomeJson: null },
];

const SETTINGS = [
  { id: "d1", documentCode: "PFMEA", name: "Process FMEA", isEnabled: true, sortOrder: 1, defaultDisposition: "Submit", ruleDefinitionIds: ["r1"], updatedAtUtc: "2026-10-01T00:00:00Z", version: 3 },
  { id: "d2", documentCode: "CP", name: "Control Plan", isEnabled: true, sortOrder: 2, defaultDisposition: "Submit", ruleDefinitionIds: [], updatedAtUtc: "2026-10-01T00:00:00Z", version: 1 },
];

const SETUP = {
  id: "t1",
  stableCode: "TIER-1",
  name: "Tier 1 suppliers",
  description: null,
  lifecycle: "Published",
  currentPublishedVersionId: "tv1",
  updatedAtUtc: "2026-10-01T00:00:00Z",
  version: 4,
  documents: [
    { documentCode: "PFMEA", isEnabled: true, disposition: "Submit", sortOrder: 1 },
    { documentCode: "CP", isEnabled: true, disposition: "Submit", sortOrder: 2 },
  ],
  rules: [
    { documentCode: "PFMEA", ruleDefinitionId: "r1", isEnabled: true },
    { documentCode: "PFMEA", ruleDefinitionId: "r2", isEnabled: true },
  ],
};

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
  await mockJsonResponse(page, "**/api/v1/identity/permissions", PERMISSIONS);
  await mockJsonResponse(page, "**/api/v1/rules/definitions/lookup", RULES);
  await mockJsonResponse(page, "**/api/v1/rules/document-settings", SETTINGS);
});

test("document settings maps rules to a document", async ({ page }) => {
  await mockJsonResponse(page, "**/api/v1/rules/document-settings/d1", { ...SETTINGS[0], ruleDefinitionIds: ["r1", "r2"], version: 4 }, { method: "PUT" });
  await page.goto("/rules/document-settings");
  await expect(page.getByRole("heading", { name: "Document settings" })).toBeVisible({ timeout: 20_000 });

  const card = page.getByTestId("document-card").filter({ hasText: "PFMEA" });
  await expect(card.getByText("1", { exact: true }).first()).toBeVisible();
  await card.getByRole("button", { name: "Configure rules" }).click();
  await page.getByRole("checkbox", { name: /MISSING_OCC/ }).check();
  const request = page.waitForRequest((req) => req.url().endsWith("/document-settings/d1") && req.method() === "PUT");
  await page.getByRole("button", { name: "Save 2 rules" }).click();
  expect((await request).postDataJSON()).toEqual({ expectedVersion: 3, name: "Process FMEA", isEnabled: true, sortOrder: 1, defaultDisposition: "Submit", ruleDefinitionIds: ["r1", "r2"] });
});

test("creates a template from document settings and opens the editor", async ({ page }) => {
  await mockJsonResponse(page, "**/api/v1/rules/template-setups?**", paged([]));
  await mockJsonResponse(page, "**/api/v1/rules/template-setups", { templateId: "t1", publishedVersionId: "tv1", version: 3, skippedRuleDefinitionIds: [] }, { method: "POST" });
  await mockJsonResponse(page, "**/api/v1/rules/template-setups/t1", SETUP);

  await page.goto("/rule-templates");
  await expect(page.getByRole("heading", { name: "Templates", exact: true })).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "New template" }).first().click();
  await page.getByLabel("Template code").fill("tier-1");
  await page.getByLabel("Template name").fill("Tier 1 suppliers");
  const request = page.waitForRequest((req) => req.url().endsWith("/template-setups") && req.method() === "POST");
  await page.getByRole("button", { name: "Create template" }).click();
  expect((await request).postDataJSON()).toEqual({ stableCode: "TIER-1", name: "Tier 1 suppliers", description: null });
  await expect(page).toHaveURL(/\/rule-templates\/t1$/);
  await expect(page.getByRole("heading", { name: "Tier 1 suppliers" })).toBeVisible({ timeout: 20_000 });
});

test("template editor enables and disables documents and rules", async ({ page }) => {
  await mockJsonResponse(page, "**/api/v1/rules/template-setups/t1", SETUP);
  await page.goto("/rule-templates/t1");
  await expect(page.getByRole("heading", { name: "Tier 1 suppliers" })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText("2 of 2 documents · 2 active rules")).toBeVisible();

  await page.getByRole("switch", { name: "CP enabled" }).first().click();
  await page.getByRole("switch", { name: "MISSING_OCC enabled" }).click();
  await expect(page.getByText("1 of 2 documents · 1 active rules")).toBeVisible();

  await page.unroute("**/api/v1/rules/template-setups/t1");
  let saved: unknown;
  await page.route("**/api/v1/rules/template-setups/t1", async (route) => {
    if (route.request().method() === "PUT") {
      saved = route.request().postDataJSON();
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ templateId: "t1", publishedVersionId: "tv2", version: 6, skippedRuleDefinitionIds: ["r3"] }) });
      return;
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(SETUP) });
  });
  await page.getByRole("button", { name: "Save template" }).click();
  await expect(page.getByText(/HDR_PART_NO/)).toBeVisible();
  expect(saved).toEqual({
    expectedVersion: 4,
    name: "Tier 1 suppliers",
    description: null,
    documents: [
      { documentCode: "PFMEA", isEnabled: true, disposition: "Submit", sortOrder: 1 },
      { documentCode: "CP", isEnabled: false, disposition: "Submit", sortOrder: 2 },
    ],
    rules: [
      { documentCode: "PFMEA", ruleDefinitionId: "r1", isEnabled: true },
      { documentCode: "PFMEA", ruleDefinitionId: "r2", isEnabled: false },
    ],
  });
});
