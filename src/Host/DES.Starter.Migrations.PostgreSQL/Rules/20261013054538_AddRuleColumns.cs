using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Rules
{
    /// <inheritdoc />
    public partial class AddRuleColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RuleColumns",
                schema: "rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DataType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HeaderNames = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleColumns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuleColumns_TenantId_Kind_Key",
                schema: "rules",
                table: "RuleColumns",
                columns: new[] { "TenantId", "Kind", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleColumns_TenantId_Kind_Name",
                schema: "rules",
                table: "RuleColumns",
                columns: new[] { "TenantId", "Kind", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuleColumns",
                schema: "rules");
        }
    }
}
