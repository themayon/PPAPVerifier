import { expect, test, type Page } from "@playwright/test";
import { mockJsonResponse } from "../helpers/api-mocks";
import { seedAuthedSession, TEST_USER } from "../helpers/auth-seed";
import { installShellMocks, paged } from "../helpers/shell-mocks";

const RULE_PERMISSIONS = [
  "Permissions.Definitions.View",
  "Permissions.Definitions.Create",
  "Permissions.Definitions.Edit",
  "Permissions.Definitions.Review",
  "Permissions.Definitions.Publish",
  "Permissions.Definitions.Retire",
  "Permissions.Definitions.Test",
];

const field = (documentCode: string, path: string) => ({ type: "field", documentCode, path });

const summary = {
  id: "01970000-0000-7000-8000-0000000000a1",
  stableCode: "COM_DATE_BLN_TARG",
  name: "Action completed after target date",
  description: "Recommended action is not completed before target date",
  category: "Row",
  lifecycle: "Published",
  currentPublishedVersionId: "01970000-0000-7000-8000-0000000000b1",
  createdAtUtc: "2026-10-01T10:00:00Z",
  updatedAtUtc: "2026-10-02T10:00:00Z",
  version: 4,
  latestOutcomeJson: JSON.stringify({ severity: "Warning", tags: ["DFMEA", "Dates"], mode: "flag", messages: [{ field: null, text: "Action #PrimaryKey# is overdue" }] }),
  conditionCount: 2,
};

const publishedAst = {
  type: "not",
  child: {
    type: "all",
    children: [
      { type: "comparison", operator: "NotEqual", left: field("DFMEA", "actions.status"), right: { type: "literal", value: "Completed" } },
      {
        type: "any",
        children: [
          { type: "comparison", operator: "GreaterThan", left: field("DFMEA", "actions.completedOn"), right: field("DFMEA", "actions.targetDate") },
          { type: "comparison", operator: "IsEmpty", left: field("DFMEA", "actions.completedOn") },
        ],
      },
    ],
  },
};

const detail = {
  ...summary,
  versions: [
    {
      id: "01970000-0000-7000-8000-0000000000b1",
      versionNumber: 1,
      status: "Published",
      createdAtUtc: "2026-10-01T10:00:00Z",
      publishedAtUtc: "2026-10-02T10:00:00Z",
      retiredAtUtc: null,
      createdByUserId: "u-test-1",
      astJson: JSON.stringify(publishedAst),
      outcomeJson: JSON.stringify({ severity: "Warning", tags: ["DFMEA"], mode: "flag", messages: [{ field: null, text: "Action #PrimaryKey# is overdue" }], primaryKey: { documentCode: "DFMEA", path: "actions.id" } }),
    },
  ],
};

async function grantRulePermissions(page: Page) {
  await mockJsonResponse(page, "**/api/v1/identity/permissions", RULE_PERMISSIONS);
}

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
  await grantRulePermissions(page);
});

test("lists validation rules as cards with severity, tags, and server-side filters", async ({ page }) => {
  const searches: URL[] = [];
  await page.route("**/api/v1/rules/definitions?**", async (route) => {
    searches.push(new URL(route.request().url()));
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(paged([summary], { pageSize: 24 })) });
  });

  await page.goto("/rules");
  await expect(page).toHaveURL(/\/rules\/validation$/);
  await expect(page.getByRole("heading", { name: "Validation rules" })).toBeVisible({ timeout: 20_000 });

  const card = page.getByTestId("rule-card").first();
  await expect(card.getByText("COM_DATE_BLN_TARG")).toBeVisible();
  await expect(card.getByText("Warning")).toBeVisible();
  await expect(card.getByRole("button", { name: "#DFMEA" })).toBeVisible();
  await expect(card.getByText("Live")).toBeVisible();
  expect(searches.some((url) => url.searchParams.get("Categories") === "Row,Document,CrossDocument,Drawing")).toBe(true);

  await page.getByRole("button", { name: "Severity" }).click();
  await page.getByRole("menuitemradio", { name: "Error" }).click();
  await expect.poll(() => searches.some((url) => url.searchParams.get("Severity") === "Error")).toBe(true);
  await expect(page).toHaveURL(/severity=Error/);

  await card.getByRole("button", { name: "#DFMEA" }).click();
  await expect.poll(() => searches.some((url) => url.searchParams.get("Tag") === "DFMEA")).toBe(true);

  await page.getByRole("radio", { name: "List view" }).click();
  await expect(page.getByText("Conditions", { exact: true })).toBeVisible();

  await page.getByRole("link", { name: /ML rules/ }).first().click();
  await expect(page).toHaveURL(/\/rules\/ml$/);
  await expect.poll(() => searches.some((url) => url.searchParams.get("Categories") === "ModelInference" && url.searchParams.get("PageSize") === "24")).toBe(true);
});

