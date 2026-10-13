using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Rules
{
    /// <inheritdoc />
    public partial class InitialRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rules");

            migrationBuilder.CreateTable(
                name: "RuleDefinitions",
                schema: "rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StableCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Lifecycle = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrentPublishedVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleVersions",
                schema: "rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AstJson = table.Column<string>(type: "jsonb", nullable: false),
                    OutcomeJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleVersions_RuleDefinitions_RuleDefinitionId",
                        column: x => x.RuleDefinitionId,
                        principalSchema: "rules",
                        principalTable: "RuleDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuleDefinitions_TenantId_StableCode",
                schema: "rules",
                table: "RuleDefinitions",
                columns: new[] { "TenantId", "StableCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleVersions_RuleDefinitionId_VersionNumber",
                schema: "rules",
                table: "RuleVersions",
                columns: new[] { "RuleDefinitionId", "VersionNumber", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuleVersions",
                schema: "rules");

            migrationBuilder.DropTable(
                name: "RuleDefinitions",
                schema: "rules");
        }
    }
}
