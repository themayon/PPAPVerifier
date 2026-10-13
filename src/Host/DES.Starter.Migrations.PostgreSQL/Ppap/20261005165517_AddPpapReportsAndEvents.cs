using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapReportsAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompletionEventId",
                schema: "ppap",
                table: "PpapJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletionEventPublishedAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FailureEventId",
                schema: "ppap",
                table: "PpapJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailureEventPublishedAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PpapReports",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionVersion = table.Column<int>(type: "integer", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableDocumentCount = table.Column<int>(type: "integer", nullable: false),
                    NotApplicableDocumentCount = table.Column<int>(type: "integer", nullable: false),
                    QuarantinedDocumentCount = table.Column<int>(type: "integer", nullable: false),
                    FindingCount = table.Column<int>(type: "integer", nullable: false),
                    CriticalFindingCount = table.Column<int>(type: "integer", nullable: false),
                    MajorFindingCount = table.Column<int>(type: "integer", nullable: false),
                    MinorFindingCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PpapReports_JobId",
                schema: "ppap",
                table: "PpapReports",
                columns: new[] { "JobId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapReports_OwnerUserId_GeneratedAtUtc",
                schema: "ppap",
                table: "PpapReports",
                columns: new[] { "OwnerUserId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapReports_SubmissionId_SubmissionVersion",
                schema: "ppap",
                table: "PpapReports",
                columns: new[] { "SubmissionId", "SubmissionVersion", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PpapReports",
                schema: "ppap");

            migrationBuilder.DropColumn(
                name: "CompletionEventId",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "CompletionEventPublishedAtUtc",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "FailureEventId",
                schema: "ppap",
                table: "PpapJobs");

            migrationBuilder.DropColumn(
                name: "FailureEventPublishedAtUtc",
                schema: "ppap",
                table: "PpapJobs");
        }
    }
}
