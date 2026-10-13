using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapTemplateSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RuleTemplateId",
                schema: "ppap",
                table: "PpapSubmissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RuleTemplateVersionId",
                schema: "ppap",
                table: "PpapSubmissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRequired",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissions_RuleTemplateId_RuleTemplateVersionId",
                schema: "ppap",
                table: "PpapSubmissions",
                columns: new[] { "RuleTemplateId", "RuleTemplateVersionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PpapSubmissions_RuleTemplateId_RuleTemplateVersionId",
                schema: "ppap",
                table: "PpapSubmissions");

            migrationBuilder.DropColumn(
                name: "RuleTemplateId",
                schema: "ppap",
                table: "PpapSubmissions");

            migrationBuilder.DropColumn(
                name: "RuleTemplateVersionId",
                schema: "ppap",
                table: "PpapSubmissions");

            migrationBuilder.DropColumn(
                name: "IsRequired",
                schema: "ppap",
                table: "PpapSubmissionDocuments");
        }
    }
}
