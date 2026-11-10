using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.Ppap
{
    /// <inheritdoc />
    public partial class AddPpapDocumentRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PpapDocumentRevisions",
                schema: "ppap",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmissionDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceFileAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    UploadStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProcessingStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExtractionRequestEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtractionRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtractionAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanonicalDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawResponseFileAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    GeneratedWorkbookFileAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviewPdfFileAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExtractorVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PpapDocumentRevisions", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO ppap."PpapDocumentRevisions" (
                    "Id", "TenantId", "SubmissionDocumentId", "RevisionNumber", "Version", "OwnerUserId",
                    "SourceFileAssetId", "SourceFileName", "SourceContentType", "SourceSizeBytes", "IsCurrent",
                    "UploadStatus", "ProcessingStatus", "ExtractionRequestEventId", "ExtractionRequestedAtUtc",
                    "ExtractionAttemptId", "CanonicalDocumentId", "RawResponseFileAssetId", "GeneratedWorkbookFileAssetId",
                    "PreviewPdfFileAssetId", "Provider", "ExtractorVersion", "FailureCode", "CreatedAtUtc", "UpdatedAtUtc", "CompletedAtUtc")
                SELECT
                    document."Id", document."TenantId", document."Id", 1, 0, submission."OwnerUserId",
                    document."FileAssetId", file."OriginalFileName", file."ContentType", file."SizeBytes", TRUE,
                    CASE WHEN document."Status" = 'Quarantined' THEN 'Quarantined' ELSE 'Available' END,
                    CASE
                        WHEN document."ExtractionTerminalStatus" = 'Succeeded' THEN 'Ready'
                        WHEN document."ExtractionTerminalStatus" IN ('InvalidDocument', 'Failed') THEN 'Failed'
                        WHEN document."ExtractionRequestPublishedAtUtc" IS NOT NULL THEN 'Queued'
                        ELSE 'NotQueued'
                    END,
                    document."ExtractionRequestEventId", document."ExtractionRequestPublishedAtUtc",
                    document."ExtractionAttemptId", document."CanonicalDocumentId", document."RawResponseFileAssetId",
                    document."GeneratedExcelFileAssetId", NULL, NULL, NULL, document."ExtractionFailureCode",
                    document."CreatedAtUtc", document."UpdatedAtUtc",
                    COALESCE(document."ExtractionCompletedAtUtc", document."ExtractionFailedAtUtc")
                FROM ppap."PpapSubmissionDocuments" document
                JOIN ppap."PpapSubmissions" submission ON submission."Id" = document."SubmissionId"
                JOIN files."FileAssets" file ON file."Id" = document."FileAssetId"
                WHERE document."FileAssetId" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PpapDocumentRevisions_ExtractionAttemptId",
                schema: "ppap",
                table: "PpapDocumentRevisions",
                columns: new[] { "ExtractionAttemptId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapDocumentRevisions_ExtractionRequestEventId",
                schema: "ppap",
                table: "PpapDocumentRevisions",
                columns: new[] { "ExtractionRequestEventId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapDocumentRevisions_ProcessingStatus",
                schema: "ppap",
                table: "PpapDocumentRevisions",
                column: "ProcessingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_PpapDocumentRevisions_SourceFileAssetId",
                schema: "ppap",
                table: "PpapDocumentRevisions",
                columns: new[] { "SourceFileAssetId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PpapDocumentRevisions_TenantId_SubmissionDocumentId",
                schema: "ppap",
                table: "PpapDocumentRevisions",
                columns: new[] { "TenantId", "SubmissionDocumentId" },
                unique: true,
                filter: "\"IsCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PpapDocumentRevisions_TenantId_SubmissionDocumentId_Revisio~",
                schema: "ppap",
                table: "PpapDocumentRevisions",
                columns: new[] { "TenantId", "SubmissionDocumentId", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PpapDocumentRevisions",
                schema: "ppap");
        }
    }
}
