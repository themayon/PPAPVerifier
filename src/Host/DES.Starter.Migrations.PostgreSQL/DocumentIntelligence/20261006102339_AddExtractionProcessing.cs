using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.DocumentIntelligence
{
    /// <inheritdoc />
    public partial class AddExtractionProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExtractionAttempts_TenantId_IdempotencyKey",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.DropIndex(
                name: "IX_ExtractionAttempts_TenantId_SubmissionDocumentId_SourceSha2~",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.AddColumn<string>(
                name: "ClaimedBy",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedUntilUtc",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletionEventId",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletionEventPublishedAtUtc",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FailureEventId",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailureEventPublishedAtUtc",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_TenantId_IdempotencyKey",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "\"Status\" <> 'DeadLettered'");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_TenantId_SubmissionDocumentId_SourceSha2~",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                columns: new[] { "TenantId", "SubmissionDocumentId", "SourceSha256", "Provider", "Model", "ExtractorVersion" },
                unique: true,
                filter: "\"Status\" <> 'DeadLettered'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExtractionAttempts_TenantId_IdempotencyKey",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.DropIndex(
                name: "IX_ExtractionAttempts_TenantId_SubmissionDocumentId_SourceSha2~",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.DropColumn(
                name: "ClaimedUntilUtc",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.DropColumn(
                name: "CompletionEventId",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.DropColumn(
                name: "CompletionEventPublishedAtUtc",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.DropColumn(
                name: "FailureEventId",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.DropColumn(
                name: "FailureEventPublishedAtUtc",
                schema: "document_intelligence",
                table: "ExtractionAttempts");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_TenantId_IdempotencyKey",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_TenantId_SubmissionDocumentId_SourceSha2~",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                columns: new[] { "TenantId", "SubmissionDocumentId", "SourceSha256", "Provider", "Model" },
                unique: true);
        }
    }
}
