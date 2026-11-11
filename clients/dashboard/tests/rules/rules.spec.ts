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
  "Permissions.Definitions.Delete",
  "Permissions.Definitions.Test",
  "Permissions.RuleColumns.View",
  "Permissions.RuleColumns.Create",
  "Permissions.RuleColumns.Update",
  "Permissions.RuleColumns.Delete",
];

const column = (id: string, name: string, key: string, dataType: string, kind = "Column") => ({
  id, name, key, kind, dataType, headerNames: name, description: null, isActive: true,
  createdAtUtc: "2026-10-01T10:00:00Z", updatedAtUtc: "2026-10-01T10:00:00Z", version: 1,
});

const COLUMNS = [
  column("c1", "Status", "status", "Text"),
  column("c2", "Completion Date", "completionDate", "Date"),
  column("c3", "Target Date", "targetDate", "Date"),
  column("c4", "Owner", "owner", "Text"),
  column("c5", "Item Id", "itemId", "Text"),
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
      { type: "comparison", operator: "NotEqual", left: field("DFMEA", "status"), right: { type: "literal", value: "Completed" } },
      {
        type: "any",
        children: [
          { type: "comparison", operator: "GreaterThan", left: field("DFMEA", "completionDate"), right: field("DFMEA", "targetDate") },
          { type: "comparison", operator: "IsEmpty", left: field("DFMEA", "completionDate") },
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
      outcomeJson: JSON.stringify({ documentCode: "DFMEA", severity: "Warning", tags: ["DFMEA"], mode: "flag", messages: [{ field: null, text: "Action #PrimaryKey# is overdue" }], primaryKey: { documentCode: "DFMEA", path: "itemId" } }),
    },
  ],
};

async function pick(page: Page, groupName: string, option: RegExp, scope: Page | ReturnType<Page["locator"]> = page) {
  await scope.getByRole("group", { name: groupName, exact: true }).getByRole("button").first().click();
  await page.getByRole("menuitemradio", { name: option }).click();
}

test.beforeEach(async ({ page }) => {
  await seedAuthedSession(page, TEST_USER);
  await installShellMocks(page);
  await mockJsonResponse(page, "**/api/v1/identity/permissions", RULE_PERMISSIONS);
  await mockJsonResponse(page, "**/api/v1/rules/columns?**", paged(COLUMNS, { pageSize: 500 }));
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

test("deletes a published rule from the list when it has no evaluation history", async ({ page }) => {
  const liveRule = { ...summary, id: "live-rule-id", stableCode: "WRONG_RULE", name: "Wrong rule", version: 3 };
  let deleted = false;
  await page.route("**/api/v1/rules/definitions?**", async (route) => {
    const items = deleted ? [summary] : [summary, liveRule];
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(paged(items, { pageSize: 24 })) });
  });
  await page.route("**/api/v1/rules/definitions/live-rule-id?**", async (route) => {
    if (route.request().method() !== "DELETE") return route.fallback();
    deleted = true;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ outcome: "Deleted" }) });
  });

  await page.goto("/rules/validation");
  await expect(page.getByRole("heading", { name: "Validation rules" })).toBeVisible({ timeout: 20_000 });

  await page.getByRole("button", { name: "Actions for WRONG_RULE" }).click();
  await expect(page.getByRole("menuitem", { name: "Restore" })).toHaveCount(0);
  await page.getByRole("menuitem", { name: "Delete" }).click();
  const dialog = page.getByRole("dialog", { name: "Delete rule" });
  await expect(dialog).toContainText("WRONG_RULE");
  const request = page.waitForRequest((req) => req.url().includes("/api/v1/rules/definitions/live-rule-id") && req.method() === "DELETE");
  await dialog.getByRole("button", { name: "Delete" }).click();
  expect(new URL((await request).url()).searchParams.get("expectedVersion")).toBe("3");
  await expect(dialog).toHaveCount(0);
  await expect(page.getByText("Rule WRONG_RULE deleted")).toBeVisible();
  await expect(page.getByText("WRONG_RULE", { exact: true })).toHaveCount(0);
});

