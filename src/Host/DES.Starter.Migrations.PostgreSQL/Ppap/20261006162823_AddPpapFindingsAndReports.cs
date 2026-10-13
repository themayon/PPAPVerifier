using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapFindingsAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MinorFindingCount",
                schema: "ppap",
                table: "PpapReports",
                newName: "NotEvaluatedFindingCount");

            migrationBuilder.RenameColumn(
                name: "MajorFindingCount",
                schema: "ppap",
                table: "PpapReports",
                newName: "FailedFindingCount");

            migrationBuilder.RenameColumn(
                name: "CriticalFindingCount",
                schema: "ppap",
                table: "PpapReports",
                newName: "ErrorFindingCount");

            migrationBuilder.Sql("""
                UPDATE ppap."PpapReports"
                SET "FailedFindingCount" = 0,
                    "NotEvaluatedFindingCount" = 0,
                    "ErrorFindingCount" = 0;
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportGenerationCompletedAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportGenerationQueuedAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReportGenerationReportId",
                schema: "ppap",
                table: "PpapJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportGenerationStartedAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PpapFindings",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleEvaluationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleEvaluationResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanonicalDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtractionAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    StableRuleCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TraceJson = table.Column<string>(type: "jsonb", maxLength: 65536, nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapFindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PpapFindings_TenantId_JobId_Outcome",
                schema: "ppap",
                table: "PpapFindings",
                columns: new[] { "TenantId", "JobId", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapFindings_TenantId_OwnerUserId_SubmissionId",
                schema: "ppap",
                table: "PpapFindings",
                columns: new[] { "TenantId", "OwnerUserId", "SubmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapFindings_TenantId_RuleEvaluationResultId",
                schema: "ppap",
                table: "PpapFindings",
                columns: new[] { "TenantId", "RuleEvaluationResultId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PpapFindings",
                schema: "ppap");

            migrationBuilder.DropColumn(
                name: "ReportGenerationCompletedAtUtc",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "ReportGenerationQueuedAtUtc",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "ReportGenerationReportId",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "ReportGenerationStartedAtUtc",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.RenameColumn(
                name: "NotEvaluatedFindingCount",
                schema: "ppap",
                table: "PpapReports",
                newName: "MinorFindingCount");

            migrationBuilder.RenameColumn(
                name: "FailedFindingCount",
                schema: "ppap",
                table: "PpapReports",
                newName: "MajorFindingCount");

            migrationBuilder.RenameColumn(
                name: "ErrorFindingCount",
                schema: "ppap",
                table: "PpapReports",
                newName: "CriticalFindingCount");
        }
    }
}
