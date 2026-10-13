import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { ADMIN_PERMS, installAdminShellMocks, paged } from "../helpers/shell-mocks";

const TEMPLATE_ID = "44444444-4444-4444-4444-444444444444";
const VERSION_ID = "55555555-5555-5555-5555-555555555555";
const RULE_VERSION_ID = "66666666-6666-4666-8666-666666666666";
const TEMPLATE = { id: TEMPLATE_ID, stableCode: "OEM.STANDARD", name: "OEM standard", description: "Core submission template", lifecycle: "Draft", currentPublishedVersionId: null, createdAtUtc: "2026-01-01T00:00:00Z", updatedAtUtc: "2026-01-01T00:00:00Z", version: 2 };
const DETAIL = { ...TEMPLATE, versions: [{ id: VERSION_ID, versionNumber: 1, status: "Draft", createdByUserId: "77777777-7777-4777-8777-777777777777", createdAtUtc: "2026-01-01T00:00:00Z", publishedAtUtc: null, retiredAtUtc: null, documentRequirements: [], ruleAssignments: [] }] };

async function installMocks(page: Parameters<typeof installAdminShellMocks>[0]) {
  await mockJsonResponse(page, "**/api/v1/rules/templates?**", paged([TEMPLATE]));
  await mockJsonResponse(page, `**/api/v1/rules/templates/${TEMPLATE_ID}`, DETAIL);
}

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, { ...TEST_USER, permissions: [...ADMIN_PERMS] });
  await installAdminShellMocks(page);
  await installMocks(page);
});

test("lists and creates a rule template", async ({ page }) => {
  let body: Record<string, unknown> | undefined;
  await page.route("**/api/v1/rules/templates", async (route) => {
    if (route.request().method() !== "POST") return route.fallback();
    body = route.request().postDataJSON();
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(TEMPLATE_ID) });
  });
  await page.goto("/rule-templates");
  await expect(page.getByText("OEM.STANDARD")).toBeVisible();
  await expect(page.getByLabel("Search rule templates")).toHaveAttribute("id", "templates-search");
  await expect(page.getByLabel("Template status")).toHaveAttribute("id", "templates-status");
  await page.getByRole("button", { name: "New template" }).click();
  await page.getByLabel("Stable code").fill("OEM.ADVANCED");
  await page.getByRole("dialog").getByRole("textbox", { name: /^Name/ }).fill("OEM advanced");
  await page.getByLabel("Description").fill("Advanced submission template");
  await page.getByRole("button", { name: "Create template" }).click();
  await expect.poll(() => body).toEqual({ stableCode: "OEM.ADVANCED", name: "OEM advanced", description: "Advanced submission template" });
});

test("configures draft requirements and published rule assignments", async ({ page }) => {
  let body: Record<string, unknown> | undefined;
  await page.route(`**/api/v1/rules/templates/${TEMPLATE_ID}/draft`, async (route) => {
    body = route.request().postDataJSON();
    await route.fulfill({ status: 204 });
  });
  await page.goto("/rule-templates");
  await page.getByText("OEM.STANDARD").click();
  await page.getByRole("button", { name: "Add document" }).click();
  await page.getByLabel("Document code").fill("PSW");
  await page.getByLabel("Sort order").fill("10");
  const disposition = page.getByLabel("Disposition");
  await expect(disposition).toHaveAttribute("id", "document-disposition-0");
  await disposition.focus();
  await expect(disposition).toBeFocused();
  await disposition.press("Enter");
  await expect(page.getByRole("menuitem", { name: "S — Submit" })).toBeVisible();
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Assign rule" }).click();
  await page.getByLabel("Published RuleVersion id").fill(RULE_VERSION_ID);
  await page.getByLabel("Document code").nth(1).fill("PSW");
  await page.getByLabel("Parameters JSON object").fill('{ "severity": "error" }');
  await page.getByRole("button", { name: "Save draft configuration" }).click();
  await expect.poll(() => body).toEqual({ versionId: VERSION_ID, expectedAggregateVersion: 2, name: "OEM standard", description: "Core submission template", documentRequirements: [{ documentCode: "PSW", disposition: "Submit", isRequired: true, sortOrder: 10 }], ruleAssignments: [{ ruleVersionId: RULE_VERSION_ID, documentCode: "PSW", parametersJson: '{"severity":"error"}' }] });
});
