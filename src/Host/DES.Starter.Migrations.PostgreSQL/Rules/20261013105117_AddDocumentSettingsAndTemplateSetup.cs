using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Rules
{
    /// <inheritdoc />
    public partial class AddDocumentSettingsAndTemplateSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentSettings",
                schema: "rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DocumentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    DefaultDisposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleTemplateDocuments",
                schema: "rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleTemplateDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleTemplateDocuments_RuleTemplates_RuleTemplateId",
                        column: x => x.RuleTemplateId,
                        principalSchema: "rules",
                        principalTable: "RuleTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuleTemplateRules",
                schema: "rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RuleDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleTemplateRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleTemplateRules_RuleDefinitions_RuleDefinitionId",
                        column: x => x.RuleDefinitionId,
                        principalSchema: "rules",
                        principalTable: "RuleDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RuleTemplateRules_RuleTemplates_RuleTemplateId",
                        column: x => x.RuleTemplateId,
                        principalSchema: "rules",
                        principalTable: "RuleTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentSettingRules",
                schema: "rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentSettingId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSettingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentSettingRules_DocumentSettings_DocumentSettingId",
                        column: x => x.DocumentSettingId,
                        principalSchema: "rules",
                        principalTable: "DocumentSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentSettingRules_RuleDefinitions_RuleDefinitionId",
                        column: x => x.RuleDefinitionId,
                        principalSchema: "rules",
                        principalTable: "RuleDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSettingRules_DocumentSettingId_RuleDefinitionId",
                schema: "rules",
                table: "DocumentSettingRules",
                columns: new[] { "DocumentSettingId", "RuleDefinitionId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSettingRules_RuleDefinitionId",
                schema: "rules",
                table: "DocumentSettingRules",
                column: "RuleDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSettings_TenantId_DocumentCode",
                schema: "rules",
                table: "DocumentSettings",
                columns: new[] { "TenantId", "DocumentCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleTemplateDocuments_RuleTemplateId_DocumentCode",
                schema: "rules",
                table: "RuleTemplateDocuments",
                columns: new[] { "RuleTemplateId", "DocumentCode", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleTemplateRules_RuleDefinitionId",
                schema: "rules",
                table: "RuleTemplateRules",
                column: "RuleDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleTemplateRules_RuleTemplateId_DocumentCode_RuleDefinitio~",
                schema: "rules",
                table: "RuleTemplateRules",
                columns: new[] { "RuleTemplateId", "DocumentCode", "RuleDefinitionId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSettingRules",
                schema: "rules");

            migrationBuilder.DropTable(
                name: "RuleTemplateDocuments",
                schema: "rules");

            migrationBuilder.DropTable(
                name: "RuleTemplateRules",
                schema: "rules");

            migrationBuilder.DropTable(
                name: "DocumentSettings",
                schema: "rules");
        }
    }
}