test("archives an evaluated rule on delete and restores it from the Archived filter", async ({ page }) => {
  const evaluated = { ...summary, id: "eval-rule-id", stableCode: "EVAL_RULE", name: "Evaluated rule", version: 5 };
  let archived = false;
  const searches: URL[] = [];
  await page.route("**/api/v1/rules/definitions?**", async (route) => {
    const url = new URL(route.request().url());
    searches.push(url);
    const onlyArchived = url.searchParams.get("Archived") === "Only";
    const items = onlyArchived ? (archived ? [{ ...evaluated, version: 6, isArchived: true, archivedAtUtc: "2026-11-10T10:00:00Z" }] : []) : archived ? [summary] : [summary, evaluated];
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(paged(items, { pageSize: 24 })) });
  });
  await page.route("**/api/v1/rules/definitions/eval-rule-id?**", async (route) => {
    if (route.request().method() !== "DELETE") return route.fallback();
    archived = true;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ outcome: "Archived" }) });
  });
  await page.route("**/api/v1/rules/definitions/eval-rule-id/restore?**", async (route) => {
    archived = false;
    await route.fulfill({ status: 204, body: "" });
  });

  await page.goto("/rules/validation");
  await expect(page.getByRole("heading", { name: "Validation rules" })).toBeVisible({ timeout: 20_000 });

  await page.getByRole("button", { name: "Actions for EVAL_RULE" }).click();
  await page.getByRole("menuitem", { name: "Delete" }).click();
  await page.getByRole("dialog", { name: "Delete rule" }).getByRole("button", { name: "Delete" }).click();
  await expect(page.getByText("Rule EVAL_RULE archived")).toBeVisible();
  await expect(page.getByText("EVAL_RULE", { exact: true })).toHaveCount(0);

  await page.getByRole("button", { name: "Archived" }).click();
  await page.getByRole("menuitemradio", { name: "Archived only" }).click();
  await expect(page).toHaveURL(/archived=Only/);
  await expect.poll(() => searches.some((url) => url.searchParams.get("Archived") === "Only")).toBe(true);
  const card = page.getByTestId("rule-card").filter({ hasText: "EVAL_RULE" });
  await expect(card.getByText("Archived", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Actions for EVAL_RULE" }).click();
  await expect(page.getByRole("menuitem", { name: "Delete" })).toHaveCount(0);
  const restore = page.waitForRequest((req) => req.url().includes("/api/v1/rules/definitions/eval-rule-id/restore") && req.method() === "POST");
  await page.getByRole("menuitem", { name: "Restore" }).click();
  expect(new URL((await restore).url()).searchParams.get("expectedVersion")).toBe("6");
  await expect(page.getByText("Rule EVAL_RULE restored")).toBeVisible();
});

