import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { ADMIN_PERMS, installAdminShellMocks, paged } from "../helpers/shell-mocks";

const RULE_ID = "11111111-1111-1111-1111-111111111111";
const VERSION_ID = "22222222-2222-2222-2222-222222222222";
const AST = JSON.stringify({ type: "comparison", operator: "Equal", left: { type: "field", documentCode: "PSW", path: "header.approved" }, right: { type: "literal", value: true } });
const RULE = { id: RULE_ID, stableCode: "PSW.APPROVED", name: "PSW approved", description: "Checks approval", category: "Document", lifecycle: "InReview", currentPublishedVersionId: null, createdAtUtc: "2026-01-01T00:00:00Z", updatedAtUtc: "2026-01-01T00:00:00Z", version: 3 };
const DETAIL = { ...RULE, versions: [{ id: VERSION_ID, versionNumber: 1, status: "InReview", createdAtUtc: "2026-01-01T00:00:00Z", publishedAtUtc: null, retiredAtUtc: null, createdByUserId: "33333333-3333-3333-3333-333333333333", astJson: AST }] };

async function installMocks(page: Parameters<typeof installAdminShellMocks>[0]) {
  await mockJsonResponse(page, "**/api/v1/rules/definitions?**", paged([RULE]));
  await mockJsonResponse(page, `**/api/v1/rules/definitions/${RULE_ID}`, DETAIL);
}

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, { ...TEST_USER, permissions: [...ADMIN_PERMS] });
  await installAdminShellMocks(page);
  await installMocks(page);
});

test("lists rules and creates a constrained comparison body", async ({ page }) => {
  let body: Record<string, unknown> | undefined;
  await page.route("**/api/v1/rules/definitions", async (route) => {
    if (route.request().method() !== "POST") return route.fallback();
    body = route.request().postDataJSON();
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(RULE_ID) });
  });
  await page.goto("/rules");
  await expect(page.getByText("PSW.APPROVED")).toBeVisible();
  await expect(page.getByLabel("Search rules")).toHaveAttribute("id", "rules-search");
  await expect(page.getByLabel("Rule category")).toHaveAttribute("id", "rules-category");
  await expect(page.getByLabel("Rule status")).toHaveAttribute("id", "rules-status");
  await page.getByRole("button", { name: "New rule" }).click();
  await page.getByLabel("Stable code").fill("DIM.WIDTH");
  await page.getByRole("dialog").getByRole("textbox", { name: /^Name/ }).fill("Width is valid");
  await page.getByLabel("Document code").fill("DIM");
  await page.getByLabel("Field path").fill("measurements.width");
  await page.getByLabel("Literal JSON").fill("42");
  await page.getByLabel("Outcome message").fill("Width must equal 42");
  await page.getByRole("button", { name: "Create rule" }).click();
  await expect.poll(() => body).toBeTruthy();
  expect(body).toMatchObject({ stableCode: "DIM.WIDTH", name: "Width is valid", description: null, category: "Document", outcomeJson: JSON.stringify({ message: "Width must equal 42" }) });
  expect(JSON.parse(body!.astJson as string)).toEqual({ type: "comparison", operator: "Equal", left: { type: "field", documentCode: "DIM", path: "measurements.width" }, right: { type: "literal", value: 42 } });
});

test("creates a comparison with an exact template parameter operand", async ({ page }) => {
  let body: Record<string, unknown> | undefined;
  await page.route("**/api/v1/rules/definitions", async (route) => {
    if (route.request().method() !== "POST") return route.fallback();
    body = route.request().postDataJSON();
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(RULE_ID) });
  });
  await page.goto("/rules");
  await page.getByRole("button", { name: "New rule" }).click();
  await page.getByLabel("Stable code").fill("DIM.WIDTH.PARAM");
  await page.getByRole("dialog").getByRole("textbox", { name: /^Name/ }).fill("Width matches parameter");
  await page.getByLabel("Document code").fill("DIM");
  await page.getByLabel("Field path").fill("measurements.width");
  await page.getByLabel("Right operand source").click();
  await page.getByRole("menuitem", { name: "Template parameter" }).click();
  await page.getByLabel("Template parameter name").fill("expected.width-1");
  await page.getByLabel("Outcome message").fill("Width must match the template parameter");
  await page.getByRole("button", { name: "Create rule" }).click();
  await expect.poll(() => body).toBeTruthy();
  expect(JSON.parse(body!.astJson as string)).toEqual({ type: "comparison", operator: "Equal", left: { type: "field", documentCode: "DIM", path: "measurements.width" }, right: { type: "parameter", name: "expected.width-1" } });
});

test("sends canonical test documents and parameters and displays the result", async ({ page }) => {
  let body: Record<string, unknown> | undefined;
  await page.route("**/api/v1/rules/definitions/test", async (route) => {
    body = route.request().postDataJSON();
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "Passed", reason: null, trace: [{ node: "Comparison", operator: "Equal", left: "true", right: "true", status: "Passed", reason: null }] }) });
  });
  await page.goto("/rules");
  await page.getByText("PSW.APPROVED").click();
  const astRegion = page.locator('pre[aria-labelledby="rule-ast-heading"]');
  await expect(astRegion).toHaveAttribute("tabindex", "0");
  await astRegion.focus();
  await expect(astRegion).toBeFocused();
  await page.getByLabel("Document code").fill("PSW");
  await page.getByLabel("Document JSON object").fill('{ "header": { "approved": true } }');
  await expect(page.getByLabel("Parameters JSON object")).toHaveValue("{}");
  await page.getByLabel("Parameters JSON object").fill('{ "expected": true, "allowed": ["A", "B"] }');
  await page.getByRole("button", { name: "Run test" }).click();
  await expect(page.getByText("Result")).toBeVisible();
  await expect(page.getByText("Comparison", { exact: true })).toBeVisible();
  const traceRegion = page.getByLabel("Rule evaluation trace");
  await expect(traceRegion).toHaveAttribute("tabindex", "0");
  await traceRegion.focus();
  await expect(traceRegion).toBeFocused();
  expect(body).toEqual({ astJson: AST, documents: { PSW: '{"header":{"approved":true}}' }, parametersJson: '{"expected":true,"allowed":["A","B"]}' });
});

test("publishes the selected version with aggregate concurrency data", async ({ page }) => {
  let body: Record<string, unknown> | undefined;
  await page.route(`**/api/v1/rules/definitions/${RULE_ID}/publish`, async (route) => {
    body = route.request().postDataJSON();
    await route.fulfill({ status: 204 });
  });
  await page.goto("/rules");
  await page.getByText("PSW.APPROVED").click();
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  const description = page.getByText("Publish rule version 1?", { exact: true });
  await expect(description).toBeVisible();
  await expect(description).toHaveJSProperty("tagName", "DIV");
  await page.getByRole("button", { name: "Publish rule", exact: true }).click();
  await expect.poll(() => body).toEqual({ versionId: VERSION_ID, expectedAggregateVersion: 3, retireCurrent: true });
});
