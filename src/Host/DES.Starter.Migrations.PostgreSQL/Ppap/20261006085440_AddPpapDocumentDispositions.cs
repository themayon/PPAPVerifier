using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapDocumentDispositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Disposition",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE ppap."PpapSubmissionDocuments" AS document
                SET "Disposition" = CASE
                    WHEN submission."RuleTemplateId" IS NULL THEN 'Submit'
                    WHEN document."IsRequired" THEN 'Submit'
                    ELSE 'Retain'
                END
                FROM ppap."PpapSubmissions" AS submission
                WHERE document."SubmissionId" = submission."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Disposition",
                schema: "ppap",
                table: "PpapSubmissionDocuments");
        }
    }
}
