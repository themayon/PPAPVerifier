using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapExtractionTerminalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CanonicalDocumentId",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExtractionAttemptId",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExtractionCompletedAtUtc",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExtractionFailedAtUtc",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionFailureCode",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionTerminalStatus",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GeneratedExcelFileAssetId",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RawResponseFileAssetId",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapSubmissionDocuments_ExtractionAttemptId",
                schema: "ppap",
                table: "PpapSubmissionDocuments",
                columns: new[] { "ExtractionAttemptId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PpapSubmissionDocuments_ExtractionAttemptId",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "CanonicalDocumentId",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "ExtractionAttemptId",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "ExtractionCompletedAtUtc",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "ExtractionFailedAtUtc",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "ExtractionFailureCode",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "ExtractionTerminalStatus",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "GeneratedExcelFileAssetId",
                schema: "ppap",
                table: "PpapSubmissionDocuments");

            migrationBuilder.DropColumn(
                name: "RawResponseFileAssetId",
                schema: "ppap",
                table: "PpapSubmissionDocuments");
        }
    }
}
