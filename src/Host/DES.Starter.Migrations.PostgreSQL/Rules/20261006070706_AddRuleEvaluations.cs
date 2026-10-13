using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Rules
{
    /// <inheritdoc />
    public partial class AddRuleEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RuleEvaluationRuns",
                schema: "rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputSchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleEvaluationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleEvaluationRuns_RuleTemplateVersions_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalSchema: "rules",
                        principalTable: "RuleTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RuleEvaluationResults",
                schema: "rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleEvaluationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TraceJson = table.Column<string>(type: "jsonb", maxLength: 65536, nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleEvaluationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleEvaluationResults_RuleDefinitions_RuleDefinitionId",
                        column: x => x.RuleDefinitionId,
                        principalSchema: "rules",
                        principalTable: "RuleDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RuleEvaluationResults_RuleEvaluationRuns_RuleEvaluationRunId",
                        column: x => x.RuleEvaluationRunId,
                        principalSchema: "rules",
                        principalTable: "RuleEvaluationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RuleEvaluationResults_RuleVersions_RuleVersionId",
                        column: x => x.RuleVersionId,
                        principalSchema: "rules",
                        principalTable: "RuleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluationResults_RuleDefinitionId",
                schema: "rules",
                table: "RuleEvaluationResults",
                column: "RuleDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluationResults_RuleEvaluationRunId_RuleVersionId_Doc~",
                schema: "rules",
                table: "RuleEvaluationResults",
                columns: new[] { "RuleEvaluationRunId", "RuleVersionId", "DocumentCode", "TenantId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluationResults_RuleVersionId",
                schema: "rules",
                table: "RuleEvaluationResults",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluationRuns_TemplateVersionId",
                schema: "rules",
                table: "RuleEvaluationRuns",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluationRuns_TenantId_OwnerUserId_CreatedAtUtc",
                schema: "rules",
                table: "RuleEvaluationRuns",
                columns: new[] { "TenantId", "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RuleEvaluationRuns_TenantId_SubjectId_CreatedAtUtc",
                schema: "rules",
                table: "RuleEvaluationRuns",
                columns: new[] { "TenantId", "SubjectId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuleEvaluationResults",
                schema: "rules");

            migrationBuilder.DropTable(
                name: "RuleEvaluationRuns",
                schema: "rules");
        }
    }
}