test("builds a multi-condition rule with a nested OR group and posts the AST", async ({ page }) => {
  test.slow();
  await mockJsonResponse(page, "**/api/v1/rules/definitions?**", paged([]));
  await mockJsonResponse(page, "**/api/v1/rules/definitions/new-rule-id", { ...detail, id: "new-rule-id", lifecycle: "Draft", versions: [{ ...detail.versions[0], status: "Draft" }] });
  await mockJsonResponse(page, "**/api/v1/rules/definitions", '"new-rule-id"', { method: "POST", status: 201 });

  await page.goto("/rules/validation/new");
  await expect(page.getByRole("heading", { name: "New validation rule" })).toBeVisible({ timeout: 20_000 });
  await page.getByLabel("Rule code").fill("com_date_bln_targ");
  await page.getByLabel("Display name").fill("Action completed after target date");
  await page.getByRole("radio", { name: "Warning" }).click();

  const rows = page.getByTestId("condition-row");
  const first = rows.nth(0);
  await first.getByRole("button", { name: "Document" }).click();
  await page.getByRole("menuitemradio", { name: /^DFMEA/ }).click();
  await first.getByLabel("Condition 1 field").fill("actions.status");
  await first.getByRole("button", { name: "equals", exact: true }).click();
  await page.getByRole("menuitemradio", { name: "does not equal" }).click();
  await first.getByLabel("Condition 1 value").fill("Completed");

  await page.getByRole("button", { name: "Group", exact: true }).click();
  const group = page.getByTestId("condition-group");
  await expect(group).toHaveCount(1);

  const nested = rows.nth(1);
  await nested.getByLabel("Condition 1 field").fill("actions.completedOn");
  await nested.getByRole("button", { name: "Text", exact: true }).click();
  await page.getByRole("menuitemradio", { name: "Date" }).click();
  await nested.getByRole("button", { name: "is on", exact: true }).click();
  await page.getByRole("menuitemradio", { name: "is after" }).click();
  await nested.getByRole("radio", { name: "Field" }).click();
  await nested.getByLabel("Condition 1 compare field").fill("actions.targetDate");
  await nested.getByLabel("Also match when empty").check();

  await group.getByRole("button", { name: "Condition", exact: true }).click();
  const third = rows.nth(2);
  await third.getByLabel("Condition 2 field").fill("actions.owner");
  await third.getByRole("button", { name: "equals", exact: true }).click();
  await page.getByRole("menuitemradio", { name: "is empty" }).click();

  await page.getByLabel("Message 1 text").fill("Action is overdue");

  const preview = page.getByTestId("rule-preview");
  if (await preview.isVisible()) {
    await expect(preview).toContainText("ANY of:");
    await expect(preview).toContainText("DFMEA › actions.completedOn is after DFMEA › actions.targetDate (or is empty)");
  }

  const request = page.waitForRequest((req) => req.url().endsWith("/api/v1/rules/definitions") && req.method() === "POST");
  await page.getByRole("button", { name: "Create draft" }).click();
  const body = (await request).postDataJSON() as { stableCode: string; category: string; astJson: string; outcomeJson: string };

  expect(body.stableCode).toBe("COM_DATE_BLN_TARG");
  expect(body.category).toBe("Row");
  expect(JSON.parse(body.astJson)).toEqual({
    type: "not",
    child: {
      type: "all",
      children: [
        { type: "comparison", operator: "NotEqual", left: field("DFMEA", "actions.status"), right: { type: "literal", value: "Completed" } },
        {
          type: "any",
          children: [
            {
              type: "any",
              children: [
                { type: "comparison", operator: "GreaterThan", left: field("DFMEA", "actions.completedOn"), right: field("DFMEA", "actions.targetDate") },
                { type: "comparison", operator: "IsEmpty", left: field("DFMEA", "actions.completedOn") },
              ],
            },
            { type: "comparison", operator: "IsEmpty", left: field("DFMEA", "actions.owner") },
          ],
        },
      ],
    },
  });
  const outcome = JSON.parse(body.outcomeJson) as Record<string, unknown>;
  expect(outcome).toMatchObject({ severity: "Warning", mode: "flag", message: "Action is overdue", Failed: { message: "Action is overdue" } });
  await expect(page).toHaveURL(/\/rules\/validation\/new-rule-id$/);
});

test("opens a published rule read-only with its conditions rebuilt from the AST", async ({ page }) => {
  await mockJsonResponse(page, `**/api/v1/rules/definitions/${detail.id}`, detail);

  await page.goto(`/rules/validation/${detail.id}`);
  await expect(page.getByRole("heading", { name: "Action completed after target date" })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText(/is\s+published\s+and read-only/i)).toBeVisible();
  await expect(page.getByRole("button", { name: "Create new draft" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Save draft" })).toHaveCount(0);

  const rows = page.getByTestId("condition-row");
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0).getByLabel("Condition 1 field")).toHaveValue("actions.status");
  await expect(rows.nth(0).getByLabel("Condition 1 field")).toBeDisabled();
  await expect(rows.nth(1).getByLabel("Also match when empty")).toBeChecked();
  await expect(page.getByLabel("Message 1 text")).toHaveValue("Action #PrimaryKey# is overdue");
});

test("blocks saving and points to issues when required details are missing", async ({ page }) => {
  let posted = false;
  await page.route("**/api/v1/rules/definitions", async (route) => {
    if (route.request().method() === "POST") posted = true;
    await route.fulfill({ status: 201, contentType: "application/json", body: '"x"' });
  });

  await page.goto("/rules/header/new");
  await expect(page.getByRole("heading", { name: "New header rule" })).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Create draft" }).click();

  await expect(page.getByText("Give the rule a display name.").first()).toBeVisible();
  await expect(page.getByText("Field: choose a document.").first()).toBeVisible();
  await expect(page.getByText("Add at least one message so reviewers know what went wrong.").first()).toBeVisible();
  expect(posted).toBe(false);
});
