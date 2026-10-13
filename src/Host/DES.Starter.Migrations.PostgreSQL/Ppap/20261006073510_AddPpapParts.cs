using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PartId",
                schema: "ppap",
                table: "PpapSubmissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PpapParts",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DrawingRevision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RuleTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuleTemplateVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapParts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissions_PartId",
                schema: "ppap",
                table: "PpapSubmissions",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_PpapParts_RuleTemplateId_RuleTemplateVersionId",
                schema: "ppap",
                table: "PpapParts",
                columns: new[] { "RuleTemplateId", "RuleTemplateVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapParts_TenantId_OwnerUserId_IsActive",
                schema: "ppap",
                table: "PpapParts",
                columns: new[] { "TenantId", "OwnerUserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PpapParts_TenantId_OwnerUserId_PartNumber",
                schema: "ppap",
                table: "PpapParts",
                columns: new[] { "TenantId", "OwnerUserId", "PartNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PpapParts",
                schema: "ppap");

            migrationBuilder.DropIndex(
                name: "IX_PpapSubmissions_PartId",
                schema: "ppap",
                table: "PpapSubmissions");

            migrationBuilder.DropColumn(
                name: "PartId",
                schema: "ppap",
                table: "PpapSubmissions");
        }
    }
}