test("builds a legacy-style formula from column master columns and posts the AST", async ({ page }) => {
  test.slow();
  await mockJsonResponse(page, "**/api/v1/rules/definitions?**", paged([]));
  await mockJsonResponse(page, "**/api/v1/rules/definitions/new-rule-id", { ...detail, id: "new-rule-id", lifecycle: "Draft", versions: [{ ...detail.versions[0], status: "Draft" }] });
  await mockJsonResponse(page, "**/api/v1/rules/definitions", '"new-rule-id"', { method: "POST", status: 201 });

  await page.goto("/rules/validation/new");
  await expect(page.getByRole("heading", { name: "New validation rule" })).toBeVisible({ timeout: 20_000 });
  await page.getByLabel("Rule code").fill("com_date_bln_targ");
  await page.getByLabel("Display name").fill("Action completed after target date");
  await page.getByRole("radio", { name: "Warning" }).click();
  await pick(page, "Rule document", /^DFMEA/);

  const rows = page.getByTestId("condition-row");
  const first = rows.nth(0);
  await pick(page, "Condition 1 column", /^Status/, first);
  await pick(page, "Condition 1 operator", /^!= does not equal/, first);
  await first.getByLabel("Condition 1 value", { exact: true }).fill("Completed");

  await page.getByRole("button", { name: "Add ( group )" }).click();
  const group = page.getByTestId("condition-group");
  await expect(group).toHaveCount(1);

  const nested = rows.nth(1);
  await pick(page, "Condition 1 column", /^Completion Date/, nested);
  await pick(page, "Condition 1 operator", /^> /, nested);
  await pick(page, "Condition 1 type", /^Column/, nested);
  await expect(nested.getByText("Value column", { exact: true })).toBeVisible();
  await pick(page, "Condition 1 value column", /^Target Date/, nested);
  await nested.getByLabel("Include empty value").check();

  await group.getByRole("button", { name: "Add formula" }).click();
  const third = rows.nth(2);
  await pick(page, "Condition 2 column", /^Owner/, third);
  await pick(page, "Condition 2 operator", /^= null/, third);
  await expect(third.getByText("Value", { exact: true })).toHaveCount(0);

  await page.getByLabel("Message 1 text").fill("Action is overdue");
  await expect(page.getByTestId("rule-formula")).toHaveText('Status != "Completed" AND ((Completion Date > Target Date OR Completion Date = null) OR Owner = null)');

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
        { type: "comparison", operator: "NotEqual", left: field("DFMEA", "status"), right: { type: "literal", value: "Completed" } },
        {
          type: "any",
          children: [
            {
              type: "any",
              children: [
                { type: "comparison", operator: "GreaterThan", left: field("DFMEA", "completionDate"), right: field("DFMEA", "targetDate") },
                { type: "comparison", operator: "IsEmpty", left: field("DFMEA", "completionDate") },
              ],
            },
            { type: "comparison", operator: "IsEmpty", left: field("DFMEA", "owner") },
          ],
        },
      ],
    },
  });
  expect(JSON.parse(body.outcomeJson)).toMatchObject({ documentCode: "DFMEA", severity: "Warning", mode: "flag", message: "Action is overdue" });
  await expect(page).toHaveURL(/\/rules\/validation\/new-rule-id$/);
});

test("checks that two date columns are not null with the != null operator", async ({ page }) => {
  await mockJsonResponse(page, "**/api/v1/rules/definitions", '"null-rule"', { method: "POST", status: 201 });
  await mockJsonResponse(page, "**/api/v1/rules/definitions/null-rule", { ...detail, id: "null-rule", lifecycle: "Draft", versions: [{ ...detail.versions[0], status: "Draft" }] });

  await page.goto("/rules/validation/new");
  await expect(page.getByRole("heading", { name: "New validation rule" })).toBeVisible({ timeout: 20_000 });
  await page.getByLabel("Rule code").fill("DATES_FILLED");
  await page.getByLabel("Display name").fill("Target and completion dates are filled");
  await pick(page, "Rule document", /^DFMEA/);
  await page.getByRole("radio", { name: "Pass when matched" }).click();

  const rows = page.getByTestId("condition-row");
  await pick(page, "Condition 1 column", /^Target Date/, rows.nth(0));
  await pick(page, "Condition 1 operator", /^!= null/, rows.nth(0));
  await expect(rows.nth(0).getByRole("group", { name: "Condition 1 type" })).toHaveCount(0);

  await page.getByRole("button", { name: "Add formula" }).click();
  await pick(page, "Condition 2 column", /^Completion Date/, rows.nth(1));
  await pick(page, "Condition 2 operator", /^!= null/, rows.nth(1));
  await page.getByLabel("Message 1 text").fill("Target date and completion date are required");

  await expect(page.getByTestId("rule-formula")).toHaveText("Target Date != null AND Completion Date != null");
  const request = page.waitForRequest((req) => req.url().endsWith("/api/v1/rules/definitions") && req.method() === "POST");
  await page.getByRole("button", { name: "Create draft" }).click();
  expect(JSON.parse(((await request).postDataJSON() as { astJson: string }).astJson)).toEqual({
    type: "all",
    children: [
      { type: "comparison", operator: "IsNotEmpty", left: field("DFMEA", "targetDate") },
      { type: "comparison", operator: "IsNotEmpty", left: field("DFMEA", "completionDate") },
    ],
  });
});

