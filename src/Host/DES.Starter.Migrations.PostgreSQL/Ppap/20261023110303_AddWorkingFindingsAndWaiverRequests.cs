using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddWorkingFindingsAndWaiverRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PpapWaiverRequests",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkingFindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    FindingFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapWaiverRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PpapWorkingFindings",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    StableRuleCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    InputFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapWorkingFindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PpapWaiverRequests_TenantId_SubmissionId_Status",
                schema: "ppap",
                table: "PpapWaiverRequests",
                columns: new[] { "TenantId", "SubmissionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapWaiverRequests_TenantId_WorkingFindingId",
                schema: "ppap",
                table: "PpapWaiverRequests",
                columns: new[] { "TenantId", "WorkingFindingId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_PpapWorkingFindings_DocumentRevisionId",
                schema: "ppap",
                table: "PpapWorkingFindings",
                column: "DocumentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PpapWorkingFindings_TenantId_RuleVersionId_InputFingerprint",
                schema: "ppap",
                table: "PpapWorkingFindings",
                columns: new[] { "TenantId", "RuleVersionId", "InputFingerprint" },
                unique: true,
                filter: "\"IsCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PpapWorkingFindings_TenantId_SubmissionId_IsCurrent",
                schema: "ppap",
                table: "PpapWorkingFindings",
                columns: new[] { "TenantId", "SubmissionId", "IsCurrent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PpapWaiverRequests",
                schema: "ppap");

            migrationBuilder.DropTable(
                name: "PpapWorkingFindings",
                schema: "ppap");
        }
    }
}
