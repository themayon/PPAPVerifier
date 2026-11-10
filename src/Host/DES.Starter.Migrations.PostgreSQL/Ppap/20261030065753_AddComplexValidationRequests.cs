using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddComplexValidationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PpapComplexValidationRequests",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutorKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InputFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapComplexValidationRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PpapComplexValidationRequests_RequestEventId",
                schema: "ppap",
                table: "PpapComplexValidationRequests",
                columns: new[] { "RequestEventId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapComplexValidationRequests_TenantId_SubmissionId_RuleVer~",
                schema: "ppap",
                table: "PpapComplexValidationRequests",
                columns: new[] { "TenantId", "SubmissionId", "RuleVersionId", "InputFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapComplexValidationRequests_ValidationRunId",
                schema: "ppap",
                table: "PpapComplexValidationRequests",
                columns: new[] { "ValidationRunId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PpapComplexValidationRequests",
                schema: "ppap");
        }
    }
}
