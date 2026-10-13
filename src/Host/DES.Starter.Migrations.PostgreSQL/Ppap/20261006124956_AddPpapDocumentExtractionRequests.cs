using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapDocumentExtractionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExtractionRequestEventId",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExtractionRequestPublishedAtUtc",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissionDocuments_ExtractionRequestEventId",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                columns: new[] { "ExtractionRequestEventId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PpapSubmissionDocuments_ExtractionRequestEventId",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "ExtractionRequestEventId",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "ExtractionRequestPublishedAtUtc",
                schema: "ppap",
                table: "PpapSubmissionDocuments");
        }
    }
}
