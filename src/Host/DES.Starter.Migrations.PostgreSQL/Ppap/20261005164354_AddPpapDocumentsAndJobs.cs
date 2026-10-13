using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapDocumentsAndJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "ppap",
                table: "PpapSubmissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PpapJobs",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionVersion = table.Column<int>(type: "integer", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrentStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PpapSubmissionDocuments",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FileAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotApplicableReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapSubmissionDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PpapSubmissionDocuments_PpapSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalSchema: "ppap",
                        principalTable: "PpapSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PpapJobs_SubmissionId_SubmissionVersion",
                schema: "ppap",
                table: "PpapJobs",
                columns: new[] { "SubmissionId", "SubmissionVersion", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapJobs_TenantId_OwnerUserId_Status",
                schema: "ppap",
                table: "PpapJobs",
                columns: new[] { "TenantId", "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapJobs_TenantId_Status_CreatedAtUtc",
                schema: "ppap",
                table: "PpapJobs",
                columns: new[] { "TenantId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissionDocuments_FileAssetId",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                column: "FileAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissionDocuments_Status",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissionDocuments_SubmissionId_DocumentCode",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                columns: new[] { "SubmissionId", "DocumentCode", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissionDocuments_TenantId_SubmissionId",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                columns: new[] { "TenantId", "SubmissionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PpapJobs",
                schema: "ppap");

            migrationBuilder.DropTable(
                name: "PpapSubmissionDocuments",
                schema: "ppap");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "ppap",
                table: "PpapSubmissions");
        }
    }
}
