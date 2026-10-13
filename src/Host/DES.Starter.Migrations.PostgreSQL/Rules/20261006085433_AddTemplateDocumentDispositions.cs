using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Rules
{
    /// <inheritdoc />
    public partial class AddTemplateDocumentDispositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Disposition",
                schema: "rules",
                table: "TemplateDocumentRequirements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE rules."TemplateDocumentRequirements"
                SET "Disposition" = CASE WHEN "IsRequired" THEN 'Submit' ELSE 'Retain' END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Disposition",
                schema: "rules",
                table: "TemplateDocumentRequirements");
        }
    }
}
