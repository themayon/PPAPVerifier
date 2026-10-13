using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DES.Starter.Migrations.PostgreSQL.DocumentIntelligence
{
    /// <inheritdoc />
    public partial class InitialDocumentIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "document_intelligence");

            migrationBuilder.CreateTable(
                name: "CanonicalDocuments",
                schema: "document_intelligence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExtractionAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CanonicalJson = table.Column<string>(type: "jsonb", nullable: false),
                    RawResponseFileAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    GeneratedExcelFileAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionAttempts",
                schema: "document_intelligence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceFileAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExtractorVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ProviderRequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalDocuments_ExtractionAttemptId",
                schema: "document_intelligence",
                table: "CanonicalDocuments",
                columns: new[] { "ExtractionAttemptId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalDocuments_TenantId_OwnerUserId_CreatedAtUtc",
                schema: "document_intelligence",
                table: "CanonicalDocuments",
                columns: new[] { "TenantId", "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_TenantId_IdempotencyKey",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_TenantId_OwnerUserId_CreatedAtUtc",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                columns: new[] { "TenantId", "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_TenantId_OwnerUserId_Status_UpdatedAtUtc",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                columns: new[] { "TenantId", "OwnerUserId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_TenantId_Status_CreatedAtUtc",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                columns: new[] { "TenantId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAttempts_TenantId_SubmissionDocumentId_SourceSha2~",
                schema: "document_intelligence",
                table: "ExtractionAttempts",
                columns: new[] { "TenantId", "SubmissionDocumentId", "SourceSha256", "Provider", "Model" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CanonicalDocuments",
                schema: "document_intelligence");

            migrationBuilder.DropTable(
                name: "ExtractionAttempts",
                schema: "document_intelligence");
        }
    }
}