test("switching Type to Value shows the value input instead of the value column", async ({ page }) => {
  await page.goto("/rules/validation/new");
  await expect(page.getByRole("heading", { name: "New validation rule" })).toBeVisible({ timeout: 20_000 });
  await pick(page, "Rule document", /^DFMEA/);
  const row = page.getByTestId("condition-row").first();
  await pick(page, "Condition 1 column", /^Target Date/, row);

  const value = row.getByLabel("Condition 1 value", { exact: true });
  await expect(value).toHaveAttribute("type", "date");
  await pick(page, "Condition 1 type", /^Column/, row);
  await expect(row.getByRole("group", { name: "Condition 1 value column" })).toBeVisible();
  await expect(value).toHaveCount(0);
  await pick(page, "Condition 1 type", /^Value/, row);
  await expect(value).toBeVisible();
});

test("opens a published rule read-only with column names rebuilt from the AST", async ({ page }) => {
  await mockJsonResponse(page, `**/api/v1/rules/definitions/${detail.id}`, detail);

  await page.goto(`/rules/validation/${detail.id}`);
  await expect(page.getByRole("heading", { name: "Action completed after target date" })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText(/is\s+published\s+and read-only/i)).toBeVisible();
  await expect(page.getByRole("button", { name: "Create new draft" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Save draft" })).toHaveCount(0);

  const rows = page.getByTestId("condition-row");
  await expect(rows).toHaveCount(2);
  const firstColumn = rows.nth(0).getByRole("group", { name: "Condition 1 column" }).getByRole("button");
  await expect(firstColumn).toHaveText("Status");
  await expect(firstColumn).toBeDisabled();
  await expect(rows.nth(1).getByLabel("Include empty value")).toBeChecked();
  await expect(page.getByTestId("rule-formula")).toHaveText('Status != "Completed" AND (Completion Date > Target Date OR Completion Date = null)');
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
  await expect(page.getByText("Choose the document this rule checks.").first()).toBeVisible();
  await expect(page.getByText("Choose a column.").first()).toBeVisible();
  await expect(page.getByText("Add at least one message so reviewers know what went wrong.").first()).toBeVisible();
  expect(posted).toBe(false);
});

test("column master lists columns per kind and creates a column with an auto key", async ({ page }) => {
  const searches: URL[] = [];
  await page.route("**/api/v1/rules/columns?**", async (route) => {
    const url = new URL(route.request().url());
    searches.push(url);
    const kind = url.searchParams.get("Kind");
    const items = kind === "Header" ? [column("h1", "Part Number", "partNumber", "Text", "Header")] : COLUMNS;
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(paged(items, { pageSize: 50 })) });
  });
  await mockJsonResponse(page, "**/api/v1/rules/columns", '"new-column"', { method: "POST", status: 201 });

  await page.goto("/rules/columns");
  await expect(page.getByRole("heading", { name: "Column master" })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText("Completion Date", { exact: true }).first()).toBeVisible();
  await expect.poll(() => searches.some((url) => url.searchParams.get("Kind") === "Column" && url.searchParams.get("IsActive") === "true")).toBe(true);

  await page.getByRole("tab", { name: /Header columns/ }).click();
  await expect(page.getByText("Part Number", { exact: true }).first()).toBeVisible();
  await page.getByRole("tab", { name: /^Columns/ }).click();

  await page.getByRole("button", { name: "New column" }).click();
  await page.getByLabel("Column name").fill("Recommended Action Date");
  await expect(page.getByLabel("Key")).toHaveValue("recommendedActionDate");
  await page.getByRole("radio", { name: "Date" }).click();
  await page.getByLabel("Actual header names").fill("Rec. Action Date, Action Date");
  const request = page.waitForRequest((req) => req.url().endsWith("/api/v1/rules/columns") && req.method() === "POST");
  await page.getByRole("button", { name: "Create column" }).click();
  expect((await request).postDataJSON()).toEqual({ name: "Recommended Action Date", key: "recommendedActionDate", kind: "Column", dataType: "Date", headerNames: "Rec. Action Date, Action Date", description: null });
});
