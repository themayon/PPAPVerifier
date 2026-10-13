import { expect, test } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks, paged } from "../helpers/shell-mocks";

const part = {
  id: "01970000-0000-7000-8000-000000000001",
  partName: "Steering Knuckle",
  partNumber: "SK-2048",
  drawingRevision: "C",
  ruleTemplateId: null,
  ruleTemplateVersionId: null,
  isActive: true,
  version: 1,
  createdAtUtc: "2026-09-15T10:00:00Z",
  updatedAtUtc: "2026-09-15T10:00:00Z",
};

const assignedPart = {
  ...part,
  ruleTemplateId: "01970000-0000-7000-8000-000000000010",
  ruleTemplateVersionId: "01970000-0000-7000-8000-000000000011",
  version: 2,
};

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
  await mockJsonResponse(page, "**/api/v1/ppap/parts?**", paged([part], { pageSize: 10 }));
  await mockJsonResponse(page, "**/api/v1/rules/templates?**", paged([{ id: assignedPart.ruleTemplateId, stableCode: "STANDARD-PPAP", name: "Standard PPAP" }], { pageSize: 100 }));
});

test("lists, searches, and creates a supplier part", async ({ page }) => {
  await mockJsonResponse(page, "**/api/v1/ppap/parts", { ...part, id: "01970000-0000-7000-8000-000000000002", partName: "Brake Caliper", partNumber: "BC-900" }, { method: "POST", status: 201 });
  await page.goto("/ppap/parts");

  await expect(page.getByRole("heading", { name: "PPAP Parts" })).toBeVisible();
  await expect(page.getByRole("main")).toHaveCount(1);
  await expect(page.getByText("Steering Knuckle")).toBeVisible();
  await page.getByLabel("Search parts").fill("SK-2048");
  await expect.poll(() => page.url()).toContain("/ppap/parts");

  await page.getByRole("button", { name: "Add part" }).click();
  await page.getByLabel("Part name").fill("Brake Caliper");
  await page.getByLabel("Part number").fill("BC-900");
  await page.getByLabel("Drawing revision").fill("B2");
  const requestPromise = page.waitForRequest((request) => request.url().endsWith("/api/v1/ppap/parts") && request.method() === "POST");
  await page.getByRole("button", { name: "Save part" }).click();

  expect((await requestPromise).postDataJSON()).toEqual({ partName: "Brake Caliper", partNumber: "BC-900", drawingRevision: "B2" });
});

test("assigns a published template and creates a draft from the stored part", async ({ page }) => {
  let listCount = 0;
  await page.route("**/api/v1/ppap/parts?**", async (route) => {
    listCount += 1;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(paged(listCount > 1 ? [assignedPart] : [part], { pageSize: 10 })) });
  });
  await mockJsonResponse(page, `**/api/v1/ppap/parts/${part.id}/assign-template`, assignedPart, { method: "POST" });
  await mockJsonResponse(page, `**/api/v1/ppap/parts/${part.id}/submissions`, '"01970000-0000-7000-8000-000000000099"', { method: "POST", status: 201 });
  await page.goto("/ppap/parts");

  await page.getByRole("button", { name: "Assign template" }).click();
  await page.getByLabel("Published template", { exact: true }).selectOption(assignedPart.ruleTemplateId!);
  const assignRequest = page.waitForRequest((request) => request.url().endsWith(`/parts/${part.id}/assign-template`) && request.method() === "POST");
  await page.getByRole("button", { name: "Assign template", exact: true }).last().click();
  expect((await assignRequest).postDataJSON()).toEqual({ ruleTemplateId: assignedPart.ruleTemplateId, expectedVersion: 1 });

  await expect(page.getByText("Template snapshot assigned")).toBeVisible();
  await page.getByRole("button", { name: "Create draft" }).click();
  await page.getByLabel("PPAP level").selectOption("4");
  const draftRequest = page.waitForRequest((request) => request.url().endsWith(`/parts/${part.id}/submissions`) && request.method() === "POST");
  await page.getByRole("button", { name: "Create draft", exact: true }).last().click();
  expect((await draftRequest).postDataJSON()).toEqual({ ppapLevel: 4 });
  await expect(page).toHaveURL("/ppap/submissions/01970000-0000-7000-8000-000000000099");
});
