using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapManualFindingsAndWaivers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PpapFindingWaivers",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetFindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapFindingWaivers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PpapManualFindings",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanonicalDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtractionAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EvidenceNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapManualFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PpapWaiverRevocations",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WaiverId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapWaiverRevocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PpapWaiverRevocations_PpapFindingWaivers_WaiverId",
                        column: x => x.WaiverId,
                        principalSchema: "ppap",
                        principalTable: "PpapFindingWaivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PpapFindingWaivers_TenantId_OwnerUserId_ReportId",
                schema: "ppap",
                table: "PpapFindingWaivers",
                columns: new[] { "TenantId", "OwnerUserId", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapFindingWaivers_TenantId_ReportId_TargetType",
                schema: "ppap",
                table: "PpapFindingWaivers",
                columns: new[] { "TenantId", "ReportId", "TargetType" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapFindingWaivers_TenantId_TargetType_TargetFindingId_IsAc~",
                schema: "ppap",
                table: "PpapFindingWaivers",
                columns: new[] { "TenantId", "TargetType", "TargetFindingId", "IsActive" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PpapManualFindings_TenantId_OwnerUserId_ReportId",
                schema: "ppap",
                table: "PpapManualFindings",
                columns: new[] { "TenantId", "OwnerUserId", "ReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapManualFindings_TenantId_ReportId_CreatedAtUtc",
                schema: "ppap",
                table: "PpapManualFindings",
                columns: new[] { "TenantId", "ReportId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapManualFindings_TenantId_SubmissionDocumentId",
                schema: "ppap",
                table: "PpapManualFindings",
                columns: new[] { "TenantId", "SubmissionDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapWaiverRevocations_TenantId_WaiverId",
                schema: "ppap",
                table: "PpapWaiverRevocations",
                columns: new[] { "TenantId", "WaiverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapWaiverRevocations_WaiverId",
                schema: "ppap",
                table: "PpapWaiverRevocations",
                column: "WaiverId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PpapManualFindings",
                schema: "ppap");

            migrationBuilder.DropTable(
                name: "PpapWaiverRevocations",
                schema: "ppap");

            migrationBuilder.DropTable(
                name: "PpapFindingWaivers",
                schema: "ppap");
        }
    }
}
